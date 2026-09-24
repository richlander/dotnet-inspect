using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Acquiring one exact package Root without full-role assembly realization,
/// from an explicit coordinate and from the owner-issued exact reacquisition
/// request, including the opaque token a host hands across a transport
/// boundary.
/// </summary>
public sealed class PackageRootAcquisitionTests
{
    const string PackageId = "acq.sample";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string AssemblyName = "Acq.Sample";

    static readonly PackageSource NuGetOrg = PackageSource.NuGetOrg;
    static readonly PackageSource Private =
        new("private", "https://private.test/v3/index.json");

    /// <summary>
    /// A surface-only Root keeps its selection and is never upgraded in place
    /// (docs/design/package-read-demand.md).
    /// </summary>
    [Fact]
    public async Task AssetDemand_SurfaceRootIsNeverUpgradedInPlace()
    {
        using var http = new HttpClient(new FailingHandler());
        IPackageStore store = await CachedStoreAsync(LibraryPackage());
        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                PackageRootAcquisitionRequest.Create(
                    PackageId,
                    Version,
                    Framework),
                Options(http, store),
                TestContext.Current.CancellationToken));
        PackageRootBinding full = acquired.Binding;
        Assert.Equal(PackageAssetDemand.SurfaceAndImplementation, full.Root.AssetDemand);

        PackageRootBinding surface = full.WithAssetDemand(PackageAssetDemand.Surface);

        Assert.Equal(PackageAssetDemand.Surface, surface.Root.AssetDemand);
        Assert.Equal(
            full.Root.AssetSelection.Assets.Select(asset => asset.Path),
            surface.Root.AssetSelection.Assets.Select(asset => asset.Path));
        Assert.Same(surface, surface.WithAssetDemand(PackageAssetDemand.Surface));
        Assert.Throws<InvalidOperationException>(() =>
            surface.WithAssetDemand(PackageAssetDemand.SurfaceAndImplementation));
    }

    /// <summary>
    /// A Root realized with named implementation assemblies realizes only
    /// those, is never widened in place, and a name that selects no
    /// implementation asset fails visibly (docs/design/package-read-demand.md,
    /// gate 9).
    /// </summary>
    [Fact]
    public async Task AssetDemand_NamedRootRealizesOnlyItsNames()
    {
        using var http = new HttpClient(new FailingHandler());
        IPackageStore store = await CachedStoreAsync(LibraryPackage());
        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                PackageRootAcquisitionRequest.Create(
                    PackageId,
                    Version,
                    Framework),
                Options(http, store),
                TestContext.Current.CancellationToken));
        PackageRootBinding full = acquired.Binding;
        PackageImplementationNames names =
            PackageImplementationNames.Create([$"{AssemblyName.ToLowerInvariant()}.DLL"]);

        PackageRootBinding named = full.WithAssetDemand(
            PackageAssetDemand.SurfaceAndImplementation,
            names);

        Assert.Same(names, named.Root.ImplementationNames);
        Assert.Equal(
            [$"lib/{Framework}/{AssemblyName}.dll"],
            named.Root.RealizedImplementationAssets.Select(asset => asset.Path));
        Assert.Same(
            named,
            named.WithAssetDemand(
                PackageAssetDemand.SurfaceAndImplementation,
                PackageImplementationNames.Create([$"{AssemblyName}.dll"])));
        Assert.Throws<InvalidOperationException>(() =>
            named.WithAssetDemand(PackageAssetDemand.SurfaceAndImplementation));
        Assert.Empty(
            named.WithAssetDemand(PackageAssetDemand.Surface)
                .Root.RealizedImplementationAssets);
        Assert.Throws<ArgumentException>(() =>
            full.WithAssetDemand(PackageAssetDemand.Surface, names));

        PackageImplementationNameException failure =
            Assert.Throws<PackageImplementationNameException>(() =>
                full.WithAssetDemand(
                    PackageAssetDemand.SurfaceAndImplementation,
                    PackageImplementationNames.Create(["Missing.dll"])));
        Assert.Equal(["Missing.dll"], failure.UnmatchedNames);
    }

    [Fact]
    public async Task ExplicitCoordinate_AcquiresRootAndIssuesExactRequest()
    {
        using var http = new HttpClient(new FailingHandler());
        IPackageStore store = await CachedStoreAsync(LibraryPackage());

        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                PackageRootAcquisitionRequest.Create(
                    PackageId,
                    Version,
                    Framework),
                Options(http, store),
                TestContext.Current.CancellationToken));

        PackageRootBinding binding = acquired.Binding;
        Assert.Equal(PackageId, binding.Coordinate.PackageId);
        Assert.Equal(Version, binding.Coordinate.Version);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            binding.Coordinate.Producer);
        Assert.Equal(Framework, binding.Coordinate.Framework);
        Assert.Null(binding.Coordinate.RuntimeIdentifier);
        Assert.Equal(
            $"lib/{Framework}/{AssemblyName}.dll",
            binding.Root.AssetSelection.Assets.Single().Path);

        // The issued request is the binding's own, so a consumer that started
        // from an explicit coordinate holds a reacquirable handle immediately.
        Assert.Equal(binding.CreateReacquisitionRequest(), acquired.Request);
        Assert.Equal(Framework, acquired.Request.CompileTargetFramework);
        Assert.Equal(Framework, acquired.Request.SelectionTargetFramework);
    }

    [Fact]
    public async Task Acquired_ExposesTheLiveContentTheBindingReads()
    {
        using var http = new HttpClient(new FailingHandler());
        IPackageStore store = await CachedStoreAsync(LibraryPackage());
        WorkspaceContextLoadOptions options = Options(http, store);

        var first = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                PackageRootAcquisitionRequest.Create(
                    PackageId,
                    Version,
                    Framework),
                options,
                TestContext.Current.CancellationToken));

        // The destination adapts this instance; it is the one the binding
        // reads through, so an exact-content check against the binding holds
        // without the destination re-deriving source or selection authority.
        IPackageContent content = first.Payload.Content;
        Assert.True(first.Binding.Root.ReferencesContent(content));
        Assert.Same(
            content.GenerationIdentity,
            first.Binding.ContentGenerationIdentity);
        Assert.Equal(
            first.Binding.Coordinate.Producer,
            first.Payload.ProducerKey);
        Assert.Equal(content.ProducerKey, first.Payload.ProducerKey);

        var second = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                first.Request,
                options,
                TestContext.Current.CancellationToken));

        // Reacquiring the same retained generation issues another handle over
        // it rather than copying the archive or minting a second generation.
        Assert.Same(
            content.GenerationIdentity,
            second.Payload.Content.GenerationIdentity);
        Assert.True(
            second.Binding.Root.ReferencesContent(second.Payload.Content));
        Assert.Equal(
            PackagePayloadOrigin.Cache,
            second.Payload.Origin);
    }

    [Fact]
    public async Task ExactRequest_ReacquiresSameLogicalRootThroughToken()
    {
        using var http = new HttpClient(new FailingHandler());
        IPackageStore store = await CachedStoreAsync(LibraryPackage());
        WorkspaceContextLoadOptions options = Options(http, store);

        var first = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                PackageRootAcquisitionRequest.Create(
                    PackageId,
                    Version,
                    Framework),
                options,
                TestContext.Current.CancellationToken));

        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                first.Request.Encode(),
                out PackageRootReacquisitionRequest? decoded));

        var second = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                decoded,
                options,
                TestContext.Current.CancellationToken));

        Assert.Equal(first.Request, second.Request);
        Assert.Equal(
            first.Binding.Root.AssetSelection.Assets.Single().Path,
            second.Binding.Root.AssetSelection.Assets.Single().Path);
        Assert.NotSame(first.Binding, second.Binding);
        Assert.NotSame(first.Binding.SelectionIdentity, second.Binding.SelectionIdentity);
    }

    [Fact]
    public async Task PortableConfiguredProducer_ReacquiresWithoutChangingContentKey()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.Create(
                Private,
                PackageSourceAssociation.Create(),
                new FailingHandler());
        PackageProducerIdentity producer = source.Source.Producer;
        string contentProducerKey = NuGetCache.GetSourceKey(Private.Url);
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            contentProducerKey,
            new MemoryStream(LibraryPackage()),
            TestContext.Current.CancellationToken);
        using var http = new HttpClient(new FailingHandler());
        PackageRootReacquisitionRequest request = RequestForProducer(
            producer.PortableKey,
            Framework,
            null,
            Framework,
            null);
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                request.Encode(),
                out PackageRootReacquisitionRequest? decoded));

        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                decoded,
                Options(
                    http,
                    store,
                    new UniformPackageSourceAuthorization([Private])),
                TestContext.Current.CancellationToken));

        Assert.Equal(request, acquired.Request);
        Assert.Equal(
            producer.PortableKey,
            acquired.Binding.Coordinate.Producer);
        Assert.Equal(
            contentProducerKey,
            acquired.Binding.Root.ProducerKey);
        Assert.Equal(
            contentProducerKey,
            acquired.Payload.ProducerKey);
        Assert.True(
            acquired.Binding.Root.ReferencesContent(
                acquired.Payload.Content));
    }

    [Fact]
    public async Task PortableProducer_StillRequiresDestinationAuthorization()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.Create(
                Private,
                PackageSourceAssociation.Create(),
                new FailingHandler());
        PackageRootReacquisitionRequest request = RequestForProducer(
            source.Source.Producer.PortableKey,
            Framework,
            null,
            Framework,
            null);
        using var http = new HttpClient(new FailingHandler());

        var failed = Assert.IsType<PackageRootAcquisitionOutcome.Failed>(
            await PackageRootAcquisition.AcquireAsync(
                request,
                Options(http, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageRootAcquisitionFailureKind.ProducerNotAuthorized,
            failed.Kind);
    }

    [Fact]
    public async Task CurrentNuGetProducerKey_ReacquiresWithoutRewritingRequest()
    {
        string contentProducerKey = NuGetCache.GetSourceKey(NuGetOrg.Url);
        IPackageStore store = await CachedStoreAsync(LibraryPackage());
        using var http = new HttpClient(new FailingHandler());
        PackageRootReacquisitionRequest request = RequestForProducer(
            PackageProducerIdentity.NuGetOrg.Key,
            Framework,
            null,
            Framework,
            null);

        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                request,
                Options(http, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(request, acquired.Request);
        Assert.Equal(
            PackageProducerIdentity.NuGetOrg.Key,
            acquired.Binding.Coordinate.Producer);
        Assert.Equal(contentProducerKey, acquired.Binding.Root.ProducerKey);
    }

    [Fact]
    public void PortableProducerAuthorization_PreservesEquivalentAuthoritiesInOrder()
    {
        PackageSource[] sources =
        [
            new("first", Private.Url),
            new("second", Private.Url),
        ];
        PackageProducerIdentity producer =
            PackageSourceClientFactory.GetProducerIdentity(sources[0]);

        PackageRootProducerAuthorization.MatchResult match =
            PackageRootProducerAuthorization.Match(
                sources,
                producer.PortableKey);

        Assert.False(match.Ambiguous);
        Assert.Equal(
            ["first", "second"],
            match.Candidates.Select(
                candidate => candidate.Source.Name));
        Assert.All(
            match.Candidates,
            candidate => Assert.Equal(
                producer,
                candidate.Producer));
    }

    [Fact]
    public async Task ExactRequest_SeparatesAcquisitionAndSelectionTargets()
    {
        using var http = new HttpClient(new FailingHandler());
        IPackageStore store = await CachedStoreAsync(
            LibraryPackage("netstandard2.0"));
        WorkspaceContextLoadOptions options = Options(http, store);

        PackageRootAcquisitionRequest explicitRequest =
            PackageRootAcquisitionRequest.CreateFrameworkNeutral(
                PackageId,
                Version,
                "netstandard2.0");
        Assert.Null(explicitRequest.AcquisitionFramework);
        Assert.Equal(
            "netstandard2.0",
            explicitRequest.SelectionTargetFramework);

        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                explicitRequest,
                options,
                TestContext.Current.CancellationToken));

        Assert.Null(acquired.Binding.Coordinate.Framework);
        Assert.Equal(
            "netstandard2.0",
            acquired.Request.SelectionTargetFramework);
        Assert.Equal(
            $"lib/netstandard2.0/{AssemblyName}.dll",
            acquired.Binding.Root.AssetSelection.Assets.Single().Path);

        // The separation survives the transport seam and reacquisition.
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                acquired.Request.Encode(),
                out PackageRootReacquisitionRequest? decoded));
        Assert.Null(decoded.Coordinate.Framework);

        var reacquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                decoded,
                options,
                TestContext.Current.CancellationToken));
        Assert.Equal(acquired.Request, reacquired.Request);
        Assert.Equal(
            $"lib/netstandard2.0/{AssemblyName}.dll",
            reacquired.Binding.Root.AssetSelection.Assets.Single().Path);
    }

    [Fact]
    public async Task ExactRequest_RejectsReplacementImplementationTarget()
    {
        using var http = new HttpClient(new FailingHandler());
        var store = new InMemoryPackageStore();
        string producer = NuGetCache.GetSourceKey(NuGetOrg.Url);
        await store.CommitAsync(
            PackageId,
            Version,
            producer,
            new MemoryStream(SplitPackage("net8.0")),
            TestContext.Current.CancellationToken);
        WorkspaceContextLoadOptions options = Options(http, store);

        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                PackageRootAcquisitionRequest.Create(
                    PackageId,
                    Version,
                    "net10.0"),
                options,
                TestContext.Current.CancellationToken));

        Assert.Equal("net10.0", acquired.Request.CompileTargetFramework);
        Assert.Equal("net8.0", acquired.Request.SelectionTargetFramework);
        Assert.True(acquired.Request.HasSelectedImplementationUniverse);

        await store.CommitAsync(
            PackageId,
            Version,
            producer,
            new MemoryStream(SplitPackage("net7.0")),
            TestContext.Current.CancellationToken);
        var failed = Assert.IsType<PackageRootAcquisitionOutcome.Failed>(
            await PackageRootAcquisition.AcquireAsync(
                acquired.Request,
                options,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageRootAcquisitionFailureKind.SelectionRequestNotReproduced,
            failed.Kind);
    }

    [Fact]
    public async Task ExactRequest_ReopensAfterCandidateWorkspaceDisposal()
    {
        using var http = new HttpClient(new FailingHandler());
        IPackageStore store = await CachedStoreAsync(LibraryPackage());
        WorkspaceContextLoadOptions options = Options(http, store);

        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                PackageRootAcquisitionRequest.Create(
                    PackageId,
                    Version,
                    Framework),
                options,
                TestContext.Current.CancellationToken));
        string token = acquired.Request.Encode();

        await using (InspectionWorkspace candidate =
            new InspectionWorkspace())
        {
            using SparsePackageAssemblyRealization realization =
                await ProjectAsync(candidate, acquired.Binding);
            Assert.Equal(
                AssemblyName,
                Named(realization));
            await candidate.CloseAsync();
        }

        // Only the token survives the candidate; it reopens the same logical
        // Root in a fresh Workspace with no retained resource in between.
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                token,
                out PackageRootReacquisitionRequest? decoded));
        var replacement =
            Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
                await PackageRootAcquisition.AcquireAsync(
                    decoded,
                    options,
                    TestContext.Current.CancellationToken));

        await using InspectionWorkspace reopened =
            new InspectionWorkspace();
        using SparsePackageAssemblyRealization second =
            await ProjectAsync(reopened, replacement.Binding);
        Assert.Equal(AssemblyName, Named(second));
    }

    [Fact]
    public async Task ExplicitCoordinate_UnauthorizedSourcesFailVisibly()
    {
        using var http = new HttpClient(new FailingHandler());

        var failed = Assert.IsType<PackageRootAcquisitionOutcome.Failed>(
            await PackageRootAcquisition.AcquireAsync(
                PackageRootAcquisitionRequest.Create(
                    PackageId,
                    Version,
                    Framework),
                Options(
                    http,
                    new InMemoryPackageStore(),
                    new DenyingAuthorization()),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageRootAcquisitionFailureKind.PackageUnavailable,
            failed.Kind);
        Assert.False(string.IsNullOrWhiteSpace(failed.Message));
    }

    [Fact]
    public async Task ExactRequest_UnauthorizedProducerFailsVisibly()
    {
        using var http = new HttpClient(new FailingHandler());
        IPackageStore store = await CachedStoreAsync(LibraryPackage());
        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                PackageRootAcquisitionRequest.Create(
                    PackageId,
                    Version,
                    Framework),
                Options(http, store),
                TestContext.Current.CancellationToken));

        var denied = Assert.IsType<PackageRootAcquisitionOutcome.Failed>(
            await PackageRootAcquisition.AcquireAsync(
                acquired.Request,
                Options(http, store, new DenyingAuthorization()),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            PackageRootAcquisitionFailureKind.ProducerNotAuthorized,
            denied.Kind);
        Assert.False(string.IsNullOrWhiteSpace(denied.Message));

        // A host that authorizes some other producer for this id does not get
        // a substitute Root: the pinned producer is the only one that answers.
        var substituted = Assert.IsType<PackageRootAcquisitionOutcome.Failed>(
            await PackageRootAcquisition.AcquireAsync(
                acquired.Request,
                Options(
                    http,
                    store,
                    new UniformPackageSourceAuthorization([Private])),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            PackageRootAcquisitionFailureKind.ProducerNotAuthorized,
            substituted.Kind);
    }

    [Fact]
    public void Token_RoundTripsExactRequest()
    {
        foreach (PackageRootReacquisitionRequest request in
            new[]
            {
                Request(Framework, null, Framework, null),
                Request(Framework, "win-x64", Framework, "win-x64"),
                Request(Framework, null, "netstandard2.0", null),
                Request(Framework, "win-x64", "netstandard2.0", "win-x64"),
                Request(
                    Framework,
                    null,
                    "net6.0",
                    null,
                    compileTargetFramework: Framework),
                Request(
                    Framework,
                    null,
                    "net8.0",
                    null,
                    compileTargetFramework: Framework),
                Request(
                    Framework,
                    null,
                    Framework,
                    null,
                    hasSelectedImplementationUniverse: false),
                Request(
                    Framework,
                    null,
                    Framework,
                    null,
                    allowsCompatibleTargetSelection: true),
                Request(
                    Framework,
                    null,
                    Framework,
                    null,
                    usesCompatibleImplementationSelection: true),
                Request(null, null, "netstandard2.0", null),
                Request(null, null, null, null),
            })
        {
            string token = request.Encode();
            Assert.StartsWith(
                PackageRootReacquisitionRequest.TokenPrefix,
                token,
                StringComparison.Ordinal);
            Assert.DoesNotContain('=', token);
            Assert.True(
                token.Length
                    <= PackageRootReacquisitionRequest.MaxEncodedLength);

            Assert.True(
                PackageRootReacquisitionRequest.TryDecode(
                    token,
                    out PackageRootReacquisitionRequest? decoded));
            Assert.Equal(request, decoded);
            Assert.Equal(request.GetHashCode(), decoded.GetHashCode());
            Assert.Equal(token, decoded.Encode());
        }

        string legacyToken = LegacyToken(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            Framework,
            null,
            Framework,
            null);
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                legacyToken,
                out PackageRootReacquisitionRequest? legacy));
        Assert.Equal(Framework, legacy.CompileTargetFramework);
        Assert.Equal(Framework, legacy.SelectionTargetFramework);
        Assert.False(legacy.AllowsCompatibleTargetSelection);
        Assert.False(legacy.UsesCompatibleImplementationSelection);
        Assert.True(legacy.HasSelectedImplementationUniverse);
        Assert.StartsWith(
            PackageRootReacquisitionRequest.TokenPrefix,
            legacy.Encode(),
            StringComparison.Ordinal);

        string olderToken = VersionedToken(
            "pkgroot2",
            PackageId,
            Version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            Framework,
            null,
            Framework,
            "net6.0",
            null);
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                olderToken,
                out PackageRootReacquisitionRequest? older));
        Assert.True(older.AllowsCompatibleTargetSelection);
        Assert.True(older.UsesCompatibleImplementationSelection);
        Assert.True(older.HasSelectedImplementationUniverse);
        Assert.StartsWith(
            PackageRootReacquisitionRequest.TokenPrefix,
            older.Encode(),
            StringComparison.Ordinal);

        string earlierToken = VersionedToken(
            "pkgroot3",
            PackageId,
            Version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            Framework,
            null,
            Framework,
            Framework,
            null,
            "compatible");
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                earlierToken,
                out PackageRootReacquisitionRequest? earlier));
        Assert.True(earlier.AllowsCompatibleTargetSelection);
        Assert.True(earlier.UsesCompatibleImplementationSelection);
        Assert.True(earlier.HasSelectedImplementationUniverse);
        Assert.StartsWith(
            PackageRootReacquisitionRequest.TokenPrefix,
            earlier.Encode(),
            StringComparison.Ordinal);

        string previousToken = VersionedToken(
            "pkgroot4",
            PackageId,
            Version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            Framework,
            null,
            Framework,
            Framework,
            null,
            "compatible",
            "exact");
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                previousToken,
                out PackageRootReacquisitionRequest? previous));
        Assert.True(previous.AllowsCompatibleTargetSelection);
        Assert.False(previous.UsesCompatibleImplementationSelection);
        Assert.True(previous.HasSelectedImplementationUniverse);
        Assert.StartsWith(
            PackageRootReacquisitionRequest.TokenPrefix,
            previous.Encode(),
            StringComparison.Ordinal);

        // Distinct requests do not share a token.
        Assert.NotEqual(
            Request(Framework, null, Framework, null).Encode(),
            Request(Framework, "win-x64", Framework, "win-x64").Encode());
    }

    [Fact]
    public void Token_RejectsMalformedOrNonCanonicalInput()
    {
        string valid = Request(Framework, null, Framework, null).Encode();
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(valid, out _));

        foreach (string? candidate in
            new string?[]
            {
                null,
                string.Empty,
                "pkgroot0" + valid[8..],
                valid[8..],
                string.Join('.', valid.Split('.')[..^1]),
                valid + ".",
                Token("A!", "1.0.0", "nuget.org", null, null, null, null, null, "exact"),
                Token("AAAAA", "1.0.0", "nuget.org", null, null, null, null, null, "exact"),
                Token("__4", "1.0.0", "nuget.org", null, null, null, null, null, "exact"),
                Token(null, Version, "nuget.org", null, null, null, null, null, "exact"),
                Token(PackageId, Version, null, null, null, null, null, null, "exact"),
                Token(
                    PackageId,
                    Version,
                    "nfp-1." + new string('a', 63),
                    null,
                    null,
                    null,
                    null,
                    null,
                    "exact"),
                Token(
                    PackageId,
                    Version,
                    "nfp-1." + new string('A', 64),
                    null,
                    null,
                    null,
                    null,
                    null,
                    "exact"),
                Token(
                    "not a package id",
                    Version,
                    "nuget.org",
                    null,
                    null,
                    null,
                    null,
                    null,
                    "exact"),
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    "net11.0",
                    "WIN-X64",
                    "net11.0",
                    "net11.0",
                    "win-x64",
                    "exact"),
                // Canonical facts spelled non-canonically: refused rather
                // than normalized, so one request has exactly one token.
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    null,
                    null,
                    ".NETStandard,Version=v2.0",
                    ".NETStandard,Version=v2.0",
                    null,
                    "exact"),
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    "net11.0",
                    "win-x64",
                    "net11.0",
                    "net11.0",
                    "WIN-X64",
                    "exact"),
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    "net11.0",
                    null,
                    null,
                    "net11.0",
                    null,
                    "exact"),
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    "net11.0",
                    null,
                    "net11.0",
                    null,
                    null,
                    "exact"),
                Token(
                    new string('a', 600),
                    new string('1', 600),
                    "nuget.org",
                    null,
                    null,
                    null,
                    null,
                    null,
                    "exact"),
                Token(
                    PackageId,
                    Version,
                    NuGetCache.GetSourceKey(NuGetOrg.Url),
                    Framework,
                    null,
                    null,
                    null,
                    null,
                    "compatible",
                    "exact"),
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    Framework,
                    null,
                    " ",
                    " ",
                    null,
                    "compatible",
                    "exact"),
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    null,
                    null,
                    null,
                    null,
                    null,
                    "exact",
                    "exact",
                    "selected"),
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    Framework,
                    null,
                    Framework,
                    " ",
                    null,
                    "compatible",
                    "exact"),
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    Framework,
                    null,
                    " net11.0",
                    " net11.0",
                    null,
                    "exact",
                    "exact"),
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    Framework,
                    null,
                    Framework,
                    Framework,
                    null,
                    "other",
                    "exact"),
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    Framework,
                    null,
                    Framework,
                    Framework,
                    null,
                    "exact",
                    "compatible"),
                Token(
                    PackageId,
                    Version,
                    "nuget.org",
                    Framework,
                    null,
                    Framework,
                    Framework,
                    null,
                    "exact",
                    "exact",
                    "other"),
            })
        {
            Assert.False(
                PackageRootReacquisitionRequest.TryDecode(
                    candidate,
                    out PackageRootReacquisitionRequest? decoded),
                $"Decoding should have refused: {candidate ?? "<null>"}");
            Assert.Null(decoded);
        }
    }

    [Theory]
    [InlineData(null, "win-x64")]
    [InlineData(null, "not a rid")]
    [InlineData("win-x64", null)]
    [InlineData("win-x64", "linux-x64")]
    [InlineData("win-x64", "not a rid")]
    public void Token_RejectsSelectionRuntimeNotIssuedByBinding(
        string? acquisitionRuntimeIdentifier,
        string? selectionRuntimeIdentifier)
    {
        string token = Token(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            Framework,
            acquisitionRuntimeIdentifier,
            Framework,
            Framework,
            selectionRuntimeIdentifier);

        Assert.False(
            PackageRootReacquisitionRequest.TryDecode(token, out var decoded));
        Assert.Null(decoded);
    }

    [Fact]
    public void ExplicitRequest_StatesItsTargetContract()
    {
        PackageRootAcquisitionRequest neutral =
            PackageRootAcquisitionRequest.Create(PackageId, Version);
        Assert.Null(neutral.AcquisitionFramework);
        Assert.Null(neutral.SelectionTargetFramework);

        PackageRootAcquisitionRequest targeted =
            PackageRootAcquisitionRequest.Create(
                PackageId,
                Version,
                "NET11.0",
                "win-x64");
        Assert.Equal(Framework, targeted.AcquisitionFramework);
        Assert.Equal("NET11.0", targeted.SelectionTargetFramework);
        Assert.Equal("win-x64", targeted.SelectionRuntimeIdentifier);

        Assert.Throws<ArgumentException>(
            () => PackageRootAcquisitionRequest.Create(
                PackageId,
                Version,
                Framework,
                "WIN-X64"));
        Assert.Throws<ArgumentException>(
            () => PackageRootAcquisitionRequest.Create(
                PackageId,
                Version,
                ".NETStandard,Version=v2.0",
                "win-x64"));
        Assert.Throws<ArgumentException>(
            () => PackageRootAcquisitionRequest.Create(PackageId, " "));

        // Framework-neutral acquisition states the separation explicitly
        // rather than inferring it from an unusable selection spelling.
        PackageRootAcquisitionRequest agnostic =
            PackageRootAcquisitionRequest.CreateFrameworkNeutral(
                PackageId,
                Version,
                "netstandard2.0");
        Assert.Null(agnostic.AcquisitionFramework);
        Assert.Equal("netstandard2.0", agnostic.SelectionTargetFramework);
        Assert.Null(agnostic.SelectionRuntimeIdentifier);
        Assert.Throws<ArgumentException>(
            () => PackageRootAcquisitionRequest.CreateFrameworkNeutral(
                PackageId,
                Version,
                " "));
    }

    static string Named(SparsePackageAssemblyRealization realization) =>
        Assert.IsType<ArtifactAssemblyQueryOutcome<string>.Validated>(
            realization.ExecuteAssemblyQuery(
                (session, _) => session.IdentityNames().Name,
                TestContext.Current.CancellationToken)).Value;

    static async Task<SparsePackageAssemblyRealization> ProjectAsync(
        InspectionWorkspace workspace,
        PackageRootBinding binding) =>
        Assert.IsType<SparsePackageAssemblyProjectionOutcome.Available>(
            await workspace.ProjectSelectedPackageAssemblyAsync(
                binding,
                binding.Root.AssetSelection.Assets.Single(),
                new SparsePackageAssemblyProjectionOptions
                {
                    MaxSelectedEntryBytes = 4 * 1024 * 1024,
                    MaxAggregateRetainedImageBytes = 8 * 1024 * 1024,
                },
                TestContext.Current.CancellationToken)).Realization;

    static PackageRootReacquisitionRequest Request(
        string? acquisitionFramework,
        string? acquisitionRuntimeIdentifier,
        string? selectionTargetFramework,
        string? selectionRuntimeIdentifier,
        string? compileTargetFramework = null,
        bool usesCompatibleImplementationSelection = false,
        bool allowsCompatibleTargetSelection = false,
        bool? hasSelectedImplementationUniverse = null)
        => RequestForProducer(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            acquisitionFramework,
            acquisitionRuntimeIdentifier,
            selectionTargetFramework,
            selectionRuntimeIdentifier,
            compileTargetFramework,
            usesCompatibleImplementationSelection,
            allowsCompatibleTargetSelection,
            hasSelectedImplementationUniverse);

    static PackageRootReacquisitionRequest RequestForProducer(
        string producer,
        string? acquisitionFramework,
        string? acquisitionRuntimeIdentifier,
        string? selectionTargetFramework,
        string? selectionRuntimeIdentifier,
        string? compileTargetFramework = null,
        bool usesCompatibleImplementationSelection = false,
        bool allowsCompatibleTargetSelection = false,
        bool? hasSelectedImplementationUniverse = null)
    {
        Assert.True(
            RealizedMemberCoordinate.Package.TryCreate(
                PackageId,
                Version,
                producer,
                acquisitionFramework,
                acquisitionRuntimeIdentifier,
                out RealizedMemberCoordinate.Package? coordinate,
                out string? problem),
            problem);
        return new PackageRootReacquisitionRequest(
            PackageArtifactRootRequest.Create(
                coordinate,
                compileTargetFramework ?? selectionTargetFramework,
                selectionTargetFramework,
                selectionRuntimeIdentifier,
                hasSelectedImplementationUniverse
                    ?? selectionTargetFramework is not null,
                usesCompatibleImplementationSelection,
                allowsCompatibleTargetSelection));
    }

    static string Token(params string?[] fields)
    {
        if (fields.Length == 9)
            fields = [.. fields, fields[8]];
        if (fields.Length == 10)
            fields = [.. fields, "selected"];

        return VersionedToken(
            PackageRootReacquisitionRequest.TokenPrefix,
            fields);
    }

    static string VersionedToken(
        string prefix,
        params string?[] fields)
    {
        var builder = new StringBuilder(prefix);
        foreach (string? field in fields)
        {
            builder.Append('.');
            if (string.IsNullOrEmpty(field))
                continue;

            builder.Append(
                Convert.ToBase64String(Encoding.UTF8.GetBytes(field))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_'));
        }

        return builder.ToString();
    }

    static string LegacyToken(params string?[] fields)
    {
        var builder = new StringBuilder("pkgroot1");
        foreach (string? field in fields)
        {
            builder.Append('.');
            if (string.IsNullOrEmpty(field))
                continue;

            builder.Append(
                Convert.ToBase64String(Encoding.UTF8.GetBytes(field))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_'));
        }

        return builder.ToString();
    }

    static WorkspaceContextLoadOptions Options(
        HttpClient client,
        IPackageStore store,
        IPackageSourceAuthorization? authorization = null) =>
        new()
        {
            HttpClient = client,
            SourceAuthorization = authorization
                ?? new UniformPackageSourceAuthorization([NuGetOrg]),
            PackageStore = store,
        };

    static async Task<IPackageStore> CachedStoreAsync(byte[] nupkg)
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            new MemoryStream(nupkg),
            TestContext.Current.CancellationToken);
        return store;
    }

    static byte[] LibraryPackage(string framework = Framework)
    {
        byte[] image = IntegrationAssembly();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using Stream stream = archive
                .CreateEntry($"lib/{framework}/{AssemblyName}.dll")
                .Open();
            stream.Write(image, 0, image.Length);
        }

        return buffer.ToArray();
    }

    static byte[] SplitPackage(string implementationFramework)
    {
        byte[] image = IntegrationAssembly();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (Stream reference = archive
                .CreateEntry($"ref/net10.0/{AssemblyName}.dll")
                .Open())
            {
                reference.Write(image);
            }
            using (Stream implementation = archive
                .CreateEntry(
                    $"lib/{implementationFramework}/{AssemblyName}.dll")
                .Open())
            {
                implementation.Write(image);
            }
        }

        return buffer.ToArray();
    }

    static byte[] IntegrationAssembly()
    {
        var assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName(AssemblyName),
            typeof(object).Assembly);
        ModuleBuilder module =
            assemblyBuilder.DefineDynamicModule(AssemblyName);
        TypeBuilder type = module.DefineType(
            "SampleType",
            TypeAttributes.Public | TypeAttributes.Class);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        type.CreateType();

        using var stream = new MemoryStream();
        assemblyBuilder.Save(stream);
        return stream.ToArray();
    }

    sealed class DenyingAuthorization : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId) =>
            PackageSourceAuthorization.Deny(
                "no producer is authorized in this test host");
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }
}
