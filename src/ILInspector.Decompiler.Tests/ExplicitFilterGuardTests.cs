using System.Diagnostics;
using ILInspector.Decompiler.Tests.Gating;
using Xunit.Sdk;

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
            "-filter", "/*/*/QueryTests/*",
            "-class-", "Excluded.Tests",
        ];

        IReadOnlyList<ExplicitFilter> filters = ExplicitFilterGuard.FindIncludedFilters(args);

        Assert.Equal(
            [
                new ExplicitFilter("-class", "Namespace.Tests"),
                new ExplicitFilter("-method", "*Tests.Case"),
                new ExplicitFilter("-filter", "/*/*/QueryTests/*"),
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
        ProcessResult mixed = await RunHostWithResponseFileAsync(
            "-class", validClass,
            "-class", missingClass,
            "-method", validMethod,
            "-method", missingMethod);
        ProcessResult missingQuery = await RunHostAsync(
            "-filter", "/*/*/ThisClassDoesNotExist/*");
        ProcessResult emptyIntersection = await RunHostAsync(
            "-class", validClass,
            "-method",
            "ILInspector.Decompiler.Tests.ExplicitFilterGuardTests.FindIncludedFilters_ExtractsClassAndMethodSelectors");
        ProcessResult disjointId = await RunHostAsync(
            "-class", validClass,
            "-id", TestContext.Current.TestCase!.UniqueID);
        ProcessResult invalidRun = await RunHostAsync(
            "-class", validClass,
            "-run", "definitely-not-a-serialized-test-case");
        ProcessResult explicitOnly = await RunHostAsync(
            "-class", "ILInspector.Decompiler.Tests.ExplicitFilterGuardTests",
            "-explicit", "only");
        ProcessResult malformedQuery = await RunHostAsync(
            "-filter", "/((*)|(Foo))/*/*/*");
        string disposalMarker = Path.Combine(
            Path.GetTempPath(),
            $"filter-guard-disposal-{Guid.NewGuid():N}");
        ProcessResult disposableTheory;
        try
        {
            disposableTheory = await RunHostAsync(
                new Dictionary<string, string?>
                {
                    [FilterGuardDisposableArgument.MarkerEnvironmentVariable] = disposalMarker,
                },
                "-preEnumerateTheories",
                "-method",
                "ILInspector.Decompiler.Tests.ExplicitFilterGuardTests."
                    + "PreflightDisposesDiscoveredTheoryArguments");
        }
        finally
        {
            File.Delete(disposalMarker);
        }

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

        Assert.Equal(2, missingQuery.ExitCode);
        Assert.Contains("-filter \"/*/*/ThisClassDoesNotExist/*\"", missingQuery.Error);
        Assert.DoesNotContain("TEST EXECUTION SUMMARY", missingQuery.Output);

        Assert.Equal(2, emptyIntersection.ExitCode);
        Assert.Contains("combined xUnit selectors matched no runnable tests", emptyIntersection.Error);
        Assert.DoesNotContain("TEST EXECUTION SUMMARY", emptyIntersection.Output);

        Assert.Equal(2, disjointId.ExitCode);
        Assert.Contains("combined xUnit selectors matched no runnable tests", disjointId.Error);
        Assert.DoesNotContain("TEST EXECUTION SUMMARY", disjointId.Output);

        Assert.Equal(2, invalidRun.ExitCode);
        Assert.Contains("-run test-case serializations could not be deserialized", invalidRun.Error);
        Assert.DoesNotContain("combined xUnit selectors", invalidRun.Error);
        Assert.DoesNotContain("TEST EXECUTION SUMMARY", invalidRun.Output);

        Assert.Equal(2, explicitOnly.ExitCode);
        Assert.Contains("combined xUnit selectors matched no runnable tests", explicitOnly.Error);
        Assert.DoesNotContain("TEST EXECUTION SUMMARY", explicitOnly.Output);

        Assert.Equal(4, malformedQuery.ExitCode);
        string malformedQueryDiagnostic = malformedQuery.Output + malformedQuery.Error;
        Assert.Contains("Unexpected null filter", malformedQueryDiagnostic);
        Assert.DoesNotContain("Unhandled exception", malformedQueryDiagnostic);

        Assert.Equal(0, disposableTheory.ExitCode);
        Assert.Contains("Total: 1,", disposableTheory.Output);
        Assert.DoesNotContain("preflight discovery did not dispose", disposableTheory.Error);
    }

    [Theory]
    [MemberData(nameof(DisposableArguments), DisableDiscoveryEnumeration = false)]
    public void PreflightDisposesDiscoveredTheoryArguments(
        FilterGuardDisposableArgument argument)
    {
        if (argument.MarkerPath is not null)
        {
            Assert.True(
                File.Exists(argument.MarkerPath),
                "preflight discovery did not dispose its test case before execution");
        }
    }

    public static TheoryData<FilterGuardDisposableArgument> DisposableArguments =>
        new()
        {
            new FilterGuardDisposableArgument(
                Environment.GetEnvironmentVariable(
                    FilterGuardDisposableArgument.MarkerEnvironmentVariable)),
        };

    private static Task<ProcessResult> RunHostAsync(params string[] arguments) =>
        RunHostAsync(null, arguments);

    private static async Task<ProcessResult> RunHostAsync(
        IReadOnlyDictionary<string, string?>? environment,
        params string[] arguments)
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

        if (environment is not null)
        {
            foreach ((string name, string? value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the decompiler test host.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static async Task<ProcessResult> RunHostWithResponseFileAsync(params string[] arguments)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(path, arguments, TestContext.Current.CancellationToken);
            return await RunHostAsync("@@", path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}

public sealed class FilterGuardDisposableArgument : IXunitSerializable, IDisposable
{
    internal const string MarkerEnvironmentVariable =
        "DOTNET_INSPECT_FILTER_GUARD_DISPOSAL_MARKER";

    [Obsolete("Called by the xUnit deserializer.")]
    public FilterGuardDisposableArgument()
    {
    }

    public FilterGuardDisposableArgument(string? markerPath)
    {
        MarkerPath = markerPath;
    }

    public string? MarkerPath { get; private set; }

    public void Deserialize(IXunitSerializationInfo info)
    {
        MarkerPath = info.GetValue<string>(nameof(MarkerPath));
    }

    public void Dispose()
    {
        if (MarkerPath is not null)
        {
            File.WriteAllText(MarkerPath, "disposed");
        }
    }

    public void Serialize(IXunitSerializationInfo info)
    {
        if (MarkerPath is not null)
        {
            info.AddValue(nameof(MarkerPath), MarkerPath);
        }
    }
}
