using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;

namespace DotnetInspector.Sections.Tests;

public sealed class QuerySpaceSectionRowCompositionTests
{
    [Fact]
    public void StructuralAssociationResolvesAndExecutesSameSchemaRowSets()
    {
        int baselineResolverCalls = 0;
        RowQueryVocabulary<ScoreRow> vocabulary =
            CreateRowVocabulary(
                () => baselineResolverCalls++);
        QuerySpaceRowScopeBinding<ScoreRow> queryScope =
            CreateQueryScope(vocabulary);
        QuerySpaceBinding querySpace =
            CreateQuerySpace(queryScope);
        PortableQueryIntent rowIntent =
            PortableQueryIntent.Create(
                [
                    new(
                        "score",
                        PortableQueryOperator.AtLeast,
                        "2"),
                ],
                [],
                [PortableQueryStage.Head(1)],
                [
                    PortableQueryOrderOperation.Fields(
                        PortableQueryOrderRole.Baseline,
                        [
                            new(
                                "score",
                                PortableQueryDirection.Descending),
                        ]),
                ]);
        QuerySpaceRowIntentAssociation association =
            new(
                queryScope.Descriptor.Identity,
                rowIntent,
                ["right", "left"]);
        QuerySpaceRequest request =
            QuerySpaceRequest.Create(
                querySpace.Descriptor,
                PortableQueryIntent.Empty,
                ["left", "right"],
                [association],
                QuerySpaceTerminalRequirement.Rows);

        SectionRowSchemaIdentity<ScoreRow> schema =
            SectionRowSchemaIdentity<ScoreRow>.Create();
        var sectionScope =
            new SectionQuerySpaceRowScopeBinding<ScoreRow>(
                queryScope,
                schema);
        SectionRowSetDeclaration<
            string,
            Projection>[] declarations =
        [
            Declaration(
                "left",
                schema,
                [new(1), new(4), new(3)],
                static (projection, rows) =>
                    projection with
                    {
                        Left =
                            rows.Select(
                                static row => row.Score)
                                .ToArray(),
                    }),
            Declaration(
                "right",
                schema,
                [new(2), new(5)],
                static (projection, rows) =>
                    projection with
                    {
                        Right =
                            rows.Select(
                                static row => row.Score)
                                .ToArray(),
                    }),
        ];

        QuerySpaceSectionRowResolutionResult<Projection> resolution =
            QuerySpaceSectionRowResolver.Resolve(
                querySpace,
                request,
                declarations,
                sectionScope);

        Assert.True(resolution.IsSuccess);
        Assert.Null(resolution.Failure);
        Assert.Same(
            association,
            resolution.Association!.Association);
        var resolved =
            Assert.IsType<
                ResolvedQuerySpaceRowAssociation<ScoreRow>>(
                    resolution.Association);
        Assert.Same(vocabulary.Identity, resolved.Plan.VocabularyIdentity);
        Assert.Single(resolved.Plan.PredicateKeyIdentities);
        Assert.NotNull(resolved.Plan.BaselineOrder);
        Assert.Single(resolved.Plan.SelectionPlan.Stages);
        Assert.Equal(
            RowSelectionStageKind.Head,
            resolved.Plan.SelectionPlan.Stages[0].Kind);
        Assert.Equal(
            QuerySpaceTerminalRequirement.Rows,
            resolution.Request!.Terminal);

        SectionRowsOutcome<string, Projection> rows =
            QuerySpaceSectionRowExecutor.ApplyRows(
                resolution.Request!);
        Assert.True(rows.IsSuccess);
        Projection projection =
            rows.Rebind(Projection.Empty);
        Assert.Equal([4], projection.Left);
        Assert.Equal([5], projection.Right);
        Assert.Equal(1, baselineResolverCalls);

        Assert.Throws<InvalidOperationException>(
            () => QuerySpaceSectionRowExecutor.ApplyCount<
                Projection,
                string>(resolution.Request!));
        QuerySpaceRequest countRequest =
            QuerySpaceRequest.Create(
                querySpace.Descriptor,
                PortableQueryIntent.Empty,
                ["left", "right"],
                [association],
                QuerySpaceTerminalRequirement.Count);
        QuerySpaceSectionRowResolutionResult<Projection>
            countResolution =
                QuerySpaceSectionRowResolver.Resolve(
                    querySpace,
                    countRequest,
                    declarations,
                    sectionScope);
        Assert.Same(
            association,
            countResolution.Association!.Association);
        Assert.Equal(
            QuerySpaceTerminalRequirement.Count,
            countResolution.Request!.Terminal);
        SectionCountOutcome<string, string> count =
            QuerySpaceSectionRowExecutor.ApplyCount<
                Projection,
                string>(countResolution.Request);
        var completed =
            Assert.IsType<
                SectionCountOutcome<
                    string,
                    string>.Completed>(count);
        Assert.Equal(
            ["left", "right"],
            completed.Counts.Select(
                static entry => entry.Identity));
        Assert.Equal(
            [1, 1],
            completed.Counts.Select(
                static entry => entry.Value));
        Assert.Equal(2, baselineResolverCalls);
    }

    [Fact]
    public void ExecutionUsesDeclarationSnapshotAfterCallerMutation()
    {
        QuerySpaceRowScopeBinding<ScoreRow> queryScope =
            CreateQueryScope(CreateRowVocabulary());
        QuerySpaceBinding querySpace =
            CreateQuerySpace(queryScope);
        PortableQueryIntent rowIntent =
            PortableQueryIntent.Create(
                [],
                [],
                [PortableQueryStage.Head(1)],
                []);
        QuerySpaceRowIntentAssociation association =
            new(
                queryScope.Descriptor.Identity,
                rowIntent,
                ["left"]);
        QuerySpaceRequest request =
            QuerySpaceRequest.Create(
                querySpace.Descriptor,
                PortableQueryIntent.Empty,
                ["left"],
                [association],
                QuerySpaceTerminalRequirement.Rows);
        SectionRowSchemaIdentity<ScoreRow> schema =
            SectionRowSchemaIdentity<ScoreRow>.Create();
        var sectionScope =
            new SectionQuerySpaceRowScopeBinding<ScoreRow>(
                queryScope,
                schema);
        var source = new List<ScoreRow>
        {
            new(1),
            new(2),
        };
        SectionRowSetDeclaration<
            string,
            Projection,
            ScoreRow> declaration =
                Declaration(
                    "left",
                    schema,
                    source,
                    static (projection, rows) =>
                        projection with
                        {
                            Left =
                                rows.Select(
                                    static row => row.Score)
                                    .ToArray(),
                        });

        QuerySpaceSectionRowResolutionResult<Projection> resolution =
            QuerySpaceSectionRowResolver.Resolve(
                querySpace,
                request,
                [declaration],
                sectionScope);

        Assert.True(resolution.IsSuccess);
        source.Clear();
        source.Add(new(99));

        SectionRowsOutcome<string, Projection> rows =
            QuerySpaceSectionRowExecutor.ApplyRows(
                resolution.Request!);

        Assert.True(rows.IsSuccess);
        Assert.Equal(
            [1],
            rows.Rebind(Projection.Empty).Left);
    }

    [Fact]
    public void PredicatesRunBeforeBaselineComparerResolution()
    {
        var predicateException =
            new InvalidOperationException("predicate");
        var baselineException =
            new InvalidOperationException("baseline");
        int baselineResolverCalls = 0;
        RowQueryKey<ScoreRow> score =
            RowQueryKey<ScoreRow>.Create(
                RowQueryKeyIdentity.Create(),
                "score",
                [RowQueryOperator.GreaterOrEqual],
                static row =>
                    RowQueryValue<int>.Present(row.Score),
                (_, _) =>
                    _ => throw predicateException,
                _ =>
                {
                    baselineResolverCalls++;
                    throw baselineException;
                });
        RowQueryVocabulary<ScoreRow> vocabulary =
            RowQueryVocabulary<ScoreRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [score],
                []);
        QuerySpaceRowScopeBinding<ScoreRow> queryScope =
            CreateQueryScope(vocabulary);
        QuerySpaceBinding querySpace =
            CreateQuerySpace(queryScope);
        PortableQueryIntent rowIntent =
            PortableQueryIntent.Create(
                [
                    new(
                        "score",
                        PortableQueryOperator.AtLeast,
                        "2"),
                ],
                [],
                [],
                [
                    PortableQueryOrderOperation.Fields(
                        PortableQueryOrderRole.Baseline,
                        [
                            new(
                                "score",
                                PortableQueryDirection.Descending),
                        ]),
                ]);
        QuerySpaceRequest request =
            QuerySpaceRequest.Create(
                querySpace.Descriptor,
                PortableQueryIntent.Empty,
                ["left"],
                [
                    new(
                        queryScope.Descriptor.Identity,
                        rowIntent,
                        ["left"]),
                ],
                QuerySpaceTerminalRequirement.Rows);
        SectionRowSchemaIdentity<ScoreRow> schema =
            SectionRowSchemaIdentity<ScoreRow>.Create();
        QuerySpaceSectionRowResolutionResult<Projection> resolution =
            QuerySpaceSectionRowResolver.Resolve(
                querySpace,
                request,
                [
                    Declaration(
                        "left",
                        schema,
                        [new(1)],
                        static (projection, rows) =>
                            projection with
                            {
                                Left =
                                    rows.Select(
                                        static row => row.Score)
                                        .ToArray(),
                            }),
                ],
                new SectionQuerySpaceRowScopeBinding<ScoreRow>(
                    queryScope,
                    schema));

        Assert.True(resolution.IsSuccess);
        Assert.Equal(0, baselineResolverCalls);
        Assert.Same(
            predicateException,
            Assert.Throws<InvalidOperationException>(
                () => QuerySpaceSectionRowExecutor.ApplyRows(
                    resolution.Request!)));
        Assert.Equal(0, baselineResolverCalls);
    }

    [Fact]
    public void RowResolutionFailureRemainsVisibleBeforeExecution()
    {
        QuerySpaceRowScopeBinding<ScoreRow> queryScope =
            CreateQueryScope(CreateRowVocabulary());
        QuerySpaceBinding querySpace =
            CreateQuerySpace(queryScope);
        QuerySpaceRequest request =
            QuerySpaceRequest.Create(
                querySpace.Descriptor,
                PortableQueryIntent.Empty,
                ["left"],
                [
                    new(
                        queryScope.Descriptor.Identity,
                        PortableQueryIntent.Create(
                            [
                                new(
                                    "score",
                                    PortableQueryOperator.AtLeast,
                                    "not-an-integer"),
                            ],
                            [],
                            [],
                            []),
                        ["left"]),
                ],
                QuerySpaceTerminalRequirement.Rows);
        SectionRowSchemaIdentity<ScoreRow> schema =
            SectionRowSchemaIdentity<ScoreRow>.Create();
        var sectionScope =
            new SectionQuerySpaceRowScopeBinding<ScoreRow>(
                queryScope,
                schema);
        SectionRowSetDeclaration<
            string,
            Projection,
            ScoreRow> declaration =
                Declaration(
                    "left",
                    schema,
                    [new(1)],
                    static (projection, _) => projection);

        QuerySpaceSectionRowResolutionResult<Projection> resolution =
            QuerySpaceSectionRowResolver.Resolve(
                querySpace,
                request,
                [declaration],
                sectionScope);

        Assert.False(resolution.IsSuccess);
        Assert.Null(resolution.Request);
        Assert.Null(resolution.Association);
        Assert.Equal(
            queryScope.Descriptor.Identity,
            resolution.Failure!.Scope);
        Assert.Equal(
            RowQueryFailureReason.InvalidValue,
            resolution.Failure.RowQueryFailure.Reason);
    }

    [Fact]
    public void DescriptorAndExecutableVocabularyMustMatch()
    {
        RowQueryVocabulary<ScoreRow> vocabulary =
            CreateRowVocabulary();
        QuerySpaceRowScopeDescriptor descriptor =
            CreateRowDescriptor(
                [
                    PortableQueryOperator.Equal,
                ]);

        ArgumentException failure =
            Assert.Throws<ArgumentException>(
                () => new QuerySpaceRowScopeBinding<ScoreRow>(
                    descriptor,
                    vocabulary));

        Assert.Contains(
            "operator surface differs",
            failure.Message);

        ArgumentException missingDefaultRanking =
            Assert.Throws<ArgumentException>(
                () => new QuerySpaceRowScopeBinding<ScoreRow>(
                    CreateRowDescriptor(
                        [PortableQueryOperator.AtLeast],
                        [RowSelectionStageKind.Top]),
                    vocabulary));
        Assert.Contains(
            "without a default executable ranking",
            missingDefaultRanking.Message);
    }

    [Fact]
    public void RequestIsRevalidatedAgainstExecutableBinding()
    {
        QuerySpaceRowScopeBinding<ScoreRow> queryScope =
            CreateQueryScope(CreateRowVocabulary());
        QuerySpaceBinding querySpace =
            CreateQuerySpace(queryScope);
        QuerySpaceDescriptor staleDescriptor =
            QuerySpaceDescriptor.Create(
                querySpace.Descriptor.Identity,
                querySpace.Operation,
                [
                    CreateRowDescriptor(
                        [PortableQueryOperator.Equal]),
                ],
                querySpace.Descriptor.Terminals,
                querySpace.Descriptor.AcceptsContinuation,
                querySpace.Descriptor.ResultContracts);
        QuerySpaceRequest staleRequest =
            QuerySpaceRequest.Create(
                staleDescriptor,
                PortableQueryIntent.Empty,
                ["left"],
                [
                    new(
                        queryScope.Descriptor.Identity,
                        PortableQueryIntent.Create(
                            [
                                new(
                                    "score",
                                    PortableQueryOperator.Equal,
                                    "2"),
                            ],
                            [],
                            [],
                            []),
                        ["left"]),
                ],
                QuerySpaceTerminalRequirement.Rows);
        SectionRowSchemaIdentity<ScoreRow> schema =
            SectionRowSchemaIdentity<ScoreRow>.Create();
        var sectionScope =
            new SectionQuerySpaceRowScopeBinding<ScoreRow>(
                queryScope,
                schema);

        ArgumentException failure =
            Assert.Throws<ArgumentException>(
                () => QuerySpaceSectionRowResolver.Resolve(
                    querySpace,
                    staleRequest,
                    [
                        Declaration(
                            "left",
                            schema,
                            [new(2)],
                            static (projection, _) => projection),
                    ],
                    sectionScope));

        Assert.Contains(
            "does not admit operator",
            failure.Message);
    }

    private static QuerySpaceRowScopeBinding<ScoreRow>
        CreateQueryScope(
            RowQueryVocabulary<ScoreRow> vocabulary) =>
        new(
            CreateRowDescriptor(
                [
                    PortableQueryOperator.AtLeast,
                ]),
            vocabulary);

    private static QuerySpaceRowScopeDescriptor
        CreateRowDescriptor(
            IReadOnlyList<PortableQueryOperator> operators,
            IReadOnlyList<RowSelectionStageKind>? stages = null) =>
        new(
            "rows.score",
            "rows.score.v1",
            ["left", "right"],
            [
                new(
                    "score",
                    "score",
                    operators,
                    "integer",
                    null,
                    "Score",
                    [],
                    "Filter and order score rows.",
                    supportsOrdering: true),
            ],
            [],
            stages ?? [RowSelectionStageKind.Head]);

    private static RowQueryVocabulary<ScoreRow>
        CreateRowVocabulary(
            Action? baselineResolved = null)
    {
        RowQueryKey<ScoreRow> score =
            RowQueryKey<ScoreRow>.Create(
                RowQueryKeyIdentity.Create(),
                "score",
                [RowQueryOperator.GreaterOrEqual],
                static row =>
                    RowQueryValue<int>.Present(row.Score),
                static (@operator, token) =>
                {
                    if (@operator
                            is not RowQueryOperator.GreaterOrEqual
                        || !int.TryParse(
                            token.Text,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out int expected))
                    {
                        return null;
                    }

                    return actual => actual >= expected;
                },
                direction =>
                {
                    baselineResolved?.Invoke();
                    return RowQueryValueOrder.Create(
                        Comparer<int>.Default,
                        direction,
                        missingLast: true);
                });
        return RowQueryVocabulary<ScoreRow>.Create(
            RowQueryVocabularyIdentity.Create(),
            [score],
            []);
    }

    private static QuerySpaceBinding CreateQuerySpace(
        QuerySpaceRowScopeBinding<ScoreRow> queryScope)
    {
        var vocabulary = new OperationVocabulary();
        QueryOperationApplicability applicability =
            new(
                ["test"],
                ["rows"],
                ["left", "right"]);
        QueryOperationDefinition<
            OperationPredicate,
            OperationPlan> operation =
                QueryOperationDefinition<
                    OperationPredicate,
                    OperationPlan>.Create(
                    "test.query",
                    vocabulary,
                    ["test"],
                    ["rows"],
                    ["left", "right"],
                    [
                        new(
                            "mode",
                            "mode",
                            QueryOperationTermRole.OperationSelector,
                            applicability,
                            new(
                                "Mode",
                                "text",
                                ["all"],
                                "Select all rows."),
                            []),
                    ],
                    [],
                    [
                        new(
                            "default",
                            ["mode"],
                            []),
                    ]);
        QueryOperationRoute<
            OperationPredicate,
            OperationPlan> route =
                QueryOperationRoute<
                    OperationPredicate,
                    OperationPlan>.Create(
                    "test.rows",
                    operation,
                    "test",
                    "rows",
                    ["left", "right"],
                    "default",
                    [],
                    []);
        return QuerySpaceBinding.Create(
            "test.space",
            route,
            [queryScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            []);
    }

    private static SectionRowSetDeclaration<
        string,
        Projection,
        ScoreRow> Declaration(
            string identity,
            SectionRowSchemaIdentity<ScoreRow> schema,
            IReadOnlyList<ScoreRow> rows,
            Func<
                Projection,
                IReadOnlyList<ScoreRow>,
                Projection> binder) =>
        new(
            identity,
            schema,
            rows,
            binder);

    private sealed record ScoreRow(int Score);

    private sealed record Projection(
        IReadOnlyList<int> Left,
        IReadOnlyList<int> Right)
    {
        public static Projection Empty { get; } =
            new([], []);
    }

    private sealed record OperationPredicate(string Value);

    private sealed record OperationPlan(
        PortableQueryResolvedIntent<OperationPredicate> Resolved);

    private sealed class OperationVocabulary :
        PortableQueryVocabulary<OperationPredicate, OperationPlan>
    {
        private static readonly ModeDeclaration Mode = new();

        public override string Identity => "test.operation";

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<
                OperationPredicate>? declaration)
        {
            declaration = key == Mode.Key ? Mode : null;
            return declaration is not null;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<
                OperationPredicate>? declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(
            RowSelectionStageKind kind) =>
            false;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) =>
            false;

        public override OperationPlan CreatePlan(
            PortableQueryResolvedIntent<
                OperationPredicate> resolved) =>
            new(resolved);
    }

    private sealed class ModeDeclaration :
        PortableQueryKeyDeclaration<OperationPredicate>
    {
        public override string Key => "mode";

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator is PortableQueryOperator.Equal;

        public override PortableQueryBinding<OperationPredicate>
            Bind(
                PortableQueryOperator @operator,
                string value) =>
            @operator is PortableQueryOperator.Equal
            && value is "all"
                ? PortableQueryBinding<OperationPredicate>.Bound(
                    "all",
                    new(value))
                : PortableQueryBinding<
                    OperationPredicate>.Rejected;
    }
}
