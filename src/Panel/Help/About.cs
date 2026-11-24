using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// The About window over CS2's dimmed content: the version and the game build it was checked with, the Guide and
/// the Tour, what the loaded version changed, time spent with CS2, where to find it online and what to know about
/// its results. Its body scrolls when it doesn't fit; the title and Close stay visible.
/// </summary>
internal sealed class About
{
    public const float Width = 940;
    private const float Margin = 16;
    private const float CardHeight = 78;

    private static readonly Color NewFill = Color.FromHtml("#22602b");

    private readonly Control _layer;
    private readonly ColorRect _dim;
    private readonly PanelContainer _window;
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _body;
    private readonly List<Button> _ring = [];
    private readonly AboutUsage _usage = new();

    public About(Control layer, Action close, Action openGuide, Action startTour)
    {
        _layer = layer;
        _dim = Ui.Add(layer, HelpLayer.Dim("Cs2AboutDim"));
        _window = Ui.Add(layer, Look.Panel(Art.FrameBox, "Cs2AboutWindow"));
        _window.MouseFilter = Control.MouseFilterEnum.Stop;
        var column = Ui.Column(12);
        _window.AddChild(Ui.Margin(column, 16, 12, 16, 14));

        var top = Ui.Add(column, Ui.Row(8));
        var title = Ui.Add(top, Look.Panel(Art.RibbonBox, "Cs2AboutTitle"));
        title.CustomMinimumSize = new Vector2(360, 0);
        title.AddChild(Ui.Label(Tr.Get("About"), TextStyle.Title));
        top.AddChild(Ui.Spacer());
        _ring.Add(Ui.Add(top, Ui.CloseButton("Cs2AboutClose", close)));

        _scroll = Ui.Add(
            column,
            new ScrollContainer
            {
                Name = "Cs2AboutBody",
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            }
        );
        _body = Ui.Column(18);
        _body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var inset = Ui.Add(_scroll, Ui.Margin(_body, 12, 6, 12, 6));
        inset.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        _body.AddChild(Identity());
        var columns = Ui.Add(_body, Ui.Row(24, "Cs2AboutColumns"));
        var left = Ui.Add(columns, Ui.Column(20));
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // 478 to 380 at 100%: the links' lines each fit on one line.
        left.SizeFlagsStretchRatio = 478f / 380;
        left.AddChild(Learn(openGuide, startTour));
        left.AddChild(News());
        var right = Ui.Add(columns, Ui.Column(20));
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.AddChild(_usage.Root);
        right.AddChild(AboutLinks.Make(_ring));
        if (AboutCredits.Make() is { } credits)
            right.AddChild(credits);
        _body.AddChild(Disclaimer());

        var footer = Ui.Add(column, Ui.Row(16, "Cs2AboutFooter"));
        footer.AddChild(
            Ui.Label(
                Tr.Get("Unofficial fan mod, not affiliated with or endorsed by Games by Malcs or Jagex."),
                TextStyle.Small,
                "Cs2AboutRights",
                wrap: true
            )
        );
        var done = Ui.Add(footer, Ui.Button(Tr.Get("Close"), Look.Button8, "Cs2AboutDone", close));
        _ring.Add(done);

        // Keyboard focus goes round the window's buttons and stays in the window.
        for (var i = 0; i < _ring.Count; i++)
        {
            var button = _ring[i];
            var next = _ring[(i + 1) % _ring.Count].GetPath();
            var previous = _ring[(i + _ring.Count - 1) % _ring.Count].GetPath();
            button.FocusMode = Control.FocusModeEnum.All;
            button.FocusNext = next;
            button.FocusPrevious = previous;
            button.FocusNeighborRight = next;
            button.FocusNeighborBottom = next;
            button.FocusNeighborLeft = previous;
            button.FocusNeighborTop = previous;
        }
        done.CallDeferred(Control.MethodName.GrabFocus);
        Layout();
    }

    private HBoxContainer Identity()
    {
        var row = Ui.Row(16, "Cs2AboutIdentity");
        var mark = Ui.Add(row, Look.Panel(Art.RowBox, "Cs2AboutMark"));
        mark.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        mark.AddChild(Ui.Picture(AboutIcons.Mark ?? Art.File(Art.CombatPath) ?? Art.Missing, 48));

        var text = Ui.Add(row, Ui.Column(2));
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AddChild(Ui.Label(Tr.Get("Combat Simulator 2"), TextStyle.Title, "Cs2AboutName"));
        // Translators: {0} is CS2's version number; Crash is the author's name.
        text.AddChild(
            Ui.Label(Tr.Format("Version {0}  ·  by Crash", Usage.Version), TextStyle.Body, "Cs2AboutVersion")
        );
        var build = Ui.Add(text, Ui.Row(6, "Cs2AboutBuild"));
        var listed = !LiveGame.Checked.Shows;
        object?[] game = [LiveGame.GameVersion, LiveGame.SteamBuild];
        if (listed)
        {
            build.AddChild(
                AboutParts.Tinted(
                    // Translators: {0} is the game's version, {1} its build number.
                    Tr.Format("Checked with Melvor Idle 2 {0} (build {1})", game),
                    TextStyle.Small,
                    Look.Safe,
                    "Cs2AboutChecked"
                )
            );
        }
        else
        {
            build.AddChild(
                AboutParts.Tinted(
                    // Translators: {0} is the game's version, {1} its build number.
                    Tr.Format("Not checked with Melvor Idle 2 {0} (build {1}). Results may be wrong.", game),
                    TextStyle.Small,
                    Look.Risky,
                    "Cs2AboutUnchecked"
                )
            );
        }

        var report = Ui.Add(
            row,
            Ui.Button(Tr.Get("Save bug report"), Look.Button8, "Cs2AboutReport", Report.ReportNote.Save)
        );
        report.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        report.TooltipText = Tr.Get(
            "Zips CS2's logs, settings and recent runs into mods/CombatSimulator2/reports, to send with a bug report."
        );
        _ring.Add(report);
        return row;
    }

    private VBoxContainer Learn(Action openGuide, Action startTour)
    {
        var section = Ui.Column(8, "Cs2AboutLearn");
        var head = Ui.Add(section, Ui.Row(8));
        head.AddChild(AboutParts.Heading(Tr.Get("Info")));
        head.AddChild(Ui.Spacer());
        var spoilers = Ui.Add(
            head,
            Cs2Check.Make("Cs2AboutSpoilers", Tr.Get("Spoiler guard"), PanelState.Prefs.SpoilerGuard, SpoilerChoice.Set)
        );
        Cs2Check.Small(spoilers, 18);
        spoilers.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        spoilers.TooltipText = Tr.Get(
            "Monsters you haven't killed and places you can't enter yet show as ???. Results stay the same."
        );
        _ring.Add(spoilers);
        var cards = Ui.Add(section, Ui.Row(8));
        cards.AddChild(
            Card(
                "Cs2AboutGuide",
                Art.GuideBookPath,
                HelpText.Title,
                Tr.Get("What CS2 does and how sure a result is"),
                openGuide
            )
        );
        cards.AddChild(
            Card(
                "Cs2AboutTour",
                Art.TourPath,
                Tr.Get("Tour"),
                Tr.Plural("{0} step around the window", "{0} steps around the window", Tour.Steps, Tour.Steps),
                startTour
            )
        );
        return section;
    }

    // A button with a picture, a name and a line under it.
    private Button Card(string name, string icon, string title, string line, Action pressed)
    {
        var card = Ui.Button("", Look.Button8, name, pressed);
        card.CustomMinimumSize = new Vector2(0, CardHeight);
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var row = Ui.Row(12);
        row.AddChild(Ui.Picture(Art.File(icon) ?? Art.Missing, 32));
        var text = Ui.Add(row, Ui.Column(0));
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        text.AddChild(Ui.Label(title, TextStyle.BodyStrong));
        var under = Ui.Add(text, Ui.Label(line, TextStyle.Small, wrap: true));
        under.MaxLinesVisible = 2;
        AboutParts.Cover(card, Ui.Margin(row, 14, 4, 14, 10));
        _ring.Add(card);
        return card;
    }

    private VBoxContainer News()
    {
        var section = Ui.Column(8, "Cs2AboutNews");
        var lines = VersionNews.For(Usage.Version);
        if (lines.Count > 0)
        {
            var head = Ui.Add(section, Ui.Row(8));
            // Translators: {0} is CS2's version number.
            head.AddChild(AboutParts.Heading(Tr.Format("What's new in {0}", Usage.Version)));
            head.AddChild(Ui.Spacer());
            head.AddChild(AboutLinks.TextLink(_ring, "Cs2AboutChanges", Tr.Get("All changes"), Links.Changes));
            var box = Ui.Add(section, Look.Panel(Art.RowBox, "Cs2AboutChangesList"));
            var list = Ui.Column(10);
            box.AddChild(Ui.Margin(list, 8, 6, 8, 6));
            for (var i = 0; i < lines.Count; i++)
            {
                var row = Ui.Add(list, Ui.Row(10, "Cs2AboutChange" + (i + 1)));
                row.AddChild(Kind(lines[i].Fixed));
                row.AddChild(Ui.Label(lines[i].Text, TextStyle.Body, wrap: true));
            }
        }
        var notify = Ui.Add(
            section,
            Cs2Check.Make(
                "Cs2AboutNotify",
                Tr.Get("Update notifications"),
                PanelState.Prefs.UpdateNotifications,
                Usage.SetUpdateNotifications
            )
        );
        Cs2Check.Small(notify, 18);
        notify.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        notify.TooltipText = Tr.Get("After an update, a red dot on the About button until About is opened.");
        return section;
    }

    // "New" in light text on green, "Fixed" quieter on a neutral wash: neither is a colour on its own colour.
    private static PanelContainer Kind(bool isFix)
    {
        var chip = new PanelContainer
        {
            Name = isFix ? "Cs2AboutFixed" : "Cs2AboutNew",
            CustomMinimumSize = new Vector2(52, 0),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var box = new StyleBoxFlat
        {
            BgColor = isFix ? new Color(1, 1, 1, 0.08f) : NewFill,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 1,
            ContentMarginBottom = 1,
        };
        PixelBox.Panel(chip, box);
        var label = Ui.Add(
            chip,
            isFix
                // Translators: Marks a change that fixed something, in the list of a version's changes.
                ? Ui.Label(Tr.Get("Fixed", "change"), TextStyle.Tiny)
                // Translators: Marks a change that added something, in the list of a version's changes.
                : AboutParts.Tinted(Tr.Get("New", "change"), TextStyle.Tiny, Look.Style(TextStyle.Title).FontColor)
        );
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return chip;
    }

    private static VBoxContainer Disclaimer()
    {
        var section = Ui.Column(8, "Cs2AboutDisclaimer");
        section.AddChild(AboutParts.Heading(Tr.Get("Disclaimer")));
        var grid = Ui.Add(section, new GridContainer { Columns = 2 });
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        (string Icon, string Text)[] notes =
        [
            (AboutIcons.Assisted, Tr.Get("Built with AI assistance.")),
            (AboutIcons.Estimate, Tr.Get("Results are estimates from simulated fights; real play can differ.")),
            (AboutIcons.Safe, Tr.Get("CS2 never changes or edits your game.")),
            (AboutIcons.Local, Tr.Get("Nothing is sent online.")),
        ];
        foreach (var (icon, text) in notes)
        {
            var note = Ui.Add(grid, Look.Panel(Art.RowBox, "Cs2AboutNote_" + icon));
            note.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var row = Ui.Add(note, Ui.Row(10));
            row.AddChild(Ui.Picture(AboutIcons.Get(icon), 18));
            row.AddChild(Ui.Label(text, TextStyle.Small, wrap: true));
        }
        return section;
    }

    /// <summary>
    /// Centres the window in the layer, at most as tall as the layer allows; the body scrolls beyond that.
    /// </summary>
    public void Layout()
    {
        _usage.OnFrame();
        var room = _layer.Size;
        _dim.Position = Vector2.Zero;
        _dim.Size = room;
        var width = Math.Min(Width, Math.Max(320, room.X - 2 * Margin));
        _scroll.CustomMinimumSize = Vector2.Zero;
        var chrome = _window.GetCombinedMinimumSize().Y;
        var bodyHeight = _scroll.GetChild<Control>(0).GetCombinedMinimumSize().Y;
        var height = Math.Min(chrome + bodyHeight, Math.Max(chrome, room.Y - 2 * Margin));
        _scroll.CustomMinimumSize = new Vector2(0, Math.Max(0, height - chrome));
        _window.Size = new Vector2(width, height);
        _window.Position = ((room - _window.Size) / 2).Round();
    }
}
