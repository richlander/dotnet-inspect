using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

internal sealed class WorkspaceReferenceSourceFixture : IAsyncDisposable
{
    internal const string RuntimePackageId = "microsoft.netcore.app.ref";
    internal const string Version = "11.0.0-rc.1.26425.128";
    internal const string TargetFramework = "net11.0";

    private readonly IPackageSourceClient _client;
    private bool _settled;

    private WorkspaceReferenceSourceFixture(
        PackageSourceAuthorization authorization,
        WorkspaceReferenceSourceClient client,
        IPackageSourceClient ownedClient,
        PackageSourceSettlementLease root,
        bool denied)
    {
        Authorization = new WorkspaceReferenceAuthorization(authorization, denied);
        Client = client;
        _client = ownedClient;
        Root = root;
        Store = new InMemoryPackageStore();
        Source = new PackagePlatformSource(
            Authorization,
            new PackagePayloadAcquisitionPlan((_, _) => Store));
    }

    internal WorkspaceReferenceAuthorization Authorization { get; }
    internal WorkspaceReferenceSourceClient Client { get; }
    internal PackageSourceSettlementLease Root { get; }
    internal InMemoryPackageStore Store { get; }
    internal PackagePlatformSource Source { get; }

    internal static WorkspaceReferenceSourceFixture Create(
        string producer,
        IReadOnlyList<KeyValuePair<string, byte[]>> entries,
        IReadOnlyList<string>? versions = null,
        PackageSourceFailureKind? payloadFailure = null,
        Func<CancellationToken, Task>? beforePayload = null,
        bool denied = false)
    {
        var packageSource = new PackageSource(
            producer,
            $"https://{producer}.example/v3/index.json");
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize([packageSource]);
        ConfiguredPackageAuthority authority = authorization.Authorities[0];
        WorkspaceReferenceSourceClient? client = null;
        IPackageSourceClient ownedClient = PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetV3(
                producer,
                producer,
                authority.HttpEndpoint!),
            authority.Association,
            factory =>
            {
                client = new WorkspaceReferenceSourceClient(
                    factory,
                    versions ?? [Version],
                    entries,
                    payloadFailure,
                    beforePayload);
                return client;
            });
        PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(candidate =>
                ReferenceEquals(candidate.Association, authority.Association)
                    ? ownedClient
                    : throw new InvalidOperationException("Unknown source association."));
        return new(authorization, client!, ownedClient, root, denied);
    }

    internal PackageSourceOperationLease IssueOperation(
        CancellationToken cancellationToken = default,
        TimeSpan? operationTimeout = null) =>
        Root.IssueOperationLease(
            cancellationToken,
            requestTimeout: TimeSpan.FromSeconds(30),
            operationTimeout: operationTimeout ?? TimeSpan.FromSeconds(30));

    internal async Task AssertSettledAsync()
    {
        ValueTask pending = Root.DisposeAsync();
        Assert.True(pending.IsCompletedSuccessfully);
        await pending;
        _settled = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_settled)
            await Root.DisposeAsync();
        _client.Dispose();
    }
}

internal sealed class WorkspaceReferenceAuthorization(
    PackageSourceAuthorization authorization,
    bool denied = false) : IPackageSourceAuthorization
{
    internal List<string> Requests { get; } = [];

    public PackageSourceAuthorization AuthorizeSourcesFor(string packageId)
    {
        Requests.Add(packageId);
        return denied
            ? PackageSourceAuthorization.Deny("The reference package is denied.")
            : authorization;
    }
}

internal sealed class WorkspaceReferenceSourceClient(
    PackageSourceResultFactory factory,
    IReadOnlyList<string> versions,
    IReadOnlyList<KeyValuePair<string, byte[]>> entries,
    PackageSourceFailureKind? payloadFailure,
    Func<CancellationToken, Task>? beforePayload) : IPackageSourceClient
{
    public PackageSourceResultIdentity Source => factory.Source;

    public PackageSourceCapabilities Capabilities =>
        PackageSourceCapabilities.VersionEnumeration
        | PackageSourceCapabilities.PackagePayload;

    internal int VersionRequests { get; private set; }
    internal int PayloadRequests { get; private set; }

    public Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        VersionRequests++;
        PackageCandidateObservation[] candidates = packageId.Equals(
            WorkspaceReferenceSourceFixture.RuntimePackageId,
            StringComparison.OrdinalIgnoreCase)
            ? [
                .. versions.Select(version => factory.Candidate(
                    PackageSourceCoordinate.Create(packageId, version),
                    PackageDiscoveryContract.CompleteVersionEnumeration,
                    PackageListingState.Listed)),
            ]
            : [];
        return Task.FromResult(factory.SucceededVersions(
            factory.Versions(candidates, hasAuthoritativeListingState: true)));
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PayloadRequests++;
        if (beforePayload is not null)
            await beforePayload(cancellationToken);
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        if (payloadFailure is { } failure)
            return factory.FailedPackage(coordinate, failure);
        if (!packageId.Equals(
                WorkspaceReferenceSourceFixture.RuntimePackageId,
                StringComparison.OrdinalIgnoreCase)
            || !version.Equals(
                WorkspaceReferenceSourceFixture.Version,
                StringComparison.Ordinal))
        {
            return factory.FailedPackage(
                coordinate,
                PackageSourceFailureKind.NotFound);
        }

        byte[] archive = WorkspaceReferenceTestData.Archive(entries);
        return factory.SucceededPackage(
            coordinate,
            factory.Payload(
                coordinate,
                PackageSourcePayloadKind.Package,
                new MemoryStream(archive, writable: false),
                archive.LongLength));
    }

    public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        throw new NotSupportedException();

    public Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
        string prefix,
        int take = 100,
        bool prerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        throw new NotSupportedException();

    public Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        throw new NotSupportedException();

    public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        throw new NotSupportedException();

    public void Dispose()
    {
    }
}

internal static class WorkspaceReferenceTestData
{
    internal static KeyValuePair<string, byte[]> Entry(string path, byte[] content) =>
        KeyValuePair.Create(path, content);

    internal static byte[] Assembly(
        string assemblyName,
        string namespaceName,
        string typeName,
        Version? version = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(assemblyName + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            version ?? new Version(11, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString(namespaceName),
            metadata.GetOrAddString(typeName),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    internal static AssemblyReferenceIdentity Identity(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    internal static byte[] Archive(
        IReadOnlyList<KeyValuePair<string, byte[]>> entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using Stream stream = entry.Open();
                stream.Write(content);
            }
        }
        return output.ToArray();
    }
}

internal sealed class WorkspaceReferenceCountingHandler : DelegatingHandler
{
    private int _requestCount;

    internal WorkspaceReferenceCountingHandler()
        : base(new SocketsHttpHandler
        {
            Credentials = null,
            PreAuthenticate = false,
            UseCookies = false,
        })
    {
    }

    internal int RequestCount => Volatile.Read(ref _requestCount);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        return base.SendAsync(request, cancellationToken);
    }
}
