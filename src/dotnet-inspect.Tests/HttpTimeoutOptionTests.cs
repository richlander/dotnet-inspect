using System.Diagnostics;
using System.Reflection;

namespace DotnetInspector.Tests;

/// <summary>
/// Covers <c>--http-timeout</c>, which is stripped from the argument array in
/// <c>Program.cs</c> before command parsing because <c>HttpClientFactory.Shared</c> is built
/// lazily on first use and the default has to be settled before any client exists.
/// </summary>
/// <remarks>
/// These run the built executable rather than calling a helper, because the parsing under test
/// lives in top-level statements that no in-process entry point exposes. The class joins the
/// console collection so it does not spawn child processes alongside the concurrency tests,
/// whose scheduling assertions are sensitive to load.
/// </remarks>
[Collection("Console")]
public class HttpTimeoutOptionTests
{
    /// <summary>
    /// The operator typed this value, so an unusable one stops the run instead of silently
    /// falling back to the 30 second default the flag exists to override.
    /// </summary>
    [Theory]
    [InlineData("abc")]     // not a number
    [InlineData("0")]       // below the accepted range
    [InlineData("-5")]      // negative
    [InlineData("3601")]    // above the accepted range
    [InlineData("12.5")]    // not whole seconds
    public void HttpTimeout_WithAnUnusableValue_FailsInsteadOfIgnoringIt(string value)
    {
        var (exitCode, _, error) = RunCli(["--http-timeout", value, "skill", "list"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("--http-timeout expects a whole number of seconds between 1 and 3600.", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both spellings have to be stripped. Handling only the spaced form would leave
    /// <c>--http-timeout=120</c> in the array, where it parses as a root option whose value
    /// never reaches the factory, so the flag would appear to work and change nothing.
    /// </summary>
    [Theory]
    [InlineData("--http-timeout", "120")]
    [InlineData("--http-timeout=120", null)]
    public void HttpTimeout_IsAcceptedOnASubcommandInBothSpellings(string flag, string? value)
    {
        string[] args = value is null
            ? [flag, "skill", "list"]
            : [flag, value, "skill", "list"];

        var (exitCode, _, error) = RunCli(args);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("--http-timeout", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stripping the flag before parsing hides it from help, so it is registered at the root
    /// purely to be discoverable. Someone hitting a 30 second timeout reads help, not source.
    /// </summary>
    [Fact]
    public void HttpTimeout_AppearsInRootHelp()
    {
        var (exitCode, output, _) = RunCli(["--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("--http-timeout", output, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Error) RunCli(string[] args)
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

        // Out of process, so the offline switch travels as environment rather than as the
        // process-wide static an in-process helper would set.
        psi.Environment["DOTNET_INSPECT_OFFLINE"] = "1";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        // Drain both pipes before waiting; a synchronous read of one blocks until EOF and lets
        // the child deadlock filling the other.
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
        // The product copy in this test project's output, not the one under artifacts/bin, so
        // that checking these cases by breaking a product line requires rebuilding the test
        // project rather than appearing to change nothing.
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
