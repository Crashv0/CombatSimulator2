using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.CombatSimulator2.Panel.Widgets;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// <c>cs2 open</c>, <c>close</c>, <c>select</c>, <c>pin</c>, <c>skin</c>, <c>motion</c>, <c>spoilers</c>, <c>scale</c> and <c>ui</c> (alias
/// <c>panel</c>): the modal for scripted checks.
/// </summary>
internal static class Cs2UiCommands
{
    /// <summary>What <c>cs2 ui</c> replies, for a bug report; set when the commands are added.</summary>
    public static Func<string> Text { get; private set; } = () => "";

    public static void Add(List<Subcommand> subs, Func<string> header)
    {
        Text = () => Cs2Modal.Describe(header()) + SearchLines();
        subs.Add(
            new(
                "open",
                "cs2 open [simulate|setup|settings|history] | cs2 open setup [page=<id>] [side=a|b] | cs2 open stats [a|b|both]",
                ["page", "side"],
                [],
                Open
            )
        );
        subs.Add(new("close", "cs2 close", [], [], (call, _) => Close(call), NeedsGame: false));
        subs.Add(new("select", "cs2 select <target>", [], [], Select));
        subs.Add(new("pin", "cs2 pin [<target>|clear]", [], [], Pin));
        subs.Add(new("skin", "cs2 skin [base|melvui]", [], [], Skin, NeedsGame: false));
        subs.Add(new("motion", "cs2 motion [full|reduced|off]", [], [], SetMotion, NeedsGame: false));
        subs.Add(new("spoilers", "cs2 spoilers [on|off]", [], [], SetSpoilers, NeedsGame: false));
        subs.Add(new("scale", "cs2 scale [70-150]", [], [], SetScale, NeedsGame: false));
        subs.Add(
            new(
                "ui",
                "cs2 ui",
                [],
                [],
                (call, _) => call.Ok(Cs2Modal.Describe(header()) + SearchLines()),
                NeedsGame: false
            )
        );
        subs.Add(
            new(
                "panel",
                "cs2 panel (alias of cs2 ui)",
                [],
                [],
                (call, _) => call.Ok(Cs2Modal.Describe(header()) + SearchLines()),
                NeedsGame: false
            )
        );
    }

    // The Setup tab on a page and side; side B is made from side A when it is asked for and there is none.
    private static void OpenSetup(CommandCall call, string page, string? side)
    {
        if (SetupPage.All.All(p => p.Id != page))
            throw new CommandException($"unknown page '{page}': {string.Join(", ", SetupPage.All.Select(p => p.Id))}");
        var letter = side switch
        {
            null => PanelState.Editing,
            "a" => SideLetter.A,
            "b" => SideLetter.B,
            _ => throw new CommandException($"unknown side '{side}': a or b"),
        };
        Cs2Modal.Open();
        Panel.SetupTab.SetupTabView.SetUp(letter);
        Panel.SetupTab.SetupTabView.ShowPage(page);
        call.Ok("ok open setup page=" + page + " side=" + (letter == SideLetter.A ? "a" : "b"));
    }

    private static void Open(CommandCall call, CommandArgs args)
    {
        var view = args.Word(0);
        if (view == Cs2Modal.Stats)
        {
            var sides = args.Word(1) ?? Panel.Stats.FullStatsView.Both;
            if (
                sides
                is not (
                    Panel.Stats.FullStatsView.Both
                    or Panel.Stats.FullStatsView.OnlyA
                    or Panel.Stats.FullStatsView.OnlyB
                )
            )
                throw new CommandException($"unknown sides '{sides}': a, b or both");
            Cs2Modal.Open();
            Panel.Stats.FullStatsView.Open(sides);
            call.Ok("ok open stats " + sides + " from " + Panel.Stats.FullStatsView.From);
            return;
        }
        if (view != null && view is not (Cs2Modal.Simulate or Cs2Modal.Setup or Cs2Modal.Settings or Cs2Modal.History))
            throw new CommandException($"unknown view '{view}': simulate, setup, settings, history or stats");
        if (view == Cs2Modal.Setup && (args.String("page") != null || args.String("side") != null))
        {
            OpenSetup(call, args.String("page") ?? SetupPage.Gear, args.String("side"));
            return;
        }
        Cs2Modal.Open(view);
        // Asked for by name, Settings shows from its first setting, not where the saved runs left it.
        if (view == Cs2Modal.Settings)
            Cs2Modal.SettingsFromTop();
        call.Ok("ok open " + PanelState.Prefs.View + (view == Cs2Modal.History ? " (saved runs)" : ""));
    }

    private static void Close(CommandCall call)
    {
        Cs2Modal.Close();
        call.Ok("ok closed" + (RunService.Instance.IsActive ? " (the run keeps going)" : ""));
    }

    // Reads or sets the window's skin, as the Settings menu does; a skin that can't be shown is saved and reported.
    private static void Skin(CommandCall call, CommandArgs args)
    {
        Skins.Refresh(findAgain: true);
        var skin = Skins.Current;
        if (args.Word(0) is { } word)
        {
            if (
                !word.Equals(SkinChoice.Base, StringComparison.OrdinalIgnoreCase)
                && !word.Equals(SkinChoice.MElvUI, StringComparison.OrdinalIgnoreCase)
            )
                throw new CommandException($"unknown skin '{word}': base or melvui");
            skin = Cs2Modal.SetSkin(word);
        }
        call.Ok(
            $"ok skin {skin.Chosen} shown {skin.Shown}" + (skin.Reason is { } reason ? $" ({reason.English()})" : "")
        );
    }

    private static void SetMotion(CommandCall call, CommandArgs args)
    {
        if (args.Word(0) is { } word)
        {
            if (!MotionTimes.TryParse(word.ToLowerInvariant(), out var level))
                throw new CommandException($"unknown level '{word}': full, reduced or off");
            MotionChoice.Set(level);
            Cs2Modal.RefreshSettings();
        }
        call.Ok("ok motion " + MotionTimes.Name(MotionChoice.Level));
    }

    // Spoiler guard on or off, and how many monsters and places it hides now.
    private static void SetSpoilers(CommandCall call, CommandArgs args)
    {
        if (args.Word(0) is { } word)
        {
            var on =
                word.Equals("on", StringComparison.OrdinalIgnoreCase) ? true
                : word.Equals("off", StringComparison.OrdinalIgnoreCase) ? false
                : throw new CommandException($"unknown state '{word}': on or off");
            SpoilerChoice.Set(on);
            Cs2Modal.RefreshSettings();
        }
        else
            SpoilerGuard.Refresh(LiveGame.Find());
        call.Ok($"ok spoilers {(PanelState.Prefs.SpoilerGuard ? "on" : "off")} hidden {Spoilers.Current.Count}");
    }

    private static void SetScale(CommandCall call, CommandArgs args)
    {
        if (args.Word(0) is { } word)
        {
            if (
                !double.TryParse(
                    word.TrimEnd('%'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var percent
                ) || !double.IsFinite(percent)
            )
                throw new CommandException($"not a scale: '{word}' (70 to 150)");
            WindowScale.Set(percent / 100);
            Cs2Modal.RefreshSettings();
        }
        call.Ok(
            $"ok scale {WindowScale.Percent(PanelState.Prefs.Scale)} cap {WindowScale.Percent(WindowScale.Cap)} drawn {WindowScale.Percent(WindowScale.Drawn)}"
        );
    }

    private static void Pin(CommandCall call, CommandArgs args)
    {
        if (args.Word(0) is not { } word)
        {
            call.Ok($"shown {Keys(PanelPins.ShownPins)}\nwaiting {Keys(PanelPins.WaitingPins)}");
            return;
        }
        if (word.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            PanelPins.ClearPins();
        }
        else
        {
            var key = Targets.Resolve(LiveGame.Require(), word).Key;
            if (RunService.Instance.Current is not { } run || !run.Results.ContainsKey(key))
                throw new CommandException($"{Ids.BriefKey(key)} isn't in the run shown");
            PanelPins.TogglePin(key);
        }
        call.Ok("ok pins " + string.Join(",", PanelState.Prefs.Pins.Select(Ids.BriefKey)));
    }

    // Selects a target as a click on its row does; the list's rows can't be clicked by their text.
    private static void Select(CommandCall call, CommandArgs args)
    {
        var word = args.Word(0) ?? throw new CommandException("usage: cs2 select <target>");
        var key = Targets.Resolve(LiveGame.Require(), word).Key;
        PanelState.Select(key);
        call.Ok("ok selected " + Ids.BriefKey(key));
    }

    private static string Keys(IReadOnlyList<string> pins) =>
        pins.Count == 0 ? "none" : string.Join(' ', pins.Select(Ids.BriefKey));

    // The search as cs2 ui prints it: how it stands, then what its answer says, a line each; nothing before the
    // session's first search.
    private static string SearchLines()
    {
        if (RunService.Instance.Search is not { } search)
            return "";
        var text = new System.Text.StringBuilder();
        var answer = search.Answer;
        var state =
            answer == null
                ? (search.Stage == 0 ? "planning" : "fighting")
                : answer.State switch
                {
                    Model.Search.AnswerState.Cancelled => "cancelled",
                    Model.Search.AnswerState.Failed => "failed",
                    _ => "done",
                };
        text.Append(
            System.Globalization.CultureInfo.InvariantCulture,
            $"\ngear-search: {state} target={Model.Ids.BriefKey(search.Request.Target)} stage={search.Stage} left={search.Left}"
        );
        if (answer is not { Finished: true })
            return text.ToString();
        if (answer.Kind == Model.Search.SearchKind.Droptimizer)
            text.Append(
                System.Globalization.CultureInfo.InvariantCulture,
                $"\ndroptimizer: gains={answer.Gains.Count} no-gain={answer.NoGain} not-yet={answer.NotYetWearable.Count}"
            );
        else
            text.Append(
                System.Globalization.CultureInfo.InvariantCulture,
                $"\ngear-answer: {GearAnswer(answer.State)} gain={(answer.Best?.Percent is { } gain ? (gain.Point * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%" : "-")} changed={answer.Best?.Slots.Count ?? 0} ties={answer.Ties.Count}"
            );
        return text.ToString();
    }

    private static string GearAnswer(Model.Search.AnswerState state) =>
        state switch
        {
            Model.Search.AnswerState.Found => "found",
            Model.Search.AnswerState.KeepCurrent => "keep-current",
            Model.Search.AnswerState.NoneWithinLimit => "none-within-limit",
            Model.Search.AnswerState.NoneKills => "none-kills",
            _ => "nothing-to-search",
        };
}
