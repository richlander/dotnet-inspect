using System.Diagnostics;
using System.Reflection;
using System.Text;
using Xunit.Runner.Common;
using Xunit.Runner.InProc.SystemConsole;
using Xunit.Sdk;

namespace ILInspector.Decompiler.Tests.Gating;

internal sealed record ExplicitFilter(string Option, string Query);

internal static class ExplicitFilterGuard
{
    internal const string PreflightArgument =
        "--internal-explicit-filter-preflight";

    internal const string SimulatedFailureEnvironmentVariable =
        "DOTNET_INSPECT_FILTER_GUARD_SIMULATED_FAILURE";

    private const string ProtocolPrefix =
        "__DOTNET_INSPECT_FILTER_PREFLIGHT__:";

    private const string InfrastructureError =
        "error: explicit xUnit filter preflight could not complete.";

    private const int DiagnosticLimit = 2048;

    internal static IReadOnlyList<ExplicitFilter> FindIncludedFilters(IReadOnlyList<string> args)
    {
        var result = new List<ExplicitFilter>();

        for (int i = 0; i + 1 < args.Count; i++)
        {
            string option = args[i];
            if (string.Equals(option, "-class", StringComparison.OrdinalIgnoreCase)
                || string.Equals(option, "-method", StringComparison.OrdinalIgnoreCase)
                || string.Equals(option, "-filter", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ExplicitFilter(option.ToLowerInvariant(), args[++i]));
            }
        }

        return result;
    }

    internal static async ValueTask<string?> ValidateAsync(string[] args, Assembly testAssembly)
    {
        if (!MightContainExplicitSelection(args))
        {
            return null;
        }

        string assemblyPath = testAssembly.Location;
        if (string.IsNullOrEmpty(assemblyPath))
        {
            return null;
        }

        ProcessStartInfo? startInfo = CreatePreflightStartInfo(assemblyPath);
        if (startInfo is null)
        {
            return FormatInfrastructureError(
                "The current process path is unavailable.");
        }

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.ArgumentList.Add(PreflightArgument);
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start filter preflight.");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string standardOutput = await output;
            string standardError = await error;

            if (process.ExitCode != 0)
            {
                return FormatInfrastructureError(
                    $"Child exit code: {process.ExitCode}.",
                    standardOutput,
                    standardError);
            }

            PreflightResult? result = ParseProtocol(standardOutput);
            return result?.Outcome switch
            {
                PreflightOutcome.Pass or PreflightOutcome.Defer => null,
                PreflightOutcome.Reject => result.Error,
                _ => FormatInfrastructureError(
                    "The child returned no valid protocol result.",
                    standardOutput,
                    standardError),
            };
        }
        catch (Exception ex)
        {
            return FormatInfrastructureError(
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string FormatInfrastructureError(
        string detail,
        string? standardOutput = null,
        string? standardError = null)
    {
        var message = new StringBuilder(InfrastructureError);
        AppendDiagnostic(message, "detail", detail);
        AppendDiagnostic(message, "stdout", standardOutput);
        AppendDiagnostic(message, "stderr", standardError);
        return message.ToString();
    }

    private static void AppendDiagnostic(
        StringBuilder message,
        string label,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string diagnostic = value.Trim();
        if (diagnostic.Length > DiagnosticLimit)
        {
            diagnostic = diagnostic[..DiagnosticLimit] + "...";
        }

        message.AppendLine();
        message.Append(label);
        message.Append(": ");
        message.Append(diagnostic);
    }

    private static ProcessStartInfo? CreatePreflightStartInfo(
        string assemblyPath)
    {
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            return null;
        }

        var fallback = new ProcessStartInfo(processPath);
        fallback.ArgumentList.Add(assemblyPath);

        return fallback;
    }

    internal static async ValueTask<int> RunPreflightAsync(
        string[] args,
        Assembly testAssembly)
    {
        if (Environment.GetEnvironmentVariable(
                SimulatedFailureEnvironmentVariable) is string simulatedFailure)
        {
            Console.Error.WriteLine(simulatedFailure);
            return 86;
        }

        PreflightResult result;
        try
        {
            result = await ValidateInProcessAsync(args, testAssembly);
        }
        catch (Exception)
        {
            result = PreflightResult.Defer;
        }

        Console.Out.WriteLine(FormatProtocol(result));
        return 0;
    }

    private static async ValueTask<PreflightResult> ValidateInProcessAsync(
        string[] args,
        Assembly testAssembly)
    {
        var commandLine = new CommandLine(
            new ConsoleHelper(TextReader.Null, TextWriter.Null),
            testAssembly,
            args);
        if (commandLine.HelpRequested)
        {
            return PreflightResult.Defer;
        }

        var registrationWarnings = new List<string>();
        try
        {
            SerializationHelper.Instance.AddRegisteredSerializers(
                testAssembly,
                registrationWarnings);
        }
        catch (Exception)
        {
            return PreflightResult.Defer;
        }

        if (registrationWarnings.Count > 0)
        {
            return PreflightResult.Defer;
        }

        XunitProjectAssembly projectAssembly;
        try
        {
            projectAssembly = commandLine.Parse();
        }
        catch (Exception)
        {
            // The console runner repeats this parse and owns its diagnostic and
            // exit code. Do not replace either with a preflight failure.
            return PreflightResult.Defer;
        }

        IReadOnlyList<ExplicitFilter> filters = FindIncludedFilters(
            projectAssembly.Configuration.Filters.ToXunit3Arguments().ToArray());
        bool hasDirectSelection = projectAssembly.TestCaseIDsToRun.Count > 0
            || projectAssembly.TestCasesToRun.Count > 0;
        if (filters.Count == 0 && !hasDirectSelection)
        {
            return PreflightResult.Defer;
        }

        if (projectAssembly.Project.Configuration.List is not null
            || projectAssembly.Project.Configuration.AssemblyInfoOrDefault)
        {
            return PreflightResult.Defer;
        }

        projectAssembly.Configuration.PreEnumerateTheories ??= false;
        using var cancellation = new CancellationTokenSource();
        var runner = new ProjectAssemblyRunner(testAssembly, AutomatedMode.Off, cancellation);
        var testCases = new List<(ITestCase TestCase, bool PassedFilter)>();
        try
        {
            try
            {
                await runner.Discover(projectAssembly, pipelineStartup: null, testCases: testCases);
            }
            catch (Exception)
            {
                // As with parsing, the real runner owns discovery failures and will
                // surface them through its normal reporter and exit code.
                return PreflightResult.Defer;
            }

            string assemblyName = Path.GetFileNameWithoutExtension(projectAssembly.AssemblyFileName);
            ExplicitFilter[] unmatched = filters
                .Where(filter => !testCases.Any(testCase => Matches(assemblyName, testCase.TestCase, filter)))
                .ToArray();

            if (unmatched.Length > 0)
            {
                return PreflightResult.Reject(
                    "error: explicit xUnit filter matched no discovered tests:\n"
                    + string.Join(
                        '\n',
                        unmatched.Select(
                            filter => $"  {filter.Option} \"{filter.Query}\"")));
            }

            HashSet<string> filteredTestCaseIds = testCases
                .Where(testCase => testCase.PassedFilter)
                .Select(testCase => testCase.TestCase.UniqueID)
                .ToHashSet(StringComparer.Ordinal);
            string[] unmatchedIds = projectAssembly.TestCaseIDsToRun
                .Where(id => !filteredTestCaseIds.Contains(id))
                .ToArray();
            if (unmatchedIds.Length > 0)
            {
                return PreflightResult.Reject(
                    "error: one or more -id test-case IDs matched no discovered tests:\n"
                    + string.Join(
                        '\n',
                        unmatchedIds.Select(id => $"  \"{id}\"")));
            }

            DeserializedRunTests runSelection = DeserializeRunTestCases(projectAssembly);
            try
            {
                if (runSelection.InvalidCount > 0)
                {
                    return PreflightResult.Reject(
                        "error: one or more -run test-case serializations could not be deserialized.");
                }

                if (!HasRunnableSelection(projectAssembly, testCases, runSelection.TestCases))
                {
                    return PreflightResult.Reject(
                        "error: the combined xUnit selectors matched no runnable tests.");
                }
            }
            finally
            {
                await DisposeTestCasesAsync(runSelection.TestCases);
            }
        }
        finally
        {
            await DisposeTestCasesAsync(testCases.Select(testCase => testCase.TestCase));
        }

        return PreflightResult.Pass;
    }

    private static bool MightContainExplicitSelection(IReadOnlyList<string> args)
        => (args.Count == 2 && args[0] == "@@")
            || args.Any(arg => string.Equals(arg, "-class", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-method", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-filter", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-id", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-run", StringComparison.OrdinalIgnoreCase));

    private static bool HasRunnableSelection(
        XunitProjectAssembly projectAssembly,
        IReadOnlyList<(ITestCase TestCase, bool PassedFilter)> testCases,
        IReadOnlyList<ITestCase> runTestCases)
    {
        bool directSelection = runTestCases.Count > 0
            || projectAssembly.TestCaseIDsToRun.Count > 0;
        var selected = new List<ITestCase>(runTestCases);

        if (projectAssembly.TestCaseIDsToRun.Count > 0)
        {
            selected.AddRange(testCases
                .Where(testCase =>
                    testCase.PassedFilter
                    && projectAssembly.TestCaseIDsToRun.Contains(testCase.TestCase.UniqueID))
                .Select(testCase => testCase.TestCase));
        }
        else if (!directSelection)
        {
            selected.AddRange(testCases
                .Where(testCase => testCase.PassedFilter)
                .Select(testCase => testCase.TestCase));
        }

        ExplicitOption explicitOption = projectAssembly.Configuration.ExplicitOptionOrDefault;
        if (projectAssembly.AutoEnableExplicit
            && directSelection
            && selected.Count > 0
            && selected.All(testCase => testCase.Explicit))
        {
            explicitOption = ExplicitOption.Only;
        }

        return selected.Any(testCase => IsRunnable(testCase, explicitOption));
    }

    private static DeserializedRunTests DeserializeRunTestCases(
        XunitProjectAssembly projectAssembly)
    {
        var result = new List<ITestCase>();
        int invalidCount = 0;
        foreach (string serialization in projectAssembly.TestCasesToRun)
        {
            try
            {
                if (SerializationHelper.Instance.Deserialize(serialization) is ITestCase testCase)
                {
                    result.Add(testCase);
                }
                else
                {
                    invalidCount++;
                }
            }
            catch (Exception)
            {
                invalidCount++;
            }
        }

        return new DeserializedRunTests(result, invalidCount);
    }

    private static async ValueTask DisposeTestCasesAsync(IEnumerable<ITestCase> testCases)
    {
        foreach (ITestCase testCase in testCases)
        {
            try
            {
                if (testCase is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (testCase is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception)
            {
                // Preflight cleanup must not replace the real runner's
                // execution, cleanup, reporting, or exit code.
            }
        }
    }

    private static bool IsRunnable(ITestCase testCase, ExplicitOption explicitOption)
        => explicitOption switch
        {
            ExplicitOption.Off => !testCase.Explicit,
            ExplicitOption.On => true,
            ExplicitOption.Only => testCase.Explicit,
            _ => false,
        };

    private static bool Matches(
        string assemblyName,
        ITestCase testCase,
        ExplicitFilter filter)
    {
        var matcher = new XunitFilters();
        switch (filter.Option)
        {
            case "-class":
                matcher.AddIncludedClassFilter(filter.Query);
                break;
            case "-method":
                matcher.AddIncludedMethodFilter(filter.Query);
                break;
            case "-filter":
                matcher.AddQueryFilter(filter.Query);
                break;
        }

        return matcher.Filter(assemblyName, testCase);
    }

    private static string FormatProtocol(PreflightResult result)
    {
        string payload = result.Error is null
            ? string.Empty
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(result.Error));
        return $"{ProtocolPrefix}{result.Outcome}:{payload}";
    }

    private static PreflightResult? ParseProtocol(string output)
    {
        foreach (string line in output.Split('\n').Reverse())
        {
            string candidate = line.TrimEnd('\r');
            if (!candidate.StartsWith(ProtocolPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string[] pieces = candidate[ProtocolPrefix.Length..].Split(':', 2);
            if (pieces.Length != 2
                || !Enum.TryParse(pieces[0], out PreflightOutcome outcome))
            {
                return null;
            }

            if (outcome != PreflightOutcome.Reject)
            {
                return new PreflightResult(outcome);
            }

            try
            {
                return PreflightResult.Reject(
                    Encoding.UTF8.GetString(Convert.FromBase64String(pieces[1])));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
    }

    private sealed record DeserializedRunTests(List<ITestCase> TestCases, int InvalidCount);

    private enum PreflightOutcome
    {
        Pass,
        Reject,
        Defer,
    }

    private sealed record PreflightResult(
        PreflightOutcome Outcome,
        string? Error = null)
    {
        internal static PreflightResult Pass { get; } =
            new(PreflightOutcome.Pass);

        internal static PreflightResult Defer { get; } =
            new(PreflightOutcome.Defer);

        internal static PreflightResult Reject(string error) =>
            new(PreflightOutcome.Reject, error);
    }
}
