using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse.Installed.Tests;

public sealed class InstalledReferencePackSourceTests
{
    [Fact]
    public void Discover_ReturnsOnlyExactFamilyAndTargetFrameworkCandidates()
    {
        using var hive = new TestHive();
        hive.CreateReferencePack("Microsoft.NETCore.App.Ref", "11.0.0", "net11.0");
        hive.CreateReferencePack(
            "Microsoft.NETCore.App.Ref",
            "11.0.0-preview.2",
            "net11.0");
        hive.CreateReferencePack("Microsoft.NETCore.App.Ref", "10.0.0", "net11.0");
        hive.CreateReferencePack("Microsoft.NETCore.App.Ref", "11.0.1", "net10.0");
        hive.CreateReferencePack("Microsoft.AspNetCore.App.Ref", "11.0.3", "net11.0");

        InstalledReferencePackSource source = hive.CreateSource();
        InstalledPlatformSourceOutcome<InstalledReferenceTargetInventory>
            outcome = source.Discover(
                new InstalledReferenceDiscoveryRequest(
                    InstalledPlatformFamily.DotNetRuntime,
                    PlatformTargetFramework.Parse("net11.0"),
                    maxCandidates: 8),
                TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledReferenceTargetInventory>.Succeeded>(outcome);
        Assert.Collection(
            succeeded.Value.Targets,
            target => Assert.Equal(
                "11.0.0-preview.2",
                target.Coordinate.Version.Value),
            target => Assert.Equal(
                "11.0.0",
                target.Coordinate.Version.Value));
        Assert.All(
            succeeded.Value.Targets,
            target =>
            {
                Assert.Same(hive.Identity, target.Coordinate.Hive);
                Assert.Equal(
                    InstalledPlatformFamily.DotNetRuntime,
                    target.Coordinate.Family);
                Assert.Equal(
                    PlatformTargetFramework.Parse("net11.0"),
                    target.Coordinate.TargetFramework);
            });
    }

    [Fact]
    public void Discover_ReturnsIncompleteWhenCandidateBudgetIsExceeded()
    {
        using var hive = new TestHive();
        hive.CreateReferencePack("Microsoft.NETCore.App.Ref", "11.0.0", "net11.0");
        hive.CreateReferencePack("Microsoft.NETCore.App.Ref", "11.0.1", "net11.0");

        InstalledPlatformSourceOutcome<InstalledReferenceTargetInventory>
            outcome = hive.CreateSource().Discover(
                new InstalledReferenceDiscoveryRequest(
                    InstalledPlatformFamily.DotNetRuntime,
                    PlatformTargetFramework.Parse("net11.0"),
                    maxCandidates: 1),
                TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledReferenceTargetInventory>.Incomplete>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.WorkLimitExceeded,
            incomplete.Diagnostic.Kind);
    }

    [Fact]
    public void Discover_ReturnsIncompleteWhenObservationLimitIsExceeded()
    {
        using var hive = new TestHive();
        hive.CreateReferencePack("Microsoft.NETCore.App.Ref", "11.0.0", "net11.0");
        hive.CreateReferencePack("Microsoft.NETCore.App.Ref", "11.0.1", "net11.0");

        InstalledPlatformSourceOutcome<InstalledReferenceTargetInventory>
            outcome = hive.CreateSource(maxObservedEntries: 1).Discover(
                new InstalledReferenceDiscoveryRequest(
                    InstalledPlatformFamily.DotNetRuntime,
                    PlatformTargetFramework.Parse("net11.0"),
                    maxCandidates: 8),
                TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledReferenceTargetInventory>.Incomplete>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.WorkLimitExceeded,
            incomplete.Diagnostic.Kind);
    }

    [Fact]
    public void Discover_CountsEntriesExcludedFromCandidateSelection()
    {
        using var hive = new TestHive();
        string packDirectory = Path.Combine(
            hive.Root,
            "packs",
            "Microsoft.NETCore.App.Ref");
        Directory.CreateDirectory(packDirectory);
        for (int index = 0; index < 8; index++)
        {
            File.WriteAllText(
                Path.Combine(packDirectory, $"ignored-{index}.txt"),
                "ignored");
        }
        Directory.CreateDirectory(
            Path.Combine(packDirectory, "11.0.0", "ref", "net11.0"));

        InstalledPlatformSourceOutcome<InstalledReferenceTargetInventory>
            outcome = hive.CreateSource(maxObservedEntries: 6).Discover(
                new InstalledReferenceDiscoveryRequest(
                    InstalledPlatformFamily.DotNetRuntime,
                    PlatformTargetFramework.Parse("net11.0"),
                    maxCandidates: 8),
                TestContext.Current.CancellationToken);

        Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledReferenceTargetInventory>.Incomplete>(outcome);
    }

    [Fact]
    public async Task RealizeCompletePopulation_SnapshotsValidatedAssemblies()
    {
        using var hive = new TestHive();
        string directory = hive.CreateReferencePack(
            "Microsoft.NETCore.App.Ref",
            "11.0.0",
            "net11.0");
        string first = hive.CopyAssembly(
            directory,
            typeof(InstalledReferencePackSourceTests).Assembly.Location);
        string second = hive.CopyAssembly(
            directory,
            typeof(PlatformFamilyTarget).Assembly.Location);

        InstalledReferencePackSource source = hive.CreateSource();
        InstalledPlatformSourceOutcome<InstalledReferenceRealization>
            outcome = await source.RealizeAsync(
                new InstalledReferenceRealizationRequest(
                    hive.Coordinate(),
                    new InstalledReferencePopulationDemand
                        .CompletePopulation(),
                    new InstalledReferenceWorkBudget(
                        maxAssemblies: 8,
                        maxBytes: 32 * 1024 * 1024)),
                TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization>.Succeeded>(outcome);
        Assert.Equal(2, succeeded.Value.Libraries.Count);
        File.Delete(first);
        File.Delete(second);
        Assert.All(
            succeeded.Value.Libraries,
            library =>
            {
                using Stream content = library.OpenRead();
                Assert.True(content.Length > 0);
            });
    }

    [Fact]
    public async Task RealizeCompletePopulation_RejectsMalformedMemberAtomically()
    {
        using var hive = new TestHive();
        string directory = hive.CreateReferencePack(
            "Microsoft.NETCore.App.Ref",
            "11.0.0",
            "net11.0");
        hive.CopyAssembly(
            directory,
            typeof(InstalledReferencePackSourceTests).Assembly.Location);
        File.WriteAllText(Path.Combine(directory, "Broken.dll"), "not a PE");

        InstalledPlatformSourceOutcome<InstalledReferenceRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledReferenceRealizationRequest(
                    hive.Coordinate(),
                    new InstalledReferencePopulationDemand
                        .CompletePopulation(),
                    new InstalledReferenceWorkBudget(
                        maxAssemblies: 8,
                        maxBytes: 32 * 1024 * 1024)),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.MalformedAssembly,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task RealizeAssembly_UsesExactAssemblyIdentityAndFileProjection()
    {
        using var hive = new TestHive();
        string directory = hive.CreateReferencePack(
            "Microsoft.NETCore.App.Ref",
            "11.0.0",
            "net11.0");
        string assemblyPath = hive.CopyAssembly(
            directory,
            typeof(InstalledReferencePackSourceTests).Assembly.Location);
        AssemblyReferenceIdentity identity = ReadIdentity(assemblyPath);

        InstalledPlatformSourceOutcome<InstalledReferenceRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledReferenceRealizationRequest(
                    hive.Coordinate(),
                    new InstalledReferencePopulationDemand.Assembly(identity),
                    new InstalledReferenceWorkBudget(
                        maxAssemblies: 1,
                        maxBytes: 32 * 1024 * 1024)),
                TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization>.Succeeded>(outcome);
        InstalledReferenceLibrary library =
            Assert.Single(succeeded.Value.Libraries);
        Assert.True(identity.IsEquivalentTo(library.Identity));
    }

    [Fact]
    public async Task Realize_RejectsCaseVariantVersionIdentity()
    {
        using var hive = new TestHive();
        string directory = hive.CreateReferencePack(
            "Microsoft.NETCore.App.Ref",
            "11.0.0-preview.7",
            "net11.0");
        hive.CopyAssembly(
            directory,
            typeof(InstalledReferencePackSourceTests).Assembly.Location);
        var coordinate = new InstalledReferencePackCoordinate(
            hive.Identity,
            InstalledPlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0-PREVIEW.7"));

        InstalledPlatformSourceOutcome<InstalledReferenceRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledReferenceRealizationRequest(
                    coordinate,
                    new InstalledReferencePopulationDemand
                        .CompletePopulation(),
                    new InstalledReferenceWorkBudget(
                        maxAssemblies: 8,
                        maxBytes: 32 * 1024 * 1024)),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization>.Rejected>(outcome);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidLayout,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public async Task Realize_CountsDirectoriesExcludedFromPopulation()
    {
        using var hive = new TestHive();
        string directory = hive.CreateReferencePack(
            "Microsoft.NETCore.App.Ref",
            "11.0.0",
            "net11.0");
        Directory.CreateDirectory(Path.Combine(directory, "one"));
        Directory.CreateDirectory(Path.Combine(directory, "two"));

        InstalledPlatformSourceOutcome<InstalledReferenceRealization>
            outcome = await hive.CreateSource(
                    maxObservedEntries: 6)
                .RealizeAsync(
                    new InstalledReferenceRealizationRequest(
                        hive.Coordinate(),
                        new InstalledReferencePopulationDemand
                            .CompletePopulation(),
                        new InstalledReferenceWorkBudget(
                            maxAssemblies: 8,
                            maxBytes: 32 * 1024 * 1024)),
                    TestContext.Current.CancellationToken);

        Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization>.Incomplete>(outcome);
    }

    [Fact]
    public async Task Realize_ObservesCancellationBeforeAbsentOrEmptyOutcome()
    {
        using var absentHive = new TestHive();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await absentHive.CreateSource().RealizeAsync(
                new InstalledReferenceRealizationRequest(
                    absentHive.Coordinate(),
                    new InstalledReferencePopulationDemand
                        .CompletePopulation(),
                    new InstalledReferenceWorkBudget(
                        maxAssemblies: 8,
                        maxBytes: 32 * 1024 * 1024)),
                cancellation.Token));

        using var emptyHive = new TestHive();
        emptyHive.CreateReferencePack(
            "Microsoft.NETCore.App.Ref",
            "11.0.0",
            "net11.0");
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await emptyHive.CreateSource().RealizeAsync(
                new InstalledReferenceRealizationRequest(
                    emptyHive.Coordinate(),
                    new InstalledReferencePopulationDemand
                        .CompletePopulation(),
                    new InstalledReferenceWorkBudget(
                        maxAssemblies: 8,
                        maxBytes: 32 * 1024 * 1024)),
                cancellation.Token));
    }

    [Fact]
    public void Discover_RejectsFileUsedAsHiveOrLayoutDirectory()
    {
        string fileRoot = Path.GetTempFileName();
        try
        {
            var source = new InstalledReferencePackSource(
                InstalledDotnetHiveIdentity.Create("file-root"),
                fileRoot);
            InstalledPlatformSourceOutcome<InstalledReferenceTargetInventory>
                fileRootOutcome = source.Discover(
                    new InstalledReferenceDiscoveryRequest(
                        InstalledPlatformFamily.DotNetRuntime,
                        PlatformTargetFramework.Parse("net11.0"),
                        maxCandidates: 8),
                    TestContext.Current.CancellationToken);
            Assert.IsType<
                InstalledPlatformSourceOutcome<
                    InstalledReferenceTargetInventory>.Rejected>(
                        fileRootOutcome);
        }
        finally
        {
            File.Delete(fileRoot);
        }

        using var hive = new TestHive();
        File.WriteAllText(Path.Combine(hive.Root, "packs"), "not a directory");
        InstalledPlatformSourceOutcome<InstalledReferenceTargetInventory>
            intermediateOutcome = hive.CreateSource().Discover(
                new InstalledReferenceDiscoveryRequest(
                    InstalledPlatformFamily.DotNetRuntime,
                    PlatformTargetFramework.Parse("net11.0"),
                    maxCandidates: 8),
                TestContext.Current.CancellationToken);
        Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledReferenceTargetInventory>.Rejected>(
                    intermediateOutcome);
    }

    [Fact]
    public async Task RealizeCompletePopulation_ReturnsIncompleteBeforeShortening()
    {
        using var hive = new TestHive();
        string directory = hive.CreateReferencePack(
            "Microsoft.NETCore.App.Ref",
            "11.0.0",
            "net11.0");
        hive.CopyAssembly(
            directory,
            typeof(InstalledReferencePackSourceTests).Assembly.Location);
        hive.CopyAssembly(
            directory,
            typeof(PlatformFamilyTarget).Assembly.Location);

        InstalledPlatformSourceOutcome<InstalledReferenceRealization>
            outcome = await hive.CreateSource().RealizeAsync(
                new InstalledReferenceRealizationRequest(
                    hive.Coordinate(),
                    new InstalledReferencePopulationDemand
                        .CompletePopulation(),
                    new InstalledReferenceWorkBudget(
                        maxAssemblies: 1,
                        maxBytes: 32 * 1024 * 1024)),
                TestContext.Current.CancellationToken);

        Assert.IsType<
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization>.Incomplete>(outcome);
    }

    private static AssemblyReferenceIdentity ReadIdentity(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            peReader.GetMetadataReader());
    }

    private sealed class TestHive : IDisposable
    {
        internal TestHive()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-inspect-installed-reference-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Identity = InstalledDotnetHiveIdentity.Create(
                "test-hive-" + Guid.NewGuid().ToString("N"));
        }

        internal string Root { get; }
        internal InstalledDotnetHiveIdentity Identity { get; }

        internal InstalledReferencePackSource CreateSource(
            int maxObservedEntries =
                InstalledReferencePackSource.DefaultMaxObservedEntries) =>
            new(Identity, Root, maxObservedEntries);

        internal InstalledReferencePackCoordinate Coordinate() =>
            new(
                Identity,
                InstalledPlatformFamily.DotNetRuntime,
                PlatformTargetFramework.Parse("net11.0"),
                PlatformVersion.Parse("11.0.0"));

        internal string CreateReferencePack(
            string pack,
            string version,
            string targetFramework)
        {
            string directory = Path.Combine(
                Root,
                "packs",
                pack,
                version,
                "ref",
                targetFramework);
            Directory.CreateDirectory(directory);
            return directory;
        }

        internal string CopyAssembly(
            string directory,
            string sourcePath)
        {
            string path = Path.Combine(
                directory,
                Path.GetFileName(sourcePath));
            File.Copy(sourcePath, path);
            return path;
        }

        public void Dispose() =>
            Directory.Delete(Root, recursive: true);
    }
}
