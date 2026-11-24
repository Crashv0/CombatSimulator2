using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Compare;
using MelvorMods.CombatSimulator2.Panel.Results;
using MelvorMods.CombatSimulator2.Panel.SetupTab;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// The Simulate view: the Setup column on the left; in the middle the target filters, the targets strip and the
/// results; and on the right the details with the run control at their foot; or the compare view across the middle and right. It posts requests to
/// the run service and redraws from it (at most once a frame), so closing CS2 never stops a run and reopening shows it
/// as it is.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class SimulateView
{
    private readonly VBoxContainer _root;
    private readonly HBoxContainer _body;
    private readonly HBoxContainer _main;
    private readonly Queue<Action> _parts = new();
    private SetupColumn _setup = null!;
    private RunControl _run = null!;
    private VBoxContainer _right = null!;
    private BasePicker _basePicker = null!;
    private VBoxContainer _middle = null!;
    private TargetFilters _filters = null!;
    private TargetStrip _strip = null!;
    private ResultsView _results = null!;
    private Details _details = null!;
    private CompareView _compare = null!;
    private CompareDetails _compareDetails = null!;
    private ComparePicker _picker = null!;
    private Control _layer = null!;
    private SlotPicker _slotPicker = null!;
    private readonly SetupCheck _check;
    private readonly ComparisonFollower _follower = new();
    private readonly RunStarter _starter;
    private bool _dirty = true;
    private bool _detailsDirty;
    private bool _checkPending;
    private bool _comparisonPending;
    private int _warm;
    private (string? Id, RunState State) _seen;
    private readonly System.Diagnostics.Stopwatch _progressClock = System.Diagnostics.Stopwatch.StartNew();

    // The containers now; each part on a frame of its own (see OnFrame), so opening never stalls a frame.
    public SimulateView(Control hoverOwner)
    {
        _root = Ui.Column(10, "Cs2SimulateView");
        _root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _body = Ui.Add(_root, Ui.Row(10, "Cs2Columns"));
        _body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _main = Ui.Add(_body, Ui.Row(10, "Cs2Main"));
        _main.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _check = new SetupCheck(_root);
        _starter = new RunStarter(text => _results.ShowMessage(text), _check, _follower);

        _parts.Enqueue(() =>
        {
            _setup = new SetupColumn();
            _setup.ChangePressed += letter =>
                _basePicker.Toggle(letter, letter == SideLetter.A ? PanelState.A : PanelState.B);
            _setup.EditPressed += SetupTabView.SetUp;
            _setup.SlotPressed += (letter, slot) => _slotPicker.Pick(letter, slot);
            _setup.SlotStepped += (letter, slot) => _slotPicker.StepDown(letter, slot);
            _setup.StatsPressed += () => Stats.FullStatsView.Open(Stats.FullStatsView.Both);
            _setup.ComparePressed += () => PanelState.PickBaseline(true);
            _body.AddChild(_setup.Root);
            _body.MoveChild(_setup.Root, 0);
            // Filled now: the view's first refresh waits for its last part, and until then the column would show
            // as built, with both cards and every button of theirs.
            _setup.Refresh(RunService.Instance.IsActive);
        });
        _parts.Enqueue(() =>
        {
            _middle = Ui.Add(_main, Ui.Column(8, "Cs2Middle"));
            _middle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _filters = new TargetFilters(hoverOwner);
            _middle.AddChild(_filters.Root);
            _strip = new TargetStrip(_filters);
            _filters.Root.AddChild(Ui.Spacer());
            _middle.AddChild(_strip.Root);
        });
        _parts.Enqueue(() =>
        {
            _results = new ResultsView(hoverOwner);
            _results.SimulateSelected += _starter.StartSelected;
            _results.SimulateFighting += _starter.StartFighting;
            _results.SimulateAgain += keys => _starter.Start([.. keys.Select(TargetSpec.FromKey)]);
            _results.UseCurrentSetup += () => PanelState.SetSide(SideLetter.A, PanelState.A.WithBase(SideBase.Current));
            _results.PickAnotherBaseline += () => PanelState.PickBaseline(true);
            _results.UseGameProcess += keys =>
            {
                PanelState.Prefs.SimulateIn = Preferences.InGame;
                PanelState.Save();
                PanelState.SetBanner(null);
                _starter.Start([.. keys.Select(TargetSpec.FromKey)]);
            };
            _middle.AddChild(_results.Root);
            _filters.Root.AddChild(_results.Toolbar);
        });
        _parts.Enqueue(() =>
        {
            // The right column: what is selected, down to the window's foot; the run control sits under the results.
            _right = Ui.Add(_main, Ui.Column(8, "Cs2RightColumn"));
            // A share of extra room small enough to keep its least width at 16:9, so only wide screens widen it.
            (_right.SizeFlagsHorizontal, _right.SizeFlagsStretchRatio) = (Control.SizeFlags.ExpandFill, 0.3f);
            _details = new Details(hoverOwner, (under, picked) => _slotPicker.PickItem(under, picked));
            _details.Root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _right.AddChild(_details.Root);
            _compareDetails = new CompareDetails(hoverOwner, (under, picked) => _slotPicker.PickItem(under, picked));
            _compareDetails.Root.Visible = false;
            _right.AddChild(_compareDetails.Root);
            Help.AboutButton.Follow(_details.Root, _compareDetails.Root);
            _run = new RunControl(hoverOwner, _starter.StartFighting);
            _run.Pressed += () =>
            {
                if (RunService.Instance.IsActive)
                    RunStarter.Cancel();
                else
                    _starter.StartSelected();
            };
            _results.AddRunControl(_run.Root, _run.Button);
            _picker = new ComparePicker();
            _picker.Closed += () => PanelState.PickBaseline(false);
            _picker.TryPicked += () =>
            {
                PanelState.PickBaseline(false);
                SetupTabView.SetUp(SideLetter.B);
            };
            _picker.PresetPicked += PresetSides.UseAsSideB;
            _picker.LoadoutPicked += slot =>
            {
                PanelState.PickBaseline(false);
                PanelState.SetSide(SideLetter.B, new SideState(SideBase.Loadout(slot)));
            };
            _picker.RunPicked += CompareAgainst;
            _picker.HistoryPressed += () => Cs2Modal.Jump(Cs2Modal.History);
            _body.AddChild(_picker.Root);
        });
        _parts.Enqueue(() =>
        {
            _compare = new CompareView(() => PanelPins.SetCompare(false));
            _compare.Root.Visible = false;
            _body.AddChild(_compare.Root);
            // A layer over the view, last so it draws on top: the base picker opens in it beside the Setup column.
            _layer = new Control { Name = "Cs2SimulateLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
            _root.AddChild(_layer);
            _basePicker = new BasePicker();
            _basePicker.Picked += BasePicked;
            _basePicker.PresetPicked += (letter, name) =>
            {
                if (PresetSides.Take(letter, name))
                    Recheck(letter);
            };
            _layer.AddChild(_basePicker.Root);
            _slotPicker = new SlotPicker(_root, _layer, _setup);
            RunService.Instance.Changed += MarkDirty;
            PanelState.Changed += MarkDirty;
            Look.Changed += MarkDirty;
            _root.TreeExiting += () =>
            {
                RunService.Instance.Changed -= MarkDirty;
                PanelState.Changed -= MarkDirty;
                Look.Changed -= MarkDirty;
            };
            // The first check builds a copy of the game (≈90 MB) on a worker thread, and the collection that
            // follows can pause the game for a frame: it starts once the window has appeared.
            _checkPending = true;
        });
    }

    // A side's new base. Side B picked as a saved run compares at once, as picking it in the Compare picker does.
    private void BasePicked(SideLetter letter, SideBase picked)
    {
        if (picked.Kind == SideBaseKind.SavedRun)
        {
            CompareAgainst(picked.Id!);
            return;
        }
        var side = letter == SideLetter.A ? PanelState.A : PanelState.B ?? new SideState(picked);
        PanelState.SetSide(letter, side.StartingFrom(picked));
        Recheck(letter);
    }

    private void Recheck(SideLetter letter)
    {
        if (letter != SideLetter.A)
            return;
        _check.ClearError();
        _check.Check(ShowCheckError);
    }

    // The base picker beside the Setup column, level with the Change button that opened it, moved up as far as it
    // must to stay in the view.
    private void PlaceBasePicker()
    {
        var wanted = _basePicker.Root.GetCombinedMinimumSize();
        _basePicker.Root.Size = wanted;
        var top = _body.Position.Y - _layer.Position.Y;
        var change = _setup.ChangeButton(_basePicker.Letter);
        var level = (_layer.GetGlobalTransform().AffineInverse() * change.GetGlobalRect().Position).Y;
        _basePicker.Root.Position = new Vector2(
            _body.Position.X + _setup.Root.Position.X + _setup.Root.Size.X + 8,
            Math.Max(top, Math.Min(level, _layer.Size.Y - wanted.Y))
        );
    }

    /// <summary>Whether all the view's parts exist.</summary>
    public bool Ready => _parts.Count == 0;

    public Control Root => _root;

    public TargetStrip Strip => _strip;

    public ResultsView Results => _results;

    /// <summary>Compares the setup as it is now against a kept run, as the saved-run picker does.</summary>
    public void CompareAgainst(string runId)
    {
        PanelState.PickBaseline(false);
        PanelState.SetCompareWith(PanelState.SavedRun, runId);
        if (!RunService.Instance.IsActive)
            _starter.Start([]);
    }

    /// <summary>Whether rows or areas are still being made a few per frame.</summary>
    public bool Building =>
        !Ready || _dirty || _detailsDirty || _details.Pending || _results.List.Pending || _comparisonPending;

    /// <summary>The run control, for the tour and the bench.</summary>
    public RunControl Run => _run;

    private void MarkDirty() => _dirty = true;

    /// <summary>
    /// Reads the setups again when the modal opens again, as they may have changed while it was closed.
    /// </summary>
    public void Reopened()
    {
        if (!Ready)
            return;
        SideChecker.Recheck();
        _check.Check(ShowCheckError);
        MarkDirty();
    }

    /// <summary>
    /// Does one piece of work a frame while open: the next part, a redraw of the results if anything changed, then
    /// the details over the frames after.
    /// </summary>
    public void OnFrame()
    {
        // A timestamp, not a stopwatch: this runs every frame while open and must allocate nothing when idle.
        var work = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_parts.Count > 0)
        {
            _parts.Dequeue()();
            Slow("part " + (6 - _parts.Count).ToString(System.Globalization.CultureInfo.InvariantCulture), work);
            return;
        }
        _run.Tick();
        _strip.Tick();
        // A run under way, or the simulator's status changing between runs (starting, filling its stock, ready): the
        // status and bar move four times a second, on a beat that leaves an idle panel doing nothing here.
        if (_progressClock.ElapsedMilliseconds >= 250)
        {
            _progressClock.Restart();
            if (
                RunService.Instance.IsActive
                || RunService.Host?.State == HostState.Starting
                || _run.PerformanceShown
                || _run.StatusStale
            )
                _run.Refresh(RunService.Instance.Current);
        }
        // Hidden behind the Setup tab, it redraws once it is shown again: an edit there needn't redraw the results.
        var shown = _root.IsVisibleInTree();
        _slotPicker.Place(shown);
        if (_dirty && shown && _warm < 2)
        {
            RunCache.Warm(RunService.Instance.Current, _warm++);
            Slow("warm " + _warm.ToString(System.Globalization.CultureInfo.InvariantCulture), work);
            return;
        }
        if (_dirty && shown)
        {
            _dirty = false;
            _warm = 0;
            Refresh();
            Slow("refresh", work);
            _detailsDirty = true;
            return;
        }
        // The details' header on one frame, then each of its sections on a frame of its own.
        if (_detailsDirty)
        {
            _detailsDirty = false;
            RefreshDetails();
            Slow("details", work);
            return;
        }
        if (_details.Pending)
        {
            _details.BuildNext();
            Slow("details section", work);
            return;
        }
        if (_checkPending && !Cs2Modal.Opening)
        {
            _checkPending = false;
            _check.Check(ShowCheckError);
            return;
        }
        if (_basePicker.Open)
            PlaceBasePicker();
        if (_results.List.Pending || _comparisonPending)
            _dirty = true;
    }

    private static void Slow(string what, long work)
    {
        var took = System.Diagnostics.Stopwatch.GetElapsedTime(work);
        if (took.TotalMilliseconds > 4)
            Log.Info(
                $"simulate view: {what} {took.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} ms"
            );
    }

    private void Refresh()
    {
        if (!GodotObject.IsInstanceValid(_root))
            return;
        var service = RunService.Instance;
        var run = service.Current;
        var selection = LiveGame.Find() != null ? PanelState.Selection() : null;
        var selected = selection?.ToRun.Count ?? 0;

        // A run that became cancelled shows the banner once, whoever cancelled it.
        if (run != null && (run.Id, run.State) != _seen)
        {
            // A new run, from here or from cs2 run: the pins on targets it doesn't have wait.
            if (_seen.Id != null && run.Id != _seen.Id)
            {
                PanelPins.RunStarted();
                _strip.SetOpen(false);
            }
            if (run.State == RunState.Cancelled && _seen.Id == run.Id)
            {
                var keys = run
                    .Targets.Where(k => run.Results.GetValueOrDefault(k)?.State == TargetState.Cancelled)
                    .ToList();
                PanelState.SetBanner(new BannerState.Cancelled(keys.Count, keys));
            }
            // A run that ended may have been the player's own fighting: the what-if's from-values follow it.
            if (run.IsFinal)
                SideChecker.Recheck();
            _seen = (run.Id, run.State);
        }

        var setupError = _check.Error != null;
        _follower.Follow(service, run);
        _setup.Refresh(service.IsActive);
        _run.Show(service, run, selected, _setup.Problem, setupError);
        if (_basePicker.Open)
            PlaceBasePicker();
        _filters.Refresh();
        _strip.Show(selection);

        var picking = PanelState.PickingBaseline;
        if (picking && !_picker.Root.Visible)
            _picker.Show();
        _picker.Root.Visible = picking;
        if (picking)
        {
            _main.Visible = false;
            _compare.Root.Visible = false;
            return;
        }

        var comparing = PanelState.ComparisonShown && service.Comparison != null;
        _details.Root.Visible = !comparing;
        _compareDetails.Root.Visible = comparing;
        if (comparing)
        {
            _main.Visible = true;
            _compare.Root.Visible = false;
            _comparisonPending = _results.RefreshComparison(service.Comparison!, selection);
            return;
        }
        _comparisonPending = false;

        var compare = PanelPins.CompareOpen && run != null && PanelPins.ShownPins.Count >= 2;
        if (compare != _compare.Root.Visible)
            Motion.FadeIn(compare ? _compare.Root : _main);
        _main.Visible = !compare;
        if (compare || PanelState.Prefs.ResultsMode != "chart")
            _results.Chart.HideHover();
        _compare.Root.Visible = compare;
        if (compare)
        {
            _compare.Show(run!, PanelPins.ShownPins, PanelState.Prefs.Unit);
            return;
        }
        _results.Refresh(run, selected, setupError, selection);
    }

    // A failed check, shown where the strip's targets would be.
    private void ShowCheckError(Note reason) => _results.ShowMessage(Tr.Note(reason));

    private void RefreshDetails()
    {
        if (!GodotObject.IsInstanceValid(_root) || PanelPins.CompareOpen)
            return;
        if (PanelState.ComparisonShown && RunService.Instance.Comparison is { } comparison)
        {
            if (PanelState.Selected is { } selected && comparison.Record.Targets.Contains(selected))
                _compareDetails.Show(comparison, selected, PanelState.Prefs.Metric, PanelState.Prefs.Unit);
            else
                _compareDetails.ShowNothing();
            return;
        }
        var run = RunService.Instance.Current;
        if (run != null && PanelState.Selected is { } key && run.Results.ContainsKey(key))
            _details.Show(run, key, PanelState.Prefs.Metric, PanelState.Prefs.Unit);
        else
            _details.ShowNothing();
    }
}
