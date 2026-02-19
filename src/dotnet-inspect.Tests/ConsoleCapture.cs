namespace DotnetInspector.Tests;

static class ConsoleCapture
{
    // Serialize access to Console.Out/Error to prevent parallel test interference
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(Func<Task<int>> action)
    {
        await _lock.WaitAsync();
        var origOut = Console.Out;
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
            _lock.Release();
        }
    }
}
