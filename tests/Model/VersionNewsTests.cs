using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class VersionNewsTests
{
    [Fact]
    public void AVersionWithoutAnEntryHasNothing()
    {
        Assert.Empty(VersionNews.For("0.0.0-none"));
        Assert.Empty(VersionNews.For(""));
    }

    [Fact]
    public void EveryEntryHasLinesWithTextNewOnesFirst()
    {
        Assert.NotEmpty(VersionNews.Listed);
        foreach (var version in VersionNews.Listed)
        {
            var lines = VersionNews.For(version);
            Assert.NotEmpty(lines);
            Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line.Text)));
            Assert.Equal(lines.OrderBy(line => line.Fixed).ToList(), lines);
            Assert.Equal(lines.Count, lines.Select(line => line.Text).Distinct().Count());
        }
    }

    // About and the GitHub release say the same: the changelog's section for a version lists its news lines.
    [Fact]
    public void TheFirstReleaseMatchesItsChangelog()
    {
        const string version = "0.1.0-preview.1";
        var changelog = File.ReadAllText(Path.Combine(TestPaths.ModFolder(), "CHANGELOG.md"));
        var section = changelog[(changelog.IndexOf($"## {version}\n", StringComparison.Ordinal) + version.Length)..];
        var next = section.IndexOf("\n## ", StringComparison.Ordinal);
        var bullets = (next < 0 ? section : section[..next])
            .Split('\n')
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[2..])
            .ToList();
        Assert.Equal(bullets, VersionNews.For(version).Select(line => line.Text));
    }
}
