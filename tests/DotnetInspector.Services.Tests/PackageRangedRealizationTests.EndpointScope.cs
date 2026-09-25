using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Package endpoint scope contract gates 1 to 3
/// (<c>docs/design/package-endpoint-scope.md#pathological-cases-and-gates</c>)
/// over the real <c>Avalonia</c> 12.1.2 archive, which ships
/// <c>ref/net10.0</c> and <c>lib/net10.0</c>, read by range.
/// </summary>
public sealed partial class PackageRangedRealizationTests
{
    /// <summary>
    /// Gate 1: a <c>Surface</c> endpoint exposes one surface participant per
    /// selected compile asset, with package provenance, and no
    /// implementation role; its ranged read materializes only the surface
    /// folder.
    /// </summary>
    [Fact]
    public async Task EndpointScope_Surface_ExposesSurfaceParticipantsOnly()
    {
        byte[] archive = ReadAvalonia();
        ArchiveOracle oracle = ArchiveOracle.Read(archive);
        var server = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        await using PackageEndpointScope scope = AssertOpened(
            await OpenEndpointAsync(
                environment,
                new InMemoryPackageStore(),
                new PackageEndpointScopeRequest(
                    PackageSourceCoordinate.Create(Avalonia, AvaloniaVersion),
                    "net10.0",
                    PackageAssetDemand.Surface)));

        Assert.Equal(11, scope.SurfaceParticipants.Length);
        Assert.Empty(scope.ImplementationParticipants);
        Assert.All(scope.SurfaceParticipants, participant =>
        {
            Assert.Equal(PackageEndpointRole.Surface, participant.Role);
            Assert.StartsWith(AvaloniaSurface, participant.Asset.Path, StringComparison.Ordinal);
            Assert.Equal(Avalonia, participant.Provenance.PackageId, ignoreCase: true);
            Assert.Equal(AvaloniaVersion, participant.Provenance.PackageVersion);
            Assert.Equal("net10.0", participant.Provenance.TargetFramework);
            Assert.Equal(participant.Asset.Path, participant.Provenance.AssetPath);
        });
        Assert.Equal(
            [.. Enumerable.Range(0, scope.SurfaceParticipants.Length)],
            scope.SurfaceParticipants.Select(static participant => participant.Index));
        var content = Assert.IsType<RangedPackageContent>(scope.Payload.Content);
        Assert.Equal(
            oracle.Folder(AvaloniaSurface).Order(StringComparer.Ordinal),
            content.MaterializedEntries.Order(StringComparer.Ordinal));

        PackageEndpointParticipant controls = scope.SurfaceParticipants.Single(
            static participant => participant.Asset.Path.EndsWith("/Avalonia.Controls.dll", StringComparison.Ordinal));
        ArtifactRootResult<string> name = await scope.UseSurfaceParticipantAsync(
            controls,
            static (_, participant, _) =>
                ValueTask.FromResult(participant.Assembly.Identity.Name),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "Avalonia.Controls",
            Assert.IsType<ArtifactRootResult<string>.Available>(name).Value);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scope.UseImplementationAsync(
                static (_, _, _) => ValueTask.FromResult(0),
                TestContext.Current.CancellationToken));

        await scope.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await scope.UseSurfaceAsync(
                static (_, participants, _) => ValueTask.FromResult(participants.Length),
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Gate 2: a <c>SurfaceAndImplementation</c> endpoint also exposes one
    /// implementation participant per realized implementation asset, each
    /// with its surface correspondence, readable through its group.
    /// </summary>
    [Fact]
    public async Task EndpointScope_SurfaceAndImplementation_CorrespondsImplementationToSurface()
    {
        byte[] archive = ReadAvalonia();
        var server = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        await using PackageEndpointScope scope = AssertOpened(
            await OpenEndpointAsync(
                environment,
                new InMemoryPackageStore(),
                new PackageEndpointScopeRequest(
                    PackageSourceCoordinate.Create(Avalonia, AvaloniaVersion),
                    "net10.0",
                    PackageAssetDemand.SurfaceAndImplementation)));

        Assert.Equal(11, scope.SurfaceParticipants.Length);
        Assert.Equal(11, scope.ImplementationParticipants.Length);
        Assert.All(scope.ImplementationParticipants, implementation =>
        {
            Assert.Equal(PackageEndpointRole.Implementation, implementation.Role);
            Assert.StartsWith(
                AvaloniaImplementation, implementation.Asset.Path, StringComparison.Ordinal);
            PackageEndpointParticipant surface =
                Assert.IsType<PackageEndpointParticipant>(implementation.SurfaceCorrespondence);
            Assert.Contains(surface, scope.SurfaceParticipants);
            Assert.Equal(surface.Asset.AssemblyName, implementation.Asset.AssemblyName);
        });

        PackageEndpointParticipant dialogs = scope.ImplementationParticipants.Single(
            static participant => participant.Asset.Path.EndsWith("/Avalonia.Dialogs.dll", StringComparison.Ordinal));
        ArtifactRootResult<string> name = await scope.UseImplementationParticipantAsync(
            dialogs,
            static (_, participant, _) =>
                ValueTask.FromResult(participant.Assembly.Identity.Name),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "Avalonia.Dialogs",
            Assert.IsType<ArtifactRootResult<string>.Available>(name).Value);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await scope.UseSurfaceParticipantAsync(
                dialogs,
                static (_, _, _) => ValueTask.FromResult(0),
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Gate 3: a coordinate the source does not have, and a framework the
    /// package has no compatible compile assets for, are typed failures that
    /// keep the House evidence, never an empty scope.
    /// </summary>
    [Theory]
    [InlineData("9.9.9", "net10.0", PackageEndpointScopeFailureKind.NotFound)]
    [InlineData(AvaloniaVersion, "net6.0", PackageEndpointScopeFailureKind.NoCompatibleFramework)]
    public async Task EndpointScope_MissingCoordinateOrFramework_IsTypedFailure(
        string version,
        string framework,
        PackageEndpointScopeFailureKind expected)
    {
        byte[] archive = ReadAvalonia();
        var server = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        PackageEndpointScopeOutcome outcome = await OpenEndpointAsync(
            environment,
            new InMemoryPackageStore(),
            new PackageEndpointScopeRequest(
                PackageSourceCoordinate.Create(Avalonia, version),
                framework,
                PackageAssetDemand.Surface));

        var failed = Assert.IsType<PackageEndpointScopeOutcome.Failed>(outcome);
        Assert.Equal(expected, failed.Kind);
        Assert.NotNull(failed.HouseResult);
        Assert.False(string.IsNullOrWhiteSpace(failed.Message));
        if (expected == PackageEndpointScopeFailureKind.NoCompatibleFramework)
        {
            PackageCompileAssetSelection selection =
                Assert.IsType<PackageCompileAssetSelection>(failed.Selection);
            Assert.False(selection.IsSelected);
        }
    }

    private static PackageEndpointScope AssertOpened(PackageEndpointScopeOutcome outcome)
    {
        if (outcome is PackageEndpointScopeOutcome.Failed failed)
            Assert.Fail($"{failed.Kind}: {failed.Message}");
        return Assert.IsType<PackageEndpointScopeOutcome.Opened>(outcome).Scope;
    }

    private static Task<PackageEndpointScopeOutcome> OpenEndpointAsync(
        RangedEnvironment environment,
        IPackageStore store,
        PackageEndpointScopeRequest request)
    {
        var house = new PackageHouse(
            environment.Authorization,
            new PackagePayloadAcquisitionPlan(
                (_, _) => store,
                access: PackagePayloadAccess.Ranged,
                log: environment.Log.Enqueue,
                rangedSizeCut: 0));
        var operation = PackageHouseOperation.Create(PackageHouseOperationProfile.Realize);
        return PackageEndpointScope.OpenAsync(
            house,
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operation.RequestTimeout,
                operation.OperationTimeout),
            request,
            TestContext.Current.CancellationToken);
    }
}
