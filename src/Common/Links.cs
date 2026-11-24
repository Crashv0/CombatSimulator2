using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Common;

/// <summary>
/// Where the About window's links go. One address each; an empty one leaves its link shown but switched off.
/// </summary>
internal static class Links
{
    /// <summary>CS2's repository: its source and changelog.</summary>
    public static string GitHub =>
        ReportLinks.Repository.Length == 0 ? "" : $"https://github.com/{ReportLinks.Repository}";

    /// <summary>Every version's changes.</summary>
    public static string Changes => GitHub.Length == 0 ? "" : $"{GitHub}/releases";

    /// <summary>The repository's bug report form.</summary>
    public static string ReportBug => GitHub.Length == 0 ? "" : $"{GitHub}/issues/new?template={ReportLinks.IssueForm}";

    /// <summary>The repository's feedback form, given the field ids <see cref="Feedback"/> fills in.</summary>
    public const string FeedbackForm = "feedback.yml";

    /// <summary>
    /// The feedback form with CS2's version and the game's filled in, the game's saying whether CS2 was checked with
    /// it, so feedback names what it was about without the player looking it up.
    /// </summary>
    public static string Feedback(string version, CheckedState game)
    {
        if (GitHub.Length == 0)
            return "";
        var gameText =
            $"{game.Version ?? "?"} (build {game.Build})"
            + game.Kind switch
            {
                CheckedKind.Listed => "",
                CheckedKind.Unlisted => ", not checked",
                _ => ", build unknown",
            };
        return $"{GitHub}/issues/new?template={FeedbackForm}"
            + $"&cs2-version={Uri.EscapeDataString(version)}&game-version={Uri.EscapeDataString(gameText)}";
    }

    /// <summary>Where players ask questions and suggest things.</summary>
    public const string Discord = ReportLinks.DiscordChannel;

    /// <summary>CS2's page on Nexus Mods.</summary>
    public const string NexusMods = "https://www.nexusmods.com/melvoridle2/mods/39";

    /// <summary>CS2's translation project, where players translate it.</summary>
    public const string Translate = "https://crowdin.com/project/combat-simulator-2";
}
