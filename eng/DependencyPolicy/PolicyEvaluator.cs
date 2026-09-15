using System.Collections.Immutable;

namespace DependencyPolicy;

internal sealed record DependencyViolation(
    string RuleId,
    string Source,
    DependencyGraphKind Graph,
    string Target,
    string Dependency)
{
    public override string ToString() =>
        $"{RuleId} [{Graph.ToString().ToLowerInvariant()}] "
        + $"{Target} -> {Dependency} is not permitted ({Source})";
}

internal static class PolicyEvaluator
{
    internal static ImmutableArray<DependencyViolation> Evaluate(
        DependencyPolicyDocument policy,
        RepositoryDependencyGraph graph)
    {
        var violations = ImmutableArray.CreateBuilder<DependencyViolation>();

        foreach (DependencyRule rule in policy.Rules)
        {
            foreach (string targetPattern in rule.Targets)
            {
                bool matched = graph.Projects.Values.Any(project =>
                    DependencyPattern.Matches(
                        targetPattern,
                        project.ProjectName)
                    && DependencyPattern.Selects(
                        rule,
                        project.ProjectName,
                        project.ProjectPath));
                if (!matched)
                {
                    throw new DependencyPolicyException(
                        $"Rule '{rule.Id}' target pattern "
                        + $"'{targetPattern}' selects no projects.");
                }
            }

            ProjectDependencyNode[] targets = graph.Projects.Values
                .Where(project => DependencyPattern.Selects(
                    rule,
                    project.ProjectName,
                    project.ProjectPath))
                .OrderBy(
                    project => project.ProjectName,
                    StringComparer.Ordinal)
                .ToArray();
            if (targets.Length == 0)
            {
                throw new DependencyPolicyException(
                    $"Rule '{rule.Id}' selects no projects.");
            }

            foreach (DependencyGraphKind graphKind in rule.Graphs)
            {
                foreach (ProjectDependencyNode target in targets)
                {
                    IEnumerable<string> dependencies = graphKind switch
                    {
                        DependencyGraphKind.Project =>
                            target.ProjectReferences,
                        DependencyGraphKind.Assembly
                            when target.AssemblyName is null =>
                            throw new DependencyPolicyException(
                                $"Rule '{rule.Id}' requires the Release "
                                + $"assembly for project "
                                + $"'{target.ProjectName}'. Build the solution "
                                + "before running dependency policy."),
                        DependencyGraphKind.Assembly =>
                            target.AssemblyReferences,
                        _ => throw new DependencyPolicyException(
                            $"Rule '{rule.Id}' uses unsupported graph "
                            + $"'{graphKind}'."),
                    };

                    foreach (string dependency in dependencies)
                    {
                        if (IsViolation(
                                rule,
                                graphKind,
                                dependency,
                                graph))
                        {
                            violations.Add(new(
                                rule.Id,
                                rule.Source,
                                graphKind,
                                target.ProjectName,
                                dependency));
                        }
                    }
                }
            }
        }

        return violations
            .OrderBy(violation => violation.RuleId, StringComparer.Ordinal)
            .ThenBy(violation => violation.Graph)
            .ThenBy(violation => violation.Target, StringComparer.Ordinal)
            .ThenBy(violation => violation.Dependency, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static bool IsViolation(
        DependencyRule rule,
        DependencyGraphKind graphKind,
        string dependency,
        RepositoryDependencyGraph graph)
    {
        if (rule.AllowOnly is not null)
        {
            return !rule.AllowOnly.Any(pattern => IsAllowed(
                pattern,
                graphKind,
                dependency,
                graph));
        }

        return DependencyPattern.MatchesAny(rule.Deny!, dependency)
            && !DependencyPattern.MatchesAny(rule.Except, dependency);
    }

    private static bool IsAllowed(
        string pattern,
        DependencyGraphKind graphKind,
        string dependency,
        RepositoryDependencyGraph graph) =>
        pattern switch
        {
            "$platform" => graphKind == DependencyGraphKind.Assembly
                && graph.PlatformAssemblyNames.Contains(dependency),
            "$repository" => graphKind == DependencyGraphKind.Assembly
                && graph.RepositoryAssemblyNames.Contains(dependency),
            _ => DependencyPattern.Matches(pattern, dependency),
        };
}
