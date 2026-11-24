using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// The About window's links: each opens in the browser when pressed and never before, and one without an address
/// shows switched off.
/// </summary>
internal static class AboutLinks
{
    private const float LinkHeight = 34;

    // Each row's node name and address as last built, so a check reads what the rows open without opening them.
    private static readonly List<(string Name, string Address)> _built = [];

    /// <summary>The links as built, for <c>cs2 ui</c>: <c>about_links=name:address …</c>, <c>off</c> for none.</summary>
    public static string Describe() =>
        "about_links=" + string.Join(' ', _built.Select(l => $"{l.Name}:{(l.Address.Length > 0 ? l.Address : "off")}"));

    /// <summary>The Links section; its buttons are added to <paramref name="ring"/>, the window's focus order.</summary>
    public static VBoxContainer Make(List<Button> ring)
    {
        _built.Clear();
        var section = Ui.Column(8, "Cs2AboutLinks");
        section.AddChild(AboutParts.Heading(Tr.Get("Links")));
        var box = Ui.Add(section, Look.Panel(Art.RowBox, "Cs2AboutLinkRows"));
        var rows = Ui.Column(0);
        box.AddChild(Ui.Margin(rows, 0, 4, 0, 4));
        rows.AddChild(
            LinkRow(
                ring,
                "Cs2AboutGitHub",
                AboutIcons.Get(AboutIcons.Code),
                "GitHub",
                Tr.Get("Source and changelog"),
                Links.GitHub
            )
        );
        rows.AddChild(
            LinkRow(
                ring,
                "Cs2AboutFeedback",
                AboutIcons.Get(AboutIcons.Report),
                Tr.Get("Send feedback"),
                Tr.Get("Ideas or issues that aren't bugs"),
                Links.Feedback(Usage.Version, LiveGame.Checked)
            )
        );
        rows.AddChild(
            LinkRow(
                ring,
                "Cs2AboutReportBug",
                AboutIcons.Get(AboutIcons.Report),
                Tr.Get("Report a bug"),
                Tr.Get("New issue on GitHub"),
                Links.ReportBug
            )
        );
        rows.AddChild(
            LinkRow(
                ring,
                "Cs2AboutDiscord",
                Art.File(Art.DiscordPath) ?? Art.Missing,
                "Discord",
                Tr.Get("Questions, comments, and reports"),
                Links.Discord
            )
        );
        rows.AddChild(
            LinkRow(
                ring,
                "Cs2AboutPrivate",
                Art.File(Art.DiscordPath) ?? Art.Missing,
                // Translators: A private message on Discord to CS2's developer.
                Tr.Get("Direct message"),
                Tr.Get("Send private bug report"),
                ReportLinks.DirectMessage
            )
        );
        rows.AddChild(
            LinkRow(
                ring,
                "Cs2AboutNexusMods",
                Art.File(Art.ModsPath) ?? Art.Missing,
                "Nexus Mods",
                Tr.Get("Main page and updates"),
                Links.NexusMods
            )
        );
        rows.AddChild(
            LinkRow(
                ring,
                "Cs2AboutTranslate",
                AboutIcons.Get(AboutIcons.Globe),
                // Translators: Opens the project where players translate CS2.
                Tr.Get("Translate"),
                Tr.Get("Community translations on Crowdin"),
                Links.Translate
            )
        );
        return section;
    }

    // One link: opened in the browser when pressed, never before. Without an address it shows, switched off.
    private static Button LinkRow(
        List<Button> ring,
        string name,
        Texture2D? icon,
        string title,
        string line,
        string address
    )
    {
        _built.Add((name, address));
        var link = Ui.Button("", "", name, () => OS.ShellOpen(address));
        link.Flat = true;
        link.CustomMinimumSize = new Vector2(0, LinkHeight);
        var row = Ui.Row(10);
        row.AddChild(Ui.Picture(icon, 20));
        row.AddChild(Ui.Label(title, TextStyle.Body));
        row.AddChild(Ui.Clipped(line, TextStyle.Tiny));
        row.AddChild(Ui.Label("↗", TextStyle.Small));
        AboutParts.Cover(link, Ui.Margin(row, 8, 0, 8, 0));
        Switch(link, row, address);
        ring.Add(link);
        return link;
    }

    /// <summary>A link shown as its text alone.</summary>
    public static Button TextLink(List<Button> ring, string name, string text, string address)
    {
        var link = Ui.Button("", "", name, () => OS.ShellOpen(address));
        link.Flat = true;
        var label = Ui.Label(text + " ↗", TextStyle.Small);
        link.CustomMinimumSize = label.GetCombinedMinimumSize() + new Vector2(8, 4);
        link.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        AboutParts.Cover(link, label);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        Switch(link, label, address);
        ring.Add(link);
        return link;
    }

    private static void Switch(Button link, Control content, string address)
    {
        if (address.Length > 0)
        {
            link.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            link.TooltipText = address;
            return;
        }
        link.Disabled = true;
        link.FocusMode = Control.FocusModeEnum.None;
        link.TooltipText = Tr.Get("Not available yet");
        content.Modulate = new Color(1, 1, 1, 0.4f);
    }
}
