using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using ILInspector.Research;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public sealed record LibraryFamilyRoleQueryPlan(
    PortableQueryIntent Intent,
    LibraryNameFamilyPopulationKind Population);

public abstract record LibraryFamilyRoleQueryPlanResult
{
    private LibraryFamilyRoleQueryPlanResult()
    {
    }

    public sealed record Accepted(LibraryFamilyRoleQueryPlan Plan)
        : LibraryFamilyRoleQueryPlanResult;

    public sealed record Rejected(PortableQueryFailure Failure)
        : LibraryFamilyRoleQueryPlanResult;
}

public sealed record LibraryFamilyRoleSemanticSelectionFailure(
    int StageNumber,
    int RequiredPosition,
    int AvailableCount);

public abstract record LibraryFamilyRoleQueryResult
{
    private LibraryFamilyRoleQueryResult()
    {
    }

    public sealed record Available(
        LibraryFamilyRoleCompositionDocument Document,
        LibraryFamilyRolePopulation Population,
        QuerySpaceRequest Request,
        ImmutableArray<LibraryFamilyRoleRow> FamilyRows,
        ImmutableArray<LibraryFamilyRoleTypeRow> TypeRows,
        int TotalRowCount,
        int SelectedRowCount,
        int? Count)
        : LibraryFamilyRoleQueryResult;

    public sealed record NameFamiliesUnavailable(
        LibraryNameFamilySummaryOutcome.Unavailable Outcome)
        : LibraryFamilyRoleQueryResult;

    public sealed record NameFamiliesRejected(
        LibraryNameFamilySummaryOutcome.Rejected Outcome)
        : LibraryFamilyRoleQueryResult;

    public sealed record StructuralRejected(
        LibrarySurfaceLeverageResult.Rejected Outcome)
        : LibraryFamilyRoleQueryResult;

    public sealed record CompositionRejected(
        LibraryFamilyRoleCompositionOutcome.Rejected Outcome)
        : LibraryFamilyRoleQueryResult;

    public sealed record PopulationUnavailable(
        LibraryFamilyRoleCompositionDocument Document,
        LibraryNameFamilyPopulationKind RequestedPopulation)
        : LibraryFamilyRoleQueryResult;

    public sealed record SelectionFailed(
        string Detail,
        RowQueryFailure? ResolutionFailure = null,
        LibraryFamilyRoleSemanticSelectionFailure?
            SemanticFailure = null)
        : LibraryFamilyRoleQueryResult;

    public sealed record Failed(Exception Error)
        : LibraryFamilyRoleQueryResult;
}

public static partial class LibraryFamilyRoleQuery
{
    public const string OperationIdentity = "library-family-roles";
    public const string OperationRouteIdentity =
        "library-family-roles/default";
    public const string OperationSubjectRole = "exact-library";
    public const string OperationResultGrain = "library-family-role";
    public const string OperationProfileIdentity = "population-selection";
    public const string OperationVocabularyIdentity =
        "library-family-roles/operation/v1";
    public const string PopulationTermKey = "population";

    public const string QuerySpaceIdentity =
        "library-family-roles/query-space/v1";
    public const string FamilyRowsScopeIdentity =
        "library-family-roles/family-rows/v1";
    public const string TypeRowsScopeIdentity =
        "library-family-roles/type-rows/v1";
    public const string FamilyRowsResultContract =
        "library-family-roles/family-result/v1";
    public const string TypeRowsResultContract =
        "library-family-roles/type-result/v1";
    public const string FamilyRowsRowSet = "family-role-rows";
    public const string TypeRowsRowSet = "type-role-rows";

    public static InspectionQuery<LibraryFamilyRoleQueryResult> Definition
    { get; } =
        new("Library name-family roles", InspectionCost.Unbounded);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static LibraryFamilyRoleQueryPlan CreatePlan(
        LibraryNameFamilyPopulationKind population)
    {
        LibraryFamilyRoleQueryPlanResult result =
            ResolveIntent(CreateIntent(population));
        return result switch
        {
            LibraryFamilyRoleQueryPlanResult.Accepted accepted =>
                accepted.Plan,
            LibraryFamilyRoleQueryPlanResult.Rejected rejected =>
                throw new InvalidOperationException(
                    "A product-issued Library family-role operation plan "
                        + $"was rejected: {rejected.Failure.Reason}."),
            _ => throw new InvalidOperationException(
                "Unknown Library family-role operation-plan result."),
        };
    }

    public static LibraryFamilyRoleQueryPlanResult ResolveIntent(
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        PortableQueryResolution<LibraryFamilyRoleQueryPlan> resolution =
            OperationRegistration.Route.Resolve(
                intent,
                cancellationToken);
        return resolution.IsResolved
            ? new LibraryFamilyRoleQueryPlanResult.Accepted(
                resolution.Plan)
            : new LibraryFamilyRoleQueryPlanResult.Rejected(
                resolution.Failure);
    }

    public static QuerySpaceRequest CreateFamilyRequest(
        LibraryFamilyRoleQueryPlan operation,
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
        LibraryFamilyRoleQueryPlan operation,
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
        LibraryFamilyRoleQueryPlan operation,
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
            LibraryFamilyRoleQueryPlan>
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

        public override LibraryFamilyRoleQueryPlan CreatePlan(
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
            LibraryFamilyRoleQueryPlan> Definition =
                CreateDefinition();

        internal static readonly QueryOperationRoute<
            OperationPredicate,
            LibraryFamilyRoleQueryPlan> Route =
                QueryOperationRoute<
                    OperationPredicate,
                    LibraryFamilyRoleQueryPlan>.Create(
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
            LibraryFamilyRoleQueryPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [OperationSubjectRole],
                [OperationResultGrain],
                []);
            var population = new QueryOperationTermBinding(
                "library-family-roles.term.population",
                PopulationTermKey,
                QueryOperationTermRole.OperationSelector,
                applicability,
                new QueryOperationTermDescription(
                    "source population",
                    "family-role source population",
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
                LibraryFamilyRoleQueryPlan>.Create(
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
