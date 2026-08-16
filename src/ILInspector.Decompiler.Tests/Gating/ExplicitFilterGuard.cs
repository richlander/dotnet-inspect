using System.Reflection;
using Xunit.Runner.Common;
using Xunit.Runner.InProc.SystemConsole;
using Xunit.Sdk;

namespace ILInspector.Decompiler.Tests.Gating;

internal sealed record ExplicitFilter(string Option, string Query);

internal static class ExplicitFilterGuard
{
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
        if (!MightContainIncludedFilter(args))
        {
            return null;
        }

        var commandLine = new CommandLine(
            new ConsoleHelper(TextReader.Null, TextWriter.Null),
            testAssembly,
            args);
        if (commandLine.HelpRequested)
        {
            return null;
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
            return null;
        }

        IReadOnlyList<ExplicitFilter> filters = FindIncludedFilters(
            projectAssembly.Configuration.Filters.ToXunit3Arguments().ToArray());
        if (filters.Count == 0)
        {
            return null;
        }

        if (projectAssembly.Project.Configuration.List is not null
            || projectAssembly.Project.Configuration.AssemblyInfoOrDefault)
        {
            return null;
        }

        projectAssembly.Configuration.PreEnumerateTheories ??= false;
        using var cancellation = new CancellationTokenSource();
        var runner = new ProjectAssemblyRunner(testAssembly, AutomatedMode.Off, cancellation);
        var testCases = new List<(ITestCase TestCase, bool PassedFilter)>();
        try
        {
            await runner.Discover(projectAssembly, pipelineStartup: null, testCases: testCases);
        }
        catch (Exception)
        {
            // As with parsing, the real runner owns discovery failures and will
            // surface them through its normal reporter and exit code.
            return null;
        }

        string assemblyName = Path.GetFileNameWithoutExtension(projectAssembly.AssemblyFileName);
        ExplicitFilter[] unmatched = filters
            .Where(filter => !testCases.Any(testCase => Matches(assemblyName, testCase.TestCase, filter)))
            .ToArray();

        if (unmatched.Length > 0)
        {
            return "error: explicit xUnit filter matched no discovered tests:\n"
                + string.Join('\n', unmatched.Select(filter => $"  {filter.Option} \"{filter.Query}\""));
        }

        try
        {
            if (projectAssembly.TestCasesToRun.Count > 0)
            {
                SerializationHelper.Instance.AddRegisteredSerializers(testAssembly);
            }
        }
        catch (Exception)
        {
            // The console runner performs the same registration and owns any
            // registration diagnostic and exit code.
            return null;
        }

        DeserializedRunTests runSelection = DeserializeRunTestCases(projectAssembly);
        try
        {
            if (runSelection.InvalidCount > 0)
            {
                return "error: one or more -run test-case serializations could not be deserialized.";
            }

            if (!HasRunnableSelection(projectAssembly, testCases, runSelection.TestCases))
            {
                return "error: the combined xUnit selectors matched no runnable tests.";
            }
        }
        finally
        {
            await DisposeTestCasesAsync(runSelection.TestCases);
        }

        return null;
    }

    private static bool MightContainIncludedFilter(IReadOnlyList<string> args)
        => (args.Count == 2 && args[0] == "@@")
            || args.Any(arg => string.Equals(arg, "-class", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-method", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-filter", StringComparison.OrdinalIgnoreCase));

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

    private static DeserializedRunTests DeserializeRunTestCases(XunitProjectAssembly projectAssembly)
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

    private sealed record DeserializedRunTests(List<ITestCase> TestCases, int InvalidCount);
}
