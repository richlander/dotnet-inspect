using DotnetInspect.Cli.Planning;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal static class MemberExplanationBindings
{
    private const int ResourceLimit = 256;
    private const int RelationshipLimit = 512;

    private static readonly Lazy<Registration> Registered =
        new(CreateRegistration);

    internal static InspectionEnvelope<MemberContextualExplanationDocument>
        ExplainCommand() =>
        MemberContextualExplanationOperation.ExplainCommand(
            Registered.Value.Command.Document,
            Registered.Value.Command.DefaultSections);

    internal static InspectionEnvelope<MemberContextualExplanationDocument>
        ExplainExactSubject(
            DotnetInspector.Queries.ResolvedMemberInspectionBasis basis) =>
        MemberContextualExplanationOperation.ExplainExactSubject(
            Registered.Value.ExactSubject.Document,
            basis,
            Registered.Value.ExactSubject.DefaultSections);

    private static Registration CreateRegistration() =>
        new(
            CreateResource(
                "member",
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.MemberType,
                    InspectionCatalogIdentity.ApiMember)),
            CreateResource(
                "member-detail",
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.MemberTarget,
                    InspectionCatalogIdentity.ApiMemberDetail)));

    private static RegisteredResource CreateResource(
        string catalogName,
        StructuralRoute route)
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
            [.. projection.DefaultSectionNames]);
    }

    private sealed record Registration(
        RegisteredResource Command,
        RegisteredResource ExactSubject);

    private sealed record RegisteredResource(
        ResourceExplanationDocument Document,
        System.Collections.Immutable.ImmutableArray<string>
            DefaultSections);
}
