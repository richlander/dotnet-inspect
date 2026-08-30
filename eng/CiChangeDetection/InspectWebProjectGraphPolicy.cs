namespace CiChangeDetection;

internal static class InspectWebProjectGraphPolicy
{
    private const string Manifest =
        "eng/inspect-web-gate-projects.txt";
    private static readonly string[] RootProjects =
    [
        "prototypes/inspect-web/engine/InspectWeb.Engine.csproj",
        "src/tsbindgen/tsbindgen.csproj",
        "tests/DotnetInspector.Artifacts.Local.PlatformProbe/LocalPathAdmissionBrowserProbe.csproj",
        "tests/ILInspector.MetadataPrimitives.PlatformProbe/MethodSemanticsBrowserProbe.csproj",
    ];

    internal static void Validate(string repository)
    {
        string manifestPath = Path.Combine(repository, Manifest);
        string[] manifestLines = File.ReadAllLines(manifestPath);
        var actual = manifestLines.ToHashSet(StringComparer.Ordinal);
        if (actual.Count != manifestLines.Length
            || manifestLines.Any(line =>
                string.IsNullOrWhiteSpace(line)
                || line != line.Trim()
                || !line.StartsWith("src/", StringComparison.Ordinal)
                || line.EndsWith('/')
                || line.Split('/').Any(part => part is "" or "." or "..")
                || !EvaluatedProjectGraph.IsProjectDirectory(
                    repository,
                    line)))
        {
            throw new InvalidOperationException(
                $"{Manifest} must contain unique, existing, canonical "
                + "repository-relative src project roots.");
        }

        IReadOnlySet<string>[] graphs = RootProjects
            .Select(root =>
                EvaluatedProjectGraph.ProjectDirectories(
                    repository,
                    root))
            .ToArray();
        var expected = graphs
            .SelectMany(graph => graph)
            .Where(project => project.StartsWith(
                "src/",
                StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        string[] missingRoots = RootProjects
            .Select((root, index) => (
                Root: Path.GetDirectoryName(root)!.Replace(
                    Path.DirectorySeparatorChar,
                    '/'),
                Graph: graphs[index]))
            .Where(item => !item.Graph.Contains(item.Root))
            .Select(item => item.Root)
            .ToArray();
        if (expected.Count == 0 || missingRoots.Length != 0)
        {
            throw new InvalidOperationException(
                "An evaluated inspect-web CI project graph did not contain "
                + "its root or src dependency closure. Missing roots: ["
                + string.Join(", ", missingRoots)
                + "].");
        }

        string[] missing = expected
            .Except(actual, StringComparer.Ordinal)
            .Order()
            .ToArray();
        string[] stale = actual
            .Except(expected, StringComparer.Ordinal)
            .Order()
            .ToArray();
        if (missing.Length != 0 || stale.Length != 0)
        {
            throw new InvalidOperationException(
                $"{Manifest} does not match the evaluated Release "
                + "inspect-web CI product project closure."
                + $"{Environment.NewLine}Missing: [{string.Join(", ", missing)}]"
                + $"{Environment.NewLine}Stale: [{string.Join(", ", stale)}]");
        }
    }
}
