using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.SetupTab;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// The start form's gear: what the side searched from wears, laid out as the game lays it out, where a click locks a
/// slot to what is in it (with a lock mark), and beside it, while side B is a setup too, which side that is; under it
/// the items never to use, as small icons that wrap (a click takes one out), folded past two rows, with their count
/// and "+ Item" to add one.
/// </summary>
internal sealed class StartGear
{
    private const int SlotSize = 40;
    private const int NeverSize = 30;
    private const int NeverGap = 4;

    private readonly GridContainer _grid;
    private readonly VBoxContainer _from;
    private readonly Segmented _fromPick;
    private readonly HFlowContainer _never;
    private readonly Label _neverCount;
    private readonly Button _neverMore;
    private readonly Button _add;
    private bool _neverAll;
    private readonly Action<Control, Action<string>> _pickItem;
    private readonly Action _changed;
    private bool _locking = true;

    /// <summary>
    /// The grid and the list; <paramref name="pickItem"/> opens the item picker under a control and gets the item
    /// chosen; <paramref name="changed"/> runs when a lock or the list changes.
    /// </summary>
    public StartGear(Action<Control, Action<string>> pickItem, Action changed)
    {
        _pickItem = pickItem;
        _changed = changed;
        Root = Ui.Column(10, "GearSearchGear");
        var top = Ui.Add(Root, Ui.Row(12));
        _grid = Ui.Add(top, new GridContainer { Name = "GearSearchSlots" });
        // The room the grid leaves at its right holds the choice of side, level with the grid's top.
        top.AddChild(Ui.Spacer());
        _from = Ui.Add(top, Ui.Column(6, "GearSearchFrom"));
        _from.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        // Translators: A setup as a noun: the Setup tab's name, and above the gear search's two buttons, A and B, that choose which compared setup it starts from.
        _from.AddChild(Ui.Label(Tr.Get("Setup"), TextStyle.Label));
        _fromPick = new Segmented(
            "GearSearchFromPick",
            [("A", SideLine.A, "GearSearchFromA"), ("B", SideLine.B, "GearSearchFromB")],
            id => SearchSide.Choose(id == "B" ? SideLetter.B : SideLetter.A),
            side: 6,
            ends: 3,
            framed: true
        );
        // Two letters, so the pair sits closer than the form's wider choices.
        _fromPick.Root.AddThemeConstantOverride("separation", 2);
        _from.AddChild(_fromPick.Root);
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        var never = Ui.Add(Root, Ui.Column(6, "GearSearchNeverUse"));
        var head = Ui.Add(never, Ui.Row(8));
        // Translators: Above the items a gear search must not use.
        var title = Ui.Add(head, Ui.Label(Tr.Get("Never use"), TextStyle.Label));
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _neverCount = Ui.Add(head, Ui.Label("", TextStyle.Small, "GearSearchNeverCount"));
        _neverCount.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(Ui.Spacer());
        // Translators: Button that adds an item to those a gear search must not use.
        _add = Ui.Add(head, SearchWords.Button(Tr.Get("+ Item"), "GearSearchNeverAdd", AddItem));
        _never = Ui.Add(never, new HFlowContainer { Name = "GearSearchNever" });
        _never.AddThemeConstantOverride("h_separation", NeverGap);
        _never.AddThemeConstantOverride("v_separation", NeverGap);
        _neverMore = Ui.Add(
            never,
            Ui.Button(
                "",
                "",
                "GearSearchNeverMore",
                () =>
                {
                    _neverAll = !_neverAll;
                    Fill();
                }
            )
        );
        _neverMore.Flat = true;
        _neverMore.AddThemeColorOverride("font_color", Look.Best);
        _neverMore.AddThemeColorOverride("font_hover_color", Look.Best.Lightened(0.2f));
        _neverMore.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
    }

    public VBoxContainer Root { get; }

    /// <summary>The slots locked to what the side searched from wears.</summary>
    public HashSet<string> Locks { get; } = new(StringComparer.Ordinal);

    /// <summary>The items the search must not use.</summary>
    public HashSet<string> Excluded { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether slots can be locked: Droptimizer keeps every slot as worn, so it has none.</summary>
    public bool Locking
    {
        get => _locking;
        set
        {
            _locking = value;
            Fill();
        }
    }

    /// <summary>Draws the grid from the side searched from as last checked, the choice of side, and the list.</summary>
    public void Fill()
    {
        Ui.Clear(_grid);
        var grid = ChangeOptions.Grid();
        var side = SearchSide.Side;
        var check = SearchSide.Check;
        _from.Visible = SearchSide.CanChoose;
        _fromPick.Show(SearchSide.Letter == SideLetter.B ? "B" : "A");
        var names = PanelState.Names();
        if (grid.Count > 0)
        {
            // Until the side is read the slots stand empty, so the form has its shape and the gear lands in place.
            var view = check == null ? null : new SideView(check.Changes, side.Changes);
            var columns = grid.Max(g => g.Column) + 1;
            var rows = grid.Max(g => g.Row) + 1;
            _grid.Columns = columns;
            var at = grid.ToDictionary(g => (g.Column, g.Row));
            for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
            {
                if (!at.TryGetValue((c, r), out var cell))
                {
                    _grid.AddChild(new Control { CustomMinimumSize = new Vector2(SlotSize, SlotSize) });
                    continue;
                }
                _grid.AddChild(
                    view == null
                        ? GameSlot.Make(
                            Ui.Id("GearSearchSlotWaiting", Ids.Brief(cell.Slot)),
                            SlotSize,
                            NativeStyles.Slot,
                            ChangeOptions.Icon(cell.Slot),
                            true,
                            false,
                            null,
                            "",
                            true
                        )
                        : SlotButton(cell.Slot, view.Item(cell.Slot), names)
                );
            }
        }
        FillNever(names);
    }

    // The items never to use as small icons, by name; past two rows only those, and "Show all".
    private void FillNever(Func<string, string> names)
    {
        Ui.Clear(_never);
        var items = Excluded.OrderBy(names, StringComparer.CurrentCultureIgnoreCase).ToList();
        _neverCount.Text = items.Count == 0 ? "" : Ui.Number(items.Count);
        var width = Root.Size.X > 0 ? Root.Size.X : 400;
        var twoRows = Math.Max(1, (int)((width + NeverGap) / (NeverSize + NeverGap))) * 2;
        var shown = _neverAll ? items : [.. items.Take(twoRows)];
        foreach (var item in shown)
            _never.AddChild(
                GameSlot.Make(
                    Ui.Id("GearSearchNever", item),
                    NeverSize,
                    NativeStyles.Slot,
                    ChangeOptions.Icon(item),
                    false,
                    false,
                    () =>
                    {
                        Excluded.Remove(item);
                        Changed();
                    },
                    // Translators: Hover of an item a gear search must not use, under its name.
                    names(item) + "\n" + Marks.Muted(Tr.Get("Click to use it again.")),
                    readOnly: false
                )
            );
        _neverMore.Visible = items.Count > twoRows;
        _neverMore.Text = _neverAll
            ? Tr.Get("Show less")
            // Translators: Shows every item a gear search must not use, when they fill more than two rows: {0} how many.
            : Tr.Format("Show all ({0})", Ui.Number(items.Count));
    }

    private Button SlotButton(string slot, string? item, Func<string, string> names)
    {
        var locked = _locking && Locks.Contains(slot);
        var tip = item == null ? names(slot) : names(slot) + " · " + names(item);
        if (_locking)
            tip +=
                "\n"
                + Marks.Muted(
                    locked
                        // Translators: Hover of a locked slot in the gear search's form.
                        ? Tr.Get("Locked: the search keeps this slot. Click to unlock.")
                        // Translators: Hover of a slot in the gear search's form.
                        : Tr.Get("Click to lock: the search keeps this slot as it is.")
                );
        var button = GameSlot.Make(
            Ui.Id("GearSearchSlot", Ids.Brief(slot)),
            SlotSize,
            NativeStyles.Slot,
            ChangeOptions.Icon(item ?? slot),
            item == null,
            false,
            _locking
                ? () =>
                {
                    if (!Locks.Remove(slot))
                        Locks.Add(slot);
                    Changed();
                }
                : null,
            tip,
            readOnly: false
        );
        // Droptimizer's grid only shows what is worn: it isn't greyed as a disabled slot is.
        if (!_locking)
            button.Disabled = false;
        if (locked && NativeStyles.LockIcon is { } lockIcon)
        {
            // A tiny mark inside the slot's top-right corner, over its frame.
            const int Mark = 11;
            var mark = Ui.Picture(lockIcon, Mark);
            mark.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            mark.OffsetLeft = -Mark - 2;
            mark.OffsetRight = -2;
            mark.OffsetTop = 2;
            mark.OffsetBottom = Mark + 2;
            mark.MouseFilter = Control.MouseFilterEnum.Ignore;
            button.AddChild(mark);
        }
        return button;
    }

    private void AddItem() =>
        _pickItem(
            _add,
            id =>
            {
                if (id != Change.None && Excluded.Add(id))
                    Changed();
            }
        );

    private void Changed()
    {
        Fill();
        _changed();
    }
}
