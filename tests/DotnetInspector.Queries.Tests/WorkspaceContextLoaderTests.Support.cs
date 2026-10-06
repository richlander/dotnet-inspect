using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    static WorkspaceContextLoadOptions Options(
        HttpClient client,
        IPackageStore store,
        IEmbeddedContentProvider? embeddedContent = null,
        IPackageSourceAuthorization? sourceAuthorization = null,
        PackagePayloadLimits? payloadLimits = null,
        Action<string>? log = null,
        IPackagePayloadTransferPolicy? packageTransferPolicy = null) =>
        new()
        {
            HttpClient = client,
            SourceAuthorization = sourceAuthorization
                ?? new UniformPackageSourceAuthorization([NuGetOrg]),
            PackageStore = store,
            PackageTransferPolicy = packageTransferPolicy,
            EmbeddedContent = embeddedContent,
            PayloadLimits = payloadLimits ?? PackagePayloadLimits.Default,
            Log = log,
        };

    static string Producer(PackageSource source) =>
        NuGetCache.GetSourceKey(source.Url);

    static string PortableProducer(PackageSource source)
    {
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                source,
                PackageSourceAssociation.Create());
        return client.Source.Producer.PortableKey;
    }

    static WorkspaceMemberCoordinate PackageMember(string? version) =>
        WorkspaceMemberCoordinate.Package(PackageId, version);

    static WorkspaceMemberCoordinate EmbeddedMember(byte[] content) =>
        WorkspaceMemberCoordinate.Embedded(
            "bundle/lookalike.dll",
            Digest(content),
            Path.GetFileNameWithoutExtension(EmbeddedPath));

    static string Digest(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    static async Task<IPackageStore> CachedStoreAsync(
        string version,
        byte[] nupkg)
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            new MemoryStream(nupkg),
            TestContext.Current.CancellationToken);
        return store;
    }

    static byte[] LibraryPackage() =>
        Archive(
            ($"lib/{Framework}/{Path.GetFileName(CallerPath)}",
                File.ReadAllBytes(CallerPath)),
            ($"lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetPath)),
            ($"lib/{Framework}/de/{Path.GetFileNameWithoutExtension(TargetPath)}.resources.dll",
                File.ReadAllBytes(TargetPath)),
            ("build/Sample.props", "<Project />"u8.ToArray()));

    static byte[] CallerPackage() =>
        Archive(
            ($"lib/{Framework}/{Path.GetFileName(CallerPath)}",
                File.ReadAllBytes(CallerPath)));

    static byte[] TargetPackage() =>
        Archive(
            ($"lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetPath)));

    static byte[] TargetV2Package() =>
        Archive(
            ($"lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetV2Path)));

    static byte[] RuntimeSpecificPackage() =>
        Archive(
            ($"lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetV2Path)),
            ($"runtimes/browser-wasm/lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetPath)));

    static byte[] RuntimePack() =>
        Archive(
            ($"runtimes/linux-x64/lib/{Framework}/{Path.GetFileName(CallerPath)}",
                File.ReadAllBytes(CallerPath)),
            ($"runtimes/linux-x64/lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetPath)));

    static byte[] CreateMalformedMetadataRootImage()
    {
        // A real PE image whose CLI metadata directory size is zeroed, so the
        // metadata root cannot be mapped. A non-PE byte string is not a
        // substitute: it has no metadata root at all and is classified as a
        // descriptor-less image well before admission runs.
        byte[] image = File.ReadAllBytes(TargetPath);
        int corHeaderStart;
        using (var peReader = new PEReader(ImmutableArray.Create(image)))
        {
            corHeaderStart = peReader.PEHeaders.CorHeaderStartOffset;
        }

        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(corHeaderStart + 12, sizeof(int)),
            0);
        return image;
    }

    static byte[] CreateNoMetadataImage()
    {
        byte[] image = File.ReadAllBytes(TargetPath);
        using var peReader = new PEReader(ImmutableArray.Create(image));
        PEHeader peHeader = peReader.PEHeaders.PEHeader!;
        int directoryBase =
            peReader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        image.AsSpan(directoryBase + (14 * 8), 8).Clear();
        return image;
    }

    static byte[] CreateUnsupportedMetadataImage()
    {
        const int fixedMetadataRootPrefixLength = 16;
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Unsupported.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Unsupported"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                "WindowsRuntime 1.4;CLR v4.0.30319",
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var imageBuilder = new BlobBuilder();
        peBuilder.Serialize(imageBuilder);
        byte[] image = imageBuilder.ToArray();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(
                peReader.PEHeaders.CorHeaderStartOffset + 12,
                sizeof(int)),
            fixedMetadataRootPrefixLength + versionLength);
        return image;
    }

    static Version? IdentityVersion(string assemblyPath) =>
        ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local("fixture identity"))
            .Identity.Version;

    static byte[] ReplaceAscii(
        byte[] source,
        string oldValue,
        string newValue)
    {
        Assert.Equal(oldValue.Length, newValue.Length);
        byte[] result = [.. source];
        ReadOnlySpan<byte> oldBytes = Encoding.UTF8.GetBytes(oldValue);
        ReadOnlySpan<byte> newBytes = Encoding.UTF8.GetBytes(newValue);
        int replacements = 0;
        for (int offset = 0;
            offset <= result.Length - oldBytes.Length;)
        {
            int relative = result.AsSpan(offset).IndexOf(oldBytes);
            if (relative < 0)
                break;

            offset += relative;
            newBytes.CopyTo(result.AsSpan(offset, newBytes.Length));
            replacements++;
            offset += newBytes.Length;
        }

        Assert.NotEqual(0, replacements);
        return result;
    }

    static byte[] Archive(params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string entryPath, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(entryPath).Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return buffer.ToArray();
    }

    static WorkspaceContextLoadOutcome.Loaded Loaded(
        WorkspaceContextLoadOutcome outcome)
        => Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(outcome);

    static WorkspaceContextLoadOutcome.Failed Failed(
        WorkspaceContextLoadOutcome outcome)
        => Assert.IsType<WorkspaceContextLoadOutcome.Failed>(outcome);

    static AssemblyContextParticipant Participant(
        WorkspaceContextLoadOutcome.Loaded loaded,
        string assemblyPath)
        => loaded.Group.Participants.Single(participant =>
            string.Equals(
                participant.Assembly.Identity.Name,
                Path.GetFileNameWithoutExtension(assemblyPath),
                StringComparison.Ordinal));

    /// <summary>
    /// Counts the groups a workspace owns. The workspace publishes no group
    /// census, and these tests assert that a rejected context creates no
    /// partial group, which is only observable from the inside.
    /// </summary>
    static int GroupCount(InspectionWorkspace workspace)
    {
        FieldInfo field =
            typeof(InspectionWorkspace).GetField(
                "_groups",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "InspectionWorkspace._groups was not found.");
        return ((System.Collections.ICollection)field.GetValue(workspace)!)
            .Count;
    }

    sealed class StubEmbeddedContent(byte[]? content)
        : IEmbeddedContentProvider
    {
        int _openCount;

        internal int OpenCount => _openCount;

        public bool TryOpenContent(
            string contentRef,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? stream)
        {
            _openCount++;
            if (content is null)
            {
                stream = null;
                return false;
            }

            stream = new MemoryStream(content, writable: false);
            return true;
        }
    }

    static void AssertTransferPolicy(
        RecordingTransferPolicy policy,
        string packageId,
        string version)
    {
        Assert.Equal(packageId, policy.Transfer?.Coordinate.PackageId);
        Assert.Equal(version, policy.Transfer?.Coordinate.Version);
        Assert.True(policy.Completed);
        Assert.True(policy.Disposed);
    }

    sealed class PayloadHandler(
        byte[] nupkg,
        string version,
        string packageId = PackageId)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(
                request.RequestUri!.ToString().Equals(
                    $"https://api.nuget.org/v3-flatcontainer/{packageId}/{version}/{packageId}.{version}.nupkg",
                    StringComparison.OrdinalIgnoreCase)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    sealed class RecordingTransferPolicy : IPackagePayloadTransferPolicy
    {
        public PackagePayloadTransfer? Transfer { get; private set; }
        public bool Completed { get; private set; }
        public bool Disposed { get; private set; }

        public ValueTask<IPackagePayloadReservation> ReserveAsync(
            PackagePayloadTransfer transfer,
            CancellationToken cancellationToken = default)
        {
            Transfer = transfer;
            return ValueTask.FromResult<IPackagePayloadReservation>(
                new Reservation(this));
        }

        sealed class Reservation(RecordingTransferPolicy owner)
            : IPackagePayloadReservation
        {
            public void Complete() => owner.Completed = true;

            public void Dispose() => owner.Disposed = true;
        }
    }

    sealed class ListingHandler(byte[] nupkg, string listedVersion)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{PackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json($$"""{"versions":["{{listedVersion}}","2.0.0"]}""");
            }

            if (url.Equals(
                $"https://api.nuget.org/v3/registration5-gz-semver2/{PackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json(
                    $$$"""
                    {"items":[{"items":[
                      {"catalogEntry":{"version":"{{{listedVersion}}}","listed":true}},
                      {"catalogEntry":{"version":"2.0.0","listed":false}}
                    ]}]}
                    """);
            }

            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{PackageId}/{listedVersion}/{PackageId}.{listedVersion}.nupkg",
                StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    });
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));

            static Task<HttpResponseMessage> Json(string body) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
        }
    }

    sealed class PlatformListingHandler(params string[] versions)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            string versionArray = string.Join(
                ",",
                versions.Select(version => $"\"{version}\""));
            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{RuntimePackPackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json($"{{\"versions\":[{versionArray}]}}");
            }

            if (url.Equals(
                $"https://api.nuget.org/v3/registration5-gz-semver2/{RuntimePackPackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                string entries = string.Join(
                    ",",
                    versions.Select(
                        version =>
                            $"{{\"catalogEntry\":{{\"version\":\"{version}\",\"listed\":true}}}}"));
                return Json($"{{\"items\":[{{\"items\":[{entries}]}}]}}");
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));

            static Task<HttpResponseMessage> Json(string body) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
        }
    }

    sealed class AlternatingPlatformListingHandler(params string[] versions)
        : HttpMessageHandler
    {
        int _listing = -1;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{RuntimePackPackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                int listing = Math.Min(
                    Interlocked.Increment(ref _listing),
                    versions.Length - 1);
                return Json(
                    $"{{\"versions\":[\"{versions[listing]}\"]}}");
            }

            if (url.Equals(
                $"https://api.nuget.org/v3/registration5-gz-semver2/{RuntimePackPackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                string version = versions[
                    Math.Clamp(
                        Volatile.Read(ref _listing),
                        0,
                        versions.Length - 1)];
                return Json(
                    "{\"items\":[{\"items\":[{\"catalogEntry\":"
                    + $"{{\"version\":\"{version}\",\"listed\":true}}"
                    + "}]}]}");
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));

            static Task<HttpResponseMessage> Json(string body) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
        }
    }

    sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// Two independent private feeds, each with its own service index and flat
    /// container, that serve only the payloads registered for them. Every
    /// request URL is recorded, so a test can assert which feed was asked about
    /// which package rather than only which answer came back.
    /// </summary>
    sealed class PerFeedHandler : HttpMessageHandler
    {
        readonly Dictionary<string, byte[]> _payloads =
            new(StringComparer.Ordinal);
        readonly Dictionary<string, string[]> _listings =
            new(StringComparer.Ordinal);
        readonly HashSet<string> _failedListings =
            new(StringComparer.Ordinal);
        readonly HashSet<string> _failedServiceIndexes =
            new(StringComparer.Ordinal);
        readonly HashSet<string> _withoutFlatContainer =
            new(StringComparer.Ordinal);
        readonly HashSet<string> _malformedFlatContainerSiblings =
            new(StringComparer.Ordinal);
        readonly List<string> _requests = [];

        internal IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                    return [.. _requests];
            }
        }

        internal void Serve(
            PackageSource feed,
            string packageId,
            string version,
            byte[] nupkg) =>
            _payloads[NupkgUrl(feed, packageId, version)] = nupkg;

        internal void List(
            PackageSource feed,
            string packageId,
            params string[] versions) =>
            _listings[
                $"{FlatContainer(feed)}{packageId}/index.json"] =
                versions;

        internal void FailListing(
            PackageSource feed,
            string packageId) =>
            _failedListings.Add(
                $"{FlatContainer(feed)}{packageId}/index.json");

        internal void FailServiceIndex(PackageSource feed) =>
            _failedServiceIndexes.Add(feed.Url);

        /// <summary>
        /// Answers this feed's service index without a flat-container resource,
        /// so its package resources cannot be discovered at all.
        /// </summary>
        internal void WithoutFlatContainer(PackageSource feed) =>
            _withoutFlatContainer.Add(feed.Url);

        internal void AddMalformedFlatContainerSibling(
            PackageSource feed) =>
            _malformedFlatContainerSiblings.Add(feed.Url);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            lock (_requests)
                _requests.Add(url);

            foreach (PackageSource feed in new[] { FeedA, FeedB })
            {
                if (url.Equals(feed.Url, StringComparison.Ordinal))
                {
                    if (_failedServiceIndexes.Contains(feed.Url))
                    {
                        return Task.FromResult(
                            new HttpResponseMessage(
                                HttpStatusCode.ServiceUnavailable));
                    }

                    string resources = _withoutFlatContainer.Contains(feed.Url)
                        ? ""
                        : $$"""{"@id":"{{FlatContainer(feed)}}","@type":"PackageBaseAddress/3.0.0"}""";
                    if (_malformedFlatContainerSiblings.Contains(feed.Url))
                    {
                        resources +=
                            """,{"@id":"not a url","@type":"PackageBaseAddress/3.0.0"}""";
                    }
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                $$"""
                                {"resources":[{{resources}}]}
                                """),
                        });
                }
            }

            return Task.FromResult(
                _failedListings.Contains(url)
                    ? new HttpResponseMessage(
                        HttpStatusCode.InternalServerError)
                    : _listings.TryGetValue(url, out string[]? versions)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $$"""
                            {"versions":[{{string.Join(
                                ",",
                                versions.Select(version => $"\"{version}\""))}}]}
                            """),
                    }
                    : _payloads.TryGetValue(url, out byte[]? nupkg)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        static string FlatContainer(PackageSource feed) =>
            $"{new Uri(feed.Url).GetLeftPart(UriPartial.Authority)}/flat/";

        static string NupkgUrl(
            PackageSource feed,
            string packageId,
            string version) =>
            $"{FlatContainer(feed)}{packageId}/{version}/{packageId}.{version}.nupkg";
    }

    /// <summary>
    /// A host policy that authorizes a different producer set for each package
    /// id, the shape NuGet package source mapping produces. An id it never
    /// heard of is authorized for nothing.
    /// </summary>
    sealed class PerPackageAuthorization
        : Dictionary<string, PackageSource[]>, IPackageSourceAuthorization
    {
        internal PerPackageAuthorization()
            : base(StringComparer.Ordinal)
        {
        }

        public PackageSourceAuthorization AuthorizeSourcesFor(string packageId)
            => TryGetValue(packageId, out PackageSource[]? sources)
                ? PackageSourceAuthorization.Authorize(sources)
                : PackageSourceAuthorization.Deny(
                    "No source is authorized for this package.");
    }

    /// <summary>
    /// Records how many times authorization was consulted, so a test can prove
    /// a front-door rejection happened before any host policy was asked.
    /// </summary>
    sealed class RecordingAuthorization : IPackageSourceAuthorization
    {
        int _requests;

        internal int Requests => Volatile.Read(ref _requests);

        public PackageSourceAuthorization AuthorizeSourcesFor(string packageId)
        {
            Interlocked.Increment(ref _requests);
            return PackageSourceAuthorization.Authorize([NuGetOrg]);
        }
    }

    /// <summary>
    /// Counts every call that reaches a store, so a test can prove a rejection
    /// happened before any cache read or commit rather than only that it
    /// happened.
    /// </summary>
    sealed class CountingPackageStore(IPackageStore inner) : IPackageStore
    {
        int _interactions;

        internal int Interactions => Volatile.Read(ref _interactions);

        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null)
        {
            Interlocked.Increment(ref _interactions);
            return inner.TryGetCached(
                packageName,
                version,
                allowedSourceKeys,
                log);
        }

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _interactions);
            return inner.CommitAsync(
                packageName,
                version,
                sourceKey,
                nupkg,
                cancellationToken);
        }
    }

    sealed class EntryCountingPackageStore(IPackageStore inner) : IPackageStore
    {
        int _entryOpens;

        internal int EntryOpens => Volatile.Read(ref _entryOpens);

        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            inner.TryGetCached(
                packageName,
                version,
                allowedSourceKeys,
                log) is { } content
                ? new EntryCountingPackageContent(
                    content,
                    () => Interlocked.Increment(ref _entryOpens))
                : null;

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default) =>
            inner.CommitAsync(
                packageName,
                version,
                sourceKey,
                nupkg,
                cancellationToken);
    }

    sealed class EntryCountingPackageContent(
        IPackageContent inner,
        Action onEntryOpen) : IPackageContent, IPackageContentEntryManifest
    {
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public bool RequiresArchiveTreeMatch =>
            inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? stream) =>
            inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
            string relativePath,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? stream)
        {
            onEntryOpen();
            return inner.TryOpenEntry(relativePath, out stream);
        }

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? stream)
        {
            onEntryOpen();
            return inner.TryOpenEntry(
                relativePath,
                maxExpandedBytes,
                out stream);
        }

        public IEnumerable<string> EnumerateEntries() =>
            inner.EnumerateEntries();

        public bool TryGetEntryLength(
            string relativePath,
            out long length) =>
            ((IPackageContentEntryManifest)inner)
                .TryGetEntryLength(relativePath, out length);

        public IReadOnlyList<PackageContentEntry>
            EnumerateEntriesWithLengths() =>
            ((IPackageContentEntryManifest)inner)
                .EnumerateEntriesWithLengths();

        public PackageContentEntryScanner CreateEntryScanner() =>
            ((IPackageContentEntryManifest)inner)
                .CreateEntryScanner();
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
