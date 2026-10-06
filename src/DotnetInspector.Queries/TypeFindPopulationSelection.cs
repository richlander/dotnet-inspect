using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// A broadened Type Find tier selected over one complete ordered population.
/// </summary>
public enum TypeFindPopulationTier
{
    Prefix,
    Substring,
    Partial,
}

/// <summary>
/// One immutable Type-name fact with its caller-owned exact association.
/// </summary>
public sealed record TypeFindPopulationCandidate<TAssociation>(
    TAssociation Association,
    string FullName);

/// <summary>
/// One selected candidate and its match quality.
/// </summary>
public sealed record TypeFindPopulationMatch<TAssociation>(
    TypeFindPopulationCandidate<TAssociation> Candidate,
    double Similarity);

/// <summary>
/// The first nonempty broadened tier for one pattern.
/// </summary>
public sealed record TypeFindPopulationSelection<TAssociation>(
    string EffectivePattern,
    TypeFindPopulationTier Tier,
    ImmutableArray<TypeFindPopulationMatch<TAssociation>> Matches);

/// <summary>
/// Selects the first nonempty Prefix, Substring, or Partial tier over an
/// already-authorized ordered Type population.
/// </summary>
public static class TypeFindPopulationSelector
{
    private static readonly Comparer<string> WithinTierOrder =
        Comparer<string>.Create(TypeNameMatchRanking.CompareWithinTier);

    public static TypeFindPopulationSelection<TAssociation>? Select<TAssociation>(
        string pattern,
        IReadOnlyList<TypeFindPopulationCandidate<TAssociation>> candidates,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(candidates);

        if (pattern.Contains('*') || pattern.Contains('?'))
            return null;

        TypeFindPopulationCandidate<TAssociation>[] distinct =
        [
            .. candidates.DistinctBy(
                static candidate => candidate.FullName,
                StringComparer.Ordinal),
        ];
        if (TypeNameMatchRanking.IsBroadenable(pattern))
        {
            List<TypeFindPopulationCandidate<TAssociation>> prefix = [];
            List<TypeFindPopulationCandidate<TAssociation>> substring = [];
            foreach (TypeFindPopulationCandidate<TAssociation> candidate
                in distinct)
            {
                switch (TypeNameMatchRanking.Classify(
                    candidate.FullName,
                    pattern))
                {
                    case TypeNameMatchTier.Prefix:
                        prefix.Add(candidate);
                        break;
                    case TypeNameMatchTier.Substring:
                        substring.Add(candidate);
                        break;
                }
            }

            if (prefix.Count > 0)
            {
                return Successful(
                    LooksLikeNamespacePrefix(pattern)
                        ? $"{pattern}*"
                        : pattern,
                    TypeFindPopulationTier.Prefix,
                    prefix,
                    limit);
            }

            if (substring.Count > 0)
            {
                return Successful(
                    pattern,
                    TypeFindPopulationTier.Substring,
                    substring,
                    limit);
            }
        }

        List<(string Name, double Similarity)> suggestions =
            TypeMatcher.FindClosest(
                    distinct.Select(
                        static candidate => candidate.FullName),
                    pattern,
                    minSimilarity: 0.5,
                    maxResults: 5)
                .ToList();
        if (suggestions.Count == 0)
            return null;

        Dictionary<string, double> similarities =
            suggestions.ToDictionary(
                static suggestion => suggestion.Name,
                static suggestion => suggestion.Similarity,
                StringComparer.Ordinal);
        ImmutableArray<TypeFindPopulationMatch<TAssociation>> partial =
        [
            .. distinct
                .Where(candidate =>
                    similarities.ContainsKey(candidate.FullName))
                .OrderByDescending(candidate =>
                    similarities[candidate.FullName])
                .ThenBy(
                    static candidate => candidate.FullName,
                    WithinTierOrder)
                .Select(candidate =>
                    new TypeFindPopulationMatch<TAssociation>(
                        candidate,
                        similarities[candidate.FullName])),
        ];
        return new(
            pattern,
            TypeFindPopulationTier.Partial,
            partial);
    }

    private static TypeFindPopulationSelection<TAssociation> Successful<TAssociation>(
        string effectivePattern,
        TypeFindPopulationTier tier,
        IEnumerable<TypeFindPopulationCandidate<TAssociation>> candidates,
        int? limit)
    {
        IEnumerable<TypeFindPopulationCandidate<TAssociation>> selected =
            candidates.OrderBy(
                static candidate => candidate.FullName,
                WithinTierOrder);
        if (limit is { } count)
            selected = selected.Take(count);

        return new(
            effectivePattern,
            tier,
            [
                .. selected.Select(candidate =>
                    new TypeFindPopulationMatch<TAssociation>(
                        candidate,
                        Similarity: 1.0)),
            ]);
    }

    private static bool LooksLikeNamespacePrefix(string pattern) =>
        pattern.Contains('.')
        && !pattern.Contains('<')
        && !pattern.Contains('`');
}
