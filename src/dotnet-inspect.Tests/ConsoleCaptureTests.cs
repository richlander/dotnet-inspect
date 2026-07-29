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
    /// wrote — no other worker's token, and never an empty writer. Without the shared
    /// semaphore, concurrent workers overwrite <c>Console.Out</c> and this fails.
    /// </summary>
    [Fact]
    public async Task ConcurrentCapturesDoNotObserveEachOthersOutput()
    {
        var failures = new ConcurrentBag<string>();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async i =>
        {
            string token = $"token-{i}";
            var (output, error) = await ConsoleCapture.RunAsync(() =>
            {
                Console.Write(token);
                Console.Error.Write(token);
            });

            if (output != token)
                failures.Add($"worker {i} stdout: expected '{token}', got '{output}'");
            if (error != token)
                failures.Add($"worker {i} stderr: expected '{token}', got '{error}'");
        }));

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
        var failures = new ConcurrentBag<string>();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async i =>
        {
            string token = $"mixed-{i}";
            string observed = i % 2 == 0
                ? (await ConsoleCapture.RunAsync(() => Console.Write(token))).Output
                : (await ConsoleCapture.RunAsync(() =>
                {
                    Console.Write(token);
                    return Task.FromResult(0);
                })).Output;

            if (observed != token)
                failures.Add($"worker {i}: expected '{token}', got '{observed}'");
        }));

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
