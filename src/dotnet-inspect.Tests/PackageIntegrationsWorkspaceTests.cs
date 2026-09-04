using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Artifacts;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class PackageIntegrationsWorkspaceTests
{
    [Fact]
    public async Task ArtifactBackedCreate_RetainsArtifactUntilActiveQueryCompletes()
    {
        const string packagePath =
            "ref/net11.0/Artifact.Package.Sample.dll";
        string directory = Directory.CreateTempSubdirectory(
            "package-artifact-integrations-").FullName;
        string selectedPath = Path.Combine(
            directory,
            packagePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(selectedPath)!);
        byte[] surfaceImage = IntegrationAssembly(
            "Artifact.Package.Sample",
            "SurfaceOnlyMarker");
        byte[] implementationImage = IntegrationAssembly(
            "Artifact.Package.Sample",
            "ImplementationOnlyMarker");
        File.WriteAllBytes(selectedPath, surfaceImage);
        PackageRootBinding binding = await CreateBindingAsync(
            ("ref/net11.0/Artifact.Package.Sample.dll", surfaceImage),
            ("lib/net11.0/Artifact.Package.Sample.dll",
                implementationImage));
        var workspace =
            await PackageIntegrationsWorkspace.CreateArtifactBackedAsync(
                [new(selectedPath, "net11.0")],
                directory,
                binding,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        var callbackEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackResume = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ResolvedAssemblyReference? retainedAssembly = null;

        try
        {
            Task<bool> query = workspace.UseAssemblyAsync(
                selectedPath,
                async (retained, integrations, _) =>
                {
                    retainedAssembly = Assert.IsType<
                        ResolvedAssemblyReference>(retained);
                    Assert.IsType<ArtifactAcquisitionRegistration>(
                        retainedAssembly.Registration
                            .ArtifactRegistration);
                    var available = Assert.IsType<
                        AssemblyIntegrationsEntry.Available>(
                        integrations);
                    Assert.NotSame(
                        retainedAssembly.Registration,
                        available.Subject.Registration);
                    callbackEntered.SetResult();
                    await callbackResume.Task;
                    using Stream stream = retainedAssembly.OpenRead();
                    Assert.Equal(surfaceImage.Length, stream.Length);
                    Assert.True(ContainsType(stream, "SurfaceOnlyMarker"));
                    using Stream implementationProbe =
                        retainedAssembly.OpenRead();
                    Assert.False(
                        ContainsType(
                            implementationProbe,
                            "ImplementationOnlyMarker"));
                    return true;
                });

            await callbackEntered.Task;
            Task close = workspace.DisposeAsync().AsTask();
            Assert.False(close.IsCompleted);

            callbackResume.SetResult();
            Assert.True(await query);
            await close;
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => workspace.UseAssemblyAsync(
                    selectedPath,
                    static (_, _, _) => Task.FromResult(true)));
        }
        finally
        {
            callbackResume.TrySetResult();
            await workspace.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ArtifactBackedImplementationRejection_PreservesSurfaceWithoutPathFallback()
    {
        const string surfacePath =
            "ref/net11.0/Artifact.Rejected.Sample.dll";
        const string implementationPath =
            "lib/net11.0/Artifact.Rejected.Sample.dll";
        string directory = Directory.CreateTempSubdirectory(
            "package-artifact-rejection-").FullName;
        string selectedPath = Path.Combine(
            directory,
            surfacePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(selectedPath)!);
        byte[] surface = IntegrationAssembly(
            "Artifact.Rejected.Sample",
            "SurfaceOnlyMarker");
        byte[] malformed = [1, 2, 3];
        File.WriteAllBytes(selectedPath, surface);
        PackageRootBinding binding = await CreateBindingAsync(
            (surfacePath, surface),
            (implementationPath, malformed));
        var workspace =
            await PackageIntegrationsWorkspace.CreateArtifactBackedAsync(
                [new(selectedPath, "net11.0")],
                directory,
                binding,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        List<(string FileName, string Reason)> failures = [];
        int inspectionCount = 0;

        try
        {
            LibraryInspection? inspection =
                await Commands.PackageCommand
                    .InspectGroupedAssemblyAsync(
                        workspace,
                        selectedPath,
                        surfacePath,
                        failures,
                        (retained, integrations, _) =>
                        {
                            inspectionCount++;
                            Assert.IsType<
                                ArtifactAcquisitionRegistration>(
                                Assert.IsType<
                                        ResolvedAssemblyReference>(
                                        retained)
                                    .Registration
                                    .ArtifactRegistration);
                            Assert.IsType<
                                AssemblyIntegrationsEntry.Rejected>(
                                integrations);
                            return Task.FromResult<
                                LibraryInspection?>(new());
                        });

            Assert.NotNull(inspection);
            Assert.Equal(1, inspectionCount);
            var failure = Assert.Single(failures);
            Assert.Equal(surfacePath, failure.FileName);
            Assert.NotEmpty(failure.Reason);
        }
        finally
        {
            await workspace.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, null, "net11.0", "producer", "ref/net11.0/Test.dll", true)]
    [InlineData(false, null, "net11.0", "producer", "lib/net11.0/Test.dll", true)]
    [InlineData(true, null, "net11.0", "producer", "lib/net11.0/Test.dll", false)]
    [InlineData(false, "net11.0", "net11.0", "producer", "lib/net11.0/Test.dll", false)]
    [InlineData(false, null, null, "producer", "lib/net11.0/Test.dll", false)]
    [InlineData(false, null, "net11.0", null, "lib/net11.0/Test.dll", false)]
    [InlineData(false, null, "net11.0", "producer", "tools/net11.0/any/Test.dll", false)]
    public void ArtifactBackedSelection_IsTheRemoteDefaultTfmPath(
        bool isLocalFile,
        string? requestedTargetFramework,
        string? selectedTargetFramework,
        string? selectedProducerKey,
        string selectedPackagePath,
        bool expected)
    {
        Assert.Equal(
            expected,
            Commands.PackageCommand
                .ShouldUseArtifactBackedPackageIntegrations(
                    isLocalFile,
                    requestedTargetFramework,
                    selectedTargetFramework,
                    selectedProducerKey,
                    [selectedPackagePath]));
    }

    [Fact]
    public void Create_PartitionsNonNetFrameworkFolders()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-integrations-workspace-").FullName;
        try
        {
            string uap = Path.Combine(directory, "lib", "uap10.0");
            string portable = Path.Combine(
                directory,
                "lib",
                "portable-net45+win8");
            Directory.CreateDirectory(uap);
            Directory.CreateDirectory(portable);
            string first = Path.Combine(uap, "First.dll");
            string second = Path.Combine(portable, "Second.dll");
            File.Copy(
                typeof(PackageIntegrationsWorkspaceTests)
                    .Assembly.Location,
                first);
            File.Copy(typeof(PdbContext).Assembly.Location, second);

            using var workspace = PackageIntegrationsWorkspace.Create(
                [
                    new(
                        first,
                        "uap10.0",
                        "lib/uap10.0"),
                    new(
                        second,
                        "portable-net45+win8",
                        "lib/portable-net45+win8"),
                ],
                "Test.Package",
                "1.0.0");

            Assert.Equal(2, workspace.ContextGroupCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Create_PartitionsSameFrameworkAcrossAssetContexts()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-integrations-workspace-").FullName;
        try
        {
            string lib = Path.Combine(directory, "lib", "net8.0");
            string runtime = Path.Combine(
                directory,
                "runtimes",
                "win-x64",
                "lib",
                "net8.0");
            Directory.CreateDirectory(lib);
            Directory.CreateDirectory(runtime);
            string first = Path.Combine(lib, "First.dll");
            string second = Path.Combine(runtime, "Second.dll");
            File.Copy(
                typeof(PackageIntegrationsWorkspaceTests)
                    .Assembly.Location,
                first);
            File.Copy(typeof(PdbContext).Assembly.Location, second);

            using var workspace = PackageIntegrationsWorkspace.Create(
                [
                    Commands.PackageCommand
                        .CreatePackageIntegrationAssembly(
                            first,
                            "lib/net8.0/First.dll"),
                    Commands.PackageCommand
                        .CreatePackageIntegrationAssembly(
                            second,
                            "runtimes/win-x64/lib/net8.0/Second.dll"),
                ],
                "Test.Package",
                "1.0.0");

            Assert.Equal(2, workspace.ContextGroupCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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
                (retained, integrations, _) =>
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
        HashSet<InspectionQueryDefinition> queries =
            [AssemblyContextIntegrationOpportunitiesQuery.Definition];

        Assert.True(
            Commands.PackageCommand.RequiresGroupedIntegrations(
                queries,
                out bool includeIntegrationOpportunities));
        Assert.True(includeIntegrationOpportunities);
        Assert.Empty(queries);
    }

    [Fact]
    public async Task OpportunityDemand_UsesTheStreamingParticipantSnapshot()
    {
        string path = typeof(Npgsql.NpgsqlConnection).Assembly.Location;
        using var workspace = PackageIntegrationsWorkspace.Create(
            [new(path, "net11.0")],
            "Test.Package",
            "1.0.0",
            includeIntegrationOpportunities: true);

        await workspace.UseAssemblyAsync(
            path,
            (retained, integrations, opportunities) =>
            {
                Assert.NotNull(retained);
                var availableIntegrations = Assert.IsType<
                    AssemblyIntegrationsEntry.Available>(
                        integrations);
                var available = Assert.IsType<
                    AssemblyIntegrationOpportunitiesEntry.Available>(
                        opportunities);
                Assert.Same(
                    availableIntegrations.Subject.Registration,
                    available.Subject.Registration);
                Assert.Contains(
                    available.Opportunities,
                    opportunity =>
                        opportunity.Integration
                        == EcosystemIntegrationNames.HealthChecks);
                Assert.True(workspace.RetainedImageBytes > 0);
                return Task.FromResult(true);
            });

        Assert.Equal(0, workspace.RetainedImageBytes);
    }

    [Fact]
    public async Task IntegrationRejection_SuppressesOpportunities()
    {
        string path =
            typeof(PackageIntegrationsWorkspaceTests).Assembly.Location;
        using var workspace = PackageIntegrationsWorkspace.Create(
            [new(path, "net11.0")],
            PackageIntegrationAcquisition.Remote(
                "Test.Package",
                "1.0.0"),
            maxRetainedImageBytes: 1,
            includeIntegrationOpportunities: true);

        await workspace.UseAssemblyAsync(
            path,
            (_, integrations, opportunities) =>
            {
                var rejected = Assert.IsType<
                    AssemblyIntegrationsEntry.Rejected>(integrations);
                var opportunityRejected = Assert.IsType<
                    AssemblyIntegrationOpportunitiesEntry.Rejected>(
                        opportunities);
                Assert.Equal(
                    rejected.Failure,
                    opportunityRejected.Failure);
                return Task.FromResult(true);
            });
    }

    [Fact]
    public async Task ApplyAssemblyIntegrationsEntry_PopulatesFindings()
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
                static (_, integrations, _) =>
                    Task.FromResult(integrations!));
        var model = new LibraryInspection();
        var logger = new VerboseLogger(enabled: false);

        LibraryMetadataService.ApplyAssemblyIntegrationsEntry(
            path,
            model,
            logger,
            entry);

        Assert.NotNull(model.EcosystemIntegrationInspection);
        Assert.NotNull(model.OpenTelemetryInspection);
        Assert.Same(entry, model.AssemblyIntegrationsEntry);
    }

    [Fact]
    public async Task UseAssemblyAsync_ReleasesParticipantBeforeAdvancing()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-integrations-release-").FullName;
        try
        {
            string first = Path.Combine(directory, "First.dll");
            string second = Path.Combine(directory, "Second.dll");
            File.Copy(
                typeof(PackageIntegrationsWorkspaceTests)
                    .Assembly.Location,
                first);
            File.Copy(typeof(PdbContext).Assembly.Location, second);
            using var workspace = PackageIntegrationsWorkspace.Create(
                [
                    new(first, "net11.0"),
                    new(second, "net11.0"),
                ],
                "Test.Package",
                "1.0.0");

            await workspace.UseAssemblyAsync(
                first,
                (retained, _, _) =>
                {
                    Assert.NotNull(retained);
                    Assert.True(workspace.RetainedImageBytes > 0);
                    return Task.FromResult(true);
                });
            Assert.Equal(0, workspace.RetainedImageBytes);

            File.Delete(first);
            int reacquisitionCallbacks = 0;
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => workspace.UseAssemblyAsync(
                    first,
                    (_, _, _) =>
                    {
                        reacquisitionCallbacks++;
                        return Task.FromResult(true);
                    }));
            Assert.Equal(0, reacquisitionCallbacks);
            Assert.Equal(0, workspace.RetainedImageBytes);

            await workspace.UseAssemblyAsync(
                second,
                (retained, _, _) =>
                {
                    Assert.NotNull(retained);
                    Assert.True(workspace.RetainedImageBytes > 0);
                    return Task.FromResult(true);
                });
            Assert.Equal(0, workspace.RetainedImageBytes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
                async (retained, integrations, _) =>
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
                        assemblyReference: retained,
                        integrationsEntry: entry);
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
                (_, _, _) =>
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
    public async Task UnreadablePreflight_DoesNotFallBackToPathInspection()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-integrations-unreadable-").FullName;
        string path = Path.Combine(directory, "Locked.dll");
        File.Copy(
            typeof(PackageIntegrationsWorkspaceTests)
                .Assembly.Location,
            path);
        try
        {
            using var locked = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            using var workspace =
                PackageIntegrationsWorkspace.Create(
                    [new(path, "net11.0")],
                    "Test.Package",
                    "1.0.0");
            List<(string FileName, string Reason)> failures = [];
            int inspectionCount = 0;

            LibraryInspection? inspection =
                await Commands.PackageCommand
                    .InspectGroupedAssemblyAsync(
                        workspace,
                        path,
                        "ref/net11.0/Locked.dll",
                        failures,
                        (_, _, _) =>
                        {
                            inspectionCount++;
                            return Task.FromResult<
                                LibraryInspection?>(new());
                        });

            Assert.Null(inspection);
            Assert.Equal(0, inspectionCount);
            var failure = Assert.Single(failures);
            Assert.Equal(
                "ref/net11.0/Locked.dll",
                failure.FileName);
            Assert.Contains(
                "could not be read",
                failure.Reason,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

    static async Task<PackageRootBinding> CreateBindingAsync(
        params (string Path, byte[] Content)[] entries)
    {
        var store = new InMemoryPackageStore();
        await using var archive = new MemoryStream(Archive(entries));
        IPackageContent content = await store.CommitAsync(
            "Artifact.Package.Sample",
            "1.0.0",
            "tests",
            archive,
            TestContext.Current.CancellationToken);
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(
                "Artifact.Package.Sample",
                "1.0.0"),
            content,
            "tests",
            PackagePayloadOrigin.Cache);
        return PackageRootBinding.CreateFromSource(
            payload,
            "net11.0");
    }

    static byte[] IntegrationAssembly(
        string assemblyName,
        string typeName)
    {
        var assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module =
            assemblyBuilder.DefineDynamicModule(assemblyName);
        TypeBuilder type = module.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Class);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        type.CreateType();

        using var stream = new MemoryStream();
        assemblyBuilder.Save(stream);
        return stream.ToArray();
    }

    static bool ContainsType(
        Stream stream,
        string typeName)
    {
        using (var pe = new PEReader(
                   stream,
                   PEStreamOptions.LeaveOpen))
        {
            MetadataReader reader = pe.GetMetadataReader();
            return reader.TypeDefinitions.Any(handle =>
                reader.GetString(
                    reader.GetTypeDefinition(handle).Name)
                == typeName);
        }
    }

    static byte[] Archive(
        params (string Path, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using Stream output = entry.Open();
                output.Write(content);
            }
        }

        return stream.ToArray();
    }
}
