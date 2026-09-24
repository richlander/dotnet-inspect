using System.Diagnostics.CodeAnalysis;
using System.Net;
using BinaryFetch;
using ZipFetch;

namespace NuGetFetch;

/// <summary>
/// One opened archive: its directory, the validator the source supplied, and
/// entry reads that run under the same operation context, credential, and
/// source identity as the open. Dispose it before the operation ends; a
/// reader owns no response stream after each call returns.
/// </summary>
public sealed class PackageArchiveReader : IAsyncDisposable
{
    private readonly PackageArchiveRangeSession _session;
    private readonly HttpRangeSource _source;
    private bool _disposed;

    internal PackageArchiveReader(
        PackageArchiveRangeSession session,
        HttpRangeSource source,
        ZipDirectory directory,
        ZipReadLimits limits)
    {
        _session = session;
        _source = source;
        Directory = directory;
        Limits = limits;
        Validator = source.Validator;
    }

    public PackageSourceCoordinate Coordinate => _session.Coordinate;

    public PackageSourceResultIdentity Source => _session.Source;

    public ZipDirectory Directory { get; }

    /// <summary>The validator the source supplied; <see cref="RangeValidatorKind.None"/> means identity across requests is unverified.</summary>
    public RangeValidatorKind Validator { get; }

    public ZipReadLimits Limits { get; }

    /// <summary>
    /// Reads and expands one entry under the caller's expanded bound (the
    /// limits' per-entry bound when <see langword="null"/>). The read runs
    /// under the operation context the open established, so
    /// <paramref name="cancellationToken"/> must be the open's caller token
    /// or <see langword="default"/>; a different token is rejected, as every
    /// operation under a shared context rejects it.
    /// </summary>
    public async Task<PackageArchiveReadResult<PackageArchiveEntryContent>> ReadEntryAsync(
        ZipEntry entry,
        long? maxExpandedBytes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancellationToken token = _session.ResolveInvocationToken(cancellationToken);
        _session.BeginEntryReads();
        try
        {
            byte[] content = await ZipArchiveReader.ReadEntryAsync(
                _source,
                Directory,
                entry,
                Limits,
                maxExpandedBytes,
                token).ConfigureAwait(false);
            return new PackageArchiveReadResult<PackageArchiveEntryContent>(
                new PackageArchiveEntryContent(entry, content));
        }
        catch (Exception exception)
            when (_session.TryMap(
                exception,
                token,
                out PackageArchiveReadResult<PackageArchiveEntryContent>? mapped))
        {
            // A failure that raced the caller's cancellation is cancellation.
            token.ThrowIfCancellationRequested();
            return mapped;
        }
    }

    /// <summary>
    /// Reads and expands several entries with as few ranged requests as their
    /// placement and the limits' merge gap allow, up to the limits' request
    /// concurrency, under a bound on their expansion together. Every entry is
    /// checked before any transfer; the result is all the entries, in the
    /// order requested, or one failure or refusal and no content. The token
    /// rule is <see cref="ReadEntryAsync"/>'s. <paramref name="entryMergeGap"/>
    /// replaces the limits' merge gap for this read, up to
    /// <see cref="ZipReadLimits.MaxEntryMergeGap"/>.
    /// </summary>
    public async Task<PackageArchiveReadResult<IReadOnlyList<PackageArchiveEntryContent>>> ReadEntriesAsync(
        IReadOnlyList<ZipEntry> entries,
        long? maxTotalExpandedBytes = null,
        int? entryMergeGap = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancellationToken token = _session.ResolveInvocationToken(cancellationToken);
        _session.BeginEntryReads();
        try
        {
            IReadOnlyList<byte[]> contents = await ZipArchiveReader.ReadEntriesAsync(
                _source,
                Directory,
                entries,
                Limits,
                maxTotalExpandedBytes,
                entryMergeGap,
                token).ConfigureAwait(false);
            var results = new PackageArchiveEntryContent[entries.Count];
            for (int i = 0; i < results.Length; i++)
                results[i] = new PackageArchiveEntryContent(entries[i], contents[i]);
            return new PackageArchiveReadResult<IReadOnlyList<PackageArchiveEntryContent>>(results);
        }
        catch (Exception exception)
            when (_session.TryMap(
                exception,
                token,
                out PackageArchiveReadResult<IReadOnlyList<PackageArchiveEntryContent>>? mapped))
        {
            // A failure that raced the caller's cancellation is cancellation.
            token.ThrowIfCancellationRequested();
            return mapped;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _source.DisposeAsync().ConfigureAwait(false);
        _session.Dispose();
    }
}

/// <summary>
/// A client instance's memory that its source ignored <c>Range</c>. The
/// client's lifetime belongs to the host's composition, so a later
/// composition probes again.
/// </summary>
internal sealed class PackageArchiveRangeMemory
{
    private int _rangeIgnored;

    public bool RangeIgnored => Volatile.Read(ref _rangeIgnored) != 0;

    public void MarkRangeIgnored() => Volatile.Write(ref _rangeIgnored, 1);
}

/// <summary>
/// The adapter core shared by the v3 and gallery clients: opens an archive
/// by reading its directory over an <see cref="HttpRangeSource"/> whose
/// every request goes through the existing retry and per-request deadline
/// mechanics, and maps the range and ZIP vocabularies into
/// <see cref="PackageArchiveReadResult{T}"/>.
/// </summary>
internal static class PackageArchiveRangeAccess
{
    public static async Task<PackageArchiveReadResult<PackageArchiveReader>> OpenAsync(
        PackageSourceResultFactory results,
        PackageSourceCoordinate coordinate,
        PackageArchiveRangeMemory memory,
        HttpClient client,
        TimeSpan clientTimeout,
        NuGetFetchOptions options,
        Func<NuGetOperationDeadline, Task<(string Url, PackageSourceCredential? Credential)>> resolveArchive,
        ZipReadLimits limits,
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext,
        PackageArchiveRequestLog? requestLog = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (memory.RangeIgnored)
        {
            return new PackageArchiveReadResult<PackageArchiveReader>(
                PackageArchiveReadRefusal.RangeIgnored);
        }

        cancellationToken = operationContext?.ResolveInvocationToken(cancellationToken)
            ?? cancellationToken;
        bool ownsContext = operationContext is null;
        NuGetOperationContext context = operationContext
            ?? new NuGetOperationContext(
                options.RequestTimeout,
                options.OperationTimeout,
                cancellationToken);
        var session = new PackageArchiveRangeSession(
            results,
            coordinate,
            memory,
            client,
            clientTimeout,
            context,
            ownsContext,
            requestLog);
        HttpRangeSource? source = null;
        try
        {
            (string url, PackageSourceCredential? credential) = await resolveArchive(
                context.CreateDeadline(clientTimeout, cancellationToken, results.Source))
                .ConfigureAwait(false);
            if (!NuGetHttpRequest.TryCreatePreservingPathAndQuery(url, out Uri? archiveUri))
                throw new InvalidDataException("The package archive URL is not a well-formed absolute URI.");
            session.UseCredential(credential);
            source = new HttpRangeSource(archiveUri!, session.SendAsync);
            ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(
                source,
                limits,
                cancellationToken).ConfigureAwait(false);
            return new PackageArchiveReadResult<PackageArchiveReader>(
                new PackageArchiveReader(session, source, directory, limits));
        }
        catch (Exception exception)
            when (session.TryMap(
                exception,
                cancellationToken,
                out PackageArchiveReadResult<PackageArchiveReader>? mapped))
        {
            if (source is not null)
                await source.DisposeAsync().ConfigureAwait(false);
            session.Dispose();
            // A failure that raced the caller's cancellation is cancellation.
            cancellationToken.ThrowIfCancellationRequested();
            return mapped;
        }
        catch
        {
            if (source is not null)
                await source.DisposeAsync().ConfigureAwait(false);
            session.Dispose();
            throw;
        }
    }
}

/// <summary>
/// The per-open state: the operation context every ranged request runs
/// under, the credential and browser options applied to each request, and
/// the failure mapping.
/// </summary>
internal sealed class PackageArchiveRangeSession : IDisposable
{
    private readonly PackageSourceResultFactory _results;
    private readonly PackageArchiveRangeMemory _memory;
    private readonly HttpClient _client;
    private readonly TimeSpan _clientTimeout;
    private readonly NuGetOperationContext _context;
    private readonly bool _ownsContext;
    private readonly bool _isBrowser = OperatingSystem.IsBrowser();
    private readonly PackageArchiveRequestLog? _requestLog;
    private PackageSourceCredential? _credential;
    private volatile bool _readingEntries;
    private bool _disposed;

    public PackageArchiveRangeSession(
        PackageSourceResultFactory results,
        PackageSourceCoordinate coordinate,
        PackageArchiveRangeMemory memory,
        HttpClient client,
        TimeSpan clientTimeout,
        NuGetOperationContext context,
        bool ownsContext,
        PackageArchiveRequestLog? requestLog = null)
    {
        _results = results;
        _requestLog = requestLog;
        Coordinate = coordinate;
        _memory = memory;
        _client = client;
        _clientTimeout = clientTimeout;
        _context = context;
        _ownsContext = ownsContext;
    }

    public PackageSourceCoordinate Coordinate { get; }

    /// <summary>The credential the resolved archive endpoint takes, applied to every ranged request.</summary>
    public void UseCredential(PackageSourceCredential? credential) => _credential = credential;

    public PackageSourceResultIdentity Source => _results.Source;

    /// <summary>Every later request reads entry spans rather than the directory.</summary>
    public void BeginEntryReads() => _readingEntries = true;

    public CancellationToken ResolveInvocationToken(CancellationToken cancellationToken) =>
        _context.ResolveInvocationToken(cancellationToken);

    /// <summary>
    /// Sends one ranged request exactly as the full fetch sends its request:
    /// a fresh message per attempt with the credential and browser options
    /// applied, under retry and a per-request deadline whose stream wrapper
    /// translates an expired deadline into the typed timeout. The returned
    /// response carries that wrapped stream, so the range source's body
    /// reads stay under the same deadline.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NuGetOperationDeadline deadline = _context.CreateDeadline(
            _clientTimeout,
            cancellationToken,
            Source);
        (Stream stream, HttpResponseMessage response) =
            await NuGetHttpRetry.RunStreamingRequestAsync(
                deadline,
                async requestToken =>
                {
                    PackageArchiveRequestLog.Entry? logged = BeginLogged(request);
                    HttpResponseMessage sent;
                    try
                    {
                        HttpRequestMessage attempt = Clone(request);
                        NuGetSourceRequest.ApplyCredential(attempt, _credential);
                        NuGetHttpRequest.ConfigureBrowserRequest(attempt, _isBrowser);
                        sent = await _client.SendAsync(
                            attempt,
                            HttpCompletionOption.ResponseHeadersRead,
                            requestToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        logged?.Settle(PackageArchiveRequestOutcome.Failed, null);
                        throw;
                    }
                    try
                    {
                        long? advertised = sent.Content.Headers.ContentLength;
                        // A transient status is a retryable failure here, as
                        // the full fetch's EnsureSuccessStatusCode makes it;
                        // every other status reaches the range source, which
                        // decides what 200, 206, and the rest mean.
                        if (IsTransientStatus(sent.StatusCode))
                        {
                            logged?.Settle(PackageArchiveRequestOutcome.Failed, advertised);
                            throw new HttpRequestException(
                                $"The package source answered a ranged request with status {(int)sent.StatusCode}.",
                                inner: null,
                                sent.StatusCode);
                        }

                        Stream body = await sent.Content
                            .ReadAsStreamAsync(requestToken)
                            .ConfigureAwait(false);
                        if (logged is not null)
                        {
                            if (ClassifyStatus(sent.StatusCode, request) is { } settled)
                                logged.Settle(settled, advertised);
                            body = new PackageArchiveCountingStream(body, logged, advertised);
                        }
                        return (body, (IDisposable)sent, sent);
                    }
                    catch
                    {
                        logged?.Settle(PackageArchiveRequestOutcome.Failed, null);
                        sent.Dispose();
                        throw;
                    }
                }).ConfigureAwait(false);

        var wrapped = new HttpResponseMessage(response.StatusCode)
        {
            Version = response.Version,
            ReasonPhrase = response.ReasonPhrase,
        };
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
            wrapped.Headers.TryAddWithoutValidation(header.Key, header.Value);
        var content = new StreamContent(stream);
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        wrapped.Content = content;
        return wrapped;
    }

    /// <summary>
    /// Maps a failure into the capability's outcome, or reports it as one
    /// to rethrow (caller cancellation and unclassified exceptions).
    /// </summary>
    public bool TryMap<T>(
        Exception exception,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out PackageArchiveReadResult<T>? mapped)
        where T : class
    {
        mapped = null;
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            return false;

        switch (exception)
        {
            case RangeFetchException range:
                switch (range.Failure)
                {
                    case RangeFetchFailure.RangeIgnored:
                        _memory.MarkRangeIgnored();
                        mapped = new(PackageArchiveReadRefusal.RangeIgnored);
                        return true;
                    case RangeFetchFailure.RepresentationChanged:
                        mapped = new(PackageArchiveReadRefusal.ArchiveChanged);
                        return true;
                    case RangeFetchFailure.InvalidResponse:
                        mapped = Failed<T>(PackageSourceFailureKind.InvalidResponse);
                        return true;
                    default:
                        mapped = Failed<T>(range.StatusCode switch
                        {
                            HttpStatusCode.NotFound => PackageSourceFailureKind.NotFound,
                            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                                PackageSourceFailureKind.AuthenticationRequired,
                            _ => PackageSourceFailureKind.Transport,
                        });
                        return true;
                }

            case ZipReadException zip:
                switch (zip.Failure)
                {
                    case ZipReadFailure.OverBound:
                        mapped = Failed<T>(PackageSourceFailureKind.ResponseRejected);
                        return true;
                    case ZipReadFailure.Unsupported:
                        mapped = new(PackageArchiveReadRefusal.ArchiveUnsupported);
                        return true;
                    default:
                        mapped = Failed<T>(PackageSourceFailureKind.InvalidResponse);
                        return true;
                }

            case PackageSourceStreamException stream:
                // The per-request deadline wrapper already classified a
                // body-read failure (an expired deadline is Timeout).
                mapped = Failed<T>(stream.Kind);
                return true;

            default:
                if (PackageSourceOperation.TryClassify(
                        exception,
                        allowNotFound: true,
                        out PackageSourceFailureKind kind))
                {
                    mapped = Failed<T>(kind);
                    return true;
                }

                return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsContext)
            _context.Dispose();
    }

    private PackageArchiveReadResult<T> Failed<T>(PackageSourceFailureKind kind)
        where T : class =>
        new(_results.FailedPackage(Coordinate, kind).Failure!);

    private PackageArchiveRequestLog.Entry? BeginLogged(HttpRequestMessage request)
    {
        if (_requestLog is null)
            return null;
        System.Net.Http.Headers.RangeItemHeaderValue? range =
            request.Headers.Range?.Ranges.FirstOrDefault();
        long? start = range?.From;
        long length = range is null
            ? 0
            : start is null
                ? range.To ?? 0
                : (range.To ?? start.Value) - start.Value + 1;
        PackageArchiveRequestPurpose purpose = _readingEntries
            ? PackageArchiveRequestPurpose.EntrySpan
            : start is null
                ? PackageArchiveRequestPurpose.DirectoryTail
                : PackageArchiveRequestPurpose.DirectoryHead;
        return _requestLog.Begin(purpose, start, length);
    }

    /// <summary>
    /// The outcome a non-partial status settles at once; a partial response
    /// settles when its body is disposed.
    /// </summary>
    private static PackageArchiveRequestOutcome? ClassifyStatus(
        HttpStatusCode status,
        HttpRequestMessage request) =>
        status switch
        {
            HttpStatusCode.PartialContent => null,
            // A 200 to a conditional request means the representation
            // changed; the body is not read.
            HttpStatusCode.OK when request.Headers.IfRange is not null =>
                PackageArchiveRequestOutcome.Abandoned,
            HttpStatusCode.OK => PackageArchiveRequestOutcome.RangeIgnored,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                PackageArchiveRequestOutcome.Refused,
            HttpStatusCode.NotFound => PackageArchiveRequestOutcome.NotFound,
            _ => PackageArchiveRequestOutcome.Failed,
        };

    /// <summary>The statuses the retry policy treats as transient when carried by an <see cref="HttpRequestException"/>.</summary>
    private static bool IsTransientStatus(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };
        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (KeyValuePair<string, object?> option in request.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        return clone;
    }
}
