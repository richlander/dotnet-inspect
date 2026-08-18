using System.Diagnostics;

namespace NuGetFetch;

internal sealed class NuGetOperationDeadline : IDisposable
{
    private readonly CancellationToken _callerToken;
    private readonly CancellationTokenSource _operationCancellation;
    private readonly TimeSpan _operationTimeout;
    private readonly TimeSpan _requestTimeout;
    private readonly long _operationStarted;
    private bool _disposed;
    private bool _ownershipTransferred;

    public NuGetOperationDeadline(
        NuGetFetchOptions options,
        TimeSpan clientTimeout,
        CancellationToken callerToken)
    {
        options = NuGetFetchOptions.Validate(options);
        _callerToken = callerToken;
        _requestTimeout = NuGetFetchOptions.RequestTimeoutForClient(
            options,
            clientTimeout);
        _operationTimeout = options.OperationTimeout;
        _operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        _operationCancellation.CancelAfter(_operationTimeout);
        _operationStarted = Stopwatch.GetTimestamp();
    }

    public async Task<T> RunRequestAsync<T>(
        Func<CancellationToken, Task<T>> request)
    {
        using CancellationTokenSource requestCancellation =
            CreateRequestCancellation();
        try
        {
            T result = await request(requestCancellation.Token)
                .ConfigureAwait(false);
            requestCancellation.Token.ThrowIfCancellationRequested();
            ThrowIfExpired();
            return result;
        }
        catch (OperationCanceledException ex)
        {
            ThrowTranslated(ex, requestCancellation);
            throw;
        }
        catch (Exception ex)
            when (requestCancellation.IsCancellationRequested
                && IsDeadlineAbort(ex))
        {
            ThrowTranslatedAbort(ex, requestCancellation);
            throw;
        }
    }

    public void ThrowIfExpired()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_callerToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "NuGet operation was canceled by the caller.",
                _callerToken);
        }

        if (_operationCancellation.IsCancellationRequested
            || Stopwatch.GetElapsedTime(_operationStarted) >= _operationTimeout)
        {
            throw new NuGetOperationTimeoutException(
                _operationTimeout,
                new OperationCanceledException(
                    "NuGet operation deadline expired.",
                    _operationCancellation.Token));
        }
    }

    public async Task DelayAsync(TimeSpan delay)
    {
        ThrowIfExpired();
        try
        {
            await Task.Delay(
                delay,
                _operationCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            if (_callerToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "NuGet operation was canceled by the caller.",
                    exception,
                    _callerToken);
            }

            throw new NuGetOperationTimeoutException(
                _operationTimeout,
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
        CancellationTokenSource requestCancellation =
            CreateRequestCancellation();
        try
        {
            (Stream stream, IDisposable owner, T metadata) =
                await request(requestCancellation.Token).ConfigureAwait(false);
            if (requestCancellation.IsCancellationRequested)
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
                    this),
                metadata);
        }
        catch (OperationCanceledException ex)
        {
            try
            {
                ThrowTranslated(ex, requestCancellation);
            }
            finally
            {
                requestCancellation.Dispose();
            }

            throw;
        }
        catch (Exception ex)
            when (requestCancellation.IsCancellationRequested
                && IsDeadlineAbort(ex))
        {
            try
            {
                ThrowTranslatedAbort(ex, requestCancellation);
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
        _operationCancellation.Dispose();
    }

    private CancellationTokenSource CreateRequestCancellation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                _operationCancellation.Token);
        cancellation.CancelAfter(_requestTimeout);
        return cancellation;
    }

    private void ThrowTranslated(
        OperationCanceledException exception,
        CancellationTokenSource requestCancellation)
    {
        if (_callerToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "NuGet operation was canceled by the caller.",
                exception,
                _callerToken);
        }

        if (_operationCancellation.IsCancellationRequested)
        {
            throw new NuGetOperationTimeoutException(
                _operationTimeout,
                exception);
        }

        throw new NuGetRequestTimeoutException(
            _requestTimeout,
            exception);
    }

    private void ThrowTranslatedAbort(
        Exception exception,
        CancellationTokenSource requestCancellation)
    {
        var cancellation = new OperationCanceledException(
            "NuGet request was aborted after its deadline expired.",
            exception,
            requestCancellation.Token);
        ThrowTranslated(cancellation, requestCancellation);
    }

    private static bool IsDeadlineAbort(Exception exception) =>
        exception is IOException
            or HttpRequestException
            or ObjectDisposedException;

    private sealed class DeadlineStream : Stream
    {
        private readonly Stream inner;
        private readonly IDisposable owner;
        private readonly CancellationTokenSource requestCancellation;
        private readonly NuGetOperationDeadline operation;
        private readonly CancellationToken _requestToken;
        private readonly CancellationTokenRegistration _deadlineRegistration;
        private readonly TaskCompletionSource _abortCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Exception? _abortDisposalFailure;
        private int _abortStarted;
        private int _deadlineCompleted;
        private bool _disposed;

        public DeadlineStream(
            Stream inner,
            IDisposable owner,
            CancellationTokenSource requestCancellation,
            NuGetOperationDeadline operation)
        {
            this.inner = inner;
            this.owner = owner;
            this.requestCancellation = requestCancellation;
            this.operation = operation;
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
            ThrowIfDeadlineExpired();
            if (count == 0)
                return 0;

            try
            {
                int read = inner.Read(buffer, offset, count);
                ThrowIfDeadlineExpired();
                if (read == 0 && count > 0)
                    CompleteDeadline();
                return read;
            }
            catch (Exception ex) when (IsDeadlineAbort(ex))
            {
                ThrowTranslated(ex);
                throw;
            }
        }

        public override int Read(Span<byte> buffer)
        {
            ThrowIfDeadlineExpired();
            if (buffer.IsEmpty)
                return 0;

            try
            {
                int read = inner.Read(buffer);
                ThrowIfDeadlineExpired();
                if (read == 0 && !buffer.IsEmpty)
                    CompleteDeadline();
                return read;
            }
            catch (Exception ex) when (IsDeadlineAbort(ex))
            {
                ThrowTranslated(ex);
                throw;
            }
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested
                && cancellationToken == operation._callerToken)
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
                await ThrowIfDeadlineExpiredAsync().ConfigureAwait(false);
                if (read == 0 && !buffer.IsEmpty)
                {
                    await DisposeDeadlineStateAsync()
                        .ConfigureAwait(false);
                }
                return read;
            }
            catch (OperationCanceledException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    if (cancellationToken == operation._callerToken)
                    {
                        await ThrowTranslatedAsync(ex).ConfigureAwait(false);
                    }

                    throw new OperationCanceledException(
                        cancellationToken);
                }

                await ThrowTranslatedAsync(ex).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex) when (IsDeadlineAbort(ex))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    if (cancellationToken == operation._callerToken)
                    {
                        await ThrowTranslatedAsync(ex).ConfigureAwait(false);
                    }

                    throw new OperationCanceledException(
                        cancellationToken);
                }

                await ThrowTranslatedAsync(ex).ConfigureAwait(false);
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
            ThrowIfDeadlineExpired();
            try
            {
                int value = inner.ReadByte();
                ThrowIfDeadlineExpired();
                if (value < 0)
                    CompleteDeadline();
                return value;
            }
            catch (Exception ex) when (IsDeadlineAbort(ex))
            {
                ThrowTranslated(ex);
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
            if (disposing && !_disposed)
            {
                _disposed = true;
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
            if (!_disposed)
            {
                _disposed = true;
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
            try
            {
                _requestToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException ex)
            {
                ThrowTranslated(ex);
                throw;
            }
        }

        private async ValueTask ThrowIfDeadlineExpiredAsync()
        {
            try
            {
                _requestToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException ex)
            {
                await ThrowTranslatedAsync(ex).ConfigureAwait(false);
                throw;
            }
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
            operation._operationCancellation.Dispose();
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
            operation._operationCancellation.Dispose();
        }

        private bool IsDeadlineAbort(Exception exception) =>
            _requestToken.IsCancellationRequested
            && NuGetOperationDeadline.IsDeadlineAbort(exception);

        private void AbortOwner()
        {
            Volatile.Write(ref _abortStarted, 1);
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
            WaitForAbortCompletion();
            ThrowTranslatedCore(exception);
        }

        private void ThrowTranslatedCore(Exception exception)
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
            operation.ThrowTranslated(cancellation, requestCancellation);
        }

        private async ValueTask ThrowTranslatedAsync(Exception exception)
        {
            await _abortCompleted.Task.ConfigureAwait(false);
            ThrowTranslatedCore(exception);
        }

        private void WaitForAbortCompletion()
        {
            _abortCompleted.Task.GetAwaiter().GetResult();
        }
    }
}
