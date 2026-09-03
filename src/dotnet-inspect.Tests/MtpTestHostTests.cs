using System.Diagnostics;

namespace DotnetInspector.Tests;

public class MtpTestHostTests
{
    private const string FixtureMethod =
        "DotnetInspector.Tests.MtpTestHostTests.SelectionFixture_Passes";

    [Fact]
    public async Task UnmatchedFilter_ExitsWithZeroTestsCode()
    {
        ProcessResult result = await RunHostAsync(
            "--filter-method",
            "DotnetInspector.Tests.MtpTestHostTests.DefinitelyNoSuchTest5099");

        AssertExitCode(8, result);
    }

    [Fact]
    public async Task ValidFilter_RunsSelectedTest()
    {
        ProcessResult result = await RunHostAsync(
            "--filter-method",
            FixtureMethod);

        AssertExitCode(0, result);
    }

    [Fact]
    public async Task MixedValidAndStaleValues_UseAggregateMinimum()
    {
        ProcessResult result = await RunHostAsync(
            "--filter-class",
            typeof(MtpTestHostTests).FullName!,
            "DotnetInspector.Tests.DefinitelyNoSuchClass5099",
            "--filter-method",
            FixtureMethod);

        AssertExitCode(0, result);
    }

    [Fact]
    public void SelectionFixture_Passes()
    {
    }

    private static async Task<ProcessResult> RunHostAsync(params string[] arguments)
    {
        string executable = Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows()
                ? "dotnet-inspect.Tests.exe"
                : "dotnet-inspect.Tests");
        Assert.True(
            File.Exists(executable),
            $"Expected the test apphost at '{executable}'.");

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start test apphost '{executable}'.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return new(
            process.ExitCode,
            await stdout,
            await stderr);
    }

    private static void AssertExitCode(int expected, ProcessResult result)
    {
        Assert.True(
            result.ExitCode == expected,
            $"Expected exit code {expected}, got {result.ExitCode}."
                + Environment.NewLine
                + "Standard output:"
                + Environment.NewLine
                + result.StandardOutput
                + Environment.NewLine
                + "Standard error:"
                + Environment.NewLine
                + result.StandardError);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
