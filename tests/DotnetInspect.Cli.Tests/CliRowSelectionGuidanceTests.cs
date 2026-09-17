using System.Text.RegularExpressions;

namespace DotnetInspect.Cli.Tests;

public sealed partial class CliRowSelectionGuidanceTests
{
    private static readonly HashSet<string> SemanticCommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "depends",
            "demo",
            "ecosystem",
            "extensions",
            "find",
            "implements",
            "inspection",
            "timeline",
            "vocabulary",
        };

    [Fact]
    public void WorkflowCommandsUseExplicitUnitsForBareShortCounts()
    {
        string workflowRoot = Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "workflows");
        var violations = new List<string>();

        foreach (string file in Directory.EnumerateFiles(
            workflowRoot,
            "*.md",
            SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                string command = lines[index].Trim();
                if (!command.Contains("dotnet-inspect", StringComparison.Ordinal))
                    continue;

                int startLine = index + 1;
                while (command.EndsWith('\\') && index + 1 < lines.Length)
                {
                    command = command[..^1] + " " + lines[++index].Trim();
                }

                string commandSegment = ShellContinuationRegex()
                    .Split(command, count: 2)[0];
                if (!BareShortCountRegex().IsMatch(commandSegment)
                    || ExplicitLineUnitRegex().IsMatch(commandSegment)
                    || UsesSemanticRows(commandSegment))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(
                    FindRepositoryRoot(),
                    file);
                violations.Add($"{relative}:{startLine}: {commandSegment}");
            }
        }

        Assert.True(
            violations.Count == 0,
            string.Join(Environment.NewLine, violations));
    }

    private static bool UsesSemanticRows(string command)
    {
        Match match = CommandRegex().Match(command);
        if (!match.Success)
            return true;

        string commandName = match.Groups["command"].Value.Trim(
            '\'',
            '"');
        if (SemanticCommands.Contains(commandName))
            return true;

        if (string.Equals(
                commandName,
                "package",
                StringComparison.OrdinalIgnoreCase)
            || !CommandLineBuilder.KnownCommands.Contains(commandName))
        {
            return PackageSemanticModeRegex().IsMatch(command);
        }

        return false;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not find repository root.");
    }

    [GeneratedRegex(@"(?:\s+\|\s+|\s+&&\s+|\s+;\s+)")]
    private static partial Regex ShellContinuationRegex();

    [GeneratedRegex(@"(?:^|\s)-n(?:\s|=)")]
    private static partial Regex BareShortCountRegex();

    [GeneratedRegex(@"(?:^|\s)--(?:tail-)?lines(?:\s|=|$)")]
    private static partial Regex ExplicitLineUnitRegex();

    [GeneratedRegex(
        @"\bdotnet-inspect\b(?:\s+-y)?(?:\s+--)?\s+(?<command>\S+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex CommandRegex();

    [GeneratedRegex(
        @"(?:^|\s)(?:query|activity|--versions|--versions-with-feed)(?:\s|$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex PackageSemanticModeRegex();
}
