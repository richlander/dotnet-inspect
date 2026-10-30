using System.Collections.Immutable;

namespace ILInspector.Metadata;

/// <summary>
/// Matching declarations in the winning name tier, with their caller-issued
/// associations intact. A match is not yet a terminal Type resolution.
/// </summary>
public sealed record MetadataTypeLookupResult<TCandidate>(
    ImmutableArray<TCandidate> Matches,
    string? MatchedName);

/// <summary>
/// Selects Type names from an already admitted declaration population.
/// Acquisition, admission, completeness, and terminal forwarding resolution
/// remain with the caller. See the shared Type lookup contract in
/// docs/design/assembly-inspection-query.md.
/// </summary>
public static class MetadataTypeLookup
{
    /// <summary>
    /// Selects the full-name tier, then the arity-preserving short/suffix tier,
    /// then the generic-name tier. Every candidate in the winning tier survives.
    /// </summary>
    public static MetadataTypeLookupResult<TCandidate> Query<TCandidate>(
        IEnumerable<TCandidate> candidates,
        Func<TCandidate, MetadataTypeDefinitionName> definition,
        string query)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (TypeMatcher.IsTypeGlobPattern(query))
        {
            throw new ArgumentException(
                "Exact Type lookup does not accept a Type glob.",
                nameof(query));
        }

        var matches = ImmutableArray.CreateBuilder<TCandidate>();
        int winningTier = 0;
        string? firstName = null;
        bool singleName = true;
        foreach (TCandidate candidate in candidates)
        {
            string name = definition(candidate).ToEscapedFullName();
            int tier = name.Equals(query, StringComparison.OrdinalIgnoreCase)
                ? 3
                : winningTier <= 2 && TypeMatcher.MatchesExactTypeName(name, query)
                    ? 2
                    : winningTier <= 1 && TypeMatcher.MatchesTypeFilter(name, query)
                        ? 1
                        : 0;
            if (tier == 0 || tier < winningTier)
                continue;
            if (tier > winningTier)
            {
                matches.Clear();
                winningTier = tier;
                firstName = name;
                singleName = true;
            }
            else if (!name.Equals(firstName, StringComparison.OrdinalIgnoreCase))
            {
                singleName = false;
            }
            matches.Add(candidate);
        }

        return new(matches.ToImmutable(), singleName ? firstName : null);
    }

    /// <summary>
    /// Selects an issued definition identity with ordinal equality of its
    /// escaped serialization. Query normalization is never applied.
    /// </summary>
    public static MetadataTypeLookupResult<TCandidate> Definition<TCandidate>(
        IEnumerable<TCandidate> candidates,
        Func<TCandidate, MetadataTypeDefinitionName> definition,
        string definitionId)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ImmutableArray<TCandidate> matches =
        [
            .. candidates.Where(candidate =>
                definition(candidate).ToEscapedFullName().Equals(
                    definitionId,
                    StringComparison.Ordinal)),
        ];
        return new(matches, matches.IsEmpty ? null : definitionId);
    }

    /// <summary>Selects an issued structured definition without spelling it for matching.</summary>
    public static MetadataTypeLookupResult<TCandidate> Definition<TCandidate>(
        IEnumerable<TCandidate> candidates,
        Func<TCandidate, MetadataTypeDefinitionName> definition,
        MetadataTypeDefinitionName selected)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(selected);
        ImmutableArray<TCandidate> matches =
        [
            .. candidates.Where(candidate =>
                definition(candidate).Equals(selected)),
        ];
        return new(matches, matches.IsEmpty ? null : selected.ToEscapedFullName());
    }
}
