using System.IO.Compression;
using System.Text.Json;

using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackagePairDirectUseClusterQueryTests
{
    const string Framework = "net11.0";

    [Fact]
    public async Task ExecuteBuildsCanonicalCompleteLibraryPairMatrix()
    {
        PackageRootBinding packageA = Binding(
            "Package.A",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath());
        PackageRootBinding packageB = Binding(
            "Package.B",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            FixtureCatalog.AnalysisSpoofSystemRuntime.AssemblyPath());
        await using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [packageA, packageB]);
        PackageAssemblyContextProjection projection =
            completion.CreateProjection([packageA, packageB]);

        var available = Assert.IsType<
            PackagePairDirectUseClusterOutcome.Available>(
                PackagePairDirectUseClusterQuery.Execute(
                    projection,
                    packageA.Root.Identity,
                    packageA.Coordinate,
                    packageB.Root.Identity,
                    packageB.Coordinate));
        PackagePairDirectUseClusterDocument document =
            available.Document;

        Assert.Equal("package.a", document.FirstPackage.PackageId);
        Assert.Equal("package.b", document.SecondPackage.PackageId);
        Assert.Equal(
            packageA.Coordinate,
            document.FirstPackage.Coordinate);
        Assert.Equal(
            packageB.Coordinate,
            document.SecondPackage.Coordinate);
        Assert.Equal(Framework, document.TargetFramework);
        Assert.Equal(4, document.Libraries.Length);
        Assert.Equal(4, document.LibraryPairs.Length);
        Assert.All(
            document.LibraryPairs,
            pair =>
            {
                Assert.NotEqual(
                    pair.First.Package.PackageId,
                    pair.Second.Package.PackageId);
                Assert.True(pair.IsComplete);
            });
        PackagePairDirectCall direct = Assert.Single(
            document.LibraryPairs.SelectMany(static pair => pair.Occurrences),
            call =>
                call.Source.AssemblyIdentity.Name
                    == "ILInspector.Analysis.CallerGraphCaller"
                && call.SourceMethod.Name == "Run"
                && call.Target.AssemblyIdentity.Name
                    == "ILInspector.Analysis.CallerGraphTarget"
                && call.TargetMethod.Name == "Ping");
        Assert.NotNull(direct.Source.ModuleVersionId);
        Assert.NotNull(direct.Target.ModuleVersionId);
        Assert.Equal(
            Enumerable.Range(1, document.Clusters.Length),
            document.Clusters.Select(static cluster => cluster.Ordinal));
        Assert.Equal(
            document.Clusters.Length,
            PackagePairDirectUseClusterRows.Clusters(document).Length);
        Assert.Equal(
            document.LibraryPairs.Length,
            PackagePairDirectUseClusterRows.LibraryPairs(document).Length);
        Assert.Equal(
            document.LibraryPairs.Sum(
                static pair => pair.Occurrences.Length),
            PackagePairDirectUseClusterRows.CallSites(document).Length);
        Assert.All(
            PackagePairDirectUseClusterRows.CallSites(document),
            static row => Assert.True(row.Cluster > 0));

        var reversed = Assert.IsType<
            PackagePairDirectUseClusterOutcome.Available>(
                PackagePairDirectUseClusterQuery.Execute(
                    projection,
                    packageB.Root.Identity,
                    packageB.Coordinate,
                    packageA.Root.Identity,
                    packageA.Coordinate));
        Assert.Equal(
            DocumentIdentity(document),
            DocumentIdentity(reversed.Document));

        await projection.ReturnAsync();
        await completion.CloseAsync();

        Assert.Equal(4, document.Libraries.Length);
        Assert.Contains(
            document.Clusters,
            cluster =>
                cluster.Identity.Source.AssemblyIdentity.Name
                    == "ILInspector.Analysis.CallerGraphCaller"
                && cluster.Identity.Target.AssemblyIdentity.Name
                    == "ILInspector.Analysis.CallerGraphTarget");
    }

    [Fact]
    public async Task ExecutePreservesSharedRequestAcrossCompatibleSelections()
    {
        PackageRootBinding packageA = CompatibleBinding(
            "Package.A",
            "net8.0",
            ("caller.dll",
                FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath()));
        PackageRootBinding packageB = CompatibleBinding(
            "Package.B",
            "netstandard2.0",
            ("target.dll",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()));
        await using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [packageA, packageB]);
        PackageAssemblyContextProjection projection =
            completion.CreateProjection([packageA, packageB]);
        try
        {
            var available = Assert.IsType<
                PackagePairDirectUseClusterOutcome.Available>(
                    PackagePairDirectUseClusterQuery.Execute(
                        projection,
                        packageA.Root.Identity,
                        packageA.Coordinate,
                        packageB.Root.Identity,
                        packageB.Coordinate));

            Assert.Equal(Framework, available.Document.TargetFramework);
            Assert.Equal(
                Framework,
                available.Document.FirstPackage.RequestedTargetFramework);
            Assert.Equal(
                "net8.0",
                available.Document.FirstPackage.SelectedTargetFramework);
            Assert.Equal(
                Framework,
                available.Document.SecondPackage.RequestedTargetFramework);
            Assert.Equal(
                "netstandard2.0",
                available.Document.SecondPackage.SelectedTargetFramework);
            Assert.Contains(
                Assert.Single(
                    available.Document.LibraryPairs).Occurrences,
                static occurrence =>
                    occurrence.SourceMethod.Name == "Run"
                    && occurrence.TargetMethod.Name == "Ping");
        }
        finally
        {
            await projection.ReturnAsync();
            await completion.CloseAsync();
        }
    }

    [Fact]
    public async Task ExecuteRetainsPositiveCallsWhenPairIsIncomplete()
    {
        PackageRootBinding packageA = Binding(
            "Package.A",
            FixtureCatalog.AnalysisCallerGraphVersionSkewCaller
                .AssemblyPath());
        PackageRootBinding packageB = Binding(
            "Package.B",
            FixtureCatalog.AnalysisCallerGraphVersionSkewTargetV2
                .AssemblyPath());
        await using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [packageA, packageB]);
        PackageAssemblyContextProjection projection =
            completion.CreateProjection([packageA, packageB]);
        try
        {
            var available = Assert.IsType<
                PackagePairDirectUseClusterOutcome.Available>(
                    PackagePairDirectUseClusterQuery.Execute(
                        projection,
                        packageA.Root.Identity,
                        packageA.Coordinate,
                        packageB.Root.Identity,
                        packageB.Coordinate));
            PackagePairDirectUseClusterDocument document =
                available.Document;

            Assert.False(document.IsComplete);
            PackagePairLibraryPair incomplete =
                Assert.Single(document.LibraryPairs);
            Assert.False(incomplete.IsComplete);
            Assert.True(
                incomplete.Diagnostics.UnresolvedCandidateCallCount > 0);
            Assert.NotEmpty(incomplete.Occurrences);
            Assert.NotEmpty(incomplete.Clusters);
        }
        finally
        {
            await projection.ReturnAsync();
            await completion.CloseAsync();
        }
    }

    [Fact]
    public async Task ExecuteRejectsWholePairPopulationBeforeAnalysis()
    {
        PackageRootBinding packageA = Binding(
            "Package.A",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath());
        PackageRootBinding packageB = Binding(
            "Package.B",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            FixtureCatalog.AnalysisSpoofSystemRuntime.AssemblyPath());
        await using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [packageA, packageB]);
        PackageAssemblyContextProjection projection =
            completion.CreateProjection([packageA, packageB]);

        var rejected = Assert.IsType<
            PackagePairDirectUseClusterOutcome.PairPopulationRejected>(
                PackagePairDirectUseClusterQuery.Execute(
                    projection,
                    packageA.Root.Identity,
                    packageA.Coordinate,
                    packageB.Root.Identity,
                    packageB.Coordinate,
                    new(maximumLibraryPairs: 3)));

        Assert.Equal(2, rejected.FirstLibraryCount);
        Assert.Equal(2, rejected.SecondLibraryCount);
        Assert.Equal(4, rejected.RequiredLibraryPairs);
        Assert.Equal(3, rejected.MaximumLibraryPairs);

        await projection.ReturnAsync();
        await completion.CloseAsync();
    }

    [Fact]
    public async Task OperationPublishesDetachedContentAndClosesContext()
    {
        PackageRootBinding packageA = Binding(
            "Package.A",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        PackageRootBinding packageB = Binding(
            "Package.B",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot scope =
            await Replace(workspace, packageA, packageB);

        var completed = Assert.IsType<
            PackagePairDirectUseClusterOperationOutcome.Completed>(
                await PackagePairDirectUseClusterOperation.ExecuteAsync(
                    new(
                        workspace,
                        scope,
                        [packageA, packageB]),
                    TestContext.Current.CancellationToken));
        var available = Assert.IsType<
            PackagePairDirectUseClusterOutcome.Available>(
                completed.Content);

        Assert.Same(scope.Revision.Identity, completed.ScopeRevision);
        Assert.True(available.Document.IsComplete);
        Assert.Single(available.Document.LibraryPairs);
        Assert.Contains(
            available.Document.Clusters,
            cluster =>
                cluster.Identity.Source.AssemblyIdentity.Name
                    == "ILInspector.Analysis.CallerGraphCaller"
                && cluster.Identity.Target.AssemblyIdentity.Name
                    == "ILInspector.Analysis.CallerGraphTarget");
    }

    [Fact]
    public async Task OperationReturnsTypedOutcomeWhenScopeLacksRoots()
    {
        PackageRootBinding packageA = Binding(
            "Package.A",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        PackageRootBinding packageB = Binding(
            "Package.B",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot scope =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync())
            .Snapshot;

        Assert.IsType<
            PackagePairDirectUseClusterOperationOutcome
                .WorkspaceNotCommitted>(
                    await PackagePairDirectUseClusterOperation.ExecuteAsync(
                        new(
                            workspace,
                            scope,
                            [packageA, packageB]),
                        TestContext.Current.CancellationToken));
    }

    static async Task<PackageAssemblyContextCompletion> ExecuteAsync(
        InspectionWorkspace workspace,
        IEnumerable<PackageRootBinding> bindings)
    {
        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion(bindings);
        return await operation.ExecuteAsync(operation.Identity);
    }

    static async Task<WorkspaceScopeSnapshot> Replace(
        InspectionWorkspace workspace,
        params PackageRootBinding[] bindings)
    {
        WorkspaceScopeSnapshot current =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync())
            .Snapshot;
        return Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.ReplaceScopeAsync(
                current.Revision,
                [.. bindings],
                DateTimeOffset.UtcNow.AddMinutes(1),
                TestContext.Current.CancellationToken))
            .Snapshot;
    }

    static string DocumentIdentity(
        PackagePairDirectUseClusterDocument document) =>
        JsonSerializer.Serialize(new
        {
            document.FirstPackage,
            document.SecondPackage,
            document.TargetFramework,
            Libraries = document.Libraries.Select(library => new
            {
                library.Package.PackageId,
                library.Asset.Path,
                library.AssemblyIdentity,
                library.ModuleVersionId,
            }).ToArray(),
            Pairs = document.LibraryPairs.Select(pair => new
            {
                pair.Ordinal,
                First = pair.First.Asset.Path,
                Second = pair.Second.Asset.Path,
                pair.IsComplete,
                Calls = pair.Occurrences.Select(call => new
                {
                    Source = call.Source.Asset.Path,
                    call.SourceMethod.MetadataToken,
                    Target = call.Target.Asset.Path,
                    TargetToken = call.TargetMethod.MetadataToken,
                    call.Call.ILOffset,
                    call.Call.OperandToken,
                }).ToArray(),
                Clusters = pair.Clusters.Select(cluster => new
                {
                    cluster.Ordinal,
                    cluster.LibraryPairOrdinal,
                    cluster.LibraryPairClusterOrdinal,
                    Source = cluster.Identity.Source.Asset.Path,
                    cluster.Identity.AnchorSourceMethodToken,
                    Target = cluster.Identity.Target.Asset.Path,
                    cluster.Identity.AnchorTargetMethodToken,
                    cluster.OccurrenceIndexes,
                }).ToArray(),
            }).ToArray(),
        });

    static PackageRootBinding Binding(
        string packageId,
        params string[] assemblyPaths)
        => BindingWithAssets(
            packageId,
            Framework,
            [.. assemblyPaths.Select(path =>
                (Path.GetFileName(path), path))]);

    static PackageRootBinding CompatibleBinding(
        string packageId,
        string selectedFramework,
        params (string AssetName, string AssemblyPath)[] assemblies)
    {
        PackageRootBinding binding = BindingWithAssets(
            packageId,
            selectedFramework,
            true,
            assemblies);
        Assert.Equal(Framework, binding.Coordinate.Framework);
        Assert.Equal(
            selectedFramework,
            binding.Root.RequestedTargetFramework);
        Assert.True(binding.UsesCompatibleImplementationSelection);
        return binding;
    }

    static PackageRootBinding BindingWithAssets(
        string packageId,
        string selectedFramework,
        params (string AssetName, string AssemblyPath)[] assemblies) =>
        BindingWithAssets(
            packageId,
            selectedFramework,
            false,
            assemblies);

    static PackageRootBinding BindingWithAssets(
        string packageId,
        string selectedFramework,
        bool compatibleSelection,
        (string AssetName, string AssemblyPath)[] assemblies)
    {
        var entries = assemblies.Select(assembly => (
            Path: $"lib/{selectedFramework}/{assembly.AssetName}",
            Content: File.ReadAllBytes(assembly.AssemblyPath)));
        var content = new InMemoryPackageContent(
            Archive(entries),
            fromCache: false,
            producerKey: "tests");
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, "1.0.0"),
            content,
            "tests",
            PackagePayloadOrigin.Download);
        PackageRootBinding binding =
            compatibleSelection
                ? PackageRootBinding
                    .CreateFromSourceWithCompatibleSelection(
                        payload,
                        Framework)
                : PackageRootBinding.CreateFromSource(
                    payload,
                    Framework);
        Assert.True(binding.Root.AssetSelection.IsSelected);
        return binding;
    }

    static byte[] Archive(
        IEnumerable<(string Path, byte[] Content)> entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream destination =
                    archive.CreateEntry(
                        path,
                        CompressionLevel.NoCompression)
                    .Open();
                destination.Write(content);
            }
        }
        return buffer.ToArray();
    }
}
