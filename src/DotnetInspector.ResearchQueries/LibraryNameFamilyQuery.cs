using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using ILInspector.Research;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public sealed record LibraryNameFamilyQueryPlan(
    PortableQueryIntent Intent,
    LibraryNameFamilyPopulationKind Population);

public abstract record LibraryNameFamilyQueryPlanResult
{
    private LibraryNameFamilyQueryPlanResult()
    {
    }

    public sealed record Accepted(LibraryNameFamilyQueryPlan Plan)
        : LibraryNameFamilyQueryPlanResult;

    public sealed record Rejected(PortableQueryFailure Failure)
        : LibraryNameFamilyQueryPlanResult;
}

public enum LibraryNameFamilyQueryUnavailableReason
{
    ProducerUnavailable,
    PopulationUnavailable,
}

public sealed record LibraryNameFamilySemanticSelectionFailure(
    int StageNumber,
    int RequiredPosition,
    int AvailableCount);

public abstract record LibraryNameFamilyQueryResult
{
    private LibraryNameFamilyQueryResult()
    {
    }

    public sealed record Available(
        LibraryNameFamilyDocument Document,
        LibraryNameFamilyPopulation Population,
        QuerySpaceRequest Request,
        ImmutableArray<LibraryNameFamilyRow> FamilyRows,
        ImmutableArray<LibraryNameFamilyTypeRow> TypeRows,
        int? Count)
        : LibraryNameFamilyQueryResult;

    public sealed record Unavailable(
        LibraryNameFamilyQueryUnavailableReason Reason,
        string Detail,
        LibraryNameFamilySummaryOutcome.Unavailable? Producer = null,
        LibraryNameFamilyDocument? Document = null,
        LibraryNameFamilyPopulationKind? RequestedPopulation = null)
        : LibraryNameFamilyQueryResult;

    public sealed record Rejected(
        LibraryNameFamilySummaryOutcome.Rejected Outcome)
        : LibraryNameFamilyQueryResult;

    public sealed record SelectionFailed(
        string Detail,
        RowQueryFailure? ResolutionFailure = null,
        LibraryNameFamilySemanticSelectionFailure?
            SemanticFailure = null)
        : LibraryNameFamilyQueryResult;

    public sealed record Failed(Exception Error)
        : LibraryNameFamilyQueryResult;
}

public static partial class LibraryNameFamilyQuery
{
    public const string OperationIdentity = "library-name-families";
    public const string OperationRouteIdentity =
        "library-name-families/default";
    public const string OperationSubjectRole = "exact-library";
    public const string OperationResultGrain = "library-name-family";
    public const string OperationProfileIdentity = "population-selection";
    public const string OperationVocabularyIdentity =
        "library-name-families/operation/v1";
    public const string PopulationTermKey = "population";

    public const string QuerySpaceIdentity =
        "library-name-families/query-space/v1";
    public const string FamilyRowsScopeIdentity =
        "library-name-families/family-rows/v1";
    public const string TypeRowsScopeIdentity =
        "library-name-families/type-rows/v1";
    public const string FamilyRowsResultContract =
        "library-name-families/family-result/v1";
    public const string TypeRowsResultContract =
        "library-name-families/type-result/v1";
    public const string FamilyRowsRowSet = "name-families";
    public const string TypeRowsRowSet = "name-family-types";

    public static InspectionQuery<LibraryNameFamilyQueryResult> Definition
    { get; } =
        new("Library name families", InspectionCost.Unbounded);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static LibraryNameFamilyQueryPlan CreatePlan(
        LibraryNameFamilyPopulationKind population)
    {
        LibraryNameFamilyQueryPlanResult result =
            ResolveIntent(CreateIntent(population));
        return result switch
        {
            LibraryNameFamilyQueryPlanResult.Accepted accepted =>
                accepted.Plan,
            LibraryNameFamilyQueryPlanResult.Rejected rejected =>
                throw new InvalidOperationException(
                    "A product-issued Library name-family operation plan "
                        + $"was rejected: {rejected.Failure.Reason}."),
            _ => throw new InvalidOperationException(
                "Unknown Library name-family operation-plan result."),
        };
    }

    public static LibraryNameFamilyQueryPlanResult ResolveIntent(
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        PortableQueryResolution<LibraryNameFamilyQueryPlan> resolution =
            OperationRegistration.Route.Resolve(
                intent,
                cancellationToken);
        return resolution.IsResolved
            ? new LibraryNameFamilyQueryPlanResult.Accepted(
                resolution.Plan)
            : new LibraryNameFamilyQueryPlanResult.Rejected(
                resolution.Failure);
    }

    public static QuerySpaceRequest CreateFamilyRequest(
        LibraryNameFamilyQueryPlan operation,
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
        LibraryNameFamilyQueryPlan operation,
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
        LibraryNameFamilyQueryPlan operation,
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

    public static string PopulationToken(
        LibraryNameFamilyPopulationKind population) =>
        population switch
        {
            LibraryNameFamilyPopulationKind.AllTypes => "all",
            LibraryNameFamilyPopulationKind.OrdinaryEvidenceOnly =>
                "ordinary",
            LibraryNameFamilyPopulationKind.GeneratedEvidenceOnly =>
                "generated",
            LibraryNameFamilyPopulationKind.MixedEvidence => "mixed",
            LibraryNameFamilyPopulationKind.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(
                nameof(population)),
        };

    public static bool TryParsePopulation(
        string value,
        out LibraryNameFamilyPopulationKind population)
    {
        population = value.ToLowerInvariant() switch
        {
            "all" => LibraryNameFamilyPopulationKind.AllTypes,
            "ordinary" =>
                LibraryNameFamilyPopulationKind.OrdinaryEvidenceOnly,
            "generated" =>
                LibraryNameFamilyPopulationKind.GeneratedEvidenceOnly,
            "mixed" => LibraryNameFamilyPopulationKind.MixedEvidence,
            "unknown" => LibraryNameFamilyPopulationKind.Unknown,
            _ => default,
        };
        return value.Equals(
                PopulationToken(population),
                StringComparison.OrdinalIgnoreCase);
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
            LibraryNameFamilyQueryPlan>
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

        public override LibraryNameFamilyQueryPlan CreatePlan(
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
            LibraryNameFamilyQueryPlan> Definition =
                CreateDefinition();

        internal static readonly QueryOperationRoute<
            OperationPredicate,
            LibraryNameFamilyQueryPlan> Route =
                QueryOperationRoute<
                    OperationPredicate,
                    LibraryNameFamilyQueryPlan>.Create(
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
            LibraryNameFamilyQueryPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [OperationSubjectRole],
                [OperationResultGrain],
                []);
            var population = new QueryOperationTermBinding(
                "library-name-families.term.population",
                PopulationTermKey,
                QueryOperationTermRole.OperationSelector,
                applicability,
                new QueryOperationTermDescription(
                    "source population",
                    "name-family source population",
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
                LibraryNameFamilyQueryPlan>.Create(
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
