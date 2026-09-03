using System.Collections.Immutable;
using System.Text.Json;

namespace DependencyPolicy;

internal static class DependencyPolicyApp
{
    internal static int Run(string[] args)
    {
        try
        {
            Options options = ParseOptions(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(
                    "Usage: DependencyPolicy "
                    + "[--repository PATH] [--rules PATH] [--dotnet PATH]");
                return 0;
            }

            string repository = ResolveFullPath(
                options.Repository,
                "repository");
            string rulesPath = ResolveFullPath(
                options.Rules
                    ?? Path.Combine(
                        repository,
                        "eng",
                        "dependency-policy.json"),
                "rules");
            DependencyPolicyDocument policy = PolicyLoader.Load(rulesPath);
            string solution = ResolveRepositoryPath(
                repository,
                policy.Solution,
                "solution");
            string dotnetHost = options.DotnetHost
                ?? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? "dotnet";
            if (string.IsNullOrWhiteSpace(dotnetHost))
            {
                throw new DependencyPolicyException(
                    "The dotnet host path must be non-empty.");
            }

            RepositoryDependencyGraph graph = RepositoryGraphReader.Read(
                repository,
                solution,
                policy.Configuration,
                dotnetHost,
                policy);
            var violations = PolicyEvaluator.Evaluate(policy, graph);

            int violationExitCode = ReportViolations(
                violations,
                Console.Error);
            if (violationExitCode != 0)
            {
                return violationExitCode;
            }

            int inspectedAssemblyCount = graph.Projects.Values.Count(
                project => project.AssemblyName is not null);
            Console.WriteLine(
                $"Dependency policy passed: {policy.Rules.Length} rules, "
                + $"{graph.Projects.Count} evaluated projects, "
                + $"{inspectedAssemblyCount} inspected assemblies.");
            return 0;
        }
        catch (DependencyPolicyException exception)
        {
            Console.Error.WriteLine(
                $"error DP0002: {exception.Message}");
            return 2;
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine(
                $"error DP0002: Invalid dependency policy JSON: "
                + exception.Message);
            return 2;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine(
                $"error DP0002: Could not read dependency policy evidence: "
                + exception.Message);
            return 2;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine(
                $"error DP0002: Could not access dependency policy evidence: "
                + exception.Message);
            return 2;
        }
    }

    internal static int ReportViolations(
        ImmutableArray<DependencyViolation> violations,
        TextWriter error)
    {
        foreach (DependencyViolation violation in violations)
        {
            error.WriteLine($"error DP0001: {violation}");
        }

        if (violations.Length == 0)
        {
            return 0;
        }

        error.WriteLine(
            $"Dependency policy failed with "
            + $"{violations.Length} violation(s).");
        return 1;
    }

    private static Options ParseOptions(string[] args)
    {
        string repository = Environment.CurrentDirectory;
        string? rules = null;
        string? dotnetHost = null;
        bool showHelp = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--repository":
                    repository = RequireValue(args, ref index, argument);
                    break;
                case "--rules":
                    rules = RequireValue(args, ref index, argument);
                    break;
                case "--dotnet":
                    dotnetHost = RequireValue(args, ref index, argument);
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new DependencyPolicyException(
                        $"Unknown argument '{argument}'.");
            }
        }

        return new(repository, rules, dotnetHost, showHelp);
    }

    private static string RequireValue(
        string[] args,
        ref int index,
        string option)
    {
        index++;
        if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new DependencyPolicyException(
                $"Option '{option}' requires a value.");
        }

        return args[index];
    }

    private static string ResolveRepositoryPath(
        string repository,
        string relativePath,
        string description)
    {
        try
        {
            if (Path.IsPathRooted(relativePath))
            {
                throw new DependencyPolicyException(
                    $"Dependency policy {description} path must be "
                    + "repository-relative.");
            }

            string fullPath = Path.GetFullPath(
                Path.Combine(
                    repository,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
            string relative = Path.GetRelativePath(repository, fullPath);
            if (relative == ".."
                || relative.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                throw new DependencyPolicyException(
                    $"Dependency policy {description} path escapes the "
                    + "repository.");
            }

            if (!File.Exists(fullPath))
            {
                throw new DependencyPolicyException(
                    $"Dependency policy {description} does not exist: "
                    + $"'{relativePath}'.");
            }

            return fullPath;
        }
        catch (ArgumentException)
        {
            throw new DependencyPolicyException(
                $"Dependency policy {description} path is invalid.");
        }
    }

    private static string ResolveFullPath(
        string path,
        string description)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            throw new DependencyPolicyException(
                $"The {description} path is invalid.");
        }
    }

    private sealed record Options(
        string Repository,
        string? Rules,
        string? DotnetHost,
        bool ShowHelp);
}
