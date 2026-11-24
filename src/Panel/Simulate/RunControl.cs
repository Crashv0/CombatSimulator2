using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Results;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// The run control at the foot of the right column: the last run's status and counter, a short bar in the engine's
/// colour, the performance line with its hover, and the one button that simulates, compares or cancels. The
/// button only tells the view; the view starts the run.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class RunControl
{
    private readonly MarginContainer _root;
    private readonly HBoxContainer _hostRow;
    private readonly Control _hostDot;
    private readonly Label _host;
    private readonly Label _progress;
    private readonly Control _progressBar;
    private readonly HoverPanel _perfHover;
    private readonly Control _hoverOwner;
    private Vector2 _perfMouse;
    private (PerfHistory? History, int Count) _perfShown;
    private readonly Button _button;
    private bool _running;
    private bool _notRunYet;

    public RunControl(Control hoverOwner, Action fight)
    {
        // Rows that are always there, so nothing moves as a run starts, steps and ends: the status and the counter,
        // the bar, then the button.
        // Lined up with the details' contents above it, not with the panel's edge: a look switch rebuilds the window,
        // so the box's margins are read again for the other look.
        var box = Look.Box(Art.SideBox);
        _root = new MarginContainer { Name = "Cs2RunInset" };
        _root.AddThemeConstantOverride("margin_left", (int)box.GetContentMargin(Side.Left));
        _root.AddThemeConstantOverride("margin_right", (int)box.GetContentMargin(Side.Right));
        // The button for what the character is fighting, beside a column of the status, the bar and Simulate: the
        // column keeps the width Simulate has alone, level with the details above, and the control grows by the button.
        var buttons = Ui.Add(_root, Ui.Row(6, "Cs2RunButtons"));
        buttons.AddChild(MakeFighting(fight));
        var progress = Ui.Add(buttons, Ui.Column(3, "Cs2RunControl"));
        progress.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        progress.CustomMinimumSize = new Vector2(
            Results.Details.Width - box.GetContentMargin(Side.Left) - box.GetContentMargin(Side.Right),
            0
        );
        // After a pasted simulation is used: one line saying so, with Undo, until a run starts.
        _used = Ui.Add(progress, Ui.Row(8, "Cs2SimulationUsed"));
        var usedText = Ui.Add(
            _used,
            Ui.Label(
                Tr.Get("Simulation used: both sides, the targets left out and the settings."),
                TextStyle.Small,
                wrap: true
            )
        );
        usedText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _used.AddChild(Ui.Button(Tr.Get("Undo"), Look.Button4, "Cs2SimulationUndo", () => SimulationPage.Undo()));
        _used.Visible = false;
        _hostRow = Ui.Add(progress, Ui.Row(6, "Cs2RunStatus"));
        _hostDot = Ui.Add(_hostRow, StatusDot.Make("Cs2HostDot"));
        _host = Ui.Clipped(" ", TextStyle.Small, "Cs2HostStatus");
        _hostRow.AddChild(_host);
        _progress = Ui.Label(" ", TextStyle.Small);
        _progress.HorizontalAlignment = HorizontalAlignment.Right;
        _progress.MouseFilter = Control.MouseFilterEnum.Pass;
        _hostRow.AddChild(_progress);
        _progressBar = Bar.Make("Cs2RunBar", 6);
        _barInset = Ui.Add(progress, new MarginContainer { Name = "Cs2RunBarInset" });
        _barInset.AddChild(_progressBar);
        _perfHover = new HoverPanel(hoverOwner);
        _hoverOwner = hoverOwner;
        // Hovering the bar shows the simulator's performance over the run.
        _progressBar.MouseFilter = Control.MouseFilterEnum.Pass;
        _progressBar.GuiInput += e =>
        {
            if (e is not InputEventMouseMotion motion)
                return;
            _perfMouse = _progressBar.GetGlobalTransform() * motion.Position;
            ShowPerfHover(force: !_perfHover.Visible);
        };
        _progressBar.MouseExited += () => _perfHover.Hide();
        _button = Ui.Button(Tr.Get("▶ Simulate"), Look.Action8, "Cs2RunButton", () => Pressed?.Invoke());
        progress.AddChild(_button);
        // The button's style is known once it is in the tree, and changes with the look and with Cancel run.
        _button.ThemeChanged += FitBar;
    }

    private readonly MarginContainer _barInset;
    private readonly HBoxContainer _used;

    // The bar spans the button's face, not its box: mElvUI's flat button has an outline the bar would overhang.
    private void FitBar()
    {
        var face = _button.GetThemeStylebox("normal") as StyleBoxFlat;
        _barInset.AddThemeConstantOverride("margin_left", face?.BorderWidthLeft ?? 0);
        _barInset.AddThemeConstantOverride("margin_right", face?.BorderWidthRight ?? 0);
    }

    public Control Root => _root;

    /// <summary>The run button, which the pin tray lines up with.</summary>
    public Control Button => _button;

    /// <summary>Raised when the button is pressed: simulate, compare, or cancel the run that is going.</summary>
    public event Action? Pressed;

    /// <summary>
    /// Shows what the button will do for <paramref name="selected"/> targets, why it can't (the setup's
    /// <paramref name="setupProblem"/>, a check that failed, nothing to run), and whether the sides were edited since
    /// the run shown; then the run's progress.
    /// </summary>
    public void Show(RunService service, Run? run, int selected, string? setupProblem, bool setupError)
    {
        _used.Visible = SimulationPage.CanUndo;
        var b = PanelState.B;
        var againstSaved = b is { CanChange: false };
        var targets = againstSaved
            ? RunStore.Shared.List().FirstOrDefault(h => h.Id == b!.Base.Id)?.Targets ?? 0
            : selected;
        var disabled =
            LiveGame.Find() == null ? Tr.Note(LiveGame.NoGame)
            : setupProblem != null ? setupProblem
            : setupError ? Tr.Get("This setup can't be simulated; see the message above the results.")
            : targets == 0 ? NothingToRun()
            : null;
        var shownId = PanelState.ComparisonShown && service.Comparison is { } comparison ? comparison.A.Id : run?.Id;
        var edited =
            PanelState.Ran is { } ran
            && ran.Run == shownId
            && (!ran.A.Equals(PanelState.A) || !Equals(ran.B, PanelState.B));
        // Two sides that are one setup run once, as a single run.
        SetButton(service.IsActive, targets, b != null && !SetupTab.SideChecker.SidesSame(), disabled, edited);
        Refresh(run);
    }

    // Why there is nothing to run: targets still being checked, a filter that shows none, or every one shown ignored.
    private static string NothingToRun() =>
        PanelState.Selection() switch
        {
            null => Tr.Get("Checking targets for this setup…"),
            { Shown.Count: > 0 } => Tr.Get("Nothing to simulate: every target shown is ignored. Return some."),
            _ => Tr.Get("Nothing to simulate: no target matches the filters."),
        };

    // "Cancel run" while a run goes, else Simulate or Compare with the number of targets the run will cover; disabled
    // says why it can't be pressed (shown on hover), and notRunYet that the sides were edited since the run shown.
    private void SetButton(bool active, int targets, bool comparing, string? disabled, bool notRunYet)
    {
        // The Simulate and Cancel looks differ in height: the button keeps the tallest it has been, so switching
        // between them doesn't move what is above.
        if (_button.Size.Y > _button.CustomMinimumSize.Y)
            _button.CustomMinimumSize = new Vector2(0, _button.Size.Y);
        if (active != _running)
        {
            _running = active;
            _button.ThemeTypeVariation = active ? Look.Cancel8 : Look.Action8;
        }
        _buttonText =
            active ? Tr.Get("■ Cancel run")
            : comparing ? Tr.Plural("▶ Compare {0} target", "▶ Compare {0} targets", targets, targets)
            : Tr.Plural("▶ Simulate {0} target", "▶ Simulate {0} targets", targets, targets);
        _button.Text = !active && Time.GetTicksMsec() < _sayingUntil ? SameAsBefore : _buttonText;
        _button.Disabled = !active && disabled != null;
        _button.TooltipText = !active ? disabled ?? "" : "";
        _notRunYet = notRunYet && !active;
    }

    /// <summary>Presses the button as a click would; when it can't be pressed, returns why and does nothing.</summary>
    public string? Press()
    {
        if (_button.Disabled)
            return _button.TooltipText.Length > 0 ? _button.TooltipText : Tr.Get("The button can't be pressed now");
        Pressed?.Invoke();
        return null;
    }

    /// <summary>The control as <c>cs2 ui</c> prints it.</summary>
    public string Describe() =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"run_control status={_progress.Text.Trim()} button={_button.Text}{(_button.Disabled ? " (disabled)" : "")} bar={Bar.Fraction(_progressBar) * 100:0}"
        );

    // A run answered whole from earlier results ends at once with the earlier numbers, so a press seems to do nothing:
    // the button says so on its face for a moment, once per run, the counter lights and says "reused" where the time
    // goes. None of it takes room from the results or covers other text.
    private string? _notedReused;
    private string _buttonText = "";
    private ulong _sayingUntil;
    private const ulong SayingMs = 1800;

    private static string SameAsBefore => Tr.Get("Same as before: nothing fought");

    private static bool AllReused(Run run) => run.Targets.Count > 0 && run.Timings?.Reused >= run.Targets.Count;

    private void NoteReused(Run? run, RunService service)
    {
        if (run == null || run != service.Current || !run.IsFinal || run.Id == _notedReused)
            return;
        _notedReused = run.Id;
        var a = service.Comparison is { } c && ReferenceEquals(c.B, run) ? c.A : run;
        if (AllReused(run) && AllReused(a) && run.EndedAt is { } end && (DateTime.UtcNow - end).TotalSeconds < 3)
        {
            _sayingUntil = Time.GetTicksMsec() + SayingMs;
            _button.Text = SameAsBefore;
            // The counter reads the same after each such press, so it lights briefly and settles back.
            _progress
                .CreateTween()
                .TweenProperty(_progress, "modulate", Colors.White, 0.7)
                .From(new Color(1.7f, 1.6f, 1.2f))
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
        }
    }

    private static string Took(Run run, double seconds) =>
        AllReused(run) ? Tr.Get("reused", "a run's time") : Words.Seconds(seconds.ToString("0.0", Tr.Culture));

    // "A 24 of 24 · B 9 of 24 · 4.1 s": each side's progress, then the time of the side running or the last.
    private static string CompareCounter(ComparedRuns comparison)
    {
        var service = RunService.Instance;
        var running = comparison.IsFinal ? null : service.Current;
        var seconds =
            running != null ? service.RealSeconds
            : comparison.B.EndedAt is { } end && comparison.BStarted ? (end - comparison.B.StartedAt).TotalSeconds
            : 0;
        var total = comparison.Record.Targets.Count;
        return Tr.Format(
            "{5} {0} of {1} · {6} {2} of {3} · {4}",
            [
                comparison.A.Done,
                total,
                comparison.BStarted ? comparison.B.Done : 0,
                total,
                comparison.IsFinal && AllReused(comparison.A) && AllReused(comparison.B)
                    ? Took(comparison.B, seconds)
                    : Words.Seconds(seconds.ToString("0.0", Tr.Culture)),
                SideLine.A,
                SideLine.B,
            ]
        );
    }

    /// <summary>
    /// Refreshes the counter, timer, status line and bar. Also called a few times a second while a run is active, so
    /// the panel shows the run moving even while no result has arrived yet (a host starting, first copies building).
    /// </summary>
    public void Refresh(Run? run)
    {
        var service = RunService.Instance;
        var total = run?.Targets.Count ?? 0;
        var done = run?.Done ?? 0;
        NoteReused(run, service);
        Bar.Set(
            _progressBar,
            BestLoadoutsProgress(service)?.Share ?? (double)done / Math.Max(1, total),
            run?.State switch
            {
                RunState.Cancelled => Look.Risky,
                RunState.Error => Look.Deadly,
                _ => Look.Engine,
            }
        );
        ShowCounter(run);
        // The run's identity on hover: who and what it simulated, and with which build and seed.
        _progress.TooltipText =
            run == null
                ? ""
                : string.Join(
                    " · ",
                    new[] { run.Setup.Character, run.Name, Words.Setup(run.Setup), run.Id }.OfType<string>()
                )
                    + "\n"
                    + PerfView.BuildAndSeed(run)
                    + (
                        run.IsFinal && AllReused(run)
                            ? "\n" + Tr.Get("Reused from earlier runs: Settings › Simulate fresh fights again.")
                            : ""
                    );
        _statusFrom = PerfView.Inputs(service.Current, service);
        var (host, statusColour, pulse) = PerfView.StatusLine(run, service);
        var colour = statusColour ?? Look.Muted(_progress);
        Ui.SetClipped(_host, host ?? " ");
        if (_host.LabelSettings?.FontColor != colour)
        {
            // Marked for mElvUI's skin to leave alone in either look: the engine colour isn't in its palette, and
            // the status must keep the colour of the bar and dot drawn beside it.
            var style = (LabelSettings)Look.Style(TextStyle.Small).Duplicate();
            style.SetMeta(Skins.SkipMeta, true);
            style.FontColor = colour;
            _host.LabelSettings = style;
        }
        StatusDot.Set(_hostDot, colour, pulse);
        _hostDot.Modulate = host != null ? Colors.White : Colors.Transparent;
        if (_perfHover.Visible)
            ShowPerfHover(force: false);
    }

    /// <summary>
    /// Whether the status shown is no longer the simulator's, so it needs a refresh even with no run active.
    /// </summary>
    public bool StatusStale => PerfView.Inputs(RunService.Instance.Current, RunService.Instance) != _statusFrom;

    // What the status line shown was made from.
    private PerfView.StatusInputs _statusFrom;

    private string? _counterShown;

    /// <summary>
    /// Called every frame: moves the timer on while a run is active, ends the button's note, and follows what the
    /// character is fighting.
    /// </summary>
    public void Tick()
    {
        if (_sayingUntil > 0 && Time.GetTicksMsec() >= _sayingUntil)
        {
            _sayingUntil = 0;
            _button.Text = _buttonText;
        }
        if (RunService.Instance.IsActive)
            ShowCounter(RunService.Instance.Current);
        TickFighting();
    }

    /// <summary>
    /// Shows the counter and its timer, redrawn only when its text changes. Called every frame while a run is active:
    /// on a slower beat the tenths would step unevenly, and the timer would look to run fast and slow by turns.
    /// </summary>
    public void ShowCounter(Run? run)
    {
        var service = RunService.Instance;
        var text =
            _notRunYet ? Tr.Get("Not run yet")
            // A search under way is what the button cancels: the last run's counter says nothing of it.
            : service.Search is { IsActive: true } search
                ? search.Stage == 0
                        // Translators: Under the run button while a gear search reads the gear, before its first step.
                        ? Tr.Get("Searching · reading your gear")
                    // Translators: Under the run button while a gear search runs: {0} the step or stage under way.
                    : Tr.Format(
                        "Searching · step {0}",
                        Model.Search.SearchProgress.Step(search.Plan?.Climb == true, search.Stages.Count, search.Stage)
                    )
            : BestLoadoutsProgress(service) is { } best ? best.Text
            : PanelState.ComparisonShown && service.Comparison is { } comparison ? CompareCounter(comparison)
            : Counter(run, service);
        if (text == _counterShown)
            return;
        _counterShown = text;
        SideLine.OnLabel(_progress, text, TextStyle.Small);
    }

    // "42 of 147 · 1.8 s" while running, "147 of 147 done · 2.5 s" after.
    private static string Counter(Run? run, RunService service)
    {
        if (run == null)
            return Tr.Get("no runs yet");
        var total = run.Targets.Count;
        var seconds =
            run == service.Current && !run.IsFinal
                ? service.RealSeconds
                : ((run.EndedAt ?? run.StartedAt) - run.StartedAt).TotalSeconds;
        var time = run.IsFinal ? Took(run, seconds) : Words.Seconds(seconds.ToString("0.0", Tr.Culture));
        var counter = run.IsFinal
            ? Tr.Format("{0} of {1} done · {2}", [run.Done, total, time])
            : Tr.Format("{0} of {1} · {2}", [run.Done, total, time]);
        return run.Name != null ? Tr.Format("{0} · {1}", run.Name, counter) : counter;
    }

    /// <summary>
    /// Whether the performance line or its hover is showing, so refreshes must go on to update and hide them.
    /// </summary>
    public bool PerformanceShown => _perfHover.Visible;

    // The performance hover, rebuilt only when a new sample arrives or it was hidden. While a gear search runs the bar
    // shows the search, so the hover does too.
    private void ShowPerfHover(bool force)
    {
        var status = RunService.Instance.Status;
        var searching = RunService.Instance.Search is { IsActive: true };
        var history = searching ? status.SearchPerf ?? status.LastSearchPerf : status.CurrentPerf ?? status.LastPerf;
        var shown = (history, history?.Samples.Count ?? 0);
        if (force || shown != _perfShown)
        {
            _perfShown = shown;
            _perfHover.Reset();
            if (searching)
                PerfView.FillSearchHover(_perfHover.Content, status);
            else
                PerfView.FillHover(_perfHover.Content, status);
        }
        _perfHover.ShowAt(_perfMouse, _hoverOwner.GetGlobalRect());
    }
}
