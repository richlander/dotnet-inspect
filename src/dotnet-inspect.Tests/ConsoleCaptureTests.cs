using System.Collections.Concurrent;

namespace DotnetInspector.Tests;

/// <summary>
/// Gates for the console-redirection invariant behind #3416. The flake there was not a
/// wrong assertion in any one test; it was two tests redirecting the process-global
/// console at once. Both halves of the invariant are gated: that the shared lock actually
/// excludes concurrent captures, and that no other file in this assembly redirects the
/// console outside that lock.
/// </summary>
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
    /// The structural half, and the one that keeps the fix from decaying. The behavioral
    /// gates above pass even if a brand-new test redirects the console on its own, because
    /// they only exercise <see cref="ConsoleCapture"/> itself. This derives its subject
    /// from the test sources on disk rather than from a hardcoded list, so a new offending
    /// file fails it without anyone remembering to update anything.
    /// </summary>
    [Fact]
    public void TestAssemblyRedirectsConsoleOnlyThroughConsoleCapture()
    {
        string testSourceRoot = Path.Combine(FindRepositoryRoot(), "src", "dotnet-inspect.Tests");
        List<string> offenders = [];

        foreach (string path in Directory.EnumerateFiles(testSourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            // Build output under bin/obj contains generated and copied sources.
            string relative = Path.GetRelativePath(testSourceRoot, path);
            string[] segments = relative.Split(Path.DirectorySeparatorChar);
            if (segments.Contains("bin") || segments.Contains("obj"))
                continue;

            if (string.Equals(segments[^1], "ConsoleCapture.cs", StringComparison.Ordinal))
                continue;

            string text = File.ReadAllText(path);
            if (text.Contains(SetOutCall, StringComparison.Ordinal)
                || text.Contains(SetErrorCall, StringComparison.Ordinal))
            {
                offenders.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
            }
        }

        offenders.Sort(StringComparer.Ordinal);

        Assert.True(
            offenders.Count == 0,
            "Console.Out/Error are process-global; redirecting them outside ConsoleCapture "
                + "reintroduces the #3416 order-dependent flake. Use ConsoleCapture.Capture or "
                + "ConsoleCapture.RunAsync instead. Offending files: "
                + string.Join(", ", offenders));
    }

    /// <summary>
    /// Non-vacuity for the scan above: it must actually be reading sources that contain
    /// the pattern, or it would pass simply by finding nothing. ConsoleCapture.cs is the
    /// one file expected to match, so it is the natural canary.
    /// </summary>
    [Fact]
    public void ConsoleRedirectionScanReadsRealSources()
    {
        string consoleCapture = Path.Combine(
            FindRepositoryRoot(), "src", "dotnet-inspect.Tests", "ConsoleCapture.cs");

        Assert.True(File.Exists(consoleCapture), $"Expected to find {consoleCapture}.");
        Assert.Contains(SetOutCall, File.ReadAllText(consoleCapture), StringComparison.Ordinal);
    }

    // Composed rather than written literally so this file is not itself an offender.
    static readonly string SetOutCall = $"Console.{nameof(Console.SetOut)}(";
    static readonly string SetErrorCall = $"Console.{nameof(Console.SetError)}(";

    static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing dotnet-inspect.slnx.");
    }
}
