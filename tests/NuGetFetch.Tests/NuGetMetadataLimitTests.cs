using System.Net;
using System.Text;
using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

public sealed class NuGetMetadataLimitTests
{
    private const string SearchUrl = "https://feed.example/query";
    private static readonly HttpRequestOptionsKey<bool> BrowserStreamingResponse =
        new("WebAssemblyEnableStreamingResponse");


    [Fact]
    public async Task Search_AdvertisedOversizeRejectsBeforeReadingTheBody()
    {
        var content = new UnreadableAdvertisedContent(65);
        using var client = new HttpClient(new SingleResponseHandler(
            request => Response(request, content)));
        var service = new SearchService(
            client,
            SearchUrl,
            Options(maximumBytes: 64));

        NuGetMetadataResponseTooLargeException error =
            await Assert.ThrowsAsync<NuGetMetadataResponseTooLargeException>(
                () => service.SearchAsync(
                    "package",
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(64, error.MaximumBytes);
        Assert.Equal(65, error.AdvertisedBytes);
        Assert.False(content.ReadAttempted);
    }

    [Fact]
    public async Task Search_UnderreportedLengthCannotBypassTheActualByteLimit()
    {
        byte[] body = SearchBody("This.Package.Name.Makes.The.Response.Too.Large");
        using var client = new HttpClient(new SingleResponseHandler(
            request => Response(
                request,
                new StreamBackedContent(body, advertisedLength: 8))));
        var service = new SearchService(
            client,
            SearchUrl,
            Options(maximumBytes: body.Length - 1));

        NuGetMetadataResponseTooLargeException error =
            await Assert.ThrowsAsync<NuGetMetadataResponseTooLargeException>(
                () => service.SearchAsync(
                    "package",
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(body.Length - 1, error.MaximumBytes);
        Assert.Null(error.AdvertisedBytes);
    }

    [Fact]
    public async Task Search_ResponseExactlyAtTheLimitSucceeds()
    {
        byte[] body = SearchBody("Exact.Package");
        using var client = new HttpClient(new SingleResponseHandler(
            request => Response(request, new StreamBackedContent(body))));
        var service = new SearchService(
            client,
            SearchUrl,
            Options(maximumBytes: body.Length));

        IReadOnlyList<SearchResult> results = await service.SearchAsync(
            "package",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Exact.Package", Assert.Single(results).Id);
    }

    [Fact]
    public async Task VersionIndex_OversizeIsNotReportedAsAnEmptyVersionList()
    {
        byte[] body = Encoding.UTF8.GetBytes(
            """{"versions":["1.0.0","2.0.0-preview.with-a-long-label"]}""");
        using var client = new HttpClient(new SingleResponseHandler(
            request => Response(request, new StreamBackedContent(body))));
        var nuget = new NuGetClient(
            client,
            Options(maximumBytes: body.Length - 1));

        await Assert.ThrowsAsync<NuGetMetadataResponseTooLargeException>(
            () => nuget.GetVersionsAsync(
                "package",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ServiceIndex_OversizeIsNotReportedAsAnAbsentResource()
    {
        byte[] body = Encoding.UTF8.GetBytes(
            """
            {"version":"3.0.0","resources":[{"@id":"https://feed.example/flat/",
            "@type":"PackageBaseAddress/3.0.0"}]}
            """);
        using var client = new HttpClient(new SingleResponseHandler(
            request => Response(request, new StreamBackedContent(body))));
        var nuget = new NuGetClient(
            client,
            Options(maximumBytes: body.Length - 1));

        await Assert.ThrowsAsync<NuGetMetadataResponseTooLargeException>(
            () => nuget.GetPackageBaseAddressAsync(
                "https://feed.example/index.json",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LatestVersionSearch_UsesTheMetadataLimit()
    {
        byte[] body = SearchBody("Package.With.An.Oversized.Search.Response");
        using var client = new HttpClient(new SingleResponseHandler(
            request => Response(request, new StreamBackedContent(body))));
        var nuget = new NuGetClient(
            client,
            Options(maximumBytes: body.Length - 1));

        await Assert.ThrowsAsync<NuGetMetadataResponseTooLargeException>(
            () => nuget.GetLatestVersionAsync(
                "package",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NuGetGets_RequestBrowserStreaming()
    {
        string[] bodies =
        [
            """{"data":[]}""",
            """{"versions":["1.0.0"]}""",
            """
            {"version":"3.0.0","resources":[{"@id":"https://feed.example/flat/",
            "@type":"PackageBaseAddress/3.0.0"}]}
            """,
            """{"data":[]}""",
            "package bytes",
        ];
        int responseIndex = 0;
        var streamingRequests = new List<bool>();
        var handler = new SingleResponseHandler(
            request =>
            {
                streamingRequests.Add(
                    request.Options.TryGetValue(
                        BrowserStreamingResponse,
                        out bool enabled)
                    && enabled);
                return Response(
                    request,
                    new StringContent(bodies[responseIndex++]));
            });
        using var client = new HttpClient(handler);
        var search = new SearchService(client, SearchUrl);
        var nuget = new NuGetClient(client);

        await search.SearchAsync(
            "package",
            cancellationToken: TestContext.Current.CancellationToken);
        await nuget.GetVersionsAsync(
            "package",
            cancellationToken: TestContext.Current.CancellationToken);
        await nuget.GetPackageBaseAddressAsync(
            "https://feed.example/index.json",
            TestContext.Current.CancellationToken);
        await nuget.GetLatestVersionAsync(
            "package",
            cancellationToken: TestContext.Current.CancellationToken);
        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, handler.RequestCount);
        Assert.All(streamingRequests, Assert.True);
    }

    [Fact]
    public async Task OversizeDoesNotFallThroughToAnotherSource()
    {
        byte[] body = Encoding.UTF8.GetBytes(
            """
            {"version":"3.0.0","resources":[{"@id":"https://feed.example/flat/",
            "@type":"PackageBaseAddress/3.0.0"}]}
            """);
        var handler = new SingleResponseHandler(
            request => Response(request, new StreamBackedContent(body)));
        using var client = new HttpClient(handler);
        var nuget = new NuGetClient(
            client,
            Options(maximumBytes: body.Length - 1));
        PackageSource[] sources =
        [
            new("first", "https://first.example/index.json"),
            new("second", "https://second.example/index.json"),
        ];

        await Assert.ThrowsAsync<NuGetMetadataResponseTooLargeException>(
            () => nuget.GetLatestVersionAsync(
                "package",
                sources,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task StalledBodyUsesTheBodyPhaseTimeout()
    {
        using var client = new HttpClient(new SingleResponseHandler(
            request => Response(
                request,
                new StreamBackedContent(() => new StallingStream()))));
        var service = new SearchService(
            client,
            SearchUrl,
            Options(
                maximumBytes: 1024,
                bodyTimeout: TimeSpan.FromMilliseconds(50)));
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(2));

        NuGetMetadataBodyTimeoutException error =
            await Assert.ThrowsAsync<NuGetMetadataBodyTimeoutException>(
                () => service.SearchAsync(
                    "package",
                    cancellationToken: guard.Token));

        Assert.Equal(TimeSpan.FromMilliseconds(50), error.Timeout);
    }

    [Fact]
    public async Task ShorterHttpClientTimeoutBoundsTheWholeRequest()
    {
        using var client = new HttpClient(new SingleResponseHandler(
            request => Response(
                request,
                new StreamBackedContent(() => new StallingStream()))));
        var service = new SearchService(
            client,
            SearchUrl,
            Options(
                maximumBytes: 1024,
                bodyTimeout: TimeSpan.FromSeconds(5)));
        client.Timeout = TimeSpan.FromMilliseconds(50);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(2));

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                () => service.SearchAsync(
                    "package",
                    cancellationToken: guard.Token));

        Assert.Equal(client.Timeout, error.Timeout);
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsABodyTimeout()
    {
        using var client = new HttpClient(new SingleResponseHandler(
            request => Response(
                request,
                new StreamBackedContent(() => new StallingStream()))));
        var service = new SearchService(
            client,
            SearchUrl,
            Options(
                maximumBytes: 1024,
                bodyTimeout: TimeSpan.FromSeconds(5)));
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SearchAsync(
                "package",
                cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData(CallerCancellationFailure.OperationCanceled)]
    [InlineData(CallerCancellationFailure.Io)]
    public async Task DirectNuGetApiCallerCancellationRetainsCallerToken(
        CallerCancellationFailure failure)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await using var stream = new CallerCancellingStream(
            cancellation,
            failure);

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => NuGetApi.GetServiceIndexAsync(
                    stream,
                    cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task DirectNuGetApiCallerCancellationPreservesTypedMetadataFailure()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await using var stream = new CallerCancellingStream(
            cancellation,
            CallerCancellationFailure.MetadataTooLarge);

        await Assert.ThrowsAsync<NuGetMetadataResponseTooLargeException>(
            () => NuGetApi.GetServiceIndexAsync(
                stream,
                cancellation.Token).AsTask());
    }

    [Fact]
    public async Task DirectNuGetApiReadersUseTheDefaultLimit()
    {
        int length = checked(
            (int)NuGetFetchOptions.DefaultMaxMetadataResponseBytes + 1);
        byte[] body = GC.AllocateUninitializedArray<byte>(length);

        (byte[] Prefix, Func<Stream, Task> Read)[] cases =
        [
            (
                Encoding.UTF8.GetBytes("""{"version":"3.0.0","resources":[]}"""),
                async stream => _ = await NuGetApi.GetServiceIndexAsync(
                    stream,
                    TestContext.Current.CancellationToken)),
            (
                Encoding.UTF8.GetBytes("""{"versions":[]}"""),
                async stream => _ = await NuGetApi.GetVersionIndexAsync(
                    stream,
                    TestContext.Current.CancellationToken)),
            (
                Encoding.UTF8.GetBytes("""{"data":[]}"""),
                async stream => _ = await NuGetApi.GetSearchResponseAsync(
                    stream,
                    TestContext.Current.CancellationToken)),
        ];

        foreach ((byte[] prefix, Func<Stream, Task> read) in cases)
        {
            Array.Fill(body, (byte)' ');
            prefix.CopyTo(body, 0);
            using var stream = new MemoryStream(body, writable: false);

            await Assert.ThrowsAsync<NuGetMetadataResponseTooLargeException>(
                () => read(stream));
        }
    }

    [Fact]
    public async Task PackagePayloadIsNotSubjectToTheMetadataLimit()
    {
        byte[] package = Encoding.ASCII.GetBytes("package bytes");
        using var client = new HttpClient(new SingleResponseHandler(
            request => Response(request, new StreamBackedContent(package))));
        var nuget = new NuGetClient(
            client,
            Options(maximumBytes: package.Length - 1));

        await using Stream downloaded = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        using var copy = new MemoryStream();
        await downloaded.CopyToAsync(
            copy,
            TestContext.Current.CancellationToken);

        Assert.Equal(package, copy.ToArray());
    }

    private static NuGetFetchOptions Options(
        long maximumBytes,
        TimeSpan? bodyTimeout = null) =>
        new()
        {
            MaxMetadataResponseBytes = maximumBytes,
            MetadataBodyTimeout =
                bodyTimeout ?? NuGetFetchOptions.DefaultMetadataBodyTimeout,
        };

    private static byte[] SearchBody(string id) =>
        Encoding.UTF8.GetBytes(
            $$"""{"data":[{"id":"{{id}}","version":"1.0.0"}]}""");

    private static HttpResponseMessage Response(
        HttpRequestMessage request,
        HttpContent content) =>
        new(HttpStatusCode.OK)
        {
            Content = content,
            RequestMessage = request,
        };

    private sealed class SingleResponseHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(response(request));
        }
    }

    private sealed class UnreadableAdvertisedContent : HttpContent
    {
        private readonly long _length;

        public UnreadableAdvertisedContent(long length)
        {
            _length = length;
            Headers.ContentLength = length;
        }

        public bool ReadAttempted { get; private set; }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            ReadAttempted = true;
            return Task.FromException(
                new InvalidOperationException("The body must not be read."));
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            ReadAttempted = true;
            return Task.FromException<Stream>(
                new InvalidOperationException("The body must not be read."));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }

    private sealed class StreamBackedContent : HttpContent
    {
        private readonly Func<Stream> _createStream;

        public StreamBackedContent(
            byte[] body,
            long? advertisedLength = null)
            : this(() => new MemoryStream(body, writable: false))
        {
            Headers.ContentLength = advertisedLength;
        }

        public StreamBackedContent(Func<Stream> createStream)
        {
            _createStream = createStream;
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            await using Stream source = _createStream();
            await source.CopyToAsync(stream);
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult(_createStream());

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
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

    public enum CallerCancellationFailure
    {
        OperationCanceled,
        Io,
        MetadataTooLarge,
    }

    private sealed class CallerCancellingStream(
        CancellationTokenSource caller,
        CallerCancellationFailure failure) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            caller.Cancel();
            Exception exception = failure switch
            {
                CallerCancellationFailure.OperationCanceled =>
                    new OperationCanceledException(cancellationToken),
                CallerCancellationFailure.Io =>
                    new IOException("Simulated cancellation transport abort."),
                CallerCancellationFailure.MetadataTooLarge =>
                    new NuGetMetadataResponseTooLargeException(1),
                _ => throw new InvalidOperationException(),
            };
            return ValueTask.FromException<int>(exception);
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
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
