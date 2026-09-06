namespace CiChangeDetection;

/// <summary>
/// Regenerates <c>eng/decompiler-gate-skip-projects.txt</c> from the decompiler
/// test project's evaluated Release dependency closure, so the manifest stays
/// comprehensive as the repository grows instead of drifting from a small
/// hand-maintained list.
/// </summary>
internal static class DecompilerSkipProjectsGenerator
{
    private const string RootProject =
        "src/ILInspector.Decompiler.Tests/ILInspector.Decompiler.Tests.csproj";
    private const string ManifestRelativePath =
        "eng/decompiler-gate-skip-projects.txt";
    private static readonly string[] ScannedTopLevelDirectories =
        ["fixtures", "src", "tests", "tools"];

    /// <summary>
    /// Writes the regenerated manifest and reports whether its content
    /// changed.
    /// </summary>
    /// <param name="repository">The repository root directory.</param>
    /// <returns>True when the manifest content changed on disk.</returns>
    internal static bool Generate(string repository)
    {
        IReadOnlySet<string> closure = EvaluatedProjectGraph.ProjectDirectories(
            repository,
            RootProject);
        string[] skipList = FindAllProjectDirectories(repository)
            .Where(project => !closure.Contains(project))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string manifestPath = Path.Combine(
            repository,
            ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string content = string.Concat(
            skipList.Select(line => line + "\n"));
        string existing = File.Exists(manifestPath)
            ? File.ReadAllText(manifestPath)
            : "";
        if (existing == content)
        {
            return false;
        }

        File.WriteAllText(manifestPath, content);
        return true;
    }

    /// <summary>
    /// Finds every canonical repository-relative project directory under the
    /// scanned top-level directories, skipping build output and hidden
    /// directories.
    /// </summary>
    /// <param name="repository">The repository root directory.</param>
    /// <returns>The discovered project directories, in enumeration order.</returns>
    private static IEnumerable<string> FindAllProjectDirectories(
        string repository)
    {
        foreach (string topLevel in ScannedTopLevelDirectories)
        {
            string root = Path.Combine(repository, topLevel);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string directory in EnumerateDirectories(root))
            {
                string relative = Path
                    .GetRelativePath(repository, directory)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (EvaluatedProjectGraph.IsProjectDirectory(
                        repository,
                        relative))
                {
                    yield return relative;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            yield return directory;
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(child);
                if (name is "bin" or "obj" || name.StartsWith('.'))
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }
}
