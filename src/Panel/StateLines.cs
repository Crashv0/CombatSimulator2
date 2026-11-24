using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>The sides, the filter and the target counts as one line each, for replies and <c>cs2 ui</c>.</summary>
internal static class StateLines
{
    /// <summary>A side as replies print it: <c>side a base=… changes=n req=check|ignore …</c>, or <c>side b none</c>.</summary>
    public static string SideLine(SideLetter letter, SideState? side)
    {
        var name = letter == SideLetter.A ? "a" : "b";
        if (side == null)
            return $"side {name} none";
        var from = side.Base.Kind == SideBaseKind.Loadout ? Ids.Brief(side.Base.Id!) : side.Base.Text;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"side {name} base={from} changes={side.Changes.Count} req={(side.IgnoreRequirements ? "ignore" : "check")}{(side.Changes.IsEmpty ? "" : " " + side.Changes.ShortForm)}"
        );
    }

    /// <summary>
    /// The sides, the side the gear search starts from, the filter, the target counts and each ignored target, a line
    /// each, for <c>cs2 ui</c>.
    /// </summary>
    public static void Describe(StringBuilder text)
    {
        text.Append('\n').Append(SideLine(SideLetter.A, PanelState.A));
        text.Append('\n').Append(SideLine(SideLetter.B, PanelState.B));
        text.Append("\ngear-search from=").Append(Search.SearchSide.Letter);
        text.Append('\n').Append(FilterLine());
        if (TargetsLine() is { } targets)
            text.Append('\n').Append(targets);
        foreach (var key in PanelState.Ignored.Order(StringComparer.Ordinal))
            text.Append("\nignored ").Append(Ids.BriefKey(key));
        SetupTab.ChangesBar.Describe(text);
        Results(text);
    }

    // What the results on screen say beyond their rows: the best target and how many died every fight, the pins
    // shown and waiting, the wider run these results replaced, and how many comparisons are kept.
    private static void Results(StringBuilder text)
    {
        var service = RunService.Instance;
        if (service.Current is { } run)
        {
            var table = RunCache.Rank(run, PanelState.Prefs.Metric, PanelState.Prefs.Unit);
            text.Append("\nbest ").Append(table.BestKey is { } best ? Ids.BriefKey(best) : "none");
            text.Append(CultureInfo.InvariantCulture, $"\ndied {table.DiedCount}");
            if (PanelPins.Earlier is { } earlier)
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"\nnarrow shown={run.Targets.Count} earlier={earlier.Targets} run={earlier.RunId} show={(PanelPins.EarlierSaved ? "yes" : "no")}"
                );
        }
        if (PanelState.Prefs.Pins.Count > 0)
            text.Append(
                CultureInfo.InvariantCulture,
                $"\npins shown={PanelPins.ShownPins.Count} waiting={PanelPins.WaitingPins.Count}"
            );
        text.Append(CultureInfo.InvariantCulture, $"\ncomparisons kept={service.KeptComparisons.Count}");
    }

    /// <summary>The filter as replies print it.</summary>
    public static string FilterLine()
    {
        var filter = PanelState.Prefs.Filter;
        var kinds = new List<string>();
        if (filter.Kinds.HasFlag(TargetTags.Monsters))
            kinds.Add("monsters");
        if (filter.Kinds.HasFlag(TargetTags.Dungeons))
            kinds.Add("dungeons");
        if (filter.Kinds.HasFlag(TargetTags.Slayer))
            kinds.Add("slayer");
        if (filter.Kinds.HasFlag(TargetTags.Masters))
            kinds.Add("masters");
        var areas = filter.Areas is { } chosen ? string.Join(',', chosen.Select(Ids.Brief)) : "all";
        return $"filter {(filter.CanFightOnly ? "can-fight" : "all")} kinds={(kinds.Count > 0 ? string.Join(',', kinds) : "none")} areas={areas} search={filter.Search}";
    }

    /// <summary>How many targets are shown, ignored and to run, as replies print it; null before targets are checked.</summary>
    public static string? TargetsLine() =>
        PanelState.Selection() is { } selection
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"targets shown={selection.Shown.Count} ignored={selection.Ignored.Count} to-run={selection.ToRun.Count}"
            )
            : null;
}
