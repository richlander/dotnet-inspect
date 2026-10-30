using System.Runtime.Versioning;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal sealed class BrowserPackageSourceCapabilities
{
    internal BrowserPackageSourceCapabilities(
        HttpClient httpClient,
        IPackageSourceAuthorization authorization)
    {
        HttpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
        Authorization = authorization
            ?? throw new ArgumentNullException(nameof(authorization));
    }

    internal HttpClient HttpClient { get; }

    internal IPackageSourceAuthorization Authorization { get; }
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserDependencyPackageSourceRuntime :
    IPackagePayloadTransferPolicy,
    IDisposable
{
    readonly object _gate = new();
    readonly HttpClient? _httpClient;
    readonly Dictionary<
        PackageSourceAssociation,
        SourceEntry> _entries =
            new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<
        string,
        BrowserPackageWorkspace.BrowserSessionPackageStore> _stores =
            new(StringComparer.Ordinal);
    bool _disposed;

    internal BrowserDependencyPackageSourceRuntime(
        BrowserPackageSourceCapabilities? capabilities)
    {
        if (capabilities is null)
        {
            Authorization = BrowserPackageWorkspace.SourceAuthorizationFor(
                BrowserPackageWorkspace.Gallery);
            BrowserPackageWorkspace.BrowserSessionPackageStore store =
                BrowserPackageWorkspace.StoreFor(
                    BrowserPackageWorkspace.Gallery);
            _entries.Add(
                BrowserPackageWorkspace.Gallery.Source.Association,
                new(
                    BrowserPackageWorkspace.Gallery,
                    store,
                    Owned: false));
            _stores.Add(
                BrowserPackageWorkspace.Gallery.Source.Producer.Key,
                store);
        }
        else
        {
            Authorization = capabilities.Authorization;
            _httpClient = capabilities.HttpClient;
        }
    }

    internal IPackageSourceAuthorization Authorization { get; }

    internal IPackageSourceClient ClientFor(
        ConfiguredPackageAuthority authority) =>
        EntryFor(authority).Client;

    internal BrowserPackageWorkspace.BrowserSessionPackageStore StoreFor(
        ConfiguredPackageAuthority authority) =>
        EntryFor(authority).Store;

    public ValueTask<IPackagePayloadReservation> ReserveAsync(
        PackagePayloadTransfer transfer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        BrowserPackageWorkspace.BrowserSessionPackageStore? store = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (SourceEntry entry in _entries.Values)
            {
                if (!entry.Store.ProducerKeysMatch(
                    transfer.ProducerKey,
                    entry.Client.Source.Producer.Key))
                {
                    continue;
                }
                if (store is not null
                    && !ReferenceEquals(store, entry.Store))
                {
                    throw new InvalidOperationException(
                        "The Browser dependency call graph matched one "
                            + "package transfer to multiple configured "
                            + "source stores.");
                }
                store ??= entry.Store;
            }
        }

        return (store
            ?? throw new InvalidOperationException(
                "The Browser dependency call graph did not establish a "
                    + "store for the package transfer source."))
            .ReserveAsync(transfer, cancellationToken);
    }

    SourceEntry EntryFor(ConfiguredPackageAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(
                authority.Association,
                out SourceEntry? existing))
            {
                return existing;
            }

            Uri endpoint = authority.HttpEndpoint
                ?? throw new InvalidOperationException(
                    "The Browser dependency call graph requires an "
                        + "HTTP or HTTPS package source.");
            var transport = new BorrowedHttpClientHandler(
                _httpClient
                ?? throw new InvalidOperationException(
                    "The Browser dependency call graph has no configured "
                        + "HTTP transport."));
            IPackageSourceClient client;
            try
            {
                client = PackageSourceClientFactory.Create(
                    authority.Source,
                    authority.Association,
                    transport,
                    new NuGetFetchOptions
                    {
                        RequestTimeout =
                            BrowserPackageWorkspace
                                .PackageChangesRequestTimeout,
                        OperationTimeout =
                            BrowserPackageWorkspace
                                .PackageChangesSourceOperationTimeout,
                    });
            }
            catch
            {
                transport.Dispose();
                throw;
            }

            SourceEntry created;
            try
            {
                if (!_stores.TryGetValue(
                    client.Source.Producer.Key,
                    out BrowserPackageWorkspace.BrowserSessionPackageStore?
                        store))
                {
                    store =
                        new BrowserPackageWorkspace.BrowserSessionPackageStore(
                            client,
                            Authorization,
                            PackageSourceIdentity.ForHttpEndpoint(endpoint));
                    _stores.Add(client.Source.Producer.Key, store);
                }
                created = new(
                    client,
                    store,
                    Owned: true);
                _entries.Add(authority.Association, created);
            }
            catch
            {
                client.Dispose();
                throw;
            }
            return created;
        }
    }

    public void Dispose()
    {
        SourceEntry[] entries;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            entries = [.. _entries.Values];
            _entries.Clear();
            _stores.Clear();
        }

        foreach (SourceEntry entry in entries)
        {
            if (entry.Owned)
                entry.Client.Dispose();
        }
    }

    sealed record SourceEntry(
        IPackageSourceClient Client,
        BrowserPackageWorkspace.BrowserSessionPackageStore Store,
        bool Owned);

    sealed class BorrowedHttpClientHandler(HttpClient client)
        : HttpMessageHandler
    {
        readonly HttpClient _client = client
            ?? throw new ArgumentNullException(nameof(client));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                throw new InvalidOperationException(
                    "The Browser package-source transport only supports "
                        + "content-free requests.");
            }

            using var forwarded = new HttpRequestMessage(
                request.Method,
                request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy,
            };
            foreach (KeyValuePair<string, IEnumerable<string>> header
                in request.Headers)
            {
                forwarded.Headers.TryAddWithoutValidation(
                    header.Key,
                    header.Value);
            }
            foreach (KeyValuePair<string, object?> option
                in request.Options)
            {
                ((IDictionary<string, object?>)forwarded.Options)[option.Key] =
                    option.Value;
            }

            HttpResponseMessage response = await _client.SendAsync(
                forwarded,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.RequestMessage = request;
            return response;
        }
    }
}
