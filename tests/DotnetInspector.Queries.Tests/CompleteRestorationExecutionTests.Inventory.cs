using System.Diagnostics.CodeAnalysis;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;

namespace DotnetInspector.Queries.Tests;

public sealed partial class CompleteRestorationExecutionTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Inventory_PackageFactsRemainUsableAfterClose(int version)
    {
        PackageFixture fixture = await SystemTextJsonPackageAsync();
        string registrations = version == 2 ? "" : "\"r\":[],";
        CompleteWorkspaceSnapshot snapshot = await RestoreInventoryAsync(
            $$$"""
            {"f":{{{version}}},"t":[["System.Text.Json","9.0.4","net9.0",null]],"g":[[0]],{{{registrations}}}"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"package"},"u":{"k":"package"}}]}
            """,
            fixture.Store);

        CompleteRestorationInventory inventory =
            Assert.IsType<CompleteRestorationInventory>(snapshot.Inventory);
        Assert.Empty(inventory.Platforms);
        CompleteRestorationPackageInventory package =
            Assert.Single(inventory.Packages);
        Assert.Equal("t0", package.NavigationId);
        Assert.Equal(0, package.ContextIndex);
        AssertPackageInventory(package, "net9.0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inventory_MixedContextPreservesInactivePlatform(
        bool reverseContexts)
    {
        PackageFixture fixture = await SystemTextJsonAndPlatformPackagesAsync();
        int mixedIndex = reverseContexts ? 1 : 0;
        string contexts = reverseContexts ? "[[2],[0,1]]" : "[[0,1],[2]]";
        CompleteWorkspaceSnapshot snapshot = await RestoreInventoryAsync(
            $$$"""
            {"f":3,"t":[[":Platform","10.0.10","net10.0","linux-x64"],["System.Text.Json","9.0.4","net10.0","linux-x64"],["System.Text.Json","9.0.4","net9.0",null]],"g":{{{contexts}}},"r":[],"a":1,"x":{{{mixedIndex}}},"v":[{"t":null,"u":{"k":"workspace"}},{"t":0},{"t":1,"r":{"k":"package"},"u":{"k":"package"}},{"t":2,"r":{"k":"package"},"u":{"k":"package"}}]}
            """,
            fixture.Store);

        Assert.Equal(
            StructuralSubjectKind.Package,
            snapshot.Navigation.State.Snapshot.ActiveSubject.Kind);
        CompleteRestorationInventory inventory =
            Assert.IsType<CompleteRestorationInventory>(snapshot.Inventory);
        Assert.Collection(
            inventory.Packages,
            package =>
            {
                Assert.Equal("t1", package.NavigationId);
                Assert.Equal(mixedIndex, package.ContextIndex);
                Assert.Equal("linux-x64", package.Package.Coordinate.RuntimeIdentifier);
                AssertPackageInventory(package, "net10.0");
            },
            package =>
            {
                Assert.Equal("t2", package.NavigationId);
                Assert.Equal(1 - mixedIndex, package.ContextIndex);
                Assert.Null(package.Package.Coordinate.RuntimeIdentifier);
                AssertPackageInventory(package, "net9.0");
            });
        CompleteRestorationPlatformInventory platform =
            Assert.Single(inventory.Platforms);
        Assert.Equal(mixedIndex, platform.ContextIndex);
        Assert.Equal("linux-x64", platform.RuntimeIdentifier);
        AssertPlatformInventory(platform);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inventory_PlatformOnlyIsExplicitAndBounded(bool capture)
    {
        PackageFixture fixture = await SystemTextJsonAndPlatformPackagesAsync();
        CompleteWorkspaceSnapshot snapshot = await RestoreInventoryAsync(
            """
            {"f":3,"t":[[":Platform","10.0.10","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}
            """,
            fixture.Store,
            capture);

        if (!capture)
        {
            Assert.Null(snapshot.Inventory);
            return;
        }
        CompleteRestorationInventory inventory =
            Assert.IsType<CompleteRestorationInventory>(snapshot.Inventory);
        Assert.Empty(inventory.Packages);
        CompleteRestorationPlatformInventory platform =
            Assert.Single(inventory.Platforms);
        Assert.Equal(0, platform.ContextIndex);
        Assert.Null(platform.RuntimeIdentifier);
        AssertPlatformInventory(platform);
    }

    [Fact]
    public async Task Inventory_RegistrationOnlyIsCapturedEmpty()
    {
        CompleteWorkspaceSnapshot snapshot = await RestoreInventoryAsync(
            """
            {"f":3,"t":[],"g":[],"r":[["p","Microsoft.Extensions."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}
            """,
            new InMemoryPackageStore());

        CompleteRestorationInventory inventory =
            Assert.IsType<CompleteRestorationInventory>(snapshot.Inventory);
        Assert.Empty(inventory.Packages);
        Assert.Empty(inventory.Platforms);
        Assert.Single(snapshot.Definition.Plan.Registrations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inventory_MissingManifestFailsOnlyRequestedCapture(bool capture)
    {
        PackageFixture fixture = await SystemTextJsonPackageAsync();
        string encoded = WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                """
                {"f":3,"t":[["System.Text.Json","9.0.4","net9.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"package"},"u":{"k":"package"}}]}
                """,
                TestContext.Current.CancellationToken));
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestorationWithCancellation(
                    encoded, authority, TestContext.Current.CancellationToken));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());
        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, new ManifestlessStore(fixture.Store)) with
                {
                    CaptureInventory = capture,
                },
                TestContext.Current.CancellationToken);

        if (!capture)
        {
            var activated =
                Assert.IsType<CompleteRestorationResult<InspectionWorkspace>.Activated>(
                    result);
            await using InspectionWorkspace workspace = activated.Activation;
            Assert.Null(activated.Workspace.Snapshot.Inventory);
            Assert.True((await workspace.CloseAsync()).Succeeded);
            return;
        }

        var failed =
            Assert.IsType<CompleteRestorationResult<InspectionWorkspace>.Failed>(
                result);
        var failure =
            Assert.IsType<CompleteRestorationFailure.ProjectionFailed>(
                failed.Failure);
        Assert.Contains("'t0'", failure.Message);
        Assert.Contains("manifest", failure.Message);
        Assert.True(host.CloseReport?.Succeeded);
    }

    private static void AssertPackageInventory(
        CompleteRestorationPackageInventory package,
        string requestedFramework)
    {
        Assert.Equal("System.Text.Json", package.Package.PackageId, ignoreCase: true);
        Assert.Equal("9.0.4", package.Package.PackageVersion);
        Assert.Equal(requestedFramework, package.Package.Coordinate.Framework);
        Assert.Equal("net9.0", package.Selection.TargetFramework);
        Assert.Contains("net8.0", package.Selection.AvailableTargetFrameworks);
        Assert.Contains("net9.0", package.Selection.AvailableTargetFrameworks);
        Assert.Contains(
            package.Entries,
            entry => entry.Path == "lib/net9.0/System.Text.Json.xml"
                && entry.Length > 0);
        CompleteRestorationPackageLibrary library =
            Assert.Single(package.Libraries);
        Assert.Equal("lib/net9.0/System.Text.Json.dll", library.Asset.Path);
        var surface =
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                Assert.Single(package.Surface.Assemblies.Assemblies));
        Assert.Same(library.Subject.Registration, surface.Subject.Registration);
        Assert.Equal(library.Subject.Identity, surface.Subject.Identity);
        Assert.Contains(
            surface.Value.Surface.Types,
            type => type.FullName == "System.Text.Json.JsonSerializer");
    }

    private static void AssertPlatformInventory(
        CompleteRestorationPlatformInventory platform)
    {
        Assert.Equal("t0", platform.NavigationId);
        Assert.Equal("runtime", platform.Family);
        Assert.Equal("10.0.10", platform.Version);
        Assert.Equal("net10.0", platform.Framework);
        Assert.True(platform.Libraries.Length > 1);
        Assert.All(platform.Libraries, library =>
        {
            Assert.Equal("runtime", library.Coordinate.Family);
            Assert.Equal("10.0.10", library.Coordinate.Version);
            Assert.Equal("net10.0", library.Coordinate.Framework);
            Assert.False(string.IsNullOrEmpty(library.Subject.Identity.Name));
        });
        ApiSurfaceProjectionTruncation truncation =
            Assert.IsType<ApiSurfaceProjectionTruncation>(platform.Surface.Truncation);
        Assert.Equal(ApiSurfaceProjectionLimit.Participants, truncation.Limit);
        Assert.Equal(1, truncation.ProjectedParticipants);
        Assert.Equal(platform.Libraries.Length - 1, truncation.OmittedParticipants);
        var surface =
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                Assert.Single(platform.Surface.Assemblies.Assemblies));
        Assert.Same(
            platform.Libraries[0].Subject.Registration,
            surface.Subject.Registration);
        Assert.NotEmpty(surface.Value.Surface.Types);
        Assert.False(platform.Surface.IsComplete);
    }

    private static async Task<CompleteWorkspaceSnapshot> RestoreInventoryAsync(
        string json,
        IPackageStore store,
        bool capture = true)
    {
        string encoded = WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(json));
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(encoded, authority));
        using var client = new HttpClient(new RejectingHandler());
        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                new TestHost(),
                Options(client, store) with
                {
                    CaptureInventory = capture,
                    PlatformSurfaceLimits = new(
                        maxParticipants: 1,
                        maxTypes: 5_000,
                        maxMembers: 100_000,
                        maxInspectionFailures: 100,
                        maxTypeForwarders: 1_000,
                        maxMetadataRows: 1_000_000,
                        maxRetainedTextCharacters: 5_000_000),
                },
                TestContext.Current.CancellationToken);
        if (result is CompleteRestorationResult<InspectionWorkspace>.Failed failed)
            Assert.Fail($"{failed.Failure.GetType().Name}: {failed.Failure.Message}");
        var activated =
            Assert.IsType<CompleteRestorationResult<InspectionWorkspace>.Activated>(
                result);
        await using InspectionWorkspace workspace = activated.Activation;
        Assert.Equal(
            encoded,
            Assert.IsType<CompleteRestorationProjection.Projectable>(
                activated.Workspace.Projection).CanonicalPacket);
        Assert.True((await workspace.CloseAsync()).Succeeded);
        return activated.Workspace.Snapshot;
    }

    private sealed class ManifestlessStore(IPackageStore inner) : IPackageStore
    {
        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            inner.TryGetCached(packageName, version, allowedSourceKeys, log)
                is { } content
                ? new ManifestlessContent(content)
                : null;

        public async ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default) =>
            new ManifestlessContent(
                await inner.CommitAsync(
                    packageName, version, sourceKey, nupkg, cancellationToken));
    }

    private sealed class ManifestlessContent(IPackageContent inner) : IPackageContent
    {
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public PackageContentGenerationIdentity GenerationIdentity =>
            inner.GenerationIdentity;
        public bool RequiresArchiveTreeMatch => inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenEntry(relativePath, out stream);

        public IEnumerable<string> EnumerateEntries() => inner.EnumerateEntries();
    }
}
