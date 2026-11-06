using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// The navigation identity of one Type on the Library's complete API surface,
/// with the Type a host projects when its ordinary Type list does not carry it.
/// </summary>
public sealed record UnsafeCodeTypeNavigation(string Type, ApiType DeclaringType);

/// <summary>
/// One participant's Finding Unsafe Any answer, with each Type's navigation
/// identity on the all-accessibility API surface, keyed by TypeDef token.
/// </summary>
public sealed record AssemblyUnsafeCodeTypesResult(
    FindingUnsafeAnyQueryResult Answer,
    ImmutableDictionary<int, UnsafeCodeTypeNavigation> Types,
    ImmutableArray<ApiSurfaceInspectionFailure> ApiSurfaceInspectionFailures);

/// <summary>
/// Runs Finding Unsafe Any over one admitted implementation participant, the
/// Library scope of the Unsafe view, as
/// docs/design/inspect-web-surface-composition.md#unsafe defines. The answer
/// owns admission and settlement; this query adds the navigation identities
/// the Browser rows link through.
/// </summary>
public static class AssemblyContextFindingUnsafeAnyQuery
{
    public static AssemblyContextEntry<AssemblyUnsafeCodeTypesResult> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        string sourceName = participant.Assembly.Identity.Name;
        return AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            session =>
            {
                ApiSurface surface = session.CompatibilityApiSurface(
                    ApiSurfaceExtractionScope.IncludeAll);
                var types = ImmutableDictionary.CreateBuilder<int, UnsafeCodeTypeNavigation>();
                foreach (ApiType type in surface.Types)
                {
                    if (type.MetadataToken is { } token)
                    {
                        types.TryAdd(token, new UnsafeCodeTypeNavigation(
                            AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type),
                            type));
                    }
                }
                return new AssemblyUnsafeCodeTypesResult(
                    FindingUnsafeAnyQuery.Execute(session, sourceName),
                    types.ToImmutable(),
                    [.. surface.InspectionFailures]);
            });
    }
}
