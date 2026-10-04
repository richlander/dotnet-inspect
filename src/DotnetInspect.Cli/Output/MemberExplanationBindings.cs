using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal static class MemberExplanationBindings
{
    private const int ResourceLimit = 256;
    private const int RelationshipLimit = 512;

    private static readonly Lazy<RegisteredResource> RegisteredCommand =
        new(() =>
            CreateResource(
                "member",
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.MemberType,
                    InspectionCatalogIdentity.ApiMember)));
    private static readonly Lazy<RegisteredResource> RegisteredMemberGroup =
        new(() =>
            CreateResource(
                "member-overload",
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.MemberTarget,
                    InspectionCatalogIdentity.ApiMemberOverload),
                [SectionNames.Methods]));
    private static readonly Lazy<RegisteredResource> RegisteredExactMember =
        new(() =>
            CreateResource(
                "member-detail",
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.MemberTarget,
                    InspectionCatalogIdentity.ApiMemberDetail)));

    internal static InspectionEnvelope<MemberContextualExplanationDocument>
        ExplainCommand() =>
        MemberContextualExplanationOperation.ExplainCommand(
            RegisteredCommand.Value.Document,
            RegisteredCommand.Value.DefaultSections);

    internal static InspectionEnvelope<MemberContextualExplanationDocument>
        ExplainMemberGroup(
            ResolvedMemberGroupExplanationBasis basis) =>
        MemberContextualExplanationOperation.ExplainMemberGroup(
            RegisteredMemberGroup.Value.Document,
            basis,
            RegisteredMemberGroup.Value.DefaultSections);

    internal static InspectionEnvelope<MemberContextualExplanationDocument>
        ExplainExactMember(
            DotnetInspector.Queries.ResolvedMemberInspectionBasis basis) =>
        MemberContextualExplanationOperation.ExplainExactMember(
            RegisteredExactMember.Value.Document,
            basis,
            RegisteredExactMember.Value.DefaultSections);

    private static RegisteredResource CreateResource(
        string catalogName,
        StructuralRoute route,
        IEnumerable<string>? defaultSections = null)
    {
        StructuralSchemaProjection projection =
            StructuralViewRegistry.Project(route);
        var capabilities = new OutputCapabilityCatalog(
            projection.SelectableSectionNames.ToDictionary(
                section => section,
                _ => SectionOutputCapabilities.Create(
                    OutputCapabilityCatalog.StandardSectionFormats),
                StringComparer.OrdinalIgnoreCase));
        DiscoveryDocumentFactory.Projection structural =
            DiscoveryDocumentFactory.CreateProjection(
                catalogName,
                discover: null,
                projection.Schema,
                projection.SectionCategories,
                projection.CatalogHiddenSections,
                projection.ListedCategoryDoors,
                projection.SectionCostAnnotations,
                projection.ExactOnlySections,
                capabilities,
                sectionCardinalities:
                    projection.SectionCardinalities)
            ?? throw new InvalidOperationException(
                $"Member explanation catalog '{catalogName}' was not created.");
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.CreateStructural(
                structural.Document,
                structural.ResourcePaths);
        ResourcePathResolution resolution =
            catalog.Resolve(catalogName);
        if (resolution is not ResourcePathResolution.Resolved resolved)
        {
            throw new InvalidOperationException(
                $"Member explanation resource '{catalogName}' was not registered.");
        }

        return new(
            catalog.Explain(
                resolved,
                new(
                    0,
                    ResourceLimit,
                    RelationshipLimit)).Content,
            defaultSections is null
                ? [.. projection.DefaultSectionNames]
                : [.. defaultSections]);
    }

    private sealed record RegisteredResource(
        ResourceExplanationDocument Document,
        System.Collections.Immutable.ImmutableArray<string>
            DefaultSections);
}
