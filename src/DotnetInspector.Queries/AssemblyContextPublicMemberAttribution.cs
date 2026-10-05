using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

internal sealed record AssemblyContextPublicMember(
    string TypeDefinitionId,
    string Member,
    string StableSelector);

internal sealed record AssemblyContextPublicMemberInventory(
    IReadOnlyDictionary<int, AssemblyContextPublicMember> ByBodyToken,
    ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures);

internal static class AssemblyContextPublicMemberAttribution
{
    internal static AssemblyContextPublicMemberInventory ProjectPrimary(
        AssemblyInspectionSession session)
    {
        ApiSurface surface =
            session.CompatibilityApiSurface(
                ApiSurfaceExtractionScope.Public);
        var members =
            new Dictionary<int, AssemblyContextPublicMember>();
        foreach (ApiType type in surface.Types)
        {
            foreach (ApiMember member in type.Members)
            {
                ImmutableArray<CallGraphMemberBodySelector> selectors =
                [
                    .. CallGraphMemberResolver.CreateBodySelectors(
                        type,
                        member),
                ];
                if (selectors.Length == 0)
                    continue;

                var publicMember =
                    new AssemblyContextPublicMember(
                        AssemblyContextApiSurfaceQuery
                            .MetadataTypeIdentity(type),
                        member.Name,
                        ApiMemberIdentity
                            .GetMemberAnchor(type, member)
                            .StableSelector);
                foreach (CallGraphMemberBodySelector selector
                    in selectors)
                {
                    members.TryAdd(
                        selector.BodyToken,
                        publicMember);
                }
            }
        }

        return new AssemblyContextPublicMemberInventory(
            members,
            [.. surface.InspectionFailures]);
    }
}
