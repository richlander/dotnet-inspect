using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace DotnetInspector.Queries.Tests;

public sealed class AnnotatedTypeMapPlanningTests
{
    private static readonly AnnotatedTypeMapFamily Alloc =
        AnnotatedTypeMapFamily.Finding("alloc", AnnotatedTypeMapFindingClassification.LocalInstruction);
    private static readonly AnnotatedTypeMapFamily Unsafe =
        AnnotatedTypeMapFamily.Finding("unsafe", AnnotatedTypeMapFindingClassification.LocalInstruction);

    [Fact]
    public void OneVersionRowsDemandsEveryMapCarriedFamilyAndTheSelectedFindings()
    {
        Fixture fixture = new();

        AnnotatedTypeMapPlan plan = Accept(fixture.Catalog.Resolve(
            MapRequest(AnnotatedTypeMapTerminal.Rows, versionPair: false, Alloc),
            fixture.Queries));

        Assert.Equal(AnnotatedTypeMapResultKind.OneVersionRows, plan.ResultKind);
        Assert.Equal(
            [
                (AnnotatedTypeMapFamily.ApiShape, AnnotatedTypeMapDemandReason.MapCarried),
                (AnnotatedTypeMapFamily.BodyShape, AnnotatedTypeMapDemandReason.MapCarried),
                (AnnotatedTypeMapFamily.FactStream, AnnotatedTypeMapDemandReason.MapCarried),
                (Alloc, AnnotatedTypeMapDemandReason.Selected),
            ],
            plan.Trail.Select(entry => (entry.Family, entry.Reason)));
        Assert.Equal(
            [fixture.ApiRows, fixture.BodyRows, fixture.StreamRows, fixture.AllocRows],
            plan.Queries);
        Assert.Equal(InspectionCost.Moderated, plan.Cost);
    }

    [Theory]
    [InlineData(AnnotatedTypeMapTerminal.Count, false, true, "alloc")]
    [InlineData(AnnotatedTypeMapTerminal.Exists, false, true, "alloc")]
    [InlineData(AnnotatedTypeMapTerminal.Rows, true, true, "shape.api shape.body fact-stream alloc")]
    [InlineData(AnnotatedTypeMapTerminal.Rows, true, false, "shape.api shape.body")]
    [InlineData(AnnotatedTypeMapTerminal.Count, true, true, "shape.api shape.body")]
    [InlineData(AnnotatedTypeMapTerminal.Exists, true, true, "shape.api shape.body")]
    [InlineData(AnnotatedTypeMapTerminal.Exists, true, false, "shape.api shape.body")]
    public void EachResultKindDemandsExactlyWhatItReads(
        AnnotatedTypeMapTerminal terminal,
        bool versionPair,
        bool selectFinding,
        string demanded)
    {
        Fixture fixture = new();

        AnnotatedTypeMapPlan plan = Accept(fixture.Catalog.Resolve(
            MapRequest(terminal, versionPair, selectFinding ? [Alloc] : []),
            fixture.Queries));

        Assert.Equal(
            demanded.Split(' '),
            plan.Trail.Where(entry => entry.Query is not null).Select(entry => entry.Family.Id));
        Assert.Equal(
            plan.Trail.Where(entry => entry.Query is not null).Select(entry => entry.Query!).Distinct(),
            plan.Queries);
    }

    [Fact]
    public void MapDiffExistsKeepsItsFindingSelectionAndBuildsNoCensus()
    {
        Fixture fixture = new();

        AnnotatedTypeMapPlan exists = Accept(fixture.Catalog.Resolve(
            MapRequest(AnnotatedTypeMapTerminal.Exists, versionPair: true, Alloc),
            fixture.Queries));
        AnnotatedTypeMapPlan rows = Accept(fixture.Catalog.Resolve(
            MapRequest(AnnotatedTypeMapTerminal.Rows, versionPair: true, Alloc, Unsafe),
            fixture.Queries));
        AnnotatedTypeMapPlan rowsWithoutFindings = Accept(fixture.Catalog.Resolve(
            MapRequest(AnnotatedTypeMapTerminal.Rows, versionPair: true),
            fixture.Queries));

        AnnotatedTypeMapPlanTrailEntry alloc = Assert.Single(exists.Trail, entry => entry.Family == Alloc);
        Assert.Equal(AnnotatedTypeMapDemandReason.SelectedNotDemanded, alloc.Reason);
        Assert.Null(alloc.Query);
        Assert.DoesNotContain(fixture.Census, fixture.Queries.ExpandRequired(exists.Queries));
        Assert.DoesNotContain(fixture.Census, fixture.Queries.ExpandRequired(rowsWithoutFindings.Queries));
        Assert.Single(fixture.Queries.Plan(rows.Queries).Queries, query => query == fixture.Census);
    }

    [Fact]
    public void FamilyContributionRequestDemandsOnlyItsFamiliesAtMemberScope()
    {
        Fixture fixture = new();

        AnnotatedTypeMapPlan plan = Accept(fixture.Catalog.Resolve(
            new AnnotatedTypeMapPlanningRequest(
                AnnotatedTypeMapRequestForm.Contribution,
                TypeFocus(),
                versionPair: true,
                [AnnotatedTypeMapFamily.ApiShape],
                memberKeys: [Key(1), Key(2)]),
            fixture.Queries));

        Assert.Equal(AnnotatedTypeMapResultKind.Contribution, plan.ResultKind);
        AnnotatedTypeMapPlanTrailEntry entry = Assert.Single(plan.Trail);
        Assert.Equal(AnnotatedTypeMapDemandReason.Selected, entry.Reason);
        Assert.Equal([fixture.ApiContribution], plan.Queries);
        Assert.Equal(2, plan.Request.MemberKeys!.Value.Length);
    }

    [Fact]
    public void MemberScopeNeverWidensToTheWholeType()
    {
        Fixture fixture = new();

        AnnotatedTypeMapPlanningFailure failure = Reject(fixture.Catalog.Resolve(
            new AnnotatedTypeMapPlanningRequest(
                AnnotatedTypeMapRequestForm.Contribution,
                TypeFocus(),
                versionPair: false,
                [AnnotatedTypeMapFamily.BodyShape],
                memberKeys: [Key(1)]),
            fixture.Queries));

        Assert.Equal(AnnotatedTypeMapPlanningFailureKind.UnsupportedFocus, failure.Kind);
        Assert.Equal("shape.body", failure.FamilyId);
    }

    [Fact]
    public void MapRequestRejectsASelectedMapCarriedFamily()
    {
        Fixture fixture = new();

        AnnotatedTypeMapPlanningFailure failure = Reject(fixture.Catalog.Resolve(
            MapRequest(AnnotatedTypeMapTerminal.Rows, versionPair: true, AnnotatedTypeMapFamily.ApiShape),
            fixture.Queries));

        Assert.Equal(AnnotatedTypeMapPlanningFailureKind.MapCarriedSelected, failure.Kind);
    }

    [Fact]
    public void MapRequestNeedsEveryMapCarriedFamilyItReadsRegistered()
    {
        var api = Query("api.rows");
        var allocRows = Query("alloc.rows");
        AnnotatedTypeMapCatalog catalog = new AnnotatedTypeMapRegistry()
            .Add(new(AnnotatedTypeMapFamily.ApiShape, AnnotatedTypeMapFocusSupport.Type, new(mapDiffRows: api)))
            .Add(new(Alloc, AnnotatedTypeMapFocusSupport.Type, new(mapDiffRows: allocRows)))
            .Compile();

        AnnotatedTypeMapPlanningFailure failure = Reject(catalog.Resolve(
            MapRequest(AnnotatedTypeMapTerminal.Rows, versionPair: true, Alloc),
            QueryCatalog(api, allocRows)));

        Assert.Equal(AnnotatedTypeMapPlanningFailureKind.UnknownFamily, failure.Kind);
        Assert.Equal("shape.body", failure.FamilyId);
    }

    [Fact]
    public void LookalikeFamilyAndLookalikeQueryDoNotResolve()
    {
        Fixture fixture = new();
        AnnotatedTypeMapFamily lookalike =
            AnnotatedTypeMapFamily.Finding("alloc", AnnotatedTypeMapFindingClassification.LocalInstruction);

        AnnotatedTypeMapPlanningFailure unknown = Reject(fixture.Catalog.Resolve(
            MapRequest(AnnotatedTypeMapTerminal.Rows, versionPair: false, lookalike),
            fixture.Queries));
        AnnotatedTypeMapPlanningFailure unavailable = Reject(fixture.Catalog.Resolve(
            MapRequest(AnnotatedTypeMapTerminal.Rows, versionPair: false, Alloc),
            QueryCatalog(Query("shape.api.rows"))));

        Assert.Equal(AnnotatedTypeMapPlanningFailureKind.UnknownFamily, unknown.Kind);
        Assert.Equal(AnnotatedTypeMapPlanningFailureKind.QueryUnavailable, unavailable.Kind);
    }

    [Fact]
    public void UnsupportedResultKindIsRejected()
    {
        var rows = Query("alloc.rows");
        AnnotatedTypeMapCatalog catalog = new AnnotatedTypeMapRegistry()
            .Add(new(Alloc, AnnotatedTypeMapFocusSupport.Type, new(oneVersionRows: rows)))
            .Compile();

        AnnotatedTypeMapPlanningFailure failure = Reject(catalog.Resolve(
            MapRequest(AnnotatedTypeMapTerminal.Count, versionPair: false, Alloc),
            QueryCatalog(rows)));

        Assert.Equal(AnnotatedTypeMapPlanningFailureKind.UnsupportedResultKind, failure.Kind);
    }

    [Fact]
    public void FamiliesSharingOneAdapterProduceOneDemandEntry()
    {
        var shared = Query("local.rows");
        AnnotatedTypeMapCatalog catalog = new AnnotatedTypeMapRegistry()
            .Add(new(Alloc, AnnotatedTypeMapFocusSupport.Type, new(contribution: shared)))
            .Add(new(Unsafe, AnnotatedTypeMapFocusSupport.Type, new(contribution: shared)))
            .Compile();

        AnnotatedTypeMapPlan plan = Accept(catalog.Resolve(
            new AnnotatedTypeMapPlanningRequest(
                AnnotatedTypeMapRequestForm.Contribution, TypeFocus(), versionPair: false, [Unsafe, Alloc]),
            QueryCatalog(shared)));

        Assert.Equal([Alloc, Unsafe], plan.Trail.Select(entry => entry.Family));
        Assert.Equal([shared], plan.Queries);
    }

    [Fact]
    public void RegistrationRejectsWhatTheMapDoesNotAdmitOrNoResultReads()
    {
        var query = Query("q");

        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapRegistration(
            AnnotatedTypeMapFamily.Finding("cost.method", AnnotatedTypeMapFindingClassification.MemberHeader),
            AnnotatedTypeMapFocusSupport.Type,
            new(oneVersionRows: query)));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapRegistration(
            AnnotatedTypeMapFamily.ApiShape, AnnotatedTypeMapFocusSupport.Type, new(oneVersionCount: query)));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapRegistration(
            AnnotatedTypeMapFamily.FactStream, AnnotatedTypeMapFocusSupport.Type, new(mapDiffExists: query)));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapRegistration(
            Alloc, AnnotatedTypeMapFocusSupport.Type, new(mapDiffCount: query)));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapAdapterQueries(
            oneVersionRows: query, mapDiffRows: query));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapAdapterQueries());
    }

    [Fact]
    public void RegistryRejectsRepeatedFamiliesAndIds()
    {
        var query = Query("q");
        var registry = new AnnotatedTypeMapRegistry()
            .Add(new(Alloc, AnnotatedTypeMapFocusSupport.Type, new(contribution: query)));

        Assert.Throws<ArgumentException>(() => registry.Add(
            new(Alloc, AnnotatedTypeMapFocusSupport.Type, new(contribution: query))));
        Assert.Throws<ArgumentException>(() => registry.Add(
            new(
                AnnotatedTypeMapFamily.Finding("alloc", AnnotatedTypeMapFindingClassification.LocalInstruction),
                AnnotatedTypeMapFocusSupport.Type,
                new(contribution: query))));
    }

    [Fact]
    public void RequestBoundaryRejectsInvalidConstruction()
    {
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapPlanningRequest(
            AnnotatedTypeMapRequestForm.Map, PackageFocus(), false, [Alloc], AnnotatedTypeMapTerminal.Rows));
        Assert.Throws<ArgumentException>(() => MapRequest(AnnotatedTypeMapTerminal.Rows, versionPair: false));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapPlanningRequest(
            AnnotatedTypeMapRequestForm.Map, TypeFocus(), true, [Alloc]));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapPlanningRequest(
            AnnotatedTypeMapRequestForm.Contribution, TypeFocus(), false, [Alloc], AnnotatedTypeMapTerminal.Rows));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapPlanningRequest(
            AnnotatedTypeMapRequestForm.Contribution, TypeFocus(), false, []));
        Assert.Throws<ArgumentException>(() => MapRequest(AnnotatedTypeMapTerminal.Rows, versionPair: false, Alloc, Alloc));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapPlanningRequest(
            AnnotatedTypeMapRequestForm.Contribution, TypeFocus(), false, [Alloc], memberKeys: []));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapPlanningRequest(
            AnnotatedTypeMapRequestForm.Contribution, TypeFocus(), false, [Alloc], memberKeys: [Key(1), Key(1)]));
    }

    [Fact]
    public void RegistrySnapshotAndPlanTrailAreStableLines()
    {
        Fixture fixture = new();

        Assert.Equal(
            [
                "shape.api\tApiShape\t-\tType, Members\tOneVersionRows=shape.api.rows\tMapDiffRows=shape.api.diff-rows\tMapDiffCount=shape.api.diff-count\tMapDiffExists=shape.api.diff-exists\tContribution=shape.api.contribution",
                "shape.body\tBodyShape\t-\tType\tOneVersionRows=shape.body.rows\tMapDiffRows=shape.body.diff-rows\tMapDiffCount=shape.body.diff-count\tMapDiffExists=shape.body.diff-exists",
                "fact-stream\tFactStream\t-\tType\tOneVersionRows=fact-stream.rows\tMapDiffRows=fact-stream.diff-rows",
                "alloc\tFinding\tLocalInstruction\tType\tOneVersionRows=alloc.rows\tOneVersionCount=alloc.count\tOneVersionExists=alloc.exists\tMapDiffRows=alloc.diff-rows",
                "unsafe\tFinding\tLocalInstruction\tType\tOneVersionRows=unsafe.rows\tMapDiffRows=unsafe.diff-rows",
            ],
            fixture.Catalog.Describe());

        AnnotatedTypeMapPlan plan = Accept(fixture.Catalog.Resolve(
            MapRequest(AnnotatedTypeMapTerminal.Exists, versionPair: true, Alloc),
            fixture.Queries));
        Assert.Equal(
            [
                "result-kind=MapDiffExists",
                "shape.api\tMapCarried\tshape.api.diff-exists",
                "shape.body\tMapCarried\tshape.body.diff-exists",
                "alloc\tSelectedNotDemanded\t-",
            ],
            plan.Explain());
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            AnnotatedTypeMapCatalog catalog = new AnnotatedTypeMapRegistry()
                .Add(new(
                    AnnotatedTypeMapFamily.ApiShape,
                    AnnotatedTypeMapFocusSupport.Type | AnnotatedTypeMapFocusSupport.Members,
                    new(
                        oneVersionRows: ApiRows,
                        mapDiffRows: Query("shape.api.diff-rows"),
                        mapDiffCount: Query("shape.api.diff-count"),
                        mapDiffExists: Query("shape.api.diff-exists"),
                        contribution: ApiContribution)))
                .Add(new(
                    AnnotatedTypeMapFamily.BodyShape,
                    AnnotatedTypeMapFocusSupport.Type,
                    new(
                        oneVersionRows: BodyRows,
                        mapDiffRows: Query("shape.body.diff-rows"),
                        mapDiffCount: Query("shape.body.diff-count"),
                        mapDiffExists: Query("shape.body.diff-exists"))))
                .Add(new(
                    AnnotatedTypeMapFamily.FactStream,
                    AnnotatedTypeMapFocusSupport.Type,
                    new(oneVersionRows: StreamRows, mapDiffRows: Query("fact-stream.diff-rows"))))
                .Add(new(
                    Alloc,
                    AnnotatedTypeMapFocusSupport.Type,
                    new(
                        oneVersionRows: AllocRows,
                        oneVersionCount: Query("alloc.count"),
                        oneVersionExists: Query("alloc.exists"),
                        mapDiffRows: Query("alloc.diff-rows"))))
                .Add(new(
                    Unsafe,
                    AnnotatedTypeMapFocusSupport.Type,
                    new(oneVersionRows: Query("unsafe.rows"), mapDiffRows: Query("unsafe.diff-rows"))))
                .Compile();
            Catalog = catalog;

            var registry = new InspectionQueryRegistry<object>();
            registry.Add(Census, static _ => throw new InvalidOperationException("Not executed."));
            foreach (AnnotatedTypeMapRegistration registration in catalog.Registrations)
            {
                bool readsCensus = registration.Family.Kind
                    is AnnotatedTypeMapFamilyKind.FactStream or AnnotatedTypeMapFamilyKind.Finding;
                foreach (AnnotatedTypeMapResultKind kind in registration.Queries.ResultKinds)
                {
                    InspectionQuery<AnnotatedTypeMapContribution> query = registration.Queries.For(kind)!;
                    if (registry.RegisteredQueries.Contains(query))
                        continue;
                    registry.Add(
                        query,
                        static _ => throw new InvalidOperationException("Planning tests do not execute adapters."),
                        readsCensus ? [Census] : []);
                }
            }
            Queries = registry.Compile();
        }

        public InspectionQuery<int> Census { get; } = new("fact-census", InspectionCost.Moderated);

        public InspectionQuery<AnnotatedTypeMapContribution> ApiRows { get; } = Query("shape.api.rows");

        public InspectionQuery<AnnotatedTypeMapContribution> ApiContribution { get; } = Query("shape.api.contribution");

        public InspectionQuery<AnnotatedTypeMapContribution> BodyRows { get; } = Query("shape.body.rows");

        public InspectionQuery<AnnotatedTypeMapContribution> StreamRows { get; } = Query("fact-stream.rows");

        public InspectionQuery<AnnotatedTypeMapContribution> AllocRows { get; } = Query("alloc.rows");

        public AnnotatedTypeMapCatalog Catalog { get; }

        public InspectionQueryCatalog<object> Queries { get; }
    }

    private static AnnotatedTypeMapPlanningRequest MapRequest(
        AnnotatedTypeMapTerminal terminal,
        bool versionPair,
        params AnnotatedTypeMapFamily[] families) =>
        new(AnnotatedTypeMapRequestForm.Map, TypeFocus(), versionPair, families, terminal);

    private static AnnotatedTypeMapPlan Accept(AnnotatedTypeMapPlanningResult result) =>
        Assert.IsType<AnnotatedTypeMapPlanningResult.Accepted>(result).Plan;

    private static AnnotatedTypeMapPlanningFailure Reject(AnnotatedTypeMapPlanningResult result) =>
        Assert.IsType<AnnotatedTypeMapPlanningResult.Rejected>(result).Failure;

    private static InspectionQuery<AnnotatedTypeMapContribution> Query(string name) =>
        new(name, InspectionCost.NetworkFree);

    private static AnnotatedTypeMapMemberKey Key(byte value) =>
        new(OpaqueByteRange.Copy([value]));

    private static IInspectionQueryCatalog QueryCatalog(
        params InspectionQuery<AnnotatedTypeMapContribution>[] queries)
    {
        var registry = new InspectionQueryRegistry<object>();
        foreach (InspectionQuery<AnnotatedTypeMapContribution> query in queries)
        {
            registry.Add(
                query,
                static _ => throw new InvalidOperationException("Planning tests do not execute adapters."));
        }
        return registry.Compile();
    }

    private static SubjectRelationFocusAuthority PackageFocus() => AnnotatedTypeMapTestData.PackageFocus();

    private static SubjectRelationFocusAuthority TypeFocus() => AnnotatedTypeMapTestData.TypeFocus();
}
