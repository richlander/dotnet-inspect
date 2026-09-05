using CiChangeDetection.Planning;

namespace CiChangeDetection;

internal static class DecompilerProjectGraphPolicy
{
    private const string RootProjectDirectory =
        "src/ILInspector.Decompiler.Tests";

    internal static void Validate(string repository)
    {
        static bool IsAtOrBelowProject(string project, string ancestor) =>
            project == ancestor
            || project.StartsWith($"{ancestor}/", StringComparison.Ordinal);

        static bool ProjectTreesOverlap(string left, string right) =>
            IsAtOrBelowProject(left, right)
            || IsAtOrBelowProject(right, left);

        if (!ProjectTreesOverlap(
                "src/dotnet-inspect/Nested",
                "src/dotnet-inspect")
            || ProjectTreesOverlap(
                "src/dotnet-inspect.TestsExtra",
                "src/dotnet-inspect.Tests"))
        {
            throw new InvalidOperationException(
                "Decompiler skip-list project boundary check is not non-vacuous.");
        }

        // The planner's typed inventory is the single reader for this
        // manifest, so the routing policy and this graph self-test cannot
        // disagree about which lines are admissible.
        if (!ProjectInventory.TryLoad(
                repository,
                "eng/decompiler-gate-skip-projects.txt",
                ["fixtures/", "src/", "tests/", "tools/"],
                requireNonEmpty: false,
                out ProjectInventory inventory))
        {
            throw new InvalidOperationException(
                "eng/decompiler-gate-skip-projects.txt must contain unique, " +
                "existing, canonical repository-relative project roots.");
        }

        IReadOnlyList<string> manifestLines = inventory.Roots;
        var actual = manifestLines.ToHashSet(StringComparer.Ordinal);
        if (actual.Count != manifestLines.Count
            || manifestLines.Any(line =>
                !EvaluatedProjectGraph.IsProjectDirectory(
                    repository,
                    line)))
        {
            throw new InvalidOperationException(
                "eng/decompiler-gate-skip-projects.txt must contain unique, " +
                "existing, canonical repository-relative project roots.");
        }

        IReadOnlySet<string> projectClosure =
            EvaluatedProjectGraph.ProjectDirectories(
                repository,
                $"{RootProjectDirectory}/ILInspector.Decompiler.Tests.csproj");
        if (!projectClosure.Contains(RootProjectDirectory))
        {
            throw new InvalidOperationException(
                "Decompiler project graph does not contain its root: " +
                RootProjectDirectory);
        }

        string[] unsafeExemptions = actual
            .Where(exemption =>
                projectClosure.Any(project =>
                    ProjectTreesOverlap(project, exemption)))
            .Order()
            .ToArray();
        if (unsafeExemptions.Length != 0)
        {
            throw new InvalidOperationException(
                "eng/decompiler-gate-skip-projects.txt exempts project " +
                "trees overlapping projects in the evaluated Release " +
                "ILInspector.Decompiler.Tests graph: [" +
                string.Join(", ", unsafeExemptions) + "].");
        }
    }
}
