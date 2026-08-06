using System.Diagnostics;
using System.Reflection;

namespace DotnetInspector.Tests;

/// <summary>
/// <c>--info</c> is a root-level switch, but only some subcommands declare
/// <c>--tips</c>. Suppressing tips by appending <c>-T:q</c> to the argument array
/// therefore handed an unrecognized token to every subcommand that does not
/// declare the option, and the parser rejected the whole invocation before the
/// command ran. Twelve subcommands were affected: <c>package search</c>,
/// <c>cache clear</c>, and all ten <c>skill</c> subcommands.
/// <para>
/// These cases run the real executable because the injection lived in top-level
/// code in <c>Program.cs</c> rather than in <c>CommandLineBuilder.PreprocessArgs</c>,
/// so the in-process harness never executed it. That is why the defect shipped
/// with a green suite, and it is why the guard has to pay for a process.
/// </para>
/// <para>
/// The <c>cache clear</c> case passes <c>--session</c> deliberately. Plain
/// <c>cache clear</c> also deletes the pre-XDG cache root, which
/// <c>CoreCache.GetLegacyBasePath()</c> resolves from the user profile rather than
/// from <c>DOTNET_INSPECT_CACHE_DIR</c>, so on Linux and macOS it would delete a
/// real directory belonging to whoever ran the suite. The <c>--session</c> form
/// reaches the same parser scope, reports a missing session as success, and
/// confines any deletion to <c>%TEMP%/dotnet-inspect-info-scope-probe</c>, a path
/// nothing but this test names.
/// </para>
/// </summary>
public sealed class InfoOptionScopeTests : IDisposable
{
    private readonly string _cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "dotnet-inspect-info-scope-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("skill", "list")]
    [InlineData("cache", "clear", "--session", "info-scope-probe")]
    [InlineData("package", "search", "Newtonsoft.Json")]
    public void Info_OnSubcommandThatDoesNotDeclareTips_IsNotRejectedByTheParser(params string[] command)
    {
        var (exitCode, _, error) = RunCli([.. command, "--info"]);

        // The token itself is the assertion. The parser echoes the argument it
        // could not place, so its absence from stderr is the direct evidence that
        // nothing was injected, and it does not depend on the exact wording
        // System.CommandLine uses for an unrecognized argument.
        // The mutation this catches: restore the "-T:q" append in Program.cs and
        // all three cases redden.
        Assert.DoesNotContain("-T:q", error, StringComparison.Ordinal);

        // Absence alone would go vacuous if the command ever started failing before
        // the parser for an unrelated reason, so each case also has to show that
        // --info actually took effect. The info block, which shares stderr with the
        // tips it replaces, is the proof that the command ran. `package search`
        // needs it most: it legitimately exits non-zero under the offline switch,
        // so it has no exit code to assert on.
        Assert.Contains("# Info", error, StringComparison.Ordinal);

        if (command[0] != "package")
        {
            Assert.Equal(0, exitCode);
        }
    }

    private (int ExitCode, string Output, string Error) RunCli(string[] args)
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
