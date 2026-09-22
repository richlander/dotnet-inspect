using System.CommandLine;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
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

public partial class DependsAssetCommandTests
{
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
        Assert.Equal(2, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(
            "asset-dependencies",
            root.GetProperty("result_kind").GetString());
        Assert.True(root.TryGetProperty("content", out _));
        JsonElement portableProjection =
            root.GetProperty("portable_projection");
        Assert.Equal(
            "nonProjectable",
            portableProjection.GetProperty("kind").GetString());
        Assert.Equal(
            "notSupported",
            portableProjection.GetProperty("reason").GetString());
        Assert.Contains(
            "requires exactly one package root",
            portableProjection.GetProperty("explanation").GetString(),
            StringComparison.Ordinal);
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

    [Fact]
    public async Task PairedEnvelopeWritesEqualBaselineToDistinctOutputFile()
    {
        using var directory =
            new TemporaryTestDirectory("depends-evidence-paired-");
        string primary = Path.Combine(
            directory.FullName,
            "baseline.json");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");

        var result = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--envelope",
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
            baselineRoot.GetProperty("portable_projection"),
            enrichedRoot.GetProperty("portable_projection")));
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
        JsonElement share = document.RootElement.GetProperty("portable_projection");
        Assert.Equal("available", share.GetProperty("kind").GetString());
        Assert.Equal(
            packet,
            share.GetProperty("packet").GetString());
        Assert.Equal(
            WorkspaceShareOutput.UrlPrefix + packet,
            share.GetProperty("full_url").GetString());

        var envelope = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "net10.0",
            "--envelope",
            "--compact",
        ]);

        Assert.Equal(0, envelope.ExitCode);
        Assert.Empty(envelope.Error);
        using JsonDocument envelopeDocument =
            JsonDocument.Parse(envelope.Output);
        JsonElement envelopeProjection =
            envelopeDocument.RootElement.GetProperty(
                "portable_projection");
        Assert.Equal(
            "available",
            envelopeProjection.GetProperty("kind").GetString());
        Assert.Equal(
            packet,
            envelopeProjection.GetProperty("packet").GetString());
    }

    [Fact]
    public async Task EvidenceEnvelopePreservesExactPackageShareWhenManifestIsUnavailable()
    {
        const string packageId =
            "Copilot.Pr7538.DoesNotExist.9f4a3d71e6b64b1ab8a6058df130417a";
        string[] arguments =
        [
            "depends",
            "--package",
            $"{packageId}@1.0.0",
            "--tfm",
            "net8.0",
            "--source",
            "https://api.nuget.org/v3/index.json",
            "--share",
            "packet",
            "--verbose",
        ];
        var ordinary = await RunCapturedOfflineAsync(arguments);
        using var directory =
            new TemporaryTestDirectory("depends-evidence-share-exact-");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");

        var withEvidence = await RunCapturedOfflineAsync(
        [
            .. arguments,
            "--evidence-envelope",
            sidecar,
        ]);

        Assert.True(
            ordinary.ExitCode == 0,
            $"Ordinary stderr:{Environment.NewLine}{ordinary.Error}");
        Assert.True(
            ordinary.ExitCode == withEvidence.ExitCode,
            $"Evidence stderr:{Environment.NewLine}{withEvidence.Error}");
        Assert.Equal(ordinary.Output, withEvidence.Output);
        Assert.Equal(
            ordinary.Error
                + $"Evidence envelope: {sidecar}{Environment.NewLine}",
            withEvidence.Error);
        using JsonDocument document =
            JsonDocument.Parse(await File.ReadAllTextAsync(
                sidecar,
                TestContext.Current.CancellationToken));
        JsonElement packageInputs =
            document.RootElement.GetProperty("evidence")
                .GetProperty("package_inputs");
        Assert.Empty(packageInputs.GetProperty("roots").EnumerateArray());
        JsonElement failure = Assert.Single(
            packageInputs.GetProperty("failed_roots").EnumerateArray());
        Assert.Equal(
            "acquisition",
            failure.GetProperty("kind").GetString());
        Assert.Equal(
            packageId.ToLowerInvariant(),
            failure.GetProperty("coordinate")
                .GetProperty("package_id")
                .GetString());
        Assert.Equal(
            "1.0.0",
            failure.GetProperty("coordinate")
                .GetProperty("version")
                .GetString());
    }

    [Fact]
    public async Task EvidenceEnvelopePreservesLatestPackageShareAndSettledCoordinate()
    {
        const string packageId = "Contoso.PortableProjection.Latest";
        const string version = "2.0.0";
        string[] arguments =
        [
            "depends",
            "--package",
            $"{packageId}@LaTeSt",
            "--tfm",
            "net8.0",
            "--source",
            "https://api.nuget.org/v3/index.json",
            "--share",
            "packet",
            "--verbose",
        ];
        var ordinary = await RunCapturedWithPackageFeedAsync(
            arguments,
            packageId,
            version);
        using var directory =
            new TemporaryTestDirectory("depends-evidence-share-latest-");
        string sidecar = Path.Combine(
            directory.FullName,
            "evidence.json");

        var withEvidence = await RunCapturedWithPackageFeedAsync(
        [
            .. arguments,
            "--evidence-envelope",
            sidecar,
        ],
            packageId,
            version);

        Assert.True(
            ordinary.ExitCode == 0,
            $"Ordinary stderr:{Environment.NewLine}{ordinary.Error}");
        Assert.True(
            ordinary.ExitCode == withEvidence.ExitCode,
            $"Evidence stderr:{Environment.NewLine}{withEvidence.Error}");
        Assert.Equal(ordinary.Output, withEvidence.Output);
        Assert.Equal(
            ordinary.Error
                + $"Evidence envelope: {sidecar}{Environment.NewLine}",
            withEvidence.Error);
        Assert.Single(
            withEvidence.Requests,
            static request => request.Host.Equals(
                "azuresearch-usnc.nuget.org",
                StringComparison.OrdinalIgnoreCase));

        using JsonDocument document =
            JsonDocument.Parse(await File.ReadAllTextAsync(
                sidecar,
                TestContext.Current.CancellationToken));
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("evidence")
                .GetProperty("package_inputs")
                .GetProperty("failed_roots")
                .EnumerateArray());
        Assert.Equal(
            packageId.ToLowerInvariant(),
            failure.GetProperty("coordinate")
                .GetProperty("package_id")
                .GetString());
        Assert.Equal(
            version,
            failure.GetProperty("coordinate")
                .GetProperty("version")
                .GetString());
        Assert.Equal(
            $"{packageId}@LaTeSt",
            failure.GetProperty("source_label").GetString());
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
        const string packageId = "Sidecar.PortableProjection";
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
}
