namespace DotnetInspector.Tests;

/// <summary>
/// The single place this test assembly is allowed to redirect the console.
/// <para>
/// <c>Console.Out</c> and <c>Console.Error</c> are process-global, so two tests that
/// redirect them concurrently steal each other's output: one sees an empty writer and
/// fails its assertion, the other sees text it never produced. Worse, each restores the
/// writer it captured on entry, so an interleaved pair can leave the console pointing at
/// a disposed <see cref="StringWriter"/> after both have finished. That was the
/// order-dependent flake in #3416, where the failing member of the pair varied per run.
/// </para>
/// <para>
/// Every capture therefore goes through the same semaphore. Redirecting the console
/// anywhere else in this assembly re-opens the race even if this type is used correctly
/// everywhere else, so
/// <c>ConsoleCaptureTests.TestAssemblyRedirectsConsoleOnlyThroughConsoleCapture</c>
/// fails if another file does it.
/// </para>
/// </summary>
static class ConsoleCapture
{
    // Serialize access to Console.Out/Error to prevent parallel test interference
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(Func<Task<int>> action)
    {
        await _lock.WaitAsync();
        var origOut = Console.Out;
        // Capturing the stream is the one thing that must reach past the
        // stderr-ownership rule (#3319): the rule keeps the product from writing
        // here, and this is the harness that reads what the product wrote.
#pragma warning disable RS0030
        var origErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            var exitCode = await action();
            return (exitCode, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
#pragma warning restore RS0030
            _lock.Release();
        }
    }

    /// <summary>
    /// Captures a synchronous action under the same lock as <see cref="RunAsync(Func{Task{int}})"/>.
    /// Kept asynchronous deliberately: a blocking overload would park a thread-pool thread
    /// while waiting on the semaphore, and xUnit runs tests on the pool, so a handful of
    /// contending callers can starve it and time out unrelated async tests.
    /// <para>
    /// Anything the action mutates that is also process-global — an environment variable
    /// the product reads, for example — should be set and restored inside
    /// <paramref name="action"/> so it is covered by the same exclusion.
    /// </para>
    /// </summary>
    public static async Task<(string Output, string Error)> RunAsync(Action action)
    {
        var (_, output, error) = await RunAsync(() =>
        {
            action();
            return Task.FromResult(0);
        });

        return (output, error);
    }
}
