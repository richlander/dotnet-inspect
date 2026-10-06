using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using ILInspector.Research;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public sealed record LibraryArchitecturalFamilyQueryPlan(
    PortableQueryIntent Intent,
    LibraryNameFamilyPopulationKind Population);

public abstract record LibraryArchitecturalFamilyQueryPlanResult
{
    private LibraryArchitecturalFamilyQueryPlanResult()
    {
    }

    public sealed record Accepted(LibraryArchitecturalFamilyQueryPlan Plan)
        : LibraryArchitecturalFamilyQueryPlanResult;

    public sealed record Rejected(PortableQueryFailure Failure)
        : LibraryArchitecturalFamilyQueryPlanResult;
}

public sealed record LibraryArchitecturalFamilySemanticSelectionFailure(
    int StageNumber,
    int RequiredPosition,
    int AvailableCount);

public abstract record LibraryArchitecturalFamilyQueryResult
{
    private LibraryArchitecturalFamilyQueryResult()
    {
    }

    public sealed record Available(
        LibraryArchitecturalFamilyCompositionDocument Document,
        LibraryArchitecturalFamilyPopulation Population,
        QuerySpaceRequest Request,
        ImmutableArray<LibraryArchitecturalFamilyRow> FamilyRows,
        ImmutableArray<LibraryArchitecturalFamilyTypeRow> TypeRows,
        int TotalRowCount,
        int SelectedRowCount,
        int? Count)
        : LibraryArchitecturalFamilyQueryResult;

    public sealed record NameFamiliesUnavailable(
        LibraryNameFamilySummaryOutcome.Unavailable Outcome)
        : LibraryArchitecturalFamilyQueryResult;

    public sealed record NameFamiliesRejected(
        LibraryNameFamilySummaryOutcome.Rejected Outcome)
        : LibraryArchitecturalFamilyQueryResult;

    public sealed record StructuralRejected(
        LibrarySurfaceLeverageResult.Rejected Outcome)
        : LibraryArchitecturalFamilyQueryResult;

    public sealed record CompositionRejected(
        LibraryArchitecturalFamilyCompositionOutcome.Rejected Outcome)
        : LibraryArchitecturalFamilyQueryResult;

    public sealed record PopulationUnavailable(
        LibraryArchitecturalFamilyCompositionDocument Document,
        LibraryNameFamilyPopulationKind RequestedPopulation)
        : LibraryArchitecturalFamilyQueryResult;

    public sealed record SelectionFailed(
        string Detail,
        RowQueryFailure? ResolutionFailure = null,
        LibraryArchitecturalFamilySemanticSelectionFailure?
            SemanticFailure = null)
        : LibraryArchitecturalFamilyQueryResult;

    public sealed record Failed(Exception Error)
        : LibraryArchitecturalFamilyQueryResult;
}

public static partial class LibraryArchitecturalFamilyQuery
{
    public const string OperationIdentity = "architectural-families";
    public const string OperationRouteIdentity =
        "architectural-families/default";
    public const string OperationSubjectRole = "exact-library";
    public const string OperationResultGrain = "architectural-family";
    public const string OperationProfileIdentity = "population-selection";
    public const string OperationVocabularyIdentity =
        "architectural-families/operation/v1";
    public const string PopulationTermKey = "population";

    public const string QuerySpaceIdentity =
        "architectural-families/query-space/v1";
    public const string FamilyRowsScopeIdentity =
        "architectural-families/family-rows/v1";
    public const string TypeRowsScopeIdentity =
        "architectural-families/type-rows/v1";
    public const string FamilyRowsResultContract =
        "architectural-families/family-result/v1";
    public const string TypeRowsResultContract =
        "architectural-families/type-result/v1";
    public const string FamilyRowsRowSet = "families";
    public const string TypeRowsRowSet = "types";

    public static InspectionQuery<LibraryArchitecturalFamilyQueryResult> Definition
    { get; } =
        new("Library architectural families", InspectionCost.Unbounded);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static LibraryArchitecturalFamilyQueryPlan CreatePlan(
        LibraryNameFamilyPopulationKind population)
    {
        LibraryArchitecturalFamilyQueryPlanResult result =
            ResolveIntent(CreateIntent(population));
        return result switch
        {
            LibraryArchitecturalFamilyQueryPlanResult.Accepted accepted =>
                accepted.Plan,
            LibraryArchitecturalFamilyQueryPlanResult.Rejected rejected =>
                throw new InvalidOperationException(
                    "A product-issued Architectural Families operation plan "
                        + $"was rejected: {rejected.Failure.Reason}."),
            _ => throw new InvalidOperationException(
                "Unknown Architectural Families operation-plan result."),
        };
    }

    public static LibraryArchitecturalFamilyQueryPlanResult ResolveIntent(
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        PortableQueryResolution<LibraryArchitecturalFamilyQueryPlan> resolution =
            OperationRegistration.Route.Resolve(
                intent,
                cancellationToken);
        return resolution.IsResolved
            ? new LibraryArchitecturalFamilyQueryPlanResult.Accepted(
                resolution.Plan)
            : new LibraryArchitecturalFamilyQueryPlanResult.Rejected(
                resolution.Failure);
    }

    public static QuerySpaceRequest CreateFamilyRequest(
        LibraryArchitecturalFamilyQueryPlan operation,
        RowSelectionIntent<string> rows,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return CreateRequest(
            operation,
            FamilyRowsScope,
            FamilyRowsRowSet,
            PortableQueryIntent.Create(
                [],
                [],
                PortableQueryRowSelection.ToStages(rows),
                []),
            terminal);
    }

    public static QuerySpaceRequest CreateTypeRequest(
        LibraryArchitecturalFamilyQueryPlan operation,
        RowSelectionIntent<string> rows,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return CreateRequest(
            operation,
            TypeRowsScope,
            TypeRowsRowSet,
            PortableQueryIntent.Create(
                [],
                [],
                PortableQueryRowSelection.ToStages(rows),
                []),
            terminal);
    }

    public static QuerySpaceRequest CreateRequest(
        LibraryArchitecturalFamilyQueryPlan operation,
        QuerySpaceRowScopeBinding scope,
        string rowSet,
        PortableQueryIntent rows,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(rowSet);
        ArgumentNullException.ThrowIfNull(rows);

        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            operation.Intent,
            [rowSet],
            [
                new(
                    scope.Descriptor.Identity,
                    rows,
                    [rowSet]),
            ],
            terminal);
    }

    public static string PopulationToken(
        LibraryNameFamilyPopulationKind population) =>
        LibraryNameFamilyQuery.PopulationToken(population);

    public static bool TryParsePopulation(
        string value,
        out LibraryNameFamilyPopulationKind population) =>
        LibraryNameFamilyQuery.TryParsePopulation(
            value,
            out population);

    private static PortableQueryIntent CreateIntent(
        LibraryNameFamilyPopulationKind population)
    {
        if (!Enum.IsDefined(population))
        {
            throw new ArgumentOutOfRangeException(
                nameof(population));
        }

        return PortableQueryIntent.Create(
            [
                new(
                    PopulationTermKey,
                    PortableQueryOperator.Equal,
                    PopulationToken(population)),
            ],
            [],
            [],
            []);
    }

    private sealed record OperationPredicate(
        LibraryNameFamilyPopulationKind Population);

    private sealed class PopulationDeclaration
        : PortableQueryKeyDeclaration<OperationPredicate>
    {
        public override string Key => PopulationTermKey;

        public override string? Family => PopulationTermKey;

        public override PortableQueryFamilyKind FamilyKind =>
            PortableQueryFamilyKind.Exclusive;

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator == PortableQueryOperator.Equal;

        public override PortableQueryBinding<OperationPredicate> Bind(
            PortableQueryOperator @operator,
            string value)
        {
            if (@operator != PortableQueryOperator.Equal
                || !TryParsePopulation(value, out var population))
            {
                return PortableQueryBinding<
                    OperationPredicate>.Rejected;
            }

            return PortableQueryBinding<OperationPredicate>.Bound(
                $"population:{PopulationToken(population)}",
                new(population));
        }
    }

    private sealed class OperationVocabulary
        : PortableQueryVocabulary<
            OperationPredicate,
            LibraryArchitecturalFamilyQueryPlan>
    {
        private static readonly PopulationDeclaration Population = new();

        public override string Identity =>
            OperationVocabularyIdentity;

        public override IReadOnlyList<string> RequiredTermFamilies =>
            [PopulationTermKey];

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<
                OperationPredicate>? declaration)
        {
            bool found = string.Equals(
                key,
                PopulationTermKey,
                StringComparison.Ordinal);
            declaration = found ? Population : null;
            return found;
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
            RowSelectionStageKind kind) => false;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) => false;

        public override LibraryArchitecturalFamilyQueryPlan CreatePlan(
            PortableQueryResolvedIntent<OperationPredicate> resolved)
        {
            OperationPredicate population =
                resolved.Terms.Single().Predicate;
            return new(
                PortableQueryIntent.Create(
                    [.. resolved.Terms.Select(term => term.Term)],
                    [],
                    [],
                    []),
                population.Population);
        }
    }

    private static class OperationRegistration
    {
        private static readonly OperationVocabulary Vocabulary = new();

        internal static readonly QueryOperationDefinition<
            OperationPredicate,
            LibraryArchitecturalFamilyQueryPlan> Definition =
                CreateDefinition();

        internal static readonly QueryOperationRoute<
            OperationPredicate,
            LibraryArchitecturalFamilyQueryPlan> Route =
                QueryOperationRoute<
                    OperationPredicate,
                    LibraryArchitecturalFamilyQueryPlan>.Create(
                        OperationRouteIdentity,
                        Definition,
                        OperationSubjectRole,
                        OperationResultGrain,
                        [
                            FamilyRowsRowSet,
                            TypeRowsRowSet,
                        ],
                        OperationProfileIdentity,
                        [],
                        []);

        private static QueryOperationDefinition<
            OperationPredicate,
            LibraryArchitecturalFamilyQueryPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [OperationSubjectRole],
                [OperationResultGrain],
                []);
            var population = new QueryOperationTermBinding(
                "architectural-families.term.population",
                PopulationTermKey,
                QueryOperationTermRole.OperationSelector,
                applicability,
                new QueryOperationTermDescription(
                    "source population",
                    "architectural-family source population",
                    [
                        "all",
                        "ordinary",
                        "generated",
                        "mixed",
                        "unknown",
                    ],
                    "Selects one owner-issued complete Type population."),
                []);

            return QueryOperationDefinition<
                OperationPredicate,
                LibraryArchitecturalFamilyQueryPlan>.Create(
                    OperationIdentity,
                    Vocabulary,
                    [OperationSubjectRole],
                    [OperationResultGrain],
                    [
                        FamilyRowsRowSet,
                        TypeRowsRowSet,
                    ],
                    [population],
                    [],
                    [
                        new(
                            OperationProfileIdentity,
                            [population.Identity],
                            []),
                    ]);
        }
    }
}
