using MelvorMods.CombatSimulator2.Game;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// The target the character is fighting, for the target strip to show it and keep it alone. What the character fights changes in the
/// game, not here, so it is read twice a second, from <see cref="Tick"/>.
/// </summary>
internal sealed class FightWatch
{
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>The target the character is fighting; null when not fighting.</summary>
    public string? Key { get; private set; }

    /// <summary>Its name as the game shows it; empty when not fighting.</summary>
    public string Name { get; private set; } = "";

    /// <summary>Raised when <see cref="Key"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Called once a frame; reads the game twice a second.</summary>
    public void Tick()
    {
        if (_clock.ElapsedMilliseconds < 500)
            return;
        _clock.Restart();
        var current =
            LiveGame.Find() is { } game && Dependencies.IsAvailable(Dependencies.CurrentTarget)
                ? LiveGame.Current(game)
                : null;
        if (current?.Key == Key)
            return;
        Key = current?.Key;
        Name = current?.Name ?? "";
        Changed?.Invoke();
    }
}
