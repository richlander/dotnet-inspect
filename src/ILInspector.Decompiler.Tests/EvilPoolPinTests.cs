using System.Text.Json;
using Xunit;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The EVIL pool's version pin, checked as a file.
///
/// <para>The pool used to resolve <c>latest</c> on every sweep, so a fresh run measured
/// different code than any recorded run and its pool identity could never match a
/// baseline's. That is why the authored-corpus ratchet (#3245) shipped with no caller on
/// the weekly lane: there was nothing stable to compare against. #3353 pins the versions;
/// these tests guard the pin itself.</para>
///
/// <para>What is checked here is only what a file can prove. That the sweep <em>honors</em>
/// the pin is a property of <c>eng/prepare-decompiler-package-sweep.cs</c> and is
/// evidenced by real runs recorded on the PR, not by these tests -- <c>eng/</c> scripts
/// have no test harness, and inventing one that re-implements package acquisition would
/// be a second implementation rather than a gate.</para>
/// </summary>
[Trait("Area", "Corpus")]
public class EvilPoolPinTests
{
    const string PinRelativePath = "docs/data/nuget-top-packages.lock.json";
    const string ListRelativePath = "docs/data/nuget-top-packages.json";

    /// <summary>
    /// Every pin names a package, states a known status, and carries an exact version
    /// when it claims to pin one. No package is pinned twice.
    ///
    /// <para>A pin with an empty version is not a pin, and a package pinned twice makes
    /// the effective version depend on read order. An unrecognised status is worse than
    /// either: the sweep treats anything that is not <c>no-library</c> as a version to
    /// acquire, so a typo would turn a deliberate skip into an acquisition of
    /// <c>null</c>.</para>
    /// </summary>
    [Fact]
    public void EveryPinNamesAPackageAndAnExactVersion()
    {
        var pins = ReadPins();

        Assert.NotEmpty(pins);
        foreach (var pin in pins)
        {
            Assert.False(string.IsNullOrWhiteSpace(pin.Package), "a pin has no package name");
            Assert.Contains(pin.Status, (string[])["pinned", "no-library"]);
            if (pin.Status == "pinned")
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(pin.Version),
                    $"'{pin.Package}' claims to be pinned but states no version");
            }
        }

        var duplicates = pins
            .GroupBy(pin => pin.Package, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.Empty(duplicates);
    }

    /// <summary>
    /// Every pinned package is one the sweep would actually select.
    ///
    /// <para>An orphan pin is a package that left the ranked list -- harmless on its own,
    /// but it means the pin was refreshed against a list that no longer matches, and the
    /// next reader cannot tell which of the two is stale.</para>
    /// </summary>
    [Fact]
    public void NoPinNamesAPackageTheListDoesNotRank()
    {
        var ranked = ReadRankedPackages();
        var orphans = ReadPins()
            .Select(pin => pin.Package)
            .Where(package => !ranked.Contains(package))
            .ToArray();

        Assert.Empty(orphans);
    }

    /// <summary>
    /// Every ranked package has a pin, so the pool is fully determined by the file.
    ///
    /// <para>Equality, not a floor. A package the pin does not mention fails the sweep,
    /// and one that yields no library is pinned as <c>no-library</c> rather than left
    /// out -- which is what makes "nobody pinned this" distinguishable from "this
    /// contributes nothing." Nine of the top hundred are meta-packages or have an
    /// ambiguous primary library and take the second form.</para>
    ///
    /// <para>This is also what catches a refresh that replaces instead of merges. A
    /// windowed refresh once rewrote the file with three entries and dropped the other
    /// eighty-eight; a coverage floor would have caught that one, but equality catches
    /// the single dropped package too.</para>
    /// </summary>
    [Fact]
    public void EveryRankedPackageHasAPin()
    {
        var ranked = ReadRankedPackages();
        var pinned = ReadPins().Select(pin => pin.Package).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(ranked.Count >= 100, $"the ranked list holds {ranked.Count} packages");
        var unpinned = ranked.Except(pinned, StringComparer.OrdinalIgnoreCase).Order().ToArray();
        Assert.Empty(unpinned);
    }

    static IReadOnlyList<PinnedPackage> ReadPins()
    {
        string path = Path.Combine(AuthoredCorpusRatchetTests.FindRepositoryRoot(), PinRelativePath);
        Assert.True(File.Exists(path), $"{PinRelativePath} is missing, so the sweep cannot be reproducible");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        return document.RootElement.GetProperty("packages")
            .EnumerateArray()
            .Select(element => new PinnedPackage(
                element.GetProperty("package").GetString() ?? "",
                element.TryGetProperty("version", out var version) ? version.GetString() : null,
                element.TryGetProperty("tfm", out var tfm) ? tfm.GetString() : null,
                element.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "pinned"))
            .ToArray();
    }

    static HashSet<string> ReadRankedPackages()
    {
        string path = Path.Combine(AuthoredCorpusRatchetTests.FindRepositoryRoot(), ListRelativePath);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty("package").GetString() ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    sealed record PinnedPackage(string Package, string? Version, string? Tfm, string Status);
}
