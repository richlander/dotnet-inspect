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
    /// limits' per-entry bound when <see langword="null"/>).
    /// </summary>
    public async Task<PackageArchiveReadResult<PackageArchiveEntryContent>> ReadEntryAsync(
        ZipEntry entry,
        long? maxExpandedBytes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancellationToken token = _session.ResolveInvocationToken(cancellationToken);
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
        NuGetOperationContext? operationContext)
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
            ownsContext);
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
    private PackageSourceCredential? _credential;
    private bool _disposed;

    public PackageArchiveRangeSession(
        PackageSourceResultFactory results,
        PackageSourceCoordinate coordinate,
        PackageArchiveRangeMemory memory,
        HttpClient client,
        TimeSpan clientTimeout,
        NuGetOperationContext context,
        bool ownsContext)
    {
        _results = results;
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
                    HttpRequestMessage attempt = Clone(request);
                    NuGetSourceRequest.ApplyCredential(attempt, _credential);
                    NuGetHttpRequest.ConfigureBrowserRequest(attempt, _isBrowser);
                    HttpResponseMessage sent = await _client.SendAsync(
                        attempt,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestToken).ConfigureAwait(false);
                    try
                    {
                        Stream body = await sent.Content
                            .ReadAsStreamAsync(requestToken)
                            .ConfigureAwait(false);
                        return (body, (IDisposable)sent, sent);
                    }
                    catch
                    {
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
