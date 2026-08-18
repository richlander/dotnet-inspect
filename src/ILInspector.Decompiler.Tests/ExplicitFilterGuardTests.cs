using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    private const string AppHostAliasDirectoryPrefix =
        ".filter-guard-host-alias-";
    private const string AppHostAliasMethod =
        "ILInspector.Decompiler.Tests.ExplicitFilterGuardTests."
        + "AppHostAlias_IsTheExecutingTestProcess";
    private const string AppHostConcurrencyMethod =
        "ILInspector.Decompiler.Tests.ExplicitFilterGuardTests."
        + "AppHostAlias_ConcurrentProcessesAreIsolated";
    private const string AppHostWorkerEnvironmentVariable =
        "DOTNET_INSPECT_FILTER_GUARD_APPHOST_WORKER";
    private const string AppHostAliasDirectoryEnvironmentVariable =
        "DOTNET_INSPECT_FILTER_GUARD_APPHOST_ALIAS_DIRECTORY";
    private const string AppHostEmptyPathEnvironmentVariable =
        "DOTNET_INSPECT_FILTER_GUARD_APPHOST_EMPTY_PATH";
    private const string ExpectedProcessPathEnvironmentVariable =
        "DOTNET_INSPECT_EXPECTED_FILTER_GUARD_PROCESS";
    private const string AppHostReadyEnvironmentVariable =
        "DOTNET_INSPECT_FILTER_GUARD_APPHOST_READY";
    private const string AppHostReleaseEnvironmentVariable =
        "DOTNET_INSPECT_FILTER_GUARD_APPHOST_RELEASE";

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
        const string simulatedPreflightFailurePrefix =
            "simulated filter preflight child failure";
        const string simulatedPreflightFailureTail =
            "simulated diagnostic tail";
        string simulatedPreflightFailure =
            simulatedPreflightFailurePrefix
            + new string('x', 4096)
            + simulatedPreflightFailureTail;
        ProcessResult preflightFailure = await RunHostAsync(
            new Dictionary<string, string?>
            {
                [ExplicitFilterGuard.SimulatedFailureEnvironmentVariable] =
                    simulatedPreflightFailure,
            },
            "-method",
            validMethod);
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

        Assert.Equal(2, preflightFailure.ExitCode);
        Assert.Contains(
            "explicit xUnit filter preflight could not complete",
            preflightFailure.Error);
        Assert.Contains("Child exit code: 86", preflightFailure.Error);
        Assert.Contains(
            simulatedPreflightFailurePrefix,
            preflightFailure.Error);
        Assert.DoesNotContain(
            simulatedPreflightFailureTail,
            preflightFailure.Error);
        Assert.DoesNotContain(
            "TEST EXECUTION SUMMARY",
            preflightFailure.Output);

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
    public async Task AppHostAlias_IsTheExecutingTestProcess()
    {
        string? expectedPath =
            Environment.GetEnvironmentVariable(
                ExpectedProcessPathEnvironmentVariable);
        if (expectedPath is not null)
        {
            Assert.Equal(
                Path.GetFullPath(expectedPath),
                Path.GetFullPath(Environment.ProcessPath!));
        }

        string? releasePath =
            Environment.GetEnvironmentVariable(
                AppHostReleaseEnvironmentVariable);
        if (releasePath is not null)
        {
            await WaitForMarkerAsync(
                releasePath,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task AppHostAlias_ConcurrentProcessesAreIsolated()
    {
        if (Environment.GetEnvironmentVariable(
                AppHostWorkerEnvironmentVariable) is not null)
        {
            ProcessResult workerResult =
                await RunAppHostAsync("-method", AppHostAliasMethod);
            AssertSuccessfulRun(workerResult, "independent apphost worker");
            return;
        }

        string markerId = Guid.NewGuid().ToString("N");
        await RunConcurrentAppHostIsolationAsync(
            markerId,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AppHostAlias_CancellationCleansParentOwnedDirectories()
    {
        string markerId = Guid.NewGuid().ToString("N");
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunConcurrentAppHostIsolationAsync(
                markerId,
                cancellation.Token,
                cancellation.Cancel));

        foreach (string path in GetAppHostWorkerDirectories(markerId))
        {
            Assert.False(
                Directory.Exists(path),
                $"Canceled apphost worker left a directory behind: {path}");
        }
    }

    private static async Task RunConcurrentAppHostIsolationAsync(
        string markerId,
        CancellationToken cancellationToken,
        Action? workersReady = null)
    {
        string readyPath1 = Path.Combine(
            Path.GetTempPath(),
            $"filter-guard-apphost-ready-{markerId}-1");
        string readyPath2 = Path.Combine(
            Path.GetTempPath(),
            $"filter-guard-apphost-ready-{markerId}-2");
        string releasePath = Path.Combine(
            Path.GetTempPath(),
            $"filter-guard-apphost-release-{markerId}");
        string[] workerDirectories =
            GetAppHostWorkerDirectories(markerId);
        Task<ProcessResult>[] workers =
        [
            RunHostAsync(
                CreateAppHostWorkerEnvironment(
                    readyPath1,
                    releasePath,
                    workerDirectories[0],
                    workerDirectories[1]),
                cancellationToken,
                "-method",
                AppHostConcurrencyMethod),
            RunHostAsync(
                CreateAppHostWorkerEnvironment(
                    readyPath2,
                    releasePath,
                    workerDirectories[2],
                    workerDirectories[3]),
                cancellationToken,
                "-method",
                AppHostConcurrencyMethod),
        ];
        ProcessResult[] workerResults;
        try
        {
            await Task.WhenAll(
                WaitForMarkerAsync(
                    readyPath1,
                    workers[0],
                    cancellationToken),
                WaitForMarkerAsync(
                    readyPath2,
                    workers[1],
                    cancellationToken));

            workersReady?.Invoke();

            string appHostDirectory = GetAppHostDirectory();
            string dotnetHostPath = GetDotnetHostPath();
            string currentPath =
                Environment.GetEnvironmentVariable("PATH")
                ?? string.Empty;
            var consumerEnvironment = new Dictionary<string, string?>
            {
                [ExpectedProcessPathEnvironmentVariable] = dotnetHostPath,
                ["PATH"] =
                    appHostDirectory
                    + Path.PathSeparator
                    + currentPath,
            };
            ProcessResult consumer = await RunHostAsync(
                consumerEnvironment,
                cancellationToken,
                "-method",
                AppHostAliasMethod);
            AssertSuccessfulRun(
                consumer,
                "muxer consumer while apphost aliases were live");
        }
        finally
        {
            File.WriteAllText(releasePath, "release");
            try
            {
                workerResults = await Task.WhenAll(workers);
            }
            finally
            {
                File.Delete(readyPath1);
                File.Delete(readyPath2);
                File.Delete(releasePath);
                foreach (string path in workerDirectories)
                {
                    DeleteDirectoryIfExists(path);
                }
            }
        }

        foreach (ProcessResult result in workerResults)
        {
            AssertSuccessfulRun(result, "independent apphost worker");
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
        => AssertSuccessfulRun(
            result,
            "apphost running a valid filter without dotnet on PATH");

    private static void AssertSuccessfulRun(
        ProcessResult result,
        string description)
    {
        Assert.True(
            result.ExitCode == 0,
            $"Expected {description} to succeed, got {result.ExitCode}.\n"
            + $"{result.Output}\n{result.Error}");
        Assert.Contains("Total: 1,", result.Output);
    }

    private static Dictionary<string, string?> CreateAppHostWorkerEnvironment(
        string readyPath,
        string releasePath,
        string aliasDirectory,
        string emptyPath) =>
        new()
        {
            [AppHostWorkerEnvironmentVariable] = "1",
            [AppHostAliasDirectoryEnvironmentVariable] = aliasDirectory,
            [AppHostEmptyPathEnvironmentVariable] = emptyPath,
            [AppHostReadyEnvironmentVariable] = readyPath,
            [AppHostReleaseEnvironmentVariable] = releasePath,
        };

    private static async Task<ProcessResult> RunAppHostAsync(
        params string[] arguments)
    {
        string assemblyPath = typeof(Program).Assembly.Location;
        string appHostDirectory = GetAppHostDirectory();
        string appHostPath = Path.Combine(
            appHostDirectory,
            Path.GetFileNameWithoutExtension(assemblyPath)
                + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        Assert.True(File.Exists(appHostPath), $"Test apphost not found: {appHostPath}");

        string emptyPath =
            Environment.GetEnvironmentVariable(
                AppHostEmptyPathEnvironmentVariable)
            ?? Path.Combine(
                Path.GetTempPath(),
                $"filter-guard-host-alias-{Guid.NewGuid():N}");
        string aliasDirectory =
            Environment.GetEnvironmentVariable(
                AppHostAliasDirectoryEnvironmentVariable)
            ?? Path.Combine(
                appHostDirectory,
                $"{AppHostAliasDirectoryPrefix}{Guid.NewGuid():N}");
        string aliasFileName =
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string aliasPath = Path.Combine(aliasDirectory, aliasFileName);
        string sharedAliasPath = Path.Combine(
            appHostDirectory,
            aliasFileName);

        try
        {
            Assert.False(
                File.Exists(sharedAliasPath),
                $"A shared apphost alias already exists: {sharedAliasPath}");
            Directory.CreateDirectory(emptyPath);
            CreateIsolatedAppHostDirectory(
                appHostDirectory,
                aliasDirectory,
                appHostPath,
                aliasPath);

            var environment = new Dictionary<string, string?>
            {
                ["PATH"] = emptyPath,
                ["DOTNET_HOST_PATH"] = aliasPath,
                [ExpectedProcessPathEnvironmentVariable] = aliasPath,
            };
            string? dotnetHostPath =
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrEmpty(dotnetHostPath))
            {
                environment["DOTNET_ROOT"] = Path.GetDirectoryName(dotnetHostPath);
            }

            string? readyPath =
                Environment.GetEnvironmentVariable(
                    AppHostReadyEnvironmentVariable);
            if (readyPath is not null)
            {
                File.WriteAllText(readyPath, "ready");
            }

            return await RunProcessAsync(
                aliasPath,
                null,
                environment,
                TestContext.Current.CancellationToken,
                arguments);
        }
        finally
        {
            DeleteDirectoryIfExists(aliasDirectory);
            DeleteDirectoryIfExists(emptyPath);
        }
    }

    private static string GetAppHostDirectory() =>
        Path.GetDirectoryName(typeof(Program).Assembly.Location)
        ?? throw new InvalidOperationException(
            "Test assembly has no directory.");

    private static string[] GetAppHostWorkerDirectories(
        string markerId)
    {
        string appHostDirectory = GetAppHostDirectory();
        string temporaryDirectory = Path.GetTempPath();
        return
        [
            Path.Combine(
                appHostDirectory,
                $"{AppHostAliasDirectoryPrefix}{markerId}-1"),
            Path.Combine(
                temporaryDirectory,
                $"filter-guard-host-alias-{markerId}-1"),
            Path.Combine(
                appHostDirectory,
                $"{AppHostAliasDirectoryPrefix}{markerId}-2"),
            Path.Combine(
                temporaryDirectory,
                $"filter-guard-host-alias-{markerId}-2"),
        ];
    }

    private static string GetDotnetHostPath()
    {
        string dotnetRoot =
            Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? throw new InvalidOperationException(
                "DOTNET_ROOT must identify the test runner's dotnet host.");
        string path = Path.Combine(
            dotnetRoot,
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        Assert.True(
            File.Exists(path),
            $"The test runner's dotnet host was not found: {path}");
        return Path.GetFullPath(path);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void CreateIsolatedAppHostDirectory(
        string sourceDirectory,
        string destinationDirectory,
        string appHostPath,
        string aliasPath)
    {
        string[] supportFiles = Directory
            .EnumerateFiles(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(path => !Path
                .GetRelativePath(sourceDirectory, path)
                .StartsWith(
                    AppHostAliasDirectoryPrefix,
                    StringComparison.Ordinal))
            .ToArray();

        Directory.CreateDirectory(destinationDirectory);
        foreach (string sourcePath in supportFiles)
        {
            string destinationPath = Path.Combine(
                destinationDirectory,
                Path.GetRelativePath(sourceDirectory, sourcePath));
            Directory.CreateDirectory(
                Path.GetDirectoryName(destinationPath)!);
            File.CreateHardLink(destinationPath, sourcePath);
        }

        File.CreateHardLink(aliasPath, appHostPath);
    }

    private static async Task WaitForMarkerAsync(
        string path,
        Task<ProcessResult>? owner = null,
        CancellationToken cancellationToken = default)
    {
        while (!File.Exists(path))
        {
            if (owner?.IsCompleted == true)
            {
                ProcessResult result = await owner;
                throw new InvalidOperationException(
                    "Apphost worker exited before publishing its ready marker. "
                    + $"Exit code: {result.ExitCode}\n"
                    + $"{result.Output}\n{result.Error}");
            }

            await Task.Delay(
                25,
                cancellationToken);
        }
    }

    private static async Task<ProcessResult> RunHostAsync(
        IReadOnlyDictionary<string, string?>? environment,
        params string[] arguments) =>
        await RunHostAsync(
            environment,
            TestContext.Current.CancellationToken,
            arguments);

    private static async Task<ProcessResult> RunHostAsync(
        IReadOnlyDictionary<string, string?>? environment,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        await RunProcessAsync(
            "dotnet",
            typeof(Program).Assembly.Location,
            environment,
            cancellationToken,
            arguments);

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string? firstArgument,
        IReadOnlyDictionary<string, string?>? environment,
        CancellationToken cancellationToken,
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
            await process.WaitForExitAsync(cancellationToken);
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
