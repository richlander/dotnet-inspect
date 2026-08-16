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
        var versionLookupStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int request = 0;
        using var client = new HttpClient(new DelayedHandler(
            async (message, cancellationToken) =>
            {
                int currentRequest = Interlocked.Increment(ref request);
                if (currentRequest == 2)
                    versionLookupStarted.TrySetResult();
                await Task.Delay(
                    currentRequest == 1
                        ? TimeSpan.FromMilliseconds(100)
                        : Timeout.InfiniteTimeSpan,
                    cancellationToken);
                return JsonResponse(
                    message,
                    $$"""
                    {
                      "version": "3.0.0",
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
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromSeconds(2)));

        Task<IReadOnlyList<string>> versions = nuget.GetVersionsAsync(
            "package",
            ServiceIndexUrl,
            cancellationToken: TestContext.Current.CancellationToken);
        await versionLookupStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                () => versions);

        Assert.Equal(TimeSpan.FromSeconds(2), error.Timeout);
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

        Assert.Equal(cancellation.Token, error.CancellationToken);
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
    public async Task RequestDeadline_TranslatesHttpRequestAbort()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    StreamResponse(
                        message,
                        new HttpRequestAbortStream()))));
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
    public async Task RequestDeadline_TranslatesSynchronousHttpRequestAbort()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    StreamResponse(
                        message,
                        new HttpRequestAbortStream()))));
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
    public async Task PerReadCancellation_IsNotReportedAsARequestTimeout()
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
        using var readCancellation =
            new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                {
                    byte[] buffer = new byte[1];
                    _ = await package.ReadAsync(
                        buffer,
                        readCancellation.Token);
                });

        Assert.Equal(readCancellation.Token, error.CancellationToken);
        Assert.IsNotType<NuGetRequestTimeoutException>(error);
        Assert.IsNotType<NuGetOperationTimeoutException>(error);
    }

    [Fact]
    public async Task TokenHonoringPerReadCancellation_PreservesItsToken()
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

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        using var readCancellation =
            new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                {
                    byte[] buffer = new byte[1];
                    _ = await package.ReadAsync(
                        buffer,
                        readCancellation.Token);
                });

        Assert.Equal(readCancellation.Token, error.CancellationToken);
        Assert.IsNotType<NuGetRequestTimeoutException>(error);
        Assert.IsNotType<NuGetOperationTimeoutException>(error);
    }

    [Fact]
    public async Task PreCancelledPerReadToken_PrecedesExpiredRequestDeadline()
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
        await Task.Delay(
            TimeSpan.FromMilliseconds(80),
            TestContext.Current.CancellationToken);
        using var readCancellation = new CancellationTokenSource();
        readCancellation.Cancel();

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                {
                    byte[] buffer = new byte[1];
                    _ = await package.ReadAsync(
                        buffer,
                        readCancellation.Token);
                });

        Assert.Equal(readCancellation.Token, error.CancellationToken);
        Assert.IsNotType<NuGetRequestTimeoutException>(error);
        Assert.IsNotType<NuGetOperationTimeoutException>(error);
    }

    [Fact]
    public async Task PreCancelledPerReadToken_PrecedesEmptyRead()
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

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        using var readCancellation = new CancellationTokenSource();
        readCancellation.Cancel();

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                {
                    _ = await package.ReadAsync(
                        Memory<byte>.Empty,
                        readCancellation.Token);
                });

        Assert.Equal(readCancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task CompletedPackageStream_RemainsAtEofAfterDeadline()
    {
        byte[] payload = [42];
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(
                        message,
                        new MemoryStream(payload, writable: false)))));
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

        Assert.Equal(
            1,
            await package.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            0,
            await package.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken));

        await Task.Delay(
            TimeSpan.FromMilliseconds(60),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            0,
            await package.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyAsyncRead_DoesNotDisarmTheRequestDeadline()
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

        Assert.Equal(
            0,
            await package.ReadAsync(
                Memory<byte>.Empty,
                TestContext.Current.CancellationToken));

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
    public async Task EmptySyncRead_DoesNotDisarmTheRequestDeadline()
    {
        var stallingStream = new DisposeAwareStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(
                        message,
                        stallingStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(500),
                operation: TimeSpan.FromSeconds(2)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, package.Read([], 0, 0));
        byte[] buffer = new byte[1];
        Task<int> read = Task.Run(
            () => package.Read(buffer, 0, buffer.Length),
            TestContext.Current.CancellationToken);
        await stallingStream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () => _ = await read);

        Assert.Equal(TimeSpan.FromMilliseconds(500), error.Timeout);
    }

    [Fact]
    public async Task OperationCeiling_IncludesPackageStreamConsumption()
    {
        var stallingStream = new SignalingStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                StreamResponse(message, stallingStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(2),
                operation: TimeSpan.FromMilliseconds(500)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[1];
        ValueTask<int> read = package.ReadAsync(
            buffer,
            TestContext.Current.CancellationToken);
        await stallingStream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                async () => _ = await read);

        Assert.Equal(TimeSpan.FromMilliseconds(500), error.Timeout);
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

        Assert.Equal(cancellation.Token, error.CancellationToken);
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

    private class StallingStream : Stream
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
        private readonly TaskCompletionSource _disposedAsync =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public Task ReadStarted => _readStarted.Task;

        public override int Read(byte[] buffer, int offset, int count)
        {
            _readStarted.TrySetResult();
            _disposed.Wait();
            throw new ObjectDisposedException(nameof(DisposeAwareStallingStream));
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            await _disposedAsync.Task;
            throw new ObjectDisposedException(
                nameof(DisposeAwareStallingStream));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed.Set();
                _disposedAsync.TrySetResult();
            }
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

    private sealed class SignalingStallingStream : StallingStream
    {
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class HttpRequestAbortStream : Stream
    {
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            _disposed.Task.GetAwaiter().GetResult();
            throw new HttpRequestException("Simulated HTTP stream abort.");
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await _disposed.Task;
            throw new HttpRequestException("Simulated HTTP stream abort.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _disposed.TrySetResult();
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
