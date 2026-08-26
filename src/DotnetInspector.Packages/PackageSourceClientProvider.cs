using DotnetInspector.Core;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// Adapts desktop host transports to typed package-source clients.
/// </summary>
/// <remarks>
/// <c>PackageSourceClientProvider_SelectsHostTransportOnlyForSharedClient</c>
/// gates production per-origin selection and injected-client preservation.
/// </remarks>
internal static class PackageSourceClientProvider
{
    internal static IPackageSourceClient Create(
        PackageSource source,
        HttpClient client,
        NuGetFetchOptions? fetchOptions = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(client);

        if (source is RoutedPackageSource route)
        {
            List<IPackageSourceClient> transports = [];
            TimeSpan operationTimeout = TimeSpan.MaxValue;
            foreach (PackageSource transport in route.Transports)
            {
                HttpClient routeTransport =
                    SelectTransport(transport, client);
                NuGetFetchOptions options =
                    fetchOptions ?? FetchOptionsFor(routeTransport);
                transports.Add(
                    PackageSourceClientFactory.Create(
                        transport,
                        routeTransport,
                        options));
                if (options.OperationTimeout < operationTimeout)
                    operationTimeout = options.OperationTimeout;
            }

            return new FailoverPackageSourceClient(
                transports,
                operationTimeout);
        }

        HttpClient selectedTransport =
            SelectTransport(source, client);
        return PackageSourceClientFactory.Create(
            source,
            selectedTransport,
            fetchOptions ?? FetchOptionsFor(selectedTransport));
    }

    internal static HttpClient SelectTransport(
        PackageSource source,
        HttpClient client) =>
        HttpClientFactory.IsSharedClient(client)
            ? HttpClientFactory.GetPackageSourceTransport(
                source.Url,
                ProducerIdentity(source.Url)?.Value ?? source.Url)
            : client;

    internal static NuGetFetchOptions FetchOptionsFor(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Timeout == Timeout.InfiniteTimeSpan
            ? new NuGetFetchOptions()
            : NuGetFetchOptions.FromRequestTimeout(client.Timeout);
    }

    internal static string ProducerKey(PackageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ProducerKey(source.Url);
    }

    internal static string ProducerKey(string sourceUrl)
    {
        ArgumentNullException.ThrowIfNull(sourceUrl);
        PackageSourceIdentity? identity = ProducerIdentity(sourceUrl);
        return identity is null
            ? NuGetCache.GetSourceKey(sourceUrl)
            : NuGetCache.GetSourceKey(identity);
    }

    internal static PackageSourceIdentity? ProducerIdentity(
        string sourceUrl) =>
        Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? endpoint)
        && endpoint.Scheme is "http" or "https"
            ? PackageSourceIdentity.ForProducerEndpoint(endpoint)
            : null;

    internal static void RecordFailure(
        PackageSource source,
        PackageSourceFailure failure,
        NetworkTrafficKind trafficKind)
    {
        using var trafficScope = NetworkTelemetry.Scope(trafficKind);
        FeedFailureTelemetry.Record(
            source.Url,
            failure.StatusCode);
    }
}

internal sealed record RoutedPackageSource : PackageSource
{
    internal RoutedPackageSource(IReadOnlyList<PackageSource> transports)
        : this(First(transports), transports)
    {
    }

    private RoutedPackageSource(
        PackageSource first,
        IReadOnlyList<PackageSource> transports)
        : base(
            first.Name,
            first.Url,
            first.Credential)
    {
        Transports = [.. transports];
    }

    internal IReadOnlyList<PackageSource> Transports { get; }

    private static PackageSource First(
        IReadOnlyList<PackageSource> transports)
    {
        ArgumentNullException.ThrowIfNull(transports);
        return transports.Count > 0
            ? transports[0]
            : throw new ArgumentException(
                "A routed package source requires a transport.",
                nameof(transports));
    }
}

internal sealed class FailoverPackageSourceClient
    : IPackageSourceClient
{
    private readonly IReadOnlyList<IPackageSourceClient> _transports;
    private readonly TimeSpan _operationTimeout;

    internal FailoverPackageSourceClient(
        IReadOnlyList<IPackageSourceClient> transports,
        TimeSpan? operationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(transports);
        if (transports.Count == 0)
        {
            throw new ArgumentException(
                "A failover package source requires a transport.",
                nameof(transports));
        }

        Identity = transports[0].Identity;
        if (transports.Any(transport => transport.Identity != Identity))
        {
            throw new ArgumentException(
                "Every failover transport must represent the same producer.",
                nameof(transports));
        }

        _transports = [.. transports];
        _operationTimeout =
            operationTimeout ?? NuGetFetchOptions.DefaultOperationTimeout;
    }

    public PackageSourceIdentity Identity { get; }

    public PackageSourceKind Kind => _transports[0].Kind;

    public PackageSourceCapabilities Capabilities =>
        _transports.Aggregate(
            PackageSourceCapabilities.None,
            static (capabilities, transport) =>
                capabilities | transport.Capabilities);

    public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (transport, routeToken) => transport.SearchAsync(
                query,
                take,
                prerelease,
                routeToken),
            PackageSourceCapabilities.Search,
            cancellationToken);

    public Task<PackageSourceOperationResult<PackageSearchResult>>
        SearchByPrefixAsync(
            string prefix,
            int take = 100,
            bool prerelease = false,
            CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (transport, routeToken) => transport.SearchByPrefixAsync(
                prefix,
                take,
                prerelease,
                routeToken),
            PackageSourceCapabilities.Search,
            cancellationToken);

    public Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (transport, routeToken) => transport.GetVersionsAsync(
                packageId,
                routeToken),
            PackageSourceCapabilities.VersionEnumeration,
            cancellationToken);

    public Task<PackageSourceOperationResult<PackageSourceManifest>>
        GetManifestAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return ExecuteAsync(
            (transport, routeToken) => transport.GetManifestAsync(
                coordinate.PackageId,
                coordinate.Version,
                routeToken),
            PackageSourceCapabilities.Manifest,
            cancellationToken,
            coordinate,
            stopOnNotFound: true);
    }

    public Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default) =>
        ExecutePayloadAsync(
            (transport, routeToken) => transport.GetPackageAsync(
                packageId,
                version,
                routeToken),
            PackageSourceCapabilities.PackagePayload,
            PackageSourceCoordinate.Create(packageId, version),
            cancellationToken);

    public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default) =>
        ExecutePayloadAsync(
            (transport, routeToken) => transport.TryGetSymbolsAsync(
                packageId,
                version,
                routeToken),
            PackageSourceCapabilities.SymbolPayload,
            PackageSourceCoordinate.Create(packageId, version),
            cancellationToken);

    public void Dispose()
    {
        foreach (IPackageSourceClient transport in _transports)
            transport.Dispose();
    }

    private async Task<PackageSourceOperationResult<T>> ExecuteAsync<T>(
        Func<IPackageSourceClient, CancellationToken,
            Task<PackageSourceOperationResult<T>>> operation,
        PackageSourceCapabilities capability,
        CancellationToken cancellationToken,
        PackageSourceCoordinate? coordinate = null,
        bool stopOnNotFound = false)
    {
        using CancellationTokenSource routeCancellation =
            CreateRouteCancellation(cancellationToken);
        PackageSourceOperationResult<T>.Failed? lastFailure = null;
        try
        {
            foreach (IPackageSourceClient transport in _transports)
            {
                routeCancellation.Token.ThrowIfCancellationRequested();
                PackageSourceOperationResult<T> result =
                    await operation(
                        transport,
                        routeCancellation.Token).ConfigureAwait(false);
                routeCancellation.Token.ThrowIfCancellationRequested();
                if (result
                    is PackageSourceOperationResult<T>.Succeeded)
                {
                    return result;
                }

                lastFailure =
                    (PackageSourceOperationResult<T>.Failed)result;
                if (stopOnNotFound
                    && lastFailure.Failure.Kind
                        == PackageSourceFailureKind.NotFound)
                {
                    return lastFailure;
                }
            }
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "NuGet operation was canceled by the caller.",
                exception,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (routeCancellation.IsCancellationRequested)
        {
            return TimeoutFailure<T>(capability, coordinate);
        }

        return lastFailure!;
    }

    private async Task<PackageSourceOperationResult<PackageSourcePayload>>
        ExecutePayloadAsync(
            Func<IPackageSourceClient, CancellationToken,
                Task<PackageSourceOperationResult<PackageSourcePayload>>> operation,
            PackageSourceCapabilities capability,
            PackageSourceCoordinate coordinate,
            CancellationToken cancellationToken)
    {
        CancellationTokenSource? routeCancellation =
            CreateRouteCancellation(cancellationToken);
        Exception? rejectedPayloadCleanupFailure = null;
        try
        {
            PackageSourceOperationResult<PackageSourcePayload>.Failed?
                lastFailure = null;
            foreach (IPackageSourceClient transport in _transports)
            {
                routeCancellation.Token.ThrowIfCancellationRequested();
                PackageSourceOperationResult<PackageSourcePayload> result =
                    await operation(
                        transport,
                        routeCancellation.Token).ConfigureAwait(false);
                if (routeCancellation.IsCancellationRequested)
                {
                    if (result
                        is PackageSourceOperationResult<PackageSourcePayload>
                            .Succeeded lateSuccess)
                    {
                        try
                        {
                            await lateSuccess.Value.Content.DisposeAsync()
                                .ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            rejectedPayloadCleanupFailure = exception;
                        }
                    }

                    routeCancellation.Token.ThrowIfCancellationRequested();
                }

                if (result
                    is PackageSourceOperationResult<PackageSourcePayload>
                        .Succeeded success)
                {
                    PackageSourcePayload payload = success.Value with
                    {
                        Content = new RouteDeadlineStream(
                            success.Value.Content,
                            routeCancellation,
                            cancellationToken,
                            _operationTimeout),
                    };
                    routeCancellation = null;
                    return new PackageSourceOperationResult<
                        PackageSourcePayload>.Succeeded(payload);
                }

                lastFailure =
                    (PackageSourceOperationResult<PackageSourcePayload>
                        .Failed)result;
                if (lastFailure.Failure.Kind
                    == PackageSourceFailureKind.NotFound)
                {
                    return lastFailure;
                }
            }

            return lastFailure!;
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            Exception inner = rejectedPayloadCleanupFailure is null
                ? exception
                : new AggregateException(
                    "Cleanup of a payload rejected after cancellation failed.",
                    exception,
                    rejectedPayloadCleanupFailure);
            throw new OperationCanceledException(
                "NuGet operation was canceled by the caller.",
                inner,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (routeCancellation!.IsCancellationRequested)
        {
            return TimeoutFailure<PackageSourcePayload>(
                capability,
                coordinate,
                rejectedPayloadCleanupFailure is not null);
        }
        finally
        {
            routeCancellation?.Dispose();
        }
    }

    private CancellationTokenSource CreateRouteCancellation(
        CancellationToken cancellationToken)
    {
        CancellationTokenSource source =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        source.CancelAfter(_operationTimeout);
        return source;
    }

    private PackageSourceOperationResult<T> TimeoutFailure<T>(
        PackageSourceCapabilities capability,
        PackageSourceCoordinate? coordinate = null,
        bool cleanupFailed = false) =>
        new PackageSourceOperationResult<T>.Failed(
            new PackageSourceFailure(
                Identity,
                Kind,
                capability,
                coordinate,
                PackageSourceFailureKind.Timeout,
                "The package source operation exceeded its configured deadline."
                + (cleanupFailed
                    ? " Cleanup of a payload rejected after the deadline also failed."
                    : string.Empty)));

    private sealed class RouteDeadlineStream(
        Stream inner,
        CancellationTokenSource routeCancellation,
        CancellationToken callerToken,
        TimeSpan operationTimeout)
        : Stream
    {
        private int _disposed;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(
            CancellationToken cancellationToken) =>
            InvokeAsync(() => inner.FlushAsync(cancellationToken));

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            Invoke(() => inner.Read(buffer, offset, count));

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            InvokeAsync(
                () => inner.ReadAsync(
                    buffer,
                    offset,
                    count,
                    cancellationToken));

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            InvokeValueAsync(
                () => inner.ReadAsync(buffer, cancellationToken));

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            inner.SetLength(value);

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            Invoke(
                () => inner.Write(buffer, offset, count));

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            InvokeAsync(
                () => inner.WriteAsync(
                    buffer,
                    offset,
                    count,
                    cancellationToken));

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            InvokeValueAsync(
                () => inner.WriteAsync(buffer, cancellationToken));

        protected override void Dispose(bool disposing)
        {
            if (disposing
                && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try
                {
                    inner.Dispose();
                }
                finally
                {
                    routeCancellation.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                await inner.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                routeCancellation.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        private T Invoke<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (OperationCanceledException exception)
            {
                throw Translate(exception);
            }
        }

        private void Invoke(Action operation)
        {
            try
            {
                operation();
            }
            catch (OperationCanceledException exception)
            {
                throw Translate(exception);
            }
        }

        private async Task InvokeAsync(Func<Task> operation)
        {
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                throw Translate(exception);
            }
        }

        private async Task<T> InvokeAsync<T>(
            Func<Task<T>> operation)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                throw Translate(exception);
            }
        }

        private async ValueTask InvokeValueAsync(
            Func<ValueTask> operation)
        {
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                throw Translate(exception);
            }
        }

        private async ValueTask<T> InvokeValueAsync<T>(
            Func<ValueTask<T>> operation)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                throw Translate(exception);
            }
        }

        private Exception Translate(
            OperationCanceledException exception)
        {
            if (callerToken.IsCancellationRequested)
            {
                return new OperationCanceledException(
                    "NuGet operation was canceled by the caller.",
                    exception,
                    callerToken);
            }

            return routeCancellation.IsCancellationRequested
                ? new NuGetOperationTimeoutException(
                    operationTimeout,
                    exception)
                : exception;
        }
    }
}
