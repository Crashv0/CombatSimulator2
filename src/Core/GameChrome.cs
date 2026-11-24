using GamesByMalcs.Melvor2.CustomControls;
using GamesByMalcs.Melvor2.EventLog;
using GamesByMalcs.Melvor2.GameMenus.Footer;
using GamesByMalcs.Melvor2.GameMenus.Header;
using GamesByMalcs.Melvor2.GameMenus.Sidebar;
using GamesByMalcs.Melvor2.GameMenus.SidebarLegacy;
using GamesByMalcs.Melvor2.GameMenus.Widgets;
using Godot;

namespace MelvorMods.Core;

/// <summary>
/// Keeps the game's sidebars (its own and the legacy one its settings can switch to, wide or collapsed), footer, event
/// log and widget strip (the column of shortcuts on the right), so a modal can fill the room they leave, let what they open draw over it, and know when a press on them should hand the game
/// back. Main thread.
/// </summary>
internal static class GameChrome
{
    private static GameSidebar? _sidebar;
    private static GameSidebarLegacy? _legacySidebar;
    private static GameFooter? _footer;
    private static WidgetsView? _widgets;
    private static EventLogView? _eventLog;

    // WidgetsView: the column of shortcut buttons. Kept as a path: it is looked up every frame a modal is open, and
    // made from text each time it would allocate each frame.
    private static readonly NodePath StripColumn = "HBoxContainer/VBoxContainer";

    /// <summary>The game's widget strip while it is in the tree.</summary>
    public static Control? Widgets =>
        _widgets != null && GodotObject.IsInstanceValid(_widgets) && _widgets.IsInsideTree() ? _widgets : null;

    private static readonly List<(Control Part, int Z)> _raised = [];

    // The parts kept, each let go as it leaves the tree. Closing a character frees the game's view, and touching a
    // part kept past that kills the game: IsInstanceValid still passed for the freed footer.
    private static readonly List<(Node Part, Action Left)> _watched = [];

    /// <summary>
    /// Draws the footer, the event log and the widget strip at <paramref name="z"/> while modals are open, so what they
    /// open (the event log, the footer's choices, the strip's panels) shows over a modal; null gives them their own
    /// back.
    /// </summary>
    public static void Raise(int? z)
    {
        foreach (var (part, own) in _raised)
            part.ZIndex = own;
        _raised.Clear();
        if (z is not { } level)
            return;
        foreach (var part in new Control?[] { _footer, _eventLog, _widgets })
            if (part != null && GodotObject.IsInstanceValid(part) && part.IsInsideTree())
            {
                _raised.Add((part, part.ZIndex));
                part.ZIndex = Math.Max(part.ZIndex, level);
            }
    }

    /// <summary>Called for every node entering the tree.</summary>
    public static void OnNodeAdded(Node node)
    {
        switch (node)
        {
            case GameSidebar sidebar:
                _sidebar = sidebar;
                break;
            case GameSidebarLegacy legacy:
                _legacySidebar = legacy;
                break;
            case GameFooter footer:
                _footer = footer;
                break;
            case WidgetsView widgets:
                _widgets = widgets;
                break;
            case EventLogView eventLog:
                _eventLog = eventLog;
                break;
            default:
                return;
        }
        Watch(node);
    }

    private static void Watch(Node part)
    {
        if (_watched.Exists(w => w.Part == part))
            return;
        Action left = () => Gone(part);
        part.TreeExiting += left;
        _watched.Add((part, left));
    }

    // While it leaves, the part is still whole: its own draw order goes back, then nothing keeps it.
    private static void Gone(Node part)
    {
        var raised = _raised.FindIndex(r => r.Part == part);
        if (raised >= 0)
        {
            _raised[raised].Part.ZIndex = _raised[raised].Z;
            _raised.RemoveAt(raised);
        }
        if (_sidebar == part)
            _sidebar = null;
        if (_legacySidebar == part)
            _legacySidebar = null;
        if (_footer == part)
            _footer = null;
        if (_widgets == part)
            _widgets = null;
        if (_eventLog == part)
            _eventLog = null;
        var watched = _watched.FindIndex(w => w.Part == part);
        if (watched >= 0)
        {
            part.TreeExiting -= _watched[watched].Left;
            _watched.RemoveAt(watched);
        }
    }

    /// <summary>
    /// Gives the parts their own draw order back and lets them go, as the mod unloads: the game keeps them, and a
    /// handler left on them would call into the unloaded mod.
    /// </summary>
    public static void Release()
    {
        Raise(null);
        foreach (var (part, left) in _watched)
            part.TreeExiting -= left;
        _watched.Clear();
        _sidebar = null;
        _legacySidebar = null;
        _footer = null;
        _widgets = null;
        _eventLog = null;
    }

    /// <summary>Finds the parts already in the tree (a mod loaded in game).</summary>
    public static void OnTreeReady(SceneTree tree)
    {
        var stack = new Stack<Node>();
        stack.Push(tree.Root);
        while (
            stack.Count > 0
            && (_sidebar == null || _legacySidebar == null || _footer == null || _widgets == null || _eventLog == null)
        )
        {
            var node = stack.Pop();
            OnNodeAdded(node);
            if (node is GameSidebar or GameSidebarLegacy or GameFooter or WidgetsView or EventLogView)
                continue;
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }
    }

    /// <summary>
    /// <paramref name="area"/> less the sidebar showing along its left, the widget strip's buttons along its right and the
    /// footer along its foot, whichever are showing.
    /// </summary>
    public static Rect2 Free(Rect2 area)
    {
        var left = area.Position.X;
        var right = area.End.X;
        var bottom = area.End.Y;
        // The game shows one sidebar at a time; the legacy one changes width as it collapses.
        foreach (var shown in new[] { Showing(_sidebar), Showing(_legacySidebar) })
            if (shown is { } sidebar)
                left = Math.Clamp(sidebar.End.X, left, right);
        // The strip's buttons alone: a panel it opens draws over the modal, which keeps its size.
        if (Showing(Widgets?.GetNodeOrNull<Control>(StripColumn) ?? Widgets) is { } widgets)
            right = Math.Clamp(widgets.Position.X, left, right);
        if (Showing(_footer) is { } footer)
            bottom = Math.Clamp(footer.Position.Y, area.Position.Y, bottom);
        return new Rect2(left, area.Position.Y, right - left, bottom - area.Position.Y);
    }

    /// <summary>
    /// True when <paramref name="pressed"/> is, or is inside, one of the game's buttons in its header or sidebar: the
    /// player has gone back to the game. The footer and the widget strip open things over whatever is showing, and a
    /// mod's own header button does what its mod says, so none of those counts.
    /// </summary>
    public static bool LeavesModals(Control? pressed)
    {
        var button = false;
        for (Node? node = pressed; node != null; node = node.GetParent())
        {
            if (node.HasMeta(HeaderButtons.Meta) || node is WidgetsView or GameFooter)
                return false;
            if (node is BaseButton or IBaseButton)
                button = true;
            if (node is GameHeader or GameSidebar or GameSidebarLegacy)
                return button;
        }
        return false;
    }

    private static Rect2? Showing(Control? control) =>
        control != null && GodotObject.IsInstanceValid(control) && control.IsVisibleInTree()
            ? control.GetGlobalRect()
            : null;
}
