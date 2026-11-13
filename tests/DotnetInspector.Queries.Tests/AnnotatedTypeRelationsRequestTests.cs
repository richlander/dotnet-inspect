using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class AnnotatedTypeRelationsRequestTests
{
    private static readonly InspectionGraphEvidenceDescriptor Evidence =
        new("test.annotated-relations.request", InspectionGraphOwner.Queries);

    [Fact]
    public void AllApplicableClosesOverDirectionAdmittedRegistrySnapshot()
    {
        InspectionGraphRelationshipDescriptor outgoing =
            Relationship("test.outgoing", InspectionGraphEndpointRole.Source);
        InspectionGraphRelationshipDescriptor incoming =
            Relationship("test.incoming", InspectionGraphEndpointRole.Target);
        var query =
            new InspectionQuery<AnnotatedTypeRelationsContribution>(
                "test.annotated-relations.rows",
                InspectionCost.NetworkFree);
        AnnotatedTypeRelationsCatalog relations =
            new AnnotatedTypeRelationsRegistry()
                .AddRelationship(Registration(outgoing, query))
                .AddRelationship(Registration(incoming, query))
                .Compile();

        AnnotatedTypeRelationsRequestResult.Accepted accepted =
            Assert.IsType<AnnotatedTypeRelationsRequestResult.Accepted>(
                AnnotatedTypeRelationsRequestResolver.Resolve(
                    relations,
                    Request(
                        AnnotatedTypeRelationsFamilySelection.AllApplicable,
                        SubjectRelationDirectionSelection.Outgoing),
                    QueryCatalog(query)));

        Assert.Equal([outgoing.Id], accepted.ResolvedRelationshipIds);
        Assert.Same(
            outgoing,
            Assert.Single(accepted.Plan.Relationships).Relationship);
    }

    [Fact]
    public void ExplicitIdsBindToCatalogOwnedDescriptorsInRegistryOrder()
    {
        InspectionGraphRelationshipDescriptor first =
            Relationship("test.first", InspectionGraphEndpointRole.Source);
        InspectionGraphRelationshipDescriptor second =
            Relationship("test.second", InspectionGraphEndpointRole.Source);
        var query =
            new InspectionQuery<AnnotatedTypeRelationsContribution>(
                "test.annotated-relations.rows",
                InspectionCost.NetworkFree);
        AnnotatedTypeRelationsCatalog relations =
            new AnnotatedTypeRelationsRegistry()
                .AddRelationship(Registration(first, query))
                .AddRelationship(Registration(second, query))
                .Compile();

        AnnotatedTypeRelationsRequestResult.Accepted accepted =
            Assert.IsType<AnnotatedTypeRelationsRequestResult.Accepted>(
                AnnotatedTypeRelationsRequestResolver.Resolve(
                    relations,
                    Request(
                        AnnotatedTypeRelationsFamilySelection.Explicit(
                            [second.Id, first.Id]),
                        SubjectRelationDirectionSelection.Outgoing),
                    QueryCatalog(query)));

        Assert.Equal(
            [first.Id, second.Id],
            accepted.ResolvedRelationshipIds);
        Assert.Equal(
            [first, second],
            accepted.Plan.Relationships.Select(
                registration => registration.Relationship));
    }

    [Fact]
    public void UnknownIdsAndEmptyAllSelectionAreTypedRejections()
    {
        InspectionGraphRelationshipDescriptor outgoing =
            Relationship("test.outgoing", InspectionGraphEndpointRole.Source);
        var query =
            new InspectionQuery<AnnotatedTypeRelationsContribution>(
                "test.annotated-relations.rows",
                InspectionCost.NetworkFree);
        AnnotatedTypeRelationsCatalog relations =
            new AnnotatedTypeRelationsRegistry()
                .AddRelationship(Registration(outgoing, query))
                .Compile();

        AnnotatedTypeRelationsRequestFailure unknown =
            Assert.IsType<AnnotatedTypeRelationsRequestResult.Rejected>(
                AnnotatedTypeRelationsRequestResolver.Resolve(
                    relations,
                    Request(
                        AnnotatedTypeRelationsFamilySelection.Explicit(
                            ["test.missing"]),
                        SubjectRelationDirectionSelection.Outgoing),
                    QueryCatalog(query))).Failure;
        Assert.Equal(
            AnnotatedTypeRelationsRequestFailureKind.UnknownRelationship,
            unknown.Kind);
        Assert.Equal("test.missing", unknown.DescriptorId);

        AnnotatedTypeRelationsRequestFailure none =
            Assert.IsType<AnnotatedTypeRelationsRequestResult.Rejected>(
                AnnotatedTypeRelationsRequestResolver.Resolve(
                    relations,
                    Request(
                        AnnotatedTypeRelationsFamilySelection.AllApplicable,
                        SubjectRelationDirectionSelection.Incoming),
                    QueryCatalog(query))).Failure;
        Assert.Equal(
            AnnotatedTypeRelationsRequestFailureKind.NoApplicableRelationships,
            none.Kind);
    }

    [Fact]
    public void ColorsResolveByCanonicalIdAndRequiredSubset()
    {
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.call", InspectionGraphEndpointRole.Source);
        InspectionGraphCharacteristicDescriptor color =
            Color("test.requires-dynamic-code");
        var query =
            new InspectionQuery<AnnotatedTypeRelationsContribution>(
                "test.annotated-relations.rows",
                InspectionCost.NetworkFree);
        AnnotatedTypeRelationsCatalog relations =
            new AnnotatedTypeRelationsRegistry()
                .AddRelationship(Registration(relationship, query))
                .AddColor(
                    new(
                        color,
                        new AnnotatedTypeRelationsAdapterQueries(rows: query)))
                .Compile();
        var request = new AnnotatedTypeRelationsRequest(
            TypeFocus(),
            AnnotatedTypeRelationsFamilySelection.AllApplicable,
            SubjectRelationDirectionSelection.Outgoing,
            [color.Id],
            [color.Id],
            AnnotatedTypeRelationsTerminal.Rows);

        AnnotatedTypeRelationsRequestResult.Accepted accepted =
            Assert.IsType<AnnotatedTypeRelationsRequestResult.Accepted>(
                AnnotatedTypeRelationsRequestResolver.Resolve(
                    relations,
                    request,
                    QueryCatalog(query)));

        Assert.Equal([color.Id], accepted.ResolvedColorIds);
        Assert.Equal([color.Id], accepted.ResolvedRequiredColorIds);
        Assert.Same(color, Assert.Single(accepted.Plan.Colors).Color);
    }

    [Fact]
    public void PlanningFailureRemainsTyped()
    {
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.call", InspectionGraphEndpointRole.Source);
        var rows =
            new InspectionQuery<AnnotatedTypeRelationsContribution>(
                "test.annotated-relations.rows",
                InspectionCost.NetworkFree);
        AnnotatedTypeRelationsCatalog relations =
            new AnnotatedTypeRelationsRegistry()
                .AddRelationship(Registration(relationship, rows))
                .Compile();

        AnnotatedTypeRelationsRequestFailure failure =
            Assert.IsType<AnnotatedTypeRelationsRequestResult.Rejected>(
                AnnotatedTypeRelationsRequestResolver.Resolve(
                    relations,
                    new(
                        TypeFocus(),
                        AnnotatedTypeRelationsFamilySelection.AllApplicable,
                        SubjectRelationDirectionSelection.Outgoing,
                        [],
                        [],
                        AnnotatedTypeRelationsTerminal.Count),
                    QueryCatalog(rows))).Failure;

        Assert.Equal(
            AnnotatedTypeRelationsRequestFailureKind.PlanningRejected,
            failure.Kind);
        Assert.Equal(
            AnnotatedTypeRelationsPlanningFailureKind.UnsupportedTerminal,
            failure.PlanningFailure?.Kind);
    }

    [Fact]
    public void ConstructionRejectsRepeatedIdsAndUnselectedPredicateColor()
    {
        Assert.Throws<ArgumentException>(
            () => AnnotatedTypeRelationsFamilySelection.Explicit(
                ["test.call", "test.call"]));
        Assert.Throws<ArgumentException>(
            () => new AnnotatedTypeRelationsRequest(
                TypeFocus(),
                AnnotatedTypeRelationsFamilySelection.AllApplicable,
                SubjectRelationDirectionSelection.Outgoing,
                ["test.selected"],
                ["test.required"],
                AnnotatedTypeRelationsTerminal.Rows));
    }

    private static AnnotatedTypeRelationsRequest Request(
        AnnotatedTypeRelationsFamilySelection selection,
        SubjectRelationDirectionSelection direction) =>
        new(
            TypeFocus(),
            selection,
            direction,
            [],
            [],
            AnnotatedTypeRelationsTerminal.Rows);

    private static AnnotatedTypeRelationsRelationshipRegistration Registration(
        InspectionGraphRelationshipDescriptor relationship,
        InspectionQuery<AnnotatedTypeRelationsContribution> rows) =>
        new(
            SubjectRelationForm.Invocation,
            relationship,
            new AnnotatedTypeRelationsAdapterQueries(rows: rows));

    private static InspectionGraphRelationshipDescriptor Relationship(
        string id,
        InspectionGraphEndpointRole typeRole) =>
        new(
            id,
            InspectionGraphOwner.Queries,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    typeRole),
            ],
            InspectionGraphEndpointProjection.Exact,
            OccurrenceIdentityProjection.Instance,
            [Evidence]);

    private static InspectionGraphCharacteristicDescriptor Color(string id) =>
        new(
            id,
            InspectionGraphOwner.Queries,
            InspectionGraphValueCatalog.Boolean,
            [InspectionGraphTargetKind.Edge],
            [],
            [InspectionGraphCharacteristicDerivationKind.Direct],
            InspectionGraphAggregationPolicy.None);

    private static SubjectRelationFocusAuthority TypeFocus()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        StructuralSubjectTestData.PackageContext context =
            StructuralSubjectTestData.Package(coordinate);
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(
                context.Subject,
                Library(coordinate));
        return SubjectRelationFocusAuthority.Capture(
            StructuralSubjectIdentity.ForType(
                library,
                TypeName("Sample", "Widget")));
    }

    private static IInspectionQueryCatalog QueryCatalog(
        params InspectionQuery<AnnotatedTypeRelationsContribution>[] queries)
    {
        var registry = new InspectionQueryRegistry<object>();
        foreach (
            InspectionQuery<AnnotatedTypeRelationsContribution> query in
            queries)
        {
            registry.Add(
                query,
                static _ => throw new InvalidOperationException(
                    "Request tests do not execute adapter queries."));
        }

        return registry.Compile();
    }

    private static RealizedMemberCoordinate.Package Coordinate() =>
        new(
            "sample.package",
            "1.0.0",
            "nuget-org",
            "net11.0",
            runtimeIdentifier: null);

    private static WorkspaceContextMember Library(
        RealizedMemberCoordinate.Package coordinate)
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Sample",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => new MemoryStream([0], writable: false),
            AssemblyResolutionProvenance.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier));
        return new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance));
    }

    private static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [name])).Name;

    private sealed class OccurrenceIdentityProjection :
        InspectionGraphOccurrenceIdentityProjection
    {
        public static OccurrenceIdentityProjection Instance { get; } = new();

        public override object Project(
            InspectionGraphOccurrence occurrence) =>
            occurrence.Evidence;
    }
}
