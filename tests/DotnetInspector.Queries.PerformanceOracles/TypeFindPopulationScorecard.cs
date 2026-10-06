using System.Runtime.CompilerServices;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

/// <summary>A real type-name population and the pattern settled against it.</summary>
public sealed record TypeFindPopulationScorecardAsset(
    string Pattern,
    IReadOnlyList<TypeFindPopulationCandidate<int>> Candidates);

/// <summary>A normalized selected row used to compare the three implementations.</summary>
public sealed record TypeFindPopulationScorecardRow(
    TypeFindPopulationTier Tier,
    string EffectivePattern,
    int Association,
    string FullName,
    double Similarity);

/// <summary>
/// Compares idiomatic LINQ, pinned NLinq, and the shipping Type Find selector
/// against the same immutable population and settlement contract.
/// </summary>
public static class TypeFindPopulationScorecard
{
    public static IReadOnlyList<ScorecardAsset<TypeFindPopulationScorecardAsset>> LoadAssets(
        string pattern,
        IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> names = ScorecardAssetNames.FromPaths(paths);
        var assets = new List<ScorecardAsset<TypeFindPopulationScorecardAsset>>(paths.Count);
        for (int i = 0; i < paths.Count; i++)
        {
            using var session = AssemblyInspectionSession.Open(paths[i]);
            ApiSurface surface = session.ApiSurface(includeAll: false, typesOnly: true);
            TypeFindPopulationCandidate<int>[] candidates =
            [
                .. surface.Types.Select(static (type, index) =>
                    new TypeFindPopulationCandidate<int>(index, type.FullName)),
            ];
            assets.Add(new(names[i], new(pattern, candidates)));
        }

        return assets;
    }

    public static ScorecardColumn<TypeFindPopulationScorecardAsset, TypeFindPopulationScorecardRow> LinqColumn(
        ScorecardShape shape) =>
        new("LINQ", (closing, asset) => LinqAnswer(shape, closing, asset));

    public static ScorecardColumn<TypeFindPopulationScorecardAsset, TypeFindPopulationScorecardRow> NLinqColumn(
        ScorecardShape shape) =>
        new("NLinq", (closing, asset) => NLinqAnswer(shape, closing, asset));

    public static ScorecardColumn<TypeFindPopulationScorecardAsset, TypeFindPopulationScorecardRow> SelectorColumn(
        ScorecardShape shape) =>
        new("Selector", (closing, asset) => SelectorAnswer(shape, closing, asset));

    public static string RowText(TypeFindPopulationScorecardRow row) =>
        $"{row.Tier}|{row.EffectivePattern}|{row.Association}|{row.FullName}|{row.Similarity:R}";

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ScorecardAnswer<TypeFindPopulationScorecardRow> LinqAnswer(
        ScorecardShape shape,
        ScorecardClosing closing,
        TypeFindPopulationScorecardAsset asset) =>
        Close(shape, closing, SelectWithLinq(asset.Candidates, asset.Pattern));

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ScorecardAnswer<TypeFindPopulationScorecardRow> NLinqAnswer(
        ScorecardShape shape,
        ScorecardClosing closing,
        TypeFindPopulationScorecardAsset asset) =>
        Close(shape, closing, SelectWithNLinq(asset.Candidates, asset.Pattern));

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ScorecardAnswer<TypeFindPopulationScorecardRow> SelectorAnswer(
        ScorecardShape shape,
        ScorecardClosing closing,
        TypeFindPopulationScorecardAsset asset)
    {
        TypeFindPopulationSelection<int>? selection =
            TypeFindPopulationSelector.Select(asset.Pattern, asset.Candidates);
        return Close(shape, closing, Normalize(selection));
    }

    static IReadOnlyList<TypeFindPopulationScorecardRow> SelectWithLinq(
        IReadOnlyList<TypeFindPopulationCandidate<int>> candidates,
        string pattern)
    {
        if (ContainsExplicitWildcard(pattern))
            return [];

        TypeFindPopulationCandidate<int>[] distinct =
        [
            .. candidates.DistinctBy(static candidate => candidate.FullName, StringComparer.Ordinal),
        ];

        TypeFindPopulationSelection<int>? selection = null;
        if (TypeNameMatchRanking.IsBroadenable(pattern))
        {
            selection = SelectRanked(
                distinct.Where(candidate =>
                    TypeNameMatchRanking.Classify(candidate.FullName, pattern)
                    == TypeNameMatchTier.Prefix),
                TypeFindPopulationTier.Prefix,
                LooksLikeNamespacePrefix(pattern) ? pattern + "*" : pattern);
            selection ??= SelectRanked(
                distinct.Where(candidate =>
                    TypeNameMatchRanking.Classify(candidate.FullName, pattern)
                    == TypeNameMatchTier.Substring),
                TypeFindPopulationTier.Substring,
                pattern);
        }

        selection ??= SelectPartial(distinct, pattern);
        return Normalize(selection);
    }

    static IReadOnlyList<TypeFindPopulationScorecardRow> SelectWithNLinq(
        IReadOnlyList<TypeFindPopulationCandidate<int>> candidates,
        string pattern)
    {
        if (ContainsExplicitWildcard(pattern))
            return [];

        var distinctState = new DistinctCandidateState();
        var source = candidates.AsNLinq();
        distinctState = NLinqExtensions.Fold<
            ReadOnlyListEnumerator<TypeFindPopulationCandidate<int>>,
            TypeFindPopulationCandidate<int>,
            DistinctCandidateState,
            AddDistinctCandidate>(
                source,
                distinctState,
                new AddDistinctCandidate());

        TypeFindPopulationSelection<int>? selection = null;
        if (TypeNameMatchRanking.IsBroadenable(pattern))
        {
            selection = SelectRankedNLinq(
                distinctState.Candidates,
                TypeNameMatchTier.Prefix,
                TypeFindPopulationTier.Prefix,
                pattern,
                LooksLikeNamespacePrefix(pattern) ? pattern + "*" : pattern);
            selection ??= SelectRankedNLinq(
                distinctState.Candidates,
                TypeNameMatchTier.Substring,
                TypeFindPopulationTier.Substring,
                pattern,
                pattern);
        }

        selection ??= SelectPartialNLinq(distinctState.Candidates, pattern);
        return Normalize(selection);
    }

    static TypeFindPopulationSelection<int>? SelectRanked(
        IEnumerable<TypeFindPopulationCandidate<int>> candidates,
        TypeFindPopulationTier tier,
        string effectivePattern)
    {
        TypeFindPopulationCandidate<int>[] matches =
        [
            .. candidates.OrderBy(
                static candidate => candidate,
                TypeFindPopulationCandidateComparer.Instance),
        ];
        if (matches.Length == 0)
            return null;

        return new(
            effectivePattern,
            tier,
            [.. matches.Select(candidate => new TypeFindPopulationMatch<int>(
                candidate,
                Similarity: 1.0))]);
    }

    static TypeFindPopulationSelection<int>? SelectRankedNLinq(
        IReadOnlyList<TypeFindPopulationCandidate<int>> candidates,
        TypeNameMatchTier matchTier,
        TypeFindPopulationTier selectionTier,
        string pattern,
        string effectivePattern)
    {
        var source = candidates.AsNLinq();
        var matches = NLinqExtensions.Where<
            ReadOnlyListEnumerator<TypeFindPopulationCandidate<int>>,
            TypeFindPopulationCandidate<int>,
            MatchTier>(
                source,
                new MatchTier(pattern, matchTier));
        List<TypeFindPopulationCandidate<int>> rows = matches.ToList();
        if (rows.Count == 0)
            return null;

        ListEnumerator<TypeFindPopulationCandidate<int>> ordered =
            OracleOperators.OrderBy<
                ListEnumerator<TypeFindPopulationCandidate<int>>,
                TypeFindPopulationCandidate<int>>(
                    rows.AsNLinq(),
                    TypeFindPopulationCandidateComparer.Instance);
        var projected = NLinqExtensions.Select<
            ListEnumerator<TypeFindPopulationCandidate<int>>,
            TypeFindPopulationCandidate<int>,
            TypeFindPopulationMatch<int>,
            CandidateExactMatch>(
                ordered,
                new CandidateExactMatch());
        List<TypeFindPopulationMatch<int>> result = projected.ToList();
        return new(effectivePattern, selectionTier, [.. result]);
    }

    static TypeFindPopulationSelection<int>? SelectPartial(
        IReadOnlyList<TypeFindPopulationCandidate<int>> candidates,
        string pattern)
    {
        List<(string Name, double Similarity)> closest =
        [
            .. TypeMatcher.FindClosest(
                candidates.Select(static candidate => candidate.FullName),
                pattern,
                minSimilarity: 0.5,
                maxResults: 5),
        ];
        return ProjectPartial(candidates, pattern, closest);
    }

    static TypeFindPopulationSelection<int>? SelectPartialNLinq(
        IReadOnlyList<TypeFindPopulationCandidate<int>> candidates,
        string pattern)
    {
        var source = candidates.AsNLinq();
        var names = NLinqExtensions.Select<
            ReadOnlyListEnumerator<TypeFindPopulationCandidate<int>>,
            TypeFindPopulationCandidate<int>,
            string,
            CandidateName>(
                source,
                new CandidateName());
        List<string> nameRows = names.ToList();
        List<(string Name, double Similarity)> closest =
        [
            .. TypeMatcher.FindClosest(
                nameRows,
                pattern,
                minSimilarity: 0.5,
                maxResults: 5),
        ];
        if (closest.Count == 0)
            return null;

        Dictionary<string, double> similarities = Similarities(closest);
        var candidatesSource = candidates.AsNLinq();
        var selected = NLinqExtensions.Where<
            ReadOnlyListEnumerator<TypeFindPopulationCandidate<int>>,
            TypeFindPopulationCandidate<int>,
            HasSimilarity>(
                candidatesSource,
                new HasSimilarity(similarities));
        ListEnumerator<TypeFindPopulationCandidate<int>> ordered =
            OracleOperators.OrderBy<
                Filter<
                    TypeFindPopulationCandidate<int>,
                    ReadOnlyListEnumerator<TypeFindPopulationCandidate<int>>,
                    HasSimilarity>,
                TypeFindPopulationCandidate<int>>(
                    selected,
                    new PartialCandidateComparer(similarities));
        var projected = NLinqExtensions.Select<
            ListEnumerator<TypeFindPopulationCandidate<int>>,
            TypeFindPopulationCandidate<int>,
            TypeFindPopulationMatch<int>,
            CandidatePartialMatch>(
                ordered,
                new CandidatePartialMatch(similarities));
        return new(
            pattern,
            TypeFindPopulationTier.Partial,
            [.. projected.ToList()]);
    }

    static TypeFindPopulationSelection<int>? ProjectPartial(
        IReadOnlyList<TypeFindPopulationCandidate<int>> candidates,
        string pattern,
        IReadOnlyList<(string Name, double Similarity)> closest)
    {
        if (closest.Count == 0)
            return null;

        Dictionary<string, double> similarities = Similarities(closest);
        return new(
            pattern,
            TypeFindPopulationTier.Partial,
            [
                .. candidates
                    .Where(candidate => similarities.ContainsKey(candidate.FullName))
                    .OrderByDescending(candidate => similarities[candidate.FullName])
                    .ThenBy(
                        static candidate => candidate.FullName,
                        WithinTierStringComparer.Instance)
                    .Select(candidate => new TypeFindPopulationMatch<int>(
                        candidate,
                        similarities[candidate.FullName])),
            ]);
    }

    static Dictionary<string, double> Similarities(
        IReadOnlyList<(string Name, double Similarity)> closest) =>
        closest.ToDictionary(
            static match => match.Name,
            static match => match.Similarity,
            StringComparer.Ordinal);

    static IReadOnlyList<TypeFindPopulationScorecardRow> Normalize(
        TypeFindPopulationSelection<int>? selection) =>
        selection is null
            ? []
            :
            [
                .. selection.Matches.Select(match => new TypeFindPopulationScorecardRow(
                    selection.Tier,
                    selection.EffectivePattern,
                    match.Candidate.Association,
                    match.Candidate.FullName,
                    match.Similarity)),
            ];

    static ScorecardAnswer<TypeFindPopulationScorecardRow> Close(
        ScorecardShape shape,
        ScorecardClosing closing,
        IReadOnlyList<TypeFindPopulationScorecardRow> rows) =>
        closing switch
        {
            ScorecardClosing.Exists => ScorecardAnswer<TypeFindPopulationScorecardRow>.OfExists(rows.Count != 0),
            ScorecardClosing.Count => ScorecardAnswer<TypeFindPopulationScorecardRow>.OfCount(rows.Count),
            ScorecardClosing.Head => ScorecardAnswer<TypeFindPopulationScorecardRow>.OfRows([.. rows.Take(shape.N)]),
            ScorecardClosing.Tail => ScorecardAnswer<TypeFindPopulationScorecardRow>.OfRows([.. rows.TakeLast(shape.N)]),
            ScorecardClosing.Rows => ScorecardAnswer<TypeFindPopulationScorecardRow>.OfRows(rows),
            ScorecardClosing.Window => rows.Count >= shape.WindowLast
                ? ScorecardAnswer<TypeFindPopulationScorecardRow>.OfRows(
                    [.. rows.Skip(shape.WindowSkip).Take(shape.WindowTake)])
                : ScorecardAnswer<TypeFindPopulationScorecardRow>.OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };

    static bool ContainsExplicitWildcard(string pattern) =>
        pattern.Contains('*')
        || pattern.Contains('?');

    static bool LooksLikeNamespacePrefix(string pattern) =>
        pattern.Contains('.')
        && !pattern.Contains('<')
        && !pattern.Contains('`');

    sealed class TypeFindPopulationCandidateComparer : IComparer<TypeFindPopulationCandidate<int>>
    {
        public static TypeFindPopulationCandidateComparer Instance { get; } = new();

        public int Compare(
            TypeFindPopulationCandidate<int>? left,
            TypeFindPopulationCandidate<int>? right) =>
            TypeNameMatchRanking.CompareWithinTier(left!.FullName, right!.FullName);
    }

    sealed class WithinTierStringComparer : IComparer<string>
    {
        public static WithinTierStringComparer Instance { get; } = new();

        public int Compare(string? left, string? right) =>
            TypeNameMatchRanking.CompareWithinTier(left!, right!);
    }

    sealed class DistinctCandidateState
    {
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        public List<TypeFindPopulationCandidate<int>> Candidates { get; } = [];
    }

    readonly struct AddDistinctCandidate :
        IFunc<DistinctCandidateState, TypeFindPopulationCandidate<int>, DistinctCandidateState>
    {
        public DistinctCandidateState Invoke(
            DistinctCandidateState state,
            TypeFindPopulationCandidate<int> candidate)
        {
            if (state.Names.Add(candidate.FullName))
                state.Candidates.Add(candidate);
            return state;
        }
    }

    readonly struct MatchTier(string pattern, TypeNameMatchTier tier) :
        IFunc<TypeFindPopulationCandidate<int>, bool>
    {
        public bool Invoke(TypeFindPopulationCandidate<int> candidate) =>
            TypeNameMatchRanking.Classify(candidate.FullName, pattern) == tier;
    }

    readonly struct CandidateExactMatch :
        IFunc<TypeFindPopulationCandidate<int>, TypeFindPopulationMatch<int>>
    {
        public TypeFindPopulationMatch<int> Invoke(TypeFindPopulationCandidate<int> candidate) =>
            new(candidate, Similarity: 1.0);
    }

    readonly struct CandidateName : IFunc<TypeFindPopulationCandidate<int>, string>
    {
        public string Invoke(TypeFindPopulationCandidate<int> candidate) => candidate.FullName;
    }

    readonly struct HasSimilarity(Dictionary<string, double> similarities) :
        IFunc<TypeFindPopulationCandidate<int>, bool>
    {
        public bool Invoke(TypeFindPopulationCandidate<int> candidate) =>
            similarities.ContainsKey(candidate.FullName);
    }

    sealed class PartialCandidateComparer(Dictionary<string, double> similarities) :
        IComparer<TypeFindPopulationCandidate<int>>
    {
        public int Compare(
            TypeFindPopulationCandidate<int>? left,
            TypeFindPopulationCandidate<int>? right)
        {
            int comparison = similarities[right!.FullName].CompareTo(
                similarities[left!.FullName]);
            return comparison != 0
                ? comparison
                : TypeNameMatchRanking.CompareWithinTier(
                    left.FullName,
                    right.FullName);
        }
    }

    readonly struct CandidatePartialMatch(Dictionary<string, double> similarities) :
        IFunc<TypeFindPopulationCandidate<int>, TypeFindPopulationMatch<int>>
    {
        public TypeFindPopulationMatch<int> Invoke(
            TypeFindPopulationCandidate<int> candidate) =>
            new(candidate, similarities[candidate.FullName]);
    }
}
