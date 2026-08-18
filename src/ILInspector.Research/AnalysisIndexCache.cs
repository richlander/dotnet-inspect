using ILInspector.Analysis;

namespace ILInspector.Research;

static class AnalysisIndexCache
{
    const int MaxCachedIndexes = 8;
    static readonly object s_indexLock = new();
    static readonly List<CachedIndex> s_indexes = [];

    public static LibraryBodyIndex ForPath(string path)
        => ForPath(
            path,
            ResearchFactRequirements.ForAssembly(
                LibraryBodyAnalysisFeatures.Default),
            methodToken: 0);

    public static LibraryBodyIndex ForPath(
        string path,
        ResearchFactRequirements requirements,
        int methodToken)
    {
        var fullPath = Path.GetFullPath(path);
        lock (s_indexLock)
        {
            CachedIndex? cached = s_indexes.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Path, fullPath)
                && (candidate.Index.Features & requirements.Features)
                    == requirements.Features
                && (candidate.MethodToken is null
                    || (requirements.Scope == ResearchAnalysisScope.Member
                        && candidate.MethodToken == methodToken)));
            if (cached is not null)
                return cached.Index;

            if (s_indexes.Count >= MaxCachedIndexes)
                s_indexes.Clear();

            int? scopedToken =
                requirements.Scope == ResearchAnalysisScope.Member
                && methodToken != 0
                    ? methodToken
                    : null;
            IReadOnlySet<int>? bodyScope = scopedToken is { } token
                ? new HashSet<int> { token }
                : null;
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                fullPath,
                requirements.Features,
                bodyScope: bodyScope);
            s_indexes.Add(new CachedIndex(fullPath, scopedToken, index));
            return index;
        }
    }

    sealed record CachedIndex(
        string Path,
        int? MethodToken,
        LibraryBodyIndex Index);
}
