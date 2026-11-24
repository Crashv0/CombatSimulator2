using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

// What the character is fighting, one click from Simulate: an icon button beside it, named in its tooltip.
internal sealed partial class RunControl
{
    private Button _fighting = null!;
    private string? _fightingKey;
    private readonly System.Diagnostics.Stopwatch _fightingClock = System.Diagnostics.Stopwatch.StartNew();

    private Button MakeFighting(Action fight)
    {
        _fighting = Ui.Button("", Look.Button8, "Cs2RunFighting", fight);
        _fighting.Icon = Art.File(Art.CombatPath);
        _fighting.IconAlignment = HorizontalAlignment.Center;
        _fighting.AddThemeConstantOverride("icon_max_width", 24);
        _fighting.CustomMinimumSize = new Vector2(44, 0);
        // Level with Simulate, under the bar that spans Simulate alone.
        _fighting.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        _fighting.Disabled = true;
        _fighting.TooltipText = Tr.Get("Not fighting anything right now.");
        return _fighting;
    }

    // What the character fights changes in the game, not here: read twice a second.
    private void TickFighting()
    {
        if (_fightingClock.ElapsedMilliseconds < 500)
            return;
        _fightingClock.Restart();
        var current =
            LiveGame.Find() is { } game && Dependencies.IsAvailable(Dependencies.CurrentTarget)
                ? LiveGame.Current(game)
                : null;
        _fighting.Disabled = current == null || RunService.Instance.IsActive;
        if (current?.Key == _fightingKey)
            return;
        _fightingKey = current?.Key;
        _fighting.TooltipText =
            current == null
                ? Tr.Get("Not fighting anything right now.")
                // Translators: Tooltip of the button beside Simulate that simulates the target the character is fighting.
                : Tr.Format("Simulate {0}", current.Name);
    }
}
