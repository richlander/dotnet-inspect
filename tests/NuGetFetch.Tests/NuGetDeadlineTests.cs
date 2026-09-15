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
    public async Task OperationCeiling_RejectsWorkAfterACompletedRequest()
    {
        using var operation = new NuGetOperationDeadline(
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromMilliseconds(40)),
            Timeout.InfiniteTimeSpan,
            TestContext.Current.CancellationToken);

        _ = await operation.RunRequestAsync(
            _ => Task.FromResult(42));
        await Task.Delay(
            TimeSpan.FromMilliseconds(80),
            TestContext.Current.CancellationToken);

        NuGetOperationTimeoutException error =
            Assert.Throws<NuGetOperationTimeoutException>(
                operation.ThrowIfExpired);
        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task RequestDeadline_DoesNotTranslateLateMetadataRejection()
    {
        using var operation = new NuGetOperationDeadline(
            Options(
                request: TimeSpan.FromMilliseconds(40),
                operation: TimeSpan.FromSeconds(1)),
            Timeout.InfiniteTimeSpan,
            TestContext.Current.CancellationToken);

        NuGetMetadataResponseTooLargeException error =
            await Assert.ThrowsAsync<NuGetMetadataResponseTooLargeException>(
                () => operation.RunRequestAsync<bool>(
                    async cancellationToken =>
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            await Task.Yield();
                        }

                        throw new NuGetMetadataResponseTooLargeException(8);
                    }));

        Assert.Equal(8, error.MaximumBytes);
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
    public async Task UnassociatedCancellation_IsNotReportedAsARequestTimeout()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (_, _) => throw new OperationCanceledException()));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(1),
                operation: TimeSpan.FromSeconds(2)));

        OperationCanceledException error =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => nuget.GetVersionsAsync(
                    "package",
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.IsNotType<NuGetRequestTimeoutException>(error);
        Assert.IsNotType<NuGetOperationTimeoutException>(error);
    }

    [Fact]
    public async Task RequestDeadline_TranslatesMetadataTransportAbort()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    MetadataResponse(
                        message,
                        new DelayedTransportFailureStream()))));
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
    public async Task OperationCeiling_TranslatesMetadataTransportAbort()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    MetadataResponse(
                        message,
                        new DelayedTransportFailureStream()))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromMilliseconds(40)));

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                () => nuget.GetVersionsAsync(
                    "package",
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task MetadataBodyDeadline_TranslatesTransportAbort()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    MetadataResponse(
                        message,
                        new DelayedTransportFailureStream()))));
        var nuget = new NuGetClient(
            client,
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(5),
                OperationTimeout = TimeSpan.FromSeconds(10),
                MetadataBodyTimeout = TimeSpan.FromMilliseconds(40),
            });

        NuGetMetadataBodyTimeoutException error =
            await Assert.ThrowsAsync<NuGetMetadataBodyTimeoutException>(
                () => nuget.GetVersionsAsync(
                    "package",
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task RequestDeadline_TranslatesMetadataIoAbort()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    MetadataResponse(
                        message,
                        new DelayedIoFailureStream()))));
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
    public async Task OperationCeiling_TranslatesMetadataIoAbort()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    MetadataResponse(
                        message,
                        new DelayedIoFailureStream()))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromMilliseconds(40)));

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                () => nuget.GetVersionsAsync(
                    "package",
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task MetadataBodyDeadline_TranslatesIoAbort()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    MetadataResponse(
                        message,
                        new DelayedIoFailureStream()))));
        var nuget = new NuGetClient(
            client,
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(5),
                OperationTimeout = TimeSpan.FromSeconds(10),
                MetadataBodyTimeout = TimeSpan.FromMilliseconds(40),
            });

        NuGetMetadataBodyTimeoutException error =
            await Assert.ThrowsAsync<NuGetMetadataBodyTimeoutException>(
                () => nuget.GetVersionsAsync(
                    "package",
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task RequestDeadline_RejectsLateSuccessfulMetadataRead()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    MetadataResponse(
                        message,
                        new LateSuccessfulReadStream()))));
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
    public async Task OperationCeiling_RejectsLateSuccessfulMetadataRead()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    MetadataResponse(
                        message,
                        new LateSuccessfulReadStream()))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromMilliseconds(40)));

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                () => nuget.GetVersionsAsync(
                    "package",
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task MetadataBodyDeadline_RejectsLateSuccessfulRead()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    MetadataResponse(
                        message,
                        new LateSuccessfulReadStream()))));
        var nuget = new NuGetClient(
            client,
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(5),
                OperationTimeout = TimeSpan.FromSeconds(10),
                MetadataBodyTimeout = TimeSpan.FromMilliseconds(40),
            });

        NuGetMetadataBodyTimeoutException error =
            await Assert.ThrowsAsync<NuGetMetadataBodyTimeoutException>(
                () => nuget.GetVersionsAsync(
                    "package",
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task MetadataTransportFailureBeforeDeadline_RemainsUnchanged()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    MetadataResponse(
                        message,
                        new ImmediateTransportFailureStream()))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromSeconds(10)));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => nuget.GetVersionsAsync(
                "package",
                cancellationToken: TestContext.Current.CancellationToken));
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
                        ? TimeSpan.FromMilliseconds(1200)
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
                async () =>
                    _ = await versions.WaitAsync(
                        TimeSpan.FromMilliseconds(1200),
                        TestContext.Current.CancellationToken));

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
        var stallingStream = new SignalingStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            async (message, cancellationToken) =>
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(1200),
                    cancellationToken);
                return StreamResponse(message, stallingStream);
            }));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(2),
                operation: TimeSpan.FromSeconds(5)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[1];
        Task<int> read = package.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken)
            .AsTask();
        await stallingStream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromMilliseconds(1200),
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(2), error.Timeout);
    }

    [Fact]
    public async Task RequestDeadline_RejectsLateSuccessfulStreamAcquisition()
    {
        using var client = new HttpClient(new DelayedHandler(
            static async (message, _) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                return StreamResponse(
                    message,
                    new MemoryStream([1, 2, 3], writable: false));
            }));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(40),
                operation: TimeSpan.FromSeconds(1)));

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                {
                    await using Stream package = await nuget.DownloadAsync(
                        "package",
                        "1.0.0",
                        cancellationToken:
                            TestContext.Current.CancellationToken);
                });

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task LateStreamAcquisitionCleanupFailureDoesNotMaskDeadline()
    {
        using var client = new HttpClient(new DelayedHandler(
            static async (message, _) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                return StreamResponse(
                    message,
                    new ThrowingDisposeMemoryStream());
            }));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(40),
                operation: TimeSpan.FromSeconds(1)));

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                {
                    await using Stream package = await nuget.DownloadAsync(
                        "package",
                        "1.0.0",
                        cancellationToken:
                            TestContext.Current.CancellationToken);
                });

        Assert.Contains(
            ExceptionTree(error),
            exception => exception is InvalidOperationException
                && exception.Message.Contains(
                    "Simulated late cleanup failure",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequestDeadline_TranslatesStreamingAcquisitionIoAbort()
    {
        using var client = new HttpClient(new DelayedHandler(
            static async (_, _) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                throw new IOException(
                    "Simulated post-deadline acquisition failure.");
            }));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(40),
                operation: TimeSpan.FromSeconds(1)));

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                {
                    await using Stream package = await nuget.DownloadAsync(
                        "package",
                        "1.0.0",
                        cancellationToken:
                            TestContext.Current.CancellationToken);
                });

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task RequestDeadline_BoundsSynchronousPackageStreamConsumption()
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
        byte[] buffer = new byte[1];
        Task<int> read = Task.Run(
            () => package.Read(buffer, 0, buffer.Length),
            TestContext.Current.CancellationToken);
        await stallingStream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(500), error.Timeout);
    }

    [Fact]
    public async Task RequestDeadline_TranslatesHttpRequestAbort()
    {
        var abortStream = new HttpRequestAbortStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(
                        message,
                        abortStream))));
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
        Task<int> read = package.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken)
            .AsTask();
        await abortStream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
    }

    [Fact]
    public async Task RequestDeadline_TranslatesSynchronousHttpRequestAbort()
    {
        var abortStream = new HttpRequestAbortStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(
                        message,
                        abortStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(500),
                operation: TimeSpan.FromSeconds(2)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[1];
        Task<int> read = Task.Run(
            () => package.Read(buffer, 0, buffer.Length),
            TestContext.Current.CancellationToken);
        await abortStream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(500), error.Timeout);
    }

    [Fact]
    public async Task HttpRequestFailureBeforeDeadline_RemainsAHttpRequestFailure()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    StreamResponse(
                        message,
                        new ImmediateTransportFailureStream()))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromSeconds(10)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[1];

        await Assert.ThrowsAsync<HttpRequestException>(
            async () =>
                _ = await package.ReadAsync(
                    buffer,
                    TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IoFailureBeforeDeadline_RemainsAnIoFailure()
    {
        using var client = new HttpClient(new DelayedHandler(
            static (message, _) =>
                Task.FromResult(
                    StreamResponse(
                        message,
                        new ImmediateTransportFailureStream()))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromSeconds(10)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[1];

        Assert.Throws<IOException>(
            () => package.Read(buffer, 0, buffer.Length));
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
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(500), error.Timeout);
    }

    [Fact]
    public async Task OperationCeiling_IncludesPackageStreamConsumption()
    {
        var stallingStream = new SignalingStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            async (message, cancellationToken) =>
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(600),
                    cancellationToken);
                return StreamResponse(message, stallingStream);
            }));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromSeconds(1)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[1];
        Task<int> read = package.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken)
            .AsTask();
        await stallingStream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromMilliseconds(700),
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(1), error.Timeout);
    }

    [Fact]
    public async Task ShortAsyncRead_DoesNotDisarmTheRequestDeadline()
    {
        var partialStream = new PartialThenStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(message, partialStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(500),
                operation: TimeSpan.FromSeconds(5)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[8192];
        Assert.Equal(
            1,
            await package.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken));
        Task<int> stalled = package.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken)
            .AsTask();
        await partialStream.StalledReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                    _ = await stalled.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(500), error.Timeout);
    }

    [Fact]
    public async Task ShortAsyncRead_DoesNotDisarmTheOperationCeiling()
    {
        var partialStream = new PartialThenStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(message, partialStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromMilliseconds(500)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[8192];
        Assert.Equal(
            1,
            await package.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken));
        Task<int> stalled = package.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken)
            .AsTask();
        await partialStream.StalledReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                async () =>
                    _ = await stalled.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(500), error.Timeout);
    }

    [Fact]
    public async Task ShortSpanRead_DoesNotDisarmTheRequestDeadline()
    {
        var partialStream = new PartialThenStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(message, partialStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(500),
                operation: TimeSpan.FromSeconds(5)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[8192];
        Assert.Equal(1, package.Read(buffer.AsSpan()));
        Task<int> stalled = Task.Run(
            () => package.Read(buffer.AsSpan()),
            TestContext.Current.CancellationToken);
        await partialStream.StalledReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                    _ = await stalled.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(500), error.Timeout);
    }

    [Fact]
    public async Task ShortArrayRead_DoesNotDisarmTheRequestDeadline()
    {
        var partialStream = new PartialThenStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(message, partialStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(500),
                operation: TimeSpan.FromSeconds(5)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[8192];
        Assert.Equal(1, package.Read(buffer, 0, buffer.Length));
        Task<int> stalled = Task.Run(
            () => package.Read(buffer, 0, buffer.Length),
            TestContext.Current.CancellationToken);
        await partialStream.StalledReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                    _ = await stalled.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(500), error.Timeout);
    }

    [Fact]
    public async Task ZeroByteValue_DoesNotDisarmTheRequestDeadline()
    {
        var partialStream = new PartialThenStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(message, partialStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(500),
                operation: TimeSpan.FromSeconds(5)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, package.ReadByte());
        Task<int> stalled = Task.Run(
            package.ReadByte,
            TestContext.Current.CancellationToken);
        await partialStream.StalledReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                    _ = await stalled.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

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
        await stallingStream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.IsNotType<NuGetRequestTimeoutException>(error);
        Assert.IsNotType<NuGetOperationTimeoutException>(error);
    }

    [Fact]
    public async Task CallerCancellation_DisposalFailureDoesNotEscape()
    {
        var stallingStream = new ThrowingDisposeStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(message, stallingStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromSeconds(10)));
        using var cancellation = new CancellationTokenSource();

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: cancellation.Token);
        byte[] buffer = new byte[1];
        Task<int> read = Task.Run(
            () => package.Read(buffer, 0, buffer.Length),
            TestContext.Current.CancellationToken);
        await stallingStream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Null(Record.Exception(cancellation.Cancel));
        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(stallingStream.DisposeAttempted);
    }

    [Fact]
    public async Task AsyncCallerCancellation_RetainsDisposalFailure()
    {
        var stallingStream = new ThrowingDisposeStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(message, stallingStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromSeconds(10)));
        using var cancellation = new CancellationTokenSource();

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: cancellation.Token);
        byte[] buffer = new byte[1];
        Task<int> read = package.ReadAsync(
                buffer,
                cancellation.Token)
            .AsTask();
        await stallingStream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Null(Record.Exception(cancellation.Cancel));
        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Contains(
            ExceptionTree(error),
            exception => exception is IOException
                && exception.Message.Contains(
                    "Simulated disposal failure",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreCancelledCallerRead_RetainsDisposalFailure()
    {
        var stallingStream = new ThrowingDisposeStallingStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(message, stallingStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromSeconds(10)));
        using var cancellation = new CancellationTokenSource();

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: cancellation.Token);
        cancellation.Cancel();

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    _ = await package.ReadAsync(
                        new byte[1].AsMemory(),
                        cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Contains(
            ExceptionTree(error),
            exception => exception is IOException
                && exception.Message.Contains(
                    "Simulated disposal failure",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreCancelledCallerReadAsync_DoesNotBlockOnCleanup()
    {
        var body = new CoordinatedDisposeStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(StreamResponse(message, body))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromSeconds(10)));
        using var cancellation = new CancellationTokenSource();

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: cancellation.Token);
        Task cancel = Task.Run(
            cancellation.Cancel,
            TestContext.Current.CancellationToken);
        await body.DisposeStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Task<ValueTask<int>> invocation = Task.Run(
            () => package.ReadAsync(
                new byte[1].AsMemory(),
                cancellation.Token),
            TestContext.Current.CancellationToken);
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken);
            Assert.True(invocation.IsCompleted);
            ValueTask<int> read = await invocation;
            Assert.False(read.IsCompleted);

            body.ReleaseDispose();
            await cancel.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            OperationCanceledException error =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    async () => _ = await read);
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        finally
        {
            body.ReleaseDispose();
        }
    }

    [Fact]
    public async Task ExpiredRequestReadAsync_DoesNotBlockOnCleanup()
    {
        var body = new CoordinatedDisposeStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(StreamResponse(message, body))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromSeconds(10)));
        using var cancellation = new CancellationTokenSource();

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: cancellation.Token);
        Task cancel = Task.Run(
            cancellation.Cancel,
            TestContext.Current.CancellationToken);
        await body.DisposeStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Task<ValueTask<int>> invocation = Task.Run(
            () => package.ReadAsync(
                new byte[1].AsMemory(),
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken);
            Assert.True(invocation.IsCompleted);
            ValueTask<int> read = await invocation;
            Assert.False(read.IsCompleted);

            body.ReleaseDispose();
            await cancel.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            OperationCanceledException error =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    async () => _ = await read);
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        finally
        {
            body.ReleaseDispose();
        }
    }

    [Fact]
    public async Task DisposeAsync_DoesNotBlockOnAbortCleanup()
    {
        var body = new CoordinatedDisposeStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(StreamResponse(message, body))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromSeconds(5),
                operation: TimeSpan.FromSeconds(10)));
        using var cancellation = new CancellationTokenSource();

        Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: cancellation.Token);
        Task cancel = Task.Run(
            cancellation.Cancel,
            TestContext.Current.CancellationToken);
        await body.DisposeStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Task<ValueTask> invocation = Task.Run(
            package.DisposeAsync,
            TestContext.Current.CancellationToken);
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken);
            Assert.True(invocation.IsCompleted);
            ValueTask dispose = await invocation;
            Assert.False(dispose.IsCompleted);

            body.ReleaseDispose();
            await cancel.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            await dispose;
        }
        finally
        {
            body.ReleaseDispose();
        }
    }

    [Theory]
    [InlineData(InlineReadCompletion.Success)]
    [InlineData(InlineReadCompletion.Cancellation)]
    [InlineData(InlineReadCompletion.Abort)]
    public async Task InlineAsyncCompletion_DoesNotDeadlockAbortCleanup(
        InlineReadCompletion completion)
    {
        var body = new InlineCompletingStream(completion);
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(StreamResponse(message, body))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(500),
                operation: TimeSpan.FromSeconds(5)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        Task<int> read = package.ReadAsync(
                new byte[1].AsMemory(),
                TestContext.Current.CancellationToken)
            .AsTask();
        await body.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(500), error.Timeout);
    }

    [Fact]
    public async Task DisposeAsync_InlineCompletionRetainsAbortFailure()
    {
        var body = new InlineDisposeCompletionFailureStream();
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(StreamResponse(message, body))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(500),
                operation: TimeSpan.FromSeconds(5)));

        Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        Task<int> read = package.ReadAsync(
                new byte[1].AsMemory(),
                TestContext.Current.CancellationToken)
            .AsTask();
        await body.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        ValueTask dispose = package.DisposeAsync();
        await body.DisposeAsyncStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));
        await dispose.AsTask().WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Contains(
            ExceptionTree(error),
            exception => exception is IOException
                && exception.Message.Contains(
                    "Simulated late abort disposal failure",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequestDeadline_DisposalFailureIsRetained()
    {
        var stallingStream = new ThrowingDisposeStallingStream(
            coordinateDisposal: true);
        using var client = new HttpClient(new DelayedHandler(
            (message, _) =>
                Task.FromResult(
                    StreamResponse(message, stallingStream))));
        var nuget = new NuGetClient(
            client,
            Options(
                request: TimeSpan.FromMilliseconds(500),
                operation: TimeSpan.FromSeconds(60)));

        await using Stream package = await nuget.DownloadAsync(
            "package",
            "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] buffer = new byte[1];
        Task<int> read = Task.Factory.StartNew(
            () => package.Read(buffer, 0, buffer.Length),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        try
        {
            await stallingStream.ReadStarted.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            await stallingStream.DisposeStarted.WaitAsync(
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);
            await stallingStream.ReadUnblocked.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<TimeoutException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromMilliseconds(500),
                        TestContext.Current.CancellationToken));
        }
        finally
        {
            stallingStream.ReleaseDispose();
        }

        NuGetRequestTimeoutException error =
            await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                async () =>
                    _ = await read.WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken));

        Assert.Contains(
            ExceptionTree(error),
            exception => exception is IOException
                && exception.Message.Contains(
                    "Simulated disposal failure",
                    StringComparison.Ordinal));
    }

    private static NuGetFetchOptions Options(
        TimeSpan request,
        TimeSpan operation) =>
        new()
        {
            RequestTimeout = request,
            OperationTimeout = operation,
        };

    private static IEnumerable<Exception> ExceptionTree(
        Exception exception)
    {
        yield return exception;

        if (exception is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
            {
                foreach (Exception descendant in ExceptionTree(inner))
                    yield return descendant;
            }
        }
        else if (exception.InnerException is Exception inner)
        {
            foreach (Exception descendant in ExceptionTree(inner))
                yield return descendant;
        }
    }

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

    private static HttpResponseMessage MetadataResponse(
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
            _disposed.Task.GetAwaiter().GetResult();
            throw new HttpRequestException("Simulated HTTP stream abort.");
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
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

    private class ImmediateTransportFailureStream : Stream
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
            throw new IOException("Simulated transport failure.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(
                new HttpRequestException("Simulated transport failure."));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class DelayedTransportFailureStream :
        ImmediateTransportFailureStream
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            throw new HttpRequestException(
                "Simulated post-deadline transport failure.");
        }
    }

    private sealed class DelayedIoFailureStream :
        ImmediateTransportFailureStream
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            throw new IOException(
                "Simulated post-deadline transport failure.");
        }
    }

    private sealed class LateSuccessfulReadStream : Stream
    {
        private static readonly byte[] Payload =
            """{"versions":["1.0.0"]}"""u8.ToArray();
        private int _offset;

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
            if (_offset == 0)
                await Task.Delay(TimeSpan.FromMilliseconds(100));

            int count = Math.Min(
                buffer.Length,
                Payload.Length - _offset);
            if (count == 0)
                return 0;

            Payload.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class PartialThenStallingStream : Stream
    {
        private readonly ManualResetEventSlim _disposed = new();
        private readonly TaskCompletionSource _disposedAsync =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stalledReadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public Task StalledReadStarted => _stalledReadStarted.Task;

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
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                buffer[offset] = 0;
                return 1;
            }

            _stalledReadStarted.TrySetResult();
            _disposed.Wait();
            throw new ObjectDisposedException(
                nameof(PartialThenStallingStream));
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                buffer.Span[0] = 0;
                return 1;
            }

            _stalledReadStarted.TrySetResult();
            await _disposedAsync.Task;
            throw new ObjectDisposedException(
                nameof(PartialThenStallingStream));
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

    private sealed class ThrowingDisposeStallingStream : Stream
    {
        private readonly ManualResetEventSlim _disposed = new();
        private readonly TaskCompletionSource _disposedAsync =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readUnblocked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim? _releaseDispose;
        private int _disposeAttempted;

        public ThrowingDisposeStallingStream(
            bool coordinateDisposal = false)
        {
            if (coordinateDisposal)
                _releaseDispose = new();
        }

        public Task DisposeStarted => _disposeStarted.Task;
        public Task ReadUnblocked => _readUnblocked.Task;
        public Task ReadStarted => _readStarted.Task;
        public bool DisposeAttempted =>
            Volatile.Read(ref _disposeAttempted) != 0;

        public void ReleaseDispose() => _releaseDispose?.Set();

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
            _readStarted.TrySetResult();
            _disposed.Wait();
            _readUnblocked.TrySetResult();
            throw new ObjectDisposedException(
                nameof(ThrowingDisposeStallingStream));
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            await _disposedAsync.Task;
            _readUnblocked.TrySetResult();
            throw new ObjectDisposedException(
                nameof(ThrowingDisposeStallingStream));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing
                && Interlocked.Exchange(ref _disposeAttempted, 1) == 0)
            {
                bool throwDisposalFailure =
                    _releaseDispose is null || !_releaseDispose.IsSet;
                _disposed.Set();
                _disposedAsync.TrySetResult();
                _disposeStarted.TrySetResult();
                if (_releaseDispose is null)
                    Thread.Sleep(50);
                else if (!_releaseDispose.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException(
                        "Timed out waiting to release simulated disposal.");

                if (throwDisposalFailure)
                    throw new IOException("Simulated disposal failure.");
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

    private sealed class ThrowingDisposeMemoryStream : MemoryStream
    {
        public ThrowingDisposeMemoryStream()
            : base([1, 2, 3], writable: false)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                throw new InvalidOperationException(
                    "Simulated late cleanup failure.");
            }
        }
    }

    private sealed class CoordinatedDisposeStream : Stream
    {
        private readonly TaskCompletionSource _disposeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _releaseDispose = new();
        private int _disposeAttempted;

        public Task DisposeStarted => _disposeStarted.Task;

        public void ReleaseDispose() => _releaseDispose.Set();

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

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        protected override void Dispose(bool disposing)
        {
            if (disposing
                && Interlocked.Exchange(ref _disposeAttempted, 1) == 0)
            {
                _disposeStarted.TrySetResult();
                _releaseDispose.Wait();
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

    public enum InlineReadCompletion
    {
        Success,
        Cancellation,
        Abort,
    }

    private sealed class InlineCompletingStream(
        InlineReadCompletion completion) : Stream
    {
        private readonly TaskCompletionSource<int> _read = new();
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Memory<byte> _buffer;
        private CancellationToken _cancellationToken;

        public Task ReadStarted => _readStarted.Task;

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

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _buffer = buffer;
            _cancellationToken = cancellationToken;
            _readStarted.TrySetResult();
            return new ValueTask<int>(_read.Task);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                switch (completion)
                {
                    case InlineReadCompletion.Success:
                        _buffer.Span[0] = 1;
                        _read.TrySetResult(1);
                        break;
                    case InlineReadCompletion.Cancellation:
                        _read.TrySetCanceled(_cancellationToken);
                        break;
                    case InlineReadCompletion.Abort:
                        _read.TrySetException(
                            new ObjectDisposedException(
                                nameof(InlineCompletingStream)));
                        break;
                }
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

    private sealed class InlineDisposeCompletionFailureStream : Stream
    {
        private readonly TaskCompletionSource<int> _read = new();
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposeAsync = new();
        private readonly TaskCompletionSource _disposeAsyncStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public Task ReadStarted => _readStarted.Task;
        public Task DisposeAsyncStarted => _disposeAsyncStarted.Task;

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

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            return new ValueTask<int>(_read.Task);
        }

        public override ValueTask DisposeAsync()
        {
            _disposeAsyncStarted.TrySetResult();
            return new ValueTask(_disposeAsync.Task);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing
                && Interlocked.Increment(ref _disposeCount) == 1)
            {
                _disposeAsync.TrySetResult();
                _read.TrySetException(
                    new ObjectDisposedException(
                        nameof(InlineDisposeCompletionFailureStream)));
                throw new IOException(
                    "Simulated late abort disposal failure.");
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
}
