using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace NuGetFetch;

internal sealed class NuGetOperationDeadline : IDisposable
{
    private readonly NuGetOperationContext _context;
    private readonly bool _ownsContext;
    private readonly PackageSourceResultIdentity? _source;
    private readonly CancellationToken _operationToken;
    private readonly TimeSpan _requestTimeout;
    private bool _disposed;
    private bool _ownershipTransferred;

    public NuGetOperationDeadline(
        NuGetFetchOptions options,
        TimeSpan clientTimeout,
        CancellationToken callerToken)
        : this(
            options,
            clientTimeout,
            callerToken,
            source: null)
    {
    }

    internal NuGetOperationDeadline(
        NuGetFetchOptions options,
        TimeSpan clientTimeout,
        CancellationToken callerToken,
        PackageSourceResultIdentity? source)
        : this(
            new NuGetOperationContext(
                options.RequestTimeout,
                options.OperationTimeout,
                callerToken),
            clientTimeout,
            source,
            ownsContext: true)
    {
    }

    internal NuGetOperationDeadline(
        NuGetOperationContext context,
        TimeSpan clientTimeout,
        PackageSourceResultIdentity? source)
        : this(
            context,
            clientTimeout,
            source,
            ownsContext: false)
    {
    }

    private NuGetOperationDeadline(
        NuGetOperationContext context,
        TimeSpan clientTimeout,
        PackageSourceResultIdentity? source,
        bool ownsContext)
    {
        _context = context;
        _ownsContext = ownsContext;
        _source = source;
        _operationToken = context.OperationToken;
        _requestTimeout = NuGetFetchOptions.RequestTimeoutForClient(
            new NuGetFetchOptions
            {
                RequestTimeout = context.RequestTimeout,
                OperationTimeout = context.OperationTimeout,
            },
            clientTimeout);
    }

    public async Task<T> RunRequestAsync<T>(
        Func<CancellationToken, Task<T>> request)
    {
        ThrowIfExpired();
        long requestStarted = Stopwatch.GetTimestamp();
        using CancellationTokenSource requestCancellation =
            CreateRequestCancellation();
        try
        {
            T result = await request(requestCancellation.Token)
                .ConfigureAwait(false);
            try
            {
                ThrowIfRequestExpired(
                    requestStarted,
                    requestCancellation);
                return result;
            }
            catch
            {
                NuGetRejectedResult.RejectIfOwned(result);
                throw;
            }
        }
        catch (OperationCanceledException ex)
        {
            ThrowTranslated(ex, requestCancellation, requestStarted);
            ThrowTransportTimeout(ex);
            throw;
        }
        catch (NuGetMetadataBodyTimeoutException ex)
            when (IsAnyDeadlineExpired(
                requestStarted,
                requestCancellation))
        {
            ThrowTranslated(
                new OperationCanceledException(
                    "NuGet metadata body deadline expired after an outer deadline.",
                    ex,
                    requestCancellation.Token),
                requestCancellation,
                requestStarted);
            throw;
        }
        catch (Exception ex)
            when (IsDeadlineEligibleFailure(ex)
                && IsAnyDeadlineExpired(requestStarted, requestCancellation))
        {
            ThrowTranslatedAbort(
                ex,
                requestCancellation,
                requestStarted);
            throw;
        }
    }

    public void ThrowIfExpired()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context.ThrowIfExpiredForActiveOperation();
    }

    public async Task DelayAsync(TimeSpan delay)
    {
        ThrowIfExpired();
        using CancellationTokenSource delayCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                _operationToken);
        try
        {
            await Task.Delay(
                delay,
                delayCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            if (_context.CancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "NuGet operation was canceled by the caller.",
                    exception,
                    _context.CancellationToken);
            }

            throw new NuGetOperationTimeoutException(
                _context.OperationTimeout,
                exception);
        }

        ThrowIfExpired();
    }

    public async Task<Stream> RunStreamingRequestAsync(
        Func<CancellationToken, Task<(Stream Stream, IDisposable Owner)>> request)
    {
        (Stream stream, _) = await RunStreamingRequestAsync(
            async cancellationToken =>
            {
                (Stream responseStream, IDisposable owner) =
                    await request(cancellationToken).ConfigureAwait(false);
                return (responseStream, owner, Metadata: false);
            }).ConfigureAwait(false);
        return stream;
    }

    public async Task<(Stream Stream, T Metadata)> RunStreamingRequestAsync<T>(
        Func<CancellationToken, Task<(
            Stream Stream,
            IDisposable Owner,
            T Metadata)>> request)
    {
        ThrowIfExpired();
        long requestStarted = Stopwatch.GetTimestamp();
        CancellationTokenSource requestCancellation =
            CreateRequestCancellation();
        try
        {
            (Stream stream, IDisposable owner, T metadata) =
                await request(requestCancellation.Token).ConfigureAwait(false);
            if (IsAnyDeadlineExpired(requestStarted, requestCancellation))
            {
                Exception? disposalFailure = null;
                try
                {
                    owner.Dispose();
                }
                catch (Exception ex)
                {
                    disposalFailure = ex;
                }

                throw new OperationCanceledException(
                    "NuGet response arrived after its deadline expired.",
                    disposalFailure,
                    requestCancellation.Token);
            }

            _ownershipTransferred = true;
            return (
                new DeadlineStream(
                    stream,
                    owner,
                    requestCancellation,
                    this,
                    requestStarted),
                metadata);
        }
        catch (OperationCanceledException ex)
        {
            try
            {
                ThrowTranslated(
                    ex,
                    requestCancellation,
                    requestStarted);
                ThrowTransportTimeout(ex);
            }
            finally
            {
                requestCancellation.Dispose();
            }

            throw;
        }
        catch (Exception ex)
            when (IsDeadlineEligibleFailure(ex)
                && IsAnyDeadlineExpired(requestStarted, requestCancellation))
        {
            try
            {
                ThrowTranslatedAbort(
                    ex,
                    requestCancellation,
                    requestStarted);
            }
            finally
            {
                requestCancellation.Dispose();
            }

            throw;
        }
        catch
        {
            requestCancellation.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed || _ownershipTransferred)
            return;

        _disposed = true;
        if (_ownsContext)
            _context.Complete();
    }

    private CancellationTokenSource CreateRequestCancellation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                _operationToken);
        cancellation.CancelAfter(_requestTimeout);
        return cancellation;
    }

    private void ThrowIfRequestExpired(
        long requestStarted,
        CancellationTokenSource requestCancellation)
    {
        if (!IsAnyDeadlineExpired(requestStarted, requestCancellation))
            return;

        var cancellation = new OperationCanceledException(
            "NuGet request completed after its deadline expired.",
            requestCancellation.Token);
        ThrowTranslated(
            cancellation,
            requestCancellation,
            requestStarted);
        throw cancellation;
    }

    private bool IsAnyDeadlineExpired(
        long requestStarted,
        CancellationTokenSource requestCancellation) =>
        _context.CancellationToken.IsCancellationRequested
        || IsOperationExpired()
        || requestCancellation.IsCancellationRequested
        || Stopwatch.GetElapsedTime(requestStarted) >= _requestTimeout;

    private bool IsOperationExpired() => _context.IsOperationExpired;

    internal TimeSpan RequestTimeout => _requestTimeout;

    private void ThrowTranslated(
        OperationCanceledException exception,
        CancellationTokenSource requestCancellation,
        long requestStarted)
    {
        if (_context.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "NuGet operation was canceled by the caller.",
                exception,
                _context.CancellationToken);
        }

        if (IsOperationExpired())
        {
            throw new NuGetOperationTimeoutException(
                _context.OperationTimeout,
                exception);
        }

        if (requestCancellation.IsCancellationRequested
            || Stopwatch.GetElapsedTime(requestStarted) >= _requestTimeout)
        {
            throw new NuGetRequestTimeoutException(
                _requestTimeout,
                exception);
        }
    }

    private void ThrowTranslatedAbort(
        Exception exception,
        CancellationTokenSource requestCancellation,
        long requestStarted)
    {
        var cancellation = new OperationCanceledException(
            "NuGet request was aborted after its deadline expired.",
            exception,
            requestCancellation.Token);
        ThrowTranslated(
            cancellation,
            requestCancellation,
            requestStarted);
    }

    private static bool IsDeadlineAbort(Exception exception) =>
        exception is IOException
            and not NuGetMetadataResponseTooLargeException
            and not NuGetRedirectLimitExceededException
            and not NuGetRegistrationResourceLimitExceededException
            or HttpRequestException
            or ObjectDisposedException;

    private static void ThrowTransportTimeout(
        OperationCanceledException exception)
    {
        if (NuGetTransportFailure.GetTimeout(exception) is { } timeout)
            ExceptionDispatchInfo.Capture(timeout).Throw();
    }

    internal static bool IsDeadlineEligibleFailure(Exception exception) =>
        IsDeadlineAbort(exception)
        || exception is TimeoutException
            or InvalidDataException
            or System.Text.Json.JsonException
            or NuGetSourceResponseException;

    private sealed class DeadlineStream : Stream
    {
        private readonly Stream inner;
        private readonly IDisposable owner;
        private readonly CancellationTokenSource requestCancellation;
        private readonly NuGetOperationDeadline operation;
        private readonly long requestStarted;
        private readonly CancellationToken _requestToken;
        private readonly CancellationTokenRegistration _deadlineRegistration;
        private readonly TaskCompletionSource _abortCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Exception? _abortDisposalFailure;
        private int _abortStarted;
        private int _deadlineCompleted;
        private int _disposeStarted;
        private int _endOfStream;

        public DeadlineStream(
            Stream inner,
            IDisposable owner,
            CancellationTokenSource requestCancellation,
            NuGetOperationDeadline operation,
            long requestStarted)
        {
            this.inner = inner;
            this.owner = owner;
            this.requestCancellation = requestCancellation;
            this.operation = operation;
            this.requestStarted = requestStarted;
            _requestToken = requestCancellation.Token;
            _deadlineRegistration = _requestToken.UnsafeRegister(
                static state => ((DeadlineStream)state!).AbortOwner(),
                this);
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfCallerDisposed();
            ThrowIfDeadlineExpired();
            if (count == 0)
                return 0;

            try
            {
                int read = inner.Read(buffer, offset, count);
                ThrowIfCallerDisposed();
                ThrowIfDeadlineExpired();
                if (read == 0 && count > 0)
                {
                    Volatile.Write(ref _endOfStream, 1);
                    CompleteDeadline();
                }
                return read;
            }
            catch (Exception ex) when (IsStreamReadFailure(ex))
            {
                if (IsDeadlineExpired())
                    ThrowTranslated(ex);
                ThrowSourceFailure(ex);
                throw;
            }
        }

        public override int Read(Span<byte> buffer)
        {
            ThrowIfCallerDisposed();
            ThrowIfDeadlineExpired();
            if (buffer.IsEmpty)
                return 0;

            try
            {
                int read = inner.Read(buffer);
                ThrowIfCallerDisposed();
                ThrowIfDeadlineExpired();
                if (read == 0 && !buffer.IsEmpty)
                {
                    Volatile.Write(ref _endOfStream, 1);
                    CompleteDeadline();
                }
                return read;
            }
            catch (Exception ex) when (IsStreamReadFailure(ex))
            {
                if (IsDeadlineExpired())
                    ThrowTranslated(ex);
                ThrowSourceFailure(ex);
                throw;
            }
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ThrowIfCallerDisposed();
            if (cancellationToken.IsCancellationRequested
                && cancellationToken == operation._context.CancellationToken)
            {
                await ThrowTranslatedAsync(
                    new OperationCanceledException(cancellationToken))
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await ThrowIfDeadlineExpiredAsync().ConfigureAwait(false);
            if (buffer.IsEmpty)
                return 0;

            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _requestToken);
            try
            {
                int read = await inner.ReadAsync(buffer, linked.Token)
                    .ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                ThrowIfCallerDisposed();
                await ThrowIfDeadlineExpiredAsync().ConfigureAwait(false);
                if (read == 0 && !buffer.IsEmpty)
                {
                    Volatile.Write(ref _endOfStream, 1);
                    await DisposeDeadlineStateAsync()
                        .ConfigureAwait(false);
                }
                return read;
            }
            catch (OperationCanceledException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    if (cancellationToken
                        == operation._context.CancellationToken)
                    {
                        await ThrowTranslatedAsync(ex).ConfigureAwait(false);
                    }

                    throw new OperationCanceledException(
                        cancellationToken);
                }

                await ThrowTranslatedAsync(ex).ConfigureAwait(false);
                ThrowSourceFailure(ex);
                throw;
            }
            catch (Exception ex) when (IsStreamReadFailure(ex))
            {
                bool deadlineExpired = IsDeadlineExpired();
                if (cancellationToken.IsCancellationRequested
                    && (deadlineExpired
                        || cancellationToken
                            == operation._context.CancellationToken))
                {
                    if (cancellationToken
                        == operation._context.CancellationToken)
                    {
                        await ThrowTranslatedAsync(ex).ConfigureAwait(false);
                    }

                    throw new OperationCanceledException(
                        cancellationToken);
                }

                await ThrowTranslatedAsync(ex).ConfigureAwait(false);
                ThrowSourceFailure(ex);
                throw;
            }
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
                .AsTask();

        public override int ReadByte()
        {
            ThrowIfCallerDisposed();
            ThrowIfDeadlineExpired();
            try
            {
                int value = inner.ReadByte();
                ThrowIfCallerDisposed();
                ThrowIfDeadlineExpired();
                if (value < 0)
                {
                    Volatile.Write(ref _endOfStream, 1);
                    CompleteDeadline();
                }
                return value;
            }
            catch (Exception ex) when (IsStreamReadFailure(ex))
            {
                if (IsDeadlineExpired())
                    ThrowTranslated(ex);
                ThrowSourceFailure(ex);
                throw;
            }
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(
            CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing
                && Interlocked.Exchange(ref _disposeStarted, 1) == 0)
            {
                if (operation._source is not null)
                {
                    Exception? cleanupFailure = null;
                    try
                    {
                        inner.Dispose();
                    }
                    catch (Exception ex)
                    {
                        cleanupFailure = ex;
                    }

                    try
                    {
                        owner.Dispose();
                    }
                    catch (Exception ex)
                    {
                        cleanupFailure ??= ex;
                    }

                    try
                    {
                        DisposeDeadlineState();
                    }
                    catch (Exception ex)
                    {
                        cleanupFailure ??= ex;
                    }

                    base.Dispose(disposing);
                    cleanupFailure ??=
                        Volatile.Read(ref _abortDisposalFailure);
                    if (cleanupFailure is not null)
                        ThrowCleanupFailure(cleanupFailure);

                    return;
                }

                try
                {
                    inner.Dispose();
                }
                finally
                {
                    try
                    {
                        owner.Dispose();
                    }
                    finally
                    {
                        DisposeDeadlineState();
                    }
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 0)
            {
                if (operation._source is not null)
                {
                    Exception? cleanupFailure = null;
                    try
                    {
                        await inner.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        cleanupFailure = ex;
                    }

                    try
                    {
                        await DisposeDeadlineStateAsync()
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        cleanupFailure ??= ex;
                    }

                    try
                    {
                        owner.Dispose();
                    }
                    catch (Exception ex)
                    {
                        cleanupFailure ??= ex;
                    }

                    GC.SuppressFinalize(this);
                    cleanupFailure ??=
                        Volatile.Read(ref _abortDisposalFailure);
                    if (cleanupFailure is not null)
                        ThrowCleanupFailure(cleanupFailure);

                    return;
                }

                try
                {
                    await inner.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await DisposeDeadlineStateAsync()
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        owner.Dispose();
                    }
                }
            }

            GC.SuppressFinalize(this);
        }

        private void ThrowIfDeadlineExpired()
        {
            if (!IsDeadlineExpired())
            {
                return;
            }

            var cancellation = new OperationCanceledException(
                "NuGet response stream exceeded its deadline.",
                _requestToken);
            ThrowTranslated(cancellation);
            throw cancellation;
        }

        private async ValueTask ThrowIfDeadlineExpiredAsync()
        {
            if (!IsDeadlineExpired())
            {
                return;
            }

            var cancellation = new OperationCanceledException(
                "NuGet response stream exceeded its deadline.",
                _requestToken);
            await ThrowTranslatedAsync(cancellation).ConfigureAwait(false);
            throw cancellation;
        }

        private void DisposeDeadlineState()
        {
            CompleteDeadline();
        }

        private async ValueTask DisposeDeadlineStateAsync()
        {
            if (Interlocked.Exchange(ref _deadlineCompleted, 1) != 0)
                return;

            await _deadlineRegistration.DisposeAsync().ConfigureAwait(false);
            if (Volatile.Read(ref _abortStarted) == 0)
                _abortCompleted.TrySetResult();
            requestCancellation.Dispose();
            operation._disposed = true;
            if (operation._ownsContext)
                operation._context.Complete();
        }

        private void CompleteDeadline()
        {
            if (Interlocked.Exchange(ref _deadlineCompleted, 1) != 0)
                return;

            _deadlineRegistration.Dispose();
            if (Volatile.Read(ref _abortStarted) == 0)
                _abortCompleted.TrySetResult();
            requestCancellation.Dispose();
            operation._disposed = true;
            if (operation._ownsContext)
                operation._context.Complete();
        }

        private bool IsStreamReadFailure(Exception exception) =>
            exception is OperationCanceledException
                or IOException
                or InvalidDataException
                or HttpRequestException
                or TimeoutException
                or ObjectDisposedException
            || IsDeadlineExpired()
                && NuGetOperationDeadline.IsDeadlineAbort(exception);

        private void ThrowIfCallerDisposed() =>
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposeStarted) != 0,
                this);

        private bool IsDeadlineExpired() =>
            Volatile.Read(ref _endOfStream) == 0
            && operation.IsAnyDeadlineExpired(
                requestStarted,
                requestCancellation);

        private void AbortOwner()
        {
            if (Interlocked.Exchange(ref _abortStarted, 1) != 0)
                return;

            try
            {
                owner.Dispose();
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(
                    ref _abortDisposalFailure,
                    ex,
                    null);
            }
            finally
            {
                _abortCompleted.TrySetResult();
            }
        }

        private void ThrowTranslated(Exception exception)
        {
            if (!IsDeadlineExpired())
            {
                return;
            }

            AbortOwner();
            WaitForAbortCompletion();
            ThrowTranslatedCore(exception);
        }

        private void ThrowTranslatedCore(
            Exception exception,
            bool cleanupFailed = false)
        {
            Exception? disposalFailure =
                Volatile.Read(ref _abortDisposalFailure);
            Exception inner = disposalFailure is null
                ? exception
                : new AggregateException(
                    "NuGet response disposal failed while aborting a deadline.",
                    exception,
                    disposalFailure);
            var cancellation = new OperationCanceledException(
                "NuGet response stream was aborted after its deadline expired.",
                inner,
                _requestToken);
            try
            {
                operation.ThrowTranslated(
                    cancellation,
                    requestCancellation,
                    requestStarted);
            }
            catch (OperationCanceledException)
                when (operation._source is not null
                    && operation._context.CancellationToken
                        .IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "NuGet payload consumption was canceled by the caller.",
                    innerException: null,
                    operation._context.CancellationToken);
            }
            catch (NuGetRequestTimeoutException timeout)
                when (operation._source is not null)
            {
                throw new PackageSourceStreamException(
                    operation._source,
                    PackageSourceFailureKind.Timeout,
                    new PackageSourceTimeout(
                        PackageSourceTimeoutKind.Request,
                        timeout.Timeout),
                    cleanupFailed || disposalFailure is not null);
            }
            catch (NuGetOperationTimeoutException timeout)
                when (operation._source is not null)
            {
                throw new PackageSourceStreamException(
                    operation._source,
                    PackageSourceFailureKind.Timeout,
                    new PackageSourceTimeout(
                        PackageSourceTimeoutKind.Operation,
                        timeout.Timeout),
                    cleanupFailed || disposalFailure is not null);
            }
        }

        private async ValueTask ThrowTranslatedAsync(Exception exception)
        {
            if (!IsDeadlineExpired())
            {
                return;
            }

            AbortOwner();
            await _abortCompleted.Task.ConfigureAwait(false);
            ThrowTranslatedCore(exception);
        }

        private void WaitForAbortCompletion()
        {
            _abortCompleted.Task.GetAwaiter().GetResult();
        }

        private void ThrowSourceFailure(Exception exception)
        {
            if (operation._source is null)
                return;

            throw new PackageSourceStreamException(
                operation._source,
                NuGetTransportFailure.IsTimeout(exception)
                    ? PackageSourceFailureKind.Timeout
                    : PackageSourceFailureKind.Transport,
                timeout: null,
                cleanupFailed: false);
        }

        private void ThrowCleanupFailure(Exception exception)
        {
            if (IsDeadlineExpired())
                ThrowTranslatedCore(exception, cleanupFailed: true);

            throw new PackageSourceStreamException(
                operation._source!,
                NuGetTransportFailure.IsTimeout(exception)
                    ? PackageSourceFailureKind.Timeout
                    : PackageSourceFailureKind.Transport,
                timeout: null,
                cleanupFailed: true);
        }
    }
}
