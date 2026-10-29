using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// The navigation identity of one declared member: its type, member name,
/// stable selector, and accessibility on the Library's complete API surface,
/// with the declaring type a host projects when its ordinary type list does
/// not carry it.
/// </summary>
public sealed record UnsafeMemberNavigation(
    string Type,
    string Member,
    string StableSelector,
    string? Accessibility,
    ApiType DeclaringType);

/// <summary>
/// One participant's unsafe member census, with each declared member's
/// navigation identity on the all-accessibility API surface.
/// </summary>
public sealed record AssemblyUnsafeMemberCensusResult(
    UnsafeMemberCensusQueryResult Census,
    ImmutableDictionary<int, UnsafeMemberNavigation> Members,
    ImmutableArray<ApiSurfaceInspectionFailure> ApiSurfaceInspectionFailures);

/// <summary>
/// Runs the unsafe member census over one admitted implementation participant,
/// as docs/design/inspect-web-surface-composition.md#unsafe defines
/// for the Unsafe tab. The census owns admission and attribution; this query
/// only adds the navigation identities the Browser rows link through.
/// </summary>
public static class AssemblyContextUnsafeMemberCensusQuery
{
    public static AssemblyContextEntry<AssemblyUnsafeMemberCensusResult> ExecuteParticipant(
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
                UnsafeMemberCensusQueryResult census =
                    UnsafeMemberCensusQuery.Execute(session, sourceName);
                ApiSurface surface = session.CompatibilityApiSurface(
                    ApiSurfaceExtractionScope.IncludeAll);
                return new AssemblyUnsafeMemberCensusResult(
                    census,
                    Navigation(surface),
                    [.. surface.InspectionFailures]);
            });
    }

    // Every body a member owns, accessors included, resolves to its anchor.
    static ImmutableDictionary<int, UnsafeMemberNavigation> Navigation(ApiSurface surface)
    {
        var members = ImmutableDictionary.CreateBuilder<int, UnsafeMemberNavigation>();
        foreach (ApiType type in surface.Types)
        {
            foreach (ApiMember member in type.Members)
            {
                var navigation = new UnsafeMemberNavigation(
                    AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type),
                    member.Name,
                    ApiMemberIdentity.GetMemberAnchor(type, member).StableSelector,
                    member.Accessibility,
                    type);
                foreach (var selector in CallGraphMemberResolver.CreateBodySelectors(type, member))
                    members.TryAdd(selector.BodyToken, navigation);
            }
        }
        return members.ToImmutable();
    }
}
