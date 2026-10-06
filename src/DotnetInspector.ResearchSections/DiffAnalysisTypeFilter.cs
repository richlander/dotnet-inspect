using CSharpText;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using System.Collections.Immutable;

namespace DotnetInspector.ResearchSections;

/// <summary>Matches API type names against Diff's established type-filter semantics.</summary>
public static class DiffAnalysisTypeFilter
{
    /// <summary>Resolves filters to the matching type names across both endpoints.</summary>
    public static ImmutableArray<string> Resolve(
        ApiSurface before,
        ApiSurface after,
        IEnumerable<string> filters)
    {
        string[] available =
        [
            .. EnumerateResolvable(before)
                .Concat(EnumerateResolvable(after))
                .Distinct(StringComparer.Ordinal),
        ];
        return [
            .. filters
                .SelectMany(filter => available
                    .Where(typeName => Matches(typeName, filter))
                    .DefaultIfEmpty(filter))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Returns whether a type name matches any supplied filter.</summary>
    public static bool MatchesAny(
        string typeFullName,
        IEnumerable<string> filters)
    {
        foreach (string filter in filters)
        {
            if (Matches(typeFullName, filter))
                return true;
        }

        return false;
    }

    /// <summary>Enumerates type names that remain targetable despite inspection failures.</summary>
    public static IEnumerable<string> EnumerateResolvable(ApiSurface surface)
    {
        foreach (ApiType type in surface.Types)
            yield return type.FullName;

        foreach (ApiSurfaceInspectionFailure failure
            in surface.InspectionFailures)
        {
            if (failure.OwningTypeDefinition is { } owner)
                yield return owner.ToMetadataFullName();

            if (failure.AffectedTypeDefinitions.IsDefaultOrEmpty)
                continue;

            foreach (MetadataTypeDefinitionName affected in
                failure.AffectedTypeDefinitions)
            {
                yield return affected.ToMetadataFullName();
            }
        }
    }

    /// <summary>Returns whether a type name matches one exact, wildcard, or namespace filter.</summary>
    public static bool Matches(string typeFullName, string filter)
    {
        if (TypeMatcher.MatchesTypeFilter(typeFullName, filter))
            return true;

        if (filter.Contains('*') || filter.Contains('?'))
            return false;

        string normalizedFilter = FqnParser.NormalizeTypeName(filter);
        return typeFullName.StartsWith(
                normalizedFilter + ".",
                StringComparison.OrdinalIgnoreCase)
            || typeFullName.Contains(
                "." + normalizedFilter + ".",
                StringComparison.OrdinalIgnoreCase);
    }
}
