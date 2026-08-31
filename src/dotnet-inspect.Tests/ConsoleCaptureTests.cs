using System.Collections.Concurrent;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

public class ConsoleCaptureParallelCollectionTests
{
    [Fact]
    public async Task CaptureFromParallelCollectionIsRejected()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ConsoleCapture.RunAsync(() => Task.FromResult(0)));

        Assert.Contains("assembly-exclusive", exception.Message);
    }
}

// This harness deliberately holds ConsoleCapture across assertions. Isolate it from every
// external test so acquiring the semaphore tests this class, not suite scheduling (#4141).
[CollectionDefinition("ConsoleCaptureGuard", DisableParallelization = true)]
public class ConsoleCaptureGuardCollection;

/// <summary>
/// Gates for the console-redirection invariant behind #3416. The flake there was not a
/// wrong assertion in any one test; it was two tests redirecting the process-global
/// console at once. These tests prove that the shared lock excludes concurrent captures;
/// the compiler's banned-API analyzer prevents another file in this assembly from
/// redirecting the console outside that lock.
/// </summary>
[Collection("ConsoleCaptureGuard")]
public class ConsoleCaptureTests
{
    /// <summary>
    /// The behavioral half. Each capture must observe exactly the text its own action
    /// wrote — no other worker's token, and never an empty writer.
    /// </summary>
    /// <remarks>
    /// Forcing real overlap takes deliberate work, and an earlier version of this test got
    /// it wrong: <see cref="ConsoleCapture.RunAsync(Action)"/> completes synchronously on an
    /// uncontended semaphore, so plain <c>async</c> lambdas ran start-to-finish one after
    /// another and the gate passed with the lock deleted. Two things fix that. Workers are
    /// started with <c>Task.Run</c> and released together, so they genuinely contend; and
    /// each sleeps <em>before</em> writing, because the damaging window is between
    /// <c>ConsoleCapture</c> redirecting the console and the action using it. A peer that
    /// redirects during that window steals this worker's writes.
    /// </remarks>
    [Fact]
    public async Task ConcurrentCapturesDoNotObserveEachOthersOutput()
    {
        const int workers = 8;
        var failures = new ConcurrentBag<string>();
        using var ready = new CountdownEvent(workers);
        var go = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, workers).Select(i => Task.Run(async () =>
        {
            ready.Signal();
            await go.Task;

            string token = $"token-{i}";
            var (output, error) = await ConsoleCapture.RunAsync(() =>
            {
                Thread.Sleep(25);
                Console.Write(token);
#pragma warning disable RS0030 // Proving the capture works requires writing to the stream it captures (#3319).
                Console.Error.Write(token);
#pragma warning restore RS0030
            });

            if (output != token)
                failures.Add($"worker {i} stdout: expected '{token}', got '{output}'");
            if (error != token)
                failures.Add($"worker {i} stderr: expected '{token}', got '{error}'");
        })).ToArray();

        ready.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        go.SetResult();
        await Task.WhenAll(tasks);

        Assert.Empty(failures);
    }

    /// <summary>
    /// The two capture overloads share one lock, so a mixed workload must be serialized
    /// too. This is the shape the #3416 flake actually took: the tests that bypassed the
    /// lock were synchronous, and the tests whose output they corrupted were not.
    /// </summary>
    [Fact]
    public async Task SyncAndAsyncCapturesExcludeEachOther()
    {
        const int workers = 8;
        var failures = new ConcurrentBag<string>();
        using var ready = new CountdownEvent(workers);
        var go = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, workers).Select(i => Task.Run(async () =>
        {
            ready.Signal();
            await go.Task;

            string token = $"mixed-{i}";
            string observed = i % 2 == 0
                ? (await ConsoleCapture.RunAsync(() =>
                {
                    Thread.Sleep(25);
                    Console.Write(token);
                })).Output
                : (await ConsoleCapture.RunAsync(async () =>
                {
                    await Task.Delay(25);
                    Console.Write(token);
                    return 0;
                })).Output;

            if (observed != token)
                failures.Add($"worker {i}: expected '{token}', got '{observed}'");
        })).ToArray();

        ready.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        go.SetResult();
        await Task.WhenAll(tasks);

        Assert.Empty(failures);
    }

    /// <summary>
    /// Projection-audit unit tests deliberately trigger diagnostics. Those diagnostics
    /// must not flow through process-wide stderr, where they can be absorbed by an
    /// unrelated test that currently owns the console capture (#3538).
    /// </summary>
    [Fact]
    public async Task ProjectionAuditDiagnosticsDoNotLeakIntoConcurrentCapture()
    {
        var captureEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCapture = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var capture = ConsoleCapture.RunAsync(async () =>
        {
            captureEntered.SetResult();
            await releaseCapture.Task.WaitAsync(TestContext.Current.CancellationToken);
            return 0;
        });

        await captureEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

        bool exclusive;
        int exitCode;
        var diagnostics = new List<string>();
        try
        {
            var root = CommandLineBuilder.CreateRootCommand();
            var conflict = root.Parse(
                ["library", "System.Runtime", "-S", "References", "--count", "--print"]);
            exclusive = ProjectionAudit.ValidateExclusive(conflict, diagnostics.Add);

            var result = root
                .Parse(["library", "System.Runtime", "-S", "References", "--count"]);
            using var request = ProjectionAudit.BeginRequest(result);
            exitCode = ProjectionAudit.Verify(0, diagnostics.Add);
        }
        finally
        {
            ProjectionAudit.ResetForTesting();
            releaseCapture.TrySetResult();
        }

        var (_, _, capturedError) = await capture;

        Assert.False(exclusive);
        Assert.Equal(1, exitCode);
        Assert.Collection(
            diagnostics,
            diagnostic => Assert.Contains("--count cannot be combined with --print", diagnostic),
            diagnostic => Assert.Contains("produced unprojected output", diagnostic));
        Assert.Empty(capturedError);
    }
}
