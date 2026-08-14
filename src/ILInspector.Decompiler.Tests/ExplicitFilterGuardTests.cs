using System.Diagnostics;
using ILInspector.Decompiler.Tests.Gating;

namespace ILInspector.Decompiler.Tests;

public class ExplicitFilterGuardTests
{
    [Fact]
    public void FindIncludedFilters_ExtractsClassAndMethodSelectors()
    {
        string[] args =
        [
            "-trait", "Speed=Slow",
            "-CLASS", "Namespace.Tests",
            "-method", "*Tests.Case",
            "-class-", "Excluded.Tests",
        ];

        IReadOnlyList<ExplicitFilter> filters = ExplicitFilterGuard.FindIncludedFilters(args);

        Assert.Equal(
            [
                new ExplicitFilter("-class", "Namespace.Tests"),
                new ExplicitFilter("-method", "*Tests.Case"),
            ],
            filters);
    }

    /// <summary>
    /// Non-vacuity for the host wiring: a filter that names no test must fail before
    /// the xUnit runner can report a successful zero-test execution (#3546).
    /// </summary>
    [Fact]
    public async Task TestHost_RejectsEveryUnmatchedExplicitFilter()
    {
        const string validClass = "ILInspector.Decompiler.Tests.GateArgumentExpanderTests";
        const string missingClass = "ILInspector.Decompiler.Tests.ThisClassDoesNotExist";
        const string validMethod =
            "ILInspector.Decompiler.Tests.GateArgumentExpanderTests.NoGateFlag_PassesArgumentsThroughUnchanged";
        const string missingMethod =
            "ILInspector.Decompiler.Tests.GateArgumentExpanderTests.ThisMethodDoesNotExist";

        ProcessResult valid = await RunHostAsync("-method", validMethod);
        ProcessResult mixed = await RunHostAsync(
            "-class", validClass,
            "-class", missingClass,
            "-method", validMethod,
            "-method", missingMethod);
        ProcessResult emptyIntersection = await RunHostAsync(
            "-class", validClass,
            "-method",
            "ILInspector.Decompiler.Tests.ExplicitFilterGuardTests.FindIncludedFilters_ExtractsClassAndMethodSelectors");

        Assert.True(
            valid.ExitCode == 0,
            $"Expected a valid filter to pass, got {valid.ExitCode}.\n{valid.Output}\n{valid.Error}");
        Assert.Contains("Total: 1,", valid.Output);

        Assert.Equal(2, mixed.ExitCode);
        Assert.Contains("explicit xUnit filter matched no discovered tests", mixed.Error);
        Assert.Contains(missingClass, mixed.Error);
        Assert.Contains(missingMethod, mixed.Error);
        Assert.DoesNotContain($"  -class \"{validClass}\"", mixed.Error);
        Assert.DoesNotContain($"  -method \"{validMethod}\"", mixed.Error);
        Assert.DoesNotContain("TEST EXECUTION SUMMARY", mixed.Output);

        Assert.Equal(2, emptyIntersection.ExitCode);
        Assert.Contains("combined xUnit filters matched no discovered tests", emptyIntersection.Error);
        Assert.DoesNotContain("TEST EXECUTION SUMMARY", emptyIntersection.Output);
    }

    private static async Task<ProcessResult> RunHostAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the decompiler test host.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
