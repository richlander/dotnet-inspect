namespace NuGetFetch;

internal static class NuGetMetadataReader
{
    public static async ValueTask<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        Func<Stream, CancellationToken, ValueTask<T>> deserialize,
        NuGetFetchOptions options,
        TimeSpan clientTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(deserialize);
        options = NuGetFetchOptions.ForClient(options, clientTimeout);

        if (response.Content.Headers.ContentLength is long advertised
            && advertised > options.MaxMetadataResponseBytes)
        {
            throw new NuGetMetadataResponseTooLargeException(
                options.MaxMetadataResponseBytes,
                advertised);
        }

        return await RunWithBodyTimeoutAsync(
            async bodyToken =>
            {
                await using Stream body = await response.Content
                    .ReadAsStreamAsync(bodyToken)
                    .ConfigureAwait(false);
                return await ReadStreamCoreAsync(
                    body,
                    deserialize,
                    options.MaxMetadataResponseBytes,
                    bodyToken).ConfigureAwait(false);
            },
            options,
            cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<T> ReadStreamAsync<T>(
        Stream stream,
        Func<Stream, CancellationToken, ValueTask<T>> deserialize,
        NuGetFetchOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(deserialize);
        options = NuGetFetchOptions.ForStream(options);

        return await RunWithBodyTimeoutAsync(
            bodyToken => ReadStreamCoreAsync(
                stream,
                deserialize,
                options.MaxMetadataResponseBytes,
                bodyToken),
            options,
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<T> ReadStreamCoreAsync<T>(
        Stream stream,
        Func<Stream, CancellationToken, ValueTask<T>> deserialize,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var bounded = new MaximumReadStream(stream, maximumBytes);
        return await deserialize(bounded, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<T> RunWithBodyTimeoutAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        NuGetFetchOptions options,
        CancellationToken cancellationToken)
    {
        if (options.MetadataBodyTimeout == Timeout.InfiniteTimeSpan)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(options.MetadataBodyTimeout);

        try
        {
            T result = await operation(timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException ex)
            when (!cancellationToken.IsCancellationRequested
                && timeout.IsCancellationRequested)
        {
            throw new NuGetMetadataBodyTimeoutException(
                options.MetadataBodyTimeout,
                ex);
        }
        catch (Exception ex)
            when (!cancellationToken.IsCancellationRequested
                && timeout.IsCancellationRequested
                && IsDeadlineAbort(ex))
        {
            var cancellation = new OperationCanceledException(
                "NuGet metadata body was aborted after its deadline expired.",
                ex,
                timeout.Token);
            throw new NuGetMetadataBodyTimeoutException(
                options.MetadataBodyTimeout,
                cancellation);
        }
    }

    private static bool IsDeadlineAbort(Exception exception) =>
        exception is IOException
            and not NuGetMetadataResponseTooLargeException
            and not NuGetRedirectLimitExceededException
            or HttpRequestException
            or ObjectDisposedException;

    private sealed class MaximumReadStream(
        Stream inner,
        long maximumBytes) : Stream
    {
        private long _bytesRead;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0;

            int read = inner.Read(Limit(buffer));
            AccountFor(read);
            return read;
        }

        public override int ReadByte()
        {
            int value = inner.ReadByte();
            AccountFor(value < 0 ? 0 : 1);
            return value;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(
                buffer.AsMemory(offset, count),
                cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
                return 0;

            int read = await inner.ReadAsync(
                Limit(buffer),
                cancellationToken).ConfigureAwait(false);
            AccountFor(read);
            return read;
        }

        private Span<byte> Limit(Span<byte> buffer)
        {
            long remaining = maximumBytes - _bytesRead;
            int allowed = remaining > 0
                ? (int)Math.Min(buffer.Length, remaining)
                : 1;
            return buffer[..allowed];
        }

        private Memory<byte> Limit(Memory<byte> buffer)
        {
            long remaining = maximumBytes - _bytesRead;
            int allowed = remaining > 0
                ? (int)Math.Min(buffer.Length, remaining)
                : 1;
            return buffer[..allowed];
        }

        private void AccountFor(int read)
        {
            if (read == 0)
                return;

            if (_bytesRead >= maximumBytes)
            {
                throw new NuGetMetadataResponseTooLargeException(
                    maximumBytes);
            }

            _bytesRead += read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}
