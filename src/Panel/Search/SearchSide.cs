using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// The setup the gear search starts from: side A, or side B while it is a setup. A saved run has results, not gear to
/// search from, so with no side B or a saved one the search is side A's.
/// </summary>
internal static class SearchSide
{
    // Kept while the game runs, like the search's kind: a player improving side B keeps searching from it.
    private static SideLetter _chosen = SideLetter.A;

    /// <summary>Whether side B can be searched from, so the form offers the choice.</summary>
    public static bool CanChoose => PanelState.B is { CanChange: true };

    public static SideLetter Letter => _chosen == SideLetter.B && CanChoose ? SideLetter.B : SideLetter.A;

    public static SideState Side => Letter == SideLetter.B ? PanelState.B! : PanelState.A;

    /// <summary>The side's latest check, for the side as it is now; null before it comes back.</summary>
    public static SideCheck? Check => Letter == SideLetter.B ? PanelState.CheckB : PanelState.CheckA;

    /// <summary>The side as the setup a search uses, without the character's name.</summary>
    public static Setup Setup() => Side.ToSetup("");

    public static void Choose(SideLetter letter)
    {
        if (_chosen == letter)
            return;
        _chosen = letter;
        PanelState.Notify();
    }
}
