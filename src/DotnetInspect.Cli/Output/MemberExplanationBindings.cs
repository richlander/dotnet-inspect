using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal static class MemberExplanationBindings
{
    private static readonly Lazy<
        System.Collections.Immutable.ImmutableArray<string>>
        CommandDefaults =
        new(() =>
            DefaultSections(
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.MemberType,
                    InspectionCatalogIdentity.ApiMember)));
    private static readonly Lazy<
        System.Collections.Immutable.ImmutableArray<string>>
        MemberGroupDefaults =
        new(() =>
            DefaultSections(
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.MemberTarget,
                    InspectionCatalogIdentity.ApiMemberOverload),
                [SectionNames.Methods]));
    private static readonly Lazy<
        System.Collections.Immutable.ImmutableArray<string>>
        ExactMemberDefaults =
        new(() =>
            DefaultSections(
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.MemberTarget,
                    InspectionCatalogIdentity.ApiMemberDetail)));

    internal static InspectionEnvelope<ResourceExplanationDocument>
        ExplainCommand() =>
        MemberContextualExplanationOperation.ExplainCommand(
            CommandDefaults.Value);

    internal static InspectionEnvelope<ResourceExplanationDocument>
        ExplainMemberGroup(
            ResolvedMemberGroupExplanationBasis basis) =>
        MemberContextualExplanationOperation.ExplainMemberGroup(
            basis,
            MemberGroupDefaults.Value);

    internal static InspectionEnvelope<ResourceExplanationDocument>
        ExplainExactMember(
            DotnetInspector.Queries.ResolvedMemberInspectionBasis basis) =>
        MemberContextualExplanationOperation.ExplainExactMember(
            basis,
            ExactMemberDefaults.Value);

    private static System.Collections.Immutable.ImmutableArray<string>
        DefaultSections(
        StructuralRoute route,
        IEnumerable<string>? defaultSections = null)
    {
        StructuralSchemaProjection projection =
            StructuralViewRegistry.Project(route);
        return defaultSections is null
                ? [.. projection.DefaultSectionNames]
                : [.. defaultSections];
    }
}
