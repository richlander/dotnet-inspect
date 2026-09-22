using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DotnetInspector.Sections;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace QuerySpace.Consumer;

public sealed record ApplicationRow(
    string Name,
    int Score);

public sealed class ApplicationRows : IReadOnlyList<ApplicationRow>
{
    private readonly ApplicationRow[] _rows;

    public ApplicationRows(params ApplicationRow[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = [.. rows];
    }

    public int Count => _rows.Length;

    public ApplicationRow this[int index] => _rows[index];

    public void Replace(
        int index,
        ApplicationRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _rows[index] = row;
    }

    public IEnumerator<ApplicationRow> GetEnumerator() =>
        ((IEnumerable<ApplicationRow>)_rows).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

public sealed record QuerySpaceConsumerObservation(
    IReadOnlyList<string> Names,
    Type RowType,
    Type CollectionType,
    string QuerySpace,
    string RowScope,
    QuerySpaceTerminalRequirement Terminal);

public static class QuerySpaceDirectConsumer
{
    public static QuerySpaceConsumerObservation Execute()
    {
        var rows = new ApplicationRows(
            new("low", 1),
            new("high", 3),
            new("middle", 2));
        RowQueryVocabulary<ApplicationRow> rowVocabulary =
            CreateRowVocabulary();
        QuerySpaceBinding querySpace =
            CreateQuerySpace(rowVocabulary);
        QuerySpaceRequest queryRequest =
            QuerySpaceRequest.Create(
                querySpace.Descriptor,
                PortableQueryIntent.Create(
                    [
                        new(
                            "mode",
                            PortableQueryOperator.Equal,
                            "all"),
                    ],
                    [],
                    [],
                    []),
                ["results"],
                [
                    new(
                        "rows.application",
                        PortableQueryIntent.Create(
                            [
                                new(
                                    "score",
                                    PortableQueryOperator.AtLeast,
                                    "2"),
                            ],
                            [],
                            [PortableQueryStage.Head(2)],
                            [
                                PortableQueryOrderOperation.Fields(
                                    PortableQueryOrderRole.Baseline,
                                    [
                                        new(
                                            "score",
                                            PortableQueryDirection.Descending),
                                    ]),
                            ]),
                        ["results"]),
                ],
                QuerySpaceTerminalRequirement.Rows);

        QuerySpaceRowScopeBinding<ApplicationRow> rowScope =
            (QuerySpaceRowScopeBinding<ApplicationRow>)
                AssertSingle(querySpace.RowScopes);
        SectionRowSchemaIdentity<ApplicationRow> schema =
            SectionRowSchemaIdentity<ApplicationRow>.Create();
        var sectionScope =
            new SectionQuerySpaceRowScopeBinding<ApplicationRow>(
                rowScope,
                schema);
        var declaration =
            new SectionRowSetDeclaration<
                string,
                ApplicationRowsProjection,
                ApplicationRow>(
                    "results",
                    schema,
                    rows,
                    static (projection, selected) =>
                        projection with
                        {
                            Rows = selected,
                        });
        QuerySpaceSectionRowResolutionResult<
            ApplicationRowsProjection> resolution =
                QuerySpaceSectionRowResolver.Resolve(
                    querySpace,
                    queryRequest,
                    [declaration],
                    sectionScope);
        QuerySpaceSectionRowExecutionRequest<
            ApplicationRowsProjection> executionRequest =
                resolution.Request
            ?? throw new InvalidOperationException(
                "The direct-consumer request failed: "
                + $"{resolution.Failure?.RowQueryFailure.Reason}.");
        var resolvedAssociation =
            (ResolvedQuerySpaceRowAssociation<ApplicationRow>)
                resolution.Association!;
        SectionRowsOutcome<
            string,
            ApplicationRowsProjection> result =
                QuerySpaceSectionRowExecutor.ApplyRows(
                    executionRequest);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                "The direct-consumer execution failed.");
        }

        rows.Replace(
            1,
            new ApplicationRow("replacement", 100));

        ApplicationRowsProjection projection =
            result.Rebind(ApplicationRowsProjection.Empty);
        return new(
            [
                .. projection.Rows.Select(
                    static row => row.Name),
            ],
            typeof(ApplicationRow),
            typeof(ApplicationRows),
            queryRequest.QuerySpace,
            resolvedAssociation.Association.Scope,
            queryRequest.Terminal);
    }

    private static RowQueryVocabulary<ApplicationRow>
        CreateRowVocabulary()
    {
        RowQueryKey<ApplicationRow> score =
            RowQueryKey<ApplicationRow>.Create(
                RowQueryKeyIdentity.Create(),
                "score",
                [
                    RowQueryOperator.GreaterOrEqual
                ],
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
                static direction =>
                    RowQueryValueOrder.Create(
                        Comparer<int>.Default,
                        direction,
                        missingLast: true));
        return RowQueryVocabulary<ApplicationRow>.Create(
            RowQueryVocabularyIdentity.Create(),
            [score],
            []);
    }

    private static QuerySpaceBinding CreateQuerySpace(
        RowQueryVocabulary<ApplicationRow> vocabulary)
    {
        var operationVocabulary =
            new ApplicationOperationVocabulary();
        var applicability = new QueryOperationApplicability(
            ["application"],
            ["row"],
            ["results"]);
        QueryOperationDefinition<
            ApplicationOperationPredicate,
            ApplicationOperationPlan> operation =
                QueryOperationDefinition<
                    ApplicationOperationPredicate,
                    ApplicationOperationPlan>.Create(
                    "application.query",
                    operationVocabulary,
                    ["application"],
                    ["row"],
                    ["results"],
                    [
                        new(
                            "term.mode",
                            "mode",
                            QueryOperationTermRole.OperationSelector,
                            applicability,
                            new(
                                "Mode",
                                "text",
                                ["all"],
                                "Select the application row population."),
                            []),
                    ],
                    [],
                    [
                        new(
                            "default",
                            ["term.mode"],
                            []),
                    ]);
        QueryOperationRoute<
            ApplicationOperationPredicate,
            ApplicationOperationPlan> route =
                QueryOperationRoute<
                    ApplicationOperationPredicate,
                    ApplicationOperationPlan>.Create(
                    "application.rows",
                    operation,
                    "application",
                    "row",
                    ["results"],
                    "default",
                    [],
                    []);
        var rows =
            new QuerySpaceRowScopeBinding<ApplicationRow>(
                new QuerySpaceRowScopeDescriptor(
                    "rows.application",
                    "rows.application.vocabulary",
                    ["results"],
                    [
                        new(
                            "row.score",
                            "score",
                            [PortableQueryOperator.AtLeast],
                            "integer",
                            null,
                            "Score",
                            [],
                            "Filter and order by score.",
                            supportsOrdering: true),
                    ],
                    [],
                    [RowSelectionStageKind.Head]),
                vocabulary);

        return QuerySpaceBinding.Create(
            "application.space",
            route,
            [rows],
            [QuerySpaceTerminalRequirement.Rows],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    "application.rows"),
            ]);
    }

    private sealed record ApplicationRowsProjection(
        IReadOnlyList<ApplicationRow> Rows)
    {
        public static ApplicationRowsProjection Empty { get; } =
            new([]);
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values) =>
        values.Count == 1
            ? values[0]
            : throw new InvalidOperationException(
                $"Expected one value, found {values.Count}.");

    private sealed record ApplicationOperationPredicate(
        string Value);

    private sealed record ApplicationOperationPlan(
        PortableQueryResolvedIntent<ApplicationOperationPredicate> Resolved);

    private sealed class ApplicationOperationVocabulary :
        PortableQueryVocabulary<
            ApplicationOperationPredicate,
            ApplicationOperationPlan>
    {
        private static readonly ApplicationOperationKey Mode = new();

        public override string Identity => "application.operation";

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<
                ApplicationOperationPredicate>? declaration)
        {
            declaration = key == Mode.Key ? Mode : null;
            return declaration is not null;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<
                ApplicationOperationPredicate>? declaration)
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

        public override ApplicationOperationPlan CreatePlan(
            PortableQueryResolvedIntent<
                ApplicationOperationPredicate> resolved) =>
            new(resolved);
    }

    private sealed class ApplicationOperationKey :
        PortableQueryKeyDeclaration<ApplicationOperationPredicate>
    {
        public override string Key => "mode";

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator is PortableQueryOperator.Equal;

        public override PortableQueryBinding<
            ApplicationOperationPredicate> Bind(
            PortableQueryOperator @operator,
            string value) =>
            @operator is PortableQueryOperator.Equal
            && value is "all"
                ? PortableQueryBinding<
                    ApplicationOperationPredicate>.Bound(
                    "mode.all",
                    new(value))
                : PortableQueryBinding<
                    ApplicationOperationPredicate>.Rejected;
    }
}
