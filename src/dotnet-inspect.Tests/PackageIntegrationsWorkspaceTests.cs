using System.Collections.Immutable;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class PackageIntegrationsWorkspaceTests
{
    [Fact]
    public async Task Create_PartitionsTfmsAndRetainsParticipantGeneration()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-integrations-workspace-").FullName;
        try
        {
            string net8 = Path.Combine(directory, "lib", "net8.0");
            string net9 = Path.Combine(directory, "lib", "net9.0");
            Directory.CreateDirectory(net8);
            Directory.CreateDirectory(net9);
            string first = Path.Combine(net8, "First.dll");
            string second = Path.Combine(net8, "Second.dll");
            string third = Path.Combine(net9, "Third.dll");
            File.Copy(
                typeof(PackageIntegrationsWorkspaceTests)
                    .Assembly.Location,
                first);
            File.Copy(typeof(PdbContext).Assembly.Location, second);
            File.Copy(
                typeof(PackageIntegrationsWorkspaceTests)
                    .Assembly.Location,
                third);

            using var workspace = PackageIntegrationsWorkspace.Create(
                [
                    new(first, "net8.0"),
                    new(second, "net8.0"),
                    new(third, "net9.0"),
                ],
                "Test.Package",
                "1.0.0");

            Assert.Equal(2, workspace.ContextGroupCount);

            var observed = await workspace.UseAssemblyAsync(
                first,
                (retained, integrations) =>
                {
                    Assert.NotNull(retained);
                    AssemblyIntegrationsEntry.Available available =
                        Assert.IsType<
                            AssemblyIntegrationsEntry.Available>(
                            integrations);
                    Assert.Same(
                        available.Subject.Registration,
                        retained.Registration);
                    var provenance =
                        Assert.IsType<
                            AssemblyResolutionProvenance.PackageAsset>(
                            available.Subject.Provenance);
                    Assert.Equal("net8.0", provenance.Tfm);

                    File.Copy(second, first, overwrite: true);
                    using PdbContext context =
                        PdbContext.OpenPrefetched(retained);
                    MethodBodyInspectionSession bodySession =
                        MethodBodyInspectionSession
                            .OpenWithPrefetchedImage(
                                first,
                                context,
                                ILInspector.Analysis
                                    .LibraryBodyAnalysisFeatures.None,
                                assembly: retained);
                    Assert.Same(
                        retained.Registration,
                        bodySession.Assembly.Registration);
                    return Task.FromResult(
                        context.ExtractAssemblyInfo().AssemblyName);
                });

            Assert.Equal(
                typeof(PackageIntegrationsWorkspaceTests)
                    .Assembly.GetName().Name,
                observed);
            Assert.Equal(0, workspace.RetainedImageBytes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OpportunityOnlyDemand_RequiresGroupedIntegrations()
    {
        ScannerRegistry registry =
            Sections.LibrarySections.CreateScannerRegistry();

        Assert.True(
            Commands.PackageCommand.RequiresGroupedIntegrations(
                [Sections.LibrarySections.ScannerIntegrationOpportunities],
                registry));
    }

    [Fact]
    public void IntegrationFailure_SuppressesOpportunities()
    {
        string path =
            typeof(PackageIntegrationsWorkspaceTests).Assembly.Location;
        var subject = new FindingSubject(
            path,
            Path.GetFileName(path));
        var model = new LibraryInspection
        {
            EcosystemIntegrationInspection =
                new FindingInspection<
                    EcosystemIntegrationSignalInfo>.Failed(
                    new InspectionError(
                        subject,
                        MetadataFindings.EcosystemIntegrationDescriptor,
                        "failed")),
            OpenTelemetryInspection =
                new FindingInspection<OpenTelemetrySignalInfo>.Failed(
                    new InspectionError(
                        subject,
                        MetadataFindings.OpenTelemetrySignalDescriptor,
                        "failed")),
            IntegrationOpportunities =
            [
                new("Logging", "AddLogging", "API", "package"),
            ],
        };

        using var session = AssemblyInspectionSession.Open(path);
        LibraryMetadataService.ScanIntegrationOpportunities(
            session,
            path,
            model,
            new VerboseLogger(enabled: false));

        Assert.Null(model.IntegrationOpportunities);
    }

    [Fact]
    public async Task ApplyAssemblyIntegrationsResult_PreventsLegacyRescan()
    {
        string path =
            typeof(PackageIntegrationsWorkspaceTests).Assembly.Location;
        using var workspace = PackageIntegrationsWorkspace.Create(
            [new(path, "net11.0")],
            "Test.Package",
            "1.0.0");
        AssemblyIntegrationsEntry entry =
            await workspace.UseAssemblyAsync(
                path,
                static (_, integrations) =>
                    Task.FromResult(integrations!));
        var model = new LibraryInspection();
        var logger = new VerboseLogger(enabled: false);

        LibraryMetadataService.ApplyAssemblyIntegrationsResult(
            path,
            model,
            logger,
            entry);
        FindingInspection<EcosystemIntegrationSignalInfo>? ecosystem =
            model.EcosystemIntegrationInspection;
        FindingInspection<OpenTelemetrySignalInfo>? openTelemetry =
            model.OpenTelemetryInspection;

        using var session = AssemblyInspectionSession.Open(path);
        LibraryMetadataService.ScanIntegrations(
            session,
            path,
            model,
            logger);

        Assert.Same(ecosystem, model.EcosystemIntegrationInspection);
        Assert.Same(openTelemetry, model.OpenTelemetryInspection);
    }

    [Fact]
    public async Task UseAssemblyAsync_ReleasesParticipantBeforeAdvancing()
    {
        string first =
            typeof(PackageIntegrationsWorkspaceTests).Assembly.Location;
        string second = typeof(PdbContext).Assembly.Location;
        using var workspace = PackageIntegrationsWorkspace.Create(
            [
                new(first, "net11.0"),
                new(second, "net11.0"),
            ],
            "Test.Package",
            "1.0.0");

        await workspace.UseAssemblyAsync(
            first,
            (retained, _) =>
            {
                Assert.NotNull(retained);
                Assert.True(workspace.RetainedImageBytes > 0);
                return Task.FromResult(true);
            });
        Assert.Equal(0, workspace.RetainedImageBytes);

        await workspace.UseAssemblyAsync(
            second,
            (retained, _) =>
            {
                Assert.NotNull(retained);
                Assert.True(workspace.RetainedImageBytes > 0);
                return Task.FromResult(true);
            });
        Assert.Equal(0, workspace.RetainedImageBytes);
    }

    [Theory]
    [InlineData(" Test.Package ", " 1.2.3 ", true)]
    [InlineData("Test Package", "1.2.3", false)]
    [InlineData("Test.Package", "not-a-version", false)]
    [InlineData("", "1.2.3", false)]
    [InlineData(".Test.Package", "1.2.3", false)]
    [InlineData("Test.Package-", "1.2.3", false)]
    [InlineData("Test..Package", "1.2.3", false)]
    public void LocalAcquisition_UsesOnlyValidNuspecCoordinates(
        string packageId,
        string packageVersion,
        bool expectedPackageProvenance)
    {
        var acquisition = PackageIntegrationAcquisition.Local(
            packageId,
            packageVersion);

        AssemblyResolutionProvenance provenance =
            acquisition.CreateProvenance("net10.0");

        if (expectedPackageProvenance)
        {
            var package = Assert.IsType<
                AssemblyResolutionProvenance.PackageAsset>(
                provenance);
            Assert.Equal("Test.Package", package.PackageId);
            Assert.Equal("1.2.3", package.PackageVersion);
            Assert.Equal("net10.0", package.Tfm);
        }
        else
        {
            Assert.IsType<AssemblyResolutionProvenance.LocalAsset>(
                provenance);
        }
    }

    [Fact]
    public void RemoteAcquisition_UsesResolvedCoordinate()
    {
        var resolution = new Packages.PackageExtractionResult(
            "/tmp/payload",
            TempDir: null,
            PackageName: "Resolved.Package",
            Version: "2.0.0");
        var acquisition = PackageIntegrationAcquisition.Remote(
            resolution,
            "Wrapper.Package",
            "1.0.0");

        var package = Assert.IsType<
            AssemblyResolutionProvenance.PackageAsset>(
            acquisition.CreateProvenance("net10.0"));

        Assert.Equal("Resolved.Package", package.PackageId);
        Assert.Equal("2.0.0", package.PackageVersion);
    }

    [Fact]
    public async Task GroupedEvidence_SuppliesIntegrationPresence()
    {
        var (path, _, _, error) =
            Services.PlatformResolver.ResolveAssembly(
                "Microsoft.Extensions.Configuration");
        Assert.Null(error);
        Assert.NotNull(path);
        using var workspace = PackageIntegrationsWorkspace.Create(
            [new(path, "net11.0")],
            "Test.Package",
            "1.0.0");
        using var httpClient = new HttpClient();
        CoreCache.Initialize("dotnet-inspect-test");

        LibraryInspection? inspection =
            await workspace.UseAssemblyAsync(
                path,
                async (retained, integrations) =>
                {
                    var available = Assert.IsType<
                        AssemblyIntegrationsEntry.Available>(
                        integrations);
                    AssemblyIntegrationsEntry entry = available with
                    {
                        EcosystemSignals =
                            ImmutableArray<
                                EcosystemIntegrationSignalInfo>.Empty,
                        OpenTelemetrySignals =
                            ImmutableArray<
                                OpenTelemetrySignalInfo>.Empty,
                        Presence = new EcosystemIntegrationPresence(),
                    };
                    return await LibraryMetadataService.InspectAsync(
                        path,
                        new Options.LibraryOptions(),
                        new VerboseLogger(enabled: false),
                        packageName: null,
                        packageVersion: null,
                        httpClient,
                        retainedAssembly: retained,
                        assemblyIntegrations: entry);
                });

        Assert.NotNull(inspection);
        Assert.False(inspection.HasConfigurationSupport);
        Assert.False(inspection.HasOpenTelemetrySupport);
        Assert.Equal(0, inspection.IntegrationCount);
        Assert.NotNull(inspection.EcosystemIntegrationInspection);
        Assert.NotNull(inspection.OpenTelemetryInspection);
    }

    [Fact]
    public async Task GroupedRejection_DoesNotFallBackToPathInspection()
    {
        string path =
            typeof(PackageIntegrationsWorkspaceTests).Assembly.Location;
        using var workspace = PackageIntegrationsWorkspace.Create(
            [new(path, "net11.0")],
            PackageIntegrationAcquisition.Remote(
                "Test.Package",
                "1.0.0"),
            maxRetainedImageBytes: 1);
        List<(string FileName, string Reason)> failures = [];
        int inspectionCount = 0;

        LibraryInspection? inspection =
            await Commands.PackageCommand.InspectGroupedAssemblyAsync(
                workspace,
                path,
                "ref/net11.0/Test.dll",
                failures,
                (_, _) =>
                {
                    inspectionCount++;
                    return Task.FromResult<LibraryInspection?>(new());
                });

        Assert.Null(inspection);
        Assert.Equal(0, inspectionCount);
        var failure = Assert.Single(failures);
        Assert.Equal("ref/net11.0/Test.dll", failure.FileName);
        Assert.Contains(
            "budget",
            failure.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GroupedIntegrationsFailure_IsVisibleAndDeduplicated()
    {
        (string FileName, string Reason) failure =
            ("lib/net10.0/Test.dll", "invalid method body");

        bool incomplete = false;
        var (_, error) = await ConsoleCapture.RunAsync(() =>
        {
            incomplete =
                Commands.PackageCommand.WriteGroupedIntegrationsFailures(
                    [failure, failure]);
        });

        Assert.True(incomplete);
        Assert.Equal(
            1,
            Commands.PackageCommand.AllLibrariesCompletionExitCode(
                incomplete));
        Assert.Contains(
            "Integrations inspection failed for 'lib/net10.0/Test.dll': invalid method body",
            error,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            error.Split(
                "Integrations inspection failed",
                StringSplitOptions.None).Length - 1);
    }
}
