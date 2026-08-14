namespace NuGetFetch;

internal static class NuGetMetadataResponse
{
    private static readonly HttpRequestOptionsKey<bool> BrowserStreamingResponse =
        new("WebAssemblyEnableStreamingResponse");

    internal static readonly TimeSpan DefaultBodyTimeout = TimeSpan.FromSeconds(30);

    internal static void EnableStreaming(HttpRequestMessage request) =>
        request.Options.Set(BrowserStreamingResponse, true);

    internal static async ValueTask<T> ReadAsync<T>(
        HttpClient client,
        HttpResponseMessage response,
        Func<Stream, CancellationToken, ValueTask<T>> reader,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long advertised
            && advertised > NuGetApi.MaxMetadataResponseBytes)
        {
            throw NuGetApi.CreateResponseTooLargeException();
        }

        TimeSpan bodyTimeout = client.Timeout == Timeout.InfiniteTimeSpan
            || client.Timeout > DefaultBodyTimeout
                ? DefaultBodyTimeout
                : client.Timeout;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(bodyTimeout);

        try
        {
            await using Stream stream = await response.Content
                .ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            return await reader(stream, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                "The NuGet metadata response body timed out.");
        }
    }
}

internal sealed class MaxBytesReadStream(
    Stream inner,
    long maxBytes,
    bool leaveOpen = false) : Stream
{
    private long _bytesRead;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = inner.Read(buffer, offset, LimitCount(count));
        Count(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer[..LimitCount(buffer.Length)]);
        Count(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(
            buffer[..LimitCount(buffer.Length)],
            cancellationToken).ConfigureAwait(false);
        Count(read);
        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();
    public override void SetLength(long value) =>
        throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
            inner.Dispose();
        base.Dispose(disposing);
    }

    private int LimitCount(int requested)
    {
        if (requested == 0)
            return 0;

        long remainingWithProbe = maxBytes - _bytesRead + 1;
        return (int)Math.Min(requested, remainingWithProbe);
    }

    private void Count(int read)
    {
        _bytesRead += read;
        if (_bytesRead > maxBytes)
            throw NuGetApi.CreateResponseTooLargeException();
    }
}
