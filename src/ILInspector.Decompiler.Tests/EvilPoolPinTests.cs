using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
/// <para>What is checked here is mostly what a file can prove. The one exception is
/// <see cref="TheSweepRefusesEveryPinFileShapeThisSuiteRefuses"/>, which runs the sweep's
/// own validator over tampered pin files so that the rules about a pin's shape live in
/// one place instead of two that drift. That the sweep <em>honors</em> the pin -- acquires
/// the pinned bytes and refuses anything else -- is still a property of
/// <c>eng/prepare-decompiler-package-sweep.cs</c> evidenced by real runs recorded on the
/// PR, because gating it would mean acquiring packages from this suite.</para>
/// </summary>
[Trait("Area", "Corpus")]
public class EvilPoolPinTests
{
    const string PinRelativePath = "docs/data/nuget-top-packages.lock.json";
    const string ListRelativePath = "docs/data/nuget-top-packages.json";

    /// <summary>
    /// Every rule this suite enforces on the pin file, the sweep enforces too.
    ///
    /// <para>This is the gate for that property, and it exists because the property kept
    /// failing. Three separate rounds of review on #3434 found a rule these tests applied
    /// that <c>eng/prepare-decompiler-package-sweep.cs</c> did not -- a bare version, the
    /// <c>schemaVersion</c>, and a <c>no-library</c> entry carrying an assembly hash. Each
    /// time, a pin file this suite went red on ran the sweep to exit 0. Two lists of rules
    /// over one file, and only the sweep's list can stop a run.</para>
    ///
    /// <para>So the sweep's list is now the only list. Each case below is a pin file this
    /// suite considers malformed; the assertion is that the sweep refuses it. The rules
    /// are not restated here -- restating them is what drifted. The sweep grows a rule and
    /// nothing here needs to change; the sweep loses one and the case that covered it goes
    /// red.</para>
    ///
    /// <para>The committed file is validated in the same invocation, so a case that
    /// refuses for the wrong reason (a broken harness writing garbage, say) cannot pass by
    /// making everything fail.</para>
    /// </summary>
    [Fact]
    public void TheSweepRefusesEveryPinFileShapeThisSuiteRefuses()
    {
        string root = AuthoredCorpusRatchetTests.FindRepositoryRoot();
        string committed = Path.Combine(root, PinRelativePath);
        var original = JsonNode.Parse(File.ReadAllText(committed))!.AsObject();

        (string Case, Action<JsonObject> Tamper)[] cases =
        [
            ("schema version the sweep cannot read",
                pin => pin["schemaVersion"] = 99),
            ("no packages at all",
                pin => pin.Remove("packages")),
            ("a null entry",
                pin => pin["packages"]!.AsArray().Insert(0, null)),
            ("an entry with no package name",
                pin => pin["packages"]![0]!["package"] = "   "),
            ("a package id that is not a bare NuGet id",
                pin => pin["packages"]![0]!["package"] = "newtonsoft.json@13.0.4"),
            ("a version with a directory traversal in it",
                pin => pin["packages"]![0]!["version"] = "../bad"),
            ("a version that does not start with a digit",
                pin => pin["packages"]![0]!["version"] = "v13.0.4"),
            ("a pinned entry with no sha256",
                pin => pin["packages"]![FirstIndexOf(pin, "pinned")]!["sha256"] = null),
            ("a pinned entry whose sha256 is not 64 lowercase hex",
                pin => pin["packages"]![FirstIndexOf(pin, "pinned")]!["sha256"] = "NOTAHASH"),
            ("a no-library entry carrying an assembly hash",
                pin => pin["packages"]![FirstIndexOf(pin, "no-library")]!["sha256"] = new string('0', 64)),
            ("a status the sweep does not know",
                pin => pin["packages"]![0]!["status"] = "probably-fine"),
            ("the same package pinned twice",
                pin => pin["packages"]!.AsArray().Add(pin["packages"]![0]!.DeepClone())),
        ];

        string scratch = Directory.CreateTempSubdirectory("evil-pin-shapes").FullName;
        try
        {
            var written = new List<(string Case, string Path)>();
            foreach (var (name, tamper) in cases)
            {
                var tampered = JsonNode.Parse(original.ToJsonString())!.AsObject();
                tamper(tampered);
                string path = Path.Combine(scratch, $"{written.Count:00}.lock.json");
                File.WriteAllText(path, tampered.ToJsonString());
                written.Add((name, path));
            }

            var verdicts = ValidateWithSweep(root, [committed, .. written.Select(w => w.Path)]);

            Assert.True(
                verdicts.TryGetValue(committed, out string? committedVerdict)
                    && committedVerdict is null,
                $"the committed pin file is not well formed by the sweep's own rules: {
                    (verdicts.GetValueOrDefault(committed) ?? "no verdict reported")}");

            var accepted = written
                .Where(w => verdicts.GetValueOrDefault(w.Path, "missing verdict") is null)
                .Select(w => w.Case)
                .ToArray();

            Assert.True(
                accepted.Length == 0,
                "the sweep accepts pin files this suite refuses, so the two disagree "
                + $"about what a pin is: {string.Join("; ", accepted)}");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    static int FirstIndexOf(JsonObject pin, string status)
    {
        var packages = pin["packages"]!.AsArray();
        for (int index = 0; index < packages.Count; index++)
        {
            if (packages[index]!["status"]?.GetValue<string>() == status)
                return index;
        }

        throw new InvalidOperationException(
            $"the committed pin file has no '{status}' entry to tamper with");
    }

    /// <summary>
    /// Runs the sweep's own pin validator over <paramref name="paths"/> and returns, per
    /// path, null when the sweep considers it well formed or the reason it gave.
    ///
    /// <para>One process for every case: the sweep is a file-based app, so each launch
    /// costs a couple of seconds, and this gate runs in PR CI where <c>Speed=Slow</c> is
    /// filtered out. A missing verdict is not treated as a pass by the caller.</para>
    /// </summary>
    static Dictionary<string, string?> ValidateWithSweep(string root, string[] paths)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
                ? host
                : "dotnet",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(Path.Combine(root, "eng", "prepare-decompiler-package-sweep.cs"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--validate-pin");
        foreach (string path in paths)
            startInfo.ArgumentList.Add(path);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("could not start the sweep");
        string output = process.StandardOutput.ReadToEnd();
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var verdicts = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line.Trim(), @"^Pin file '(?<path>.*)' (?<verdict>.*)\.$");
            if (match.Success)
            {
                verdicts[match.Groups["path"].Value] =
                    match.Groups["verdict"].Value == "is well formed" ? null : match.Groups["verdict"].Value;
            }
        }

        // A validator that printed nothing recognizable would otherwise read as "every
        // case refused", which is the shape of a gate that passes because it is broken.
        Assert.True(
            verdicts.Count == paths.Length,
            $"the sweep reported {verdicts.Count} verdicts for {paths.Length} pin files; "
            + $"stdout was:\n{output}\nstderr was:\n{errors}");

        return verdicts;
    }

    /// <summary>
    /// The packages pinned as <c>no-library</c> are exactly the nine known to contribute
    /// no assembly. Nothing else may claim that status.
    ///
    /// <para>Without this, <c>no-library</c> is a way to delete a package from the pool by
    /// editing one word: the entry stops owing an assembly and stops supplying one at the
    /// same time, so the two cancel and the sweep reports a reproducible pool that is
    /// simply smaller. Flipping all ninety-one left an empty pool and a green suite.</para>
    ///
    /// <para>The gate that actually decides the question is the sweep, which acquires
    /// every <c>no-library</c> entry at its pinned version and requires
    /// <c>TfmSelector</c> to still find no primary library -- a claim checked against the
    /// package rather than against this list. That gate needs the network, so it runs on
    /// the sweep lane and is evidenced by real runs. This test is the offline tripwire:
    /// it cannot tell whether a package ships a library, but it can tell that the set of
    /// packages claiming not to has changed, which is a deliberate act that belongs in a
    /// diff.</para>
    /// </summary>
    [Fact]
    public void OnlyTheKnownMetaPackagesClaimToContributeNoLibrary()
    {
        // Meta-packages that carry only dependencies, and packages whose primary library
        // is ambiguous. Refreshing the pin can legitimately change this set; changing it
        // here is how that becomes visible.
        string[] expected =
        [
            "grpc.tools",
            "microsoft.net.workloads.10.0.100",
            "newrelic.agent",
            "nunit",
            "nunit3testadapter",
            "swashbuckle.aspnetcore",
            "xunit",
            "xunit.core",
            "xunit.runner.visualstudio",
        ];

        var actual = ReadPins()
            .Where(pin => pin.Status == "no-library")
            .Select(pin => pin.Package)
            .OrderBy(package => package, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(package => package, StringComparer.Ordinal), actual);
    }

    /// <summary>
    /// Every pin names a package, states a known status, and carries an exact version.
    /// No package is pinned twice.
    ///
    /// <para>A pin with an empty version is not a pin, and a package pinned twice makes
    /// the effective version depend on read order. The version is required for both
    /// statuses because the sweep acquires both: a <c>no-library</c> entry is confirmed
    /// at its pinned version rather than believed, so a versionless one states a claim
    /// about nothing in particular.</para>
    ///
    /// <para>The rules themselves live in the sweep and are asserted by
    /// <see cref="TheSweepRefusesEveryPinFileShapeThisSuiteRefuses"/>, which hands it each
    /// of these shapes and requires a refusal. This test is the everyday reading of the
    /// committed file: it names which entry is wrong, which a pass/fail exit code from
    /// another process cannot.</para>
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
            Assert.False(
                string.IsNullOrWhiteSpace(pin.Version),
                $"'{pin.Package}' is pinned as {pin.Status} but states no version");
        }

        var duplicates = pins
            .GroupBy(pin => pin.Package, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.Empty(duplicates);
    }

    /// <summary>
    /// Every <c>pinned</c> entry names the bytes of the assembly it stands for.
    ///
    /// <para>A version and a TFM describe the request the sweep makes; only the hash
    /// describes the file it measures. A local NuGet cache entry whose contents were
    /// replaced -- by a partial extraction, a manual edit, or a tool writing into it --
    /// still answers that request with the pinned version and TFM, so without the hash
    /// the sweep would happily pool a different assembly and report success.</para>
    ///
    /// <para>Required rather than optional, and checked here as well as by the sweep:
    /// an entry allowed to omit the hash is an entry that can opt out of the check by
    /// omitting it, which is exactly how a null TFM became a wildcard earlier in this
    /// change. <c>no-library</c> entries have no assembly, so they must carry no hash --
    /// a hash there would describe a file that does not exist.</para>
    ///
    /// <para>This test gates the file. That the sweep <em>verifies</em> the hash is
    /// evidenced by real runs on the PR, for the reason given in the class summary.</para>
    /// </summary>
    [Fact]
    public void EveryPinnedPackageNamesTheBytesOfItsAssembly()
    {
        var pins = ReadPins();

        Assert.NotEmpty(pins);
        foreach (var pin in pins)
        {
            if (pin.Status == "pinned")
            {
                Assert.True(
                    pin.Sha256 is { Length: 64 } sha && sha.All(char.IsAsciiHexDigitLower),
                    $"'{pin.Package}' is pinned but states no sha256 of its assembly");
            }
            else
            {
                Assert.True(
                    pin.Sha256 is null,
                    $"'{pin.Package}' is pinned as {pin.Status} but states an assembly hash");
            }
        }
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
        var ranked = ReadRankedPackages().ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                element.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "pinned",
                element.TryGetProperty("sha256", out var sha) ? sha.GetString() : null))
            .ToArray();
    }

    /// <summary>
    /// Both files name bare NuGet package ids, not package references.
    ///
    /// <para>The extractor accepts <c>id@version</c>, so a ranked entry spelled that way
    /// acquired the embedded version while <c>--resolve-latest</c> reported it was
    /// sampling what ships today. It also defeats the duplicate check above: <c>x</c> and
    /// <c>x@1.0.0</c> are different strings and the same package, so the pool holds one
    /// library twice and every count still says what it should.</para>
    ///
    /// <para>The sweep refuses the spelling in both files and, separately, refuses a
    /// package that comes back under a different identity than the one selected. This is
    /// the offline half: an id that is not an id belongs in a diff.</para>
    /// </summary>
    [Fact]
    public void BothFilesNameBareNuGetIds()
    {
        static bool IsBare(string id) =>
            id.Length > 0 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

        string[] ids = [.. ReadRankedPackages(), .. ReadPins().Select(pin => pin.Package)];

        Assert.DoesNotContain(ids, id => !IsBare(id));
    }

    /// <summary>
    /// The ranked list names each package once.
    ///
    /// <para>Distinct ranks are not distinct packages. A list that ranks one package
    /// twice displaces a pinned package out of the top hundred and acquires the same
    /// assembly into two pool slots, so the pool holds a hundred files covering
    /// ninety-nine packages while every count in sight still reads a hundred. A padded
    /// denominator skews the ratchet exactly like a shortened one -- #3245's defect
    /// wearing the other sign.</para>
    ///
    /// <para>This reads the list as a list. An earlier draft of these tests collapsed it
    /// into a set before comparing, which is what let a duplicate through green: the set
    /// erased the very repetition being looked for.</para>
    /// </summary>
    [Fact]
    public void TheRankedListRanksEachPackageOnce()
    {
        var ranked = ReadRankedPackages();
        var repeated = ranked
            .GroupBy(package => package, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} x{group.Count()}")
            .Order()
            .ToArray();

        Assert.Empty(repeated);
    }

    /// <summary>
    /// The list ranks 1 through N with no gaps, so a rank range names a package set.
    ///
    /// <para>The sweep takes a window as "start rank, count", and a count is not a
    /// window: ranks need only be positive and distinct, so a list missing rank 2
    /// answers a request for ranks 1-2 with ranks 1 and 3. That is the right number of
    /// packages, every one of them pinned, and a pool that is not the one the caller
    /// named -- the same shape as #3245's shortened denominator, and as this change's
    /// own <c>Take()</c> defect.</para>
    ///
    /// <para>The sweep refuses a gap in the window it was asked for. This test says the
    /// committed list has none anywhere, so the refusal never fires in normal use and
    /// the top hundred is actually a hundred.</para>
    /// </summary>
    [Fact]
    public void TheRankedListRanksOneThroughNWithNoGaps()
    {
        var ranks = ReadRankedRanks();

        Assert.NotEmpty(ranks);
        Assert.Equal(Enumerable.Range(1, ranks.Count).ToArray(), ranks.Order().ToArray());
    }

    /// <summary>
    /// Reads the ranks preserving cardinality, so a caller can see a repeated rank.
    /// </summary>
    static IReadOnlyList<int> ReadRankedRanks()
    {
        string path = Path.Combine(AuthoredCorpusRatchetTests.FindRepositoryRoot(), ListRelativePath);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty("rank").GetInt32())
            .ToArray();
    }

    /// <summary>
    /// Reads the ranked list preserving cardinality, so a caller can see a repeat.
    /// Callers that want set semantics say so themselves.
    /// </summary>
    static IReadOnlyList<string> ReadRankedPackages()
    {
        string path = Path.Combine(AuthoredCorpusRatchetTests.FindRepositoryRoot(), ListRelativePath);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty("package").GetString() ?? "")
            .ToArray();
    }

    sealed record PinnedPackage(
        string Package, string? Version, string? Tfm, string Status, string? Sha256);
}
