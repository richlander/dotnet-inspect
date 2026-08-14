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
                || string.Equals(option, "-method", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ExplicitFilter(option.ToLowerInvariant(), args[++i]));
            }
        }

        return result;
    }

    internal static async ValueTask<string?> ValidateAsync(string[] args, Assembly testAssembly)
    {
        IReadOnlyList<ExplicitFilter> filters = FindIncludedFilters(args);
        if (filters.Count == 0)
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
        catch (ArgumentException)
        {
            // The console runner owns argument diagnostics. Do not replace its
            // error with a secondary validation failure.
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
        await runner.Discover(projectAssembly, pipelineStartup: null, testCases: testCases);

        string assemblyName = Path.GetFileNameWithoutExtension(projectAssembly.AssemblyFileName);
        ExplicitFilter[] unmatched = filters
            .Where(filter => !testCases.Any(testCase => Matches(assemblyName, testCase.TestCase, filter)))
            .ToArray();

        if (unmatched.Length > 0)
        {
            return "error: explicit xUnit filter matched no discovered tests:\n"
                + string.Join('\n', unmatched.Select(filter => $"  {filter.Option} \"{filter.Query}\""));
        }

        if (!testCases.Any(testCase => testCase.PassedFilter))
        {
            return "error: the combined xUnit filters matched no discovered tests.";
        }

        return null;
    }

    private static bool Matches(
        string assemblyName,
        ITestCase testCase,
        ExplicitFilter filter)
    {
        var matcher = new XunitFilters();
        if (filter.Option == "-class")
        {
            matcher.AddIncludedClassFilter(filter.Query);
        }
        else
        {
            matcher.AddIncludedMethodFilter(filter.Query);
        }

        return matcher.Filter(assemblyName, testCase);
    }
}
