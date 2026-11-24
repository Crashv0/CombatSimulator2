namespace MelvorMods.CombatSimulator2.Model;

/// <summary>One change in a version, as the About window lists it: something new, or something fixed.</summary>
public sealed record NewsLine(bool Fixed, string Text);

/// <summary>
/// What each version changed, in the player's words: written by hand before a release, under the version the
/// manifest will carry, new things before fixes. A version without an entry has nothing to tell. In English only:
/// the lines change with every version, and translators' time goes further on the panel, which stays.
/// </summary>
public static class VersionNews
{
    private static readonly Dictionary<string, NewsLine[]> Versions = new(StringComparer.Ordinal)
    {
        ["0.1.0-preview.1"] =
        [
            New("Simulate every monster, dungeon and Slayer task with your setup, ranked by XP, kills and more"),
            New("Compare two setups on the same fights"),
            New("Try any gear, levels, prayers and Setup entries, owned or not"),
            New("Find the best setup for a target, with a cheaper and a safer one"),
            New("Rare drop chances per kill, and roughly when the first should drop"),
            New("Slayer masters: the best loadout for each, and which tasks to block"),
        ],
        ["0.1.0-preview.2"] =
        [
            New("Find best setup can search every item in the game, narrowed to what you can wear or have reached"),
            New("Items you don't own say where they come from"),
            New("Slots your goal can't tell apart keep what you wear; Gap from now shows what to get first"),
            New("Set how long a gear search may run, 15 min by default"),
            Fix("Gear searches read Early Access's item stats, so they fight far fewer sets"),
            Fix("Slayer Coins and XP for your current task no longer show too low with Slayer task \"As save\""),
        ],
        // Also what preview.3 changed, which had no entry: a player coming from preview.2 hears it here.
        ["0.1.0-preview.4"] =
        [
            New("Simulate what you're fighting with the button beside Simulate"),
            New("The target list puts what you're fighting first, with a button to focus the next run on it"),
            New("While comparing, Find best setup can search from either setup"),
            New("CS2 in Simplified Chinese"),
            Fix("CS2's window leaves room for the game's legacy sidebar"),
            Fix("Magic setups attack when your bank has none of the spell's runes"),
        ],
    };

    /// <summary>Every version with an entry.</summary>
    public static IReadOnlyCollection<string> Listed => Versions.Keys;

    /// <summary>The changes of <paramref name="version"/>; empty when it has no entry.</summary>
    public static IReadOnlyList<NewsLine> For(string version) =>
        version != null && Versions.TryGetValue(version, out var lines) ? lines : [];

    private static NewsLine New(string text) => new(false, text);

    private static NewsLine Fix(string text) => new(true, text);
}
