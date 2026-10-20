using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class AnnotatedTypeRelationsPlanningTests
{
    private static readonly InspectionGraphEvidenceDescriptor Evidence =
        new("test.annotated-relations", InspectionGraphOwner.Queries);

    [Fact]
    public void ExactSelectionIsDeterministicAndSharesAdapterQueries()
    {
        InspectionGraphRelationshipDescriptor first =
            Relationship("test.first", InspectionGraphEndpointRole.Source);
        InspectionGraphRelationshipDescriptor second =
            Relationship("test.second", InspectionGraphEndpointRole.Source);
        InspectionGraphCharacteristicDescriptor color =
            Color("test.unsafe-propagation");
        var shared =
            new InspectionQuery<int>(
                "test.annotated-relations.shared",
                InspectionCost.Moderated);
        var colorQuery =
            new InspectionQuery<int>(
                "test.annotated-relations.color",
                InspectionCost.Unbounded);
        AnnotatedTypeRelationsCatalog relationsCatalog =
            new AnnotatedTypeRelationsRegistry()
                .AddRelationship(
                    RelationshipRegistration(first, rows: shared))
                .AddRelationship(
                    RelationshipRegistration(second, rows: shared))
                .AddColor(ColorRegistration(color, rows: colorQuery))
                .Compile();
        IInspectionQueryCatalog queryCatalog =
            QueryCatalog(shared, colorQuery);

        var request = new AnnotatedTypeRelationsPlanningRequest(
            TypeFocus(),
            [second, first],
            SubjectRelationDirectionSelection.Outgoing,
            [color],
            [color],
            AnnotatedTypeRelationsTerminal.Rows);
        AnnotatedTypeRelationsPlan plan =
            Assert.IsType<AnnotatedTypeRelationsPlanningResult.Accepted>(
                relationsCatalog.Resolve(request, queryCatalog)).Plan;

        Assert.Equal(
            [first, second],
            plan.Relationships.Select(
                registration => registration.Relationship));
        Assert.Equal(
            [color],
            plan.Colors.Select(registration => registration.Color));
        Assert.Equal([shared, colorQuery], plan.Queries);
        Assert.Equal(InspectionCost.Unbounded, plan.Cost);
        Assert.Same(request, plan.Request);
    }

    [Fact]
    public void LookalikeDescriptorsDoNotResolveById()
    {
        InspectionGraphRelationshipDescriptor registered =
            Relationship("test.invocation", InspectionGraphEndpointRole.Source);
        InspectionGraphRelationshipDescriptor lookalike =
            Relationship("test.invocation", InspectionGraphEndpointRole.Source);
        var query =
            new InspectionQuery<int>(
                "test.annotated-relations.rows",
                InspectionCost.NetworkFree);
        AnnotatedTypeRelationsCatalog catalog =
            new AnnotatedTypeRelationsRegistry()
                .AddRelationship(
                    RelationshipRegistration(registered, rows: query))
                .Compile();

        AnnotatedTypeRelationsPlanningFailure failure =
            Assert.IsType<AnnotatedTypeRelationsPlanningResult.Rejected>(
                catalog.Resolve(
                    Request(
                        [lookalike],
                        SubjectRelationDirectionSelection.Outgoing),
                    QueryCatalog(query))).Failure;

        Assert.Equal(
            AnnotatedTypeRelationsPlanningFailureKind.UnknownRelationship,
            failure.Kind);
        Assert.Equal(lookalike.Id, failure.DescriptorId);
    }

    [Fact]
    public void LookalikeColorsDoNotResolveById()
    {
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.invocation", InspectionGraphEndpointRole.Source);
        InspectionGraphCharacteristicDescriptor registered =
            Color("test.unsafe-propagation");
        InspectionGraphCharacteristicDescriptor lookalike =
            Color("test.unsafe-propagation");
        var query =
            new InspectionQuery<int>(
                "test.annotated-relations.rows",
                InspectionCost.NetworkFree);
        AnnotatedTypeRelationsCatalog catalog =
            new AnnotatedTypeRelationsRegistry()
                .AddRelationship(
                    RelationshipRegistration(relationship, rows: query))
                .AddColor(ColorRegistration(registered, rows: query))
                .Compile();
        var request = new AnnotatedTypeRelationsPlanningRequest(
            TypeFocus(),
            [relationship],
            SubjectRelationDirectionSelection.Outgoing,
            [lookalike],
            [],
            AnnotatedTypeRelationsTerminal.Rows);

        AnnotatedTypeRelationsPlanningFailure failure =
            Assert.IsType<AnnotatedTypeRelationsPlanningResult.Rejected>(
                catalog.Resolve(request, QueryCatalog(query))).Failure;

        Assert.Equal(
            AnnotatedTypeRelationsPlanningFailureKind.UnknownColor,
            failure.Kind);
        Assert.Equal(lookalike.Id, failure.DescriptorId);
    }

    [Fact]
    public void DirectionComesFromTypeSeedAdmission()
    {
        InspectionGraphRelationshipDescriptor outgoing =
            Relationship("test.outgoing", InspectionGraphEndpointRole.Source);
        var query =
            new InspectionQuery<int>(
                "test.annotated-relations.rows",
                InspectionCost.NetworkFree);
        AnnotatedTypeRelationsCatalog catalog =
            new AnnotatedTypeRelationsRegistry()
                .AddRelationship(
                    RelationshipRegistration(outgoing, rows: query))
                .Compile();

        AnnotatedTypeRelationsPlanningFailure failure =
            Assert.IsType<AnnotatedTypeRelationsPlanningResult.Rejected>(
                catalog.Resolve(
                    Request(
                        [outgoing],
                        SubjectRelationDirectionSelection.Incoming),
                    QueryCatalog(query))).Failure;
        Assert.Equal(
            AnnotatedTypeRelationsPlanningFailureKind.UnsupportedDirection,
            failure.Kind);

        Assert.IsType<AnnotatedTypeRelationsPlanningResult.Accepted>(
            catalog.Resolve(
                Request(
                    [outgoing],
                    SubjectRelationDirectionSelection.Both),
                QueryCatalog(query)));
    }

    [Fact]
    public void UnsupportedTerminalIsRejected()
    {
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.invocation", InspectionGraphEndpointRole.Source);
        var rows =
            new InspectionQuery<int>(
                "test.annotated-relations.rows",
                InspectionCost.NetworkFree);
        AnnotatedTypeRelationsCatalog catalog =
            new AnnotatedTypeRelationsRegistry()
                .AddRelationship(
                    RelationshipRegistration(relationship, rows: rows))
                .Compile();

        AnnotatedTypeRelationsPlanningFailure failure =
            Assert.IsType<AnnotatedTypeRelationsPlanningResult.Rejected>(
                catalog.Resolve(
                    Request(
                        [relationship],
                        SubjectRelationDirectionSelection.Outgoing,
                        AnnotatedTypeRelationsTerminal.Count),
                    QueryCatalog(rows))).Failure;

        Assert.Equal(
            AnnotatedTypeRelationsPlanningFailureKind.UnsupportedTerminal,
            failure.Kind);
    }

    [Fact]
    public void AdapterQueryMustBelongToSuppliedQueryCatalog()
    {
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.invocation", InspectionGraphEndpointRole.Source);
        var registered =
            new InspectionQuery<int>(
                "test.annotated-relations.registered",
                InspectionCost.NetworkFree);
        var lookalike =
            new InspectionQuery<int>(
                registered.Name,
                InspectionCost.NetworkFree);
        AnnotatedTypeRelationsCatalog catalog =
            new AnnotatedTypeRelationsRegistry()
                .AddRelationship(
                    RelationshipRegistration(relationship, rows: registered))
                .Compile();

        AnnotatedTypeRelationsPlanningFailure failure =
            Assert.IsType<AnnotatedTypeRelationsPlanningResult.Rejected>(
                catalog.Resolve(
                    Request(
                        [relationship],
                        SubjectRelationDirectionSelection.Outgoing),
                    QueryCatalog(lookalike))).Failure;

        Assert.Equal(
            AnnotatedTypeRelationsPlanningFailureKind.QueryUnavailable,
            failure.Kind);
    }

    [Fact]
    public void RegistryRejectsRepeatedDescriptorIds()
    {
        InspectionGraphRelationshipDescriptor first =
            Relationship("test.invocation", InspectionGraphEndpointRole.Source);
        InspectionGraphRelationshipDescriptor duplicate =
            Relationship("test.invocation", InspectionGraphEndpointRole.Target);
        var query =
            new InspectionQuery<int>(
                "test.annotated-relations.rows",
                InspectionCost.NetworkFree);
        var registry = new AnnotatedTypeRelationsRegistry()
            .AddRelationship(RelationshipRegistration(first, rows: query));

        Assert.Throws<ArgumentException>(
            () => registry.AddRelationship(
                RelationshipRegistration(duplicate, rows: query)));
    }

    [Fact]
    public void RequestRequiresTypeFocusAndSelectedPredicateColors()
    {
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.invocation", InspectionGraphEndpointRole.Source);
        InspectionGraphCharacteristicDescriptor selected =
            Color("test.selected");
        InspectionGraphCharacteristicDescriptor required =
            Color("test.required");

        Assert.Throws<ArgumentException>(
            () => new AnnotatedTypeRelationsPlanningRequest(
                PackageFocus(),
                [relationship],
                SubjectRelationDirectionSelection.Outgoing,
                [],
                [],
                AnnotatedTypeRelationsTerminal.Rows));
        Assert.Throws<ArgumentException>(
            () => new AnnotatedTypeRelationsPlanningRequest(
                TypeFocus(),
                [relationship],
                SubjectRelationDirectionSelection.Outgoing,
                [selected],
                [required],
                AnnotatedTypeRelationsTerminal.Rows));
    }

    private static AnnotatedTypeRelationsPlanningRequest Request(
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        SubjectRelationDirectionSelection direction,
        AnnotatedTypeRelationsTerminal terminal =
            AnnotatedTypeRelationsTerminal.Rows) =>
        new(
            TypeFocus(),
            relationships,
            direction,
            colors: [],
            requiredColors: [],
            terminal);

    private static AnnotatedTypeRelationsRelationshipRegistration
        RelationshipRegistration(
            InspectionGraphRelationshipDescriptor relationship,
            InspectionQueryDefinition? rows = null,
            InspectionQueryDefinition? count = null,
            InspectionQueryDefinition? exists = null) =>
        new(
            SubjectRelationForm.Invocation,
            relationship,
            new(rows, count, exists));

    private static AnnotatedTypeRelationsColorRegistration ColorRegistration(
        InspectionGraphCharacteristicDescriptor color,
        InspectionQueryDefinition? rows = null,
        InspectionQueryDefinition? count = null,
        InspectionQueryDefinition? exists = null) =>
        new(color, new(rows, count, exists));

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

    private static IInspectionQueryCatalog QueryCatalog(
        params InspectionQuery<int>[] queries)
    {
        var registry = new InspectionQueryRegistry<object>();
        foreach (InspectionQuery<int> query in queries)
        {
            registry.Add(query, static _ => 0);
        }

        return registry.Compile();
    }

    private static SubjectRelationFocusAuthority PackageFocus() =>
        SubjectRelationFocusAuthority.Capture(
            StructuralSubjectTestData.Package(Coordinate()).Subject);

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
