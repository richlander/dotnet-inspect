using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections.Tests;

public class MemberContextualExplanationTests
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    [Fact]
    public void CommandExplanation_IsOneInstalledCommonDocument()
    {
        ResourceExplanationDocument document =
            MemberContextualExplanationOperation
                .ExplainCommand(["Values", "Fields"])
                .Content;

        Assert.Equal("member", document.RequestedPath?.Value);
        ResourceExplanationResource root =
            Assert.Single(document.Resources);
        Assert.Equal("member", root.Path?.Value);
        Assert.Equal(document.Root, root.Key);
        Assert.Equal("member", root.Owner.Value);
        Assert.Equal("command", root.ResourceType.Value);
        Assert.Equal(ExplanationSnapshotScope.Installed, root.Scope);
        Assert.Equal("Member command", Text(root, "context"));
        Assert.Equal(
            ["Values", "Fields"],
            Texts(root, "selected-content"));
        Assert.Empty(document.Relationships);
    }

    [Fact]
    public void MemberGroupExplanation_ProjectsTypedContextAndOperations()
    {
        var group = new MemberGroupSubject(
            TypeName(),
            "Serialize");
        var basis = new ResolvedMemberGroupExplanationBasis(
            Source(),
            group,
            MemberDefaultFacet(),
            new(["Overloads"], ["Overloads"]));

        ResourceExplanationDocument document =
            MemberContextualExplanationOperation
                .ExplainMemberGroup(
                    basis,
                    ["Methods"])
                .Content;

        ResourceExplanationResource root = document.Resources[0];
        Assert.Null(document.RequestedPath);
        Assert.All(document.Resources, resource =>
            Assert.Null(resource.Path));
        Assert.Equal("member-group", root.ResourceType.Value);
        Assert.Equal("MemberGroup", Text(root, "context"));
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            Text(root, "type-name"));
        Assert.Equal("System.Text.Json", Text(root, "library"));
        Assert.Equal(
            "System.Text.Json@10.0.0",
            Text(root, "package"));
        Assert.Equal("net10.0", Text(root, "framework"));
        Assert.Equal(
            MemberDefaultFacet().Value,
            Text(root, "default-view"));
        Assert.Equal(["Overloads"], Texts(root, "selected-content"));
        Assert.Equal(5, document.Resources.Length);
        ResourceExplanationRelationship relationship =
            Assert.Single(document.Relationships);
        Assert.Equal(
            "related-operation",
            relationship.Relationship.Value);
        Assert.Equal(
            MemberRelatedOperationAffordances.All
                .Select(static operation => operation.Id.Value),
            relationship.Targets.Select(target =>
                Assert.IsType<ExplanationValue.Scalar>(
                    target.Resource.IdentityValue).Value.Text));
    }

    [Fact]
    public void ExactExplanation_ProjectsResolvedIdentityAndDefaults()
    {
        ResolvedMemberInspectionBasis basis = ExactBasis([]);

        ResourceExplanationDocument document =
            MemberContextualExplanationOperation
                .ExplainExactMember(
                    basis,
                    ["Signature"])
                .Content;

        ResourceExplanationResource root = document.Resources[0];
        Assert.Equal("exact-member", root.ResourceType.Value);
        Assert.Equal("Exact Member", Text(root, "context"));
        Assert.Equal(
            basis.Target.Member.StableSelector,
            Text(root, "stable-selector"));
        Assert.Equal(
            basis.Target.Member.CanonicalSignature,
            Text(root, "canonical-signature"));
        Assert.Equal(
            basis.Target.Member.Fingerprint,
            Text(root, "fingerprint"));
        Assert.Equal(["Signature"], Texts(root, "selected-content"));
        Assert.Equal(
            MemberRelatedOperationAffordances.All
                .Select(static operation => operation.Id.Value),
            document.Resources.Skip(1)
                .Select(resource => Text(resource, "identity")));
    }

    [Fact]
    public void PathlessDocument_RoundTripsWithoutSyntheticPaths()
    {
        ResourceExplanationDocument document =
            MemberContextualExplanationOperation
                .ExplainExactMember(
                    ExactBasis(["Signature"]),
                    ["Documentation"])
                .Content;

        string json = JsonSerializer.Serialize(
            document,
            ResourceExplanationJsonContext
                .Default
                .ResourceExplanationDocument);
        ResourceExplanationDocument roundTripped =
            JsonSerializer.Deserialize(
                json,
                ResourceExplanationJsonContext
                    .Default
                    .ResourceExplanationDocument)!;

        Assert.DoesNotContain("\"requested_path\"", json);
        Assert.DoesNotContain("\"path\"", json);
        Assert.Null(roundTripped.RequestedPath);
        Assert.All(roundTripped.Resources, resource =>
            Assert.Null(resource.Path));
        Assert.Equal(document.Root, roundTripped.Root);
    }

    [Fact]
    public void ResolvedBasis_RejectsNonSuccessResolution()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(() =>
                new ResolvedMemberExplanationBasis(
                    Source(),
                    new MemberDocumentResolutionOutcome.Rejected(
                        MemberDocumentResolutionRejection
                            .MemberGroupNotFound),
                    MemberDefaultFacet(),
                    new([], []),
                    ["Overloads"]));

        Assert.Equal("resolution", exception.ParamName);
    }

    [Fact]
    public async Task ResolvedSingleton_MapsExactMemberAndPopulation()
    {
        byte[] content =
            await File.ReadAllBytesAsync(
                FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
                TestContext.Current.CancellationToken);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        MemberDocumentResolutionOutcome resolution =
            Resolve(
                    library,
                    Name(
                        "ILInspector.Decompiler.Fixtures.NewUnsafe",
                        "MemorySafetySpellingFixture"),
                    "PointerFreeUnsafeMethod")
                .Content;
        MemberDocument exact =
            Assert.IsType<MemberDocumentResolutionOutcome.Exact>(
                    resolution)
                .Document;

        ResourceExplanationDocument document =
            MemberContextualExplanationOperation
                .ExplainResolvedMember(
                    new(
                        FixtureSource(),
                        resolution,
                        MemberDefaultFacet(),
                        new([], []),
                        ["Signature"]))
                .Content;

        ResourceExplanationResource root = document.Resources[0];
        Assert.Equal("exact-member", root.ResourceType.Value);
        Assert.Equal(
            exact.Subject.Anchor.StableSelector,
            Text(root, "stable-selector"));
        Assert.Equal(
            exact.Subject.Fingerprint.ToString(),
            Text(root, "fingerprint"));
        Assert.Equal(
            exact.Subject.Population.ModuleVersionId.ToString("D"),
            Text(root, "population-module-version-id"));
        Assert.Equal(
            exact.Subject.Population.TypeDefinitionToken.ToString("X8"),
            Text(root, "population-type-definition-token"));
        Assert.Equal(
            exact.Subject.MetadataToken.ToString("X8"),
            Text(root, "metadata-token"));
        Assert.Equal(
            exact.Subject.BaselineOrdinal.ToString(),
            Text(root, "baseline-ordinal"));
        Assert.Equal(["Signature"], Texts(root, "selected-content"));

        string json = JsonSerializer.Serialize(
            document,
            ResourceExplanationJsonContext
                .Default
                .ResourceExplanationDocument);
        ResourceExplanationDocument roundTripped =
            JsonSerializer.Deserialize(
                json,
                ResourceExplanationJsonContext
                    .Default
                    .ResourceExplanationDocument)!;
        Assert.Equal(document.Root, roundTripped.Root);
        Assert.Equal(
            exact.Subject.Population.ModuleVersionId.ToString("D"),
            Text(
                roundTripped.Resources[0],
                "population-module-version-id"));

        await library.RetireAsync();
    }

    [Fact]
    public async Task ResolvedOverview_PreservesPopulationIntentAndIdentity()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");
        MemberDocumentResolutionOutcome extension =
            Resolve(
                    library,
                    type,
                    "Deserialize",
                    receiver:
                        MemberOverloadReceiverFilter.Extension)
                .Content;
        MemberDocumentResolutionOutcome nonExtension =
            Resolve(
                    library,
                    type,
                    "Deserialize",
                    receiver:
                        MemberOverloadReceiverFilter.NonExtension)
                .Content;

        ResourceExplanationDocument extensionExplanation =
            ExplainResolved(extension);
        ResourceExplanationDocument nonExtensionExplanation =
            ExplainResolved(nonExtension);
        ResourceExplanationResource root =
            extensionExplanation.Resources[0];
        MemberOverviewDocument overview =
            Assert.IsType<MemberDocumentResolutionOutcome.Overview>(
                    extension)
                .Document;

        Assert.Equal("member-group", root.ResourceType.Value);
        Assert.Equal("CSharp", Text(root, "group-spelling"));
        Assert.Equal(
            overview.Population.Assembly.Name.ToString(),
            Text(root, "population-assembly-name"));
        Assert.Equal(
            overview.Population.ModuleVersionId.ToString("D"),
            Text(root, "population-module-version-id"));
        Assert.Equal(
            "Extension",
            Text(root, "population-receiver"));
        Assert.Equal(
            "False",
            Text(root, "population-include-hidden"));
        Assert.Equal(["Overloads"], Texts(root, "selected-content"));
        Assert.NotEqual(
            extensionExplanation.Root,
            nonExtensionExplanation.Root);

        await library.RetireAsync();
    }

    private static string Text(
        ResourceExplanationResource resource,
        string identity) =>
        Assert.IsType<ExplanationValue.Scalar>(
            Assert.Single(
                resource.Facts.Single(fact =>
                    fact.Fact.Value == identity).Values)).Value.Text!;

    private static string[] Texts(
        ResourceExplanationResource resource,
        string identity) =>
    [
        .. resource.Facts.Single(fact =>
                fact.Fact.Value == identity).Values
            .Select(value =>
                Assert.IsType<ExplanationValue.Scalar>(value).Value.Text!),
    ];

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

    private static ResolvedInspectionSource FixtureSource() =>
        new(
            AssemblyResolutionProvenance.Local(
                FixtureCatalog.DecompilerUnsafeNew.AssemblyPath()),
            null,
            "DecompilerUnsafeNew",
            framework: null);

    private static ResourceExplanationDocument ExplainResolved(
        MemberDocumentResolutionOutcome resolution) =>
        MemberContextualExplanationOperation
            .ExplainResolvedMember(
                new(
                    Source(),
                    resolution,
                    MemberDefaultFacet(),
                    new([], []),
                    ["Overloads"]))
            .Content;

    private static InspectionEnvelope<MemberDocumentResolutionOutcome>
        Resolve(
            LibraryInspectionTestLibrary library,
            MetadataTypeDefinitionName type,
            string memberName,
            MemberDocumentSelector? selector = null,
            MemberOverloadReceiverFilter receiver =
                MemberOverloadReceiverFilter.All) =>
        MemberDocumentResolutionOperation.Execute(
            new(
                library.Reference,
                new(
                    new(type, memberName),
                    s_bounds,
                    selector,
                    receiver: receiver)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static ViewFacetId MemberDefaultFacet() =>
        InspectionViewFacetCatalog.Registry
            .GetRequiredDescriptor(
                StructuralSubjectKind.Member,
                ViewFacetRole.MemberOverview)
            .Id;

    private static MetadataTypeDefinitionName TypeName() =>
        Name("System.Text.Json", "JsonSerializer");

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [.. segments]))
            .Name;
}
