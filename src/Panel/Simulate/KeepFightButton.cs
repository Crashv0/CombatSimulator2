using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// The button leading the target strip, named for what the character is fighting, a toggle: pressed, it ignores every
/// other target the filters show and returns this one, so the next run is that target alone; pressed again, it puts
/// back the targets ignored before. Switched off when nothing is fought or the filters don't show it.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class KeepFightButton
{
    private readonly Button _button;
    private readonly FightWatch _fight;
    private TargetSelection? _selection;

    // What was ignored before keeping one target alone, and that target; null when not keeping one.
    private HashSet<string>? _before;
    private string? _kept;

    public KeepFightButton(FightWatch fight)
    {
        _fight = fight;
        _button = Ui.Button("", Look.Button4, "Cs2StripKeepFighting", Keep);
        _button.Icon = Art.File(Art.CombatPath);
        _button.AddThemeConstantOverride("icon_max_width", 18);
        _button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _button.ToggleMode = true;
        Show(null);
    }

    public Button Root => _button;

    /// <summary>Follows what the filters show and what the character is fighting.</summary>
    public void Show(TargetSelection? selection)
    {
        _selection = selection;
        // Kept only while the run is still that one target: an ignore changed by hand ends it, and leaves it as it is.
        if (_before != null && !(selection?.ToRun is [{ } only] && only.Key == _kept))
            (_before, _kept) = (null, null);
        _button.SetPressedNoSignal(_before != null);
        var shown = _fight.Key != null && selection?.Shown.Any(t => t.Key == _fight.Key) == true;
        _button.Text = _fight.Name;
        _button.Disabled = !shown && _before == null;
        _button.TooltipText =
            // Translators: Tooltip of the pressed button leading the target strip: undoes keeping one target alone.
            _before != null
                ? Tr.Get("Bring back the targets as they were")
            : _fight.Key == null ? Tr.Get("Not fighting anything right now.")
            // Translators: Tooltip of the button leading the target strip when the filters hide the fought target.
            : !shown ? Tr.Get("The filters don't show what you're fighting.")
            // Translators: Tooltip of the button leading the target strip: ignores every other target shown.
            : Tr.Get("Keep only what you're fighting for the next run");
    }

    private void Keep()
    {
        if (_before is { } before)
        {
            (_before, _kept) = (null, null);
            PanelState.SetIgnored([.. PanelState.Ignored.Except(before)], false);
            PanelState.SetIgnored(before, true);
            return;
        }
        if (_fight.Key is not { } key || _selection == null)
            return;
        (_before, _kept) = ([.. PanelState.Ignored], key);
        PanelState.SetIgnored(_selection.Shown.Select(t => t.Key).Where(k => k != key), true);
        PanelState.SetIgnored([key], false);
    }
}
