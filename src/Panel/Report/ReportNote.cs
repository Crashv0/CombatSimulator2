using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Panel.Report;

/// <summary>
/// The note shown after saving a bug report from the panel: the zip's name and what is in it, or why it couldn't be
/// saved, with the reports folder a click away.
/// </summary>
internal static class ReportNote
{
    private static ModModal? _modal;
    private static ReportResult? _result;
    private static string? _error;

    internal static void Register(ModContext context) =>
        _modal = context.Modals.Register("Cs2Report", new Vector2(760, 640), Build);

    internal static void Reset()
    {
        _modal = null;
        _result = null;
        _error = null;
    }

    /// <summary>Saves a report and shows the note when it is done.</summary>
    public static void Save() => RunService.Instance.SaveReport(PanelState.Prefs.ReportIncludesSave, Show);

    private static void Show(ReportResult? result, string? error)
    {
        _result = result;
        _error = error;
        if (_modal == null)
            return;
        _modal.Scale = WindowScale.DrawnScale;
        _modal.Rebuild();
        _modal.Open();
    }

    private static void OpenFolder()
    {
        if (_result is { Folder: var folder } && Directory.Exists(folder))
            OS.ShellOpen(folder);
    }

    private static Control Build()
    {
        var root = Tr.Adopt(Look.Panel(Art.FrameBox, "Cs2ReportNote"));
        Dress(root);
        // The note fills its window, up to its design size; on a screen with less room its text scrolls, wrapped to
        // the width there is.
        var scroll = new ScrollContainer
        {
            Name = "Cs2ReportScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        root.AddChild(Ui.Margin(scroll, 24, 18, 24, 20));
        var column = Ui.Add(scroll, Ui.Column(10));
        column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var top = Ui.Add(column, Ui.Row(8));
        top.AddChild(
            Ui.Label(
                _result != null ? Tr.Get("Bug report saved") : Tr.Get("Couldn't save the bug report"),
                TextStyle.Title
            )
        );
        top.AddChild(Ui.Spacer());
        top.AddChild(Ui.CloseButton("Cs2ReportClose", () => _modal?.Close()));
        if (_result is not { } result)
        {
            column.AddChild(Ui.Label(_error ?? "", TextStyle.Body, "Cs2ReportError", wrap: true));
            return root;
        }
        column.AddChild(
            Ui.Label(
                Tr.Format("{0}, in mods/CombatSimulator2/reports in the game's folder.", result.File),
                TextStyle.Body,
                "Cs2ReportFile",
                wrap: true
            )
        );
        column.AddChild(
            Ui.Label(
                result.Entries.Any(e => e.StartsWith("save/", StringComparison.Ordinal))
                    ? Tr.Get(
                        "It holds CS2's logs, settings and recent runs, with your user and computer names taken out, and your character's save, so the developer can run what you ran."
                    )
                    : Tr.Get(
                        "It holds CS2's logs, settings and recent runs, with your user and computer names taken out. Your save isn't in it."
                    ),
                TextStyle.Small,
                wrap: true
            )
        );
        column.AddChild(Ui.Label(string.Join("\n", Listed(result.Entries)), TextStyle.Small, "Cs2ReportEntries"));
        Sending(column, result);
        return root;
    }

    // The zip's entries, with CS2's logs as one line where the first is (a restart adds each log's previous file), the
    // snapshots of failed runs as one line (their file names are run ids) and the panel's state by what it is: short
    // enough that the ways to send it stay in sight under the list.
    private static List<string> Listed(IReadOnlyList<string> entries)
    {
        // Translators: In the list of what a saved bug report holds: CS2's log files, as one line.
        var logs = Tr.Get("CS2's logs");
        var listed = entries
            .Where(e => !e.StartsWith("snapshots/", StringComparison.Ordinal))
            .Select(e =>
                e.StartsWith("logs/", StringComparison.Ordinal) ? logs
                : e == "panel.txt" ? Tr.Get("What the panel showed")
                : e
            )
            .Distinct()
            .ToList();
        if (entries.Any(e => e.StartsWith("snapshots/", StringComparison.Ordinal)))
            listed.Add(Tr.Get("Snapshots of failed runs"));
        return listed;
    }

    // Discord first, where players already are; GitHub once CS2 has a repository. The text and the link are shown too,
    // to copy by hand if the clipboard or the browser fails.
    private static void Sending(VBoxContainer column, ReportResult result)
    {
        var discord = ReportText.Discord(result.Summary);
        var link = ReportText.IssueLink(result.Summary, ReportLinks.Repository);
        var copied = Ui.Label("", TextStyle.Small, "Cs2ReportCopied", wrap: true);
        copied.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var channel = Ui.Button(
            Tr.Get("Open the channel"),
            Look.Button4,
            "Cs2ReportChannel",
            () => OS.ShellOpen(ReportLinks.DiscordChannel)
        );
        channel.Visible = false;
        // Before any button: posting the zip is the player's choice, and where it goes decides who can read it.
        var privacy = Ui.Add(column, Look.Panel(Art.RowBox, "Cs2ReportPrivateNote"));
        privacy.AddChild(
            Ui.Label(
                Tr.Get(
                    "GitHub issues and the Discord channel are public. To keep your save private, send the zip to Crashv in a direct message on Discord."
                ),
                TextStyle.Small,
                wrap: true
            )
        );
        // Four buttons outgrow the note at large scales: they wrap rather than widen it.
        var buttons = Ui.Add(column, new HFlowContainer());
        buttons.AddThemeConstantOverride("h_separation", 8);
        buttons.AddThemeConstantOverride("v_separation", 8);
        buttons.AddChild(
            Ui.Button(
                Tr.Get("Copy for Discord"),
                Look.Button8,
                "Cs2ReportDiscord",
                () =>
                {
                    DisplayServer.ClipboardSet(discord);
                    copied.Text = Tr.Get(
                        "Copied. Paste it in the Melvor Discord's mod bug reports channel, then drag the zip in after it."
                    );
                    channel.Visible = true;
                }
            )
        );
        var github = Ui.Button(
            Tr.Get("Open a GitHub issue"),
            Look.Button8,
            "Cs2ReportGitHub",
            () =>
            {
                if (link == null)
                    return;
                OS.ShellOpen(link);
                copied.Text = Tr.Get(
                    "Opened in your browser. Say what happened there, then drag the zip into the Report zip box."
                );
                channel.Visible = false;
            }
        );
        github.Disabled = link == null;
        if (link == null)
            github.TooltipText = Tr.Get("CS2's GitHub page isn't up yet");
        buttons.AddChild(github);
        buttons.AddChild(
            Ui.Button(
                // Translators: A private message on Discord to CS2's developer.
                Tr.Get("Direct message"),
                Look.Button8,
                "Cs2ReportPrivate",
                () =>
                {
                    OS.ShellOpen(ReportLinks.DirectMessage);
                    copied.Text = Tr.Get("Opened Discord. Send Crashv the zip there, with what happened.");
                    channel.Visible = false;
                }
            )
        );
        buttons.AddChild(Ui.Button(Tr.Get("Open folder"), Look.Button8, "Cs2ReportFolder", OpenFolder));
        var confirm = Ui.Add(column, Ui.Row(8));
        confirm.AddChild(copied);
        confirm.AddChild(channel);
        column.AddChild(
            new TextEdit
            {
                Name = "Cs2ReportDiscordText",
                Text = discord,
                Editable = false,
                CustomMinimumSize = new Vector2(0, 140),
                WrapMode = TextEdit.LineWrappingMode.Boundary,
            }
        );
        if (link != null)
            column.AddChild(
                new LineEdit
                {
                    Name = "Cs2ReportLink",
                    Text = link,
                    Editable = false,
                }
            );
    }

    /// <summary>
    /// Dresses the note as CS2's main window: the skin's theme, and a solid backdrop inside the game's
    /// see-through frame. Call before it enters the tree.
    /// </summary>
    private static void Dress(Control root)
    {
        if (Look.IsGameLook)
            root.AddToGroup(Skins.SkipGroup);
        root.Theme = Cs2Modal.WindowTheme();
        root.AddChild(
            new ColorRect
            {
                Name = "Backdrop",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Color = Look.IsGameLook ? Color.FromHtml("#1d1b18") : Color.FromHtml("#0c0c0c"),
            }
        );
    }
}
