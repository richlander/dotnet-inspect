using ILInspector.Analysis;

namespace ILInspector.Research;

static class AnalysisIndexCache
{
    const int MaxCachedIndexes = 8;
    static readonly object s_indexLock = new();
    static readonly Dictionary<string, LibraryBodyIndex> s_indexes = new(PathComparer());

    public static LibraryBodyIndex ForPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (s_indexLock)
        {
            if (s_indexes.TryGetValue(fullPath, out var index))
                return index;
            if (s_indexes.Count >= MaxCachedIndexes)
                s_indexes.Clear();
            index = LibraryBodyIndex.Open(fullPath);
            s_indexes[fullPath] = index;
            return index;
        }
    }

    static StringComparer PathComparer()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
