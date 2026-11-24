using System.Text;
using System.Text.RegularExpressions;
using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// The details' Gear tab: one sentence, "Find best setup for this target", whose gold words pick the kind of search and
/// what it covers, and the info button at its right opens the guide; under it the start form, the search under way, or
/// its answer. A target with a kept answer shows the answer on returning to it, out of date
/// when its setup has changed since; "Search again" goes back to the form.
/// </summary>
internal sealed class GearTab
{
    // Kept while the game runs: a player who looks at Droptimizer keeps looking at it from target to target.
    private static SearchKind _kind = SearchKind.BestGear;

    // What the search covers: the selected target, its area's enemies, or the pins; kept like the kind.
    private static string _scope = One;
    private const string One = "one";
    private const string AreaScope = "area";
    private const string Pinned = "pinned";

    private readonly Dropdown _kindPick;
    private readonly Dropdown _scopePick;
    private readonly PoolPick _pool;

    // The scopes in the scope's list, in its order.
    private readonly List<string> _scopeIds = [];
    private string? _selected;
    private readonly Label _message;
    private readonly StartForm _form;
    private readonly Progress _progress;
    private readonly AnswerView _answer;
    private readonly DroptimizerList _drops;
    private string? _target;
    private readonly Control _page;

    // The targets and kinds whose form was asked for again over their answer: kept across a change of target, so
    // going to an area or the pins and back doesn't bring the out-of-date answer back over the form.
    private readonly HashSet<(string Target, SearchKind Kind)> _again = [];
    private string _view = "none";

    // Per kind, the kept answer looked up for the target against the side searched from as last checked; each kind keeps its own, so
    // switching between them looks nothing up again.
    private readonly Dictionary<SearchKind, Lookup> _lookups = [];

    private sealed class Lookup(object check)
    {
        public object Check { get; } = check;
        public bool Done { get; set; }
        public (Answer Answer, IReadOnlyList<Note> Changed)? Kept { get; set; }
    }

    // The answer shown, which Open as setup opens a set of.
    private Answer? _shownAnswer;

    // Side B as it was before the shown answer's first swap, when the search started from it: a swap taken off again
    // puts back what B had there, as side A's does from A.
    private (Answer Answer, SideState Side)? _swapsFrom;

    // The target and kind the view shown was drawn for.
    private (string Target, SearchKind Kind)? _shownFor;

    /// <summary>
    /// The tab's content on <paramref name="page"/>; <paramref name="pickItem"/> opens the item picker under a
    /// control.
    /// </summary>
    public GearTab(VBoxContainer page, VBoxContainer foot, Control hoverOwner, Action<Control, Action<string>> pickItem)
    {
        _page = page;
        page.VisibilityChanged += () =>
        {
            _form!.Foot.Visible = page.Visible && _form.Root.Visible;
            if (page.Visible)
                Refresh();
        };
        _kindPick = new Dropdown("GearSearchKindPick", Dropdown.Link) { RightAligned = true };
        // Translators: Gear tab sentence "Find [best setup] for [this target]": the best setup of what the character has.
        _kindPick.AddItem(Tr.Get("best setup"));
        // Translators: Gear tab sentence "Find [upgrades] for [this target]": which unowned items would gain the most.
        _kindPick.AddItem(Tr.Get("upgrades"));
        _kindPick.ItemSelected += i =>
        {
            _kind = i == 1 ? SearchKind.Droptimizer : SearchKind.BestGear;
            Refresh();
        };
        _scopePick = new Dropdown("GearSearchScopePick", Dropdown.Link) { RightAligned = true };
        _scopePick.ItemSelected += i =>
        {
            _scope = _scopeIds[i];
            if (_selected is { } selected)
                Show(selected);
        };
        var head = Ui.Add(page, Ui.Row(6, "GearSearchHead"));
        // The sentence wraps before the info button when the pool's words make it longer than the tab.
        var sentence = Ui.Add(head, new HFlowContainer { Name = "GearSearchSentence" });
        sentence.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sentence.AddThemeConstantOverride("h_separation", 6);
        // The sentence is translated whole, so a language may put its two choices in either order.
        // Translators: Gear tab sentence; {0} is "best setup" or "upgrades", {1} is "this target", "its area" or "the 3 pins", each a gold choice.
        foreach (var part in Regex.Split(Tr.Get("Find {0} for {1}"), @"(\{[01]\})"))
        {
            if (part == "{0}")
                sentence.AddChild(_kindPick.Root);
            else if (part == "{1}")
                sentence.AddChild(_scopePick.Root);
            else if (part.Trim().Length > 0)
            {
                var words = Ui.Add(sentence, Ui.Label(part.Trim(), TextStyle.Body));
                words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            }
        }
        // A new pool has its own kept answer, and the form counts it again.
        _pool = new PoolPick(() =>
        {
            _lookups.Clear();
            _form!.PoolChanged();
            Refresh();
        });
        sentence.AddChild(_pool.Root);
        Button? guide = null;
        guide = IconButton.Quiet(
            Game.Art.File(Game.Art.InfoPath) ?? Game.Art.Missing,
            // Translators: Hover of the Gear tab's info button, which opens its guide.
            Tr.Get("How gear search works"),
            "GearSearchGuide",
            () => Help.HelpLayer.OpenGearGuide(guide)
        );
        guide.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        head.AddChild(guide);
        guide.Visible = PanelState.Prefs.ShowGearGuide;
        // Settings can show it again while the tab is out of sight; it follows when the tab shows.
        head.VisibilityChanged += () =>
        {
            if (head.IsVisibleInTree())
                guide.Visible = PanelState.Prefs.ShowGearGuide;
        };
        _message = Ui.Add(page, Ui.Label("", TextStyle.Small, "GearSearchMessage", wrap: true));
        _message.LabelSettings = Compare.CompareText.Coloured(Look.Risky, TextStyle.Small);
        _message.Visible = false;
        _form = new StartForm(pickItem, Start);
        _progress = new Progress(hoverOwner);
        _answer = new AnswerView(Again, Open, LeaveOut);
        _drops = new DroptimizerList(Again, Toggle);
        foreach (var view in new Control[] { _form.Root, _progress.Root, _answer.Root, _drops.Root })
        {
            view.Visible = false;
            page.AddChild(view);
        }
        _form.Foot.Visible = false;
        foot.AddChild(_form.Foot);
    }

    /// <summary>Shows the tab for <paramref name="selected"/>, or its area or the pins as the scope chosen says.</summary>
    public void Show(string selected)
    {
        _selected = selected;
        var target = Scoped(selected);
        if (_target != target)
        {
            _target = target;
            _lookups.Clear();
            _message.Visible = false;
        }
        Refresh();
    }

    // The search's target for the scope chosen: an area (an enemy's) or the pins (two or three) when they apply,
    // else the selected target; the scope's list greys what doesn't apply, saying why.
    private string Scoped(string selected)
    {
        var spec = Game.TargetSpec.FromKey(selected);
        var pins = PanelPins.ShownPins;
        var scope = _scope switch
        {
            AreaScope when spec.Kind == TargetKind.Enemy => AreaScope,
            Pinned when pins.Count >= 2 => Pinned,
            _ => One,
        };
        _scopeIds.Clear();
        _scopePick.Clear();
        foreach (var (id, words) in ScopeWords(pins.Count))
        {
            _scopeIds.Add(id);
            _scopePick.AddItem(
                words,
                off: id switch
                {
                    // Translators: Hover of the greyed "its area" choice when the selected target isn't an enemy.
                    AreaScope when spec.Kind != TargetKind.Enemy => Tr.Get("Select an enemy to search its whole area."),
                    // Translators: Hover of the greyed pins choice when fewer than two targets are pinned.
                    Pinned when pins.Count < 2 => Tr.Get("Pin 2 or 3 targets to search them together."),
                    _ => null,
                }
            );
        }
        _scopePick.Select(_scopeIds.IndexOf(scope));
        return scope switch
        {
            AreaScope => Ids.AreaPrefix + spec.Area,
            Pinned => Ids.PinnedKey(pins),
            _ => selected,
        };
    }

    /// <summary>
    /// Picks the view for the target as things stand: progress, the answer, or the form. Only while the tab is chosen:
    /// looking up the kept answer reads the gear from a check copy, work a target's details shouldn't pay for on
    /// every open; choosing the tab refreshes it.
    /// </summary>
    public void Refresh()
    {
        if (_target is not { } target || !_page.Visible)
            return;
        _kindPick.Select(_kind == SearchKind.Droptimizer ? 1 : 0);
        _pool.Show(_kind, target);
        var search = RunService.Instance.Search;
        var mine = search != null && search.Request.Target == target && search.Request.Kind == _kind;
        if (mine && search!.IsActive)
        {
            // A search started for this target, from here or a command: its answer shows when it ends.
            _again.Remove((target, _kind));
            Choose(_progress.Root, "progress", target);
            _progress.Show();
            return;
        }
        if (!_again.Contains((target, _kind)))
        {
            var lookup = LookUp(target);
            var live =
                mine && search!.Answer is { Finished: true } done && done.Key.SamePool(PoolPick.Key(_kind, target))
                    ? done
                    : null;
            // A search that just ended is kept as the target's answer: it takes the place of the one looked up before
            // it, which would otherwise come back, out of date, once this search is no longer the tab's own.
            if (live != null && lookup != null && lookup.Kept?.Answer.FoundAt != live.FoundAt)
            {
                lookup.Kept = (live, []);
                lookup.Done = true;
            }
            if (lookup?.Kept is { } kept)
            {
                ShowAnswer(kept.Answer, kept.Changed, target);
                return;
            }
            if (live != null)
            {
                ShowAnswer(live, [], target);
                return;
            }
            // Until the kept answer is read, the view stays as it was for this target and kind, or empty: the form
            // would show for a moment and then give way to the answer.
            if (lookup is { Done: false })
            {
                if (_shownFor != (target, _kind))
                    Choose(null, "looking", target);
                return;
            }
        }
        Choose(_form.Root, "form", target);
        _form.Show(target, _kind, mine ? SearchWords.Ended(search!.Answer) : SearchWords.Busy());
    }

    /// <summary>The tab as shown, for <c>cs2 ui</c>.</summary>
    public void Describe(StringBuilder text)
    {
        text.Append("\ngear-tab: ")
            .Append(_view)
            .Append(" kind=")
            .Append(_kind == SearchKind.Droptimizer ? "droptimizer" : "top");
        text.Append(' ')
            .Append(
                _view switch
                {
                    "form" => _form.Describe(),
                    "progress" => Progress.Describe(),
                    "answer" => _answer.Describe(),
                    "drops" => _drops.Describe(),
                    _ => "",
                }
            );
    }

    private void ShowAnswer(Answer answer, IReadOnlyList<Note> changed, string target)
    {
        _shownAnswer = answer;
        if (answer.Kind == SearchKind.Droptimizer)
        {
            Choose(_drops.Root, "drops", target);
            _drops.Show(answer, changed);
        }
        else
        {
            Choose(_answer.Root, "answer", target);
            _answer.Show(answer, changed);
        }
    }

    private void Choose(Control? shown, string view, string target)
    {
        _view = view;
        _shownFor = shown == null ? null : (target, _kind);
        foreach (var root in new Control[] { _form.Root, _progress.Root, _answer.Root, _drops.Root })
            root.Visible = ReferenceEquals(root, shown);
        _form.Foot.Visible = _page.Visible && _form.Root.Visible;
    }

    // Looks up the kept answer once per kind and check of the side searched from, for the tab's target: a change of gear since may put
    // it out of date. The answer arrives on a later frame and the tab is shown again with it. Null before the side has
    // been checked.
    private Lookup? LookUp(string target)
    {
        var check = SearchSide.Check;
        if (_lookups.TryGetValue(_kind, out var known) && ReferenceEquals(known.Check, check))
            return known;
        if (Game.LiveGame.Find() == null || check == null)
            return known;
        var lookup = _lookups[_kind] = new Lookup(check) { Kept = known?.Kept };
        var kind = _kind;
        try
        {
            RunService.FindKept(
                kind,
                SearchSide.Setup(),
                target,
                found =>
                {
                    if (_target != target || !_lookups.TryGetValue(kind, out var now) || !ReferenceEquals(now, lookup))
                        return;
                    // A lookup begun before a search ended reads the answer that search replaced.
                    if (
                        !(lookup.Kept is { } newer && found is { } older && older.Answer.FoundAt < newer.Answer.FoundAt)
                    )
                        lookup.Kept = found;
                    lookup.Done = true;
                    if (GodotObject.IsInstanceValid(_form.Root))
                        Refresh();
                },
                kind == SearchKind.BestGear ? PoolPick.Key(kind, target) : null
            );
        }
        catch (CommandException e)
        {
            lookup.Done = true;
            Log.Warn("the kept answer couldn't be looked up", e);
        }
        return lookup;
    }

    private void Start(SearchRequest request)
    {
        try
        {
            RunService.Instance.StartSearch(request);
            _again.Remove((request.Target, request.Kind));
            _message.Visible = false;
            Refresh();
        }
        catch (CommandException e)
        {
            Say(e.Message);
        }
    }

    // A source left out from an answer's items to get: the form again, over every item without it.
    private void LeaveOut(string source)
    {
        PanelState.Prefs.SearchLeftOut = [.. PanelState.Prefs.SearchLeftOut, source];
        PanelState.Save();
        _lookups.Clear();
        _form.PoolChanged();
        Again();
    }

    private void Again()
    {
        if (_target is { } target)
            _again.Add((target, _kind));
        _message.Visible = false;
        Refresh();
    }

    // A Droptimizer swap goes onto side B beside those added before, or comes back off: the slot then holds what the
    // side searched from has. Searched from side A, side B starts as side A when it is another loadout or a run;
    // searched from side B, the swaps go onto B itself, side A staying as it is. Nothing is fought until a run.
    private void Toggle(string item)
    {
        try
        {
            if (_shownAnswer is not { } answer)
                return;
            var from = SwapsFrom(answer);
            var changes = PanelState.B is { } b && b.Base == from.Base ? b.Changes : from.Changes;
            var swap = RunService.AnswerSetOf(answer, null, item).Changes;
            var on = swap.All(c => c.Kind != ChangeKind.Equip || changes.Find(c.Key)?.Value == c.Value);
            foreach (var change in swap)
                changes =
                    !on ? changes.With(change)
                    : from.Changes.Find(change.Key) is { } before ? changes.With(before)
                    : changes.Without(change.Key);
            PanelState.PickBaseline(false);
            PanelState.ShowComparison(false);
            PanelState.SetSide(SideLetter.B, new SideState(from.Base, changes, from.IgnoreRequirements));
            Focus(_target);
            _message.Visible = false;
        }
        catch (CommandException e)
        {
            Say(e.Message);
        }
    }

    private void Open(int? tie, string? alternative)
    {
        try
        {
            if (_shownAnswer is not { } answer)
                return;
            // Side B becomes the side searched from with the set's changes: side A stays, so searched from side A it
            // compares the set with A, and searched from side B it improves B. What the search fought with a
            // requirement ignored needs it ignored on side B too, or the game refuses it there.
            var from = SearchSide.Side;
            var set = RunService.AnswerSetOf(answer, tie, null, alternative);
            var changes = from.Changes;
            foreach (var change in set.Changes)
                changes = changes.With(change);
            PanelState.PickBaseline(false);
            PanelState.ShowComparison(false);
            PanelState.SetSide(
                SideLetter.B,
                new SideState(from.Base, changes, from.IgnoreRequirements || set.NeedsRequirementsIgnored)
            );
            Focus(_target);
            _message.Visible = false;
        }
        catch (CommandException e)
        {
            Say(e.Message);
        }
    }

    // The side the shown answer's swaps start from: side A as it is, or side B as it was before the first of them.
    private SideState SwapsFrom(Answer answer)
    {
        if (SearchSide.Letter == SideLetter.A)
            return PanelState.A;
        if (_swapsFrom is not { } kept || !ReferenceEquals(kept.Answer, answer))
            _swapsFrom = kept = (answer, SearchSide.Side);
        return kept.Side;
    }

    // The targets narrowed to the one searched, by quoted search text that clearing brings every target back from;
    // brought back too when ignored, so the next run fights it.
    private static void Focus(string? target)
    {
        if (target == null || PanelState.Catalog is not { } catalog)
            return;
        var facts = catalog.List.Select(t => t.Fact(PanelState.A.IgnoreRequirements)).ToList();
        if (facts.Find(f => f.Key == target) is not { } fact)
            return;
        PanelState.SetIgnored([target], false);
        PanelState.SetFilter(
            PanelState.Prefs.Filter with
            {
                Search = TargetSelection.ExactSearch(facts, fact),
                Kinds = TargetTags.All,
                Areas = null,
                CanFightOnly = PanelState.Prefs.Filter.CanFightOnly && fact.CanFight,
            }
        );
        PanelState.Select(target);
    }

    private static IEnumerable<(string Id, string Words)> ScopeWords(int pins) =>
        [
            // Translators: Gear tab sentence "Find [best setup] for [this target]": the selected target alone.
            (One, Tr.Get("this target")),
            // Translators: Gear tab sentence "Find [best setup] for [its area]": every enemy of the target's area.
            (AreaScope, Tr.Get("its area")),
            // Translators: Gear tab sentence "Find [best setup] for [the 3 pins]": the pinned targets together.
            (Pinned, pins >= 2 ? Tr.Format("the {0} pins", pins) : Tr.Get("the pins")),
        ];

    private void Say(string message)
    {
        _message.Text = message;
        _message.Visible = message.Length > 0;
    }
}
