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
        => Rank(
            query,
            candidates,
            static candidate => candidate.Key,
            static candidate => candidate.Name,
            static candidate => candidate.Full,
            createHit);

    /// <summary>
    /// Applies the product's Type-name ranking to a caller-owned candidate
    /// shape without requiring that shape to inherit a product model.
    /// </summary>
    public static THit[] Rank<TCandidate, THit>(
        string? query,
        IReadOnlyList<TCandidate> candidates,
        Func<TCandidate, string> selectKey,
        Func<TCandidate, string> selectName,
        Func<TCandidate, string> selectFullName,
        Func<string, LoadedTypeSearchMatchKind, THit> createHit)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(selectKey);
        ArgumentNullException.ThrowIfNull(selectName);
        ArgumentNullException.ThrowIfNull(selectFullName);
        ArgumentNullException.ThrowIfNull(createHit);

        string pattern = query?.Trim() ?? "";
        if (pattern.Length == 0)
        {
            return
            [
                .. candidates
                    .OrderBy(candidate => selectName(candidate).Length)
                    .ThenBy(
                        selectName,
                        StringComparer.OrdinalIgnoreCase)
                    .Take(EmptyQueryLimit)
                    .Select(candidate =>
                        createHit(
                            selectKey(candidate),
                            LoadedTypeSearchMatchKind.All)),
            ];
        }

        var hits = new List<THit>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        void AddTier(
            LoadedTypeSearchMatchKind kind,
            Func<TCandidate, bool> predicate)
        {
            foreach (TCandidate candidate in candidates
                .Where(candidate =>
                    !used.Contains(selectKey(candidate))
                    && predicate(candidate))
                .OrderBy(selectFullName, WithinTier))
            {
                string key = selectKey(candidate);
                if (used.Add(key))
                    hits.Add(createHit(key, kind));
            }
        }

        bool isGlob = TypeMatcher.IsTypeGlobPattern(pattern);
        AddTier(
            LoadedTypeSearchMatchKind.Exact,
            candidate => TypeMatcher.Matches(
                    selectFullName(candidate),
                    pattern)
                || (isGlob
                    && TypeMatcher.MatchesTypeFilter(
                        selectFullName(candidate),
                        pattern)));
        Dictionary<string, TypeNameMatchTier?> tiers = candidates
            .DistinctBy(selectKey)
            .ToDictionary(
                selectKey,
                candidate => TypeNameMatchRanking.Classify(
                    selectFullName(candidate),
                    pattern),
                StringComparer.Ordinal);
        AddTier(
            LoadedTypeSearchMatchKind.Prefix,
            candidate => tiers[selectKey(candidate)]
                == TypeNameMatchTier.Prefix);
        AddTier(
            LoadedTypeSearchMatchKind.Substring,
            candidate => tiers[selectKey(candidate)]
                == TypeNameMatchTier.Substring);
        AddTier(
            LoadedTypeSearchMatchKind.Path,
            candidate => tiers[selectKey(candidate)]
                == TypeNameMatchTier.Path);

        var remaining =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (TCandidate candidate in candidates.Where(
            candidate => !used.Contains(selectKey(candidate))))
        {
            string fullName = selectFullName(candidate);
            if (!remaining.TryGetValue(
                    fullName,
                    out List<string>? keys))
            {
                remaining[fullName] = keys = [];
            }
            keys.Add(selectKey(candidate));
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
