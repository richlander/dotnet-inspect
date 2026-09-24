using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;
using QuerySpace;
using QuerySpace.Operations;

namespace DotnetInspector.Queries.Tests;

public sealed class SubjectRelationsPopulationTests
{
    private static readonly InspectionGraphEvidenceDescriptor Evidence =
        new("test.subject-relations", InspectionGraphOwner.Queries);

    private static readonly InspectionGraphRelationshipDescriptor
        Relationship =
        CreateRelationship("test.depends-on");

    private static InspectionGraphRelationshipDescriptor CreateRelationship(
        string id) =>
        new(
            id,
            InspectionGraphOwner.Queries,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Package],
            [InspectionGraphSubjectKind.Package],
            [InspectionGraphSubjectKind.Package],
            [InspectionGraphSubjectKind.Package],
            [
                new(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
            ],
            InspectionGraphEndpointProjection.Exact,
            new OccurrenceIdentityProjection(),
            [Evidence]);

    private static readonly InspectionQuery<int> Producer =
        new("test.subject-relations", InspectionCost.NetworkFree);

    [Fact]
    public void RoutesAcceptOnlyTheirExactStructuralSubject()
    {
        Context context = CreateContext();
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(
                context.Focus,
                Library(context.Focus.Coordinate));
        StructuralSubjectIdentity.TypeSubject type =
            StructuralSubjectIdentity.ForType(
                library,
                TypeName("Sample", "Widget"));
        StructuralSubjectIdentity.MemberSubject member =
            StructuralSubjectIdentity.ForMember(
                type,
                new(
                    "Run()",
                    "Sample.Widget.Run()",
                    MemberAnchor.ComputeFingerprint(
                        "Sample.Widget.Run()"),
                    "Sample.Widget",
                    "Run"));
        (
            SubjectRelationsRouteKind Route,
            StructuralSubjectIdentity Subject)[] subjects =
        [
            (SubjectRelationsRouteKind.Package, context.Focus),
            (SubjectRelationsRouteKind.Library, library),
            (SubjectRelationsRouteKind.Type, type),
            (SubjectRelationsRouteKind.Member, member),
        ];

        foreach ((SubjectRelationsRouteKind route, StructuralSubjectIdentity
            subject) in subjects)
        {
            _ = new SubjectRelationsInspectionRequest(
                route,
                subject,
                context.Population,
                RowsRequest(10));

            foreach (StructuralSubjectIdentity other in
                     subjects
                         .Where(candidate => candidate.Route != route)
                         .Select(static candidate => candidate.Subject))
            {
                Assert.Throws<ArgumentException>(
                    () => new SubjectRelationsInspectionRequest(
                        route,
                        other,
                        context.Population,
                        RowsRequest(10)));
            }
        }

        Assert.Throws<ArgumentException>(
            () => new SubjectRelationsInspectionRequest(
                SubjectRelationsRouteKind.Library,
                StructuralSubjectIdentity.ForAllLibraries(context.Focus),
                context.Population,
                RowsRequest(10)));
    }

    [Fact]
    public void QuerySpaceRegistersAllExactSubjectRoutes()
    {
        (
            SubjectRelationsRouteKind Kind,
            string Identity,
            string SubjectRole)[] expected =
        [
            (
                SubjectRelationsRouteKind.Package,
                SubjectRelationsQuery.PackageRouteIdentity,
                SubjectRelationsQuery.PackageSubjectRole),
            (
                SubjectRelationsRouteKind.Library,
                SubjectRelationsQuery.LibraryRouteIdentity,
                SubjectRelationsQuery.LibrarySubjectRole),
            (
                SubjectRelationsRouteKind.Type,
                SubjectRelationsQuery.TypeRouteIdentity,
                SubjectRelationsQuery.TypeSubjectRole),
            (
                SubjectRelationsRouteKind.Member,
                SubjectRelationsQuery.MemberRouteIdentity,
                SubjectRelationsQuery.MemberSubjectRole),
        ];

        foreach ((SubjectRelationsRouteKind kind, string identity,
            string subjectRole) in expected)
        {
            IQueryOperationRoute route = SubjectRelationsQuery.Route(kind);
            Assert.Equal(identity, route.Identity);
            Assert.Equal(
                SubjectRelationsQuery.OperationIdentity,
                route.OperationIdentity);
            Assert.Equal(subjectRole, route.SubjectRole);
            Assert.Equal(SubjectRelationsQuery.ResultGrain, route.ResultGrain);
            Assert.Equal(
                [SubjectRelationsQuery.RelationsRowSet],
                route.RowSets);
            Assert.Equal(6, route.Capabilities.Terms.Count);
            Assert.Empty(route.Capabilities.Stages);
        }
    }

    [Fact]
    public void QuerySpaceResolvesCanonicalPopulationFacets()
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [
                Term(SubjectRelationsQuery.FormTermKey, "invocation"),
                Term(
                    SubjectRelationsQuery.RelationTermKey,
                    "call"),
                Term(
                    SubjectRelationsQuery.DirectionTermKey,
                    "incoming"),
                Term(
                    SubjectRelationsQuery.EvidenceTermKey,
                    "static-il"),
                Term(
                    SubjectRelationsQuery.EcosystemTermKey,
                    "ecosystem.dotnet"),
                Term(
                    SubjectRelationsQuery.ConceptTermKey,
                    IntegrationConceptCatalog.HttpClient.Id.Value),
            ],
            [],
            [],
            []);

        var accepted =
            Assert.IsType<SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    intent,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SubjectRelationForm.Invocation,
            accepted.Plan.Selection.Form);
        Assert.Equal("call", accepted.Plan.Selection.Relationship);
        Assert.Equal(
            SubjectRelationDirectionSelection.Incoming,
            accepted.Plan.Selection.Direction);
        Assert.Equal(
            SubjectRelationEvidenceKind.StaticIlObservation,
            accepted.Plan.Selection.Evidence);
        Assert.Equal(
            "ecosystem.dotnet",
            accepted.Plan.Selection.Ecosystem?.Value);
        Assert.Equal(
            IntegrationConceptCatalog.HttpClient.Id.Value,
            accepted.Plan.Selection.Concept);
    }

    [Fact]
    public void QuerySpaceRejectsConflictingOrUnknownFacets()
    {
        SubjectRelationsQueryPlanResult conflicting =
            SubjectRelationsQuery.ResolveIntent(
                SubjectRelationsRouteKind.Package,
                PortableQueryIntent.Create(
                    [
                        Term(
                            SubjectRelationsQuery.DirectionTermKey,
                            "incoming"),
                        Term(
                            SubjectRelationsQuery.DirectionTermKey,
                            "outgoing"),
                    ],
                    [],
                    [],
                    []),
                TestContext.Current.CancellationToken);
        SubjectRelationsQueryPlanResult unknown =
            SubjectRelationsQuery.ResolveIntent(
                SubjectRelationsRouteKind.Package,
                PortableQueryIntent.Create(
                    [
                        Term(
                            SubjectRelationsQuery.ConceptTermKey,
                            "unknown-concept"),
                    ],
                    [],
                    [],
                    []),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            PortableQueryFailureReason.TermsIncompatible,
            Assert.IsType<SubjectRelationsQueryPlanResult.Rejected>(
                conflicting).Failure.Reason);
        Assert.Equal(
            PortableQueryFailureReason.ValueRejected,
            Assert.IsType<SubjectRelationsQueryPlanResult.Rejected>(
                unknown).Failure.Reason);
    }

    [Fact]
    public void PopulationRequiresCountRowsOrBoth()
    {
        Assert.Throws<ArgumentException>(
            () => new SubjectRelationPopulationRequest(
                new()));

        var count = new SubjectRelationPopulationCountRequest();
        var rows = new SubjectRelationPopulationRowsRequest(10);
        var request = new SubjectRelationPopulationRequest(
            new(),
            count,
            rows);

        Assert.Same(count, request.Count);
        Assert.Same(rows, request.Rows);
    }

    [Fact]
    public void CanonicalRowRetainsOwnerIssuedIdentity()
    {
        Context context = CreateContext();
        object occurrenceIdentity = new();
        object correspondenceEvidence = new();
        object integrationEvidence = new();
        SubjectRelationRow row = Row(
            context,
            "2.0.0",
            integration: true,
            occurrenceIdentity,
            correspondenceEvidence,
            integrationEvidence);

        Assert.Equal(SubjectRelationDirection.Outgoing, row.Direction);
        Assert.Same(occurrenceIdentity, row.Occurrences[0].Identity);
        Assert.Same(
            correspondenceEvidence,
            row.Correspondence.Evidence);
        Assert.Same(
            integrationEvidence,
            row.IntegrationAssociations[0].Evidence);
        Assert.True(row.IsIntegration);
    }

    [Fact]
    public void CanonicalRowRejectsDuplicateOccurrenceIdentity()
    {
        Context context = CreateContext();
        InspectionGraphSubject source =
            InspectionGraphSubject.ForRealizedPackage(
                context.Coordinate);
        InspectionGraphSubject target =
            InspectionGraphSubject.ForRealizedPackage(
                Coordinate("2.0.0"));
        object identity = new();
        InspectionGraphOccurrence first = Occurrence(
            0,
            source,
            target,
            identity);
        InspectionGraphOccurrence second = Occurrence(
            1,
            source,
            target,
            identity);
        SubjectRelationFocusCorrespondence correspondence =
            SubjectRelationFocusCorrespondence.Create(
                context.Focus,
                context.Population,
                source,
                InspectionGraphEndpointRole.Source,
                new object());

        Assert.Throws<ArgumentException>(
            () => new SubjectRelationRow(
                SubjectRelationForm.PackageDependency,
                SubjectRelationEvidenceKind.Declaration,
                Relationship,
                source,
                target,
                correspondence,
                [first, second]));
    }

    [Fact]
    public void IntegrationSelectionUsesIntrinsicAssociations()
    {
        Context context = CreateContext();
        SubjectRelationRow ordinary = Row(
            context,
            "2.0.0",
            integration: false);
        SubjectRelationRow integration = Row(
            context,
            "3.0.0",
            integration: true);
        SubjectRelationPopulationRequest populationRequest =
            RowsRequest(
                10,
                new(
                    integration:
                        SubjectRelationIntegrationSelection.Associated,
                    ecosystem: WorkspaceEcosystemRegistrationId.Create(
                        "ecosystem.dotnet"),
                    concept:
                        IntegrationConceptCatalog.HttpClient.Id.Value));
        SubjectRelationsInspectionRequest request =
            InspectionRequest(context, populationRequest);
        SubjectRelationPopulationEvidence evidence =
            PopulationEvidence(context, complete: true);

        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                request,
                evidence,
                count: null,
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [ordinary, integration],
                    continuation: null)));

        SubjectRelationPopulationResult settled =
            SubjectRelationsPopulationOperation.Settle(
                request,
                evidence,
                count: null,
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [integration],
                    continuation: null));

        SubjectRelationRow selected =
            Assert.Single(
                Assert.IsType<
                    SubjectRelationPopulationRowsOutcome.Read>(
                        settled.Rows).Items);
        Assert.Same(integration, selected);
        Assert.Same(
            integration.Occurrences[0].Identity,
            selected.Occurrences[0].Identity);
    }

    [Fact]
    public void IntegrationFacetsMustMatchOneAssociation()
    {
        Context context = CreateContext();
        InspectionGraphSubject source =
            InspectionGraphSubject.ForRealizedPackage(
                context.Coordinate);
        InspectionGraphSubject target =
            InspectionGraphSubject.ForRealizedPackage(
                Coordinate("2.0.0"));
        SubjectRelationRow row = new(
            SubjectRelationForm.PackageDependency,
            SubjectRelationEvidenceKind.Declaration,
            Relationship,
            source,
            target,
            SubjectRelationFocusCorrespondence.Create(
                context.Focus,
                context.Population,
                source,
                InspectionGraphEndpointRole.Source,
                new object()),
            [Occurrence(0, source, target, new object())],
            [
                SubjectRelationIntegrationAssociation.Create(
                    IntegrationConceptCatalog.HttpClient,
                    IntegrationConceptCatalog.HttpClient
                        .ProducerPolicies[0],
                    WorkspaceEcosystemRegistrationId.Create(
                        "ecosystem.other"),
                    new object()),
                SubjectRelationIntegrationAssociation.Create(
                    IntegrationConceptCatalog.AspNetCore,
                    IntegrationConceptCatalog.AspNetCore
                        .ProducerPolicies[0],
                    WorkspaceEcosystemRegistrationId.Create(
                        "ecosystem.dotnet"),
                    new object()),
            ]);
        SubjectRelationsInspectionRequest request =
            InspectionRequest(
                context,
                RowsRequest(
                    10,
                    new(
                        ecosystem:
                            WorkspaceEcosystemRegistrationId.Create(
                                "ecosystem.dotnet"),
                        concept:
                            IntegrationConceptCatalog.HttpClient.Id.Value)));

        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                request,
                PopulationEvidence(context, complete: true),
                count: null,
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [row],
                    continuation: null)));
    }

    [Fact]
    public void CountAndRowsSettleIndependently()
    {
        Context context = CreateContext();
        SubjectRelationsInspectionRequest request =
            InspectionRequest(
                context,
                new(
                    new(),
                    new SubjectRelationPopulationCountRequest(),
                    new SubjectRelationPopulationRowsRequest(10)));

        SubjectRelationPopulationResult settled =
            SubjectRelationsPopulationOperation.Settle(
                request,
                PopulationEvidence(context, complete: true),
                new SubjectRelationPopulationCountOutcome.Counted(2),
                new SubjectRelationPopulationRowsOutcome.Unavailable());

        Assert.Equal(
            2,
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    settled.Count).Value);
        Assert.IsType<
            SubjectRelationPopulationRowsOutcome.Unavailable>(
                settled.Rows);
    }

    [Fact]
    public void PartialRowsRemainVisibleWithoutBecomingCount()
    {
        Context context = CreateContext();
        SubjectRelationRow row = Row(
            context,
            "2.0.0",
            integration: false);
        SubjectRelationsInspectionRequest request =
            InspectionRequest(
                context,
                new(
                    new(),
                    new SubjectRelationPopulationCountRequest(),
                    new SubjectRelationPopulationRowsRequest(10)));

        SubjectRelationPopulationResult settled =
            SubjectRelationsPopulationOperation.Settle(
                request,
                PopulationEvidence(context, complete: false),
                new SubjectRelationPopulationCountOutcome.Incomplete(),
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [row],
                    continuation: null));

        Assert.IsType<
            SubjectRelationPopulationCountOutcome.Incomplete>(
                settled.Count);
        Assert.Equal(
            [row],
            Assert.IsType<
                SubjectRelationPopulationRowsOutcome.Read>(
                    settled.Rows).Items);

        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                request,
                PopulationEvidence(context, complete: false),
                new SubjectRelationPopulationCountOutcome.Counted(1),
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [row],
                    continuation: null)));
    }

    [Fact]
    public void CompleteRowsMustAgreeWithExactCount()
    {
        Context context = CreateContext();
        SubjectRelationRow row = Row(
            context,
            "2.0.0",
            integration: false);
        SubjectRelationsInspectionRequest request =
            InspectionRequest(
                context,
                new(
                    new(),
                    new SubjectRelationPopulationCountRequest(),
                    new SubjectRelationPopulationRowsRequest(10)));

        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                request,
                PopulationEvidence(context, complete: true),
                new SubjectRelationPopulationCountOutcome.Counted(2),
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [row],
                    continuation: null)));
    }

    [Fact]
    public void ContinuedFinalRowsUsePopulationOrdinalForCountAgreement()
    {
        Context context = CreateContext();
        SubjectRelationRow row = Row(
            context,
            "3.0.0",
            integration: false);
        var continuation = new SubjectRelationPopulationContinuation(
            new InertString(TextPolicy.Field, "relations-next"));
        var selection = new SubjectRelationPopulationSelection();
        SubjectRelationsInspectionRequest request =
            InspectionRequest(
                context,
                new(
                    selection,
                    new SubjectRelationPopulationCountRequest(),
                    new SubjectRelationPopulationRowsRequest(
                        1,
                        continuation: continuation)));
        SubjectRelationPopulationContinuationAuthority authority =
            SubjectRelationPopulationContinuationAuthority.Capture(
                continuation,
                context.Focus,
                context.Population,
                selection,
                SubjectRelationPopulationOrdering.Producer,
                SubjectRelationRowProjection.Canonical,
                nextOrdinal: 1);
        SubjectRelationPopulationRowsOutcome.Read finalRows =
            new(
                SubjectRelationPopulationOrdering.Producer,
                [row],
                continuation: null);

        SubjectRelationPopulationResult settled =
            SubjectRelationsPopulationOperation.Settle(
                request,
                PopulationEvidence(context, complete: true),
                new SubjectRelationPopulationCountOutcome.Counted(2),
                finalRows,
                authority);

        Assert.Equal(
            2,
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    settled.Count).Value);
        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                request,
                PopulationEvidence(context, complete: true),
                new SubjectRelationPopulationCountOutcome.Counted(1),
                finalRows,
                authority));
        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                request,
                PopulationEvidence(context, complete: true),
                new SubjectRelationPopulationCountOutcome.Counted(2),
                finalRows));
    }

    [Fact]
    public void ExactCountMustExtendBeyondReturnedContinuation()
    {
        Context context = CreateContext();
        SubjectRelationRow row = Row(
            context,
            "2.0.0",
            integration: false);
        var continuation = new SubjectRelationPopulationContinuation(
            new InertString(TextPolicy.Field, "relations-next"));
        SubjectRelationsInspectionRequest request =
            InspectionRequest(
                context,
                new(
                    new(),
                    new SubjectRelationPopulationCountRequest(),
                    new SubjectRelationPopulationRowsRequest(1)));
        SubjectRelationPopulationRowsOutcome.Read rows =
            new(
                SubjectRelationPopulationOrdering.Producer,
                [row],
                continuation);

        _ = SubjectRelationsPopulationOperation.Settle(
            request,
            PopulationEvidence(context, complete: true),
            new SubjectRelationPopulationCountOutcome.Counted(2),
            rows);
        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                request,
                PopulationEvidence(context, complete: true),
                new SubjectRelationPopulationCountOutcome.Counted(1),
                rows));
    }

    [Fact]
    public void ReturnedRowsMustRespectTheRequestedSegmentBound()
    {
        Context context = CreateContext();
        SubjectRelationsInspectionRequest request =
            InspectionRequest(context, RowsRequest(1));

        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                request,
                PopulationEvidence(context, complete: true),
                count: null,
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [
                        Row(context, "2.0.0", integration: false),
                        Row(context, "3.0.0", integration: false),
                    ],
                    continuation: null)));
    }

    [Fact]
    public void SegmentSizeDoesNotChangePopulationBinding()
    {
        Context context = CreateContext();
        SubjectRelationRow row = Row(
            context,
            "2.0.0",
            integration: false);
        var continuation = new SubjectRelationPopulationContinuation(
            new InertString(TextPolicy.Field, "relations-next"));
        SubjectRelationPopulationEvidence evidence =
            PopulationEvidence(context, complete: true);

        SubjectRelationPopulationResult first =
            SubjectRelationsPopulationOperation.Settle(
                InspectionRequest(
                    context,
                    RowsRequest(1)),
                evidence,
                count: null,
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [row],
                    continuation));
        SubjectRelationPopulationResult second =
            SubjectRelationsPopulationOperation.Settle(
                InspectionRequest(
                    context,
                    RowsRequest(20)),
                evidence,
                count: null,
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [row],
                    continuation: null));

        Assert.Equal(first.Binding, second.Binding);
    }

    [Fact]
    public void ContinuationCompatibilityExcludesSegmentSize()
    {
        Context context = CreateContext();
        var continuation = new SubjectRelationPopulationContinuation(
            new InertString(TextPolicy.Field, "relations-next"));
        var selection = new SubjectRelationPopulationSelection(
            direction: SubjectRelationDirectionSelection.Outgoing);
        SubjectRelationPopulationContinuationAuthority authority =
            SubjectRelationPopulationContinuationAuthority.Capture(
                continuation,
                context.Focus,
                context.Population,
                selection,
                SubjectRelationPopulationOrdering.Producer,
                SubjectRelationRowProjection.Canonical,
                nextOrdinal: 1);

        Assert.Null(
            SubjectRelationsPopulationOperation.ContinuationRejection(
                InspectionRequest(
                    context,
                    new(
                        selection,
                        rows: new(
                            20,
                            continuation: continuation))),
                authority));

        Assert.Equal(
            SubjectRelationPopulationRowsRejection
                .IncompatibleContinuation,
            SubjectRelationsPopulationOperation.ContinuationRejection(
                InspectionRequest(
                    context,
                    new(
                        new(
                            direction:
                                SubjectRelationDirectionSelection.Incoming),
                        rows: new(
                            20,
                            continuation: continuation))),
                authority));
    }

    [Fact]
    public void ContinuationRejectsChangedFocusOrPopulationGeneration()
    {
        Context context = CreateContext();
        var continuation = new SubjectRelationPopulationContinuation(
            new InertString(TextPolicy.Field, "relations-next"));
        var selection = new SubjectRelationPopulationSelection();
        SubjectRelationPopulationContinuationAuthority authority =
            SubjectRelationPopulationContinuationAuthority.Capture(
                continuation,
                context.Focus,
                context.Population,
                selection,
                SubjectRelationPopulationOrdering.Producer,
                SubjectRelationRowProjection.Canonical,
                nextOrdinal: 1);
        SubjectRelationPopulationAuthority replacementPopulation =
            SubjectRelationPopulationAuthority.Capture(
                context.Focus.Workspace,
                new object());
        SubjectRelationsInspectionRequest request =
            new(
                SubjectRelationsRouteKind.Package,
                context.Focus,
                replacementPopulation,
                new(
                    selection,
                    rows: new(
                        10,
                        continuation: continuation)));

        Assert.Equal(
            SubjectRelationPopulationRowsRejection.StaleContinuation,
            SubjectRelationsPopulationOperation.ContinuationRejection(
                request,
                authority));

        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(
                context.Focus,
                Library(context.Focus.Coordinate));
        StructuralSubjectIdentity.TypeSubject firstType =
            StructuralSubjectIdentity.ForType(
                library,
                TypeName("Sample", "Widget"));
        StructuralSubjectIdentity.TypeSubject secondType =
            StructuralSubjectIdentity.ForType(
                library,
                TypeName("Sample", "OtherWidget"));
        SubjectRelationPopulationContinuationAuthority typeAuthority =
            SubjectRelationPopulationContinuationAuthority.Capture(
                continuation,
                firstType,
                context.Population,
                selection,
                SubjectRelationPopulationOrdering.Producer,
                SubjectRelationRowProjection.Canonical,
                nextOrdinal: 1);
        SubjectRelationsInspectionRequest changedSubjectRequest =
            new(
                SubjectRelationsRouteKind.Type,
                secondType,
                context.Population,
                new(
                    selection,
                    rows: new(
                        10,
                        continuation: continuation)));

        Assert.Equal(
            SubjectRelationPopulationRowsRejection.StaleContinuation,
            SubjectRelationsPopulationOperation.ContinuationRejection(
                changedSubjectRequest,
                typeAuthority));
    }

    [Fact]
    public void EquivalentStructuralSubjectRetainsContinuationAndRows()
    {
        Context context = CreateContext();
        StructuralSubjectIdentity.PackageSubject equalFocus =
            StructuralSubjectIdentity.ForPackage(
                context.Focus.Workspace,
                context.Focus.Occurrence);
        var continuation = new SubjectRelationPopulationContinuation(
            new InertString(TextPolicy.Field, "relations-next"));
        var selection = new SubjectRelationPopulationSelection();
        SubjectRelationPopulationContinuationAuthority authority =
            SubjectRelationPopulationContinuationAuthority.Capture(
                continuation,
                context.Focus,
                context.Population,
                selection,
                SubjectRelationPopulationOrdering.Producer,
                SubjectRelationRowProjection.Canonical,
                nextOrdinal: 1);
        SubjectRelationsInspectionRequest request =
            new(
                SubjectRelationsRouteKind.Package,
                equalFocus,
                context.Population,
                new(
                    selection,
                    rows: new(
                        10,
                        continuation: continuation)));

        Assert.Equal(context.Focus, equalFocus);
        Assert.Null(
            SubjectRelationsPopulationOperation.ContinuationRejection(
                request,
                authority));
        _ = SubjectRelationsPopulationOperation.Settle(
            request,
            PopulationEvidence(context, complete: true),
            count: null,
            new SubjectRelationPopulationRowsOutcome.Read(
                SubjectRelationPopulationOrdering.Producer,
                [Row(context, "2.0.0", integration: false)],
                continuation: null),
            authority);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void CompleteProducerRejectsIncompleteCoverage(
        int unavailable,
        int limited)
    {
        Assert.Throws<ArgumentException>(
            () => new SubjectRelationProducerOutcome(
                Producer,
                SubjectRelationProducerDisposition.Complete,
                new(
                    considered: 2,
                    examined: 1,
                    excluded: 1 - unavailable - limited,
                    unavailable,
                    limited),
                [Relationship]));
    }

    [Fact]
    public void CompleteProducerRejectsCompletionDiagnostic()
    {
        Assert.Throws<ArgumentException>(
            () => new SubjectRelationProducerOutcome(
                Producer,
                SubjectRelationProducerDisposition.Complete,
                new(1, 1, 0, 0, 0),
                [Relationship],
                [
                    SubjectRelationProducerDiagnostic.Create(
                        SubjectRelationProducerDiagnosticKind.Limit,
                        new object()),
                ]));
    }

    [Fact]
    public void EvidenceMustUseExactCandidatePopulation()
    {
        Context context = CreateContext();
        SubjectRelationPopulationAuthority other =
            SubjectRelationPopulationAuthority.Capture(
                context.Focus.Workspace,
                new object());
        SubjectRelationPopulationEvidence evidence =
            new(
                other,
                [
                    ProducerOutcome(
                        SubjectRelationProducerDisposition.Complete),
                ]);

        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                InspectionRequest(
                    context,
                    RowsRequest(10)),
                evidence,
                count: null,
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [],
                    continuation: null)));
    }

    [Fact]
    public void RowsMustBelongToProducerEvidence()
    {
        Context context = CreateContext();
        InspectionGraphRelationshipDescriptor foreignRelationship =
            CreateRelationship("test.foreign");
        SubjectRelationRow row = Row(
            context,
            "2.0.0",
            integration: false,
            relationship: foreignRelationship);

        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                InspectionRequest(
                    context,
                    RowsRequest(10)),
                PopulationEvidence(context, complete: true),
                count: null,
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [row],
                    continuation: null)));
    }

    [Theory]
    [InlineData(SubjectRelationProducerDisposition.Unavailable)]
    [InlineData(SubjectRelationProducerDisposition.Failed)]
    public void NonUsableProducerCannotPublishRowsBesideUsableProducer(
        SubjectRelationProducerDisposition disposition)
    {
        Context context = CreateContext();
        InspectionGraphRelationshipDescriptor nonUsableRelationship =
            CreateRelationship("test.non-usable");
        SubjectRelationRow row = Row(
            context,
            "2.0.0",
            integration: false,
            relationship: nonUsableRelationship);
        var nonUsableProducer =
            new InspectionQuery<int>(
                "test.subject-relations.non-usable",
                InspectionCost.NetworkFree);
        SubjectRelationPopulationEvidence evidence =
            new(
                context.Population,
                [
                    ProducerOutcome(
                        SubjectRelationProducerDisposition.Complete),
                    new(
                        nonUsableProducer,
                        disposition,
                        new(1, 0, 0, 1, 0),
                        [nonUsableRelationship]),
                ]);

        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                InspectionRequest(
                    context,
                    RowsRequest(10)),
                evidence,
                count: null,
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [row],
                    continuation: null)));
    }

    [Fact]
    public void RowsMustUseExactFocusPopulationCorrespondence()
    {
        Context context = CreateContext();
        SubjectRelationPopulationAuthority otherPopulation =
            SubjectRelationPopulationAuthority.Capture(
                context.Focus.Workspace,
                new object());
        SubjectRelationRow row = Row(
            context,
            "2.0.0",
            integration: false,
            population: otherPopulation);

        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                InspectionRequest(
                    context,
                    RowsRequest(10)),
                PopulationEvidence(context, complete: true),
                count: null,
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [row],
                    continuation: null)));
    }

    [Fact]
    public void RowsRejectRepeatedCanonicalRelation()
    {
        Context context = CreateContext();
        SubjectRelationRow first = Row(
            context,
            "2.0.0",
            integration: false);
        SubjectRelationRow duplicate = Row(
            context,
            "2.0.0",
            integration: true);
        SubjectRelationsInspectionRequest request =
            InspectionRequest(
                context,
                new(
                    new(),
                    new SubjectRelationPopulationCountRequest(),
                    new SubjectRelationPopulationRowsRequest(10)));

        Assert.Throws<ArgumentException>(
            () => SubjectRelationsPopulationOperation.Settle(
                request,
                PopulationEvidence(context, complete: true),
                new SubjectRelationPopulationCountOutcome.Counted(2),
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [first, duplicate],
                    continuation: null)));
    }

    [Fact]
    public void RowsRetainDistinctCanonicalRelations()
    {
        Context context = CreateContext();
        SubjectRelationRow first = Row(
            context,
            "2.0.0",
            integration: false);
        SubjectRelationRow second = Row(
            context,
            "3.0.0",
            integration: false);
        SubjectRelationPopulationResult settled =
            SubjectRelationsPopulationOperation.Settle(
                InspectionRequest(
                    context,
                    new(
                        new(),
                        new SubjectRelationPopulationCountRequest(),
                        new SubjectRelationPopulationRowsRequest(10))),
                PopulationEvidence(context, complete: true),
                new SubjectRelationPopulationCountOutcome.Counted(2),
                new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    [first, second],
                    continuation: null));

        Assert.Equal(
            [first, second],
            Assert.IsType<
                SubjectRelationPopulationRowsOutcome.Read>(
                    settled.Rows).Items);
    }

    private static SubjectRelationsInspectionRequest InspectionRequest(
        Context context,
        SubjectRelationPopulationRequest request) =>
        new(
            SubjectRelationsRouteKind.Package,
            context.Focus,
            context.Population,
            request);

    private static PortableQueryTerm Term(string key, string value) =>
        new(key, PortableQueryOperator.Equal, value);

    private static SubjectRelationPopulationRequest RowsRequest(
        int maximumRows,
        SubjectRelationPopulationSelection? selection = null) =>
        new(
            selection ?? new(),
            rows: new(maximumRows));

    private static SubjectRelationPopulationEvidence PopulationEvidence(
        Context context,
        bool complete) =>
        new(
            context.Population,
            [
                ProducerOutcome(
                    complete
                        ? SubjectRelationProducerDisposition.Complete
                        : SubjectRelationProducerDisposition.Partial),
            ]);

    private static SubjectRelationProducerOutcome ProducerOutcome(
        SubjectRelationProducerDisposition disposition) =>
        new(
            Producer,
            disposition,
            disposition switch
            {
                SubjectRelationProducerDisposition.Complete =>
                    new(1, 1, 0, 0, 0),
                SubjectRelationProducerDisposition.Partial =>
                    new(2, 1, 0, 0, 1),
                SubjectRelationProducerDisposition.Unavailable =>
                    new(1, 0, 0, 1, 0),
                SubjectRelationProducerDisposition.Failed =>
                    new(1, 0, 0, 1, 0),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(disposition)),
            },
            [Relationship]);

    private static SubjectRelationRow Row(
        Context context,
        string targetVersion,
        bool integration,
        object? occurrenceIdentity = null,
        object? correspondenceEvidence = null,
        object? integrationEvidence = null,
        InspectionGraphRelationshipDescriptor? relationship = null,
        SubjectRelationPopulationAuthority? population = null)
    {
        relationship ??= Relationship;
        population ??= context.Population;
        InspectionGraphSubject source =
            InspectionGraphSubject.ForRealizedPackage(
                context.Coordinate);
        InspectionGraphSubject target =
            InspectionGraphSubject.ForRealizedPackage(
                Coordinate(targetVersion));
        InspectionGraphOccurrence occurrence = Occurrence(
            0,
            source,
            target,
            occurrenceIdentity ?? new object(),
            relationship);
        SubjectRelationFocusCorrespondence correspondence =
            SubjectRelationFocusCorrespondence.Create(
                context.Focus,
                population,
                source,
                InspectionGraphEndpointRole.Source,
                correspondenceEvidence ?? new object());
        SubjectRelationIntegrationAssociation[] associations =
            integration
                ?
                [
                    SubjectRelationIntegrationAssociation.Create(
                        IntegrationConceptCatalog.HttpClient,
                        IntegrationConceptCatalog.HttpClient
                            .ProducerPolicies[0],
                        WorkspaceEcosystemRegistrationId.Create(
                            "ecosystem.dotnet"),
                        integrationEvidence ?? new object()),
                ]
                : [];
        return new(
            SubjectRelationForm.PackageDependency,
            SubjectRelationEvidenceKind.Declaration,
            relationship,
            source,
            target,
            correspondence,
            [occurrence],
            associations);
    }

    private static InspectionGraphOccurrence Occurrence(
        int id,
        InspectionGraphSubject source,
        InspectionGraphSubject target,
        object identity,
        InspectionGraphRelationshipDescriptor? relationship = null) =>
        new(
            id,
            relationship ?? Relationship,
            source,
            target,
            new OccurrenceEvidence(identity),
            []);

    private static Context CreateContext()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectTestData.PackageContext package =
            StructuralSubjectTestData.Package(coordinate);
        SubjectRelationPopulationAuthority population =
            SubjectRelationPopulationAuthority.Capture(
                package.Workspace,
                new object());
        return new(coordinate, package.Subject, population);
    }

    private static RealizedMemberCoordinate.Package Coordinate(
        string version) =>
        new(
            "sample.package",
            version,
            "nuget-org",
            "net11.0",
            runtimeIdentifier: null);

    private static WorkspaceContextMember Library(
        RealizedMemberCoordinate.Package coordinate)
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Sample.Library",
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

    private sealed record Context(
        RealizedMemberCoordinate.Package Coordinate,
        StructuralSubjectIdentity.PackageSubject Focus,
        SubjectRelationPopulationAuthority Population);

    private sealed record OccurrenceEvidence(object Identity)
        : IInspectionGraphOccurrenceEvidence
    {
        public InspectionGraphEvidenceDescriptor Descriptor => Evidence;
    }

    private sealed class OccurrenceIdentityProjection :
        InspectionGraphOccurrenceIdentityProjection
    {
        public override object Project(
            InspectionGraphOccurrence occurrence) =>
            Assert.IsType<OccurrenceEvidence>(occurrence.Evidence).Identity;
    }
}
