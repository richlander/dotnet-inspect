using System.Text.RegularExpressions;

namespace ILInspector.Decompiler.Tests;

// Census + fingerprint for the "Dynamic" fixture category: test-local direct
// Roslyn compilations (CSharpCompilation.Create) in this project that live
// outside the Built (FixtureCatalog) and Generated (GeneratedFixtureCatalog)
// source-verification systems. Original-source verification and Fully Raised
// work cover only Built and Generated fixtures, so every remaining Dynamic site
// must be explicitly accounted for here with a retain reason (docs/fixture-governance.md).
//
// This is intentionally a reviewed manifest, not a live-derived list: adding or
// removing a CSharpCompilation.Create site forces a deliberate edit to
// RetainedDynamicSites (and its explanation), which is the fingerprint update
// step. A migrated site (e.g. CompileBackTypeIdentity -> Built fixture
// FixtureIds.DecompilerTypeIdentity) must disappear from the live scan and from
// this manifest simultaneously.
public sealed class DynamicCompilationSiteInventoryTests
{
    // File name -> (occurrence count, retain reason). Each entry is a Dynamic
    // site deliberately kept dynamic because runtime construction, an input
    // matrix, malformed input, or seam isolation is intrinsic to the test.
    static readonly IReadOnlyDictionary<string, (int Occurrences, string Reason)> RetainedDynamicSites =
        new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            // Product-output validity gates: compile decompiler-produced or
            // synthesized C# text that varies per case and assert it is valid.
            // The compiled source is a runtime product, not a fixed fixture.
            ["CatchEntryFoldingTests.cs"] = (1, "Product-output validity: compiles synthesized try/catch source per case."),
            ["CharElementStorePrinterTests.cs"] = (1, "Product-output validity: compiles printer-produced char-element-store source."),
            ["CoerceChokePointTests.cs"] = (1, "Product-output validity: compiles synthesized coercion source per case."),
            ["CSharpPrinterReceiverTests.cs"] = (1, "Product-output validity: compiles printer receiver-spelling output."),
            ["DataflowFactsTests.cs"] = (1, "Product-output validity: compiles synthesized dataflow source per case."),
            ["EnumCastPrinterTests.cs"] = (1, "Product-output validity: compiles printer-produced enum-cast source."),
            ["FinallyDisposePrinterTests.cs"] = (1, "Product-output validity: compiles printer-produced finally/dispose source."),
            ["MemberNameCollisionRenderingTests.cs"] = (1, "Product-output validity: compiles rendered colliding-member source."),
            ["MixedSignComparisonTests.cs"] = (1, "Product-output validity: compiles synthesized mixed-sign comparison source."),
            ["MultiDimensionalArrayPrinterTests.cs"] = (1, "Product-output validity: compiles printer-produced multidim-array source."),
            ["NonFiniteConstantPrinterTests.cs"] = (1, "Product-output validity: compiles printer-produced non-finite constant source."),
            ["NestedScopeNameCollisionTests.cs"] = (1, "Product-output validity: compiles rendered nested-scope collision source."),
            ["PrinterPrecedenceTests.cs"] = (1, "Product-output validity: compiles printer-produced precedence source per case."),
            ["IrImporterTests.cs"] = (1, "Product-output validity: compiles synthesized source feeding the IR importer."),
            ["MemberBodyProducerUnionTests.cs"] = (1, "Product-output validity: recompiles member-body producer output per rule set."),
            ["LadderRung6GateTests.cs"] = (1, "Product-output validity: compiles synthesized rung-6 gate source."),
            ["LadderRung9GateTests.cs"] = (1, "Product-output validity: compiles synthesized rung-9 gate source with feature parse options."),

            // Malformed-input / input-matrix / semantic-model seam isolation.
            ["ClosureDiagnosticEvidenceTests.cs"] = (6, "Input matrix + semantic-model seam: many compile-error/closure sources across a Theory."),
            ["FidelityCheckGeneratedFilterTests.cs"] = (2, "Seam isolation: exercises the generated-code filter over constructed compilations."),
            ["ValidityShellNoiseTests.cs"] = (1, "Seam isolation: injects deliberate shell noise into a validity compilation."),

            // Optimization / parse-option matrices intrinsic to the claim.
            ["IteratorReconstructionPassTests.cs"] = (1, "Optimization matrix: compiles the same source under Debug and Release."),
            ["CompilerFeatureOptionsTests.cs"] = (1, "Parse-option matrix: varies LanguageVersion/feature flags across compilations."),

            // Cross-assembly reference seam.
            ["CrossAssemblyMethodFactsTests.cs"] = (1, "Cross-assembly seam: constructs referencing compilations to test cross-assembly facts."),

            // Product-output validity under varying compilation options.
            ["ExpressionTreeLambdaTests.cs"] = (2, "Product-output validity: compiles synthesized expression-tree source and checks diagnostics/emit under varying compilation options (e.g. overflow checks)."),

            // Bespoke positive-source gates sharing one helper, each asserting
            // many per-fact expectations over a runtime-varying compilation
            // rather than a single addressable target type/member.
            ["TypeSourceCheckTests.cs"] = (1, "Runtime-varying validity gate: compiles per-case type source and checks binding."),
            ["UnsafeEmitterTests.cs"] = (1, "Runtime-varying validity gate: compiles per-case unsafe source with varying parse options."),
            ["DefaultParameterValidityTests.cs"] = (1, "Runtime-varying validity gate: compiles per-case default-parameter signatures."),
            ["ReturnToSenderPrototypeTests.cs"] = (1, "Runtime construction: builds a shell input assembly asserting 30+ facts."),
            ["ReturnToSenderFixtureCatalogTests.cs"] = (1, "Input-generation seam: builds a temporary input assembly for the RTS catalog."),
        };

    // Fingerprint. Migration of CompileBackTypeIdentity to the Built fixture
    // FixtureIds.DecompilerTypeIdentity removed exactly one file and one site.
    //   Before (origin/main): 30 files, 37 sites.
    //   After  (this change):  29 files, 36 sites.
    const int ExpectedDynamicFiles = 29;
    const int ExpectedDynamicSites = 36;

    // Migrated away from Dynamic in this change; must not reappear in the scan.
    static readonly string[] MigratedFiles = ["CompileBackTypeIdentityTests.cs"];

    [Fact]
    public void Manifest_MatchesLiveDynamicSites()
    {
        var live = ScanLiveSites();
        if (live is null)
        {
            Assert.Skip("Source tree not available next to the test binary; census scan skipped.");
            return;
        }

        var liveFiles = live.Keys.ToHashSet(StringComparer.Ordinal);
        var manifestFiles = RetainedDynamicSites.Keys.ToHashSet(StringComparer.Ordinal);

        var unaccounted = liveFiles.Except(manifestFiles).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            unaccounted.Length == 0,
            $"New Dynamic CSharpCompilation.Create sites are not accounted for in RetainedDynamicSites: {string.Join(", ", unaccounted)}. " +
            "Add a manifest entry with a retain reason, or migrate the site to a Built/Generated fixture.");

        var stale = manifestFiles.Except(liveFiles).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            stale.Length == 0,
            $"RetainedDynamicSites lists files that no longer contain a Dynamic site: {string.Join(", ", stale)}. " +
            "Remove the stale manifest entries and update the fingerprint.");

        foreach (var (file, live_) in live)
        {
            int expected = RetainedDynamicSites[file].Occurrences;
            Assert.True(
                live_ == expected,
                $"{file} has {live_} Dynamic site(s) but the manifest records {expected}. Update the entry and the fingerprint.");
        }
    }

    [Fact]
    public void MigratedFixtures_AreAbsentFromDynamicSites()
    {
        var live = ScanLiveSites();
        if (live is null)
        {
            Assert.Skip("Source tree not available next to the test binary; census scan skipped.");
            return;
        }

        foreach (var migrated in MigratedFiles)
        {
            Assert.False(
                live.ContainsKey(migrated),
                $"{migrated} was migrated to a Built/Generated fixture but still contains a test-local CSharpCompilation.Create.");
            Assert.False(
                RetainedDynamicSites.ContainsKey(migrated),
                $"{migrated} was migrated; it must not appear in the retained-Dynamic manifest.");
        }
    }

    [Fact]
    public void DynamicSiteCount_MatchesExpectedFingerprint()
    {
        Assert.Equal(ExpectedDynamicFiles, RetainedDynamicSites.Count);
        Assert.Equal(ExpectedDynamicSites, RetainedDynamicSites.Values.Sum(entry => entry.Occurrences));

        var live = ScanLiveSites();
        if (live is null)
        {
            Assert.Skip("Source tree not available next to the test binary; census scan skipped.");
            return;
        }

        Assert.Equal(ExpectedDynamicFiles, live.Count);
        Assert.Equal(ExpectedDynamicSites, live.Values.Sum());
    }

    [Fact]
    public void EveryRetainedSite_HasReason()
    {
        Assert.All(RetainedDynamicSites, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Value.Reason), $"{entry.Key} has no retain reason.");
            Assert.True(entry.Value.Occurrences > 0, $"{entry.Key} has a non-positive occurrence count.");
        });
    }

    // Live scan of this project's .cs sources for test-local CSharpCompilation.Create
    // occurrences. Returns null when the source tree is not reachable from the
    // running binary (matches the repo convention of skipping rather than
    // false-failing when sources are absent — see InverseArchitectureTests).
    static IReadOnlyDictionary<string, int>? ScanLiveSites()
    {
        string? projectDir = FindProjectDirectory();
        if (projectDir is null)
            return null;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(path);
            // The census guard describes the pattern in prose/manifest strings;
            // exclude it so it never counts itself.
            if (fileName == "DynamicCompilationSiteInventoryTests.cs")
                continue;

            int occurrences = Regex.Matches(File.ReadAllText(path), @"CSharpCompilation\s*\.\s*Create\s*\(").Count;
            if (occurrences > 0)
                counts[fileName] = occurrences;
        }

        return counts;
    }

    static string? FindProjectDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "ILInspector.Decompiler.Tests");
            if (File.Exists(Path.Combine(dir.FullName, "dotnet-inspect.slnx")) && Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
