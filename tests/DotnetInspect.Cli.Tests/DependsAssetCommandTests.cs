using System.CommandLine;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Cache;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class DependsAssetCommandTests
{
    private static string NuspecFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
            "manifest.nuspec");

    private static string AssetsFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
            "project.assets.json");

    private static string ProjectDirectoryFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.ProjectDirectory();

#if DEBUG
    [Fact]
    public async Task EvidenceEnvelopePreservesOrdinaryJsonAndPublishesCompleteFrame()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");
        string[] ordinaryArguments =
        [
            "depends",
            "--project",
            AssetsFixture,
            "--json",
            "--compact",
        ];

        var ordinary = await RunCapturedAsync(ordinaryArguments);
        var withEvidence = await RunCapturedAsync(
        [
            .. ordinaryArguments,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(0, ordinary.ExitCode);
        Assert.Empty(ordinary.Error);
        Assert.Equal(0, withEvidence.ExitCode);
        Assert.Equal(ordinary.Output, withEvidence.Output);
        Assert.Equal(
            $"Evidence envelope: {sidecar}{Environment.NewLine}",
            withEvidence.Error);

        using JsonDocument document =
            JsonDocument.Parse(await File.ReadAllTextAsync(
                sidecar,
                TestContext.Current.CancellationToken));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(
            "asset-dependencies",
            root.GetProperty("result_kind").GetString());
        Assert.True(root.TryGetProperty("content", out _));
        Assert.True(root.TryGetProperty("share", out _));
        Assert.True(root.TryGetProperty("diagnostics", out _));
        Assert.True(root.TryGetProperty("evidence", out _));
        Assert.False(root.TryGetProperty("inspection", out _));
    }

    [Fact]
    public async Task EvidenceEnvelopePreservesOrdinaryMarkdown()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-markdown-");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");
        string[] ordinaryArguments =
        [
            "depends",
            "--project",
            AssetsFixture,
        ];

        var ordinary = await RunCapturedAsync(ordinaryArguments);
        var withEvidence = await RunCapturedAsync(
        [
            .. ordinaryArguments,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(0, ordinary.ExitCode);
        Assert.Empty(ordinary.Error);
        Assert.Equal(0, withEvidence.ExitCode);
        Assert.Equal(ordinary.Output, withEvidence.Output);
        Assert.Equal(
            $"Evidence envelope: {sidecar}{Environment.NewLine}",
            withEvidence.Error);
    }

    [Fact]
    public async Task EvidenceEnvelopePreservesOrdinaryTree()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-tree-");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");
        string[] ordinaryArguments =
        [
            "depends",
            "--project",
            AssetsFixture,
            "-S",
            DependsAssetSections.DependencyHierarchy,
            "--tree",
        ];

        var ordinary = await RunCapturedAsync(ordinaryArguments);
        var withEvidence = await RunCapturedAsync(
        [
            .. ordinaryArguments,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(0, ordinary.ExitCode);
        Assert.Empty(ordinary.Error);
        Assert.Equal(0, withEvidence.ExitCode);
        Assert.Equal(ordinary.Output, withEvidence.Output);
        Assert.Equal(
            $"Evidence envelope: {sidecar}{Environment.NewLine}",
            withEvidence.Error);
    }

    [Theory]
    [InlineData(DependsAssetSections.Roots)]
    [InlineData(DependsAssetSections.DependencyGroups)]
    [InlineData(DependsAssetSections.RestoredPackages)]
    [InlineData(DependsAssetSections.RestoredEdges)]
    public async Task EvidenceEnvelopePreservesSelectedDiagnosticView(
        string section)
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-view-");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");
        string[] ordinaryArguments =
        [
            "depends",
            "--project",
            AssetsFixture,
            "-S",
            section,
            "--json",
            "--compact",
        ];

        var ordinary = await RunCapturedAsync(ordinaryArguments);
        var withEvidence = await RunCapturedAsync(
        [
            .. ordinaryArguments,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(0, ordinary.ExitCode);
        Assert.Empty(ordinary.Error);
        Assert.Equal(0, withEvidence.ExitCode);
        Assert.Equal(ordinary.Output, withEvidence.Output);
        Assert.Equal(
            $"Evidence envelope: {sidecar}{Environment.NewLine}",
            withEvidence.Error);
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                sidecar,
                TestContext.Current.CancellationToken));
        Assert.True(document.RootElement.TryGetProperty(
            "evidence",
            out _));
    }

    [Fact]
    public async Task EvidenceEnvelopePreservesOrdinaryJsonAtDistinctOutputPath()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-output-");
        string primary = Path.Combine(directory.FullName, "ordinary.json");
        string sidecar = Path.Combine(directory.FullName, "evidence.json");
        string[] ordinaryArguments =
        [
            "depends",
            "--project",
            AssetsFixture,
            "--json",
            "--compact",
        ];

        var ordinary = await RunCapturedAsync(ordinaryArguments);
        var withEvidence = await RunCapturedAsync(
        [
            .. ordinaryArguments,
            "--out",
            primary,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(0, ordinary.ExitCode);
        Assert.Empty(ordinary.Error);
        Assert.Equal(0, withEvidence.ExitCode);
        Assert.Empty(withEvidence.Output);
        Assert.Equal(
            $"Evidence envelope: {sidecar}{Environment.NewLine}",
            withEvidence.Error);
        using JsonDocument expected = JsonDocument.Parse(ordinary.Output);
        using JsonDocument actual = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                primary,
                TestContext.Current.CancellationToken));
        Assert.True(JsonElement.DeepEquals(
            expected.RootElement,
            actual.RootElement));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PairedEnvelopeWritesEqualBaselineToDistinctOutputFile(
        bool selector)
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-paired-");
        string primary = Path.Combine(
            directory.FullName,
            "baseline.json");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");
        string[] envelope =
            selector
                ? ["-o", "envelope"]
                : ["--envelope"];

        var result = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            .. envelope,
            "--compact",
            "--out",
            primary,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(
            $"Evidence envelope: {sidecar}{Environment.NewLine}",
            result.Error);

        using JsonDocument baseline =
            JsonDocument.Parse(await File.ReadAllTextAsync(
                primary,
                TestContext.Current.CancellationToken));
        using JsonDocument enriched =
            JsonDocument.Parse(await File.ReadAllTextAsync(
                sidecar,
                TestContext.Current.CancellationToken));
        JsonElement baselineRoot = baseline.RootElement;
        JsonElement enrichedRoot = enriched.RootElement;

        Assert.False(baselineRoot.TryGetProperty("evidence", out _));
        Assert.True(enrichedRoot.TryGetProperty("evidence", out _));
        Assert.True(JsonElement.DeepEquals(
            baselineRoot.GetProperty("content"),
            enrichedRoot.GetProperty("content")));
        Assert.True(JsonElement.DeepEquals(
            baselineRoot.GetProperty("share"),
            enrichedRoot.GetProperty("share")));
        Assert.True(JsonElement.DeepEquals(
            baselineRoot.GetProperty("diagnostics"),
            enrichedRoot.GetProperty("diagnostics")));
    }

    [Fact]
    public async Task EvidenceEnvelopeRejectsMissingPathBeforeAcquisition()
    {
        var result = await RunCapturedAsync(
        [
            "depends",
            "--project",
            "missing-project.csproj",
            "--evidence-envelope",
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Required argument missing for option: '--evidence-envelope'",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "missing-project",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceEnvelopeRejectsOptionShapedPathBeforeAcquisition()
    {
        string requestedPath =
            $"-depends-evidence-{Guid.NewGuid():N}.json";
        string unintendedPath = Path.GetFullPath(requestedPath);
        try
        {
            var result = await RunCapturedAsync(
            [
                "depends",
                "--package",
                "No.Such.Package@1.0.0",
                "--evidence-envelope",
                requestedPath,
            ]);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains(
                "not an option-shaped value",
                result.Error,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "No.Such.Package",
                result.Error,
                StringComparison.Ordinal);
            Assert.False(File.Exists(unintendedPath));
        }
        finally
        {
            if (File.Exists(unintendedPath))
                File.Delete(unintendedPath);
        }
    }

    [Fact]
    public async Task EvidenceEnvelopeRejectsTypeModeBeforeAcquisition()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-type-");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");

        var result = await RunCapturedAsync(
        [
            "depends",
            "No.Such.Type",
            "--platform",
            "System.Private.CoreLib",
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--evidence-envelope is supported only by asset-mode depends",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(sidecar));
    }

    [Fact]
    public async Task EvidenceEnvelopeRejectsPortableDestinationCollision()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-collision-");
        string primary = Path.Combine(
            directory.FullName,
            "Primary.json");
        string sidecar = Path.Combine(
            directory.FullName,
            "primary.JSON");

        var result = await RunCapturedAsync(
        [
            "depends",
            "--project",
            "missing-project.csproj",
            "--out",
            primary,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--out and --evidence-envelope must name distinct files",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(primary));
        Assert.False(File.Exists(sidecar));
    }

    [Fact]
    public async Task EvidenceEnvelopeRejectsDirectoryBeforeAcquisition()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-directory-");

        var result = await RunCapturedAsync(
        [
            "depends",
            "--project",
            "missing-project.csproj",
            "--evidence-envelope",
            directory.FullName,
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires a file destination, not a directory",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "missing-project",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceEnvelopeRejectsMissingParentBeforeAcquisition()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-parent-");
        string sidecar = Path.Combine(
            directory.FullName,
            "missing",
            "evidence.json");

        var result = await RunCapturedAsync(
        [
            "depends",
            "--project",
            "missing-project.csproj",
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires an existing parent directory",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "missing-project",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(sidecar));
    }

    [Fact]
    public async Task EvidencePublicationFailurePreservesOrdinaryOutput()
    {
        using var parent =
            new TemporaryTestDirectory("depends-evidence-failure-");
        string destination = Directory.CreateDirectory(
            Path.Combine(parent.FullName, "destination.json")).FullName;
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Project,
                    AssetsFixture),
            ],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            EvidenceEnvelopePath = destination,
        };

        var result = await ConsoleCapture.RunAsync(
            () => DependsCommand.ExecuteAssetDependsAsync(
                options,
                TestContext.Current.CancellationToken));

        Assert.Equal(1, result.ExitCode);
        using JsonDocument _ = JsonDocument.Parse(result.Output);
        Assert.Contains(
            "Evidence envelope publication failed",
            result.Error,
            StringComparison.Ordinal);
        Assert.True(Directory.Exists(destination));
        Assert.Empty(
            Directory.EnumerateFiles(
                parent.FullName,
                $".{Path.GetFileName(destination)}.*.tmp"));
    }

    [Fact]
    public async Task EvidenceEnvelopePreservesProjectablePackageShare()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-share-available-");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");
        string[] ordinaryArguments =
        [
            "depends",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "net10.0",
            "--share",
            "packet",
        ];

        var ordinary = await RunCapturedAsync(ordinaryArguments);
        var result = await RunCapturedAsync(
        [
            .. ordinaryArguments,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(0, ordinary.ExitCode);
        Assert.Empty(ordinary.Error);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ordinary.Output, result.Output);
        Assert.Equal(
            $"Evidence envelope: {sidecar}{Environment.NewLine}",
            result.Error);
        string packet = result.Output.Trim();
        WorkspaceSharePacket decoded = WorkspaceSharePacketCodec.Decode(
            packet,
            TestContext.Current.CancellationToken);
        WorkspaceShareTab tab = Assert.Single(decoded.Tabs);
        Assert.Equal("System.Text.Json", tab.Source);
        Assert.Equal("10.0.0", tab.Version);
        Assert.Equal("net10.0", tab.Framework);

        using JsonDocument document =
            JsonDocument.Parse(await File.ReadAllTextAsync(
                sidecar,
                TestContext.Current.CancellationToken));
        JsonElement share = document.RootElement.GetProperty("share");
        Assert.Equal("available", share.GetProperty("kind").GetString());
        Assert.Equal(
            packet,
            share.GetProperty("packet").GetString());
        Assert.Equal(
            WorkspaceShareOutput.UrlPrefix + packet,
            share.GetProperty("full_url").GetString());
    }

    [Fact]
    public async Task EvidenceEnvelopeRetainsShareOptionValidation()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-share-options-");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");

        var result = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "No.Such.Package@1.0.0",
            "--tfm",
            "net10.0",
            "--share",
            "packet",
            "--json",
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--share selects packet or URL output",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "No.Such.Package",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(sidecar));
    }

    [Theory]
    [InlineData("--envelope")]
    [InlineData("--out")]
    public async Task EvidenceEnvelopeRejectsUnrepresentableShareOutput(
        string outputOption)
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-share-output-");
        string primary = Path.Combine(
            directory.FullName,
            "ordinary.json");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");
        var arguments = new List<string>
        {
            "depends",
            "--package",
            "No.Such.Package@1.0.0",
            "--tfm",
            "net10.0",
            "--share",
            "packet",
            outputOption,
        };
        if (outputOption == "--out")
            arguments.Add(primary);
        arguments.AddRange(["--evidence-envelope", sidecar]);

        var result = await RunCapturedAsync([.. arguments]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--share cannot be combined with --envelope or --out",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "No.Such.Package",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(primary));
        Assert.False(File.Exists(sidecar));
    }

    [Theory]
    [InlineData("--rows")]
    [InlineData("-n")]
    public async Task PairedEnvelopeRejectsPostServiceRowSelectionBeforeAcquisition(
        string rowOption)
    {
        using var directory =
            new TemporaryTestDirectory("depends-paired-envelope-rows-");
        string baseline = Path.Combine(
            directory.FullName,
            "baseline.json");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");

        var result = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "No.Such.Package@1.0.0",
            "--envelope",
            "--out",
            baseline,
            "--evidence-envelope",
            sidecar,
            rowOption,
            "1",
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"--envelope cannot be combined with {rowOption}",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "No.Such.Package",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(baseline));
        Assert.False(File.Exists(sidecar));
    }

    [Fact]
    public async Task EvidenceEnvelopeRejectsMixedJsonlProjectionBeforeAcquisition()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-jsonl-projection-");
        string ordinary = Path.Combine(
            directory.FullName,
            "ordinary.jsonl");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");

        var result = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "No.Such.Package@1.0.0",
            "-S",
            $"{DependsAssetSections.DependencyHierarchy},{DependsAssetSections.Failures}",
            "--jsonl",
            "--columns",
            "Reason",
            "--out",
            ordinary,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Projected JSONL columns cannot represent",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "No.Such.Package",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(ordinary));
        Assert.False(File.Exists(sidecar));
    }

    [Fact]
    public async Task EvidenceEnvelopeRejectsUnknownProjectionBeforeAcquisition()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-static-projection-");
        string ordinary = Path.Combine(
            directory.FullName,
            "ordinary.txt");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");

        var result = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "No.Such.Package@1.0.0",
            "-S",
            DependsAssetSections.Roots,
            "--table",
            "--columns",
            "NoSuchColumn",
            "--out",
            ordinary,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "No columns matched projection: NoSuchColumn",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "No.Such.Package",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(ordinary));
        Assert.False(File.Exists(sidecar));
    }

    [Fact]
    public async Task EvidenceEnvelopeCountUsesOrdinaryOutputDestination()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-count-");
        string primary = Path.Combine(directory.FullName, "count.txt");
        string sidecar = Path.Combine(directory.FullName, "evidence.json");

        var result = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "-S",
            DependsAssetSections.Roots,
            "--count",
            "--out",
            primary,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(
            $"Evidence envelope: {sidecar}{Environment.NewLine}",
            result.Error);
        Assert.Equal(
            "1\n",
            await File.ReadAllTextAsync(
                primary,
                TestContext.Current.CancellationToken));
        Assert.True(File.Exists(sidecar));
    }

    [Fact]
    public async Task EvidenceEnvelopeLocatorPrecedesFinalShareRefusal()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-share-");
        const string packageId = "Sidecar.Share";
        const string version = "1.0.0";
        WriteLocalSourcePackage(
            directory.FullName,
            packageId,
            version,
            dependenciesXml: "");
        string packagePath = Path.Combine(
            directory.FullName,
            $"{packageId}.{version}.nupkg");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");

        string[] ordinaryArguments =
        [
            "depends",
            "--package",
            packagePath,
            "--tfm",
            "net8.0",
            "--share",
            "packet",
        ];
        var ordinary = await RunCapturedAsync(ordinaryArguments);
        var result = await RunCapturedAsync(
        [
            .. ordinaryArguments,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.Equal(1, ordinary.ExitCode);
        Assert.Empty(ordinary.Output);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.True(File.Exists(sidecar));
        string[] errorLines = result.Error.Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            $"Evidence envelope: {sidecar}",
            errorLines[^2]);
        Assert.Equal(
            ordinary.Error.Trim(),
            errorLines[^1]);
    }
#endif

    [Fact]
    public async Task TypeEnvelopeRejectsRenderedLineSelectionBeforeAcquisition()
    {
        var result = await RunCapturedAsync(
        [
            "depends",
            "No.Such.Type",
            "--platform",
            "System.Private.CoreLib",
            "--envelope",
            "-n",
            "1",
            "--lines",
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output",
            result.Error);
        Assert.DoesNotContain(
            "not found",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssetProjectionPreservesTheOperationEnvelope()
    {
        var summary = new DependencyInspectionSummary(
            DependencyInspectionRootSetCompletion.Complete,
            RequestedRoots: 1,
            AdmittedRoots: 1,
            FailedRoots: 0,
            DependencyInspectionTraversalCompletion.NotRequested,
            RequestedDepth: null,
            HierarchyOccurrences: 0,
            CanonicalNodes: 0,
            Relationships: 0,
            DependencyInspectionEvidencePhaseCompletion.NotRequested,
            DependencyInspectionEvidencePhaseCompletion.NotRequested,
            DependencyInspectionPruningSummary.NotRequested,
            IsPrefixRootSet: false,
            PackagePrefix: null);
        DependencyHierarchyDocument hierarchy =
            DependencyHierarchyDocument.Empty;
        var contentRoot = new DependencyInspectionRoot(
            new DependencyRootOccurrenceIdentity(1),
            DependencyInspectionRootKind.Library,
            new InertString(TextPolicy.Field, "example.dll"),
            DependencyInspectionRootState.Admitted,
            DependencyIdentity: null,
            DependencyInspectionTraversalCompletion.NotRequested,
            DependencyInspectionEvidenceAvailability.NotRequested,
            DependencyInspectionEvidencePhaseCompletion.NotRequested,
            DependencyInspectionSelectionStatus.NotRequested,
            DependencyInspectionEvidenceAvailability.NotRequested,
            DependencyInspectionEvidencePhaseCompletion.NotRequested);
        var content = new DependencyInspectionContent(
            summary,
            hierarchy,
            [contentRoot],
            [],
            [],
            []);
        var inspection = new InspectionEnvelope<DependencyInspectionContent>(
            content,
            new InspectionShare.NonProjectable(
                "dependencies",
                "Test inspection."));
        var root = new DependsRootRow(
            contentRoot,
            source: "Path",
            identityKind: null,
            identity: null);

        var projection = new DependsAssetProjection(
            inspection,
            content.Summary,
            hierarchy.BackingGraph,
            hierarchy,
            HierarchyRows: [],
            [root],
            content.Dependencies,
            content.Pruning,
            RestoredEdges: [],
            content.Failures,
            DependencyGroups: [],
            RestoredPackages: [],
            Evidence: null);

        Assert.Same(inspection, projection.Inspection);
        Assert.Same(content, projection.Content);
        Assert.Same(summary, projection.Summary);
        Assert.Same(hierarchy, projection.Hierarchy);
        Assert.Same(hierarchy.BackingGraph, projection.Graph);
        Assert.Same(contentRoot, Assert.Single(projection.Content.Roots));
        Assert.Equal(
            new DependencyRootOccurrenceIdentity(1),
            Assert.Single(projection.Content.Roots).Identity);
        Assert.Null(projection.Evidence);
        Assert.Empty(projection.Content.Dependencies);
        Assert.Empty(projection.Content.Pruning);
        Assert.Empty(projection.Content.Failures);
    }

    [Fact]
    public void AssetProjectionExcludesLivePackageAuthorityFromContent()
    {
        var runtimeFailure = new PackageAuthorityFailure(
            new InertString(TextPolicy.Field, "private"),
            PackageAuthorityFailureKind.Transport,
            "The source failed.");
        var graph = new DependencyGraphDocument(
            [],
            [
                new DependencyGraphNode(
                    0,
                    new DependencyGraphNodeIdentity.Package(
                        "Example.Package",
                        "1.0.0"),
                    new InertString(
                        TextPolicy.Field,
                        "Example.Package@1.0.0")),
            ],
            [],
            [
                new DependencyGraphPackageProjection(
                    0,
                    0,
                    PackageDependencyTraversalProjectionKind
                        .CandidateAcquired,
                    PackageDependencyTraversalProjectionExpansion.Expanded,
                    Evidence: null,
                    Candidate: null,
                    RootOccurrence: null,
                    [
                        DependencyInspectionPackageAuthorityFailure.Create(
                            runtimeFailure),
                    ])
                {
                    RuntimeDiagnostics = [runtimeFailure],
                },
            ],
            []);
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest([], []));
        var request = new DependencyInspectionOperationRequest(
            new DependencyInspectionPlan(
                Declarations: false,
                RestoredRelationships: false,
                Traversal: true,
                Pruning: false,
                RequestedFramework: null,
                RequestedDepth: null),
            requestedRoots: 0,
            isPrefixRootSet: false,
            outcome,
            admittedRootOccurrences: [],
            failedRootOccurrences: [],
            roots: [],
            graph,
            additionalFailures: null,
            pruning: null,
            pruningFailures: null,
            DependencyInspectionPruningSummary.NotRequested);
        DependencyInspectionContent content =
            DependencyInspectionOperation.Execute(request).Content;

        Assert.Single(graph.PackageProjections[0].RuntimeDiagnostics);
        Assert.Empty(
            content.Hierarchy.BackingGraph.PackageProjections[0]
                .RuntimeDiagnostics);
        Assert.Single(
            content.Hierarchy.BackingGraph.PackageProjections[0].Diagnostics);
    }

    [Fact]
    public void DependencyOperationDetachesLivePackageSourcesFromContent()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageDependencyEvidenceRootFailure.PackageProfile failure =
            PackageDependencyEvidenceQuery.CreatePackageProfileFailure(
                new PackageProfileFailure(
                    "Example.Bad",
                    "1.0.0",
                    source.Source,
                    PackageProfileFailureKind.SearchContract,
                    "Search failed"));
        var completion = new PackageDependencyEvidencePackagePrefixCompletion(
            new InertString(TextPolicy.Field, "Example."),
            failure.Source,
            candidates: 1,
            matches: 0,
            failures: 1,
            PackageSearchTruncationReason.None);
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [],
                    [failure],
                    packagePrefixCompletion: completion));
        PackageDependencyEvidenceSourceIdentity liveSource =
            outcome.RootSet.PackagePrefixCompletion!.Source;
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Example.Root", "1.0.0");
        var rootIdentity =
            new PackageDependencyEvidenceRootIdentity.Package(coordinate);
        var evidenceRoot = new PackageDependencyEvidenceRoot(
            rootIdentity,
            new PackageDependencyEvidenceRootProvenance.Package(
                PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
                PackageManifestIdentityProvenance.ExpectedCoordinate,
                new InertString(TextPolicy.Field, "Example.Root"),
                liveSource),
            new InertString(TextPolicy.Field, "Example.Root@1.0.0"),
            new PackageDependencyEvidenceDeclarationResult.NotApplicable(),
            new PackageDependencyEvidenceSelection(
                PackageDependencyEvidenceSelectionStatus.NoDependencyGroups,
                SelectedGroup: null,
                SelectedSourceOccurrence: null,
                RequestedFramework: null,
                SelectedFramework: null),
            RestoredTarget: null,
            new PackageDependencyEvidenceRelationshipResult.NotApplicable(),
            new PackageDependencyEvidenceProcessingResult.NotApplicable());
        var groupIdentity = new PackageDependencyEvidenceGroupIdentity.Package(
            rootIdentity,
            IsImplicitManifestGroup: true,
            FirstSourceOccurrence: 0);
        var declarationIdentity =
            new PackageDependencyEvidenceDeclarationIdentity(
                groupIdentity,
                "example.dependency");
        var declaration = new PackageDependencyEvidenceDeclaration(
            declarationIdentity,
            "example.dependency",
            "[1.0.0]",
            new InertString(TextPolicy.Field, "Example.Dependency"),
            new InertString(TextPolicy.Field, "[1.0.0]"),
            SourceOccurrenceCount: 1,
            PackageDependencyEvidenceAuthorship.LibraryDeclared);
        var applicability = new PackageHouseDependencyPruningApplicability(
            evidenceRoot,
            declaration,
            PackageHouseDependencyPruningApplicabilityState.CandidateRequired,
            Processing: null,
            TargetUnavailableReason: null);
        var graph = new DependencyGraphDocument(
            [],
            [
                new DependencyGraphNode(
                    0,
                    new DependencyGraphNodeIdentity.Package(
                        "Example.Root",
                        "1.0.0"),
                    new InertString(
                        TextPolicy.Field,
                        "Example.Root@1.0.0")),
            ],
            [],
            [
                new DependencyGraphPackageProjection(
                    0,
                    0,
                    PackageDependencyTraversalProjectionKind.RootSupplied,
                    PackageDependencyTraversalProjectionExpansion.Expanded,
                    evidenceRoot,
                    Candidate: null,
                    RootOccurrence: null,
                    Diagnostics: []),
            ],
            []);
        var pruning = new DependencyInspectionPruning(
            RootOccurrence: 1,
            rootIdentity,
            evidenceRoot.Display,
            declarationIdentity,
            new InertString(TextPolicy.Field, "net8.0"),
            new InertString(TextPolicy.Field, "net8.0"),
            "example.dependency",
            new InertString(TextPolicy.Field, "Example.Dependency"),
            "[1.0.0]",
            new InertString(TextPolicy.Field, "[1.0.0]"),
            CandidateVersion: null,
            PlatformFamily: null,
            PlatformTargetFramework: null,
            PlatformVersion: null,
            PlatformProvidedVersion: null,
            DependencyInspectionPruningDisposition.NotEvaluated,
            "Candidate required.",
            applicability,
            CandidateOutcome: null,
            Result: null);
        var request = new DependencyInspectionOperationRequest(
            new DependencyInspectionPlan(
                Declarations: false,
                RestoredRelationships: false,
                Traversal: true,
                Pruning: true,
                RequestedFramework: null,
                RequestedDepth: null),
            requestedRoots: 1,
            isPrefixRootSet: true,
            outcome,
            admittedRootOccurrences: [],
            failedRootOccurrences: [null],
            roots: [],
            graph,
            additionalFailures: null,
            pruning: [pruning],
            pruningFailures: null,
            new DependencyInspectionPruningSummary(
                DependencyInspectionPruningCompletion.Complete,
                Roots: 1,
                Declarations: 1,
                Evaluated: 0,
                Delegated: 0,
                Retained: 0,
                NotEvaluated: 1,
                SourceBounded: 0,
                Failed: 0));
        EvidenceInspectionEnvelope<
            DependencyInspectionContent,
            DependencyInspectionEvidenceDocument> enriched =
            DependencyInspectionOperation.ExecuteWithEvidence(request);
        DependencyInspectionContent content = enriched.Inspection.Content;

        Assert.True(
            enriched.Evidence.PackageInputs.RootSet
                .PackagePrefixCompletion!.Source
                .MatchesRuntimeAssociation(source.Source.Association));
        Assert.False(
            content.Summary.PackagePrefix!.Source
                .MatchesRuntimeAssociation(source.Source.Association));
        Assert.False(GraphContentSource(content.Hierarchy.BackingGraph)
            .MatchesRuntimeAssociation(source.Source.Association));
        Assert.False(
            Assert.IsType<DependencyInspectionFailure.Evidence>(
                    Assert.Single(content.Failures))
                .Value.Source!
                .MatchesRuntimeAssociation(source.Source.Association));
        Assert.False(
            RootContentSource(Assert.Single(content.Pruning)
                    .Applicability.Root)
                .MatchesRuntimeAssociation(source.Source.Association));

        static PackageDependencyEvidenceSourceIdentity GraphContentSource(
            DependencyGraphDocument graph) =>
            RootContentSource(
                Assert.Single(graph.PackageProjections).Evidence!);

        static PackageDependencyEvidenceSourceIdentity RootContentSource(
            PackageDependencyEvidenceRoot root) =>
            Assert.IsType<PackageDependencyEvidenceRootProvenance.Package>(
                    root.Provenance)
                .Source!;
    }

    [Fact]
    public void ModeValidation_PreservesTypeScopesAndRejectsCrossModeGestures()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();

        Assert.Empty(root.Parse(
        [
            "depends",
            "Example.Type",
            "--package",
            "Example.Package",
            "--library",
            "Example.dll",
            "--project",
            "Example.csproj",
        ]).Errors);

        Assert.Contains(
            root.Parse(["depends", "Example.Type", "--nuspec", "x.nuspec"])
                .Errors,
            error => error.Message.Contains(
                "only without a positional type",
                StringComparison.Ordinal));
        Assert.Contains(
            root.Parse(["depends", "--package", "Example", "--platform"])
                .Errors,
            error => error.Message.Contains(
                "only with a positional type",
                StringComparison.Ordinal));
        Assert.Contains(
            root.Parse(["depends", "--project", "Example", "--depth", "0"])
                .Errors,
            error => error.Message.Contains(
                "positive integer",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task TypeDepthBoundMatchesUnboundedShortestPathEdges()
    {
        PersistentCache.Initialize("dotnet-inspect-test");
        string[] common =
        [
            "depends",
            "System.Int128",
            "--platform",
            "-S",
            "Dependency Graph",
            "--jsonl",
        ];
        (int unboundedExit, string unboundedOutput, string unboundedError) =
            await RunCapturedAsync(common);
        (int boundedExit, string boundedOutput, string boundedError) =
            await RunCapturedAsync([.. common, "--depth", "4"]);

        Assert.True(
            unboundedExit == 0,
            $"unbounded stderr: {unboundedError}");
        Assert.True(
            boundedExit == 0,
            $"bounded stderr: {boundedError}");
        Assert.Empty(unboundedError);
        Assert.Empty(boundedError);

        string[] expected = ParseGraphLines(unboundedOutput)
            .Where(static edge => edge.Depth <= 4)
            .Select(static edge => edge.Identity)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actual = ParseGraphLines(boundedOutput)
            .Select(static edge => edge.Identity)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task TypeDepthBoundariesAreTypedEndpointContext()
    {
        PersistentCache.Initialize("dotnet-inspect-test");
        string[] graph =
        [
            "depends",
            "System.Int128",
            "--platform",
            "-S",
            "Dependency Graph",
            "--depth",
            "1",
        ];
        (int jsonExit, string jsonOutput, string jsonError) =
            await RunCapturedAsync([.. graph, "--json", "--compact"]);
        (int treeExit, string treeOutput, string treeError) =
            await RunCapturedAsync([.. graph, "--tree"]);
        (int countExit, string countOutput, string countError) =
            await RunCapturedAsync([.. graph, "--count"]);

        Assert.Equal(0, jsonExit);
        Assert.Equal(0, treeExit);
        Assert.Equal(0, countExit);
        Assert.Empty(jsonError);
        Assert.Empty(treeError);
        Assert.Empty(countError);

        using JsonDocument document = JsonDocument.Parse(jsonOutput);
        JsonElement[] boundaries =
        [
            .. document.RootElement.GetProperty("queryResult").GetProperty("dependency")
                .GetProperty("depthBoundaries")
                .EnumerateArray(),
        ];
        Assert.NotEmpty(boundaries);
        Assert.All(
            boundaries,
            boundary =>
            {
                Assert.Equal(
                    1,
                    boundary.GetProperty("maximumDepth").GetInt32());
                Assert.False(
                    string.IsNullOrEmpty(
                        boundary.GetProperty("typeName").GetString()));
            });
        Assert.Contains(
            "(bounded at depth 1)",
            treeOutput,
            StringComparison.Ordinal);
        Assert.Equal(
            document.RootElement.GetProperty("rowSelection").GetProperty("relationships")
                .GetArrayLength(),
            int.Parse(
                countOutput.Trim(),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TypeMode_RejectsUnusedColumnProjection()
    {
        PersistentCache.Initialize("dotnet-inspect-test");
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "System.Int128",
            "--platform",
            "-S",
            "Dependency Graph",
            "--jsonl",
            "--columns",
            "Target",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "not supported in positional type mode",
            error,
            StringComparison.Ordinal);
    }

#if DEBUG
    [Fact]
    public async Task ExplicitRoots_PreserveHeterogeneousOccurrenceOrder()
    {
        (int exitCode, string output, _) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            "/missing/first.nuspec",
            "--project",
            "/missing/project",
            "--nuspec",
            "/missing/last.nuspec",
            "-S",
            "Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] roots =
        [
            .. document.RootElement.GetProperty("roots").EnumerateArray(),
        ];
        Assert.Equal(
            ["/missing/first.nuspec", "/missing/project", "/missing/last.nuspec"],
            roots.Select(root => root.GetProperty("input").GetString()));
        Assert.Equal(
            [1, 2, 3],
            roots.Select(root => root.GetProperty("root").GetInt32()));
    }

    [Fact]
    public async Task ProjectLocatorsShareIdentityAndRetainLocatorProvenance()
    {
        string csproj = Path.Combine(
            ProjectDirectoryFixture,
            "DotnetInspector.RestoredProjectFixtures.csproj");

        JsonElement directory = await RootAsync(ProjectDirectoryFixture);
        JsonElement project = await RootAsync(csproj);
        JsonElement assets = await RootAsync(AssetsFixture);

        string directoryDigest = RestoredDigest(directory);
        Assert.Equal(directoryDigest, RestoredDigest(project));
        Assert.Equal(directoryDigest, RestoredDigest(assets));
        Assert.Equal("ProjectLocator", directory.GetProperty("source").GetString());
        Assert.Equal("ProjectLocator", project.GetProperty("source").GetString());
        Assert.Equal("ProjectAssets", assets.GetProperty("source").GetString());
    }

    [Fact]
    public async Task DirectoryNamedProjectAssetsJsonRetainsLocatorProvenance()
    {
        string parent = CreateTemporaryDirectory();
        string directory = Directory.CreateDirectory(
            Path.Combine(parent, "project.assets.json")).FullName;
        string obj = Directory.CreateDirectory(
            Path.Combine(directory, "obj")).FullName;
        File.Copy(
            AssetsFixture,
            Path.Combine(obj, "project.assets.json"));

        try
        {
            JsonElement root = await RootAsync(directory);

            Assert.Equal(
                "ProjectLocator",
                root.GetProperty("source").GetString());
            Assert.Equal(
                RestoredDigest(await RootAsync(AssetsFixture)),
                RestoredDigest(root));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
#endif

    [Fact]
    public async Task RestoredTraversal_DepthIsRootRelative()
    {
        int depthOne = await HierarchyCountAsync(
            ["--project", AssetsFixture, "--depth", "1"]);
        int depthTwo = await HierarchyCountAsync(
            ["--project", AssetsFixture, "--depth", "2"]);
        int unbounded = await HierarchyCountAsync(
            ["--project", AssetsFixture]);

        Assert.True(depthOne > 0);
        Assert.True(depthTwo > depthOne);
        Assert.True(unbounded >= depthTwo);

        using JsonDocument bounded = await HierarchyJsonAsync(
            ["--project", AssetsFixture, "--depth", "1"]);
        JsonElement summary = bounded.RootElement.GetProperty("summary");
        Assert.Equal(
            "DepthBounded",
            summary.GetProperty("traversal_completion")
                .GetString());
        JsonElement boundedHierarchy =
            bounded.RootElement.GetProperty("dependency_hierarchy");
        JsonElement[] boundaries =
        [
            .. boundedHierarchy.GetProperty("depth_boundaries")
                .EnumerateArray(),
        ];
        Assert.Equal(2, boundaries.Length);
        Assert.All(
            boundaries,
            boundary =>
            {
                Assert.Equal(
                    "Restored",
                    boundary.GetProperty("producer").GetString());
                Assert.Equal(
                    1,
                    boundary.GetProperty("maximum_depth").GetInt32());
                Assert.Equal(
                    1,
                    boundary.GetProperty("root_occurrence").GetInt32());
                Assert.False(
                    boundary.TryGetProperty(
                        "package_projection",
                        out _));
            });
        Assert.Equal(
            depthOne,
            boundedHierarchy.GetProperty("occurrences").GetArrayLength());
        Assert.Equal(
            depthOne,
            summary.GetProperty("hierarchy_occurrences").GetInt32());

        using JsonDocument expanded = await HierarchyJsonAsync(
            ["--project", AssetsFixture, "--depth", "2"]);
        JsonElement[] expandedEdges =
        [
            .. expanded.RootElement.GetProperty("dependency_hierarchy")
                .GetProperty("occurrences")
                .EnumerateArray(),
        ];
        Assert.All(
            boundaries,
            boundary => Assert.Contains(
                expandedEdges,
                edge => edge.GetProperty("source_identity").GetRawText()
                    == boundary.GetProperty("node_identity").GetRawText()));

        (int markdownExit, string markdown, string markdownError) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                AssetsFixture,
                "--depth",
                "1",
                "-S",
                "Dependency Hierarchy",
            ]);
        (int treeExit, string tree, string treeError) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                AssetsFixture,
                "--depth",
                "1",
                "-S",
                "Dependency Hierarchy",
                "--tree",
            ]);
        Assert.Equal(0, markdownExit);
        Assert.Equal(0, treeExit);
        Assert.Empty(markdownError);
        Assert.Empty(treeError);
        Assert.Equal(
            2,
            markdown.Split(
                "(bounded at depth 1)",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            2,
            tree.Split(
                "(bounded at depth 1)",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task FailedRestoredTraversalReportsFailedAndTypedDetail()
    {
        var frameworks = new JsonObject
        {
            ["net11.0"] = new JsonObject
            {
                ["dependencies"] = new JsonObject(),
            },
        };
        var assets = new JsonObject
        {
            ["version"] = 4,
            ["targets"] = new JsonObject
            {
                ["net11.0"] = new JsonObject(),
                ["NET11.0"] = new JsonObject(),
            },
            ["projectFileDependencyGroups"] = new JsonObject
            {
                ["net11.0"] = new JsonArray(),
            },
            ["project"] = new JsonObject
            {
                ["frameworks"] = frameworks,
            },
        };
        string path = WriteTemporaryFile(
            "project.assets.json",
            Encoding.UTF8.GetBytes(assets.ToJsonString()));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            path,
            "-S",
            "Dependency Hierarchy,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "Failed",
            document.RootElement.GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
        JsonElement traversal = Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray(),
            failure => failure.GetProperty("phase").GetString()
                == "Traversal").GetProperty("traversal");
        Assert.Equal(
            "AmbiguousTargetIdentity",
            traversal.GetProperty("restored")
                .GetProperty("graph_reason")
                .GetString());
    }

    [Fact]
    public async Task AvailableRestoredTraversalReportsTypedGraphFailureWithPartialRows()
    {
        string path = WriteTemporaryFile(
            "project.assets.json",
            DenseProjectMeshDocument(projects: 130));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            path,
            "-S",
            "Dependency Hierarchy,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "Partial",
            document.RootElement.GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
        JsonElement edges = document.RootElement
            .GetProperty("dependency_hierarchy")
            .GetProperty("occurrences");
        Assert.True(edges.GetArrayLength() > 0);
        Assert.True(
            edges.GetArrayLength()
                <= RestoredProjectDependencyTraversalQuery
                    .MaxProjectRelationships);

        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray(),
            row => row.GetProperty("phase").GetString() == "Traversal");
        Assert.Equal(
            "ConfiguredLimitExceeded",
            failure.GetProperty("reason").GetString());
        JsonElement traversal = failure.GetProperty("traversal");
        Assert.Equal(
            [1],
            traversal.GetProperty("affected_roots")
                .EnumerateArray()
                .Select(static root => root.GetInt32()));
        JsonElement restored = traversal.GetProperty("restored");
        Assert.Equal("Graph", restored.GetProperty("kind").GetString());
        Assert.Equal(
            "ConfiguredLimitExceeded",
            restored.GetProperty("graph_reason").GetString());
        Assert.Equal(1, restored.GetProperty("occurrences").GetInt32());
    }

    [Fact]
    public async Task DirectNuspec_ProducesASourceBoundedBoundaryHierarchy()
    {
        using JsonDocument document = await HierarchyJsonAsync(
        [
            "--nuspec",
            NuspecFixture,
            "--depth",
            "3",
        ]);

        Assert.Equal(
            "SourceBounded",
            document.RootElement
                .GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
        JsonElement edges = document.RootElement
            .GetProperty("dependency_hierarchy")
            .GetProperty("occurrences");
        Assert.True(edges.GetArrayLength() > 0);
        Assert.All(
            edges.EnumerateArray(),
            edge => Assert.Equal(
                "package-boundary",
                edge.GetProperty("target_identity")
                    .GetProperty("kind")
                    .GetString()));
    }

    [Fact]
    public async Task DirectNuspec_InvalidTraversalTfmFailsBeforeProjection()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            NuspecFixture,
            "--tfm",
            "not a tfm",
            "-S",
            "Dependency Hierarchy",
            "--json",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "is not a valid NuGet framework",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatedDirectNuspecRootsRetainDistinctBoundaryOccurrences()
    {
        int single = await HierarchyCountAsync(
            ["--nuspec", NuspecFixture]);
        int repeated = await HierarchyCountAsync(
        [
            "--nuspec",
            NuspecFixture,
            "--nuspec",
            NuspecFixture,
        ]);

        Assert.True(single > 0);
        Assert.Equal(single * 2, repeated);
    }

    [Fact]
    public async Task EvidenceOnly_DoesNotRequestTraversal()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "-S",
            "Dependencies",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "NotRequested",
            document.RootElement
                .GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
        Assert.False(
            document.RootElement.TryGetProperty(
                "dependency_hierarchy",
                out _));
        Assert.True(
            document.RootElement.GetProperty("dependencies")
                .GetArrayLength() > 0);
    }

    [Fact]
    public async Task BareSelect_DoesNotRequestExpandablePackageTraversal()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "/missing/second-audit.nupkg",
            "-S",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "NotRequested",
            document.RootElement
                .GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
        Assert.False(
            document.RootElement.TryGetProperty(
                "dependency_hierarchy",
                out _));
    }

#if DEBUG
    [Fact]
    public async Task RootsOnly_DoesNotPublishUnrequestedDeclarationFailure()
    {
        string path = WriteTemporaryFile(
            "conflicting.nuspec",
            Manifest(
                "Contoso.Conflicting",
                "1.0.0",
                """
                <group targetFramework="net8.0">
                  <dependency id="Contoso.Dependency" version="[1.0.0]" />
                  <dependency id="Contoso.Dependency" version="[2.0.0]" />
                </group>
                """));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            path,
            "-S",
            "Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement summary = document.RootElement.GetProperty("summary");
        Assert.Equal(
            "NotRequested",
            summary.GetProperty("declaration_completion").GetString());
        Assert.Equal(
            "NotRequested",
            summary.GetProperty("restored_relationship_completion")
                .GetString());
        JsonElement root = document.RootElement.GetProperty("roots")[0];
        Assert.Equal(
            "NotRequested",
            root.GetProperty("declaration").GetString());
        Assert.Equal(
            "NotRequested",
            root.GetProperty("restored_relationships").GetString());
        Assert.Equal(
            "NotRequested",
            root.GetProperty("selection").GetString());
        Assert.Equal(0, root.GetProperty("declaration_groups").GetInt32());
        Assert.Equal(0, root.GetProperty("declarations").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("failures", out _));
    }

    [Fact]
    public async Task RootsEffectiveDiscovery_DoesNotRunDeclarationPhase()
    {
        string path = WriteTemporaryFile(
            "conflicting-effective.nuspec",
            Manifest(
                "Contoso.Conflicting",
                "1.0.0",
                """
                <group targetFramework="net8.0">
                  <dependency id="Contoso.Dependency" version="[1.0.0]" />
                  <dependency id="Contoso.Dependency" version="[2.0.0]" />
                </group>
                """));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            path,
            "-D",
            "Roots",
            "--effective",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("| Root | column |", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EffectiveDiscoveryRetainsItsProjectionSchema()
    {
        var result = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "-D",
            "--effective",
            "--json",
            "--columns",
            "Name",
        ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.NotEmpty(document.RootElement.EnumerateArray());
        Assert.All(
            document.RootElement.EnumerateArray(),
            row => Assert.True(row.TryGetProperty("name", out _)));
    }
#endif

    [Fact]
    public async Task MarkdownRendersSelectedSectionsWithoutCompletionDocument()
    {
        string[] arguments =
        [
            "depends",
            "--project",
            AssetsFixture,
            "--depth",
            "1",
            "-S",
            "Dependency Hierarchy,Dependencies",
        ];

        (int markdownExit, string markdown, string markdownError) =
            await RunCapturedAsync(arguments);
        (int projectedExit, string projected, string projectedError) =
            await RunCapturedAsync([.. arguments, "--columns", "Target"]);
        (int jsonExit, string json, string jsonError) =
            await RunCapturedAsync([.. arguments, "--json", "--compact"]);

        Assert.Equal(0, markdownExit);
        Assert.Equal(0, projectedExit);
        Assert.Equal(0, jsonExit);
        Assert.Empty(markdownError);
        Assert.Empty(projectedError);
        Assert.Empty(jsonError);
        Assert.StartsWith(
            "## Dependency Hierarchy",
            markdown.TrimStart(),
            StringComparison.Ordinal);
        Assert.Contains(
            "## Dependencies",
            markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "| Root Set |",
            markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "# Dependencies",
            markdown.Split(Environment.NewLine),
            StringComparer.Ordinal);
        Assert.StartsWith(
            "## Dependency Hierarchy",
            projected.TrimStart(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "| Root Set |",
            projected,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "# Dependencies",
            projected.Split(Environment.NewLine),
            StringComparer.Ordinal);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("summary", out _));
    }

    [Fact]
    public async Task RestoredDependencies_ExposeResolvedVersionInEveryTableShape()
    {
        string[] arguments =
        [
            "depends",
            "--project",
            AssetsFixture,
            "-S",
            "Dependencies",
        ];

        (int markdownExit, string markdown, string markdownError) =
            await RunCapturedAsync(arguments);
        (int tableExit, string table, string tableError) =
            await RunCapturedAsync([.. arguments, "--table"]);
        (int jsonExit, string json, string jsonError) =
            await RunCapturedAsync([.. arguments, "--json", "--compact"]);

        Assert.Equal(0, markdownExit);
        Assert.Equal(0, tableExit);
        Assert.Equal(0, jsonExit);
        Assert.Empty(markdownError);
        Assert.Empty(tableError);
        Assert.Empty(jsonError);
        Assert.Contains("| Resolved |", markdown, StringComparison.Ordinal);
        Assert.Contains("resolved", table, StringComparison.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement resolved = document.RootElement
            .GetProperty("dependencies")
            .EnumerateArray()
            .First(dependency =>
                dependency.TryGetProperty("resolved", out JsonElement value)
                && !string.IsNullOrEmpty(value.GetString()));
        Assert.True(
            resolved.TryGetProperty(
                "resolved_package_identity",
                out _));
        Assert.True(
            resolved.TryGetProperty(
                "resolved_relationship_identity",
                out _));
    }

    [Fact]
    public async Task RepeatedRestoredRootsRetainDistinctDependencyRows()
    {
        int single = await DependencyCountAsync(
            ["--project", AssetsFixture]);
        int repeated = await DependencyCountAsync(
        [
            "--project",
            AssetsFixture,
            "--project",
            AssetsFixture,
        ]);

        Assert.True(single > 0);
        Assert.Equal(single * 2, repeated);
    }

#if DEBUG
    [Fact]
    public async Task RootsJson_RetainsOwnerIssuedRestoredProvenance()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "-S",
            "Roots,Dependencies,Restored Edges",
            "--json",
            "--compact",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement.GetProperty("roots")[0];

        Assert.False(
            string.IsNullOrEmpty(
                root.GetProperty("content_digest").GetString()));
        Assert.Equal(
            "Available",
            root.GetProperty("declaration").GetString());
        Assert.Equal(
            "Complete",
            root.GetProperty("declaration_completion").GetString());
        Assert.True(root.TryGetProperty("restored_selection", out _));
        Assert.True(
            root.TryGetProperty("target_framework_identity", out _));
        Assert.True(root.TryGetProperty("target_selection", out _));

        (exitCode, output, error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            NuspecFixture,
            "-S",
            "Roots,Dependencies",
            "--json",
            "--compact",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument manifest = JsonDocument.Parse(output);
        JsonElement manifestRoot =
            manifest.RootElement.GetProperty("roots")[0];
        Assert.Equal(
            "Selected",
            manifestRoot.GetProperty("selection").GetString());
        Assert.True(manifestRoot.TryGetProperty("selected_group", out _));
        Assert.True(
            manifestRoot.TryGetProperty(
                "selected_source_occurrence",
                out _));

        (int markdownExit, string markdown, string markdownError) =
            await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            NuspecFixture,
            "-S",
            "Roots,Dependencies",
        ]);
        Assert.Equal(0, markdownExit);
        Assert.Empty(markdownError);
        Assert.Contains("package:1", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageHierarchyJson_RetainsProjectionProvenance()
    {
        string rootSource = CreateTemporaryDirectory();
        string dependencySource = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            rootSource,
            "Contoso.Root",
            "1.0.0",
            Dependency("Contoso.Child", "[1.0.0]"));
        WriteLocalSourcePackage(
            dependencySource,
            "Contoso.Child",
            "1.0.0",
            "");

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "Contoso.Root@1.0.0",
            "--source",
            rootSource,
            "--source",
            dependencySource,
            "-S",
            "Dependency Hierarchy,Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] projections =
        [
            .. document.RootElement.GetProperty("dependency_hierarchy")
                .GetProperty("package_projections")
                .EnumerateArray(),
        ];
        JsonElement acquired = Assert.Single(
            projections,
            projection =>
                projection.GetProperty("kind").GetString()
                    == "CandidateAcquired");
        JsonElement supplied = Assert.Single(
            projections,
            projection =>
                projection.GetProperty("kind").GetString()
                    == "RootSupplied");
        Assert.Equal(
            "Expanded",
            acquired.GetProperty("expansion").GetString());
        Assert.True(
            acquired.GetProperty("candidate")
                .GetProperty("correspondence")
                .GetInt32() > 0);
        Assert.NotEmpty(
            acquired.GetProperty("candidate")
                .GetProperty("authorities")
                .EnumerateArray());
        JsonElement acquiredSource =
            acquired.GetProperty("evidence")
                .GetProperty("source");
        JsonElement suppliedSource =
            supplied.GetProperty("evidence")
                .GetProperty("source");
        string? acquiredProducer =
            acquiredSource.GetProperty("producer_key").GetString();
        int[] authorityAssociations =
        [
            .. acquired.GetProperty("candidate")
                .GetProperty("authorities")
                .EnumerateArray()
                .Select(static authority =>
                    authority.GetProperty("association").GetInt32()),
        ];
        int acquiredAssociation =
            acquiredSource.GetProperty("association").GetInt32();
        int suppliedAssociation =
            suppliedSource.GetProperty("association").GetInt32();

        Assert.NotEqual(suppliedAssociation, acquiredAssociation);
        Assert.Contains(acquiredAssociation, authorityAssociations);
        Assert.False(string.IsNullOrEmpty(acquiredProducer));
        Assert.Equal(
            "NoDependencyGroups",
            acquired.GetProperty("evidence")
                .GetProperty("selection")
                .GetString());
        Assert.False(
            acquired.GetProperty("evidence")
                .TryGetProperty("selected_group", out _));
        Assert.False(
            acquired.GetProperty("evidence")
                .TryGetProperty("selected_source_occurrence", out _));
        Assert.False(
            acquired.GetProperty("evidence")
                .TryGetProperty("selected_framework", out _));
        Assert.Equal(
            "Selected",
            supplied.GetProperty("evidence")
                .GetProperty("selection")
                .GetString());
        Assert.True(
            supplied.GetProperty("evidence")
                .TryGetProperty("selected_group", out _));
        Assert.True(
            supplied.GetProperty("evidence")
                .TryGetProperty("selected_source_occurrence", out _));
        Assert.True(
            supplied.GetProperty("evidence")
                .TryGetProperty("selected_framework", out _));
        Assert.False(
            acquired.GetProperty("evidence")
                .TryGetProperty("declaration", out _));
        JsonElement root = document.RootElement.GetProperty("roots")[0];
        Assert.Equal(
            suppliedAssociation,
            root.GetProperty("package_source")
                .GetProperty("association")
                .GetInt32());
        Assert.Equal(
            "ExpectedCoordinate",
            root.GetProperty("identity_provenance").GetString());
    }

    [Fact]
    public void SourceTokens_CorrelateDocumentLocalOrdinalsByRuntimeSource()
    {
        using IPackageSourceClient first =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        using IPackageSourceClient second =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageDependencyEvidenceSourceIdentity firstEvidence =
            EvidenceSource(first.Source);
        PackageDependencyEvidenceSourceIdentity secondEvidence =
            EvidenceSource(second.Source);
        DependencyEvidenceSourceTokens tokens =
            DependencyEvidenceSourceTokens.Create();

        tokens.Reserve(firstEvidence);
        tokens.Reserve(secondEvidence);
        int firstRuntimeToken = tokens.Reserve(first.Source);
        int secondRuntimeToken = tokens.Reserve(second.Source);

        Assert.Equal(1, firstEvidence.Association);
        Assert.Equal(1, secondEvidence.Association);
        Assert.NotEqual(firstRuntimeToken, secondRuntimeToken);
        Assert.Equal(firstRuntimeToken, tokens.Reserve(firstEvidence));
        Assert.Equal(secondRuntimeToken, tokens.Reserve(secondEvidence));

        static PackageDependencyEvidenceSourceIdentity EvidenceSource(
            PackageSourceResultIdentity source)
        {
            PackageDependencyEvidenceRootFailure.PackageProfile failure =
                PackageDependencyEvidenceQuery.CreatePackageProfileFailure(
                    new PackageProfileFailure(
                        "Example.Package",
                        "1.0.0",
                        source,
                        PackageProfileFailureKind.SearchContract,
                        "Search failed"));
            PackageDependencyEvidenceOutcome outcome =
                PackageDependencyEvidenceQuery.Execute(
                    new PackageDependencyEvidenceRequest(
                        [],
                        [failure]));
            return Assert.IsType<
                PackageDependencyEvidenceRootFailure.PackageProfile>(
                    Assert.Single(outcome.FailedRoots)).Source;
        }
    }
#endif

    [Fact]
    public async Task PackageDepthBoundary_RetainsAllAffectedRootOccurrences()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.RootA",
            "1.0.0",
            Dependency("Contoso.Shared", "[1.0.0]"));
        WriteLocalSourcePackage(
            source,
            "Contoso.RootB",
            "1.0.0",
            Dependency("Contoso.Shared", "[1.0.0]"));
        WriteLocalSourcePackage(
            source,
            "Contoso.Shared",
            "1.0.0",
            "");

        using JsonDocument document = await HierarchyJsonAsync(
        [
            "--package",
            "Contoso.RootA@1.0.0",
            "--package",
            "Contoso.RootB@1.0.0",
            "--source",
            source,
            "--depth",
            "1",
        ]);

        JsonElement hierarchy =
            document.RootElement.GetProperty("dependency_hierarchy");
        JsonElement[] boundaries =
        [
            .. hierarchy.GetProperty("depth_boundaries").EnumerateArray(),
        ];
        Assert.Equal(
            [1, 2],
            boundaries.Select(boundary =>
                boundary.GetProperty("root_occurrence").GetInt32()));
        Assert.All(
            boundaries,
            boundary =>
            {
                Assert.Equal(
                    "Package",
                    boundary.GetProperty("producer").GetString());
                Assert.True(
                    boundary.GetProperty("package_projection")
                        .GetInt32() >= 0);
            });
        Assert.Equal(
            2,
            hierarchy.GetProperty("occurrences").GetArrayLength());
        Assert.Equal(
            2,
            document.RootElement.GetProperty("summary")
                .GetProperty("hierarchy_occurrences")
                .GetInt32());
    }

#if DEBUG
    [Fact]
    public async Task HierarchyOnly_RetainsFrameworkSelectionState()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--tfm",
            "net99.0",
            "-S",
            "Dependency Hierarchy,Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Dependency traversal completed as Partial",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement summary = document.RootElement.GetProperty("summary");
        Assert.Equal(
            "Partial",
            summary.GetProperty("traversal_completion").GetString());
        Assert.Equal(
            0,
            document.RootElement.GetProperty("dependency_hierarchy")
                .GetProperty("occurrences")
                .GetArrayLength());
        JsonElement root = document.RootElement.GetProperty("roots")[0];
        Assert.Equal(
            "NoMatchingTargetFramework",
            root.GetProperty("selection").GetString());
        Assert.Equal(
            "net99.0",
            root.GetProperty("requested_framework").GetString());
        Assert.NotEqual(
            "NotRequested",
            root.GetProperty("traversal").GetString());
        Assert.False(root.TryGetProperty("selected_framework", out _));

        (exitCode, output, error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--tfm",
            "net11.0",
            "-S",
            "Dependency Hierarchy,Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument selectedDocument = JsonDocument.Parse(output);
        JsonElement selectedRoot =
            selectedDocument.RootElement.GetProperty("roots")[0];
        Assert.Equal(
            "Selected",
            selectedRoot.GetProperty("selection").GetString());
        Assert.True(selectedRoot.TryGetProperty("selected_group", out _));
        Assert.True(
            selectedRoot.TryGetProperty(
                "selected_source_occurrence",
                out _));
        Assert.Equal(
            "net11.0",
            selectedRoot.GetProperty("requested_framework").GetString());
        Assert.Equal(
            "net11.0",
            selectedRoot.GetProperty("selected_framework").GetString());
        Assert.Equal(
            "Requested",
            selectedRoot.GetProperty("target_selection").GetString());
    }
#endif

    [Fact]
    public async Task TraversalFailureJson_RetainsAllAffectedRootsAndTypedSourceDetail()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Root",
            "1.0.0",
            Dependency("Contoso.Missing", "[1.0.0]"));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "Contoso.Root@1.0.0",
            "--package",
            "Contoso.Root@1.0.0",
            "--source",
            source,
            "-S",
            "Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument noTraversal = JsonDocument.Parse(output);
        Assert.False(
            noTraversal.RootElement.TryGetProperty(
                "dependency_hierarchy",
                out _));

        (exitCode, output, error) = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "Contoso.Root@1.0.0",
            "--package",
            "Contoso.Root@1.0.0",
            "--source",
            source,
            "-S",
            "Dependency Hierarchy,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray(),
            row => row.GetProperty("phase").GetString() == "Traversal");
        JsonElement traversal = failure.GetProperty("traversal");
        Assert.Equal(
            [1, 2],
            traversal.GetProperty("affected_roots")
                .EnumerateArray()
                .Select(static root => root.GetInt32()));
        Assert.Equal(
            "Acquisition",
            traversal.GetProperty("manifest").GetProperty("kind").GetString());
        Assert.NotEmpty(
            traversal.GetProperty("manifest")
                .GetProperty("source_failures")
                .EnumerateArray());

        (int jsonlExit, string jsonl, string jsonlError) =
            await RunCapturedAsync(
            [
                "depends",
                "--package",
                "Contoso.Root@1.0.0",
                "--package",
                "Contoso.Root@1.0.0",
                "--source",
                source,
                "-S",
                "Dependency Hierarchy,Failures",
                "--jsonl",
            ]);
        Assert.Equal(1, jsonlExit);
        Assert.Contains("typed failure", jsonlError, StringComparison.Ordinal);
        JsonElement[] jsonlRows =
        [
            .. jsonl.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument row = JsonDocument.Parse(line);
                    return row.RootElement.Clone();
                }),
        ];
        Assert.Contains(
            jsonlRows,
            row => row.GetProperty("kind").GetString()
                == "dependency-hierarchy");
        JsonElement jsonlFailure = Assert.Single(
            jsonlRows,
            row => row.GetProperty("kind").GetString() == "failure");
        Assert.Equal(
            [1, 2],
            jsonlFailure.GetProperty("failure")
                .GetProperty("traversal")
                .GetProperty("affected_roots")
                .EnumerateArray()
                .Select(static root => root.GetInt32()));
        Assert.NotEmpty(
            jsonlFailure.GetProperty("failure")
                .GetProperty("traversal")
                .GetProperty("manifest")
                .GetProperty("source_failures")
                .EnumerateArray());

        (int projectedExit, _, string projectedError) =
            await RunCapturedAsync(
            [
                "depends",
                "--package",
                "Contoso.Root@1.0.0",
                "--package",
                "Contoso.Root@1.0.0",
                "--source",
                source,
                "-S",
                "Dependency Hierarchy,Failures",
                "--json",
                "--columns",
                "Reason",
            ]);
        Assert.Equal(1, projectedExit);
        Assert.Contains(
            "cannot represent typed traversal failure detail",
            projectedError,
            StringComparison.Ordinal);
    }

#if DEBUG
    [Fact]
    public async Task FailedExplicitRootStillHasAnExactRootCount()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            "/missing/root-count.nuspec",
            "-S",
            "Roots",
            "--count",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Equal("1", output.Trim());
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
    }
#endif

    [Fact]
    public async Task EffectiveDiscovery_UsesActualDirectNuspecApplicability()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();
        Assert.Empty(
            root.Parse(
            [
                "depends",
                "--nuspec",
                NuspecFixture,
                "-D",
                "--effective",
            ]).Errors);

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            NuspecFixture,
            "-D",
            "--effective",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("Dependency Hierarchy", output, StringComparison.Ordinal);
        Assert.Contains("Dependencies", output, StringComparison.Ordinal);
#if DEBUG
        Assert.Contains("Roots", output, StringComparison.Ordinal);
        Assert.Contains(
            "Dependency Groups",
            output,
            StringComparison.Ordinal);
#else
        Assert.DoesNotContain("Roots", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Dependency Groups",
            output,
            StringComparison.Ordinal);
#endif
        Assert.DoesNotContain("Restored Edges", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Restored Packages",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EffectiveDiscovery_RetainsRootFailureExitStatus()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            "/missing/effective-root.nuspec",
            "-D",
            "--effective",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Failures", output, StringComparison.Ordinal);
#if DEBUG
        Assert.Contains("Roots", output, StringComparison.Ordinal);
#else
        Assert.DoesNotContain("Roots", output, StringComparison.Ordinal);
#endif
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PackageSearchTruncationReason.None, false)]
    [InlineData(PackageSearchTruncationReason.RequestedLimit, false)]
    [InlineData(PackageSearchTruncationReason.SourcePageLimit, true)]
    [InlineData(PackageSearchTruncationReason.ClientPageLimit, true)]
    public void PackagePrefixTruncation_OnlyProducerLimitsFail(
        PackageSearchTruncationReason reason,
        bool expected) =>
        Assert.Equal(
            expected,
            DependsCommand.IsFailedPrefixTruncation(reason));

    [Fact]
    public async Task FailedSibling_RetainsUsableRootAndReturnsNonzero()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--nuspec",
            "/missing/sibling.nuspec",
            "-S",
            "Dependencies,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement summary = document.RootElement.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("admitted_roots").GetInt32());
        Assert.Equal(1, summary.GetProperty("failed_roots").GetInt32());
        Assert.True(
            document.RootElement.GetProperty("dependencies")
                .GetArrayLength() > 0);
        Assert.Equal(
            1,
            document.RootElement.GetProperty("failures").GetArrayLength());
    }

    [Fact]
    public async Task MalformedLibraryPackage_RetainsValidSiblingRoot()
    {
        string malformed = WriteTemporaryFile(
            "malformed-library.nupkg",
            Encoding.UTF8.GetBytes("not a package archive"));
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            malformed,
            "--project",
            AssetsFixture,
            "-S",
            "Dependencies,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.True(
            document.RootElement.GetProperty("dependencies")
                .GetArrayLength() > 0);
        int expectedCount =
            document.RootElement.GetProperty("dependencies").GetArrayLength();
        Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray());

        (int countExit, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--library",
                malformed,
                "--project",
                AssetsFixture,
                "-S",
                "Dependencies",
                "--count",
            ]);

        Assert.Equal(1, countExit);
        Assert.Equal(
            expectedCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            countOutput.Trim());
        Assert.Contains(
            "typed failure",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedPlatformLibraryTfm_RetainsValidSiblingRoot()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            NuspecFixture,
            "--library",
            "System.Runtime",
            "--tfm",
            "net48",
            "-S",
            "Dependencies,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.True(
            document.RootElement.GetProperty("dependencies")
                .GetArrayLength() > 0);
        Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray());
    }

    [Fact]
    public async Task HierarchyAndFailuresJsonl_UsesOneDiscriminatedSchema()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--nuspec",
            "/missing/jsonl-sibling.nuspec",
            "-S",
            "Dependency Hierarchy,Failures",
            "--jsonl",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        JsonElement[] rows =
        [
            .. output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument row = JsonDocument.Parse(line);
                    return row.RootElement.Clone();
                }),
        ];
        Assert.Contains(
            rows,
            row => row.GetProperty("kind").GetString()
                == "dependency-hierarchy");
        JsonElement failure = Assert.Single(
            rows,
            row => row.GetProperty("kind").GetString() == "failure");
        Assert.True(
            failure.GetProperty("failure")
                .TryGetProperty("evidence", out _));
    }

    [Fact]
    public async Task Pruning_ProjectsDelegationAndPackageRetentionWithoutTraversal()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Pruning.Root",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="System.Text.Json" version="[9.0.0]" />
              <dependency id="System.Runtime" version="[4.3.2]" />
            </group>
            """);
        WriteLocalSourcePackage(
            source,
            "System.Text.Json",
            "9.0.0",
            "");
        WriteLocalSourcePackage(
            source,
            "System.Runtime",
            "4.3.2",
            "");
        int inventoryReads = 0;
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Pruning.Root@1.0.0"),
            ],
            Tfm = "net11.0",
            PruningPlatformFamily = "RUNTIME",
            Select = [DependsAssetSections.Pruning],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    frameworkSpec =>
                    {
                        inventoryReads++;
                        Assert.Equal("runtime@11.0", frameworkSpec);
                        return PruneInventory(
                            "System.Text.Json|11.0.0",
                            "System.Runtime|4.3.1");
                    },
                    TestContext.Current.CancellationToken));

        Assert.True(
            exitCode == 0,
            $"Expected success.{Environment.NewLine}{error}{Environment.NewLine}{output}");
        Assert.Empty(error);
        Assert.Equal(1, inventoryReads);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement pruningSummary =
            document.RootElement.GetProperty("summary")
                .GetProperty("pruning");
        Assert.Equal(
            "Complete",
            pruningSummary.GetProperty("completion").GetString());
        Assert.Equal(2, pruningSummary.GetProperty("evaluated").GetInt32());
        Assert.Equal(1, pruningSummary.GetProperty("delegated").GetInt32());
        Assert.Equal(1, pruningSummary.GetProperty("retained").GetInt32());

        JsonElement[] rows =
        [
            .. document.RootElement.GetProperty("pruning")
                .EnumerateArray(),
        ];
        JsonElement delegated = Assert.Single(rows, row =>
            row.GetProperty("package_id").GetString()
                == "system.text.json");
        Assert.Equal(
            "PlatformDelegation",
            delegated.GetProperty("disposition").GetString());
        Assert.Equal(
            "9.0.0",
            delegated.GetProperty("candidate_version").GetString());
        Assert.Equal(
            "11.0.0",
            delegated.GetProperty("platform_provided_version")
                .GetString());
        Assert.True(
            delegated.GetProperty("delegates_to_platform").GetBoolean());
        Assert.Equal(
            "Resolved",
            delegated.GetProperty("candidate")
                .GetProperty("kind")
                .GetString());
        Assert.Equal(
            "9.0.0",
            delegated.GetProperty("resolved_candidate")
                .GetProperty("coordinate")
                .GetProperty("version")
                .GetString());

        JsonElement retained = Assert.Single(rows, row =>
            row.GetProperty("package_id").GetString()
                == "system.runtime");
        Assert.Equal(
            "PackageRetained",
            retained.GetProperty("disposition").GetString());
        Assert.Equal(
            "4.3.2",
            retained.GetProperty("candidate_version").GetString());
        Assert.Equal(
            "4.3.1",
            retained.GetProperty("platform_provided_version")
                .GetString());
        Assert.False(
            retained.GetProperty("delegates_to_platform").GetBoolean());
        Assert.False(
            document.RootElement.TryGetProperty(
                "dependency_hierarchy",
                out _));
    }

    [Fact]
    public async Task Pruning_ExplainsApplicationAuthorshipWithoutInventoryWork()
    {
        int inventoryReads = 0;
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Project,
                    AssetsFixture),
            ],
            Tfm = "net11.0",
            Select = [DependsAssetSections.Pruning],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ =>
                    {
                        inventoryReads++;
                        throw new InvalidOperationException(
                            "Inventory must not be read.");
                    },
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(0, inventoryReads);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
        [
            .. document.RootElement.GetProperty("pruning")
                .EnumerateArray(),
        ];
        Assert.NotEmpty(rows);
        Assert.All(
            rows,
            row =>
            {
                Assert.Equal(
                    "NotEvaluated",
                    row.GetProperty("disposition").GetString());
                Assert.Equal(
                    "ApplicationAuthoredExemption",
                    row.GetProperty("applicability").GetString());
            });
    }

    [Fact]
    public async Task Pruning_UsesTheSelectedAspNetCoreInventory()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.AspNetCore.Pruning",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="Microsoft.AspNetCore.Authentication" version="[10.0.0]" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.AspNetCore.Pruning@1.0.0"),
            ],
            Tfm = "net11.0",
            PruningPlatformFamily = "ASPNETCORE",
            Select = [DependsAssetSections.Pruning],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    frameworkSpec =>
                    {
                        Assert.Equal("aspnetcore@11.0", frameworkSpec);
                        return PruneInventoryForFamily(
                            "Microsoft.AspNetCore.App",
                            "net11.0",
                            "Microsoft.AspNetCore.Authentication|11.0.0");
                    },
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row = Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        Assert.Equal(
            "AspNetCore",
            row.GetProperty("platform_family").GetString());
        Assert.Equal(
            "PlatformDelegation",
            row.GetProperty("disposition").GetString());
    }

    [Fact]
    public async Task Pruning_HumanFormatsPreservePolicyRows()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Pruning.Render",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="System.Text.Json" version="[9.0.0]" />
              <dependency id="System.Runtime" version="[4.3.2]" />
            </group>
            """);

        foreach (OutputFormat format in new[]
                 {
                     OutputFormat.Markdown,
                     OutputFormat.Table,
                 })
        {
            var options = new DependsOptions
            {
                AssetRoots =
                [
                    new DependsAssetRoot(
                        1,
                        DependencyInspectionRootKind.Package,
                        "Contoso.Pruning.Render@1.0.0"),
                ],
                Tfm = "net11.0",
                Select = [DependsAssetSections.Pruning],
                Format = format,
                SourceOptions = new NuGetSourceOptions
                {
                    Sources = [source],
                },
            };

            (int exitCode, string output, string error) =
                await ConsoleCapture.RunAsync(
                    () => DependsCommand.ExecuteAssetDependsAsync(
                        options,
                        _ => PruneInventory(
                            "System.Text.Json|11.0.0",
                            "System.Runtime|4.3.1"),
                        TestContext.Current.CancellationToken));

            Assert.Equal(0, exitCode);
            Assert.Empty(error);
            Assert.Contains(
                "System.Text.Json",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "System.Runtime",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "PlatformDelegation",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "PackageRetained",
                output,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Pruning_RowWindowDoesNotChangeSummaryOrExitStatus()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Pruning.Window",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="System.Text.Json" version="[9.0.0]" />
              <dependency id="System.Runtime" version="[4.3.2]" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Pruning.Window@1.0.0"),
            ],
            Tfm = "net11.0",
            Select = [DependsAssetSections.Pruning],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            Rows = RowWindow.Head(1),
            LineWindowExplicitlySet = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ => PruneInventory(
                        "System.Text.Json|11.0.0",
                        "System.Runtime|4.3.1"),
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            2,
            document.RootElement.GetProperty("summary")
                .GetProperty("pruning")
                .GetProperty("evaluated")
                .GetInt32());
        Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
    }

    [Fact]
    public async Task Pruning_RowWindowRetainsFailures()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Pruning.WindowFailure",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="Contoso.Missing" version="[1.0.0,2.0.0)" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Pruning.WindowFailure@1.0.0"),
            ],
            Tfm = "net11.0",
            Select =
            [
                DependsAssetSections.Pruning,
                DependsAssetSections.Failures,
            ],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            Rows = RowWindow.Head(0),
            LineWindowExplicitlySet = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ => PruneInventory(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Package pruning evidence completed as Failed",
            error,
            StringComparison.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            Assert.Empty(
                document.RootElement.GetProperty("pruning")
                    .EnumerateArray());
            JsonElement failure = Assert.Single(
                document.RootElement.GetProperty("failures")
                    .EnumerateArray());
            Assert.Equal(
                "Pruning",
                failure.GetProperty("phase").GetString());
        }
    }

    [Fact]
    public async Task Pruning_IncompleteDeclarationsCannotProduceAnExactCount()
    {
        string path = WriteTemporaryFile(
            "conflicting-pruning.nuspec",
            Manifest(
                "Contoso.Conflicting",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="System.Runtime" version="[4.3.1]" />
                  <dependency id="System.Runtime" version="[4.3.2]" />
                </group>
                """));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            path,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            JsonElement summary = document.RootElement
                .GetProperty("summary");
            Assert.Equal(
                "Partial",
                summary.GetProperty("declaration_completion").GetString());
            JsonElement pruning = summary.GetProperty("pruning");
            Assert.Equal(
                "Failed",
                pruning.GetProperty("completion").GetString());
            Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
        }

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                path,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_FailedRootCannotProduceAnExactCount()
    {
        string missing = Path.Combine(
            CreateTemporaryDirectory(),
            "missing.nuspec");

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            missing,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            JsonElement pruning = document.RootElement
                .GetProperty("summary")
                .GetProperty("pruning");
            Assert.Equal(
                "Failed",
                pruning.GetProperty("completion").GetString());
            Assert.Equal(1, pruning.GetProperty("roots").GetInt32());
            Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
            Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
        }

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                missing,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_FailedRootKeepsSuccessfulSiblingPartial()
    {
        string directory = CreateTemporaryDirectory();
        string valid = WriteTemporaryFile(
            "valid-pruning.nuspec",
            Manifest(
                "Contoso.Valid",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="System.Runtime" version="[4.3.2]" />
                </group>
                """));
        string missing = Path.Combine(directory, "missing.nuspec");

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            valid,
            "--nuspec",
            missing,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement pruning = document.RootElement
            .GetProperty("summary")
            .GetProperty("pruning");
        Assert.Equal(
            "Partial",
            pruning.GetProperty("completion").GetString());
        Assert.Equal(2, pruning.GetProperty("roots").GetInt32());
        Assert.Equal(1, pruning.GetProperty("declarations").GetInt32());
        Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
        Assert.Equal(1, pruning.GetProperty("source_bounded").GetInt32());
        Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
        Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
    }

    [Fact]
    public async Task Pruning_FailedLibraryCannotProduceAnExactCount()
    {
        string missing = Path.Combine(
            CreateTemporaryDirectory(),
            "missing.dll");

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            missing,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            JsonElement pruning = document.RootElement
                .GetProperty("summary")
                .GetProperty("pruning");
            Assert.Equal(
                "Failed",
                pruning.GetProperty("completion").GetString());
            Assert.Equal(1, pruning.GetProperty("roots").GetInt32());
            Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
            Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
            JsonElement failure = Assert.Single(
                document.RootElement.GetProperty("failures")
                    .EnumerateArray());
            Assert.Equal("Root", failure.GetProperty("phase").GetString());
        }

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--library",
                missing,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_AdmittedLibraryIsSuccessfulAndNotApplicable()
    {
        string library = typeof(DependsAssetCommandTests).Assembly.Location;

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            library,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            JsonElement pruning = document.RootElement
                .GetProperty("summary")
                .GetProperty("pruning");
            Assert.Equal(
                "Complete",
                pruning.GetProperty("completion").GetString());
            Assert.Equal(1, pruning.GetProperty("roots").GetInt32());
            Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
            Assert.Equal(0, pruning.GetProperty("failed").GetInt32());
            Assert.Empty(
                document.RootElement.GetProperty("pruning")
                    .EnumerateArray());
        }

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--library",
                library,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(0, countExitCode);
        Assert.Equal("0\n", countOutput);
        Assert.Empty(countError);
    }

    [Fact]
    public async Task Pruning_FailedLibraryKeepsSuccessfulSiblingPartial()
    {
        string missing = Path.Combine(
            CreateTemporaryDirectory(),
            "missing.dll");
        string valid = WriteTemporaryFile(
            "valid-pruning.nuspec",
            Manifest(
                "Contoso.Valid",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="System.Runtime" version="[4.3.2]" />
                </group>
                """));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            missing,
            "--nuspec",
            valid,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement pruning = document.RootElement
            .GetProperty("summary")
            .GetProperty("pruning");
        Assert.Equal(
            "Partial",
            pruning.GetProperty("completion").GetString());
        Assert.Equal(2, pruning.GetProperty("roots").GetInt32());
        Assert.Equal(1, pruning.GetProperty("declarations").GetInt32());
        Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
        Assert.Equal(1, pruning.GetProperty("source_bounded").GetInt32());
        Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
        Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures")
                .EnumerateArray());
        Assert.Equal("Root", failure.GetProperty("phase").GetString());

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--library",
                missing,
                "--nuspec",
                valid,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_UnavailableDeclarationsCannotProduceAnExactCount()
    {
        string path = WriteTemporaryFile(
            "project.assets.json",
            Encoding.UTF8.GetBytes("""{"version":3}"""));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            path,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Dependency declaration evidence completed",
            error,
            StringComparison.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            JsonElement pruning = document.RootElement
                .GetProperty("summary")
                .GetProperty("pruning");
            Assert.Equal(
                "Failed",
                pruning.GetProperty("completion").GetString());
            Assert.Equal(1, pruning.GetProperty("roots").GetInt32());
            Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
            Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
            JsonElement failure = Assert.Single(
                document.RootElement.GetProperty("failures")
                    .EnumerateArray());
            Assert.Equal("Pruning", failure.GetProperty("phase").GetString());
            JsonElement prerequisite = failure.GetProperty("pruning");
            Assert.Equal(
                "Prerequisite",
                prerequisite.GetProperty("kind").GetString());
            Assert.Equal(1, prerequisite.GetProperty("root").GetInt32());
            Assert.Equal(
                "Unavailable",
                prerequisite.GetProperty("declaration_state").GetString());
            Assert.True(prerequisite.TryGetProperty("root_identity", out _));
        }

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                path,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_UnavailableDeclarationsKeepSuccessfulSiblingPartial()
    {
        string assets = WriteTemporaryFile(
            "project.assets.json",
            Encoding.UTF8.GetBytes("""{"version":3}"""));
        string valid = WriteTemporaryFile(
            "valid-pruning.nuspec",
            Manifest(
                "Contoso.Valid",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="System.Runtime" version="[4.3.2]" />
                </group>
                """));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            assets,
            "--nuspec",
            valid,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Dependency declaration evidence completed",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement pruning = document.RootElement
            .GetProperty("summary")
            .GetProperty("pruning");
        Assert.Equal(
            "Partial",
            pruning.GetProperty("completion").GetString());
        Assert.Equal(2, pruning.GetProperty("roots").GetInt32());
        Assert.Equal(1, pruning.GetProperty("declarations").GetInt32());
        Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
        Assert.Equal(1, pruning.GetProperty("source_bounded").GetInt32());
        Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
        Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures")
                .EnumerateArray());
        Assert.Equal("Pruning", failure.GetProperty("phase").GetString());
        JsonElement prerequisite = failure.GetProperty("pruning");
        Assert.Equal(
            "Prerequisite",
            prerequisite.GetProperty("kind").GetString());
        Assert.Equal(1, prerequisite.GetProperty("root").GetInt32());
        Assert.Equal(
            "Unavailable",
            prerequisite.GetProperty("declaration_state").GetString());

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                assets,
                "--nuspec",
                valid,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DependenciesSelectionDoesNotReadPruningInventory()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Dependencies.Only",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="System.Text.Json" version="[9.0.0]" />
            </group>
            """);
        int inventoryReads = 0;
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Dependencies.Only@1.0.0"),
            ],
            Tfm = "net11.0",
            Select = [DependsAssetSections.Dependencies],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ =>
                    {
                        inventoryReads++;
                        throw new InvalidOperationException(
                            "Inventory must not be read.");
                    },
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(0, inventoryReads);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "NotRequested",
            document.RootElement.GetProperty("summary")
                .GetProperty("pruning")
                .GetProperty("completion")
                .GetString());
        Assert.False(
            document.RootElement.TryGetProperty("pruning", out _));
    }

    [Fact]
    public async Task Pruning_DirectNuspecRemainsSourceBounded()
    {
        int inventoryReads = 0;
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Nuspec,
                    NuspecFixture),
            ],
            Tfm = "net11.0",
            Select = [DependsAssetSections.Pruning],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ =>
                    {
                        inventoryReads++;
                        throw new InvalidOperationException(
                            "Inventory must not be read.");
                    },
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(0, inventoryReads);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "SourceBounded",
            document.RootElement.GetProperty("summary")
                .GetProperty("pruning")
                .GetProperty("completion")
                .GetString());
        Assert.All(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray(),
            row => Assert.Equal(
                "SourceBounded",
                row.GetProperty("disposition").GetString()));
    }

    [Fact]
    public async Task Pruning_InventoryFailurePreventsCandidateResolution()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Inventory.Root",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="Contoso.Missing.Child" version="[1.0.0]" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Inventory.Root@1.0.0"),
            ],
            Tfm = "net11.0",
            Select =
            [
                DependsAssetSections.Pruning,
                DependsAssetSections.Failures,
            ],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ => new InstalledPlatformPruneSource.Result(
                        null,
                        "The requested installed inventory is unavailable."),
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Package pruning evidence completed as Failed",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row = Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        Assert.Equal(
            "InventoryUnavailable",
            row.GetProperty("disposition").GetString());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures")
                .EnumerateArray());
        Assert.Equal(
            "Pruning",
            failure.GetProperty("phase").GetString());
        Assert.Equal(
            "Inventory",
            failure.GetProperty("pruning")
                .GetProperty("kind")
                .GetString());
    }

    [Fact]
    public async Task Pruning_InventoryTargetMismatchIsAVisibleFailure()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Inventory.Root",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="Contoso.Inventory.Child" version="[1.0.0]" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Inventory.Root@1.0.0"),
            ],
            Tfm = "net11.0",
            Select =
            [
                DependsAssetSections.Pruning,
                DependsAssetSections.Failures,
            ],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ => PruneInventoryFor(
                        "net10.0",
                        "Contoso.Inventory.Child|1.0.0"),
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Package pruning evidence completed as Failed",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row = Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        Assert.Equal(
            "InventoryUnavailable",
            row.GetProperty("disposition").GetString());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures")
                .EnumerateArray());
        Assert.Contains(
            "describes 'net10.0', not requested target 'net11.0'",
            failure.GetProperty("pruning")
                .GetProperty("message")
                .GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_CandidateFailureRemainsTypedAndNonzero()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Candidate.Root",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="Contoso.Missing.Candidate" version="[1.0.0,2.0.0)" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Candidate.Root@1.0.0"),
            ],
            Tfm = "net11.0",
            Select =
            [
                DependsAssetSections.Pruning,
                DependsAssetSections.Failures,
            ],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ => PruneInventory(),
                    TestContext.Current.CancellationToken));

        Assert.True(
            exitCode == 1,
            $"Expected failure.{Environment.NewLine}{error}{Environment.NewLine}{output}");
        Assert.Contains(
            "Package pruning evidence completed as Failed",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row = Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        Assert.Equal(
            "CandidateUnavailable",
            row.GetProperty("disposition").GetString());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures")
                .EnumerateArray());
        JsonElement pruning = failure.GetProperty("pruning");
        Assert.Equal("Candidate", pruning.GetProperty("kind").GetString());
        Assert.Equal(
            "Failed",
            pruning.GetProperty("candidate_state").GetString());
        Assert.Equal(
            "NoMatchingVersion",
            row.GetProperty("candidate")
                .GetProperty("kind")
                .GetString());
        Assert.Equal(
            "NoMatchingVersion",
            pruning.GetProperty("candidate")
                .GetProperty("kind")
                .GetString());
    }

    [Fact]
    public async Task Pruning_RequiresTargetAndOwnsPlatformFamilyGesture()
    {
        (int missingTfmExit, _, string missingTfmError) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                NuspecFixture,
                "-S",
                DependsAssetSections.Pruning,
            ]);
        (int unusedFamilyExit, _, string unusedFamilyError) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                NuspecFixture,
                "--platform-family",
                "aspnetcore",
                "-S",
                DependsAssetSections.Dependencies,
            ]);
        (int typeExit, _, string typeError) = await RunCapturedAsync(
        [
            "depends",
            "System.Int128",
            "--platform-family",
            "runtime",
        ]);

        Assert.Equal(1, missingTfmExit);
        Assert.Contains("requires --tfm", missingTfmError);
        Assert.Equal(1, unusedFamilyExit);
        Assert.Contains("requires the Pruning section", unusedFamilyError);
        Assert.Equal(1, typeExit);
        Assert.Contains("only by asset-mode depends", typeError);
    }

    [Fact]
    public async Task DiscoveryReflectsCompiledSectionCatalog()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(["depends", "-D"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        foreach (string section in new[]
        {
            DependsAssetSections.DependencyHierarchy,
            DependsAssetSections.Dependencies,
            DependsAssetSections.Pruning,
            DependsAssetSections.Failures,
        })
            Assert.Contains(section, output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            DependsTypeSections.DependencyGraph,
            output,
            StringComparison.Ordinal);
        foreach (string section in new[]
        {
            DependsAssetSections.Roots,
            DependsAssetSections.RestoredEdges,
            DependsAssetSections.DependencyGroups,
            DependsAssetSections.RestoredPackages,
        })
#if DEBUG
            Assert.Contains(section, output, StringComparison.Ordinal);
#else
            Assert.DoesNotContain(section, output, StringComparison.Ordinal);
#endif
        Assert.Contains("@Dependencies", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssetModeRejectsObsoleteDependencyGraphSection()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                NuspecFixture,
                "-S",
                DependsTypeSections.DependencyGraph,
            ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            DependsTypeSections.DependencyGraph,
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            DependsAssetSections.DependencyHierarchy,
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BareEffectiveDiscoveryDoesNotRunUnboundedPruning()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                NuspecFixture,
                "-D",
                "--effective",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.DoesNotContain(
            DependsAssetSections.Pruning,
            output,
            StringComparison.Ordinal);
    }

#if !DEBUG
    [Fact]
    public async Task RetailCatalogSchemaAndCategoryOmitDiagnosticSections()
    {
        Assert.Equal(
            [
                DependsAssetSections.DependencyHierarchy,
                DependsAssetSections.Dependencies,
                DependsAssetSections.Pruning,
                DependsAssetSections.Failures,
            ],
            DependsAssetSections.SectionOrder);
        var expectedSections = new HashSet<string>(
            [
                DependsAssetSections.DependencyHierarchy,
                DependsAssetSections.Dependencies,
                DependsAssetSections.Pruning,
                DependsAssetSections.Failures,
            ],
            StringComparer.OrdinalIgnoreCase);
        Assert.True(
            expectedSections.SetEquals(
                DependsAssetSections.Catalog.SelectableSectionNames));
        Assert.Equal(
            [
                DependsAssetSections.DependencyHierarchy,
                DependsAssetSections.Dependencies,
                DependsAssetSections.Failures,
            ],
            DependsAssetSections.Catalog.SelectionCategoryMap[
                SectionCategoryNames.Dependencies]);

        (int exitCode, string output, string error) =
            await RunCapturedAsync(["depends", "-D", "--schema"]);
        (int hierarchyExit, string hierarchy, string hierarchyError) =
            await RunCapturedAsync(
            [
                "depends",
                "-D",
                DependsAssetSections.DependencyHierarchy,
                "--schema",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(0, hierarchyExit);
        Assert.Empty(hierarchyError);
        Assert.Contains("Edge ID", hierarchy, StringComparison.Ordinal);
        foreach (string section in new[]
        {
            DependsAssetSections.Roots,
            DependsAssetSections.RestoredEdges,
            DependsAssetSections.DependencyGroups,
            DependsAssetSections.RestoredPackages,
        })
            Assert.DoesNotContain(section, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Roots")]
    [InlineData("Restored Edges")]
    [InlineData("Dependency Groups")]
    [InlineData("Restored Packages")]
    [InlineData("Restored*")]
    public async Task RetailSelectionCannotReachDiagnosticSections(
        string selector)
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "-S",
            selector,
        ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.True(
            error.Contains("not found", StringComparison.Ordinal)
            || error.Contains("No sections match", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("-v:m")]
    [InlineData("-v:n")]
    [InlineData("-v:d")]
    public async Task RetailVerbosityNeverEmitsDiagnosticSections(
        string verbosity)
    {
        string[][] outputs =
        {
            ["--json"],
            ["-o", "json"],
        };
        foreach (string[] outputSelection in outputs)
        {
            (int jsonExitCode, string jsonOutput, string jsonError) =
                await RunCapturedAsync(
            [
                "depends",
                "--project",
                AssetsFixture,
                verbosity,
                .. outputSelection,
                "--compact",
            ]);

            Assert.Equal(0, jsonExitCode);
            Assert.Empty(jsonError);
            using JsonDocument document = JsonDocument.Parse(jsonOutput);
            foreach (string property in new[]
            {
                "roots",
                "restored_edges",
                "dependency_groups",
                "restored_packages",
            })
                Assert.False(document.RootElement.TryGetProperty(property, out _));
        }

        (int exitCode, string output, string error) =
            await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            verbosity,
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        foreach (string heading in new[]
        {
            "## Roots",
            "## Restored Edges",
            "## Dependency Groups",
            "## Restored Packages",
        })
            Assert.DoesNotContain(heading, output, StringComparison.Ordinal);
    }
#else
    [Fact]
    public async Task DiagnosticSectionsRemainExactlySelectableInDebugBuild()
    {
        var scenarios =
            new (string Section, string Property, string[] Arguments)[]
            {
                (
                    DependsAssetSections.Roots,
                    "roots",
                    ["--nuspec", NuspecFixture]),
                (
                    DependsAssetSections.RestoredEdges,
                    "restored_edges",
                    ["--project", AssetsFixture]),
                (
                    DependsAssetSections.DependencyGroups,
                    "dependency_groups",
                    ["--nuspec", NuspecFixture]),
                (
                    DependsAssetSections.RestoredPackages,
                    "restored_packages",
                    ["--project", AssetsFixture]),
            };

        foreach ((string section, string property, string[] arguments)
            in scenarios)
        {
            (int exitCode, string output, string error) =
                await RunCapturedAsync(
            [
                "depends",
                .. arguments,
                "-S",
                section,
                "--json",
                "--compact",
            ]);

            Assert.Equal(0, exitCode);
            Assert.Empty(error);
            using JsonDocument document = JsonDocument.Parse(output);
            Assert.True(
                document.RootElement.GetProperty(property)
                    .GetArrayLength() > 0);
        }
    }
#endif

    [Fact]
    public async Task TypeModeDiscoveryExposesOnlyTheGraphSection()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(["depends", "Int128", "-D"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("Dependency Graph", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"{Environment.NewLine}Roots",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Restored Edges", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryDepthBeyondTheHierarchyReportsComplete()
    {
        string library = typeof(DependsAssetCommandTests).Assembly.Location;
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            library,
            "--depth",
            "100",
            "-S",
            "Dependency Hierarchy",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "Complete",
            document.RootElement.GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
    }

    [Fact]
    public async Task MissingLibraryBinding_IsTypedPartialTraversalFailure()
    {
        string directory = CreateTemporaryDirectory();
        string library = Path.Combine(
            directory,
            "ILInspector.Metadata.TypeDependencyConsumer.dll");
        File.Copy(
            FixtureCatalog.MetadataTypeDependencyConsumer.AssemblyPath(),
            library);

        try
        {
            (int exitCode, string output, string error) =
                await RunCapturedAsync(
            [
                "depends",
                "--library",
                library,
                "-S",
                "Dependency Hierarchy,Failures",
                "--json",
                "--compact",
            ]);

            Assert.Equal(1, exitCode);
            Assert.Contains("typed failure", error, StringComparison.Ordinal);
            Assert.Contains(
                "Dependency traversal completed as Partial",
                error,
                StringComparison.Ordinal);

            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement summary =
                document.RootElement.GetProperty("summary");
            Assert.Equal(
                "Partial",
                summary.GetProperty("traversal_completion").GetString());

            const string missingAssembly =
                "ILInspector.Metadata.TypeDependencyReference";
            JsonElement edge = Assert.Single(
                document.RootElement.GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences")
                    .EnumerateArray(),
                candidate => candidate.GetProperty("target_identity")
                    .GetProperty("library")
                    .GetProperty("name")
                    .GetString() == missingAssembly);
            Assert.Equal(
                "declared",
                edge.GetProperty("resolution").GetString());
            Assert.Equal(
                missingAssembly,
                edge.GetProperty("evidence_identity")
                    .GetProperty("assembly_reference")
                    .GetProperty("name")
                    .GetString());

            JsonElement failure = Assert.Single(
                document.RootElement.GetProperty("failures")
                    .EnumerateArray(),
                row => row.GetProperty("reason").GetString() == "Missing");
            JsonElement traversal = failure.GetProperty("traversal");
            JsonElement binding =
                traversal.GetProperty("assembly_binding");
            Assert.Equal(
                "Missing",
                binding.GetProperty("kind").GetString());
            Assert.Equal(
                "NoNameOwner",
                binding.GetProperty("disposition").GetString());
            JsonElement requestedAssembly =
                binding.GetProperty("requested_assembly");
            Assert.Equal(
                missingAssembly,
                requestedAssembly
                    .GetProperty("name")
                    .GetString());
            Assert.Equal(
                edge.GetProperty("evidence_identity")
                    .GetProperty("assembly_reference")
                    .GetRawText(),
                requestedAssembly.GetRawText());
            Assert.Equal(
                [1],
                traversal.GetProperty("affected_roots")
                    .EnumerateArray()
                    .Select(static root => root.GetInt32()));

            (int countExit, string countOutput, string countError) =
                await RunCapturedAsync(
            [
                "depends",
                "--library",
                library,
                "-S",
                "Dependency Hierarchy",
                "--count",
            ]);

            Assert.Equal(1, countExit);
            Assert.Empty(countOutput);
            Assert.Contains(
                "--count cannot report an exact 'Dependency Hierarchy' count",
                countError,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InstalledPlatformLibraryRejectsUnusedSourceOverride()
    {
        (int exitCode, _, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            "System.Text.Json",
            "--source",
            "https://example.invalid/v3/index.json",
            "-S",
            "Dependencies",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "require a remote --package root",
            error,
            StringComparison.Ordinal);
    }

#if DEBUG
    [Fact]
    public async Task LibraryRoot_RetainsAssemblySourceKind()
    {
        string library = typeof(DependsAssetCommandTests).Assembly.Location;
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            library,
            "-S",
            "Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "Assembly",
            document.RootElement.GetProperty("roots")[0]
                .GetProperty("source")
                .GetString());
    }
#endif

    [Fact]
    public async Task PreCanceledAssetRequestDoesNotPublishAnOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Nuspec,
                    "/missing/cancelled.nuspec"),
            ],
            Select = ["Dependencies"],
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DependsCommand.ExecuteAssetDependsAsync(
                options,
                cancellation.Token));
    }

    [Fact]
    public async Task HierarchyFormatsUseOneOccurrenceCurrency()
    {
        string[] root =
        [
            "depends",
            "--project",
            AssetsFixture,
            "--depth",
            "1",
            "-S",
            "Dependency Hierarchy",
            "--rows",
            "1..2",
        ];

        (_, string markdown, _) = await RunCapturedAsync(root);
        (_, string table, _) = await RunCapturedAsync([.. root, "--table"]);
        (_, string tsv, _) = await RunCapturedAsync([.. root, "--tsv"]);
        (_, string jsonl, _) = await RunCapturedAsync([.. root, "--jsonl"]);
        (_, string json, _) = await RunCapturedAsync([.. root, "--json"]);
        (_, string tree, _) = await RunCapturedAsync([.. root, "--tree"]);
        (_, string mermaid, _) = await RunCapturedAsync(
            [.. root, "--mermaid"]);
        (_, string count, _) = await RunCapturedAsync(
            [.. root, "--count"]);

        Assert.Contains("```text", markdown, StringComparison.Ordinal);
        Assert.Equal(3, NonEmptyLines(table));
        Assert.Equal(3, NonEmptyLines(tsv));
        Assert.Equal(2, NonEmptyLines(jsonl));
        using JsonDocument typed = JsonDocument.Parse(json);
        Assert.Equal(
            2,
            typed.RootElement.GetProperty("dependency_hierarchy")
                .GetProperty("occurrences")
                .GetArrayLength());
        Assert.Contains("└", tree, StringComparison.Ordinal);
        Assert.StartsWith("graph TD", mermaid, StringComparison.Ordinal);
        Assert.Equal("2", count.Trim());
    }

    [Fact]
    public async Task SemanticWindowComposesWithRenderedLineLimit()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                AssetsFixture,
                "--depth",
                "1",
                "-S",
                "Dependency Hierarchy",
                "--rows",
                "2..2",
                "--count",
                "-n",
                "100",
                "--lines",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1..1")]
    public async Task LegacyRowsRemainCommandOwnedDuringSemanticAdoption(
        string rows)
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                AssetsFixture,
                "--depth",
                "1",
                "-S",
                "Dependency Hierarchy",
                "--rows",
                rows,
                "--count",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task PlainTextColumns_RenderProjectedHierarchyRows()
    {
        string[] arguments =
        [
            "depends",
            "--project",
            AssetsFixture,
            "--depth",
            "1",
            "--columns",
            "Target",
        ];

        (int plainExit, string plain, string plainError) =
            await RunCapturedAsync([.. arguments, "--plaintext"]);
        (int markdownExit, string markdown, string markdownError) =
            await RunCapturedAsync(arguments);

        Assert.Equal(0, plainExit);
        Assert.Equal(0, markdownExit);
        Assert.Empty(plainError);
        Assert.Empty(markdownError);
        Assert.DoesNotContain("└", plain, StringComparison.Ordinal);
        Assert.Contains("Target", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Source Kind", plain, StringComparison.Ordinal);
        Assert.Contains("Target", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Source Kind",
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task QuietPositionalTypeMode_SucceedsWithoutOutput()
    {
        PersistentCache.Initialize("dotnet-inspect-test");
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "System.Int128",
            "--platform",
            "-v:q",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task QuietPositionalTypeDepthFailsBeforeSourceAcquisition()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"depends-quiet-depth-{Guid.NewGuid():N}.dll");
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "Example.Type",
                "--library",
                missing,
                "-v:q",
                "--depth",
                "1",
            ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "--depth requires the Dependency Graph section",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HostileNuspecText_RemainsContained()
    {
        string path = WriteTemporaryFile(
            "hostile.nuspec",
            Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>Hostile.Package</id>
                    <version>1.0.0</version>
                    <authors>Author</authors>
                    <description>Hostile fixture.</description>
                    <dependencies>
                      <group targetFramework="net8.0&#x202E;hostile">
                        <dependency id="Contoso.Dependency" version="[1.0.0]" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """));

        (_, string markdown, _) = await RunCapturedAsync(
            ["depends", "--nuspec", path, "-v:n"]);
        (_, string json, _) = await RunCapturedAsync(
            ["depends", "--nuspec", path, "-v:n", "--json"]);
        (_, string tsv, _) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            path,
            "-S",
            "Dependencies",
            "--tsv",
        ]);

        foreach (string rendered in new[] { markdown, json, tsv })
            HostileOutputAssert.NoRenderingHazard(rendered, "stdout");
        Assert.Contains(@"net8.0\u202Ehostile", markdown, StringComparison.Ordinal);
        Assert.Contains(@"net8.0\\u202Ehostile", json, StringComparison.Ordinal);
        Assert.Contains(@"net8.0\u202Ehostile", tsv, StringComparison.Ordinal);
    }

#if DEBUG
    private static async Task<JsonElement> RootAsync(string path)
    {
        (int exitCode, string output, _) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            path,
            "-S",
            "Roots",
            "--json",
            "--compact",
        ]);
        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("roots")[0].Clone();
    }

    private static string RestoredDigest(JsonElement root) =>
        root.GetProperty("evidence_identity")
            .GetProperty("restored_project")
            .GetProperty("facts_digest")
            .GetString()!;
#endif

    private static async Task<int> HierarchyCountAsync(string[] arguments)
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            .. arguments,
            "-S",
            "Dependency Hierarchy",
            "--count",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        return int.Parse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> DependencyCountAsync(string[] arguments)
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            .. arguments,
            "-S",
            "Dependencies",
            "--count",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        return int.Parse(
            output.Trim(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<JsonDocument> HierarchyJsonAsync(
        string[] arguments)
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            .. arguments,
            "-S",
            "Dependency Hierarchy",
            "--json",
            "--compact",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        return JsonDocument.Parse(output);
    }

    private static int NonEmptyLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    private static IEnumerable<(string Identity, int Depth)> ParseGraphLines(
        string output)
    {
        foreach (string line in output.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            yield return (
                $"{root.GetProperty("source_identity").GetString()}\0"
                    + $"{root.GetProperty("relationship").GetString()}\0"
                    + root.GetProperty("target_identity").GetString(),
                root.GetProperty("minimum_depth").GetInt32());
        }
    }

    private static Task<(int ExitCode, string Output, string Error)>
        RunCapturedAsync(string[] args) =>
        ConsoleCapture.RunAsync(() => RunAsync(args));

    private static Task<int> RunAsync(string[] args)
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();
        string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
        return CommandLineBuilder.InvokeAsync(
            root.Parse(processed),
            processed);
    }

    private static string WriteTemporaryFile(string name, byte[] content)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-depends-tests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-depends-tests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteLocalSourcePackage(
        string folder,
        string packageId,
        string version,
        string dependenciesXml)
    {
        string path = Path.Combine(
            folder,
            $"{packageId}.{version}.nupkg");
        using FileStream file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry($"{packageId}.nuspec");
        using Stream entryStream = entry.Open();
        entryStream.Write(
            Manifest(packageId, version, dependenciesXml));
    }

    private static string Dependency(string packageId, string constraint) =>
        $"""
         <group targetFramework="net8.0">
           <dependency id="{packageId}" version="{constraint}" />
         </group>
         """;

    private static byte[] DenseProjectMeshDocument(int projects)
    {
        var targets = new JsonObject();
        for (int index = 0; index < projects; index++)
        {
            var dependencies = new JsonObject();
            for (int other = 0; other < projects; other++)
            {
                if (other != index)
                    dependencies.Add($"Mesh.Project{other}", "1.0.0");
            }

            targets.Add(
                $"Mesh.Project{index}/1.0.0",
                new JsonObject
                {
                    ["type"] = "project",
                    ["dependencies"] = dependencies,
                });
        }

        var document = new JsonObject
        {
            ["version"] = 4,
            ["targets"] = new JsonObject
            {
                ["net11.0"] = targets,
            },
            ["projectFileDependencyGroups"] = new JsonObject
            {
                ["net11.0"] =
                    new JsonArray("Mesh.Project0 >= 1.0.0"),
            },
            ["project"] = new JsonObject
            {
                ["frameworks"] = new JsonObject
                {
                    ["net11.0"] = new JsonObject
                    {
                        ["dependencies"] = new JsonObject(),
                    },
                },
            },
        };
        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }

    private static byte[] Manifest(
        string packageId,
        string version,
        string dependencies) =>
        Encoding.UTF8.GetBytes(
            $$"""
              <?xml version="1.0" encoding="utf-8"?>
              <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                <metadata>
                  <id>{{packageId}}</id>
                  <version>{{version}}</version>
                  <authors>Depends Tests</authors>
                  <description>Depends test package.</description>
                  <dependencies>
                    {{dependencies}}
                  </dependencies>
                </metadata>
              </package>
              """);

    private static InstalledPlatformPruneSource.Result PruneInventory(
        params string[] lines) =>
        PruneInventoryFor("net11.0", lines);

    private static InstalledPlatformPruneSource.Result PruneInventoryFor(
        string targetFramework,
        params string[] lines) =>
        PruneInventoryForFamily(
            "Microsoft.NETCore.App",
            targetFramework,
            lines);

    private static InstalledPlatformPruneSource.Result
        PruneInventoryForFamily(
            string family,
            string targetFramework,
            params string[] lines) =>
        new(
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    family,
                    targetFramework,
                    NuGetVersion.Parse("11.0.0")),
                lines),
            Error: null);
}
