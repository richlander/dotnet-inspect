namespace DotnetInspector.Services.Tests;

/// <summary>
/// The single place this test assembly is allowed to redirect the console.
/// </summary>
/// <remarks>
/// <para>
/// <c>Console.Error</c> is process-global and xUnit runs test classes in parallel, so two tests
/// that redirect it concurrently steal each other's output, and each restores the writer it
/// captured on entry -- an interleaved pair can leave the console pointing at a disposed
/// <see cref="StringWriter"/> after both have finished. Every capture therefore goes through the
/// same semaphore.
/// </para>
/// <para>
/// The hazard is not limited to tests that redirect. Any test that causes product code to write
/// to stderr while another test holds the redirect pollutes that test's captured output, and the
/// victim then fails on text it never produced. That happened for real in the CLI test assembly
/// once refusals began reporting unconditionally: four unrelated tests failed intermittently and
/// passed in isolation. Tests here that trigger a refusal should run inside this helper for the
/// same reason, whether or not they assert on the text.
/// </para>
/// </remarks>
static class StderrCapture
{
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public static async Task<string> RunAsync(Action action)
    {
        await _lock.WaitAsync();
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetError(original);
            _lock.Release();
        }
    }
}
