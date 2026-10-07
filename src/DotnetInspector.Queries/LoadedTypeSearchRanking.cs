using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>One caller-authorized Type name with its opaque activation key.</summary>
public record LoadedTypeSearchCandidate(
    string Key,
    string Name,
    string Full);

/// <summary>The semantic tier that admitted one loaded Type candidate.</summary>
public enum LoadedTypeSearchMatchKind
{
    All,
    Exact,
    Prefix,
    Substring,
    Path,
    Fuzzy,
}

/// <summary>
/// Ranks one finite caller-authorized population of already-loaded Type names.
/// </summary>
public static class LoadedTypeSearchRanking
{
    private const int EmptyQueryLimit = 30;
    private const int FuzzyResultLimit = 8;
    private const int ResultLimit = 40;
    private const double MinimumFuzzySimilarity = 0.5;

    private static readonly Comparer<string> WithinTier =
        Comparer<string>.Create(TypeNameMatchRanking.CompareWithinTier);

    /// <summary>
    /// Applies the product's direct, broadened, and fuzzy Type-name ranking
    /// without acquiring or inspecting an artifact.
    /// </summary>
    public static THit[] Rank<THit>(
        string? query,
        IReadOnlyList<LoadedTypeSearchCandidate> candidates,
        Func<string, LoadedTypeSearchMatchKind, THit> createHit)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(createHit);

        string pattern = query?.Trim() ?? "";
        if (pattern.Length == 0)
        {
            return
            [
                .. candidates
                    .OrderBy(candidate => candidate.Name.Length)
                    .ThenBy(
                        candidate => candidate.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .Take(EmptyQueryLimit)
                    .Select(candidate =>
                        createHit(
                            candidate.Key,
                            LoadedTypeSearchMatchKind.All)),
            ];
        }

        var hits = new List<THit>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        void AddTier(
            LoadedTypeSearchMatchKind kind,
            Func<LoadedTypeSearchCandidate, bool> predicate)
        {
            foreach (LoadedTypeSearchCandidate candidate in candidates
                .Where(candidate =>
                    !used.Contains(candidate.Key)
                    && predicate(candidate))
                .OrderBy(candidate => candidate.Full, WithinTier))
            {
                if (used.Add(candidate.Key))
                    hits.Add(createHit(candidate.Key, kind));
            }
        }

        bool isGlob = TypeMatcher.IsTypeGlobPattern(pattern);
        AddTier(
            LoadedTypeSearchMatchKind.Exact,
            candidate => TypeMatcher.Matches(candidate.Full, pattern)
                || (isGlob
                    && TypeMatcher.MatchesTypeFilter(
                        candidate.Full,
                        pattern)));
        Dictionary<string, TypeNameMatchTier?> tiers = candidates
            .DistinctBy(static candidate => candidate.Key)
            .ToDictionary(
                static candidate => candidate.Key,
                candidate => TypeNameMatchRanking.Classify(
                    candidate.Full,
                    pattern),
                StringComparer.Ordinal);
        AddTier(
            LoadedTypeSearchMatchKind.Prefix,
            candidate => tiers[candidate.Key] == TypeNameMatchTier.Prefix);
        AddTier(
            LoadedTypeSearchMatchKind.Substring,
            candidate => tiers[candidate.Key] == TypeNameMatchTier.Substring);
        AddTier(
            LoadedTypeSearchMatchKind.Path,
            candidate => tiers[candidate.Key] == TypeNameMatchTier.Path);

        var remaining =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (LoadedTypeSearchCandidate candidate in candidates.Where(
            candidate => !used.Contains(candidate.Key)))
        {
            if (!remaining.TryGetValue(
                    candidate.Full,
                    out List<string>? keys))
            {
                remaining[candidate.Full] = keys = [];
            }
            keys.Add(candidate.Key);
        }

        if (remaining.Count > 0)
        {
            foreach ((string name, _) in TypeMatcher.FindClosest(
                remaining.Keys,
                pattern,
                MinimumFuzzySimilarity,
                FuzzyResultLimit))
            {
                if (!remaining.TryGetValue(name, out List<string>? keys))
                    continue;
                foreach (string key in keys)
                {
                    if (used.Add(key))
                    {
                        hits.Add(
                            createHit(
                                key,
                                LoadedTypeSearchMatchKind.Fuzzy));
                    }
                }
            }
        }

        return [.. hits.Take(ResultLimit)];
    }
}
