using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// Droptimizer's answer: the unowned items that would gain, each swapped alone into current gear, grouped under
/// where they come from with the largest gain first; then how many were no gain or over the death limit, the items
/// not yet wearable with what each is missing, and the counts. A row adds its swap to side B, or takes it back out,
/// so several can be tried together; a row on side B is marked.
/// </summary>
internal sealed class DroptimizerList
{
    private readonly Action _again;
    private readonly Action<string> _open;
    private Answer? _shown;
    private (Answer, string, int, string)? _stamp;

    /// <summary>
    /// The list; <paramref name="again"/> goes to the start form; <paramref name="open"/> gets an item to add to side
    /// B or take out of it.
    /// </summary>
    public DroptimizerList(Action again, Action<string> open)
    {
        _again = again;
        _open = open;
        Root = Ui.Column(12, "DroptimizerList");
    }

    public VBoxContainer Root { get; }

    /// <summary>
    /// Shows <paramref name="answer"/>; <paramref name="changed"/> says why it is out of date; rows open only when
    /// it is current.
    /// </summary>
    public void Show(Answer answer, IReadOnlyList<Note> changed)
    {
        // Side B is in the stamp: a row's mark follows it.
        var stamp = (answer, string.Join(",", changed.Select(Tr.Note)), Tr.Version, OnB().ShortForm);
        if (stamp == _stamp)
            return;
        _stamp = stamp;
        _shown = answer;
        Ui.Clear(Root);
        if (changed.Count > 0)
            Root.AddChild(
                Banner.Make(
                    "DroptimizerOutOfDate",
                    Look.Risky,
                    null,
                    Tr.Get("Out of date"),
                    Tr.Notes(changed, ", "),
                    // Translators: Button on the out-of-date notice: simulate the search again with the setup as it is now.
                    SearchWords.Button(Tr.Get("Sim again"), "DroptimizerAgainOutOfDate", _again)
                )
            );
        var body = Ui.Add(Root, Ui.Column(12));
        body.Modulate = Colors.White with { A = changed.Count > 0 ? 0.5f : 1f };
        var opens = changed.Count == 0;

        var head = Ui.Add(body, Ui.Row(8));
        var titles = Ui.Add(head, Ui.Column(2));
        titles.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titles.AddChild(
            Ui.Label(
                answer.State == AnswerState.NothingToSearch ? Tr.Get("Nothing to try: no unowned item can be worn now.")
                    : answer.Gains.Count == 0
                        // Translators: Droptimizer's heading when no item the character doesn't own would gain.
                        ? Tr.Get("No upgrades found")
                    // Translators: Droptimizer's heading: {0} items the character doesn't own that would gain if worn.
                    : Tr.Plural("{0} upgrade found", "{0} upgrades found", answer.Gains.Count, answer.Gains.Count),
                TextStyle.Heading,
                "DroptimizerHeading",
                wrap: true
            )
        );
        titles.AddChild(
            Ui.Label(
                // Translators: Under Droptimizer's heading: what it tries, so a weak item worn on purpose isn't a surprise.
                Tr.Get(
                    "Each item you don't own, tried alone in place of what you wear now. Your other gear isn't changed."
                ),
                TextStyle.Small,
                "DroptimizerWhat",
                wrap: true
            )
        );
        titles.AddChild(Ui.Label(SearchWords.Goal(answer.Key.Goal), TextStyle.Small, wrap: true));
        if (SearchWords.CurrentStuck(answer) is { } stuck)
            titles.AddChild(Ui.Label(stuck, TextStyle.Small, "DroptimizerCurrentStuck", wrap: true));
        if (changed.Count == 0)
        {
            var again = Ui.Button(Tr.Get("Search again"), "", "DroptimizerAgain", _again);
            again.Flat = true;
            again.AddThemeColorOverride("font_color", Look.Best);
            again.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            head.AddChild(again);
        }

        // The answer lists gains best first, in the metric's direction: a source's group sits where its best gain does.
        foreach (var group in answer.Gains.GroupBy(g => Tr.Note(g.Source)))
        {
            var column = Ui.Add(body, Ui.Column(4, Ui.Id("DroptimizerSource", group.Key)));
            column.AddChild(Ui.Label(group.Key, TextStyle.Label));
            foreach (var gain in group)
                column.AddChild(Row(gain, opens));
        }
        if (answer.Safer.Count > 0)
        {
            var column = Ui.Add(body, Ui.Column(4, "DroptimizerSafer"));
            // Translators: Droptimizer's group of items as good on the goal that take clearly less damage.
            column.AddChild(Ui.Label(Tr.Get("Safer, same result"), TextStyle.Label));
            foreach (var safer in answer.Safer)
                column.AddChild(Row(safer, opens));
        }

        body.AddChild(
            Ui.Label(
                // Translators: Under Droptimizer's gains: {0} items that gained nothing, {1} that died more than the limit allows.
                Tr.Format("No gain: {0} · over the death limit: {1}", [answer.NoGain, answer.OverLimit]),
                TextStyle.Small,
                "DroptimizerNoGain",
                wrap: true
            )
        );
        if (answer.NotYetWearable.Count > 0)
        {
            var fold = new Disclosure(
                "DroptimizerNotYet",
                // Translators: Folded list of unowned items the character can't wear yet.
                Tr.Get("Not yet wearable"),
                Ui.Number(answer.NotYetWearable.Count),
                false,
                _ => { }
            );
            body.AddChild(fold.Root);
            foreach (var item in answer.NotYetWearable)
            {
                var row = Ui.Add(fold.Body, Ui.Row(6));
                row.AddChild(Slot.Make(ChangeOptions.Icon(item.Item), 28));
                var words = Ui.Add(row, Ui.Column(0));
                words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                words.AddChild(Ui.Clipped(item.Name, TextStyle.Body));
                words.AddChild(
                    Ui.Label(
                        string.Join(" · ", item.Missing.Select(Tr.Note).Prepend(Tr.Note(item.Source))),
                        TextStyle.Tiny,
                        wrap: true
                    )
                );
            }
        }
        body.AddChild(
            Ui.Label(
                // Translators: Droptimizer's counts: {0} unowned items read, {1} wearable now, {2} fought.
                Tr.Format(
                    "{0} not owned · {1} wearable now · {2} simulated",
                    [
                        Ui.Number(answer.Unowned),
                        Ui.Number(answer.Wearable),
                        Ui.Number(Math.Max(0, answer.Simulated - 1)),
                    ]
                ),
                TextStyle.Small,
                "DroptimizerCounts",
                wrap: true
            )
        );
    }

    /// <summary>The list as shown, for <c>cs2 ui</c>.</summary>
    public string Describe() =>
        _shown is { } a ? $"gains={a.Gains.Count} no-gain={a.NoGain} not-yet={a.NotYetWearable.Count}" : "none";

    /// <summary>
    /// What side B changes over the side searched from, when it is that side's loadout with changes (always, searched
    /// from side B): the swaps added from this list show among them. Empty otherwise.
    /// </summary>
    public static ChangeList OnB() =>
        PanelState.B is { } b && b.Base == SearchSide.Side.Base ? b.Changes : ChangeList.Empty;

    // Whether the swap is on side B: its item is equipped there in its slot.
    private static bool Added(DropGain gain) =>
        OnB().Find(new ChangeKey(ChangeKind.Equip, gain.Slot))?.Value == gain.Item;

    // An icon, the item, its slot, what it replaces and its stance; the gain and its range on the right.
    private Button Row(DropGain gain, bool opens)
    {
        var added = Added(gain);
        var row = new Button
        {
            Name = Ui.Id("DroptimizerRow", Ids.Brief(gain.Item)),
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            Disabled = !opens,
            TooltipText =
                !opens ? ""
                : added
                    // Translators: Hover of a Droptimizer row whose item is on side B.
                    ? Tr.Get("On setup B. Click to take it out.")
                // Translators: Hover of a Droptimizer row: clicking adds the item to side B, beside the others added.
                : Tr.Get("Click to add to setup B."),
            CustomMinimumSize = new Vector2(0, 40),
        };
        row.Pressed += () => _open(gain.Item);
        var line = Ui.Add(row, Ui.Row(6));
        line.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        line.MouseFilter = Control.MouseFilterEnum.Ignore;
        line.AddChild(Slot.Make(ChangeOptions.Icon(gain.Item), 32));
        var words = Ui.Add(line, Ui.Column(0));
        words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        words.MouseFilter = Control.MouseFilterEnum.Ignore;
        // The name and the gain on the first line, the slot and the range under them: the name has the row's
        // width but for the gain's few characters.
        var top = Ui.Add(words, Ui.Row(8));
        if (added)
            top.AddChild(Ui.Label("B", TextStyle.Gold, "DroptimizerOnB"));
        var name = Ui.Add(top, Ui.Clipped(gain.Name, TextStyle.Body));
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // A gain shows by how much; a safer item by how much less damage it takes, and the damage before and after.
        var (shown, colour, range) = gain is { Taken: { } taken, TakenBefore: { } before }
            ? (
                // Translators: Droptimizer's safer item: {0} how much less damage a second it takes, "-18%".
                Tr.Format("{0}% damage", ((taken.Point / before.Point - 1) * 100).ToString("+0;-0", Tr.Culture)),
                Look.Safe,
                // Translators: Droptimizer's safer item: the damage a second taken now, then with the item.
                Tr.Format("{0} → {1} damage/s", [Ui.Number(before.Point), Ui.Number(taken.Point)])
            )
            : (
                SearchWords.Headline(gain.Percent, gain.Gain, gain.Result, gain.Metric),
                SearchWords.GainColour(gain.Percent, gain.Gain),
                SearchWords.Range(gain.Percent, gain.Gain)
            );
        var value = Ui.Add(top, Ui.Label(shown, TextStyle.BodyStrong));
        if (colour is { } tint)
            value.LabelSettings = Compare.CompareText.Coloured(tint, TextStyle.BodyStrong);
        var detail = new List<string> { PanelState.Names()(gain.Slot) };
        if (gain.Displaced.Count > 0)
            detail.Add(Tr.Format("replaces {0}", string.Join(", ", gain.Displaced)));
        if (gain.Changes.FirstOrDefault(c => c.Kind == ChangeKind.Stance) is { } stance)
            detail.Add(Tr.Format("Stance: {0}", PanelState.Names()(stance.Value)));
        var under = Ui.Add(words, Ui.Row(8));
        var slot = Ui.Add(under, Ui.Clipped(string.Join(" · ", detail), TextStyle.Tiny));
        slot.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        under.AddChild(Ui.Label(range, TextStyle.Tiny));
        foreach (var label in line.FindChildren("*", "Label", true, false).OfType<Label>())
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return row;
    }
}
