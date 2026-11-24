using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Results;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Compare;

/// <summary>
/// One target of a comparison: its name and the verdict for the ranked metric, each side's card with what it changed,
/// then the single run's tabs with every metric as a table row of A, B and B against A: Rates (then the drops, the
/// kill rate's change said once), Fights (risk, the fight numbers with their bars, and a dungeon's fight by fight) and
/// Gear, the gear search as in a single run's details, from the side chosen beside its gear.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class CompareDetails
{
    private sealed class MetricRow
    {
        public required VBoxContainer Root { get; init; }
        public required Label Name { get; init; }
        public required Label A { get; init; }
        public required Label B { get; init; }
        public required Label Difference { get; init; }
        public required Control Bar { get; init; }
    }

    private readonly PanelContainer _root;
    private readonly Search.GearTab _gear;
    private readonly CenterContainer _empty;
    private readonly VBoxContainer _body;
    private readonly PanelContainer _portrait;
    private readonly Label _name;
    private readonly Label _sub;
    private readonly Label _verdict;
    private readonly CompareSides _sides = new();
    private readonly CompareDrops _drops = new();
    private readonly DetailsTabs _tabs;
    private readonly Dictionary<string, (VBoxContainer Column, List<MetricRow> Rows)> _rows = [];
    private readonly VBoxContainer _risk;
    private readonly VBoxContainer _depth;
    private readonly ScrollContainer _scroll;

    /// <summary>The panel; <paramref name="pickItem"/> serves its Gear tab.</summary>
    public CompareDetails(Control hoverOwner, Action<Control, Action<string>> pickItem)
    {
        _root = Look.Panel(Art.SideBox, "Cs2CompareDetails");
        _root.CustomMinimumSize = new Vector2(Details.Width, 0);
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var holder = Ui.Add(_root, new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var empty = (HFlowContainer)
            SideLine.Make(
                Tr.Format("Select a target to see {0} and {1} side by side.", SideLine.A, SideLine.B),
                TextStyle.Body,
                wrap: true
            );
        empty.Alignment = FlowContainer.AlignmentMode.Center;
        empty.CustomMinimumSize = new Vector2(Details.Width - 48, 0);
        _empty = new CenterContainer();
        _empty.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _empty.AddChild(empty);
        holder.AddChild(_empty);
        // As a single run's details: the header and tabs stay put, only the tab's page scrolls.
        var gutter = Ui.Add(holder, new MarginContainer { Name = "Cs2CompareDetailGutter" });
        gutter.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _body = Ui.Add(gutter, Ui.Column(10, "Cs2CompareDetailBody"));
        _body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var head = Ui.Add(_body, Ui.Row(12));
        _portrait = Slot.Make(null, 96);
        head.AddChild(_portrait);
        var names = Ui.Add(head, Ui.Column(2));
        names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        names.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _name = Ui.Label("", TextStyle.Title, wrap: true);
        names.AddChild(_name);
        _sub = Ui.Label("", TextStyle.Small, wrap: true);
        names.AddChild(_sub);
        _verdict = Ui.Label("", TextStyle.BodyStrong, wrap: true);
        names.AddChild(_verdict);
        _body.AddChild(_sides.Root);
        _tabs = new DetailsTabs(_body, gear: true);
        _scroll = _tabs.Scroll;
        var rates = _tabs.Page(DetailsTabs.Rates);
        var rateTable = Ui.Add(rates, Ui.Column(0, "Cs2CompareMetrics"));
        rateTable.AddChild(CompareCells.Heads(Tr.Get("Rates")));
        _rows[DetailsTabs.Rates] = (Ui.Add(rateTable, Ui.Column(0)), []);
        rates.AddChild(_drops.Root);
        var fights = _tabs.Page(DetailsTabs.Fights);
        _risk = Ui.Add(fights, Ui.Column(0, "Cs2CompareRisk"));
        var fightTable = Ui.Add(fights, Ui.Column(0, "Cs2CompareFightRows"));
        fightTable.AddChild(CompareCells.Heads(Tr.Get("Fights")));
        _rows[DetailsTabs.Fights] = (Ui.Add(fightTable, Ui.Column(8)), []);
        _depth = Ui.Add(fights, Ui.Column(10, "Cs2CompareDepth"));
        _gear = new Search.GearTab(_tabs.Page(DetailsTabs.Gear), _tabs.Foot, hoverOwner, pickItem);
        ShowNothing();
    }

    public Control Root => _root;

    public string? Key { get; private set; }

    public void ShowNothing()
    {
        Key = null;
        _empty.Visible = true;
        _body.GetParent<Control>().Visible = false;
    }

    public void Show(ComparedRuns comparison, string key, string metric, TimeUnit unit)
    {
        Key = key;
        _empty.Visible = false;
        _body.GetParent<Control>().Visible = true;
        var a = comparison.A;
        var b = comparison.B;
        var game = LiveGame.Find();
        var prices = PanelState.Prices();
        var info = PanelState.Catalog?.Find(key);
        var result = b.Results.GetValueOrDefault(key) ?? a.Results.GetValueOrDefault(key);
        Slot.Set(_portrait, game != null ? Art.Target(game, TargetSpec.FromKey(key)) : Art.Missing);
        var (name, area) = RankList.Names(
            new RankRow(key, result?.Name ?? key, null, null, 0, TargetState.Finished, null),
            info
        );
        _name.Text = name;
        _sub.Text = area;
        var saved = comparison.Record.Kind == ComparisonKind.NowAgainstSaved;
        Func<string, Texture2D?>? art = game != null ? k => Art.Metric(game, k) : null;
        _sides.Show(a, b, saved, art);
        _gear.Show(key);

        // Every metric either side has, "XP · main skill" first.
        var results = new[] { a.Results.GetValueOrDefault(key), b.Results.GetValueOrDefault(key) }
            .Where(r => r != null)
            .Select(r => r!)
            .ToList();
        var metrics = new List<Metric> { new(Metrics.MainXp, Note.Of("XP · main skill"), true) };
        metrics.AddRange(Metrics.For(results, prices, PanelState.Names()).Where(m => !FightNumbers.IsFightKey(m.Key)));
        // Fight details, after the other metrics, when either side has counts or a stalled trial.
        if (results.Any(r => r.HasFightCounts || r.Stall != null))
            metrics.AddRange(FightNumbers.Keys.Where(k => k != FightNumbers.RanOver).Select(k => Metrics.Describe(k)));
        var differences = Differences.ForTarget(a, b, key, metrics.Select(m => m.Key), unit, prices);

        var ranked = differences.Zip(metrics).FirstOrDefault(p => p.Second.Key == metric).First;
        if (ranked?.Reason is { } died)
        {
            _verdict.Text = Tr.Note(died);
            _verdict.LabelSettings = Look.Style(TextStyle.BodyStrong);
        }
        else if (ranked?.OnlyKills is { } only)
        {
            // Translators: The comparison's headline: only side {0} (A or B) can kill the target in time.
            _verdict.Text = Tr.Format("Only {0} kills it", only);
            _verdict.LabelSettings = CompareText.Coloured(
                DiffBar.Colour(ranked.Verdict, _verdict),
                TextStyle.BodyStrong
            );
        }
        else if (ranked?.Delta != null && !ranked.SkillsDiffer)
        {
            var label = Words.MetricName(metrics.First(m => m.Key == metric), unit);
            _verdict.Text = ranked.Verdict switch
            {
                Verdict.Better => Tr.Format(
                    "B is better · {0} {1}",
                    [CompareText.SignedPercent(ranked.Percent?.Point ?? double.NaN), label]
                ),
                Verdict.Worse => Tr.Format(
                    "B is worse · {0} {1}",
                    [CompareText.SignedPercent(ranked.Percent?.Point ?? double.NaN), label]
                ),
                _ => Tr.Format("Unclear in {0}", label),
            };
            _verdict.LabelSettings = CompareText.Coloured(
                DiffBar.Colour(ranked.Verdict, _verdict),
                TextStyle.BodyStrong
            );
        }
        else
        {
            _verdict.Text = "";
        }

        // Bars in percent on one scale, so metrics of every size compare at a glance.
        var percentScale = Math.Max(
            0.1,
            DiffBar.ScaleOf(differences.Where(d => !d.SkillsDiffer).Select(d => d.Percent))
        );
        // Each metric on its tab, as a single run's details place them; a tab's spare rows hide.
        var placed = metrics
            .Zip(differences)
            .GroupBy(p => TabOf(p.First.Key))
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var (tab, (column, rows)) in _rows)
        {
            var pairs = placed.GetValueOrDefault(tab) ?? [];
            while (rows.Count < pairs.Count)
                rows.Add(MakeRow(column));
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].Root.Visible = i < pairs.Count;
                if (i < pairs.Count)
                    FillRow(
                        rows[i],
                        pairs[i].First,
                        pairs[i].Second,
                        a,
                        b,
                        key,
                        unit,
                        tab == DetailsTabs.Fights ? percentScale : null
                    );
            }
        }
        _drops.Show(
            a,
            b,
            key,
            unit,
            placed.GetValueOrDefault(LootRows) ?? [],
            differences.Zip(metrics).FirstOrDefault(p => p.Second.Key == Metrics.Kills).First,
            art
        );
        var resultA = a.Results.GetValueOrDefault(key);
        var resultB = b.Results.GetValueOrDefault(key);
        ShowDepth(resultA, resultB);
        // The Fights tab's mark for whichever side warns, as a single run's details carry it.
        _tabs.Mark(
            resultB is { } rb && Risk.Of(rb) is { Warning: true } riskB ? riskB
                : resultA is { } ra ? Risk.Of(ra)
                : null,
            resultA?.NoneSurvived == true || resultB?.NoneSurvived == true
        );
    }

    // The drops, on Rates under its rates, laid out by CompareDrops rather than as plain rows.
    private const string LootRows = "loot";

    // Fight numbers on Fights, drops under the rates, the rest on Rates.
    private static string TabOf(string key) =>
        key.Split(':')[0] switch
        {
            "fight" => DetailsTabs.Fights,
            "item" => LootRows,
            _ => DetailsTabs.Rates,
        };

    // A row of the table; the bar only where a percent scale is given (Fights), as the change says it elsewhere.
    private static void FillRow(
        MetricRow row,
        Metric m,
        Difference d,
        Run a,
        Run b,
        string key,
        TimeUnit unit,
        double? percentScale
    )
    {
        row.Name.Text = m.Key == Metrics.MainXp ? Words.Metric(m) : Words.MetricName(m, unit);
        row.A.Text = CompareCells.Side(a.Results.GetValueOrDefault(key), d.A, m.Key);
        row.B.Text = CompareCells.Side(b.Results.GetValueOrDefault(key), d.B, m.Key);
        CompareCells.SetDifference(row.Difference, d);
        row.Bar.Visible = percentScale != null;
        if (percentScale is { } scale)
            DiffBar.Set(row.Bar, d.SkillsDiffer ? null : d.Percent, scale, d.Verdict);
    }

    // Risk for A and B at the top of Fights, and each side's fights for a dungeon after the fight numbers.
    private void ShowDepth(TargetResult? a, TargetResult? b)
    {
        Ui.Clear(_risk);
        _risk.Visible = a?.HasEstimates == true || b?.HasEstimates == true;
        if (_risk.Visible)
            _risk.AddChild(RiskBlock.Compare(a, b));
        Ui.Clear(_depth);
        var names = PanelState.Names();
        foreach (var (side, result) in new[] { ("A", a), ("B", b) })
        {
            if (result is { HasEstimates: true, Kind: TargetKind.Dungeon } && result.Fights.Count > 0)
                _depth.AddChild(FightsSection.Make(result, names, side).Root);
        }
        _depth.Visible = _depth.GetChildCount() > 0;
    }

    private static MetricRow MakeRow(VBoxContainer column)
    {
        var (root, content) = CompareCells.Row();
        column.AddChild(root);
        var line = Ui.Add(content, Ui.Row(8));
        var name = Ui.Add(line, Ui.Label("", TextStyle.Small, wrap: true));
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var a = Ui.Add(line, CompareCells.Value());
        var b = Ui.Add(line, CompareCells.Value());
        var difference = Ui.Add(line, CompareCells.Change());
        var bar = DiffBar.Make("", 8);
        content.AddChild(bar);
        return new MetricRow
        {
            Root = root,
            Name = name,
            A = a,
            B = b,
            Difference = difference,
            Bar = bar,
        };
    }
}
