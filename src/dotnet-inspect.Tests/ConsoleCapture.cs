namespace DotnetInspector.Tests;

static class ConsoleCapture
{
    // Serialize access to Console.Out/Error to prevent parallel test interference
    private static readonly SemaphoreSlim _lock = new(1, 1);

    /// <param name="nugetPackagesRoot">
    /// When set, redirects the NuGet package root for the duration of the run. The assignment
    /// happens under the same lock that serializes console capture, so a run that needs a fixture
    /// package root cannot be observed by a concurrent run that expects the real one.
    /// </param>
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        Func<Task<int>> action,
        string? nugetPackagesRoot = null)
    {
        await _lock.WaitAsync();
        var origOut = Console.Out;
        var origErr = Console.Error;
        var origPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (nugetPackagesRoot != null)
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", nugetPackagesRoot);
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
            if (nugetPackagesRoot != null)
                Environment.SetEnvironmentVariable("NUGET_PACKAGES", origPackages);
            _lock.Release();
        }
    }
}
