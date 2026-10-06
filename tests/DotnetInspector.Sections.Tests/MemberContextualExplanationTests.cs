using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Sections.Tests;

public class MemberContextualExplanationTests
{
    [Fact]
    public void CommandExplanation_UsesRegisteredResourceAndDefaults()
    {
        ResourceExplanationDocument resource =
            ResourceDocument("member");

        InspectionEnvelope<MemberContextualExplanationDocument>
            explanation =
                MemberContextualExplanationOperation.ExplainCommand(
                    resource,
                    ["Values", "Fields"]);

        Assert.Equal(
            MemberContextualExplanationKind.Command,
            explanation.Content.Kind);
        Assert.Same(resource, explanation.Content.Resource);
        Assert.Null(explanation.Content.Subject);
        Assert.Null(explanation.Content.DefaultFacet);
        Assert.Equal(
            ["Values", "Fields"],
            explanation.Content.SelectedSections);
        Assert.Empty(explanation.Content.RelatedOperations);
    }

    [Fact]
    public void Document_RejectsKindAndSubjectMismatch()
    {
        ResourceExplanationDocument resource =
            ResourceDocument("member-detail");
        ExactMemberContextualExplanationSubject exact =
            Assert.IsType<ExactMemberContextualExplanationSubject>(
                MemberContextualExplanationOperation
                    .ExplainExactMember(
                        resource,
                        ExactBasis([]),
                        ["Signature"])
                    .Content
                    .Subject);

        Assert.Throws<ArgumentException>(() =>
            new MemberContextualExplanationDocument(
                MemberContextualExplanationKind.Command,
                resource,
                exact,
                defaultFacet: null,
                selectedSections: [],
                relatedOperations: []));
        Assert.Throws<ArgumentException>(() =>
            new MemberContextualExplanationDocument(
                MemberContextualExplanationKind.MemberGroup,
                resource,
                subject: null,
                defaultFacet: null,
                selectedSections: [],
                relatedOperations: []));
    }

    [Fact]
    public void MemberGroupExplanation_PreservesOwnerIssuedGroup()
    {
        ResourceExplanationDocument resource =
            ResourceDocument("member-overload");
        var group = new MemberGroupSubject(
            TypeName(),
            "Serialize");
        var basis = new ResolvedMemberGroupExplanationBasis(
            Source(),
            group,
            MemberDefaultFacet(),
            new(["Overloads"], ["Overloads"]));

        MemberContextualExplanationDocument content =
            MemberContextualExplanationOperation
                .ExplainMemberGroup(
                    resource,
                    basis,
                    ["Methods"])
                .Content;

        Assert.Equal(
            MemberContextualExplanationKind.MemberGroup,
            content.Kind);
        Assert.Same(resource, content.Resource);
        Assert.Equal(["Overloads"], content.SelectedSections);
        MemberGroupContextualExplanationSubject subject =
            Assert.IsType<MemberGroupContextualExplanationSubject>(
                content.Subject);
        Assert.Same(group, subject.Group);
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            subject.TypeName);
        Assert.Equal("System.Text.Json", subject.Library);
        Assert.Equal(
            "System.Text.Json@10.0.0",
            subject.Package);
        Assert.Equal("net10.0", subject.Framework);
        Assert.Equal(
            MemberRelatedOperationAffordances.All
                .Select(static operation => operation.Id),
            content.RelatedOperations
                .Select(static operation => operation.Id));
    }

    [Fact]
    public void MemberGroupExplanation_UsesRegisteredDefaultsForBareDemand()
    {
        var group = new MemberGroupSubject(
            TypeName(),
            "Serialize");
        MemberContextualExplanationDocument content =
            MemberContextualExplanationOperation
                .ExplainMemberGroup(
                    ResourceDocument("member-overload"),
                    new(
                        Source(),
                        group,
                        MemberDefaultFacet(),
                        new([], [])),
                    ["Overloads"])
                .Content;

        Assert.Equal(["Overloads"], content.SelectedSections);
    }

    [Fact]
    public void ExactExplanation_PreservesResolvedBasisInMemory()
    {
        ResourceExplanationDocument resource =
            ResourceDocument("member-detail");
        ResolvedMemberInspectionBasis basis =
            ExactBasis(["Signature"]);

        InspectionEnvelope<MemberContextualExplanationDocument>
            explanation =
                MemberContextualExplanationOperation
                    .ExplainExactMember(
                        resource,
                        basis,
                        ["Documentation"]);

        MemberContextualExplanationDocument content =
            explanation.Content;
        Assert.Equal(
            MemberContextualExplanationKind.ExactMember,
            content.Kind);
        Assert.Same(resource, content.Resource);
        Assert.Equal(["Signature"], content.SelectedSections);
        ExactMemberContextualExplanationSubject subject =
            Assert.IsType<ExactMemberContextualExplanationSubject>(
                content.Subject);
        Assert.Equal(
            basis.Target.Member.CanonicalSignature,
            subject.CanonicalSignature);
        Assert.Equal(
            "System.Text.Json@10.0.0",
            subject.Package);
        Assert.Equal("net10.0", subject.Framework);
        Assert.Equal(
            "member.overview",
            content.DefaultFacet?.Value);
        Assert.Equal(
            MemberRelatedOperationAffordances.All
                .Select(static operation => operation.Id),
            content.RelatedOperations
                .Select(static operation => operation.Id));
    }

    [Fact]
    public void ExactExplanation_UsesRegisteredDefaultsForBareDemand()
    {
        MemberContextualExplanationDocument content =
            MemberContextualExplanationOperation
                .ExplainExactMember(
                    ResourceDocument("member-detail"),
                    ExactBasis([]),
                    ["Signature"])
                .Content;

        Assert.Equal(["Signature"], content.SelectedSections);
    }

    private static ResourceExplanationDocument ResourceDocument(
        string path)
    {
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.CreateStructural(
                new DiscoveryDocument(
                    path,
                    [],
                    [],
                    new DiscoverySelection(
                        isCatalog: true,
                        addressedResources: [],
                        rows: [])),
                []);
        ResourcePathResolution.Resolved resolved =
            Assert.IsType<ResourcePathResolution.Resolved>(
                catalog.Resolve(path));
        return catalog.Explain(
            resolved,
            new(
                depth: 0,
                resourceLimit: 1,
                relationshipLimit: 1)).Content;
    }

    private static ResolvedMemberInspectionBasis ExactBasis(
        string[] sections)
    {
        var anchor = new MemberAnchor(
            "Serialize~1dc14dd1fb",
            "M:System.Text.Json.JsonSerializer.Serialize``1("
                + "``0,System.Text.Json.JsonSerializerOptions)",
            "1dc14dd1fb",
            "System.Text.Json.JsonSerializer",
            "Serialize");
        return new(
            Source(),
            new ResolvedInspectionMemberTarget(
                "System.Text.Json.JsonSerializer",
                null,
                anchor),
            MemberDefaultFacet(),
            new InspectionCatalogReference(
                "ApiMemberDetail",
                version: 1),
            new InspectionSemanticDemand(
                sections,
                sections),
            new InspectionCapabilityRequestProvenance(
                InspectionRequestVerbosity.Normal,
                sections,
                InspectionDiscoveryRequest.None));
    }

    private static ResolvedInspectionSource Source() =>
        new(
            AssemblyResolutionProvenance.Package(
                "System.Text.Json",
                "10.0.0",
                "net10.0",
                rid: null),
            null,
            "System.Text.Json",
            "net10.0");

    private static ViewFacetId MemberDefaultFacet() =>
        InspectionViewFacetCatalog.Registry
            .GetRequiredDescriptor(
                StructuralSubjectKind.Member,
                ViewFacetRole.MemberOverview)
            .Id;

    private static MetadataTypeDefinitionName TypeName() =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.ParseSerialized(
                "System.Text.Json.JsonSerializer"))
            .Name;
}
