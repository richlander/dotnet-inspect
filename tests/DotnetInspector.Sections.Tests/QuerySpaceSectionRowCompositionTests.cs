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
    public void RowsPreserveIndependentSourceOutcomes()
    {
        int residualCalls = 0;
        QuerySpaceRowScopeBinding<ScoreRow> queryScope =
            CreateQueryScope(
                CreateRowVocabulary(
                    () => residualCalls++));
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
        SectionRowSchemaIdentity<ScoreRow> schema =
            SectionRowSchemaIdentity<ScoreRow>.Create();
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
                [new(9)],
                static (projection, rows) =>
                    projection with
                    {
                        Right =
                            rows.Select(
                                static row => row.Score)
                                .ToArray(),
                    }),
        ];
        var partial =
            new CompletionReceipt("candidate limit");
        var unavailable =
            new CompletionReceipt("source unavailable");
        SectionRowSourceState<
            string,
            SourceDisposition,
            CompletionReceipt>[] sources =
        [
            Source(
                "left",
                SourceDisposition.Partial,
                partial,
                rowsAreUsable: true,
                countIsSufficient: false),
            Source(
                "right",
                SourceDisposition.Unavailable,
                unavailable,
                rowsAreUsable: false,
                countIsSufficient: false),
        ];
        var sectionScope =
            new SectionQuerySpaceRowScopeBinding<ScoreRow>(
                queryScope,
                schema);

        QuerySpaceSectionSourceRowResolutionResult<
            Projection,
            SourceDisposition,
            CompletionReceipt> rowsResolution =
                QuerySpaceSectionRowResolver.Resolve(
                    querySpace,
                    CreateRequest(
                        querySpace,
                        queryScope,
                        rowIntent,
                        QuerySpaceTerminalRequirement.Rows),
                    declarations,
                    sources,
                    sectionScope);

        Assert.True(rowsResolution.IsSuccess);
        SectionSourceRowsOutcome<
            string,
            Projection,
            SourceDisposition,
            CompletionReceipt> rows =
                QuerySpaceSectionRowExecutor.ApplyRows(
                    rowsResolution.Request!);
        Assert.True(rows.IsSuccess);
        Assert.Equal(["left", "right"], rows.RowSets.Select(
            static rowSet => rowSet.Identity));
        Assert.Equal(
            [SourceDisposition.Partial, SourceDisposition.Unavailable],
            rows.RowSets.Select(
                static rowSet =>
                    rowSet.Source.Evidence.Disposition));
        Assert.Same(
            partial,
            rows.RowSets[0].Source.Evidence.Completion);
        Assert.Same(
            unavailable,
            rows.RowSets[1].Source.Evidence.Completion);
        var selectedLeft =
            Assert.IsType<
                SectionRowSetResult<
                    string,
                    Projection,
                    ScoreRow>>(
                        rows.RowSets[0].SelectedRows);
        Assert.Equal(
            [4],
            selectedLeft.Rows.Select(
                static row => row.Score));
        Assert.False(rows.RowSets[1].RowsAreAvailable);
        Assert.Null(rows.RowSets[1].SelectedRows);
        Projection projection =
            rows.Rebind(Projection.Empty);
        Assert.Equal([4], projection.Left);
        Assert.Empty(projection.Right);
        Assert.Equal(1, residualCalls);
        Assert.Throws<InvalidOperationException>(
            () => QuerySpaceSectionRowExecutor.ApplyCount(
                rowsResolution.Request!));

        QuerySpaceSectionSourceRowResolutionResult<
            Projection,
            SourceDisposition,
            CompletionReceipt> countResolution =
                QuerySpaceSectionRowResolver.Resolve(
                    querySpace,
                    CreateRequest(
                        querySpace,
                        queryScope,
                        rowIntent,
                        QuerySpaceTerminalRequirement.Count),
                    declarations,
                    sources,
                    sectionScope);
        SectionCountOutcome<
            string,
            SectionRowSourceEvidence<
                SourceDisposition,
                CompletionReceipt>> count =
                    QuerySpaceSectionRowExecutor.ApplyCount(
                        countResolution.Request!);
        var sourceFailure =
            Assert.IsType<
                SectionCountOutcome<
                    string,
                    SectionRowSourceEvidence<
                        SourceDisposition,
                        CompletionReceipt>>.SourceForCount>(
                            count);
        Assert.Equal(
            ["left", "right"],
            sourceFailure.Sources.Select(
                static source => source.Identity));
        Assert.Same(
            partial,
            sourceFailure.Sources[0].Evidence.Completion);
        Assert.Same(
            unavailable,
            sourceFailure.Sources[1].Evidence.Completion);
        Assert.Equal(1, residualCalls);
        Assert.Throws<InvalidOperationException>(
            () => QuerySpaceSectionRowExecutor.ApplyRows(
                countResolution.Request!));
    }

    [Fact]
    public void IncompleteRowsRemainVisibleWithoutBecomingCount()
    {
        int residualCalls = 0;
        QuerySpaceRowScopeBinding<ScoreRow> queryScope =
            CreateQueryScope(
                CreateRowVocabulary(
                    () => residualCalls++));
        QuerySpaceBinding querySpace =
            CreateQuerySpace(queryScope);
        SectionRowSchemaIdentity<ScoreRow> schema =
            SectionRowSchemaIdentity<ScoreRow>.Create();
        SectionRowSetDeclaration<
            string,
            Projection>[] declarations =
        [
            Declaration(
                "left",
                schema,
                [],
                static (projection, rows) =>
                    projection with
                    {
                        Left =
                            rows.Select(
                                static row => row.Score)
                                .ToArray(),
                    }),
        ];
        var sectionScope =
            new SectionQuerySpaceRowScopeBinding<ScoreRow>(
                queryScope,
                schema);
        PortableQueryIntent rowIntent =
            PortableQueryIntent.Empty;
        var partialReceipt =
            new CompletionReceipt("bounded before exhaustion");
        SectionRowSourceState<
            string,
            SourceDisposition,
            CompletionReceipt>[] partialSources =
        [
            Source(
                "left",
                SourceDisposition.Partial,
                partialReceipt,
                rowsAreUsable: true,
                countIsSufficient: false),
        ];

        QuerySpaceSectionSourceRowResolutionResult<
            Projection,
            SourceDisposition,
            CompletionReceipt> rowsResolution =
                QuerySpaceSectionRowResolver.Resolve(
                    querySpace,
                    CreateRequest(
                        querySpace,
                        queryScope,
                        rowIntent,
                        QuerySpaceTerminalRequirement.Rows,
                        ["left"]),
                    declarations,
                    partialSources,
                    sectionScope);
        SectionSourceRowsOutcome<
            string,
            Projection,
            SourceDisposition,
            CompletionReceipt> rows =
                QuerySpaceSectionRowExecutor.ApplyRows(
                    rowsResolution.Request!);
        Assert.True(rows.IsSuccess);
        Assert.True(rows.RowSets[0].RowsAreAvailable);
        Assert.Empty(
            Assert.IsType<
                SectionRowSetResult<
                    string,
                    Projection,
                    ScoreRow>>(
                        rows.RowSets[0].SelectedRows)
                .Rows);
        Assert.Same(
            partialReceipt,
            rows.RowSets[0].Source.Evidence.Completion);

        QuerySpaceSectionSourceRowResolutionResult<
            Projection,
            SourceDisposition,
            CompletionReceipt> partialCountResolution =
                QuerySpaceSectionRowResolver.Resolve(
                    querySpace,
                    CreateRequest(
                        querySpace,
                        queryScope,
                        rowIntent,
                        QuerySpaceTerminalRequirement.Count,
                        ["left"]),
                    declarations,
                    partialSources,
                    sectionScope);
        var partialCount =
            Assert.IsType<
                SectionCountOutcome<
                    string,
                    SectionRowSourceEvidence<
                        SourceDisposition,
                        CompletionReceipt>>.SourceForCount>(
                            QuerySpaceSectionRowExecutor.ApplyCount(
                                partialCountResolution.Request!));
        Assert.Single(partialCount.Sources);
        Assert.Same(
            partialReceipt,
            partialCount.Sources[0].Evidence.Completion);

        SectionRowSourceState<
            string,
            SourceDisposition,
            CompletionReceipt>[] completeSources =
        [
            Source(
                "left",
                SourceDisposition.Complete,
                new("logical exhaustion"),
                rowsAreUsable: true,
                countIsSufficient: true),
        ];
        QuerySpaceSectionSourceRowResolutionResult<
            Projection,
            SourceDisposition,
            CompletionReceipt> completeCountResolution =
                QuerySpaceSectionRowResolver.Resolve(
                    querySpace,
                    CreateRequest(
                        querySpace,
                        queryScope,
                        rowIntent,
                        QuerySpaceTerminalRequirement.Count,
                        ["left"]),
                    declarations,
                    completeSources,
                    sectionScope);
        var completed =
            Assert.IsType<
                SectionCountOutcome<
                    string,
                    SectionRowSourceEvidence<
                        SourceDisposition,
                        CompletionReceipt>>.Completed>(
                            QuerySpaceSectionRowExecutor.ApplyCount(
                                completeCountResolution.Request!));
        Assert.Single(completed.Counts);
        Assert.Equal("left", completed.Counts[0].Identity);
        Assert.Equal(0, completed.Counts[0].Value);
        Assert.Equal(0, residualCalls);
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
    public void CountFallsBackToRowsWhenTopFollowsStrictWindow()
    {
        QuerySpaceRowScopeBinding<ScoreRow> queryScope =
            CreateQueryScope(
                CreateRowVocabulary(
                    includeTopRanking: true),
                [
                    RowSelectionStageKind.Window,
                    RowSelectionStageKind.Top,
                ],
                includeTopRanking: true);
        QuerySpaceBinding querySpace =
            CreateQuerySpace(queryScope);
        PortableQueryIntent rowIntent =
            PortableQueryIntent.Create(
                [],
                [],
                [
                    PortableQueryStage.Window(1, 2),
                    PortableQueryStage.Top(1),
                ],
                []);
        SectionRowSchemaIdentity<ScoreRow> schema =
            SectionRowSchemaIdentity<ScoreRow>.Create();
        var source = new List<ScoreRow>
        {
            new(1),
            new(3),
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
                CreateRequest(
                    querySpace,
                    queryScope,
                    rowIntent,
                    QuerySpaceTerminalRequirement.Count,
                    ["left"]),
                [declaration],
                new SectionQuerySpaceRowScopeBinding<ScoreRow>(
                    queryScope,
                    schema));

        Assert.True(resolution.IsSuccess);

        var completed =
            Assert.IsType<
                SectionCountOutcome<
                    string,
                    string>.Completed>(
                        QuerySpaceSectionRowExecutor.ApplyCount<
                            Projection,
                            string>(resolution.Request!));
        Assert.Equal(1, Assert.Single(completed.Counts).Value);
    }

    [Fact]
    public void SourceFilteringPreservesDeclaredCohortFailureOrder()
    {
        var calls = new List<string>();
        SectionRowSchemaIdentity<ScoreRow> schemaA =
            SectionRowSchemaIdentity<ScoreRow>.Create();
        SectionRowSchemaIdentity<ScoreRow> schemaB =
            SectionRowSchemaIdentity<ScoreRow>.Create();
        RowSelectionPlan<string> strictWindow =
            RowSelectionPlan<string>.Create(
                [
                    RowSelectionStage<string>.Window(
                        1,
                        2),
                ]);
        var bindingA =
            new SectionRowSchemaBinding<string, ScoreRow>(
                schemaA,
                sequences =>
                {
                    calls.Add(
                        "A:"
                        + string.Join(
                            ",",
                            sequences.Select(
                                static sequence =>
                                    sequence.Identity)));
                    return RowsCohortExecutor.Apply(
                        sequences,
                        strictWindow);
                });
        var bindingB =
            new SectionRowSchemaBinding<string, ScoreRow>(
                schemaB,
                sequences =>
                {
                    calls.Add(
                        "B:"
                        + string.Join(
                            ",",
                            sequences.Select(
                                static sequence =>
                                    sequence.Identity)));
                    return RowsCohortExecutor.Apply(
                        sequences,
                        strictWindow);
                });
        SectionRowSetDeclaration<
            string,
            Projection>[] declarations =
        [
            Declaration(
                "a0",
                schemaA,
                [new(0)],
                static (projection, _) => projection),
            Declaration(
                "b0",
                schemaB,
                [new(0)],
                static (projection, _) => projection),
            Declaration(
                "a1",
                schemaA,
                [new(0)],
                static (projection, _) => projection),
        ];
        SectionRowSourceState<
            string,
            SourceDisposition,
            CompletionReceipt>[] sources =
        [
            Source(
                "a0",
                SourceDisposition.Unavailable,
                new("unavailable"),
                rowsAreUsable: false,
                countIsSufficient: false),
            Source(
                "b0",
                SourceDisposition.Complete,
                new("complete"),
                rowsAreUsable: true,
                countIsSufficient: true),
            Source(
                "a1",
                SourceDisposition.Complete,
                new("complete"),
                rowsAreUsable: true,
                countIsSufficient: true),
        ];
        SectionSourceRowExecutionRequest<
            string,
            Projection,
            SourceDisposition,
            CompletionReceipt> request =
                SectionSourceRowExecutionRequest<
                    string,
                    Projection,
                    SourceDisposition,
                    CompletionReceipt>.Create(
                        declarations,
                        new(
                            ["a0", "b0", "a1"],
                            [bindingB, bindingA]),
                        sources);

        SectionSourceRowsOutcome<
            string,
            Projection,
            SourceDisposition,
            CompletionReceipt> rows =
                SectionSourceRowExecutor.ApplyRows(request);

        Assert.False(rows.IsSuccess);
        Assert.Equal("a1", rows.Failure!.Identity);
        Assert.Empty(rows.RowSets);
        Assert.Equal(["A:a1"], calls);
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
            RowQueryVocabulary<ScoreRow> vocabulary,
            IReadOnlyList<RowSelectionStageKind>? stages = null,
            bool includeTopRanking = false) =>
        new(
            CreateRowDescriptor(
                [
                    PortableQueryOperator.AtLeast,
                ],
                stages,
                includeTopRanking),
            vocabulary);

    private static QuerySpaceRowScopeDescriptor
        CreateRowDescriptor(
            IReadOnlyList<PortableQueryOperator> operators,
            IReadOnlyList<RowSelectionStageKind>? stages = null,
            bool includeTopRanking = false)
    {
        IReadOnlyList<RowSelectionStageKind> effectiveStages =
            stages ?? [RowSelectionStageKind.Head];
        IReadOnlyList<QuerySpaceRowOrderDescriptor> orders =
            includeTopRanking
                ? [new("score-ranking", ranking: true)]
                : [];
        return new(
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
            orders,
            effectiveStages);
    }

    private static RowQueryVocabulary<ScoreRow>
        CreateRowVocabulary(
            Action? baselineResolved = null,
            bool includeTopRanking = false)
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
        if (!includeTopRanking)
        {
            return RowQueryVocabulary<ScoreRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [score],
                []);
        }

        var scoreRanking =
            new RowQueryNamedOrder<ScoreRow>(
                RowQueryNamedOrderIdentity.Create(),
                "score-ranking",
                RowQueryOrderPurpose.Ranking,
                direction =>
                {
                    IComparer<ScoreRow> comparer =
                        Comparer<ScoreRow>.Create(
                            static (left, right) =>
                                left.Score.CompareTo(right.Score));
                    return direction
                        is RowQueryOrderDirection.Ascending
                            ? comparer
                            : Comparer<ScoreRow>.Create(
                                (left, right) =>
                                    comparer.Compare(right, left));
                });
        return RowQueryVocabulary<ScoreRow>.Create(
            RowQueryVocabularyIdentity.Create(),
            [score],
            [scoreRanking],
            defaultTopRanking:
                new(
                    scoreRanking,
                    RowQueryOrderDirection.Descending));
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

    private static QuerySpaceRequest CreateRequest(
        QuerySpaceBinding querySpace,
        QuerySpaceRowScopeBinding<ScoreRow> queryScope,
        PortableQueryIntent rowIntent,
        QuerySpaceTerminalRequirement terminal,
        IReadOnlyList<string>? rowSets = null)
    {
        IReadOnlyList<string> participating =
            rowSets ?? ["left", "right"];
        return QuerySpaceRequest.Create(
            querySpace.Descriptor,
            PortableQueryIntent.Empty,
            participating,
            [
                new(
                    queryScope.Descriptor.Identity,
                    rowIntent,
                    participating),
            ],
            terminal);
    }

    private static SectionRowSourceState<
        string,
        SourceDisposition,
        CompletionReceipt> Source(
            string identity,
            SourceDisposition disposition,
            CompletionReceipt completion,
            bool rowsAreUsable,
            bool countIsSufficient) =>
        new(
            identity,
            new(disposition, completion),
            rowsAreUsable,
            countIsSufficient);

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

    private enum SourceDisposition
    {
        Complete,
        Partial,
        Unavailable,
    }

    private sealed record CompletionReceipt(string Description);

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
