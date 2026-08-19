using Xunit;

namespace NuGetFetch.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThreadPoolDeadlineCollection
{
    public const string Name = "ThreadPoolDeadlineGuard";
}

[Collection(ThreadPoolDeadlineCollection.Name)]
public sealed class NuGetDeadlineRaceTests
{
    [Fact]
    public async Task RequestCompletion_UsesElapsedTimeWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                using var operation = CreateOperation();

                NuGetRequestTimeoutException error =
                    await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                        () => operation.RunRequestAsync(
                            _ =>
                            {
                                Thread.Sleep(TimeSpan.FromMilliseconds(250));
                                return Task.FromResult(42);
                            }));

                Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
            });
    }

    [Fact]
    public async Task StreamConsumption_UsesElapsedTimeWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                using var operation = CreateOperation();
                using var owner = new MemoryStream();
                using Stream response =
                    await operation.RunStreamingRequestAsync(
                        _ => Task.FromResult<(Stream, IDisposable)>(
                            (new MemoryStream([42], writable: false), owner)));

                Thread.Sleep(TimeSpan.FromMilliseconds(250));

                NuGetRequestTimeoutException error =
                    Assert.Throws<NuGetRequestTimeoutException>(
                        () => response.ReadByte());
                Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
            });
    }

    [Fact]
    public async Task MetadataBodyCompletion_UsesElapsedTimeWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                await using var stream = new MemoryStream();

                NuGetMetadataBodyTimeoutException error =
                    await Assert.ThrowsAsync<NuGetMetadataBodyTimeoutException>(
                        () => NuGetMetadataReader.ReadStreamAsync(
                            stream,
                            static (_, _) =>
                            {
                                Thread.Sleep(TimeSpan.FromMilliseconds(250));
                                return ValueTask.FromResult(42);
                            },
                            CreateOptions(),
                            TestContext.Current.CancellationToken).AsTask());

                Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
            });
    }

    [Fact]
    public async Task MetadataBodyAbort_UsesElapsedTimeWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                await using var stream = new MemoryStream();

                NuGetMetadataBodyTimeoutException error =
                    await Assert.ThrowsAsync<NuGetMetadataBodyTimeoutException>(
                        () => NuGetMetadataReader.ReadStreamAsync<int>(
                            stream,
                            static (_, _) =>
                            {
                                Thread.Sleep(TimeSpan.FromMilliseconds(250));
                                return ValueTask.FromException<int>(
                                    new IOException(
                                        "Simulated post-deadline transport failure."));
                            },
                            CreateOptions(),
                            TestContext.Current.CancellationToken).AsTask());

                OperationCanceledException cancellation =
                    Assert.IsType<OperationCanceledException>(
                        error.InnerException);
                Assert.IsType<IOException>(
                    cancellation.InnerException);
                Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
            });
    }

    [Fact]
    public async Task MetadataBodyCancellation_UsesElapsedTimeWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                await using var stream = new MemoryStream();

                NuGetMetadataBodyTimeoutException error =
                    await Assert.ThrowsAsync<NuGetMetadataBodyTimeoutException>(
                        () => NuGetMetadataReader.ReadStreamAsync<int>(
                            stream,
                            static (_, _) =>
                            {
                                Thread.Sleep(TimeSpan.FromMilliseconds(250));
                                return ValueTask.FromException<int>(
                                    new OperationCanceledException(
                                        "Simulated unassociated cancellation."));
                            },
                            CreateOptions(),
                            TestContext.Current.CancellationToken).AsTask());

                Assert.IsType<OperationCanceledException>(
                    error.InnerException);
                Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
            });
    }

    [Theory]
    [InlineData(40, 80)]
    [InlineData(120, 80)]
    public async Task OperationCeiling_OutranksMetadataBodyDeadlineWhenTimerCallbackIsDelayed(
        int operationTimeoutMilliseconds,
        int bodyTimeoutMilliseconds)
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                var options = new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(5),
                    OperationTimeout =
                        TimeSpan.FromMilliseconds(operationTimeoutMilliseconds),
                    MetadataBodyTimeout =
                        TimeSpan.FromMilliseconds(bodyTimeoutMilliseconds),
                };
                using var operation = new NuGetOperationDeadline(
                    options,
                    Timeout.InfiniteTimeSpan,
                    TestContext.Current.CancellationToken);
                await using var stream = new MemoryStream();

                NuGetOperationTimeoutException error =
                    await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                        () => operation.RunRequestAsync(
                            token => NuGetMetadataReader.ReadStreamAsync(
                                stream,
                                static (_, _) =>
                                {
                                    Thread.Sleep(
                                        TimeSpan.FromMilliseconds(250));
                                    return ValueTask.FromResult(42);
                                },
                                options,
                                token).AsTask()));

                Assert.Equal(options.OperationTimeout, error.Timeout);
            });
    }

    [Fact]
    public async Task RequestDeadline_OutranksMetadataBodyDeadlineWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                var options = new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(120),
                    OperationTimeout = TimeSpan.FromSeconds(5),
                    MetadataBodyTimeout = TimeSpan.FromMilliseconds(80),
                };
                using var operation = new NuGetOperationDeadline(
                    options,
                    Timeout.InfiniteTimeSpan,
                    TestContext.Current.CancellationToken);
                await using var stream = new DelayedReadStream(
                    TimeSpan.FromMilliseconds(250));

                NuGetRequestTimeoutException error =
                    await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                        () => operation.RunRequestAsync(
                            token => NuGetMetadataReader.ReadStreamAsync(
                                stream,
                                ReadOneByteAsync,
                                options,
                                token).AsTask()));

                Assert.Equal(options.RequestTimeout, error.Timeout);
            });
    }

    [Fact]
    public async Task MetadataBodyDeadline_RemainsAuthoritativeWhenOuterDeadlinesHaveNotExpired()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                var options = new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(5),
                    OperationTimeout = TimeSpan.FromSeconds(10),
                    MetadataBodyTimeout = TimeSpan.FromMilliseconds(40),
                };
                using var operation = new NuGetOperationDeadline(
                    options,
                    Timeout.InfiniteTimeSpan,
                    TestContext.Current.CancellationToken);
                await using var stream = new DelayedReadStream(
                    TimeSpan.FromMilliseconds(250));

                NuGetMetadataBodyTimeoutException error =
                    await Assert.ThrowsAsync<NuGetMetadataBodyTimeoutException>(
                        () => operation.RunRequestAsync(
                            token => NuGetMetadataReader.ReadStreamAsync(
                                stream,
                                ReadOneByteAsync,
                                options,
                                token).AsTask()));

                Assert.Equal(options.MetadataBodyTimeout, error.Timeout);
            });
    }

    private static ValueTask<int> ReadOneByteAsync(
        Stream stream,
        CancellationToken cancellationToken) =>
        stream.ReadAsync(new byte[1], cancellationToken);

    private static NuGetOperationDeadline CreateOperation() =>
        new(
            CreateOptions(),
            Timeout.InfiniteTimeSpan,
            TestContext.Current.CancellationToken);

    private static NuGetFetchOptions CreateOptions() =>
        new()
        {
            RequestTimeout = TimeSpan.FromMilliseconds(40),
            OperationTimeout = TimeSpan.FromSeconds(30),
            MetadataBodyTimeout = TimeSpan.FromMilliseconds(40),
        };

    private static async Task WithDelayedTimerCallbacksAsync(
        Func<Task> action)
    {
        ThreadPool.GetMinThreads(out int originalMinWorkers, out int minIo);
        ThreadPool.GetMaxThreads(out int originalMaxWorkers, out int maxIo);
        using var blockerStarted = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        bool currentThreadIsWorker = Thread.CurrentThread.IsThreadPoolThread;
        CancellationToken testCancellation =
            TestContext.Current.CancellationToken;

        try
        {
            Assert.True(ThreadPool.SetMinThreads(1, minIo));
            Assert.True(ThreadPool.SetMaxThreads(1, maxIo));
            if (!currentThreadIsWorker)
            {
                ThreadPool.QueueUserWorkItem(
                    _ =>
                    {
                        blockerStarted.Set();
                        releaseBlocker.Wait(testCancellation);
                    });
                blockerStarted.Wait(testCancellation);
            }

            await action();
        }
        finally
        {
            releaseBlocker.Set();
            Assert.True(ThreadPool.SetMaxThreads(originalMaxWorkers, maxIo));
            Assert.True(ThreadPool.SetMinThreads(originalMinWorkers, minIo));
        }
    }

    private sealed class DelayedReadStream(TimeSpan delay)
        : MemoryStream([42], writable: false)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Thread.Sleep(delay);
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
