using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Queries.Tests;

public sealed class AnnotatedTypeMapGenerationTests
{
    private static readonly AnnotatedTypeMapFamily Alloc =
        AnnotatedTypeMapFamily.Finding("alloc", AnnotatedTypeMapFindingClassification.LocalInstruction);
    private static readonly AnnotatedTypeMapFamily Unsafe =
        AnnotatedTypeMapFamily.Finding("unsafe", AnnotatedTypeMapFindingClassification.LocalInstruction);

    [Fact]
    public void OneVersionContributionResultHoldsEachFamilyByExactInstance()
    {
        var focus = AnnotatedTypeMapTestData.TypeFocus();
        var api = Query("api");
        Setup setup = new(
            new AnnotatedTypeMapRegistry()
                .Add(new(AnnotatedTypeMapFamily.ApiShape, AnnotatedTypeMapFocusSupport.Type, new(contribution: api))),
            Contribution(api, side => new(focus, AnnotatedTypeMapTypePresence.Present, [ApiContent([Key(1)])])));

        AnnotatedTypeMapPlan plan = setup.Plan(focus, versionPair: false, [AnnotatedTypeMapFamily.ApiShape]);
        AnnotatedTypeMapContributionResult result =
            AnnotatedTypeMapGeneration.SettleContributions(plan, setup.Run(plan, AnnotatedTypeMapSide.Single));

        Assert.Equal([AnnotatedTypeMapSide.Single], result.Sides);
        Assert.Equal(AnnotatedTypeMapTypePresence.Present, result.Presence[AnnotatedTypeMapSide.Single]);
        AnnotatedTypeMapApiShapeContent content = Assert.IsType<AnnotatedTypeMapApiShapeContent>(
            result.Content(AnnotatedTypeMapFamily.ApiShape, AnnotatedTypeMapSide.Single));
        Assert.True(content.TypeEntry!.Content!.Admitted);
        Assert.Equal([Key(1)], content.Keys);
        Assert.Empty(result.AbsentKeys);
    }

    [Fact]
    public void FamiliesSharingOneAdapterSettleFromItsOneContribution()
    {
        var focus = AnnotatedTypeMapTestData.TypeFocus();
        var shared = Query("local");
        Setup setup = new(
            new AnnotatedTypeMapRegistry()
                .Add(new(Alloc, AnnotatedTypeMapFocusSupport.Type, new(contribution: shared)))
                .Add(new(Unsafe, AnnotatedTypeMapFocusSupport.Type, new(contribution: shared))),
            Contribution(shared, side => new(
                focus,
                AnnotatedTypeMapTypePresence.Present,
                [FindingContent(Alloc, []), FindingContent(Unsafe, [])])));

        AnnotatedTypeMapPlan plan = setup.Plan(focus, versionPair: true, [Alloc, Unsafe]);
        AnnotatedTypeMapContributionResult result = AnnotatedTypeMapGeneration.SettleContributions(
            plan,
            setup.Run(plan, AnnotatedTypeMapSide.Before),
            setup.Run(plan, AnnotatedTypeMapSide.After));

        Assert.Equal([AnnotatedTypeMapSide.Before, AnnotatedTypeMapSide.After], result.Sides);
        Assert.Same(Alloc, result.Content(Alloc, AnnotatedTypeMapSide.After).Family);
        Assert.Same(Unsafe, result.Content(Unsafe, AnnotatedTypeMapSide.Before).Family);
    }

    [Fact]
    public void AbsentKeysAreUnavailablePerRequestedFamilyAndOneSidedKeysAreNot()
    {
        var focus = AnnotatedTypeMapTestData.TypeFocus();
        var shared = Query("local");
        Setup setup = new(
            new AnnotatedTypeMapRegistry()
                .Add(new(Alloc, AnnotatedTypeMapFocusSupport.Members, new(contribution: shared)))
                .Add(new(Unsafe, AnnotatedTypeMapFocusSupport.Members, new(contribution: shared))),
            Contribution(shared, side => new(
                focus,
                AnnotatedTypeMapTypePresence.Present,
                side == AnnotatedTypeMapSide.Before
                    ? [FindingContent(Alloc, [Key(1)]), FindingContent(Unsafe, [Key(1)])]
                    : [FindingContent(Alloc, []), FindingContent(Unsafe, [])])));

        AnnotatedTypeMapPlan plan = setup.Plan(focus, versionPair: true, [Alloc, Unsafe], [Key(1), Key(2)]);
        AnnotatedTypeMapContributionResult result = AnnotatedTypeMapGeneration.SettleContributions(
            plan,
            setup.Run(plan, AnnotatedTypeMapSide.Before),
            setup.Run(plan, AnnotatedTypeMapSide.After));

        Assert.Equal(
            [(Alloc, Key(2)), (Unsafe, Key(2))],
            result.AbsentKeys.Select(entry => (entry.Family, entry.Key)));
        Assert.All(result.AbsentKeys, entry =>
        {
            Assert.Equal(AnnotatedTypeMapOutcomeKind.Unavailable, entry.Outcome.Kind);
            Assert.False(entry.Outcome.IsSettled);
        });
        Assert.Equal([Key(1)], result.Content(Alloc, AnnotatedTypeMapSide.Before).Keys);
        Assert.Empty(result.Content(Alloc, AnnotatedTypeMapSide.After).Keys);
    }

    [Fact]
    public void OneVersionAbsentKeyIsUnavailable()
    {
        var focus = AnnotatedTypeMapTestData.TypeFocus();
        var api = Query("api");
        Setup setup = new(
            new AnnotatedTypeMapRegistry()
                .Add(new(AnnotatedTypeMapFamily.ApiShape, AnnotatedTypeMapFocusSupport.Members, new(contribution: api))),
            Contribution(api, side => new(focus, AnnotatedTypeMapTypePresence.Present, [ApiContent([])])));

        AnnotatedTypeMapPlan plan = setup.Plan(focus, versionPair: false, [AnnotatedTypeMapFamily.ApiShape], [Key(7)]);
        AnnotatedTypeMapContributionResult result =
            AnnotatedTypeMapGeneration.SettleContributions(plan, setup.Run(plan, AnnotatedTypeMapSide.Single));

        AnnotatedTypeMapAbsentKeyEntry entry = Assert.Single(result.AbsentKeys);
        Assert.Same(AnnotatedTypeMapFamily.ApiShape, entry.Family);
        Assert.Equal(Key(7), entry.Key);
    }

    [Fact]
    public void AbsentTypeContributesNoEntriesOnItsSide()
    {
        var focus = AnnotatedTypeMapTestData.TypeFocus();
        var api = Query("api");
        Setup setup = new(
            new AnnotatedTypeMapRegistry()
                .Add(new(AnnotatedTypeMapFamily.ApiShape, AnnotatedTypeMapFocusSupport.Type, new(contribution: api))),
            Contribution(api, side => side == AnnotatedTypeMapSide.Before
                ? new(focus, AnnotatedTypeMapTypePresence.Absent, [ApiContent([], typeEntry: false)])
                : new(focus, AnnotatedTypeMapTypePresence.Present, [ApiContent([Key(1)])])));

        AnnotatedTypeMapPlan plan = setup.Plan(focus, versionPair: true, [AnnotatedTypeMapFamily.ApiShape]);
        AnnotatedTypeMapContributionResult result = AnnotatedTypeMapGeneration.SettleContributions(
            plan,
            setup.Run(plan, AnnotatedTypeMapSide.Before),
            setup.Run(plan, AnnotatedTypeMapSide.After));

        Assert.Equal(AnnotatedTypeMapTypePresence.Absent, result.Presence[AnnotatedTypeMapSide.Before]);
        Assert.Equal(AnnotatedTypeMapTypePresence.Present, result.Presence[AnnotatedTypeMapSide.After]);
        Assert.False(result.Content(AnnotatedTypeMapFamily.ApiShape, AnnotatedTypeMapSide.Before).HasTypeEntry);
    }

    [Fact]
    public void CorrespondenceFailuresAreContractExceptions()
    {
        var focus = AnnotatedTypeMapTestData.TypeFocus();
        var shared = Query("local");
        var registry = new AnnotatedTypeMapRegistry()
            .Add(new(Alloc, AnnotatedTypeMapFocusSupport.Type | AnnotatedTypeMapFocusSupport.Members, new(contribution: shared)))
            .Add(new(Unsafe, AnnotatedTypeMapFocusSupport.Type | AnnotatedTypeMapFocusSupport.Members, new(contribution: shared)));

        void Fails(
            Func<AnnotatedTypeMapSide, AnnotatedTypeMapContribution> make,
            IReadOnlyList<AnnotatedTypeMapMemberKey>? keys = null)
        {
            Setup setup = new(registry, Contribution(shared, make));
            AnnotatedTypeMapPlan plan = setup.Plan(focus, versionPair: false, [Alloc, Unsafe], keys);
            Assert.Throws<AnnotatedTypeMapContractException>(() =>
                AnnotatedTypeMapGeneration.SettleContributions(plan, setup.Run(plan, AnnotatedTypeMapSide.Single)));
        }

        Fails(side => new(
            AnnotatedTypeMapTestData.TypeFocus(),
            AnnotatedTypeMapTypePresence.Present,
            [FindingContent(Alloc, []), FindingContent(Unsafe, [])]));
        Fails(side => new(focus, AnnotatedTypeMapTypePresence.Present, [FindingContent(Alloc, [])]));
        Fails(
            side => new(
                focus,
                AnnotatedTypeMapTypePresence.Present,
                [FindingContent(Alloc, [Key(9)]), FindingContent(Unsafe, [])]),
            [Key(1)]);
    }

    [Fact]
    public void ContributionsOfOneSideMustAgreeOnTypePresence()
    {
        var focus = AnnotatedTypeMapTestData.TypeFocus();
        var api = Query("api");
        var body = Query("body");
        Setup setup = new(
            new AnnotatedTypeMapRegistry()
                .Add(new(AnnotatedTypeMapFamily.ApiShape, AnnotatedTypeMapFocusSupport.Type, new(contribution: api)))
                .Add(new(AnnotatedTypeMapFamily.BodyShape, AnnotatedTypeMapFocusSupport.Type, new(contribution: body))),
            Contribution(api, side => new(focus, AnnotatedTypeMapTypePresence.Present, [ApiContent([])])),
            Contribution(body, side => new(
                focus,
                AnnotatedTypeMapTypePresence.Absent,
                [new AnnotatedTypeMapBodyShapeContent(AnnotatedTypeMapFamily.BodyShape, null, [])])));

        AnnotatedTypeMapPlan plan = setup.Plan(
            focus, versionPair: false, [AnnotatedTypeMapFamily.ApiShape, AnnotatedTypeMapFamily.BodyShape]);

        Assert.Throws<AnnotatedTypeMapContractException>(() =>
            AnnotatedTypeMapGeneration.SettleContributions(plan, setup.Run(plan, AnnotatedTypeMapSide.Single)));
    }

    [Fact]
    public void ContributionSettlementRejectsAMapPlanOrTheWrongSideCount()
    {
        var focus = AnnotatedTypeMapTestData.TypeFocus();
        var allocRows = Query("alloc.rows");
        var api = Query("api");
        Setup mapSetup = new(
            new AnnotatedTypeMapRegistry()
                .Add(new(Alloc, AnnotatedTypeMapFocusSupport.Type, new(oneVersionCount: allocRows))),
            Contribution(allocRows, side => new(focus, AnnotatedTypeMapTypePresence.Present, [FindingContent(Alloc, [])])));
        AnnotatedTypeMapPlan mapPlan = Assert.IsType<AnnotatedTypeMapPlanningResult.Accepted>(
            mapSetup.Catalog.Resolve(
                new AnnotatedTypeMapPlanningRequest(
                    AnnotatedTypeMapRequestForm.Map, focus, versionPair: false, [Alloc], AnnotatedTypeMapTerminal.Count),
                mapSetup.Queries)).Plan;
        Setup pairSetup = new(
            new AnnotatedTypeMapRegistry()
                .Add(new(AnnotatedTypeMapFamily.ApiShape, AnnotatedTypeMapFocusSupport.Type, new(contribution: api))),
            Contribution(api, side => new(focus, AnnotatedTypeMapTypePresence.Present, [ApiContent([])])));
        AnnotatedTypeMapPlan pairPlan = pairSetup.Plan(focus, versionPair: true, [AnnotatedTypeMapFamily.ApiShape]);

        Assert.Throws<AnnotatedTypeMapContractException>(() =>
            AnnotatedTypeMapGeneration.SettleContributions(mapPlan, mapSetup.Run(mapPlan, AnnotatedTypeMapSide.Single)));
        Assert.Throws<AnnotatedTypeMapContractException>(() =>
            AnnotatedTypeMapGeneration.SettleContributions(pairPlan, pairSetup.Run(pairPlan, AnnotatedTypeMapSide.Before)));
    }

    [Fact]
    public void ContentAndContributionRejectInvalidShapes()
    {
        var focus = AnnotatedTypeMapTestData.TypeFocus();

        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapEntry<AnnotatedTypeMapFindings>(
            AnnotatedTypeMapOutcome.Complete));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapEntry<AnnotatedTypeMapFindings>(
            new(AnnotatedTypeMapOutcomeKind.Failed, "boom"), new AnnotatedTypeMapFindings([])));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapFindingContent(
            AnnotatedTypeMapFamily.ApiShape, null, []));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapApiTypeFacts(admitted: true, projection: null));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapContribution(
            focus, AnnotatedTypeMapTypePresence.Absent, [FindingContent(Alloc, [Key(1)])]));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapContribution(
            focus, AnnotatedTypeMapTypePresence.Present, [FindingContent(Alloc, [], typeEntry: false)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapContribution(
            focus, AnnotatedTypeMapTypePresence.Present, [FindingContent(Alloc, []), FindingContent(Alloc, [])]));
        Assert.Throws<ArgumentException>(() => new AnnotatedTypeMapContribution(
            focus, AnnotatedTypeMapTypePresence.Present, []));
        Assert.True(AnnotatedTypeMapOutcome.NoApplicableInput.IsSettled);
    }

    private sealed class Setup
    {
        public Setup(
            AnnotatedTypeMapRegistry registry,
            params (InspectionQuery<AnnotatedTypeMapContribution> Query,
                Func<AnnotatedTypeMapSide, AnnotatedTypeMapContribution> Make)[] adapters)
        {
            Catalog = registry.Compile();
            var queries = new InspectionQueryRegistry<AnnotatedTypeMapSide>();
            foreach ((InspectionQuery<AnnotatedTypeMapContribution> query,
                Func<AnnotatedTypeMapSide, AnnotatedTypeMapContribution> make) in adapters)
            {
                queries.Add(query, make);
            }
            Queries = queries.Compile();
        }

        public AnnotatedTypeMapCatalog Catalog { get; }

        public InspectionQueryCatalog<AnnotatedTypeMapSide> Queries { get; }

        public AnnotatedTypeMapPlan Plan(
            SubjectRelationFocusAuthority focus,
            bool versionPair,
            AnnotatedTypeMapFamily[] families,
            IReadOnlyList<AnnotatedTypeMapMemberKey>? keys = null) =>
            Assert.IsType<AnnotatedTypeMapPlanningResult.Accepted>(Catalog.Resolve(
                new AnnotatedTypeMapPlanningRequest(
                    AnnotatedTypeMapRequestForm.Contribution, focus, versionPair, families, memberKeys: keys),
                Queries)).Plan;

        public InspectionQueryResults Run(AnnotatedTypeMapPlan plan, AnnotatedTypeMapSide side) =>
            Queries.Run(plan.Queries, side);
    }

    private static (InspectionQuery<AnnotatedTypeMapContribution>, Func<AnnotatedTypeMapSide, AnnotatedTypeMapContribution>)
        Contribution(
            InspectionQuery<AnnotatedTypeMapContribution> query,
            Func<AnnotatedTypeMapSide, AnnotatedTypeMapContribution> make) => (query, make);

    private static AnnotatedTypeMapApiShapeContent ApiContent(
        AnnotatedTypeMapMemberKey[] keys,
        bool typeEntry = true) =>
        new(
            AnnotatedTypeMapFamily.ApiShape,
            typeEntry
                ? new(AnnotatedTypeMapOutcome.Complete, new AnnotatedTypeMapApiTypeFacts(true, new ApiSurface()))
                : null,
            keys.Select(key => KeyValuePair.Create(
                key,
                new AnnotatedTypeMapEntry<AnnotatedTypeMapApiMemberFacts>(
                    AnnotatedTypeMapOutcome.Complete,
                    new AnnotatedTypeMapApiMemberFacts(new ApiMember { Name = "M" })))));

    private static AnnotatedTypeMapFindingContent FindingContent(
        AnnotatedTypeMapFamily family,
        AnnotatedTypeMapMemberKey[] keys,
        bool typeEntry = true) =>
        new(
            family,
            typeEntry ? new(AnnotatedTypeMapOutcome.NoApplicableInput) : null,
            keys.Select(key => KeyValuePair.Create(
                key,
                new AnnotatedTypeMapEntry<AnnotatedTypeMapFindings>(
                    AnnotatedTypeMapOutcome.Complete,
                    new AnnotatedTypeMapFindings([])))));

    private static InspectionQuery<AnnotatedTypeMapContribution> Query(string name) =>
        new(name, InspectionCost.NetworkFree);

    private static AnnotatedTypeMapMemberKey Key(byte value) =>
        new(OpaqueByteRange.Copy([value]));
}
