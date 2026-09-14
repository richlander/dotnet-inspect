using System.Net;

namespace DotnetInspector.Services.Tests;

public class SourceFetchTests
{
    private const string Url = "https://example.test/source.cs";
    private static readonly byte[] Expected = "validated source"u8.ToArray();

    [Fact]
    public async Task PolicyRejectionPrecedesStoredCandidate()
    {
        RecordingSourceContentStore store = new();
        store.Seed(Url, Expected);
        var handler = new StubHandler(_ => Response(Expected));
        using var client = new HttpClient(handler);
        var fetch = new SourceFetch(client, store, new RejectingPolicy());

        SourceFetchBytesResult result =
            await fetch.FetchVerifiedSourceBytesResultAsync(
                Url,
                static _ => true,
                TestContext.Current.CancellationToken);

        Assert.Equal(SourceFetchFailureKind.RequestNotAuthorized, result.Failure);
        Assert.Null(result.Bytes);
        Assert.Equal(0, store.ReadCount);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task InvalidUrlDoesNotConsultStoreOrNetwork()
    {
        RecordingSourceContentStore store = new();
        var handler = new StubHandler(_ => Response(Expected));
        using var client = new HttpClient(handler);
        var fetch = new SourceFetch(client, store);

        SourceFetchBytesResult result =
            await fetch.FetchVerifiedSourceBytesResultAsync(
                "file:///tmp/source.cs",
                static _ => true,
                TestContext.Current.CancellationToken);

        Assert.Equal(SourceFetchFailureKind.InvalidUrl, result.Failure);
        Assert.Null(result.Bytes);
        Assert.Equal(0, store.ReadCount);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ValidatedNetworkBytesAreStoredAndReusedByExactUrl()
    {
        RecordingSourceContentStore store = new();
        var handler = new StubHandler(_ => Response(Expected));
        using var client = new HttpClient(handler);

        SourceFetchBytesResult initial =
            await new SourceFetch(client, store).FetchVerifiedSourceBytesResultAsync(
                Url,
                IsExpected,
                TestContext.Current.CancellationToken);
        SourceFetchBytesResult repeated =
            await new SourceFetch(client, store).FetchVerifiedSourceBytesResultAsync(
                Url,
                IsExpected,
                TestContext.Current.CancellationToken);

        Assert.Equal(Expected, initial.Bytes);
        Assert.Equal(Expected, repeated.Bytes);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(2, store.ReadCount);
        Assert.Equal(1, store.WriteCount);
        Assert.Equal(Url, store.LastReadKey);
        Assert.Equal(Url, store.LastWriteKey);
    }

    [Fact]
    public async Task CancellationBeforeMemoryLookupPropagates()
    {
        RecordingSourceContentStore store = new();
        var handler = new StubHandler(_ => Response(Expected));
        using var client = new HttpClient(handler);
        var fetch = new SourceFetch(client, store);
        await fetch.FetchVerifiedSourceBytesResultAsync(
            Url,
            IsExpected,
            TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetch.FetchVerifiedSourceBytesResultAsync(
                Url,
                IsExpected,
                cancellation.Token));

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(1, store.WriteCount);
    }

    [Fact]
    public async Task CancellationAtBodyCompletionDoesNotValidateOrStore()
    {
        using CancellationTokenSource cancellation = new();
        RecordingSourceContentStore store = new();
        int validations = 0;
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new CancellationAtEofContent(Expected, cancellation),
            });
        using var client = new HttpClient(handler);
        var fetch = new SourceFetch(client, store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetch.FetchVerifiedSourceBytesResultAsync(
                Url,
                _ =>
                {
                    validations++;
                    return true;
                },
                cancellation.Token));

        Assert.Equal(0, validations);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task CancellationDuringValidationDoesNotStore()
    {
        using CancellationTokenSource cancellation = new();
        RecordingSourceContentStore store = new();
        var handler = new StubHandler(_ => Response(Expected));
        using var client = new HttpClient(handler);
        var fetch = new SourceFetch(client, store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetch.FetchVerifiedSourceBytesResultAsync(
                Url,
                _ =>
                {
                    cancellation.Cancel();
                    return true;
                },
                cancellation.Token));

        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task NetworkValidatorHttpFailureEscapes()
    {
        RecordingSourceContentStore store = new();
        var handler = new StubHandler(_ => Response(Expected));
        using var client = new HttpClient(handler);
        var fetch = new SourceFetch(client, store);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => fetch.FetchVerifiedSourceBytesResultAsync(
                Url,
                static _ => throw new HttpRequestException("validator failed"),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task NetworkValidatorCancellationShapedFailureEscapes()
    {
        RecordingSourceContentStore store = new();
        var handler = new StubHandler(_ => Response(Expected));
        using var client = new HttpClient(handler);
        var fetch = new SourceFetch(client, store);

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => fetch.FetchVerifiedSourceBytesResultAsync(
                Url,
                static _ => throw new TaskCanceledException("validator failed"),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task NotFoundIsDistinctFromTransportFailure()
    {
        RecordingSourceContentStore store = new();
        var handler = new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var fetch = new SourceFetch(client, store);

        SourceFetchBytesResult result =
            await fetch.FetchVerifiedSourceBytesResultAsync(
                Url,
                IsExpected,
                TestContext.Current.CancellationToken);

        Assert.Equal(SourceFetchFailureKind.NotFound, result.Failure);
        Assert.Null(result.Bytes);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task InvalidNetworkBytesAreNotStored()
    {
        RecordingSourceContentStore store = new();
        var handler = new StubHandler(_ => Response("wrong source"u8.ToArray()));
        using var client = new HttpClient(handler);
        var fetch = new SourceFetch(client, store);

        SourceFetchBytesResult result =
            await fetch.FetchVerifiedSourceBytesResultAsync(
                Url,
                IsExpected,
                TestContext.Current.CancellationToken);

        Assert.Equal(SourceFetchFailureKind.ValidationFailed, result.Failure);
        Assert.Null(result.Bytes);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task TransportHttpFailureIsUnavailable()
    {
        RecordingSourceContentStore store = new();
        var handler = new StubHandler(
            _ => throw new HttpRequestException("transport failed"));
        using var client = new HttpClient(handler);
        var fetch = new SourceFetch(client, store);

        SourceFetchBytesResult result =
            await fetch.FetchVerifiedSourceBytesResultAsync(
                Url,
                IsExpected,
                TestContext.Current.CancellationToken);

        Assert.Equal(SourceFetchFailureKind.Unavailable, result.Failure);
        Assert.Null(result.Bytes);
        Assert.Equal(0, store.WriteCount);
    }

    private static bool IsExpected(ReadOnlyMemory<byte> content)
        => content.Span.SequenceEqual(Expected);

    private static HttpResponseMessage Response(byte[] content)
        => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class RecordingSourceContentStore : ISourceContentStore
    {
        private readonly Dictionary<string, byte[]> _entries =
            new(StringComparer.Ordinal);

        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public string? LastReadKey { get; private set; }
        public string? LastWriteKey { get; private set; }

        public void Seed(string key, byte[] content)
            => _entries[key] = content.ToArray();

        public ValueTask<byte[]?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            LastReadKey = key;
            return ValueTask.FromResult(
                _entries.TryGetValue(key, out byte[]? content)
                    ? content.ToArray()
                    : null);
        }

        public ValueTask StoreAsync(
            string key,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            LastWriteKey = key;
            _entries[key] = content.ToArray();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RejectingPolicy : ISourceFetchPolicy
    {
        public bool IsRequestAllowed(Uri requestUri) => false;

        public void ConfigureRequest(HttpRequestMessage request)
            => throw new InvalidOperationException(
                "A rejected request must not be configured.");
    }

    private sealed class CancellationAtEofContent(
        byte[] content,
        CancellationTokenSource cancellation)
        : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
            => stream.WriteAsync(content).AsTask();

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(
                new CancellationAtEofStream(content, cancellation));

        protected override bool TryComputeLength(out long length)
        {
            length = content.Length;
            return true;
        }
    }

    private sealed class CancellationAtEofStream(
        byte[] content,
        CancellationTokenSource cancellation)
        : MemoryStream(content, writable: false)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = Read(buffer.Span);
            if (read == 0)
                cancellation.Cancel();
            return ValueTask.FromResult(read);
        }
    }
}
