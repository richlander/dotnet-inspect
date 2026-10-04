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
        Assert.Equal(
            ["Values", "Fields"],
            explanation.Content.SelectedSections);
        Assert.Empty(explanation.Content.RelatedOperations);
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
                    .ExplainExactSubject(
                        resource,
                        basis,
                        ["Documentation"]);

        MemberContextualExplanationDocument content =
            explanation.Content;
        Assert.Equal(
            MemberContextualExplanationKind.ExactSubject,
            content.Kind);
        Assert.Same(resource, content.Resource);
        Assert.Equal(["Signature"], content.SelectedSections);
        MemberContextualExplanationSubject subject =
            Assert.IsType<MemberContextualExplanationSubject>(
                content.Subject);
        Assert.Equal(
            basis.Target.Member.CanonicalSignature,
            subject.CanonicalSignature);
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
    public void ExactExplanation_UsesRegisteredDefaultsForBareDemand()
    {
        MemberContextualExplanationDocument content =
            MemberContextualExplanationOperation
                .ExplainExactSubject(
                    ResourceDocument("member-detail"),
                    ExactBasis([]),
                    ["Signature"])
                .Content;

        Assert.Equal(["Signature"], content.SelectedSections);
    }

    private static ResourceExplanationDocument ResourceDocument(
        string path)
    {
        var resourcePath = new ResourcePath(path);
        var resource = new ResourceExplanationResource(
            resourcePath,
            new ResourceExplanationIdentity.Catalog(path),
            ResourceExplanationResourceKind.Catalog,
            new ResourceExplanationDetail.CatalogDetails(
                path,
                entryCount: 0));
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.Create([resource], []);
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
            new ResolvedInspectionSource(
                AssemblyResolutionProvenance.Package(
                    "System.Text.Json",
                    "10.0.0",
                    "net10.0",
                    rid: null),
                null,
                "System.Text.Json",
                "net10.0"),
            new ResolvedInspectionMemberTarget(
                "System.Text.Json.JsonSerializer",
                null,
                anchor),
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
}
