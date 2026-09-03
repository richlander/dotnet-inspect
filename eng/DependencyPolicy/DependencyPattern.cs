using System.IO.Enumeration;

namespace DependencyPolicy;

internal static class DependencyPattern
{
    internal static bool Selects(
        DependencyRule rule,
        string projectName,
        string projectPath) =>
        MatchesAny(rule.Targets, projectName)
        && !MatchesAny(rule.ExcludeTargets, projectName)
        && (rule.ProjectPaths.Length == 0
            || MatchesAny(rule.ProjectPaths, projectPath))
        && !MatchesAny(rule.ExcludeProjectPaths, projectPath);

    internal static bool MatchesAny(
        IEnumerable<string> patterns,
        string value) =>
        patterns.Any(pattern => Matches(pattern, value));

    internal static bool Matches(string pattern, string value) =>
        FileSystemName.MatchesSimpleExpression(
            pattern,
            value,
            ignoreCase: false);
}
