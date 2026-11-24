using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

// What the character is fighting, shown where the player looks for a target: its portrait leading the grid in a group
// of its own, and marked where its area shows it. Simulating it is the run's buttons' work, not the strip's.
internal sealed partial class TargetStrip
{
    private const string FightingId = "fighting";

    private string? Fighting => _fight.Key;

    /// <summary>Called once a frame while the view is open; follows the fight only while the strip is open.</summary>
    public void Tick()
    {
        if (_root.Visible)
            _fight.Tick();
    }

    // The target being fought, first in the grid in a group of its own, when the filters show it.
    private void AddFighting(TargetSelection selection)
    {
        if (Fighting == null || selection.Shown.FirstOrDefault(t => t.Key == Fighting) is not { } target)
            return;
        // Translators: Group leading the target strip: the target the character is fighting.
        var group = new Area(FightingId, Tr.Get("Fighting"));
        group.Targets.Add(target);
        _areas.Insert(0, group);
    }
}
