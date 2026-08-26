using System.Net;
using DotnetInspector.Packages;
using NuGetFetch;
using Xunit;

namespace DotnetInspector.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NuGetSearchDeadlineCollection
{
    public const string Name = "NuGetSearchDeadlineGuard";
}

[Collection(NuGetSearchDeadlineCollection.Name)]
public sealed class NuGetSearchDeadlineRaceTests
{
    [Fact]
    public async Task ServiceIndexCompletion_UsesElapsedTimeWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                var handler = new DelayedServiceIndexHandler();
                using var client = new HttpClient(handler);

                var error = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => NuGetSearchService.SearchAsync(
                        client,
                        "Contoso",
                        sourceOptions: new NuGetSourceOptions
                        {
                            Sources = ["https://feed.example/v3/index.json"],
                        },
                        fetchOptions: new NuGetFetchOptions
                        {
                            RequestTimeout = TimeSpan.FromMilliseconds(40),
                            OperationTimeout = TimeSpan.FromSeconds(30),
                        }));

                Assert.Contains(
                    nameof(NuGetRequestTimeoutException),
                    error.Message,
                    StringComparison.Ordinal);
                Assert.Equal(1, handler.RequestCount);
            });
    }

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

    private sealed class DelayedServiceIndexHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Thread.Sleep(TimeSpan.FromMilliseconds(250));
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"resources":[
                          {"@id":"https://feed.example/v3/query","@type":"SearchQueryService/3.5.0"}
                        ]}
                        """),
                    RequestMessage = request,
                });
        }
    }
}
