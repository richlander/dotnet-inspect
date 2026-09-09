using DotnetInspector.Core;

namespace DotnetInspector.Services.Tests;

public class AsyncCacheTests
{
    [Fact]
    public async Task GetOrAddAsync_ConcurrentRequestsShareOneTask()
    {
        var cache = new AsyncCache<string, object>();
        var release = new TaskCompletionSource<object>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int invocations = 0;

        Task<object>[] requests = Enumerable.Range(0, 32)
            .Select(_ => cache.GetOrAddAsync(
                "key",
                _ =>
                {
                    Interlocked.Increment(ref invocations);
                    return release.Task;
                }))
            .ToArray();

        Assert.All(requests, request => Assert.Same(requests[0], request));
        Assert.Equal(1, Volatile.Read(ref invocations));

        var value = new object();
        release.SetResult(value);
        object[] results = await Task.WhenAll(requests);

        Assert.All(results, result => Assert.Same(value, result));
        Assert.Same(
            requests[0],
            cache.GetOrAddAsync(
                "key",
                _ => Task.FromResult(new object())));
    }

    [Fact]
    public async Task GetOrAddAsync_FaultedResolutionCanRetry()
    {
        var cache = new AsyncCache<string, int>();
        var failure = new InvalidOperationException("failed");

        var first = cache.GetOrAddAsync(
            "key",
            _ => Task.FromException<int>(failure));

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => first));

        var second = cache.GetOrAddAsync(
            "key",
            _ => Task.FromResult(42));

        Assert.NotSame(first, second);
        Assert.Equal(42, await second);
    }

    [Fact]
    public async Task GetOrAddAsync_CancelledResolutionCanRetry()
    {
        var cache = new AsyncCache<string, int>();

        var first = cache.GetOrAddAsync(
            "key",
            _ => Task.FromCanceled<int>(new CancellationToken(canceled: true)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        var second = cache.GetOrAddAsync(
            "key",
            _ => Task.FromResult(42));

        Assert.NotSame(first, second);
        Assert.Equal(42, await second);
    }

    [Fact]
    public async Task GetOrAddAsync_RejectedResultCanRetry()
    {
        var cache = new AsyncCache<string, string?>();

        var first = cache.GetOrAddAsync(
            "key",
            _ => Task.FromResult<string?>(null),
            static value => value is not null);

        Assert.Null(await first);

        var second = cache.GetOrAddAsync(
            "key",
            _ => Task.FromResult<string?>("value"),
            static value => value is not null);

        Assert.NotSame(first, second);
        Assert.Equal("value", await second);
    }
}
