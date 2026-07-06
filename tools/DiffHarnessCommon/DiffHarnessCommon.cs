namespace ILInspector.DiffHarnessCommon;

public enum OutputFormat
{
    Markdown,
    Tsv,
    Jsonl,
}

public sealed record AssemblyPair(string OldPath, string NewPath);

public static class DiffHarnessCommon
{
    public static bool TryParseOutputFormat(string value, out OutputFormat format)
    {
        format = value.ToLowerInvariant() switch
        {
            "markdown" or "md" => OutputFormat.Markdown,
            "tsv" => OutputFormat.Tsv,
            "jsonl" => OutputFormat.Jsonl,
            _ => (OutputFormat)(-1),
        };
        return format is OutputFormat.Markdown or OutputFormat.Tsv or OutputFormat.Jsonl;
    }

    public static IEnumerable<AssemblyPair> ReadManifest(string manifestPath)
    {
        string manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();
        int lineNumber = 0;
        foreach (var rawLine in File.ReadLines(manifestPath))
        {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var parts = line.Split('\t', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
                throw new InvalidOperationException($"Invalid pair manifest line {lineNumber}: expected old<TAB>new.");

            yield return new AssemblyPair(ResolveManifestPath(manifestDirectory, parts[0]), ResolveManifestPath(manifestDirectory, parts[1]));
        }
    }

    public static string DisplayPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal)
            ? fullPath
            : relative;
    }

    public static string AbsoluteSnapshotPath(string path) => Path.GetFullPath(path);

    public static string RelativeSnapshotPath(string path)
        => Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.GetFullPath(path)).Replace('\\', '/');

    public static string PathLabel(string path, bool snapshotPath, Func<string, string> snapshotPathFormatter)
        => snapshotPath ? snapshotPathFormatter(path) : DisplayPath(path);

    static string ResolveManifestPath(string manifestDirectory, string path)
        => Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(Path.Combine(manifestDirectory, path));
}
