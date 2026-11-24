using System.Globalization;
using System.Text;
using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// The targets the filter shows, as portraits grouped by area with the dungeons as one group, in one drawn control:
/// a portrait toggles its target's ignore, an area's name the whole area's, and the mark after the name folds the
/// area. Its height is bounded and it scrolls inside that, so every target of the build stays one node. The header
/// "Targets n of m" opens and closes it; it starts closed, stays as the player leaves it, and closes when a run starts.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class TargetStrip
{
    private const string DungeonsId = "dungeons";
    private const string MastersId = "masters";
    private const float Portrait = 36;
    private const float Gap = 4;
    private const float AreaGap = 22;
    private const float LineGap = 10;
    private const float MaxHeight = 230;

    // An area as drawn: its targets, and where its name, fold mark and portraits sit on the canvas.
    private sealed class Area(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public List<TargetFact> Targets { get; } = [];
        public Rect2 NameRect { get; set; }
        public Rect2 FoldRect { get; set; }
        public List<Rect2> Portraits { get; } = [];
    }

    private readonly PanelContainer _root;
    private readonly PanelContainer _header;
    private readonly Label _headerText;
    private readonly TextureRect _headerArrow;
    private readonly Button _reset;
    private readonly Button _ignoreAll;
    private readonly Button _returnAll;
    private readonly ScrollContainer _scroll;
    private readonly Control _canvas;
    private readonly List<Area> _areas = [];
    private readonly Dictionary<string, Texture2D?> _art = new(StringComparer.Ordinal);

    // Folded areas, for the session: the strip opens with every area open.
    private readonly HashSet<string> _folded = new(StringComparer.Ordinal);
    private TargetSelection? _selection;
    private readonly FightWatch _fight = new();
    private readonly KeepFightButton _keep;
    private float _laidOutWidth = -1;

    /// <summary>
    /// The strip, with the search of <paramref name="filters"/> leading its head and its "Targets n of m" header at the
    /// end of their row.
    /// </summary>
    public TargetStrip(TargetFilters filters)
    {
        var search = filters.Search;
        (_header, _headerText, _headerArrow, _reset) = MakeHeader();
        filters.Root.AddChild(_header);
        _root = Look.Panel(Art.RowBox, "Cs2StripPanel");
        var column = Ui.Add(_root, Ui.Column(6));
        var head = Ui.Add(column, Ui.Row(10));
        search.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _fight.Changed += () => Show(_selection);
        _keep = new KeepFightButton(_fight);
        head.AddChild(_keep.Root);
        head.AddChild(search);
        var hint = Ui.Label(
            Tr.Get(
                "Click a target to ignore it in the next run, an area's name for the whole area. Right-click to run only it, then to add more."
            ),
            TextStyle.Small,
            wrap: true
        );
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(hint);
        // Every target the filters show, so a run can start from one or two returned.
        _ignoreAll = Ui.Button(
            Tr.Get("Ignore all"),
            Look.Button4,
            "Cs2StripIgnoreAll",
            () => PanelState.SetIgnored([.. _selection?.Shown.Select(t => t.Key) ?? []], true)
        );
        _ignoreAll.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_ignoreAll);
        _returnAll = Ui.Button(Tr.Get("Return all"), Look.Button4, "Cs2StripReturnAll", PanelState.BringAllBack);
        _returnAll.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_returnAll);
        _scroll = new ScrollContainer
        {
            Name = "Cs2StripScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        // The portraits sit in the game's inset panel so the strip reads as a window of its own; they fade out at an end
        // with more past it, into the panel behind them.
        var well = Ui.Add(column, Look.Panel(Art.SideBox, "Cs2StripWell"));
        well.AddChild(ScrollFade.Wrap(_scroll));
        _canvas = new Control
        {
            Name = "Cs2TargetStrip",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _canvas.Draw += Paint;
        _canvas.Resized += Layout;
        _canvas.GuiInput += Input;
        _canvas.SetMeta(InstantTooltips.Live, true);
        _canvas.ThemeChanged += Relayout;
        _scroll.AddChild(_canvas);
        SetOpen(false);
    }

    /// <summary>The strip with its search, hint, Ignore all and Return all.</summary>
    public Control Root => _root;

    // Drawn as the game's foldable sections are (Smithing's recipe groups): their textured bar, the title, and the arrow
    // that says whether it is open. A click anywhere on it folds or unfolds the strip.
    private (PanelContainer, Label, TextureRect, Button) MakeHeader()
    {
        var header = Look.Panel(Art.SectionBox, "Cs2StripHeader");
        // As short as the toolbar's buttons beside it.
        var box = (StyleBox)header.GetThemeStylebox("panel").Duplicate();
        box.ContentMarginTop = box.ContentMarginBottom = 3;
        PixelBox.Panel(header, box);
        header.MouseFilter = Control.MouseFilterEnum.Stop;
        header.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        header.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var row = Ui.Add(header, Ui.Row(10));
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        var text = Ui.Label("", TextStyle.BodyStrong);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(text);
        var arrow = Ui.Picture(null, 16);
        arrow.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(arrow);
        // Every target back and the search emptied, without opening the strip: a button takes its own click.
        var reset = Ui.Button("✕", "", "Cs2StripReset", Reset);
        reset.Flat = true;
        reset.FocusMode = Control.FocusModeEnum.None;
        reset.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        reset.TooltipText = Tr.Get("Return every target and clear the search");
        reset.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        row.AddChild(reset);
        header.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                SetOpen(!Open);
                header.AcceptEvent();
            }
        };
        header.MouseEntered += () => header.Modulate = new Color(1.15f, 1.15f, 1.15f);
        header.MouseExited += () => header.Modulate = Colors.White;
        return (header, text, arrow, reset);
    }

    private static void Reset()
    {
        PanelState.BringAllBack();
        if (PanelState.Prefs.Filter.Search.Length > 0)
            PanelState.SetFilter(PanelState.Prefs.Filter with { Search = "" });
    }

    public bool Open { get; private set; }

    /// <summary>Opens or closes the strip: closed at first, and when a run starts.</summary>
    public void SetOpen(bool open)
    {
        Open = open;
        Motion.Reveal(_root, open, MotionKind.Strip);
        ShowHeader();
    }

    /// <summary>Draws the filter's targets and their ignores; nothing to draw until targets are checked.</summary>
    public void Show(TargetSelection? selection)
    {
        _selection = selection;
        _areas.Clear();
        if (selection != null)
        {
            var byId = new Dictionary<string, Area>(StringComparer.Ordinal);
            Area? dungeons = null;
            Area? masters = null;
            foreach (var target in selection.Shown)
            {
                Area area;
                if (target.Tags.HasFlag(TargetTags.Dungeons))
                    area = dungeons ??= new Area(DungeonsId, Tr.Get("Dungeons"));
                else if (target.Tags.HasFlag(TargetTags.Masters))
                    area = masters ??= new Area(MastersId, Tr.Get("Slayer masters"));
                else if (!byId.TryGetValue(target.Area, out area!))
                {
                    area = byId[target.Area] = new Area(target.Area, target.AreaName);
                    _areas.Add(area);
                }
                area.Targets.Add(target);
            }
            if (dungeons != null)
                _areas.Add(dungeons);
            if (masters != null)
                _areas.Add(masters);
            AddFighting(selection);
        }
        _keep.Show(selection);
        _ignoreAll.Disabled = selection is not { ToRun.Count: > 0 };
        _returnAll.Disabled = PanelState.Ignored.Count == 0;
        ShowHeader();
        Relayout();
    }

    private void ShowHeader()
    {
        _headerText.Text = _selection is { } s
            ? Tr.Format("{0} of {1} targets", [s.ToRun.Count, s.Shown.Count])
            : Tr.Get("Targets");
        _headerArrow.Texture = Art.File(Open ? Art.OpenArrowPath : Art.ClosedArrowPath);
        // Only when there is something to reset.
        _reset.Visible = PanelState.Ignored.Count > 0 || PanelState.Prefs.Filter.Search.Length > 0;
    }

    private void Relayout()
    {
        _laidOutWidth = -1;
        Layout();
    }

    // Areas flow left to right and wrap; an area wider than the strip wraps its own portraits. The scroll stays as
    // tall as what is drawn, up to its bound.
    private void Layout()
    {
        var width = _canvas.Size.X;
        if (width <= 0 || Mathf.IsEqualApprox(width, _laidOutWidth))
        {
            _canvas.QueueRedraw();
            return;
        }
        _laidOutWidth = width;
        var font = Look.Font(400);
        var size = 13;
        var nameLine = size + 8f;
        var foldWidth = font.GetStringSize(" ▾", HorizontalAlignment.Left, -1, size).X + 4;
        var perLine = Math.Max(1, (int)((width + Gap) / (Portrait + Gap)));
        float x = 0;
        float y = 0;
        float lineHeight = 0;
        foreach (var area in _areas)
        {
            var nameWidth = font.GetStringSize(area.Name, HorizontalAlignment.Left, -1, size).X;
            var folded = _folded.Contains(area.Id);
            var columns = folded ? 0 : Math.Min(area.Targets.Count, perLine);
            var lines = columns == 0 ? 0 : (area.Targets.Count + columns - 1) / columns;
            var blockWidth = Math.Min(width, Math.Max(nameWidth + foldWidth, columns * (Portrait + Gap) - Gap));
            var blockHeight = nameLine + (lines == 0 ? 0 : 2 + lines * (Portrait + Gap) - Gap);
            if (x > 0 && x + blockWidth > width)
            {
                x = 0;
                y += lineHeight + LineGap;
                lineHeight = 0;
            }
            area.NameRect = new Rect2(x, y, Math.Min(nameWidth, blockWidth - foldWidth), nameLine);
            area.FoldRect = new Rect2(area.NameRect.End.X, y, foldWidth, nameLine);
            area.Portraits.Clear();
            for (var i = 0; i < area.Targets.Count && !folded; i++)
                area.Portraits.Add(
                    new Rect2(
                        x + i % columns * (Portrait + Gap),
                        y + nameLine + 2 + i / columns * (Portrait + Gap),
                        Portrait,
                        Portrait
                    )
                );
            x += blockWidth + AreaGap;
            lineHeight = Math.Max(lineHeight, blockHeight);
        }
        var height = y + lineHeight;
        _canvas.CustomMinimumSize = new Vector2(0, height);
        _scroll.CustomMinimumSize = new Vector2(0, Math.Min(height, MaxHeight));
        _canvas.QueueRedraw();
    }

    private Texture2D? ArtOf(string key)
    {
        if (!_art.TryGetValue(key, out var art))
            _art[key] = art = LiveGame.Find() is { } game ? Art.Target(game, TargetSpec.FromKey(key)) : null;
        return art;
    }

    private void Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion:
                _canvas.TooltipText = TooltipAt(motion.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                if (Click(click.Position, only: false))
                    _canvas.AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } click:
                if (Click(click.Position, only: true))
                    _canvas.AcceptEvent();
                break;
        }
    }

    // A portrait toggles its target; an area's name ignores the area, or brings it back when all of it is ignored;
    // the fold mark folds it. A right-click (only) runs that target or area alone, every other one shown ignored; on
    // one already ignored it brings it back beside those kept, so a few can be picked one by one. Nothing changes while
    // a run is under way.
    private bool Click(Vector2 at, bool only)
    {
        foreach (var area in _areas)
        {
            if (area.FoldRect.HasPoint(at))
            {
                if (!_folded.Remove(area.Id))
                    _folded.Add(area.Id);
                Relayout();
                return true;
            }
            if (RunService.Instance.IsActive)
                continue;
            if (area.NameRect.HasPoint(at))
            {
                var keys = area.Targets.Select(t => t.Key).ToList();
                if (only)
                    PanelState.RunOnly(keys);
                else
                    PanelState.SetIgnored(keys, !keys.All(PanelState.Ignored.Contains));
                return true;
            }
            for (var i = 0; i < area.Portraits.Count; i++)
                if (area.Portraits[i].HasPoint(at))
                {
                    var key = area.Targets[i].Key;
                    if (only)
                        PanelState.RunOnly([key]);
                    else
                        PanelState.SetIgnored([key], !PanelState.Ignored.Contains(key));
                    return true;
                }
        }
        return false;
    }

    private string TooltipAt(Vector2 at)
    {
        foreach (var area in _areas)
        {
            if (area.FoldRect.HasPoint(at))
                return _folded.Contains(area.Id) ? Tr.Get("Show this area's targets") : Tr.Get("Fold this area");
            if (area.NameRect.HasPoint(at))
                return area.Targets.All(t => PanelState.Ignored.Contains(t.Key))
                    ? Tr.Format("{0}: click to return the whole area", area.Name)
                    : Tr.Format("{0}: click to ignore the whole area", area.Name);
            for (var i = 0; i < area.Portraits.Count; i++)
                if (area.Portraits[i].HasPoint(at))
                {
                    var target = area.Targets[i];
                    var state =
                        PanelState.Ignored.Contains(target.Key) ? Tr.Get("Ignored: click to return it")
                        : !target.CanFight ? Tr.Get("This setup can't fight it yet: the run refuses it")
                        : Tr.Get("Click to ignore it in the next run");
                    // Translators: Line in a target's tooltip in the target strip: the character is fighting it.
                    var fighting = target.Key == Fighting ? Tr.Get("Fighting now") + "\n" : "";
                    return target.Name + "\n" + fighting + state;
                }
        }
        return "";
    }

    /// <summary>Whether the strip is open, and each area with its targets and how many are ignored, for <c>cs2 ui</c>.</summary>
    public void Describe(StringBuilder text)
    {
        text.Append("\nstrip=").Append(Open ? "open" : "closed");
        foreach (var area in _areas)
            text.Append("\nstrip_area ")
                .Append(area.Id is DungeonsId or MastersId ? area.Id : Ids.Brief(area.Id))
                .Append(' ')
                .Append(area.Targets.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" ignored=")
                .Append(
                    area.Targets.Count(t => PanelState.Ignored.Contains(t.Key)).ToString(CultureInfo.InvariantCulture)
                )
                .Append(_folded.Contains(area.Id) ? " folded" : "");
    }
}
