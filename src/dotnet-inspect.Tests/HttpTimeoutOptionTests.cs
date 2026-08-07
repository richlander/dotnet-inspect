using System.Diagnostics;
using System.Reflection;

namespace DotnetInspector.Tests;

/// <summary>
/// Covers the CLI-owned <c>--http-timeout</c> parsing and startup configuration.
/// </summary>
/// <remarks>
/// The option-shape cases run the built executable because they cover the complete startup
/// wiring rather than only the parsing helper. The class joins the console collection so it
/// does not spawn child processes alongside concurrency tests whose scheduling assertions are
/// sensitive to load.
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
    /// Every spelling accepted by System.CommandLine has to be stripped. Leaving an inline form
    /// in the array lets it parse as a root option whose value never reaches the factory, so the
    /// flag would appear to work and change nothing.
    /// </summary>
    [Theory]
    [InlineData("--http-timeout", "120")]
    [InlineData("--http-timeout=120", null)]
    [InlineData("--http-timeout:120", null)]
    public void HttpTimeout_IsAcceptedOnASubcommandInEverySpelling(string flag, string? value)
    {
        string[] args = value is null
            ? [flag, "skill", "list"]
            : [flag, value, "skill", "list"];

        var (exitCode, _, error) = RunCli(args);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("--http-timeout", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--http-timeout=0")]
    [InlineData("--http-timeout:0")]
    public void HttpTimeout_InlineUnusableValue_FailsInsteadOfBeingDiscarded(string flag)
    {
        var (exitCode, _, error) = RunCli([flag, "skill", "list"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("--http-timeout expects a whole number of seconds between 1 and 3600.", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--http-timeout", "2", "--http-timeout", "5")]
    [InlineData("--http-timeout=2", null, "--http-timeout:5", null)]
    public void HttpTimeout_DuplicateFlagsAreRejected(
        string firstFlag,
        string? firstValue,
        string secondFlag,
        string? secondValue)
    {
        var args = new List<string> { firstFlag };
        if (firstValue is not null)
            args.Add(firstValue);
        args.Add(secondFlag);
        if (secondValue is not null)
            args.Add(secondValue);
        args.Add("skill");
        args.Add("list");

        var (exitCode, _, error) = RunCli([.. args]);

        Assert.Equal(1, exitCode);
        Assert.Contains("--http-timeout may only be specified once.", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stripping the flag before parsing means scanning the whole array, so the scan has to
    /// stop at a bare <c>--</c> or the end-of-options separator would not mean what it says.
    /// Without the stop, <c>-- --http-timeout</c> was consumed as the flag with an empty value
    /// and the run died on a parse error the operator never asked for.
    /// </summary>
    [Theory]
    [InlineData("--http-timeout 120")]
    [InlineData("--http-timeout")]
    [InlineData("--http-timeout=120")]
    [InlineData("--http-timeout:120")]
    public void HttpTimeout_AfterAnEndOfOptionsSeparator_IsLeftForTheCommand(string trailing)
    {
        string[] trailingTokens = trailing.Split(' ');
        var (exitCode, _, error) = RunCli([.. new[] { "skill", "list", "--" }, .. trailingTokens]);

        // The flag is not a valid argument to `skill list`, so the parser rejecting it is the
        // proof that the token survived the strip. What must not appear is this class's own
        // parse error, which would mean the separator was ignored.
        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("expects a whole number of seconds", error, StringComparison.Ordinal);
        foreach (string token in trailingTokens)
        {
            Assert.Contains($"Unrecognized command or argument '{token}'", error, StringComparison.Ordinal);
        }
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

    /// <summary>
    /// The flag and environment variable share the CLI-owned parser so their accepted ranges
    /// cannot drift apart.
    /// </summary>
    [Theory]
    [InlineData("1", true, 1)]
    [InlineData("120", true, 120)]
    [InlineData("3600", true, 3600)]
    [InlineData(null, false, 0)]
    [InlineData("", false, 0)]
    [InlineData("0", false, 0)]
    [InlineData("-5", false, 0)]
    [InlineData("3601", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("12.5", false, 0)]
    [InlineData("99999999", false, 0)]
    [InlineData(" 120 ", true, 120)]
    public void HttpTimeout_ParserAcceptsWholeSecondsInRange(
        string? value,
        bool expected,
        int expectedSeconds)
    {
        bool accepted = HttpTimeoutConfiguration.TryParseSeconds(value, out TimeSpan timeout);

        Assert.Equal(expected, accepted);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), timeout);
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData("", 30)]
    [InlineData("120", 120)]
    [InlineData("0", 30)]
    [InlineData("3601", 30)]
    [InlineData("abc", 30)]
    public void HttpTimeout_EnvironmentValueFallsBackToTheBuiltInDefault(
        string? value,
        int expectedSeconds)
    {
        TimeSpan timeout = HttpTimeoutConfiguration.ResolveEnvironmentDefault(value);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), timeout);
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
