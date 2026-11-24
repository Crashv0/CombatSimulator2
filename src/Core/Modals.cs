using Godot;

namespace MelvorMods.Core;

sealed partial class ModContext
{
    private ModModals? _modals;

    /// <summary>Large modals over the game, below its header; modals registered here belong to this mod.</summary>
    public ModModals Modals => _modals ??= CreateModals();

    // The first use starts drawing the mod's modals every frame, and their layer goes when the mod unloads.
    private ModModals CreateModals()
    {
        var modals = new ModModals(Id, Hooks);
        OnFrame(ModalHost.OnFrame);
        OnReady(GameChrome.OnTreeReady);
        OnNodeAdded(GameChrome.OnNodeAdded);
        Hooks.AddOwned(ModalHost.ReleaseLayer);
        Hooks.AddOwned(GameChrome.Release);
        return modals;
    }
}

// The pause menu reaches the modals through these.
internal static partial class PauseMenus
{
    static partial void CloseModals() => ModalHost.CloseAll();

    static partial void CancelModal(ref bool handled) => handled = ModalHost.Cancel();
}

/// <summary>A mod's handle on its large modals; modals registered here belong to the mod.</summary>
sealed class ModModals
{
    private readonly string _owner;
    private readonly ModHooks _hooks;

    internal ModModals(string owner, ModHooks hooks)
    {
        _owner = owner;
        _hooks = hooks;
    }

    /// <summary>
    /// Registers a modal: a frame of <paramref name="baseSize"/> in the game's 1920 × 1080 base units, centred in the
    /// room the game's header, sidebars and footer leave and clamped to it less a small margin, over the room dimmed.
    /// Those stay uncovered and clickable; a press on a button in the header or left sidebar closes the modals, and
    /// what the footer and the widget strip open shows over them. <paramref name="build"/> makes the content on the first <see cref="ModModal.Open"/>;
    /// the content is the frame (the mod draws none of its own) and is kept, hidden, while closed, until
    /// <see cref="ModModal.Rebuild"/>. Escape closes the top modal; opening the pause menu closes them all. The content
    /// sits under a root in the mod's translation domain with Godot's automatic translation off (see
    /// <see cref="ModText.Adopt"/>): only text the mod looks up is translated. The mod frees it when it unloads.
    /// </summary>
    public ModModal Register(string id, Vector2 baseSize, Func<Control> build)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(build);
        if (baseSize.X <= 0 || baseSize.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseSize), baseSize, "a modal needs a positive size");
        var modal = new ModModal($"{_owner}:{id}", _owner, baseSize, build);
        ModalHost.Add(modal);
        _hooks.AddOwned(() => ModalHost.Remove(modal));
        return modal;
    }

    /// <summary>Registers a modal that fills the room the header, sidebars and footer leave; see the other overload.</summary>
    public ModModal Register(string id, Func<Control> build) =>
        Register(id, new Vector2(float.PositiveInfinity, float.PositiveInfinity), build);
}

/// <summary>
/// Draws the open modals over the game's view, below its widget strip's panels, its tooltips and its pop-ups. Each modal
/// dims the room the game's header, sidebars and footer leave and blocks clicks there; those stay live, and a press
/// on a button in the header or left sidebar closes the modals. Layout follows the window, the
/// game's UI scale and the header's height, checked once a frame while a modal is open.
/// </summary>
internal static class ModalHost
{
    /// <summary>Space kept between the frame and the room around it, in base units.</summary>
    public const float Margin = 8;
    private const int Layer = 2;

    // Above what the game's pages draw raised (their floating icons); the footer, event log and widget strip go one
    // above, and the game's tooltips (10) above those.
    private const int ModalZ = 8;
    private static readonly Color DimColour = new(0, 0, 0, 0.55f);

    private static readonly Dictionary<string, ModModal> _modals = new(StringComparer.Ordinal);

    // In the game's middle area, just before its widget strip, so the strip's panels and the game's tooltips draw
    // over the modals and take the mouse first; a layer of the mod's own above the game where there is no strip.
    private static Node? _layer;
    private static Rect2 _laidOutFor;
    private static Rect2 _freeFor;

    // Replaced content still to free, hidden, freed leaves first a little each frame.
    private static readonly Stack<Node> _freeing = new();
    private const double FreeBudgetMs = 2;

    public static ModalStack Stack { get; } = Make();

    private static ModalStack Make()
    {
        var stack = new ModalStack();
        stack.Changed += Changed;
        stack.EscapeFailed += (key, e) =>
            Log.Write(
                _modals.TryGetValue(key, out var modal) ? modal.Owner : "core",
                $"ERROR modal '{key}' Escape failed: {e}"
            );
        return stack;
    }

    public static void Add(ModModal modal)
    {
        if (_modals.TryGetValue(modal.Key, out var old))
            Remove(old);
        _modals[modal.Key] = modal;
        Stack.SetEscape(modal.Key, modal.Escape);
    }

    public static void Remove(ModModal modal)
    {
        modal.Removed = true;
        Stack.Remove(modal.Key);
        if (_modals.TryGetValue(modal.Key, out var current) && ReferenceEquals(current, modal))
            _modals.Remove(modal.Key);
        if (modal.Slot is { } slot && GodotObject.IsInstanceValid(slot))
        {
            // Its replaced content goes with it; nothing of an unloading mod stays referenced here.
            var keep = _freeing.Where(n => GodotObject.IsInstanceValid(n) && !slot.IsAncestorOf(n)).Reverse().ToList();
            _freeing.Clear();
            foreach (var node in keep)
                _freeing.Push(node);
            slot.GetParent()?.RemoveChild(slot);
            slot.QueueFree();
        }
        modal.Slot = null;
        modal.Content = null;
        modal.Dim = null;
    }

    // Frees replaced content a node at a time, deepest first, until the frame's budget is spent. Freeing a large
    // tree at once costs every node's exit from the tree in one frame.
    private static void FreeSome()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (_freeing.Count > 0 && clock.Elapsed.TotalMilliseconds < FreeBudgetMs)
        {
            var node = _freeing.Peek();
            if (!GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion())
            {
                _freeing.Pop();
                continue;
            }
            var count = node.GetChildCount();
            if (count > 0)
            {
                _freeing.Push(node.GetChild(count - 1));
                continue;
            }
            _freeing.Pop();
            node.Free();
        }
    }

    /// <summary>Handles Escape by closing the top modal. Returns whether it did.</summary>
    public static bool Cancel() => Stack.Cancel();

    public static void CloseAll() => Stack.CloseAll();

    /// <summary>
    /// Releases the mod's layer as it unloads, or before it is made again: the layer leaves the tree now and is freed,
    /// so a reloaded copy of the mod makes its own under the same name.
    /// </summary>
    public static void ReleaseLayer()
    {
        GameChrome.Raise(null);
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
        {
            _layer.GetParent()?.RemoveChild(_layer);
            _layer.QueueFree();
        }
        _layer = null;
    }

    /// <summary>Called once a frame: keeps open modals placed as the window, UI scale or header change.</summary>
    public static void OnFrame()
    {
        if (_freeing.Count > 0)
            FreeSome();
        if (Stack.Opened.Count == 0 || _layer == null || !GodotObject.IsInstanceValid(_layer))
            return;
        var area = Area(_layer);
        if (area != _laidOutFor || GameChrome.Free(area) != _freeFor)
            LayOutAll(area);
        CloseOnChromeClick(_layer);
    }

    private static bool _mouseWasDown;

    // A press on one of the game's own buttons in its header or sidebar leaves the modals for the game; they
    // stay uncovered, so the press reaches the button as well. Read once a frame: a press shorter than a frame is
    // rare enough to miss.
    private static void CloseOnChromeClick(Node layer)
    {
        var down = Input.IsMouseButtonPressed(MouseButton.Left);
        var pressed = down && !_mouseWasDown;
        _mouseWasDown = down;
        if (pressed && GameChrome.LeavesModals(layer.GetViewport().GuiGetHoveredControl()))
            CloseAll();
    }

    private static void Changed(string key, bool open)
    {
        if (!_modals.TryGetValue(key, out var modal))
            return;
        try
        {
            if (open)
                Show(modal);
            else if (modal.Slot is { } slot && GodotObject.IsInstanceValid(slot))
                slot.Visible = false;
            GameChrome.Raise(Stack.Opened.Count > 0 ? ModalZ + 1 : null);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write(modal.Owner, $"ERROR modal '{key}' failed to {(open ? "open" : "close")}: {e}");
            if (open)
            {
                Stack.Remove(key);
                return;
            }
        }
        try
        {
            modal.RaiseOpenChanged(open);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write(modal.Owner, $"ERROR modal '{key}' open handler failed: {e}");
        }
    }

    private static void Show(ModModal modal)
    {
        var layer = EnsureLayer();
        if (modal.Slot == null || !GodotObject.IsInstanceValid(modal.Slot))
        {
            // Hidden until its content is built, so a build that fails leaves no dimmed layer blocking the game.
            var slot = new Control
            {
                Name = $"MelvorMods_{modal.Key.Replace(':', '_')}",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            };
            slot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            // The content is the mod's: its translation domain, and only what it looks up is translated.
            CoreLabels.Adopt(slot, modal.Owner);
            var dim = new ColorRect
            {
                Name = "Dim",
                // A modal that fills the room leaves only its margin to dim, which reads as a dark edge.
                Color = float.IsPositiveInfinity(modal.BaseSize.X) ? Colors.Transparent : DimColour,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            dim.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    dim.AcceptEvent();
                    modal.OutsideClick?.Invoke();
                }
            };
            slot.AddChild(dim);
            layer.AddChild(slot);
            modal.Slot = slot;
            modal.Content = null;
            modal.Dim = dim;
        }
        if (modal.Content == null || !GodotObject.IsInstanceValid(modal.Content))
            AddContent(modal, modal.Slot);
        layer.MoveChild(modal.Slot, -1);
        modal.Slot.Visible = true;
        LayOut(modal, Area(layer));
    }

    private static void AddContent(ModModal modal, Control slot)
    {
        modal.Content = null;
        var content = modal.Build() ?? throw new InvalidOperationException("the modal's build returned null");
        content.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        slot.AddChild(content);
        modal.Content = content;
    }

    /// <summary>Frees a modal's content and, if it is open, builds it again in its place.</summary>
    public static void Rebuild(ModModal modal)
    {
        if (modal.Content is { } old && GodotObject.IsInstanceValid(old))
        {
            // Hidden now and freed from the next frame on: the caller may be one of its own handlers.
            old.Visible = false;
            old.Name = old.Name + "_Freed";
            _freeing.Push(old);
        }
        modal.Content = null;
        if (!modal.IsOpen || modal.Slot is not { } slot || !GodotObject.IsInstanceValid(slot) || _layer == null)
            return;
        try
        {
            AddContent(modal, slot);
            LayOut(modal, Area(_layer));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write(modal.Owner, $"ERROR modal '{modal.Key}' failed to rebuild: {e}");
            modal.Close();
        }
    }

    private static Node EnsureLayer()
    {
        var strip = GameChrome.Widgets;
        // A layer of the mod's own gives way once the game's strip is there, and a layer by a strip the game freed
        // went with it.
        if (_layer != null && GodotObject.IsInstanceValid(_layer) && (_layer is not CanvasLayer || strip == null))
            return _layer;
        ReleaseLayer();
        const string name = "MelvorMods_Modals";
        if (strip?.GetParent() is { } middle)
        {
            var host = new Control
            {
                Name = name,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = ModalZ,
            };
            host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            middle.AddChild(host);
            middle.MoveChild(host, strip.GetIndex());
            _layer = host;
            return host;
        }
        var tree = GameTree.Current ?? throw new InvalidOperationException("the game's scene tree isn't ready");
        _layer = new CanvasLayer { Name = name, Layer = Layer };
        tree.Root.AddChild(_layer);
        return _layer;
    }

    // The visible rect below the game's header, in canvas (base-unit) coordinates.
    private static Rect2 Area(Node layer)
    {
        var visible = layer.GetViewport().GetVisibleRect();
        var top = Math.Clamp(HeaderButtons.HeaderBottom(), visible.Position.Y, visible.End.Y);
        return new Rect2(visible.Position.X, top, visible.Size.X, visible.End.Y - top);
    }

    /// <summary>Lays a modal out again now (its scale changed).</summary>
    public static void Relayout(ModModal modal)
    {
        if (modal.IsOpen && _layer != null && GodotObject.IsInstanceValid(_layer))
            LayOut(modal, Area(_layer));
    }

    private static void LayOutAll(Rect2 area)
    {
        _laidOutFor = area;
        foreach (var key in Stack.Opened)
            if (_modals.TryGetValue(key, out var modal))
                LayOut(modal, area);
    }

    private static void LayOut(ModModal modal, Rect2 area)
    {
        _laidOutFor = area;
        _freeFor = GameChrome.Free(area);
        // The header, sidebars and footer stay uncovered and live: the dim and the frame keep to the room they leave.
        area = _freeFor;
        // The rects are the canvas's; the slot may sit below the top-left of the screen.
        var origin = modal.Slot is { } slot && GodotObject.IsInstanceValid(slot) ? slot.GlobalPosition : Vector2.Zero;
        if (modal.Dim is { } dim)
        {
            dim.Position = area.Position - origin;
            dim.Size = area.Size;
        }
        if (modal.Content is not { } content)
            return;
        // Scaled content: the room in its own units is the room on screen divided by the scale. The scale is the
        // mod's and is kept when the content's minimum doesn't fit: drawing everything smaller to make it fit would
        // shrink text past reading, so the content has to scroll instead.
        var room = new Vector2(Math.Max(1, area.Size.X - 2 * Margin), Math.Max(1, area.Size.Y - 2 * Margin));
        try
        {
            modal.ReportRoom(room);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write(modal.Owner, $"ERROR modal '{modal.Key}' room handler failed: {e}");
        }
        var scale = modal.Scale;
        var size = new Vector2(
            Math.Max(0, Math.Min(modal.BaseSize.X, room.X / scale)),
            Math.Max(0, Math.Min(modal.BaseSize.Y, room.Y / scale))
        );
        content.Scale = new Vector2(scale, scale);
        content.Size = size;
        // A content minimum larger than the space grows the control; centre what it really is, and keep its top-left
        // corner (the title and the close button) on screen when it is larger than the room.
        var centred = area.Position + (area.Size - content.Size * scale) / 2;
        content.Position = centred.Max(area.Position + new Vector2(Margin, Margin)).Round() - origin;
    }
}
