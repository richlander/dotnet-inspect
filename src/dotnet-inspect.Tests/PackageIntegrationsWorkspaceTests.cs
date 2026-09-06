using System.Collections.Immutable;
using System.Diagnostics;
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
        DateTime selectedTimestamp =
            new(2024, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(selectedPath, selectedTimestamp);
        PackageRootBinding binding = await CreateBindingAsync(
            (surfacePath, surface),
            (implementationPath, malformed));
        PackageIntegrationsWorkspace? workspace =
            await PackageIntegrationsWorkspace.TryCreateArtifactBackedAsync(
                [new(selectedPath, "net11.0")],
                directory,
                binding,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.NotNull(workspace);
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
            Assert.Equal(selectedTimestamp, inspection.LastModified);
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
    [InlineData(false, "net11.0", "producer", "ref/net11.0/Test.dll", true)]
    [InlineData(false, "net11.0", "producer", "lib/net11.0/Test.dll", true)]
    [InlineData(true, "net11.0", "producer", "lib/net11.0/Test.dll", false)]
    [InlineData(false, null, "producer", "lib/net11.0/Test.dll", false)]
    [InlineData(false, "net11.0", null, "lib/net11.0/Test.dll", false)]
    [InlineData(false, "net11.0", "producer", "tools/net11.0/any/Test.dll", false)]
    [InlineData(false, "net35-Unity Full v3.5", "producer", "lib/net35-Unity Full v3.5/Test.dll", false)]
    public void CompileRoleSelection_RequiresOneRemoteCompileFramework(
        bool isLocalFile,
        string? selectedTargetFramework,
        string? selectedProducerKey,
        string selectedPackagePath,
        bool expected)
    {
        Assert.Equal(
            expected,
            Commands.PackageCommand
                .ShouldUsePackageCompileRoles(
                    isLocalFile,
                    selectedTargetFramework,
                    selectedProducerKey,
                    [selectedPackagePath]));
    }

    [Theory]
    [InlineData(null, false, true)]
    [InlineData("net10.0", false, true)]
    [InlineData("all", false, false)]
    [InlineData("net10.0", true, false)]
    [InlineData("net35-Unity Full v3.5", false, false)]
    public async Task PackageCommand_ExplicitTfmPreservesSelectionAndUsesCompatibleArtifactRoles(
        string? targetFramework,
        bool includeReferenceRole,
        bool artifactBacked)
    {
        const string packageName = "Artifact.Command.Sample";
        const string source = "https://artifact-command.invalid/v3/index.json";
        string directory = Directory.CreateTempSubdirectory(
            "package-artifact-command-").FullName;
        bool wasOffline = Core.HttpClientFactory.IsOffline;
        try
        {
            string staged = Path.Combine(directory, "content");
            byte[] image = File.ReadAllBytes(
                typeof(Npgsql.NpgsqlConnection).Assembly.Location);
            string fixtureFramework = targetFramework is null or "all"
                ? "net10.0"
                : targetFramework;
            foreach (string framework in new[] { fixtureFramework, "net11.0" })
            {
                string assets = Path.Combine(staged, "lib", framework);
                Directory.CreateDirectory(assets);
                File.WriteAllBytes(Path.Combine(assets, "Npgsql.dll"), image);
            }
            if (includeReferenceRole)
            {
                string assets = Path.Combine(staged, "ref", fixtureFramework);
                Directory.CreateDirectory(assets);
                File.WriteAllBytes(Path.Combine(assets, "Npgsql.dll"), image);
            }
            File.WriteAllText(
                Path.Combine(staged, $"{packageName}.nuspec"),
                $"""
                <package><metadata>
                  <id>{packageName}</id><version>1.0.0</version>
                  <authors>tests</authors><description>test package</description>
                </metadata></package>
                """);
            string archive = Path.Combine(directory, $"{packageName}.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(staged, archive);
            NuGetCache.Initialize(
                "dotnet-inspect-test",
                Path.Combine(directory, "cache"),
                skipNuGetCache: true);
            NuGetCache.CommitPackage(
                staged, archive, packageName, "1.0.0",
                NuGetCache.GetSourceKey(source));
            Core.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = true });
            Core.HttpClientFactory.ResetSharedForTesting();

            string[] arguments =
            [
                "package", $"{packageName}@1.0.0",
                "--all-libraries",
                .. targetFramework is null ? Array.Empty<string>() : ["--tfm", targetFramework],
                "-S", "Integration: Opportunities",
                "--source", source,
                "--markdown", "--verbose", "--tips", "q",
            ];
            var (exit, output, error) = await ConsoleCapture.RunAsync(async () =>
            {
                arguments = CommandLineBuilder.PreprocessArgs(arguments);
                var parsed = CommandLineBuilder.CreateRootCommand().Parse(arguments);
                Assert.Empty(parsed.Errors);
                return await CommandLineBuilder.InvokeAsync(parsed);
            });

            Assert.True(exit == 0, error);
            Assert.Contains("## Integration: Opportunities", output);
            Assert.Contains("Npgsql.NpgsqlConnection", output);
            Assert.Equal(
                includeReferenceRole || targetFramework == "all" ? 2 : 1,
                output.Split(
                    "| Aspire | `Npgsql.NpgsqlConnection` |",
                    StringSplitOptions.None).Length - 1);
            Assert.Equal(
                artifactBacked,
                error.Contains(
                    "Using artifact-backed package Integrations for ",
                    StringComparison.Ordinal));
            if (artifactBacked)
            {
                Assert.Contains(
                    $"Using artifact-backed package Integrations for {targetFramework ?? "net11.0"}.",
                    error);
            }
            else
            {
                Assert.Contains(
                    "Using artifact-backed selected-entry package Integrations.",
                    error);
            }
            if (targetFramework == "all")
            {
                Assert.Contains("net10.0", output);
                Assert.Contains("net11.0", output);
            }
            else
            {
                Assert.Contains(targetFramework ?? "net11.0", output);
                Assert.DoesNotContain(
                    targetFramework is null ? "net10.0" : "net11.0",
                    output);
            }
        }
        finally
        {
            Core.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = wasOffline });
            Core.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TryArtifactBackedCreate_RequiresExactVisibleSurfaceSelection()
    {
        const string surfacePath =
            "lib/net11.0/Artifact.Surface.Sample.dll";
        const string nestedPath =
            "lib/net11.0/x64/Artifact.Native.Sample.dll";
        string directory = Directory.CreateTempSubdirectory(
            "package-artifact-surface-").FullName;
        byte[] image = IntegrationAssembly(
            "Artifact.Surface.Sample",
            "SurfaceMarker");
        PackageRootBinding binding = await CreateBindingAsync(
            (surfacePath, image),
            (nestedPath, image));

        try
        {
            PackageIntegrationsWorkspace? workspace =
                await PackageIntegrationsWorkspace
                    .TryCreateArtifactBackedAsync(
                        [
                            new(
                                Path.Combine(directory, surfacePath),
                                "net11.0"),
                            new(
                                Path.Combine(directory, nestedPath),
                                "net11.0"),
                        ],
                        directory,
                        binding,
                        cancellationToken:
                            TestContext.Current.CancellationToken);

            Assert.Null(workspace);
            await using var selected = await PackageIntegrationsWorkspace.CreateSelectedAsync(
                [
                    new(Path.Combine(directory, surfacePath), "net11.0", "lib/net11.0"),
                    new(Path.Combine(directory, nestedPath), "net11.0", "lib/net11.0/x64"),
                ],
                directory,
                PackageInspectionInput.CreateFromBinding(binding),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(2, selected.ContextGroupCount);
            await selected.UseAssemblyAsync(
                Path.Combine(directory, nestedPath),
                (retained, integrations, _) =>
                {
                    Assert.NotNull(retained?.Registration.ArtifactRegistration);
                    Assert.IsType<AssemblyIntegrationsEntry.Available>(integrations);
                    return Task.FromResult(true);
                });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TryArtifactBackedCreate_RejectsEmptyCompileGroup()
    {
        const string selectedPath =
            "lib/net11.0/Artifact.Empty.Sample.dll";
        string directory = Directory.CreateTempSubdirectory(
            "package-artifact-empty-").FullName;
        byte[] image = IntegrationAssembly(
            "Artifact.Empty.Sample",
            "ImplementationMarker");
        PackageRootBinding binding = await CreateBindingAsync(
            ("ref/net11.0/_._", []),
            (selectedPath, image));

        try
        {
            PackageIntegrationsWorkspace? workspace =
                await PackageIntegrationsWorkspace
                    .TryCreateArtifactBackedAsync(
                        [
                            new(
                                Path.Combine(directory, selectedPath),
                                "net11.0"),
                        ],
                        directory,
                        binding,
                        cancellationToken:
                            TestContext.Current.CancellationToken);

            Assert.Null(workspace);
            await using var selected = await PackageIntegrationsWorkspace.CreateSelectedAsync(
                [new(Path.Combine(directory, selectedPath), "net11.0")],
                directory,
                PackageInspectionInput.CreateFromBinding(binding),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(
                PackageCompileAssetSelectionStatus.EmptyCompileGroup,
                binding.Root.AssetSelection.Status);
            await selected.UseAssemblyAsync(
                Path.Combine(directory, selectedPath),
                (retained, integrations, _) =>
                {
                    Assert.NotNull(retained?.Registration.ArtifactRegistration);
                    Assert.IsType<AssemblyIntegrationsEntry.Available>(integrations);
                    return Task.FromResult(true);
                });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TryArtifactBackedCreate_RejectsIdentityMismatch()
    {
        const string surfacePath =
            "ref/net11.0/Artifact.Mismatch.Sample.dll";
        const string implementationPath =
            "lib/net11.0/Artifact.Mismatch.Sample.dll";
        string directory = Directory.CreateTempSubdirectory(
            "package-artifact-mismatch-").FullName;
        PackageRootBinding binding = await CreateBindingAsync(
            (
                surfacePath,
                IntegrationAssembly(
                    "Artifact.Surface.Identity",
                    "SurfaceMarker")),
            (
                implementationPath,
                IntegrationAssembly(
                    "Artifact.Implementation.Identity",
                    "ImplementationMarker")));

        try
        {
            PackageIntegrationsWorkspace? workspace =
                await PackageIntegrationsWorkspace
                    .TryCreateArtifactBackedAsync(
                        [
                            new(
                                Path.Combine(directory, surfacePath),
                                "net11.0"),
                        ],
                        directory,
                        binding,
                        cancellationToken:
                            TestContext.Current.CancellationToken);

            Assert.Null(workspace);
            await using var selected = await PackageIntegrationsWorkspace.CreateSelectedAsync(
                [new(Path.Combine(directory, surfacePath), "net11.0")],
                directory,
                PackageInspectionInput.CreateFromBinding(binding),
                cancellationToken: TestContext.Current.CancellationToken);
            await selected.UseAssemblyAsync(
                Path.Combine(directory, surfacePath),
                (retained, integrations, _) =>
                {
                    Assert.NotNull(retained);
                    Assert.Equal("Artifact.Surface.Identity", retained.Identity.Name);
                    Assert.NotNull(retained.Registration.ArtifactRegistration);
                    var available = Assert.IsType<AssemblyIntegrationsEntry.Available>(integrations);
                    Assert.Same(retained.Registration, available.Subject.Registration);
                    return Task.FromResult(true);
                });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Create_PartitionsNonNetFrameworkFolders()
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

            await using var workspace = await CreateSelectedWorkspaceAsync(
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
    public async Task Create_PartitionsSameFrameworkAcrossAssetContexts()
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

            await using var workspace = await CreateSelectedWorkspaceAsync(
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

            await using var workspace = await CreateSelectedWorkspaceAsync(
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
                    Assert.NotNull(retained.Registration.ArtifactRegistration);
                    Assert.Null(retained.Path);
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
        await using var workspace = await CreateSelectedWorkspaceAsync(
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
        await using var workspace = await CreateSelectedWorkspaceAsync(
            [new(path, "net11.0")],
            "Test.Package",
            "1.0.0",
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
        await using var workspace = await CreateSelectedWorkspaceAsync(
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
            await using var workspace = await CreateSelectedWorkspaceAsync(
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
    public async Task LocalAcquisition_UsesOnlyValidNuspecCoordinates(
        string packageId,
        string packageVersion,
        bool expectedPackageProvenance)
    {
        string path = typeof(PackageIntegrationsWorkspaceTests).Assembly.Location;
        await using var workspace = await CreateSelectedWorkspaceAsync(
            [new(path, "net10.0")],
            packageId,
            packageVersion);

        AssemblyResolutionProvenance provenance =
            await workspace.UseAssemblyAsync(
                path,
                (retained, _, _) => Task.FromResult(
                    Assert.IsType<ResolvedAssemblyReference>(retained).Provenance));

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
    public async Task RemoteAcquisition_UsesResolvedCoordinate()
    {
        const string entry = "tools/net10.0/any/Sample.dll";
        PackageRootBinding binding = await CreateBindingAsync(
            (entry, IntegrationAssembly("Sample", "Marker")));
        var input = PackageInspectionInput.CreateFromBinding(binding);
        await using var workspace = await PackageIntegrationsWorkspace.CreateSelectedAsync(
            [new(Path.GetFullPath(entry), "net10.0")],
            Directory.GetCurrentDirectory(),
            input,
            cancellationToken: TestContext.Current.CancellationToken);

        var package = Assert.IsType<
            AssemblyResolutionProvenance.PackageAsset>(
            await workspace.UseAssemblyAsync(
                entry,
                (retained, _, _) => Task.FromResult(
                    Assert.IsType<ResolvedAssemblyReference>(retained).Provenance)));

        Assert.Equal(binding.Root.PackageId, package.PackageId);
        Assert.Equal(binding.Root.PackageVersion, package.PackageVersion);
        Assert.Equal(binding.Coordinate, input.Coordinate);
        Assert.Same(binding.ContentGenerationIdentity, input.ContentGenerationIdentity);
    }

    [Fact]
    public async Task GroupedEvidence_SuppliesIntegrationPresence()
    {
        var (path, _, _, error) =
            Services.PlatformResolver.ResolveAssembly(
                "Microsoft.Extensions.Configuration");
        Assert.Null(error);
        Assert.NotNull(path);
        await using var workspace = await CreateSelectedWorkspaceAsync(
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
        await using var workspace = await CreateSelectedWorkspaceAsync(
            [new(path, "net11.0")],
            "Test.Package",
            "1.0.0",
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
            await using var workspace =
                await CreateSelectedWorkspaceAsync(
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

    [Theory]
    [InlineData("empty-compile", 1)]
    [InlineData("all-frameworks", 2)]
    [InlineData("nested", 2)]
    [InlineData("tools", 2)]
    [InlineData("no-nuspec", 1)]
    [InlineData("invalid-nuspec-id", 1)]
    [InlineData("invalid-nuspec-version", 1)]
    [InlineData("native-image", 1)]
    [InlineData("invalid-image", 1)]
    public async Task PackageCommand_LocalInspectionSelectionPreservesSupportedShapes(
        string shape,
        int expectedLibraries)
    {
        string directory = Directory.CreateTempSubdirectory("package-selected-command-").FullName;
        try
        {
            byte[] image = File.ReadAllBytes(typeof(Npgsql.NpgsqlConnection).Assembly.Location);
            List<(string Path, byte[] Content)> entries =
                [("lib/net11.0/Npgsql.dll", image)];
            switch (shape)
            {
                case "empty-compile":
                    entries.Add(("ref/net11.0/_._", []));
                    break;
                case "all-frameworks":
                    entries.Add(("lib/net10.0/Npgsql.dll", image));
                    break;
                case "nested":
                    entries.Add(("lib/net11.0/x64/Npgsql.dll", image));
                    break;
                case "tools":
                    entries.Add(("tools/net11.0/any/Npgsql.dll", image));
                    break;
                case "native-image":
                    byte[] native = image.ToArray();
                    using (var pe = new PEReader(new MemoryStream(native)))
                    {
                        int directoriesOffset = pe.PEHeaders.PEHeader!.Magic == PEMagic.PE32Plus
                            ? 112 : 96;
                        Array.Clear(native,
                            pe.PEHeaders.PEHeaderStartOffset + directoriesOffset + 14 * 8, 8);
                    }
                    entries.Add(("lib/net11.0/Native.dll", native));
                    break;
                case "invalid-image":
                    entries.Add(("lib/net11.0/Invalid.dll", [1, 2, 3]));
                    break;
            }
            if (shape != "no-nuspec")
            {
                string id = shape == "invalid-nuspec-id" ? "Not A Package" : "Selected.Sample";
                string version = shape == "invalid-nuspec-version" ? "not-a-version" : "1.0.0";
                entries.Add(("Selected.Sample.nuspec", System.Text.Encoding.UTF8.GetBytes(
                    $"""
                    <package><metadata><id>{id}</id><version>{version}</version>
                    <authors>tests</authors><description>inspection selection</description>
                    </metadata></package>
                    """)));
            }
            string archive = Path.Combine(directory, "not-a-package-coordinate.nupkg");
            File.WriteAllBytes(archive, Archive([.. entries]));
            string[] arguments =
            [
                "package", archive, "--all-libraries",
                "--tfm", shape == "all-frameworks" ? "all" : "net11.0",
                "-S", "Integration: Opportunities", "--markdown",
                "--offline", "--no-nuget-cache", "--verbose", "--tips", "q",
            ];
            var start = new ProcessStartInfo(
                Path.Combine(AppContext.BaseDirectory,
                    OperatingSystem.IsWindows() ? "dotnet-inspect.exe" : "dotnet-inspect"))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory,
            };
            start.Environment["XDG_CACHE_HOME"] = Path.Combine(directory, "cache");
            foreach (string argument in arguments)
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the package command.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                OutOfProcessCliProcess.KillAndWaitForExit(process, TimeSpan.FromSeconds(10));
                throw;
            }
            int exit = process.ExitCode;
            string output = await stdout;
            string error = await stderr;

            Assert.True(exit == 0, error);
            Assert.Contains("Using artifact-backed selected-entry package Integrations.", error);
            Assert.Contains("## Integration: Opportunities", output);
            Assert.Equal(expectedLibraries, output.Split(
                "| Aspire | `Npgsql.NpgsqlConnection` |", StringSplitOptions.None).Length - 1);
            Assert.Contains("Health Checks", output);
            foreach ((string path, _) in entries.Where(entry => entry.Path.EndsWith("Npgsql.dll")))
                Assert.Contains(path, output);
            if (shape is "native-image" or "invalid-image")
            {
                Assert.DoesNotContain("Native.dll", output);
                Assert.DoesNotContain("Invalid.dll", output);
            }
            if (shape == "invalid-image")
            {
                Assert.Contains("Could not read library:", error);
            }
            if (shape == "native-image")
            {
                Assert.DoesNotContain("Could not read library:", error);
            }
            TestContext.Current.TestOutputHelper?.WriteLine($"{shape}: exit {exit}\n{output}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static ValueTask<PackageIntegrationsWorkspace> CreateSelectedWorkspaceAsync(
        IEnumerable<PackageIntegrationAssembly> assemblies,
        string? packageId,
        string? packageVersion,
        long? maxRetainedImageBytes = null,
        bool includeIntegrationOpportunities = false)
    {
        PackageIntegrationAssembly[] selected = [.. assemblies];
        string root = Path.GetDirectoryName(Path.GetFullPath(selected[0].Path))!;
        while (selected.Any(item => Path.GetRelativePath(root, item.Path)
            .StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            root = Path.GetDirectoryName(root)!;
        return PackageIntegrationsWorkspace.CreateSelectedAsync(
            selected,
            root,
            PackageInspectionInput.CreateLocal(
                new FileSystemPackageContent(root, null, false, "tests"),
                packageId,
                packageVersion),
            maxRetainedImageBytes,
            includeIntegrationOpportunities,
            TestContext.Current.CancellationToken);
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
