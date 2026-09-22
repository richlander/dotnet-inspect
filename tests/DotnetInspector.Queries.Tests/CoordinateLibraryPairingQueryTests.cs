using System.IO.Compression;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Queries.EmbeddedFixtures;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class CoordinateLibraryPairingQueryTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task VersionAndPathChanges_PairFreshDestinationApiRegistration()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding before = Binding("1.0.0",
            ("ref/net11.0/Original.dll", FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()));
        PackageRootBinding after = Binding("2.0.0",
            ("lib/net11.0/Renamed.dll", FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath()));
        WorkspaceScopeSnapshot scope = await Replace(workspace, before, after);
        CoordinatePackageObservation first = await Observe(workspace, before, scope);
        CoordinatePackageObservation second = await Observe(workspace, after, scope);

        CoordinateApiLibraryObservation original = Assert.Single(first.Libraries);
        CoordinateLibraryPairingResult result =
            CoordinateLibraryPairingQuery.Execute(original.Subject, first, second);

        Assert.Equal(CoordinateLibraryPairingStatus.Exact, result.Status);
        CoordinateApiLibraryObservation destination = Assert.IsType<CoordinateApiLibraryObservation>(result.Destination);
        Assert.NotEqual(original.Assembly.Version, destination.Assembly.Version);
        Assert.Equal("lib/net11.0/Renamed.dll", destination.Asset.Path);
        Assert.Equal(PackageCompileAssetKind.Library, destination.Asset.Kind);
        Assert.NotSame(original.Subject.Identity.Registration, destination.Subject.Identity.Registration);
        Assert.Same(second.Occurrence.Identity, destination.Subject.Package.Occurrence.Identity);
        Assert.Same(second.Generation, result.After.Generation);
    }

    [Fact]
    public async Task TwoAssemblyVersionsInDestination_AreAmbiguousNotFirstMatch()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding before = Binding("1.0.0",
            ("ref/net11.0/One.dll", FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()));
        PackageRootBinding after = Binding("2.0.0",
            ("ref/net11.0/One.dll", FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()),
            ("ref/net11.0/Two.dll", FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath()));
        WorkspaceScopeSnapshot scope = await Replace(workspace, before, after);
        CoordinatePackageObservation first = await Observe(workspace, before, scope);
        CoordinatePackageObservation second = await Observe(workspace, after, scope);

        CoordinateLibraryPairingResult result =
            CoordinateLibraryPairingQuery.Execute(first.Libraries[0].Subject, first, second);

        Assert.Equal(CoordinateLibraryPairingStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.Candidates.Length);
        Assert.Null(result.Destination);
    }

    [Theory]
    [InlineData("ref/net11.0/_._")]
    [InlineData("tools/net11.0/Hidden.dll")]
    public async Task EmptySelectedApiPopulation_ProvesAbsence(string destinationPath)
    {
        await using var workspace = new InspectionWorkspace();
        string image = typeof(EmbeddedSourceFixture).Assembly.Location;
        PackageRootBinding before = Binding("1.0.0", ("lib/net11.0/Fixture.dll", image));
        PackageRootBinding after = Binding("2.0.0", (destinationPath, image));
        WorkspaceScopeSnapshot scope = await Replace(workspace, before, after);
        CoordinatePackageObservation first = await Observe(workspace, before, scope);
        CoordinatePackageObservation second = await Observe(workspace, after, scope);

        CoordinateLibraryPairingResult result =
            CoordinateLibraryPairingQuery.Execute(first.Libraries[0].Subject, first, second);

        Assert.Empty(second.Libraries);
        Assert.Equal(CoordinateLibraryPairingStatus.Absent, result.Status);
        Assert.Null(result.Destination);
    }

    [Fact]
    public async Task ImplementationOnlyMatch_IsNotADestinationApiCandidate()
    {
        await using var workspace = new InspectionWorkspace();
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        PackageRootBinding before = Binding("1.0.0", ("ref/net11.0/Target.dll", target));
        PackageRootBinding after = Binding("2.0.0",
            ("ref/net11.0/Other.dll", typeof(EmbeddedSourceFixture).Assembly.Location),
            ("lib/net11.0/Target.dll", target),
            ("ref/net10.0/Target.dll", target));
        WorkspaceScopeSnapshot scope = await Replace(workspace, before, after);
        CoordinatePackageObservation first = await Observe(workspace, before, scope);
        CoordinatePackageObservation second = await Observe(workspace, after, scope);

        Assert.Equal("ref/net11.0/Other.dll", Assert.Single(second.Libraries).Asset.Path);
        Assert.Equal(CoordinateLibraryPairingStatus.Absent,
            CoordinateLibraryPairingQuery.Execute(first.Libraries[0].Subject, first, second).Status);
    }

    [Fact]
    public async Task DistinctWorkspaces_PreserveFreshDestinationOccurrence()
    {
        await using var firstWorkspace = new InspectionWorkspace();
        await using var secondWorkspace = new InspectionWorkspace();
        PackageRootBinding before = Binding("1.0.0",
            ("ref/net11.0/Original.dll",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()));
        PackageRootBinding after = Binding("2.0.0",
            ("lib/net11.0/Renamed.dll",
                FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath()));
        WorkspaceScopeSnapshot firstScope = await Replace(firstWorkspace, before);
        WorkspaceScopeSnapshot secondScope = await Replace(secondWorkspace, after);
        CoordinatePackageObservation first =
            await Observe(firstWorkspace, before, firstScope);
        CoordinatePackageObservation second =
            await Observe(secondWorkspace, after, secondScope);

        CoordinateLibraryPairingResult result =
            CoordinateLibraryPairingQuery.Execute(first.Libraries[0].Subject, first, second);

        Assert.Equal(CoordinateLibraryPairingStatus.Exact, result.Status);
        CoordinateApiLibraryObservation destination =
            Assert.IsType<CoordinateApiLibraryObservation>(result.Destination);
        Assert.NotSame(
            first.Occurrence.Identity.WorkspaceIdentity,
            destination.Subject.Workspace.Identity);
        Assert.Same(
            second.Occurrence.Identity,
            destination.Subject.Package.Occurrence.Identity);
        Assert.NotSame(
            first.Libraries[0].Subject.Identity.Registration,
            destination.Subject.Identity.Registration);

        CoordinateLibraryPairingEvidence evidence = result.Detach();
        await firstWorkspace.CloseAsync();
        await secondWorkspace.CloseAsync();

        Assert.Equal(CoordinateLibraryPairingStatus.Exact, evidence.Status);
        Assert.Equal("1.0.0", evidence.Before.PackageVersion);
        Assert.Equal("2.0.0", evidence.After.PackageVersion);
    }

    [Fact]
    public async Task SourceLibraryFromDestinationWorkspace_IsRefused()
    {
        await using var firstWorkspace = new InspectionWorkspace();
        await using var secondWorkspace = new InspectionWorkspace();
        PackageRootBinding binding = Binding("1.0.0",
            ("lib/net11.0/Fixture.dll", typeof(EmbeddedSourceFixture).Assembly.Location));
        WorkspaceScopeSnapshot firstScope = await Replace(firstWorkspace, binding);
        WorkspaceScopeSnapshot secondScope = await Replace(secondWorkspace, binding);
        CoordinatePackageObservation first = await Observe(firstWorkspace, binding, firstScope);
        CoordinatePackageObservation second = await Observe(secondWorkspace, binding, secondScope);

        CoordinateLibraryPairingResult result =
            CoordinateLibraryPairingQuery.Execute(second.Libraries[0].Subject, first, second);

        Assert.Equal(CoordinateLibraryPairingStatus.Refused, result.Status);
        Assert.Equal(
            CoordinateLibraryPairingFailureKind.ForeignWorkspace,
            result.Failure!.Kind);
    }

    [Fact]
    public async Task InvalidSourceAssociation_DetachesTheSourcePackage()
    {
        await using var workspace = new InspectionWorkspace();
        string image = typeof(EmbeddedSourceFixture).Assembly.Location;
        PackageRootBinding sourceBinding = BindingFrom(
            "source.package", "1.0.0", PackageProducerIdentity.NuGetOrg,
            ("lib/net11.0/Fixture.dll", image));
        PackageRootBinding before = Binding(
            "1.0.0", ("lib/net11.0/Fixture.dll", image));
        PackageRootBinding after = Binding(
            "2.0.0", ("lib/net11.0/Fixture.dll", image));
        WorkspaceScopeSnapshot scope =
            await Replace(workspace, sourceBinding, before, after);
        CoordinatePackageObservation source =
            await Observe(workspace, sourceBinding, scope);
        CoordinatePackageObservation first =
            await Observe(workspace, before, scope);
        CoordinatePackageObservation second =
            await Observe(workspace, after, scope);

        CoordinateLibraryPairingEvidence evidence =
            CoordinateLibraryPairingQuery.Execute(
                Assert.Single(source.Libraries).Subject,
                first,
                second).Detach();
        await workspace.CloseAsync();

        Assert.Equal(CoordinateLibraryPairingStatus.Refused, evidence.Status);
        Assert.Equal(
            CoordinateLibraryPairingFailureKind.InvalidEndpointAssociation,
            evidence.Failure!.Kind);
        Assert.Equal("source.package", evidence.Source.Package.PackageId);
        Assert.Equal("coordinate.sample", evidence.Before.PackageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshBindingForSameLogicalRequest_CannotRelabelRetainedGeneration(
        bool emptyCompileGroup)
    {
        await using var workspace = new InspectionWorkspace();
        string image = typeof(EmbeddedSourceFixture).Assembly.Location;
        string entry = emptyCompileGroup
            ? "ref/net11.0/_._"
            : "lib/net11.0/Fixture.dll";
        PackageRootBinding retained = Binding("1.0.0", (entry, image));
        PackageRootBinding fresh = Binding("1.0.0", (entry, image));
        WorkspaceScopeSnapshot scope = await Replace(workspace, retained);

        CoordinatePackageObservationResult result = await CoordinateLibraryPairingQuery.ObserveAsync(
            workspace, fresh, Assert.Single(scope.Packages), Cancellation);

        Assert.Equal(CoordinateLibraryPairingFailureKind.InvalidEndpointAssociation,
            Assert.IsType<CoordinatePackageObservationResult.Unavailable>(result).Failure.Kind);
    }

    [Fact]
    public async Task LegacyProducerKeyAlone_IsNotCompleteProducerEvidence()
    {
        await using var workspace = new InspectionWorkspace();
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry("lib/net11.0/Fixture.dll").Open();
            entry.Write(File.ReadAllBytes(typeof(EmbeddedSourceFixture).Assembly.Location));
        }
        var content = new InMemoryPackageContent(bytes.ToArray(), false, "legacy");
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create("coordinate.sample", "1.0.0"),
            content, "legacy", PackagePayloadOrigin.Download);
        PackageRootBinding binding = PackageRootBinding.CreateFromSource(payload, "net11.0");
        WorkspaceScopeSnapshot scope = await Replace(workspace, binding);

        CoordinatePackageObservationResult result = await CoordinateLibraryPairingQuery.ObserveAsync(
            workspace, binding, Assert.Single(scope.Packages), Cancellation);

        Assert.Equal(CoordinateLibraryPairingFailureKind.MissingProducer,
            Assert.IsType<CoordinatePackageObservationResult.Unavailable>(result).Failure.Kind);
    }

    [Fact]
    public async Task ObservationAfterRootRemoval_DoesNotReopenRetiredEvidence()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding binding = Binding("1.0.0",
            ("lib/net11.0/Fixture.dll", typeof(EmbeddedSourceFixture).Assembly.Location));
        WorkspaceScopeSnapshot scope = await Replace(workspace, binding);
        Assert.IsType<WorkspaceScopeOperationResult.Committed>(await workspace.ClearScopeAsync(
            scope.Revision, DateTimeOffset.UtcNow.AddMinutes(1), Cancellation));

        CoordinatePackageObservationResult result = await CoordinateLibraryPairingQuery.ObserveAsync(
            workspace, binding, Assert.Single(scope.Packages), Cancellation);

        Assert.Equal(CoordinateLibraryPairingFailureKind.RootUnavailable,
            Assert.IsType<CoordinatePackageObservationResult.Unavailable>(result).Failure.Kind);
    }

    [Theory]
    [InlineData(false, CoordinateLibraryPairingFailureKind.DifferentPackage)]
    [InlineData(true, CoordinateLibraryPairingFailureKind.DifferentProducer)]
    public async Task DifferentPackageOrCompleteProducer_IsRefused(
        bool differentProducer, CoordinateLibraryPairingFailureKind expected)
    {
        using IPackageSourceClient alternate = PackageSourceClientFactory.Create(
            new PackageSource("alternate", "https://packages.example.test/v3/index.json"),
            PackageSourceAssociation.Create(), new HttpClientHandler());
        await using var workspace = new InspectionWorkspace();
        string image = typeof(EmbeddedSourceFixture).Assembly.Location;
        PackageRootBinding before = Binding("1.0.0", ("lib/net11.0/Fixture.dll", image));
        PackageRootBinding after = BindingFrom(
            differentProducer ? "coordinate.sample" : "another.package", "2.0.0",
            differentProducer ? alternate.Source.Producer : PackageProducerIdentity.NuGetOrg,
            ("lib/net11.0/Fixture.dll", image));
        WorkspaceScopeSnapshot scope = await Replace(workspace, before, after);
        CoordinatePackageObservation first = await Observe(workspace, before, scope);
        CoordinatePackageObservation second = await Observe(workspace, after, scope);

        CoordinateLibraryPairingResult result =
            CoordinateLibraryPairingQuery.Execute(first.Libraries[0].Subject, first, second);

        Assert.Equal(CoordinateLibraryPairingStatus.Refused, result.Status);
        Assert.Equal(expected, result.Failure!.Kind);
    }

    [Fact]
    public async Task UnsignedNameMatch_IsNotAWildcardForASignedDestination()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding before = Binding("1.0.0",
            ("lib/net11.0/System.Runtime.dll", FixtureCatalog.AnalysisSpoofSystemRuntime.AssemblyPath()));
        PackageRootBinding after = Binding("2.0.0",
            ("lib/net11.0/System.Runtime.dll",
                Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.dll")));
        WorkspaceScopeSnapshot scope = await Replace(workspace, before, after);
        CoordinatePackageObservation first = await Observe(workspace, before, scope);
        CoordinatePackageObservation second = await Observe(workspace, after, scope);
        Assert.Equal(first.Libraries[0].Assembly.Name, second.Libraries[0].Assembly.Name);
        Assert.True(string.IsNullOrEmpty(first.Libraries[0].Assembly.PublicKeyToken));
        Assert.False(string.IsNullOrEmpty(second.Libraries[0].Assembly.PublicKeyToken));

        Assert.Equal(CoordinateLibraryPairingStatus.Absent,
            CoordinateLibraryPairingQuery.Execute(first.Libraries[0].Subject, first, second).Status);
    }

    static async Task<CoordinatePackageObservation> Observe(
        InspectionWorkspace workspace, PackageRootBinding binding, WorkspaceScopeSnapshot snapshot) =>
        Assert.IsType<CoordinatePackageObservationResult.Available>(
            await CoordinateLibraryPairingQuery.ObserveAsync(
                workspace, binding, snapshot.FindPackageOccurrence(binding)!, Cancellation)).Observation;

    static async Task<WorkspaceScopeSnapshot> Replace(
        InspectionWorkspace workspace, params PackageRootBinding[] bindings)
    {
        WorkspaceScopeSnapshot current = Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;
        return Assert.IsType<WorkspaceScopeOperationResult.Committed>(await workspace.ReplaceScopeAsync(
            current.Revision, [.. bindings], DateTimeOffset.UtcNow.AddMinutes(1), Cancellation)).Snapshot;
    }

    internal static PackageRootBinding Binding(string version, params (string Entry, string Image)[] assets) =>
        BindingFrom("coordinate.sample", version, PackageProducerIdentity.NuGetOrg, assets);

    static PackageRootBinding BindingFrom(
        string packageId, string version, PackageProducerIdentity producer,
        params (string Entry, string Image)[] assets)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string image) in assets)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                if (!path.EndsWith("/_._", StringComparison.Ordinal))
                    entry.Write(File.ReadAllBytes(image));
            }
        }
        var content = new InMemoryPackageContent(bytes.ToArray(), false, producer.Key);
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, version),
            content, producer.Key, producer, PackagePayloadOrigin.Download);
        return PackageRootBinding.CreateFromSource(payload, "net11.0");
    }
}
