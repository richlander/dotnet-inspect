using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ILInspector.Decompiler.Tests.Gating;
using Xunit.Sdk;

[assembly: RegisterXunitSerializer(
    typeof(ILInspector.Decompiler.Tests.FilterGuardCustomSerializer),
    typeof(ILInspector.Decompiler.Tests.FilterGuardCustomValue))]

namespace ILInspector.Decompiler.Tests;

[CollectionDefinition("ExplicitFilterGuardProcess", DisableParallelization = true)]
public sealed class ExplicitFilterGuardProcessCollection;

[Collection("ExplicitFilterGuardProcess")]
public class ExplicitFilterGuardTests
{
    private const string AppHostAliasMethod =
        "ILInspector.Decompiler.Tests.ExplicitFilterGuardTests."
        + "AppHostAlias_IsTheExecutingTestProcess";

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
        ProcessResult appHostWithoutDotnetPath = await RunAppHostAsync(
            "-method",
            AppHostAliasMethod);
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
        const string customSerializationMethod =
            "ILInspector.Decompiler.Tests.ExplicitFilterGuardTests."
            + "ProcessIsolatedPreflightPreservesCustomSerialization";
        ProcessResult customSerializationList = await RunHostAsync(
            "-preEnumerateTheories",
            "-method",
            customSerializationMethod,
            "-noColor",
            "-list",
            "discovery/json");
        string customSerialization = JsonDocument
            .Parse(customSerializationList.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0])
            .RootElement
            .GetProperty("Serialization")
            .GetString()
            ?? throw new InvalidOperationException("Discovery serialization was null.");
        ProcessResult customSerializationRun = await RunHostAsync(
            "-preEnumerateTheories",
            "-method",
            customSerializationMethod,
            "-run",
            customSerialization);
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

        AssertSuccessfulAppHostRun(appHostWithoutDotnetPath);

        Assert.Equal(2, mixed.ExitCode);
        Assert.Contains("explicit xUnit filter matched no discovered tests", mixed.Error);
        Assert.Contains(missingClass, mixed.Error);
        Assert.Contains(missingMethod, mixed.Error);
        Assert.DoesNotContain($"  -class \"{validClass}\"", mixed.Error);
        Assert.DoesNotContain($"  -method \"{validMethod}\"", mixed.Error);
        Assert.DoesNotContain("TEST EXECUTION SUMMARY", mixed.Output);

        Assert.True(
            missingQuery.ExitCode == 2,
            "Expected a missing query to be rejected, "
            + $"got {missingQuery.ExitCode}.\n"
            + $"{missingQuery.Output}\n{missingQuery.Error}");
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

        Assert.Equal(0, customSerializationList.ExitCode);
        Assert.Equal(0, customSerializationRun.ExitCode);
        Assert.Contains("Total: 1,", customSerializationRun.Output);
        Assert.DoesNotContain("already supported", customSerializationRun.Output);
        Assert.DoesNotContain("already supported", customSerializationRun.Error);
        Assert.DoesNotContain(
            "custom serializer was constructed more than once",
            customSerializationRun.Output + customSerializationRun.Error,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "-run test-case serializations could not be deserialized",
            customSerializationRun.Error);

        Assert.Equal(0, disposableTheory.ExitCode);
        Assert.Contains("Total: 1,", disposableTheory.Output);
        Assert.DoesNotContain("preflight discovery did not dispose", disposableTheory.Error);
    }

    [Theory]
    [MemberData(
        nameof(CustomSerializedArguments),
        DisableDiscoveryEnumeration = false)]
    public void ProcessIsolatedPreflightPreservesCustomSerialization(
        (FilterGuardCustomValue Value, int Number) argument)
    {
        Assert.Equal("custom", argument.Value.Text);
        Assert.Equal(42, argument.Number);
    }

    public static TheoryData<(FilterGuardCustomValue, int)> CustomSerializedArguments =>
        new()
        {
            (new FilterGuardCustomValue("custom"), 42),
        };

    [Fact]
    public void AppHostAlias_IsTheExecutingTestProcess()
    {
        string? expectedPath =
            Environment.GetEnvironmentVariable(
                "DOTNET_INSPECT_EXPECTED_FILTER_GUARD_APPHOST");
        if (expectedPath is not null)
        {
            Assert.Equal(
                Path.GetFullPath(expectedPath),
                Path.GetFullPath(Environment.ProcessPath!));
        }
    }

    [Fact]
    public async Task AppHostAlias_ConcurrentInvocationsAreSerialized()
    {
        ProcessResult[] results = await Task.WhenAll(
            RunAppHostAsync("-method", AppHostAliasMethod),
            RunAppHostAsync("-method", AppHostAliasMethod));

        foreach (ProcessResult result in results)
        {
            AssertSuccessfulAppHostRun(result);
        }
    }

    [Theory]
    [MemberData(nameof(DisposableArguments), DisableDiscoveryEnumeration = false)]
    public void PreflightDisposesDiscoveredTheoryArguments(
        FilterGuardDisposableArgument argument)
    {
        if (argument.MarkerPath is not null)
        {
            Assert.False(
                argument.IsDisposed,
                "preflight disposed a cached theory argument in the runner process");
            Assert.True(
                File.Exists(argument.MarkerPath),
                "preflight discovery did not dispose its test case before execution");
        }
    }

    public static TheoryData<FilterGuardDisposableArgument> DisposableArguments { get; } =
        new()
        {
            new FilterGuardDisposableArgument(
                Environment.GetEnvironmentVariable(
                    FilterGuardDisposableArgument.MarkerEnvironmentVariable)),
        };

    private static Task<ProcessResult> RunHostAsync(params string[] arguments) =>
        RunHostAsync(null, arguments);

    private static void AssertSuccessfulAppHostRun(
        ProcessResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            "Expected the apphost to run a valid filter without dotnet on PATH, "
            + $"got {result.ExitCode}.\n"
            + $"{result.Output}\n{result.Error}");
        Assert.Contains("Total: 1,", result.Output);
    }

    private static async Task<ProcessResult> RunAppHostAsync(
        params string[] arguments)
    {
        string assemblyPath = typeof(Program).Assembly.Location;
        string appHostPath = Path.Combine(
            Path.GetDirectoryName(assemblyPath)
                ?? throw new InvalidOperationException("Test assembly has no directory."),
            Path.GetFileNameWithoutExtension(assemblyPath)
                + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        Assert.True(File.Exists(appHostPath), $"Test apphost not found: {appHostPath}");

        string emptyPath = Path.Combine(
            Path.GetTempPath(),
            $"filter-guard-host-alias-{Guid.NewGuid():N}");
        string aliasPath = Path.Combine(
            Path.GetDirectoryName(appHostPath)!,
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        string stagedAliasPath =
            $"{aliasPath}.{Guid.NewGuid():N}.tmp";
        string lockPath = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-filter-guard-"
                + Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            Path.GetFullPath(appHostPath))))
                + ".lock");
        // Removing this rendezvous file could split existing waiters from new openers.
        await using FileStream aliasLock =
            await AcquireExclusiveLockAsync(lockPath);
        bool aliasCreated = false;
        bool stagedAliasCreated = false;

        try
        {
            Directory.CreateDirectory(emptyPath);
            if (File.Exists(aliasPath))
            {
                Assert.True(
                    FilesHaveSameContent(appHostPath, aliasPath),
                    $"A non-test apphost alias already exists: {aliasPath}");
                File.Delete(aliasPath);
            }

            File.Copy(appHostPath, stagedAliasPath);
            stagedAliasCreated = true;
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    stagedAliasPath,
                    File.GetUnixFileMode(appHostPath));
            }

            File.Move(stagedAliasPath, aliasPath);
            stagedAliasCreated = false;
            aliasCreated = true;

            var environment = new Dictionary<string, string?>
            {
                ["PATH"] = emptyPath,
                ["DOTNET_HOST_PATH"] = aliasPath,
                ["DOTNET_INSPECT_EXPECTED_FILTER_GUARD_APPHOST"] = aliasPath,
            };
            string? dotnetHostPath =
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrEmpty(dotnetHostPath))
            {
                environment["DOTNET_ROOT"] = Path.GetDirectoryName(dotnetHostPath);
            }

            return await RunProcessAsync(aliasPath, null, environment, arguments);
        }
        finally
        {
            if (aliasCreated && File.Exists(aliasPath))
            {
                File.Delete(aliasPath);
            }

            if (stagedAliasCreated && File.Exists(stagedAliasPath))
            {
                File.Delete(stagedAliasPath);
            }

            if (Directory.Exists(emptyPath))
            {
                Directory.Delete(emptyPath);
            }
        }
    }

    private static async Task<FileStream> AcquireExclusiveLockAsync(
        string path)
    {
        while (true)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(
                    50,
                    TestContext.Current.CancellationToken);
            }
        }
    }

    private static bool FilesHaveSameContent(
        string left,
        string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        return leftInfo.Length == rightInfo.Length
            && File.ReadAllBytes(left).AsSpan()
                .SequenceEqual(File.ReadAllBytes(right));
    }

    private static async Task<ProcessResult> RunHostAsync(
        IReadOnlyDictionary<string, string?>? environment,
        params string[] arguments) =>
        await RunProcessAsync(
            "dotnet",
            typeof(Program).Assembly.Location,
            environment,
            arguments);

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string? firstArgument,
        IReadOnlyDictionary<string, string?>? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (firstArgument is not null)
        {
            startInfo.ArgumentList.Add(firstArgument);
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach ((string name, string? value) in environment)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(name);
                }
                else
                {
                    startInfo.Environment[name] = value;
                }
            }
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the decompiler test host.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            throw;
        }

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

    public bool IsDisposed { get; private set; }

    public void Deserialize(IXunitSerializationInfo info)
    {
        MarkerPath = info.GetValue<string>(nameof(MarkerPath));
    }

    public void Dispose()
    {
        IsDisposed = true;
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

public sealed record FilterGuardCustomValue(string Text);

public sealed class FilterGuardCustomSerializer : IXunitSerializer
{
    private static int s_constructorCount;

    public FilterGuardCustomSerializer()
    {
        if (Interlocked.Increment(ref s_constructorCount) != 1)
        {
            throw new InvalidOperationException(
                "The custom serializer was constructed more than once in one process.");
        }
    }

    public object Deserialize(Type type, string serializedValue) =>
        new FilterGuardCustomValue(serializedValue);

    public bool IsSerializable(
        Type type,
        object? value,
        [NotNullWhen(false)] out string? failureReason)
    {
        failureReason = null;
        return value is FilterGuardCustomValue;
    }

    public string Serialize(object value) =>
        ((FilterGuardCustomValue)value).Text;
}
