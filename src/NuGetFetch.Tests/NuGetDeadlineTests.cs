using System.Net;
using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

public sealed class NuGetDeadlineTests
{
    private const string ServiceIndexUrl =
        "https://feed.example/v3/index.json";
    private const string FlatContainerUrl =
        "https://feed.example/v3/flat/";
    private const string PackageUrl =
        "https://feed.example/v3/flat/package/1.0.0/package.1.0.0.nupkg";

    [Fact]
    public void Defaults_AreThirtySecondRequestsAndTwoMinuteOperations()
    {
        var options = new NuGetFetchOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(120), options.OperationTimeout);
        Assert.Equal(
            Timeout.InfiniteTimeSpan,
            options.MetadataBodyTimeout);
    }

    [Fact]
    public void RequestTimeout_DoesNotCreateASecondResponseBodyTimer()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMinutes(3),
            OperationTimeout = TimeSpan.FromMinutes(4),
        };

        NuGetFetchOptions effective = NuGetFetchOptions.ForClient(
            options,
            Timeout.InfiniteTimeSpan);

        Assert.Equal(
            Timeout.InfiniteTimeSpan,
            effective.MetadataBodyTimeout);
    }

    [Fact]
    public void DirectStreamParsing_UsesTheRequestTimeout()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMinutes(3),
            OperationTimeout = TimeSpan.FromMinutes(4),
        };

        NuGetFetchOptions effective = NuGetFetchOptions.ForStream(options);

        Assert.Equal(options.RequestTimeout, effective.MetadataBodyTimeout);
    }

    [Fact]
    public void ShorterWholeRequest_SuppressesTheLongerBodyTimer()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(30),
            OperationTimeout = TimeSpan.FromMinutes(2),
            MetadataBodyTimeout = TimeSpan.FromSeconds(5),
        };

        NuGetFetchOptions effective = NuGetFetchOptions.ForClient(
            options,
            TimeSpan.FromMilliseconds(50));

        Assert.Equal(
            Timeout.InfiniteTimeSpan,
            effective.MetadataBodyTimeout);
    }

    [Fact]
    public async Task RequestDeadline_BoundsARequestBeforeHeaders()
    {
        using var client = new HttpClient(new DelayedHandler(
            static async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            }));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(40),
                operation: TimeSpan.FromSeconds(1)));

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                () => nuget.GetVersionsAsync(
                    "package",
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task OperationCeiling_SpansServiceDiscoveryAndVersionLookup()
    {
        int request = 0;
        using var client = new HttpClient(new DelayedHandler(
            async (message, cancellationToken) =>
            {
                request++;
                await Task.Delay(
                    request == 1
                        ? TimeSpan.FromMilliseconds(45)
                        : Timeout.InfiniteTimeSpan,
                    cancellationToken);
                return JsonResponse(
                    message,
                    $$"""
                    {
                      "resources": [
                        {
                          "@id": "{{FlatContainerUrl}}",
                          "@type": "PackageBaseAddress/3.0.0"
                        }
                      ]
                    }
                    """);
            }));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(200),
                operation: TimeSpan.FromMilliseconds(80)));

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                () => nuget.GetVersionsAsync(
                    "package",
                    ServiceIndexUrl,
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(80), error.Timeout);
        Assert.Equal(2, request);
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsADeadline()
    {
        using var client = new HttpClient(new DelayedHandler(
            static async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            }));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(1),
                operation: TimeSpan.FromSeconds(2)));
        using var cancellation =
            new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => nuget.GetVersionsAsync(
                    "package",
                    cancellationToken: cancellation.Token));

        Assert.IsNotType<NuGetRequestTimeoutException>(error);
        Assert.IsNotType<NuGetOperationTimeoutException>(error);
    }

    [Fact]
    public async Task RequestDeadline_BoundsPackageStreamConsumption()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    StreamResponse(message, new StallingStream()))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(40),
                operation: TimeSpan.FromSeconds(1)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                {
                    byte[] buffer = new byte[1];
                    _ = await package.ReadAsync(
                        buffer,
                        TestContext.Current.CancellationToken);
                });

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task RequestDeadline_BoundsSynchronousPackageStreamConsumption()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    StreamResponse(
                        message,
                        new DisposeAwareStallingStream()))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(40),
                operation: TimeSpan.FromSeconds(1)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[1];
        Task<int> read = Task.Run(
            () => package.Read(buffer, 0, buffer.Length),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () => _ = await read);

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task OperationCeiling_IncludesPackageStreamConsumption()
    {
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
            {
                HttpResponseMessage response =
                    message.RequestUri!.ToString() switch
                {
                    ServiceIndexUrl => JsonResponse(
                        message,
                        $$"""
                        {
                          "resources": [
                            {
                              "@id": "{{FlatContainerUrl}}",
                              "@type": "PackageBaseAddress/3.0.0"
                            }
                          ]
                        }
                        """),
                    PackageUrl => StreamResponse(
                        message,
                        new StallingStream()),
                    _ => throw new InvalidOperationException(),
                };
                return Task.FromResult(response);
            }));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(1),
                operation: TimeSpan.FromMilliseconds(60)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            ServiceIndexUrl,
            cancellationToken: TestContext.Current.CancellationToken);

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                async () =>
                {
                    byte[] buffer = new byte[1];
                    _ = await package.ReadAsync(
                        buffer,
                        TestContext.Current.CancellationToken);
                });

        Assert.Equal(TimeSpan.FromMilliseconds(60), error.Timeout);
    }

    [Fact]
    public async Task PackageCallerCancellation_IsNotReportedAsADeadline()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    StreamResponse(message, new StallingStream()))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(1),
                operation: TimeSpan.FromSeconds(2)));
        using var cancellation = new CancellationTokenSource();

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: cancellation.Token);
        cancellation.Cancel();

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                {
                    byte[] buffer = new byte[1];
                    _ = await package.ReadAsync(
                        buffer,
                        TestContext.Current.CancellationToken);
                });

        Assert.IsNotType<NuGetRequestTimeoutException>(error);
        Assert.IsNotType<NuGetOperationTimeoutException>(error);
    }

    [Fact]
    public async Task SynchronousPackageCallerCancellation_RemainsCancellation()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    StreamResponse(
                        message,
                        new DisposeAwareStallingStream()))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(1),
                operation: TimeSpan.FromSeconds(2)));
        using var cancellation = new CancellationTokenSource();

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: cancellation.Token);
        byte[] buffer = new byte[1];
        Task<int> read = Task.Run(
            () => package.Read(buffer, 0, buffer.Length),
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => _ = await read);

        Assert.IsNotType<NuGetRequestTimeoutException>(error);
        Assert.IsNotType<NuGetOperationTimeoutException>(error);
    }

    private static NuGetFetchOptions Options(
        TimeSpan request,
        TimeSpan operation) =>
        new()
        {
            RequestTimeout = request,
            OperationTimeout = operation,
        };

    private static HttpResponseMessage JsonResponse(
        HttpRequestMessage request,
        string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
            RequestMessage = request,
        };

    private static HttpResponseMessage StreamResponse(
        HttpRequestMessage request,
        Stream stream) =>
        new(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
            RequestMessage = request,
        };

    private sealed class DelayedHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>
            response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            response(request, cancellationToken);
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

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class DisposeAwareStallingStream : Stream
    {
        private readonly ManualResetEventSlim _disposed = new();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _disposed.Wait();
            throw new ObjectDisposedException(nameof(DisposeAwareStallingStream));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _disposed.Set();
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
