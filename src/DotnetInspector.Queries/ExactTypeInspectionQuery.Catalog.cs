using System.Collections.Immutable;

using CSharpText;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>One Type prefix and its complete Member remainder.</summary>
public sealed record TypeMemberSelectionCandidate(string TypeName, string MemberSelector);

/// <summary>
/// Selection from an already admitted, detached API catalog. The selected
/// object retains its source association; this adapter performs no acquisition.
/// </summary>
public sealed record ExactTypeCatalogSelection(
    ExactTypeInspectionOutcome Outcome,
    string RequestedType,
    string? MatchedType,
    ApiType? Type,
    string? ImpliedMember,
    ImmutableArray<string> Suggestions,
    ImmutableArray<ApiSurfaceInspectionFailure> Failures);

internal static partial class ExactTypeInspectionQuery
{
    internal static ExactTypeCatalogSelection SelectCatalog(
        ApiSurface catalog,
        string query,
        bool allowImpliedMember,
        ExactTypeSelectionKind selectionKind)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (!Enum.IsDefined(selectionKind))
            throw new ArgumentOutOfRangeException(nameof(selectionKind));

        ExactTypeCatalogSelection first = Select(query);
        if (!allowImpliedMember
            || selectionKind == ExactTypeSelectionKind.DefinitionIdentity
            || first.Outcome != ExactTypeInspectionOutcome.NotFound)
            return first;

        foreach (TypeMemberSelectionCandidate candidate in TypeMemberCandidates(query))
        {
            ExactTypeCatalogSelection prefix = Select(candidate.TypeName);
            if (prefix.Outcome != ExactTypeInspectionOutcome.NotFound)
            {
                return prefix with
                {
                    RequestedType = query,
                    ImpliedMember = prefix.Outcome == ExactTypeInspectionOutcome.Available
                        ? candidate.MemberSelector
                        : null,
                };
            }
        }
        return first;

        ExactTypeCatalogSelection Select(string type)
        {
            ImmutableArray<ApiSurfaceInspectionFailure> failures =
            [
                .. catalog.InspectionFailures.Where(failure =>
                    failure.Operation != ApiSurface.ConstraintResolutionOperation
                        && MayAffectTypeLookup(failure, type, selectionKind)),
            ];
            if (!failures.IsEmpty || catalog.Types.Any(item => item.DefinitionName is null))
                return Result(ExactTypeInspectionOutcome.Unavailable, failures: failures);

            ImmutableArray<ApiType> matches;
            string? matchedName;
            if (selectionKind == ExactTypeSelectionKind.DefinitionIdentity)
            {
                var selection = MetadataTypeLookup.Definition(
                    catalog.Types, static item => item.DefinitionName!, type);
                matches = selection.Matches;
                matchedName = selection.MatchedName;
            }
            else if (TypeMatcher.IsTypeGlobPattern(type))
            {
                // Catalog presentation retains its separately owned pattern
                // behavior. Exact query selection below never uses it.
                matches = [.. catalog.Types.Where(item =>
                    TypeMatcher.MatchesTypeFilter(item.DefinitionName!, type))];
                matchedName = matches.Length == 1
                    ? matches[0].DefinitionName!.ToEscapedFullName()
                    : null;
            }
            else
            {
                var selection = MetadataTypeLookup.Query(
                    catalog.Types, static item => item.DefinitionName!, type);
                matches = selection.Matches;
                matchedName = selection.MatchedName;
            }

            if (matches.Length > 1)
                return Result(ExactTypeInspectionOutcome.Ambiguous,
                    suggestions: [.. matches.Select(item =>
                        item.DefinitionName!.ToEscapedFullName())]);
            if (matches.Length == 1)
                return Result(ExactTypeInspectionOutcome.Available, matches[0], matchedName);

            if (!TypeMatcher.IsTypeGlobPattern(type))
            {
                var forwarders = catalog.TypeForwarders.Where(item => item.DefinitionName is not null);
                var unresolved = selectionKind == ExactTypeSelectionKind.DefinitionIdentity
                    ? MetadataTypeLookup.Definition(forwarders, static item => item.DefinitionName!, type)
                    : MetadataTypeLookup.Query(forwarders, static item => item.DefinitionName!, type);
                if (!unresolved.Matches.IsEmpty)
                    return Result(ExactTypeInspectionOutcome.Unavailable);
            }
            return Result(ExactTypeInspectionOutcome.NotFound,
                suggestions: selectionKind == ExactTypeSelectionKind.Query
                    ? [.. TypeMatcher.Lookup(
                        [.. catalog.Types.Select(item => item.DefinitionName!.ToEscapedFullName())],
                        type).Suggestions]
                    : []);

            ExactTypeCatalogSelection Result(
                ExactTypeInspectionOutcome outcome,
                ApiType? selected = null,
                string? name = null,
                ImmutableArray<string> suggestions = default,
                ImmutableArray<ApiSurfaceInspectionFailure> failures = default) =>
                new(outcome, query, name, selected, null,
                    suggestions.IsDefault ? [] : suggestions,
                    failures.IsDefault ? [] : failures);
        }
    }

    internal static IEnumerable<TypeMemberSelectionCandidate> TypeMemberCandidates(string query)
    {
        int searchEnd = query.Length;
        for (int probes = 0; probes < 64 && searchEnd > 0; probes++)
        {
            int dot = FqnParser.LastTopLevelDot(query[..searchEnd]);
            if (dot <= 0)
                yield break;

            string member = query[(dot + 1)..];
            int typeEnd = dot;
            MemberTargetSelector selector = MemberTargetSelector.Parse(member);
            if (string.IsNullOrWhiteSpace(selector.Name))
                yield break;
            if (dot > 1 && query[dot - 1] == '.')
            {
                if (!(selector.Name.Equals(".ctor", StringComparison.OrdinalIgnoreCase)
                    || selector.Name.Equals("ctor", StringComparison.OrdinalIgnoreCase)
                    || selector.Name.Equals("cctor", StringComparison.OrdinalIgnoreCase)
                    || selector.Name.Equals(".cctor", StringComparison.OrdinalIgnoreCase)))
                    yield break;
                member = $".{member}";
                typeEnd--;
                if (typeEnd > 0 && query[typeEnd - 1] == '.')
                    yield break;
            }
            yield return new(query[..typeEnd], member);
            searchEnd = dot;
        }
    }
}
