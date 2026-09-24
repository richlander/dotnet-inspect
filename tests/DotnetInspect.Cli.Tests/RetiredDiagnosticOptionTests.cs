using System.Diagnostics;
using System.Reflection;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Transfer receipt design gate 9: the retired <c>--info</c> and
/// <c>--trace-mermaid</c> experiences are gone. Both flags are rejected as
/// unknown options, and their environment variables no longer change output.
/// Owned by <c>docs/design/package-transfer-receipt.md#retired-experiences</c>.
/// <para>
/// These cases run the real executable because both flags used to be stripped
/// from the argument array by top-level code in <c>Program.cs</c> before any
/// parser saw them, so the in-process harness could not observe that path.
/// </para>
/// <para>
/// Joined to the <c>Console</c> collection so these child processes cannot run
/// alongside timing-sensitive tests; the collection is the suite's
/// serialization group for process-global and timing-sensitive state.
/// </para>
/// </summary>
[Collection("Console")]
public sealed class RetiredDiagnosticOptionTests : IDisposable
{
    private readonly string _cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "dotnet-inspect-retired-options-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("--info", "skill", "list")]
    [InlineData("--info", "type", "--platform", "System.Runtime")]
    [InlineData("--trace-mermaid", "skill", "list")]
    [InlineData("--trace-mermaid", "type", "--platform", "System.Runtime")]
    public void RetiredOption_IsRejectedAsUnknown(string retired, params string[] command)
    {
        var (exitCode, _, error) = RunCli([.. command, retired]);

        Assert.NotEqual(0, exitCode);
        Assert.Contains(retired, error, StringComparison.Ordinal);
        Assert.Contains("Unrecognized", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("# Info", error, StringComparison.Ordinal);
        Assert.DoesNotContain("flowchart", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DOTNET_INSPECT_INFO")]
    [InlineData("DOTNET_INSPECT_TRACE_MERMAID")]
    public void RetiredEnvironmentVariable_HasNoEffect(string variable)
    {
        var (exitCode, _, error) = RunCli(
            ["skill", "list"],
            new Dictionary<string, string> { [variable] = "1" });

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("# Info", error, StringComparison.Ordinal);
        Assert.DoesNotContain("flowchart", error, StringComparison.Ordinal);
    }

    private (int ExitCode, string Output, string Error) RunCli(
        string[] args,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        string executable = Path.Combine(
            Path.GetDirectoryName(ProductAssemblyPath())!,
            OperatingSystem.IsWindows() ? "dotnet-inspect.exe" : "dotnet-inspect");
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        // Out-of-process, so both settings travel as environment. The cache
        // directory is per-test and temporary.
        psi.Environment["DOTNET_INSPECT_OFFLINE"] = "1";
        psi.Environment["DOTNET_INSPECT_CACHE_DIR"] = _cacheDirectory;
        foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
        {
            psi.Environment[name] = value;
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        // Drain both pipes before waiting; a synchronous read of one blocks
        // until EOF and lets the child deadlock filling the other.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            OutOfProcessCliProcess.KillAndWaitForExit(process, TimeSpan.FromSeconds(10));
            throw new TimeoutException($"{executable} did not exit.");
        }

        Task.WaitAll([stdout, stderr], 10_000);
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string ProductAssemblyPath()
    {
        // Deliberately the product copy in *this* test project's output, not the
        // one under artifacts/bin/dotnet-inspect. Anyone checking these cases are
        // non-vacuous by restoring the injected token must rebuild the test
        // project, not just the product: building the product alone leaves this
        // copy stale and the tamper appears to change nothing.
        string path = Path.Combine(AppContext.BaseDirectory, "dotnet-inspect.dll");
        if (File.Exists(path))
        {
            return path;
        }

        var located = Assembly.Load("dotnet-inspect").Location;
        return string.IsNullOrEmpty(located)
            ? throw new FileNotFoundException("Could not locate the dotnet-inspect product assembly.")
            : located;
    }
}
