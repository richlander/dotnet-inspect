using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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
/// The scope one unsafe member census request answers: the whole Library, one
/// Type, or one declared member of a Type. Type and member are named by the
/// API surface's exact Type identity and the member's stable selector.
/// </summary>
public abstract record UnsafeMemberCensusScope
{
    UnsafeMemberCensusScope()
    {
    }

    public static UnsafeMemberCensusScope Library { get; } = new LibraryScope();

    public sealed record LibraryScope : UnsafeMemberCensusScope;

    public sealed record TypeScope(string TypeIdentity) : UnsafeMemberCensusScope;

    public sealed record MemberScope(string TypeIdentity, string StableSelector)
        : UnsafeMemberCensusScope;
}

/// <summary>
/// Runs the unsafe member census over one admitted implementation participant
/// at the requested scope, as
/// docs/design/inspect-web-surface-composition.md#unsafe defines for the
/// Unsafe view. The census owns admission and attribution; this query resolves
/// a Type or member scope through the API surface and adds the navigation
/// identities the Browser rows link through.
/// </summary>
public static class AssemblyContextUnsafeMemberCensusQuery
{
    public static AssemblyContextEntry<AssemblyUnsafeMemberCensusResult> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant) =>
        ExecuteParticipant(group, participant, UnsafeMemberCensusScope.Library);

    public static AssemblyContextEntry<AssemblyUnsafeMemberCensusResult> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        UnsafeMemberCensusScope scope)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(scope);
        string sourceName = participant.Assembly.Identity.Name;
        return AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            session =>
            {
                ApiSurface surface = session.CompatibilityApiSurface(
                    ApiSurfaceExtractionScope.IncludeAll);
                (UnsafeMemberCensusQueryResult census, IEnumerable<ApiType> types) = scope switch
                {
                    UnsafeMemberCensusScope.LibraryScope =>
                        (UnsafeMemberCensusQuery.Execute(session, sourceName), surface.Types),
                    UnsafeMemberCensusScope.TypeScope type => ExecuteType(
                        session, sourceName, ApiSurfaceMemberSelection.SelectType(surface, type.TypeIdentity)),
                    UnsafeMemberCensusScope.MemberScope member => ExecuteMember(
                        session, sourceName, ApiSurfaceMemberSelection.SelectType(surface, member.TypeIdentity),
                        member.StableSelector),
                    _ => throw new InvalidOperationException("Unknown unsafe member census scope."),
                };
                return new AssemblyUnsafeMemberCensusResult(
                    census,
                    Navigation(types),
                    [.. surface.InspectionFailures]);
            });
    }

    static (UnsafeMemberCensusQueryResult, IEnumerable<ApiType>) ExecuteType(
        AssemblyInspectionSession session,
        string sourceName,
        ApiType type) =>
        (UnsafeMemberCensusQuery.ExecuteExactTypes(session, sourceName, TypeHandle(type)), [type]);

    // Every definition that anchors to the member, accessors included, is the
    // Member scope's breadth.
    static (UnsafeMemberCensusQueryResult, IEnumerable<ApiType>) ExecuteMember(
        AssemblyInspectionSession session,
        string sourceName,
        ApiType type,
        string stableSelector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableSelector);
        MethodDefinitionHandle[] methods =
        [
            .. type.Members
                .Where(member => ApiMemberIdentity.GetMemberAnchor(type, member).StableSelector == stableSelector)
                .SelectMany(member => CallGraphMemberResolver.CreateBodySelectors(type, member))
                .Select(static selector => MetadataTokens.MethodDefinitionHandle(selector.BodyToken))
                .Distinct(),
        ];
        if (methods.Length == 0)
        {
            throw new InvalidOperationException(
                $"Type '{AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type)}' has no member definitions "
                    + $"for stable selector '{stableSelector}'.");
        }
        return (UnsafeMemberCensusQuery.ExecuteExactMethods(session, sourceName, methods), [type]);
    }

    static TypeDefinitionHandle TypeHandle(ApiType type) =>
        type.MetadataToken is { } token
            ? MetadataTokens.TypeDefinitionHandle(token)
            : throw new InvalidOperationException(
                $"Type '{type.Name}' has no definition token for a Type-scope census.");

    // Every body a member owns, accessors included, resolves to its anchor.
    static ImmutableDictionary<int, UnsafeMemberNavigation> Navigation(IEnumerable<ApiType> types)
    {
        var members = ImmutableDictionary.CreateBuilder<int, UnsafeMemberNavigation>();
        foreach (ApiType type in types)
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
