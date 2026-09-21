using DotnetInspector.QueryOperations;
using QuerySpace.Rows;

namespace DotnetInspector.PortableQueries.Tests;

public sealed class QueryOperationInfrastructureGateTests
{
    private const string PopulationRole = "population";
    private const string SelectedSubjectRole = "selected-subject";
    private const string PackageGrain = "package";
    private const string LibraryGrain = "library";
    private const string ResultsRowSet = "results";
    private const string DetailsRowSet = "details";
    private const string DependsBinding = "term.depends";
    private const string ToolBinding = "term.tool";
    private const string NameBinding = "term.name";
    private const string RankingBinding = "order.relevance";
    private const string SequenceBinding = "order.alphabetical";
    private const string NameOrderBinding = "order.name";
    private const string FullProfile = "full";
    private const string RestrictedProfile = "restricted";

    [Fact]
    public void ExecutableRegistrationsDriveRouteCapabilities()
    {
        var vocabulary = new TestVocabulary
        {
            AdmitsRankingStages = true,
            DeclaredDefaultRanking = TestVocabulary.RankingOrder,
        };
        QueryOperationDefinition<TestPredicate, TestPlan> operation =
            CreateOperation(vocabulary);
        QueryOperationRoute<TestPredicate, TestPlan> route =
            CreateRoute(operation, FullProfile);

        Assert.Equal("test.query", route.Capabilities.Vocabulary);
        Assert.Equal(
            [DependsBinding, NameBinding],
            route.Capabilities.Terms
                .Select(capability =>
                    capability.Binding.Identity)
                .ToArray());
        Assert.Equal(
            [
                PortableQueryOperator.Equal,
                PortableQueryOperator.NotEqual,
            ],
            route.Capabilities.Terms[0].Operators);
        Assert.Equal(
            [RankingBinding, SequenceBinding, NameOrderBinding],
            route.Capabilities.Orders
                .Select(capability =>
                    capability.Binding.Identity)
                .ToArray());
        Assert.Equal(
            [
                QueryOperationOrderRole.Baseline,
                QueryOperationOrderRole.Ranking,
            ],
            route.Capabilities.Orders[0].Roles);
        Assert.Equal(
            [QueryOperationOrderRole.Baseline],
            route.Capabilities.Orders[1].Roles);
        Assert.Equal(
            [
                PortableQueryDirection.Ascending,
                PortableQueryDirection.Descending,
            ],
            route.Capabilities.Orders[2].Directions);
        Assert.Equal(
            [TestVocabulary.CandidatesDimension],
            route.Capabilities.Dimensions);
        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Top,
            ],
            route.Capabilities.Stages);

        PortableQueryResolution<TestPlan> resolution =
            Resolve(
                route,
                Intent(
                    terms:
                    [
                        Term(TestVocabulary.DependsKey, "Serilog"),
                    ],
                    bounds:
                    [
                        new(
                            TestVocabulary.CandidatesDimension,
                            20),
                    ],
                    stages:
                    [
                        PortableQueryStage.Top(5),
                    ],
                    order:
                    [
                        PortableQueryOrderOperation.Named(
                            PortableQueryOrderRole.ForStage(0),
                            TestVocabulary.RankingOrder,
                            PortableQueryDirection.Descending),
                    ]));

        Assert.True(resolution.IsResolved);
        Assert.Equal(1, vocabulary.PlansCreated);
        Assert.Equal(
            "serilog",
            Assert.Single(
                resolution.Plan.Resolved.Terms).Predicate.Value);
    }

    [Fact]
    public void DescriptiveRegistrationsWithoutExecutorsAreRejected()
    {
        var vocabulary = new TestVocabulary();
        QueryOperationApplicability applicability =
            PackageApplicability();

        ArgumentException missingTerm = Assert.Throws<ArgumentException>(
            () => QueryOperationDefinition<TestPredicate, TestPlan>.Create(
                "test.operation",
                vocabulary,
                [PopulationRole],
                [PackageGrain],
                [ResultsRowSet],
                [
                    TermBinding(
                        "term.missing",
                        "missing",
                        applicability),
                ],
                [],
                [
                    new(
                        "default",
                        ["term.missing"],
                        []),
                ]));
        Assert.Contains("unknown key", missingTerm.Message);

        ArgumentException missingOrder = Assert.Throws<ArgumentException>(
            () => QueryOperationDefinition<TestPredicate, TestPlan>.Create(
                "test.operation",
                vocabulary,
                [PopulationRole],
                [PackageGrain],
                [ResultsRowSet],
                [],
                [
                    OrderBinding(
                        "order.missing",
                        QueryOperationOrderKind.Named,
                        "missing",
                        applicability),
                ],
                [
                    new(
                        "default",
                        [],
                        ["order.missing"]),
                ]));
        Assert.Contains("unknown order", missingOrder.Message);

        ArgumentException nonOrderable = Assert.Throws<ArgumentException>(
            () => QueryOperationDefinition<TestPredicate, TestPlan>.Create(
                "test.operation",
                vocabulary,
                [PopulationRole],
                [PackageGrain],
                [ResultsRowSet],
                [],
                [
                    OrderBinding(
                        "order.opaque",
                        QueryOperationOrderKind.Field,
                        TestVocabulary.OpaqueKey,
                        applicability),
                ],
                [
                    new(
                        "default",
                        [],
                        ["order.opaque"]),
                ]));
        Assert.Contains("not orderable", nonOrderable.Message);

        ArgumentException rejectedValue = Assert.Throws<ArgumentException>(
            () => QueryOperationDefinition<TestPredicate, TestPlan>.Create(
                "test.operation",
                vocabulary,
                [PopulationRole],
                [PackageGrain],
                [ResultsRowSet],
                [
                    new(
                        "term.format",
                        TestVocabulary.ToolFormatKey,
                        QueryOperationTermRole.SubjectQualification,
                        applicability,
                        new(
                            "Tool format",
                            "format",
                            ["v3"],
                            "Select a tool format."),
                        []),
                ],
                [],
                [
                    new(
                        "default",
                        ["term.format"],
                        []),
                ]));
        Assert.Contains("binder rejects", rejectedValue.Message);
    }

    [Fact]
    public void AdvertisedValuesMustBindForEveryOperator()
    {
        var vocabulary = new TestVocabulary();
        QueryOperationApplicability applicability =
            PackageApplicability();

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => QueryOperationDefinition<TestPredicate, TestPlan>.Create(
                "test.operation",
                vocabulary,
                [PopulationRole],
                [PackageGrain],
                [ResultsRowSet],
                [
                    new(
                        "term.mode",
                        TestVocabulary.ModeKey,
                        QueryOperationTermRole.SubjectQualification,
                        applicability,
                        new(
                            "Mode",
                            "mode",
                            ["fast"],
                            "Select a mode."),
                        []),
                ],
                [],
                [
                    new(
                        "default",
                        ["term.mode"],
                        []),
                ]));

        Assert.Contains("operator 'NotEqual'", failure.Message);
        Assert.Equal(0, vocabulary.PlansCreated);
    }

    [Fact]
    public void RouteBindingsRejectInapplicableCapabilities()
    {
        var vocabulary = new TestVocabulary();
        QueryOperationDefinition<TestPredicate, TestPlan> operation =
            CreateOperation(vocabulary);

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => CreateRoute(
                operation,
                "library-only"));

        Assert.Contains("does not apply", failure.Message);
    }

    [Fact]
    public void QueryProfileRestrictsResolutionWithoutStartingAPlan()
    {
        var vocabulary = new TestVocabulary
        {
            AdmitsRankingStages = true,
        };
        QueryOperationDefinition<TestPredicate, TestPlan> operation =
            CreateOperation(vocabulary);
        QueryOperationRoute<TestPredicate, TestPlan> route =
            CreateRoute(operation, RestrictedProfile);

        PortableQueryFailure excludedTerm =
            Resolve(
                route,
                Intent(terms:
                [
                    Term(TestVocabulary.ToolKey, "true"),
                ])).Failure;
        Assert.Equal(
            PortableQueryFailureReason.UnknownKey,
            excludedTerm.Reason);

        PortableQueryFailure excludedNamedOrder =
            Resolve(
                route,
                Intent(order:
                [
                    PortableQueryOrderOperation.Named(
                        PortableQueryOrderRole.Baseline,
                        TestVocabulary.SequenceOrder,
                        PortableQueryDirection.Ascending),
                ])).Failure;
        Assert.Equal(
            PortableQueryFailureReason.UnknownOrderReference,
            excludedNamedOrder.Reason);

        PortableQueryFailure fieldAsTerm =
            Resolve(
                route,
                Intent(terms:
                [
                    Term(TestVocabulary.NameKey, "value"),
                ])).Failure;
        Assert.Equal(
            PortableQueryFailureReason.OperatorNotAdmitted,
            fieldAsTerm.Reason);

        Assert.True(
            Resolve(
                route,
                Intent(order:
                [
                    PortableQueryOrderOperation.Fields(
                        PortableQueryOrderRole.Baseline,
                        [
                            new(
                                TestVocabulary.NameKey,
                                PortableQueryDirection.Ascending),
                        ]),
                ])).IsResolved);
        Assert.Equal(1, vocabulary.PlansCreated);
    }

    [Fact]
    public void RequiredQueryPartsMustBeRepresentableByTheRoute()
    {
        var vocabulary = new TestVocabulary
        {
            RequiredFamilies = ["dependency"],
            RequiredBounds = [TestVocabulary.CandidatesDimension],
        };
        QueryOperationDefinition<TestPredicate, TestPlan> operation =
            CreateOperation(vocabulary);

        ArgumentException missingFamily = Assert.Throws<ArgumentException>(
            () => QueryOperationRoute<TestPredicate, TestPlan>.Create(
                "route.missing-family",
                operation,
                PopulationRole,
                PackageGrain,
                [ResultsRowSet],
                RestrictedProfile,
                [TestVocabulary.CandidatesDimension],
                [RowSelectionStageKind.Head]));
        Assert.Contains("required term family", missingFamily.Message);

        ArgumentException missingDimension = Assert.Throws<ArgumentException>(
            () => QueryOperationRoute<TestPredicate, TestPlan>.Create(
                "route.missing-dimension",
                operation,
                PopulationRole,
                PackageGrain,
                [ResultsRowSet],
                "required",
                [],
                [RowSelectionStageKind.Head]));
        Assert.Contains("required work dimension", missingDimension.Message);

        QueryOperationRoute<TestPredicate, TestPlan> complete =
            QueryOperationRoute<TestPredicate, TestPlan>.Create(
                "route.complete",
                operation,
                PopulationRole,
                PackageGrain,
                [ResultsRowSet],
                "required",
                [TestVocabulary.CandidatesDimension],
                [RowSelectionStageKind.Head]);
        Assert.True(
            Resolve(
                complete,
                Intent(
                    terms:
                    [
                        Term(
                            TestVocabulary.DependenciesKey,
                            "none"),
                    ],
                    bounds:
                    [
                        new(
                            TestVocabulary.CandidatesDimension,
                            20),
                    ])).IsResolved);
    }

    [Fact]
    public void TopRequiresAnExecutableRankingOrder()
    {
        var vocabulary = new TestVocabulary
        {
            AdmitsRankingStages = true,
        };
        QueryOperationDefinition<TestPredicate, TestPlan> operation =
            CreateOperation(vocabulary);

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => QueryOperationRoute<TestPredicate, TestPlan>.Create(
                "route.top-without-ranking",
                operation,
                PopulationRole,
                PackageGrain,
                [ResultsRowSet],
                "required",
                [TestVocabulary.CandidatesDimension],
                [RowSelectionStageKind.Top]));

        Assert.Contains("exposes no ranking order", failure.Message);
    }

    [Fact]
    public void DefaultRankingMustNameARankingOrder()
    {
        var vocabulary = new TestVocabulary
        {
            AdmitsRankingStages = true,
            DeclaredDefaultRanking = TestVocabulary.SequenceOrder,
        };

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => CreateOperation(vocabulary));

        Assert.Contains("not a ranking order", failure.Message);
        Assert.Equal(0, vocabulary.PlansCreated);
    }

    [Fact]
    public void RouteMustRepresentBindingWorkDimensions()
    {
        QueryOperationDefinition<TestPredicate, TestPlan> operation =
            CreateOperation(new TestVocabulary());

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => QueryOperationRoute<TestPredicate, TestPlan>.Create(
                "route.missing-effect-dimension",
                operation,
                PopulationRole,
                PackageGrain,
                [ResultsRowSet],
                FullProfile,
                [],
                [RowSelectionStageKind.Head]));

        Assert.Contains("route does not support", failure.Message);
    }

    [Fact]
    public void RegistryProjectsHeterogeneousRoutesByIdentity()
    {
        QueryOperationDefinition<TestPredicate, TestPlan> operation =
            CreateOperation(new TestVocabulary());
        QueryOperationRoute<TestPredicate, TestPlan> full =
            CreateRoute(operation, FullProfile);
        QueryOperationRoute<TestPredicate, TestPlan> restricted =
            CreateRoute(
                operation,
                RestrictedProfile,
                identity: "route.restricted");

        QueryOperationRegistry registry =
            QueryOperationRegistry.Create(
                [full, restricted]);

        Assert.Same(
            full,
            AssertRoute(
                registry,
                "route.full"));
        Assert.Same(
            restricted,
            AssertRoute(
                registry,
                "route.restricted"));
        Assert.False(
            registry.TryGetRoute(
                "route.missing",
                out _));
        Assert.Throws<ArgumentException>(
            () => QueryOperationRegistry.Create(
                [full, full]));
    }

    private static QueryOperationDefinition<TestPredicate, TestPlan>
        CreateOperation(TestVocabulary vocabulary)
    {
        QueryOperationApplicability package =
            PackageApplicability();
        var library =
            new QueryOperationApplicability(
                [PopulationRole],
                [LibraryGrain],
                [ResultsRowSet]);

        return QueryOperationDefinition<
            TestPredicate,
            TestPlan>.Create(
                "test.operation",
                vocabulary,
                [PopulationRole, SelectedSubjectRole],
                [PackageGrain, LibraryGrain],
                [ResultsRowSet, DetailsRowSet],
                [
                    TermBinding(
                        DependsBinding,
                        TestVocabulary.DependsKey,
                        package),
                    TermBinding(
                        ToolBinding,
                        TestVocabulary.ToolKey,
                        package),
                    TermBinding(
                        NameBinding,
                        TestVocabulary.NameKey,
                        package,
                        QueryOperationTermRole.ResultPredicate),
                    TermBinding(
                        "term.dependencies",
                        TestVocabulary.DependenciesKey,
                        package),
                    TermBinding(
                        "term.library-only",
                        TestVocabulary.DependsKey,
                        library),
                ],
                [
                    OrderBinding(
                        RankingBinding,
                        QueryOperationOrderKind.Named,
                        TestVocabulary.RankingOrder,
                        package),
                    OrderBinding(
                        SequenceBinding,
                        QueryOperationOrderKind.Named,
                        TestVocabulary.SequenceOrder,
                        package),
                    OrderBinding(
                        NameOrderBinding,
                        QueryOperationOrderKind.Field,
                        TestVocabulary.NameKey,
                        package),
                ],
                [
                    new(
                        FullProfile,
                        [DependsBinding, NameBinding],
                        [
                            RankingBinding,
                            SequenceBinding,
                            NameOrderBinding,
                        ]),
                    new(
                        RestrictedProfile,
                        [DependsBinding],
                        [NameOrderBinding]),
                    new(
                        "required",
                        ["term.dependencies"],
                        []),
                    new(
                        "library-only",
                        ["term.library-only"],
                        []),
                ]);
    }

    private static QueryOperationRoute<TestPredicate, TestPlan>
        CreateRoute(
            QueryOperationDefinition<TestPredicate, TestPlan> operation,
            string profile,
            string identity = "route.full")
    {
        RowSelectionStageKind[] stages =
            operation.Vocabulary.AdmitsStageKind(
                RowSelectionStageKind.Top)
                ?
                [
                    RowSelectionStageKind.Head,
                    RowSelectionStageKind.Top,
                ]
                :
                [
                    RowSelectionStageKind.Head,
                ];
        return QueryOperationRoute<TestPredicate, TestPlan>.Create(
            identity,
            operation,
            PopulationRole,
            PackageGrain,
            [ResultsRowSet],
            profile,
            [TestVocabulary.CandidatesDimension],
            stages);
    }

    private static QueryOperationApplicability
        PackageApplicability() =>
        new(
            [PopulationRole],
            [PackageGrain],
            [ResultsRowSet]);

    private static QueryOperationTermBinding TermBinding(
        string identity,
        string key,
        QueryOperationApplicability applicability,
        QueryOperationTermRole role =
            QueryOperationTermRole.SubjectQualification) =>
        new(
            identity,
            key,
            role,
            applicability,
            new(
                key,
                "text",
                [],
                $"Query by {key}."),
            [
                new(
                    QueryOperationEffectKind.WorkDimension,
                    TestVocabulary.CandidatesDimension),
            ]);

    private static QueryOperationOrderBinding OrderBinding(
        string identity,
        QueryOperationOrderKind kind,
        string reference,
        QueryOperationApplicability applicability) =>
        new(
            identity,
            kind,
            reference,
            applicability,
            new(
                reference,
                $"Order by {reference}."),
            []);

    private static PortableQueryTerm Term(
        string key,
        string value) =>
        new(
            key,
            PortableQueryOperator.Equal,
            value);

    private static PortableQueryIntent Intent(
        PortableQueryTerm[]? terms = null,
        PortableQueryBound[]? bounds = null,
        PortableQueryStage[]? stages = null,
        PortableQueryOrderOperation[]? order = null) =>
        PortableQueryIntent.Create(
            terms ?? [],
            bounds ?? [],
            stages ?? [],
            order ?? []);

    private static PortableQueryResolution<TestPlan> Resolve(
        QueryOperationRoute<TestPredicate, TestPlan> route,
        PortableQueryIntent intent) =>
        route.Resolve(
            intent,
            TestContext.Current.CancellationToken);

    private static IQueryOperationRoute AssertRoute(
        QueryOperationRegistry registry,
        string identity)
    {
        Assert.True(
            registry.TryGetRoute(
                identity,
                out IQueryOperationRoute? route));
        return route!;
    }
}
