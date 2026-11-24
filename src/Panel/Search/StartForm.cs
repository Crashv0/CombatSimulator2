using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// What a search is asked: the metric to rank by, the death limit as three buttons, whether to try other styles, the
/// gear with its locks and the items never to use; then what it would fight, counted from the plan before anything
/// starts, how long that is expected to take, and the button that starts it.
/// </summary>
internal sealed class StartForm
{
    private readonly Dropdown _rankBy;
    private readonly Segmented _deaths;
    private readonly Button _styles;
    private readonly Button _ignore;
    private readonly StartGear _gear;
    private readonly KindSwitches _switches;
    private readonly PoolNarrowing _narrowing;
    private readonly StartCounts _counts = new();
    private readonly OptionsFold _options;
    private readonly Label _note;
    private readonly Button _find;
    private readonly Action<SearchRequest> _start;
    private List<Metric> _metrics = [];
    private string _metric = Metrics.MainXp;
    private DeathLimit _limit = DeathLimit.None;
    private bool _allStyles;
    private SearchKind _kind = SearchKind.BestGear;
    private string? _target;
    private int _version;
    private PlanPreview? _preview;

    // The counts last shown, kept while they are counted again so that only their numbers fade.
    private PlanPreview? _shown;
    private bool _counting;
    private (string Target, SearchKind Kind, object? Check, bool Choose, string? Note, int Language)? _stamp;

    /// <summary>
    /// The form; <paramref name="pickItem"/> opens the item picker under a control; <paramref name="start"/> gets
    /// the request when the player starts the search.
    /// </summary>
    public StartForm(Action<Control, Action<string>> pickItem, Action<SearchRequest> start)
    {
        _start = start;
        _switches = new KindSwitches(Changed);
        _narrowing = new PoolNarrowing(Changed);
        Root = Ui.Column(12, "GearSearchStart");

        var rank = Ui.Row(8);
        var rankLabel = Ui.Add(rank, Ui.Label(Tr.Get("Rank by"), TextStyle.Label));
        rankLabel.CustomMinimumSize = new Vector2(76, 0);
        rankLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // The bar and its list framed as the tab's buttons are.
        _rankBy = new Dropdown("GearSearchRankBy", "") { RightAligned = true, ListBox = Look.FramedListBox };
        FramedButton.Make(_rankBy.Root, 10, 3);
        _rankBy.FixWidth(200);
        _rankBy.ItemSelected += i =>
        {
            if (i >= 0 && i < _metrics.Count)
            {
                _metric = _metrics[i].Key;
                Changed();
            }
        };
        rank.AddChild(_rankBy.Root);

        var deaths = Ui.Row(8);
        // Translators: Label of the death limit's three buttons in the gear search's form.
        var deathsLabel = Ui.Add(deaths, Ui.Label(Tr.Get("Deaths"), TextStyle.Label));
        deathsLabel.CustomMinimumSize = new Vector2(76, 0);
        deathsLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // Short words on the buttons, to fit the panel; each says the limit in full on hover.
        _deaths = new Segmented(
            "GearSearchDeaths",
            [
                ("none", Tr.Get("None"), "GearSearchDeathsNone"),
                // Translators: Short button of the gear search's death limit: fewer than 5% of trials die.
                ("five", Tr.Get("Under 5%"), "GearSearchDeathsFive"),
                // Translators: Short button of the gear search's death limit: any deaths, if one trial was survived.
                ("any", Tr.Get("Any"), "GearSearchDeathsAny"),
            ],
            id =>
            {
                _limit = id switch
                {
                    "five" => DeathLimit.UnderFivePercent,
                    "any" => DeathLimit.SurvivedOnce,
                    _ => DeathLimit.None,
                };
                Changed();
            },
            side: 6,
            ends: 3,
            framed: true
        );
        _deaths.Button("none").TooltipText = SearchWords.Deaths(DeathLimit.None);
        _deaths.Button("five").TooltipText = SearchWords.Deaths(DeathLimit.UnderFivePercent);
        _deaths.Button("any").TooltipText = SearchWords.Deaths(DeathLimit.SurvivedOnce);
        deaths.AddChild(_deaths.Root);

        _styles = Cs2Check.Make(
            "GearSearchStyles",
            // Translators: Tick in the gear search's form: also try the other combat styles' weapons.
            Tr.Get("Include other styles"),
            false,
            on =>
            {
                _allStyles = on;
                Changed();
            }
        );
        _styles.TooltipText = Tr.Get(
            "Also search with a weapon of each other combat style, and show each style's best."
        );
        // The searched side's own setting, here too: what the search may wear and try hangs on it. The side's new check
        // counts the search again.
        _ignore = Cs2Check.Make(
            "GearSearchIgnoreRequirements",
            Tr.Get("Ignore requirements"),
            false,
            on =>
            {
                if (SearchSide.Side is { CanChange: true } side)
                    PanelState.SetSide(SearchSide.Letter, side.WithIgnoreRequirements(on));
            }
        );
        _gear = new StartGear(pickItem, Changed);
        // The gear first, where the player looks and locks; then the goal; the rest folded with a line of what's on.
        _options = new OptionsFold([_ignore, _narrowing.Root, _styles, .. _switches.Controls], OptionsOn);
        foreach (var part in new Control[] { _gear.Root, rank, deaths, _options.Root })
            Root.AddChild(part);

        // The counts and the button stay in view at the details' foot, however long the form above them.
        Foot = Ui.Column(8, "GearSearchStartFoot");
        Foot.AddChild(_counts.Root);
        _note = Ui.Add(Foot, Ui.Label("", TextStyle.Small, "GearSearchNote", wrap: true));
        _note.LabelSettings = Compare.CompareText.Coloured(Look.Risky, TextStyle.Small);

        var foot = Ui.Add(Foot, Ui.Row(8));
        foot.Alignment = BoxContainer.AlignmentMode.End;
        _find = FramedButton.Make(Ui.Button("", "", "GearSearchFind", Start), 16, 6, main: true);
        _find.AddThemeFontOverride("font", Look.Font(700));
        _find.AddThemeFontSizeOverride("font_size", 18);
        foot.AddChild(_find);
    }

    public VBoxContainer Root { get; }

    /// <summary>The counts and the start button, which the tab puts where they stay in view.</summary>
    public VBoxContainer Foot { get; }

    /// <summary>
    /// The form for <paramref name="target"/> and a search of <paramref name="kind"/>, counted again when either or
    /// the side searched from has changed since it was last shown.
    /// </summary>
    public void Show(string target, SearchKind kind, string? note = null)
    {
        var stamp = (target, kind, SearchSide.Check, SearchSide.CanChoose, note, Tr.Version);
        if (stamp == _stamp)
            return;
        var fresh =
            _stamp is not { } last
            || last.Target != target
            || last.Kind != kind
            || !ReferenceEquals(last.Check, SearchSide.Check);
        _stamp = stamp;
        if (_target != target)
            _shown = null;
        _target = target;
        _kind = kind;
        _styles.Visible = kind == SearchKind.BestGear;
        _gear.Locking = kind == SearchKind.BestGear;
        _find.Text =
            kind == SearchKind.BestGear
                // Translators: Start button of a gear search for the best setup of what the character has (gear, prayers, food…).
                ? Tr.Get("Find best setup")
                // Translators: Start button of a gear search for which unowned items would gain the most.
                : Tr.Get("Find upgrades");
        FillMetrics();
        _deaths.Show(
            _limit switch
            {
                DeathLimit.UnderFivePercent => "five",
                DeathLimit.SurvivedOnce => "any",
                _ => "none",
            }
        );
        Cs2Check.Set(_styles, _allStyles);
        Cs2Check.Set(_ignore, SearchSide.Side.IgnoreRequirements);
        var bypass = Game.Dependencies.IsAvailable(Game.Dependencies.IgnoreRequirements);
        _ignore.Disabled = !SearchSide.Side.CanChange || !bypass;
        _ignore.TooltipText = bypass
            // Translators: Hover of the gear search form's Ignore requirements tick, which is the searched side's own setting; {0} is its letter, A or B.
            ? Tr.Format(
                "Side {0}'s setting, as on the Setup tab: search any gear and choice, requirements met or not.",
                SearchSide.Letter
            )
            : Tr.Get("Unsupported on this game build");
        _switches.Show(kind);
        _narrowing.Show(PoolPick.For(kind, target) == ItemPool.Every);
        _options.Refresh();
        _gear.Fill();
        _note.Text = note ?? "";
        _note.Visible = note != null;
        if (fresh || _preview == null)
            Count();
    }

    /// <summary>The pool chosen again in the tab's sentence: its narrowing shown or hidden, and counted again.</summary>
    public void PoolChanged()
    {
        if (_target != null)
            _narrowing.Show(PoolPick.For(_kind, _target) == ItemPool.Every);
        Changed();
    }

    /// <summary>The form's choices and counts, for <c>cs2 ui</c>.</summary>
    public string Describe() =>
        $"pool={(_target != null ? PoolPick.For(_kind, _target) : ItemPool.Owned)} metric={_metric} deaths={_limit} styles={(_allStyles ? "all" : "setup")} locks={_gear.Locks.Count} never={_gear.Excluded.Count} counts=\"{_counts.Describe()}\"";

    private void FillMetrics()
    {
        _metrics = [.. RunCache.Metrics(RunService.Instance.Current)];
        if (_metrics.All(m => m.Key != _metric))
            _metric = _metrics.Any(m => m.Key == PanelState.Prefs.Metric) ? PanelState.Prefs.Metric : Metrics.MainXp;
        _rankBy.Clear();
        var run = RunService.Instance.Current;
        var game = Game.LiveGame.Find();
        foreach (var metric in _metrics)
            _rankBy.AddItem(
                metric.Key == Metrics.MainXp ? Words.Metric(metric) : Words.MetricName(metric, null),
                game != null ? Game.Art.Metric(game, run != null ? Metrics.Resolve(run, metric.Key) : metric.Key) : null
            );
        _rankBy.Select(Math.Max(0, _metrics.FindIndex(m => m.Key == _metric)));
    }

    private void Changed()
    {
        _note.Visible = false;
        _options.Refresh();
        Count();
    }

    // What the options switched on add, for the options' summary.
    private string OptionsOn()
    {
        var prefs = PanelState.Prefs;
        var on = new List<string>();
        if (SearchSide.Side.IgnoreRequirements)
            // Translators: In the summary of the gear search form's options: the side searched from ignores requirements.
            on.Add(Tr.Get("requirements ignored"));
        if (_target != null && PoolPick.For(_kind, _target) == ItemPool.Every)
        {
            if (prefs.SearchPoolWearable)
                on.Add(Tr.Get("wearable at my levels"));
            if (prefs.SearchPoolReached)
                on.Add(Tr.Get("content I've reached"));
            if (prefs.SearchLeftOut.Count > 0)
                // Translators: In the summary of the gear search form's options: {0} how many item sources are left out.
                on.Add(Tr.Format("{0} left out", Ui.Number(prefs.SearchLeftOut.Count)));
        }
        if (_kind == SearchKind.BestGear)
        {
            if (_allStyles)
                // Translators: In the summary of the gear search form's options: other combat styles are tried too.
                on.Add(Tr.Get("other styles"));
            on.AddRange(KindSwitches.For(_kind).Select(KindSwitches.KindName));
            if (prefs.SearchIncludeUnowned)
                // Translators: In the summary of the gear search form's options: choices the character doesn't have are tried too.
                on.Add(Tr.Get("choices I don't have"));
        }
        // Translators: The summary of the gear search form's options when none is switched on.
        return on.Count == 0 ? Tr.Get("Gear only") : string.Join(" · ", on);
    }

    private SearchRequest? Request()
    {
        if (_target == null)
            return null;
        var prefs = PanelState.Prefs;
        return PoolPick.Apply(
            new SearchRequest(
                _kind,
                SearchSide.Setup(),
                _target,
                new Goal(_metric, _limit),
                prefs.Trials,
                prefs.Copies ?? RunService.Instance.Status.DefaultCopies
            )
            {
                Locks = _kind == SearchKind.BestGear ? new HashSet<string>(_gear.Locks) : new HashSet<string>(),
                Excluded = new HashSet<string>(_gear.Excluded),
                AllStyles = _kind == SearchKind.BestGear && _allStyles,
                Kinds = KindSwitches.For(_kind),
                IncludeUnowned = _kind == SearchKind.BestGear && PanelState.Prefs.SearchIncludeUnowned,
                MaxSeconds = prefs.SearchMaxSeconds,
            }
        );
    }

    // The plan's counts, read from a check copy; a later change makes an earlier count's answer stale. The counts
    // shown before stay, their numbers faded, until the new ones come.
    private void Count()
    {
        _preview = null;
        _counting = true;
        _find.Disabled = true;
        if (_shown is { } shown && shown.Kind == _kind)
            Render(shown);
        else
            Say(Tr.Get("Counting combinations…"));
        if (Request() is not { } request || Game.LiveGame.Find() == null)
            return;
        var version = ++_version;
        try
        {
            RunService.PreviewSearch(
                request,
                (preview, error) =>
                {
                    if (version != _version || !GodotObject.IsInstanceValid(Root))
                        return;
                    _counting = false;
                    if (preview == null)
                    {
                        Say(error?.Message ?? Tr.Get("The sets couldn't be counted."));
                        return;
                    }
                    _preview = preview;
                    _shown = preview;
                    Render(preview);
                    _find.Disabled = preview.NothingToSearch;
                    if (preview.OverLimit && preview.Kind == SearchKind.BestGear)
                        // Translators: The start button when fighting every set would pass the long-search limit.
                        _find.Text = Tr.Get("Shorter search");
                }
            );
        }
        catch (CommandException e)
        {
            _counting = false;
            Say(e.Message);
        }
    }

    // A message in place of the counts.
    private void Say(string message)
    {
        _shown = null;
        _counts.Say(message);
    }

    private void Render(PlanPreview preview)
    {
        _switches.Counts(preview);
        _counts.Render(preview, _counting);
    }

    private void Start()
    {
        if (Request() is { } request)
            _start(request);
    }
}
