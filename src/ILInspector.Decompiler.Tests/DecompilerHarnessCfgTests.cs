using System.Diagnostics;
using System.Reflection;

namespace ILInspector.Decompiler.Tests;

public class DecompilerHarnessCfgTests
{
    const string FixtureMethod = "ILInspector.Decompiler.Tests.DecompilerHarnessCfgFixture::TryFinally";

    [Fact]
    public async Task Cfg_DefaultsToRaisedGraph()
    {
        var result = await RunHarness(
            typeof(DecompilerHarnessCfgFixture).Assembly.Location,
            "--dump", FixtureMethod, "--cfg", "--skip-pdb");

        AssertSucceeded(result);
        Assert.Contains("pipeline: next, control-flow graph", result.Stdout);
        Assert.DoesNotContain("IL BlockGraph", result.Stdout);
        Assert.True(string.IsNullOrWhiteSpace(result.Stderr), result.Diagnostics);
    }

    [Fact]
    public async Task CfgIl_PrintsRungOneEhGraph()
    {
        var result = await RunHarness(
            typeof(DecompilerHarnessCfgFixture).Assembly.Location,
            "--dump", FixtureMethod, "--cfg", "--il", "--skip-pdb");

        AssertSucceeded(result);
        Assert.Contains("pipeline: IL rung 1, control-flow graph", result.Stdout);
        Assert.Contains("IL BlockGraph", result.Stdout);
        Assert.Contains("(leave region)", result.Stdout);
        Assert.True(string.IsNullOrWhiteSpace(result.Stderr), result.Diagnostics);
    }

    [Fact]
    public async Task CfgIlMermaid_PrintsRungOneEhGraph()
    {
        var result = await RunHarness(
            typeof(DecompilerHarnessCfgFixture).Assembly.Location,
            "--dump", FixtureMethod, "--cfg", "--il", "--mermaid", "--skip-pdb");

        AssertSucceeded(result);
        Assert.Contains("pipeline: IL rung 1, mermaid flowchart", result.Stdout);
        Assert.Contains("```mermaid", result.Stdout);
        Assert.Contains("_leave([\"leave region\"])", result.Stdout);
        Assert.True(string.IsNullOrWhiteSpace(result.Stderr), result.Diagnostics);
    }

    [Fact]
    public async Task CfgIl_OverloadMenuKeepsStandardOutputPipeClean()
    {
        var result = await RunHarness(
            "--dump", "System.String::IndexOf", "--index", "0",
            "--cfg", "--il", "--skip-pdb");

        AssertSucceeded(result);
        Assert.Contains("pipeline: IL rung 1", result.Stdout);
        Assert.DoesNotContain("overload", result.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("System.String::IndexOf", result.Stderr);
        Assert.Contains("overload", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CfgIl_BodylessMethodFailsVisibly()
    {
        var result = await RunHarness(
            "--dump", "System.IDisposable::Dispose", "--cfg", "--il", "--skip-pdb");

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.Stdout), result.Diagnostics);
        Assert.Contains("has no IL body", result.Stderr);
    }

    static void AssertSucceeded(HarnessResult result)
        => Assert.True(result.ExitCode == 0, result.Diagnostics);

    static async Task<HarnessResult> RunHarness(params string[] arguments)
    {
        string harnessPath = HarnessPath();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(harnessPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(harnessPath);
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true";
        startInfo.Environment["DOTNET_NOLOGO"] = "true";

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
            await process.WaitForExitAsync();
            Assert.Fail(
                "DecompilerHarness did not complete within 30 seconds."
                + $"\n--- stdout ---\n{await stdoutTask}"
                + $"\n--- stderr ---\n{await stderrTask}");
        }

        return new HarnessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    static string HarnessPath()
    {
        string? path = Environment.GetEnvironmentVariable("DECOMPILER_HARNESS_TEST_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = typeof(DecompilerHarnessCfgTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .SingleOrDefault(attribute => attribute.Key == "DecompilerHarnessPath")
                ?.Value;
        }

        Assert.False(string.IsNullOrWhiteSpace(path), "DecompilerHarnessPath assembly metadata is missing.");
        path = Path.GetFullPath(path!);
        Assert.True(File.Exists(path), $"DecompilerHarness was not found at '{path}'.");
        return path;
    }

    sealed record HarnessResult(int ExitCode, string Stdout, string Stderr)
    {
        public string Diagnostics
            => $"Exit code: {ExitCode}\n--- stdout ---\n{Stdout}\n--- stderr ---\n{Stderr}";
    }
}

internal static class DecompilerHarnessCfgFixture
{
    internal static int TryFinally(bool value)
    {
        try
        {
            return value ? 1 : 2;
        }
        finally
        {
            System.GC.KeepAlive(value);
        }
    }
}
