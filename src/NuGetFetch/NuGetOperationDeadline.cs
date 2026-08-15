namespace NuGetFetch;

internal sealed class NuGetOperationDeadline : IDisposable
{
    private readonly CancellationToken _callerToken;
    private readonly CancellationTokenSource _operationCancellation;
    private readonly TimeSpan _operationTimeout;
    private readonly TimeSpan _requestTimeout;
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
    }

    public async Task<T> RunRequestAsync<T>(
        Func<CancellationToken, Task<T>> request)
    {
        using CancellationTokenSource requestCancellation =
            CreateRequestCancellation();
        try
        {
            return await request(requestCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            ThrowTranslated(ex, requestCancellation);
            throw;
        }
    }

    public async Task<Stream> RunStreamingRequestAsync(
        Func<CancellationToken, Task<(Stream Stream, IDisposable Owner)>> request)
    {
        CancellationTokenSource requestCancellation =
            CreateRequestCancellation();
        try
        {
            (Stream stream, IDisposable owner) =
                await request(requestCancellation.Token).ConfigureAwait(false);
            _ownershipTransferred = true;
            return new DeadlineStream(
                stream,
                owner,
                requestCancellation,
                this);
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
            return;

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

    private sealed class DeadlineStream(
        Stream inner,
        IDisposable owner,
        CancellationTokenSource requestCancellation,
        NuGetOperationDeadline operation) : Stream
    {
        private readonly CancellationTokenRegistration _deadlineRegistration =
            requestCancellation.Token.UnsafeRegister(
                static state => ((IDisposable)state!).Dispose(),
                owner);
        private bool _disposed;

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
            try
            {
                int read = inner.Read(buffer, offset, count);
                ThrowIfDeadlineExpired();
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
            try
            {
                int read = inner.Read(buffer);
                ThrowIfDeadlineExpired();
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
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    requestCancellation.Token);
            try
            {
                int read = await inner.ReadAsync(buffer, linked.Token)
                    .ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                ThrowIfDeadlineExpired();
                return read;
            }
            catch (OperationCanceledException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;

                operation.ThrowTranslated(ex, requestCancellation);
                throw;
            }
            catch (Exception ex) when (IsDeadlineAbort(ex))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(
                        cancellationToken);
                }

                ThrowTranslated(ex);
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
                        owner.Dispose();
                    }
                    finally
                    {
                        DisposeDeadlineState();
                    }
                }
            }

            GC.SuppressFinalize(this);
        }

        private void ThrowIfDeadlineExpired()
        {
            try
            {
                requestCancellation.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException ex)
            {
                operation.ThrowTranslated(ex, requestCancellation);
                throw;
            }
        }

        private void DisposeDeadlineState()
        {
            _deadlineRegistration.Dispose();
            requestCancellation.Dispose();
            operation._disposed = true;
            operation._operationCancellation.Dispose();
        }

        private bool IsDeadlineAbort(Exception exception) =>
            requestCancellation.IsCancellationRequested
            && exception is IOException or ObjectDisposedException;

        private void ThrowTranslated(Exception exception)
        {
            if (operation._callerToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    operation._callerToken);
            }

            var cancellation = new OperationCanceledException(
                "NuGet response stream was aborted after its deadline expired.",
                exception,
                requestCancellation.Token);
            operation.ThrowTranslated(cancellation, requestCancellation);
        }
    }
}
