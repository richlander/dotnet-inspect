using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Packages;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.PlatformHouse.Packages.Tests;

internal sealed record TestSourceBehavior(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Versions,
    IReadOnlyDictionary<PackageSourceCoordinate, IReadOnlyList<KeyValuePair<string, byte[]>>> Packages,
    PackageSourceFailureKind? VersionFailure = null,
    PackageSourceFailureKind? PayloadFailure = null,
    Func<CancellationToken, Task>? BeforeVersions = null,
    Func<CancellationToken, Task>? BeforePayload = null)
{
    internal static TestSourceBehavior Create(
        string packageId,
        IReadOnlyList<string>? versions = null,
        IReadOnlyList<KeyValuePair<string, byte[]>>? entries = null,
        string version = PackagePlatformTestEnvironment.Version,
        PackageSourceFailureKind? versionFailure = null,
        PackageSourceFailureKind? payloadFailure = null,
        Func<CancellationToken, Task>? beforeVersions = null,
        Func<CancellationToken, Task>? beforePayload = null)
    {
        IReadOnlyDictionary<PackageSourceCoordinate, IReadOnlyList<KeyValuePair<string, byte[]>>> packages =
            entries is null
                ? new Dictionary<PackageSourceCoordinate, IReadOnlyList<KeyValuePair<string, byte[]>>>()
                : new Dictionary<PackageSourceCoordinate, IReadOnlyList<KeyValuePair<string, byte[]>>>
                {
                    [PackageSourceCoordinate.Create(packageId, version)] = entries,
                };
        return new(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [packageId] = versions ?? [version],
            },
            packages,
            versionFailure,
            payloadFailure,
            beforeVersions,
            beforePayload);
    }

    internal static TestSourceBehavior CreatePackages(
        params (
            string PackageId,
            string Version,
            IReadOnlyList<KeyValuePair<string, byte[]>> Entries)[] packages) =>
        new(
            packages
                .GroupBy(
                    static package => package.PackageId,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group => group.Key,
                    static group =>
                        (IReadOnlyList<string>)[.. group.Select(
                            static package => package.Version)],
                StringComparer.OrdinalIgnoreCase),
            packages.ToDictionary(
                static package => PackageSourceCoordinate.Create(
                    package.PackageId,
                    package.Version),
                static package => package.Entries));
}

internal sealed class PackagePlatformTestEnvironment : IAsyncDisposable
{
    internal const string RuntimePackageId = "microsoft.netcore.app.ref";
    internal const string AspNetPackageId = "microsoft.aspnetcore.app.ref";
    internal const string RuntimeImplementationPackageId =
        "microsoft.netcore.app.runtime.linux-x64";
    internal const string AspNetImplementationPackageId =
        "microsoft.aspnetcore.app.runtime.linux-x64";
    internal const string Version = "11.0.0-rc.1.26425.128";
    private readonly IReadOnlyList<IPackageSourceClient> _ownedClients;
    private bool _settled;

    private PackagePlatformTestEnvironment(
        TestAuthorization authorization,
        PackageSourceSettlementLease root,
        IReadOnlyList<TestSourceClient> clients,
        IReadOnlyList<IPackageSourceClient> ownedClients,
        IPackageStore store)
    {
        Authorization = authorization;
        Root = root;
        Clients = clients;
        _ownedClients = ownedClients;
        Store = store;
    }

    internal TestAuthorization Authorization { get; }
    internal PackageSourceSettlementLease Root { get; }
    internal IReadOnlyList<TestSourceClient> Clients { get; }
    internal IPackageStore Store { get; set; }

    internal static PackagePlatformTestEnvironment Create(
        IReadOnlyList<TestSourceBehavior> behaviors,
        IPackageStore? store = null,
        IEnumerable<string>? deniedPackageIds = null)
    {
        PackageSource[] sources =
        [
            .. behaviors.Select((_, index) => new PackageSource(
                $"source-{index + 1}",
                $"https://source-{index + 1}.example/v3/index.json")),
        ];
        PackageSourceAuthorization authorized = PackageSourceAuthorization.Authorize(sources);
        var clientsByAssociation =
            new Dictionary<PackageSourceAssociation, IPackageSourceClient>(
                ReferenceEqualityComparer.Instance);
        var clients = new List<TestSourceClient>();
        for (int index = 0; index < authorized.Authorities.Count; index++)
        {
            ConfiguredPackageAuthority authority = authorized.Authorities[index];
            TestSourceClient? client = null;
            IPackageSourceClient owned = PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetV3(
                    $"source-{index + 1}",
                    $"Source {index + 1}",
                    authority.HttpEndpoint!),
                authority.Association,
                factory =>
                {
                    client = new TestSourceClient(factory, behaviors[index]);
                    return client;
                });
            clientsByAssociation.Add(authority.Association, owned);
            clients.Add(client!);
        }

        PackageSourceSettlementLease root = PackageSourceSettlementService.IssueLease(
            authority => clientsByAssociation.TryGetValue(
                authority.Association,
                out IPackageSourceClient? client)
                ? client
                : throw new InvalidOperationException("Unknown source association."));
        return new(
            new TestAuthorization(authorized, deniedPackageIds),
            root,
            clients,
            [.. clientsByAssociation.Values],
            store ?? new InMemoryPackageStore());
    }

    internal PackagePlatformSource CreateSource(PackagePlatformSourceLimits? limits = null) =>
        new(
            Authorization,
            new PackagePayloadAcquisitionPlan((_, _) => Store),
            limits);

    internal PackageSourceOperationLease IssueOperation(
        CancellationToken cancellationToken = default,
        TimeSpan? requestTimeout = null,
        TimeSpan? operationTimeout = null) =>
        Root.IssueOperationLease(
            cancellationToken,
            requestTimeout ?? TimeSpan.FromSeconds(30),
            operationTimeout ?? TimeSpan.FromSeconds(30));

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
        foreach (IPackageSourceClient client in _ownedClients)
            client.Dispose();
    }
}

internal sealed class TestAuthorization(
    PackageSourceAuthorization authorized,
    IEnumerable<string>? deniedPackageIds = null) : IPackageSourceAuthorization
{
    private readonly HashSet<string> _denied =
        new(deniedPackageIds ?? [], StringComparer.OrdinalIgnoreCase);

    internal List<string> Requests { get; } = [];

    public PackageSourceAuthorization AuthorizeSourcesFor(string packageId)
    {
        Requests.Add(packageId);
        return _denied.Contains(packageId)
            ? PackageSourceAuthorization.Deny("The package ID is denied.")
            : authorized;
    }
}

internal sealed class TestSourceClient(
    PackageSourceResultFactory factory,
    TestSourceBehavior behavior) : IPackageSourceClient
{
    public PackageSourceResultIdentity Source => factory.Source;

    public PackageSourceCapabilities Capabilities =>
        PackageSourceCapabilities.VersionEnumeration
        | PackageSourceCapabilities.PackagePayload;

    internal int VersionRequests { get; private set; }
    internal int PayloadRequests { get; private set; }

    public async Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        VersionRequests++;
        if (behavior.BeforeVersions is not null)
            await behavior.BeforeVersions(cancellationToken);
        if (behavior.VersionFailure is { } failure)
            return factory.FailedVersions(failure);

        behavior.Versions.TryGetValue(packageId, out IReadOnlyList<string>? versions);
        PackageCandidateObservation[] candidates =
        [
            .. (versions ?? []).Select(version => factory.Candidate(
                PackageSourceCoordinate.Create(packageId, version),
                PackageDiscoveryContract.CompleteVersionEnumeration,
                PackageListingState.Listed)),
        ];
        return factory.SucceededVersions(
            factory.Versions(candidates, hasAuthoritativeListingState: true));
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PayloadRequests++;
        if (behavior.BeforePayload is not null)
            await behavior.BeforePayload(cancellationToken);
        PackageSourceCoordinate coordinate = PackageSourceCoordinate.Create(packageId, version);
        if (behavior.PayloadFailure is { } failure)
            return factory.FailedPackage(coordinate, failure);
        if (!behavior.Packages.TryGetValue(coordinate, out var entries))
            return factory.FailedPackage(coordinate, PackageSourceFailureKind.NotFound);

        byte[] archive = PackagePlatformTestData.Archive(entries);
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

internal sealed class TrackingPackageContent(
    string producerKey,
    IReadOnlyList<KeyValuePair<string, byte[]>> entries,
    IEnumerable<string>? throwOnOpen = null,
    Func<string, Exception>? openFailure = null) : IPackageContent, IPackageContentEntryManifest
{
    private readonly byte[] _archive = PackagePlatformTestData.Archive(entries);
    private readonly HashSet<string> _throwOnOpen =
        new(throwOnOpen ?? [], StringComparer.Ordinal);
    private bool _retired;

    internal List<string> OpenedEntries { get; } = [];
    public string? RootPath => null;
    public string? NupkgPath => null;
    public bool FromCache => true;
    public string ProducerKey { get; } = producerKey;
    public bool RequiresArchiveTreeMatch => false;

    internal void Retire() => _retired = true;

    public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
    {
        ThrowIfRetired();
        stream = new MemoryStream(_archive, writable: false);
        return true;
    }

    public bool TryOpenEntry(
        string relativePath,
        [NotNullWhen(true)] out Stream? stream) =>
        TryOpenEntry(relativePath, long.MaxValue, out stream);

    public bool TryOpenEntry(
        string relativePath,
        long maxExpandedBytes,
        [NotNullWhen(true)] out Stream? stream)
    {
        ThrowIfRetired();
        OpenedEntries.Add(relativePath);
        if (_throwOnOpen.Contains(relativePath))
        {
            throw openFailure?.Invoke(relativePath)
                ?? new InvalidDataException(
                    $"Entry {relativePath} must not be opened.");
        }
        KeyValuePair<string, byte[]> entry =
            entries.SingleOrDefault(pair => pair.Key == relativePath);
        if (entry.Key is null || entry.Value.LongLength > maxExpandedBytes)
        {
            stream = null;
            return false;
        }
        stream = new MemoryStream(entry.Value, writable: false);
        return true;
    }

    public IEnumerable<string> EnumerateEntries()
    {
        ThrowIfRetired();
        return entries.Select(static pair => pair.Key);
    }

    public bool TryGetEntryLength(string relativePath, out long length)
    {
        ThrowIfRetired();
        KeyValuePair<string, byte[]> entry =
            entries.SingleOrDefault(pair => pair.Key == relativePath);
        length = entry.Key is null ? 0 : entry.Value.LongLength;
        return entry.Key is not null;
    }

    public IReadOnlyList<PackageContentEntry> EnumerateEntriesWithLengths()
    {
        ThrowIfRetired();
        return [.. entries.Select(static pair =>
            new PackageContentEntry(pair.Key, pair.Value.LongLength))];
    }

    public PackageContentEntryScanner CreateEntryScanner()
    {
        ThrowIfRetired();
        return PackageContentEntryScanner.From(
            [.. entries.Select(static pair =>
                new PackageContentEntry(
                    pair.Key,
                    pair.Value.LongLength))]);
    }

    private void ThrowIfRetired()
    {
        if (_retired)
            throw new ObjectDisposedException(nameof(TrackingPackageContent));
    }
}

internal sealed class StaticPackageStore(params IPackageContent[] contents) : IPackageStore
{
    public IPackageContent? TryGetCached(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys,
        Action<string>? log = null) =>
        contents.FirstOrDefault(content =>
            allowedSourceKeys?.Contains(content.ProducerKey, StringComparer.Ordinal) is true);

    public ValueTask<IPackageContent> CommitAsync(
        string packageName,
        string version,
        string sourceKey,
        Stream nupkg,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The static store must serve cached content.");
}

internal static class PackagePlatformTestData
{
    internal static KeyValuePair<string, byte[]> Entry(string path, byte[] content) =>
        KeyValuePair.Create(path, content);

    internal static byte[] Archive(IReadOnlyList<KeyValuePair<string, byte[]>> entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
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

    internal static byte[] Assembly(
        string name,
        Version? version = null,
        string metadataVersion = "v4.0.30319")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(name + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            version ?? new Version(1, 0, 0, 0),
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
        return Serialize(metadata, metadataVersion);
    }

    internal static byte[] Netmodule(string name)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(name + ".netmodule"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata, "v4.0.30319");
    }

    internal static AssemblyReferenceIdentity Identity(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader());
    }

    internal static async Task<byte[]> ReadAllAsync(PackageReferenceLibrary library)
    {
        await using Stream stream = library.OpenRead();
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    internal static async Task<byte[]> ReadAllAsync(
        PackageImplementationLibrary library)
    {
        await using Stream stream = library.OpenRead();
        using var output = new MemoryStream();
        await stream.CopyToAsync(
            output,
            TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    internal static IReadOnlyList<KeyValuePair<string, byte[]>>
        RuntimePackEntries(
            string frameworkName,
            byte[] runtimeConfiguration,
            byte[] dependencyManifest,
            params (string FileName, byte[] Content)[] members) =>
        RuntimePackEntries(
            frameworkName,
            "net11.0",
            runtimeConfiguration,
            dependencyManifest,
            members);

    internal static IReadOnlyList<KeyValuePair<string, byte[]>>
        RuntimePackEntries(
            string frameworkName,
            string targetFramework,
            byte[] runtimeConfiguration,
            byte[] dependencyManifest,
            params (string FileName, byte[] Content)[] members)
    {
        string prefix =
            $"runtimes/linux-x64/lib/{targetFramework}/";
        return
        [
            Entry(
                prefix + frameworkName + ".runtimeconfig.json",
                runtimeConfiguration),
            Entry(
                prefix + frameworkName + ".deps.json",
                dependencyManifest),
            .. members.Select(
                member => Entry(
                    prefix + member.FileName,
                    member.Content)),
        ];
    }

    internal static byte[] RuntimeConfiguration(
        params (
            string Name,
            string Version,
            string RollForward)[] frameworks)
    {
        object runtimeOptions = frameworks.Length switch
        {
            0 => new { },
            1 => new
            {
                framework = new
                {
                    name = frameworks[0].Name,
                    version = frameworks[0].Version,
                    rollForward = frameworks[0].RollForward,
                },
            },
            _ => new
            {
                frameworks = frameworks.Select(
                    static framework => new
                    {
                        name = framework.Name,
                        version = framework.Version,
                        rollForward = framework.RollForward,
                    }),
            },
        };
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { runtimeOptions });
    }

    internal static byte[] DependencyManifest(params string[] assets) =>
        DependencyManifestForTarget(
            ".NETCoreApp,Version=v11.0/linux-x64",
            assets);

    internal static byte[] DependencyManifestForTarget(
        string runtimeTargetName,
        params string[] assets)
    {
        var runtime = assets.ToDictionary(
            static asset => asset,
            static _ => new { },
            StringComparer.Ordinal);
        var library = new Dictionary<string, object>
        {
            ["Fixture/1.0.0"] = new { runtime },
        };
        var targets = new Dictionary<string, object>
        {
            [runtimeTargetName] = library,
        };
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                runtimeTarget = new
                {
                    name = runtimeTargetName,
                },
                targets,
            });
    }

    private static byte[] Serialize(MetadataBuilder metadata, string metadataVersion)
    {
        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, metadataVersion, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }
}
