using System.Diagnostics.CodeAnalysis;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

public sealed record TypeDeclaredMethodPopulationQueryPlan(
    PortableQueryIntent Intent);

public enum TypeDeclaredMethodPopulationQueryRejection
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentNotSupported,
    TerminalMismatch,
    ResultContractMismatch,
}

public abstract record TypeDeclaredMethodPopulationQueryResult
{
    private TypeDeclaredMethodPopulationQueryResult()
    {
    }

    public sealed record Accepted(
        TypeDeclaredMethodPopulationQueryPlan Plan,
        QuerySpaceTerminalRequirement Terminal)
        : TypeDeclaredMethodPopulationQueryResult;

    public sealed record Rejected(
        TypeDeclaredMethodPopulationQueryRejection Reason)
        : TypeDeclaredMethodPopulationQueryResult;

    public sealed record IntentRejected(PortableQueryFailure Failure)
        : TypeDeclaredMethodPopulationQueryResult;
}

public static class TypeDeclaredMethodPopulationQuery
{
    public const string OperationIdentity =
        "type-declared-method-population";
    public const string OperationRouteIdentity =
        "type-declared-method-population/default";
    public const string SubjectRole = "exact-type";
    public const string ResultGrain = "declared-method";
    public const string RowSet = "declared-methods";
    public const string OperationProfileIdentity = "default";
    public const string VocabularyIdentity =
        "type-declared-method-population/v1";
    public const string RowScopeIdentity =
        "type-declared-method-population/rows";
    public const string RowVocabularyIdentity =
        "type-declared-method-population/rows/v1";
    public const string QuerySpaceIdentity =
        "type-declared-method-population/query-space/v1";
    public const string RowsResultContract =
        "type-declared-method-population/rows-result/v1";
    public const string CountResultContract =
        "type-declared-method-population/count-result/v1";

    private static readonly QueryOperationDefinition<
        OperationPredicate,
        TypeDeclaredMethodPopulationQueryPlan> s_definition =
            QueryOperationDefinition<
                OperationPredicate,
                TypeDeclaredMethodPopulationQueryPlan>.Create(
                    OperationIdentity,
                    new OperationVocabulary(),
                    [SubjectRole],
                    [ResultGrain],
                    [RowSet],
                    [],
                    [],
                    [
                        new(
                            OperationProfileIdentity,
                            [],
                            []),
                    ]);

    private static readonly QueryOperationRoute<
        OperationPredicate,
        TypeDeclaredMethodPopulationQueryPlan> s_route =
            QueryOperationRoute<
                OperationPredicate,
                TypeDeclaredMethodPopulationQueryPlan>.Create(
                    OperationRouteIdentity,
                    s_definition,
                    SubjectRole,
                    ResultGrain,
                    [RowSet],
                    OperationProfileIdentity,
                    [],
                    []);

    public static IQueryOperationRoute OperationRoute => s_route;

    public static QuerySpaceBinding QuerySpace { get; } =
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            s_route,
            [
                new QuerySpaceRowScopeBinding<int>(
                    new(
                        RowScopeIdentity,
                        RowVocabularyIdentity,
                        [RowSet],
                        [],
                        [],
                        []),
                    RowQueryVocabulary<int>.Create(
                        RowQueryVocabularyIdentity.Create(),
                        [],
                        [])),
            ],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    RowsResultContract),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    CountResultContract),
            ]);

    public static QuerySpaceRequest CreateRequest(
        QuerySpaceTerminalRequirement terminal) =>
        QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            PortableQueryIntent.Empty,
            [RowSet],
            [],
            terminal);

    public static TypeDeclaredMethodPopulationQueryResult ResolveRequest(
        QuerySpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.QuerySpace.Equals(
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return new TypeDeclaredMethodPopulationQueryResult.Rejected(
                TypeDeclaredMethodPopulationQueryRejection
                    .QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets.Count != 1
            || !request.ParticipatingRowSets[0].Equals(
                RowSet,
                StringComparison.Ordinal))
        {
            return new TypeDeclaredMethodPopulationQueryResult.Rejected(
                TypeDeclaredMethodPopulationQueryRejection
                    .ParticipatingRowSetsMismatch);
        }
        if (request.RowIntents.Count != 0)
        {
            return new TypeDeclaredMethodPopulationQueryResult.Rejected(
                TypeDeclaredMethodPopulationQueryRejection
                    .RowIntentNotSupported);
        }
        if (request.Terminal
            is not QuerySpaceTerminalRequirement.Rows
                and not QuerySpaceTerminalRequirement.Count)
        {
            return new TypeDeclaredMethodPopulationQueryResult.Rejected(
                TypeDeclaredMethodPopulationQueryRejection
                    .TerminalMismatch);
        }

        string expectedContract =
            request.Terminal == QuerySpaceTerminalRequirement.Rows
                ? RowsResultContract
                : CountResultContract;
        if (!string.Equals(
                request.ResultContract,
                expectedContract,
                StringComparison.Ordinal))
        {
            return new TypeDeclaredMethodPopulationQueryResult.Rejected(
                TypeDeclaredMethodPopulationQueryRejection
                    .ResultContractMismatch);
        }

        PortableQueryResolution<TypeDeclaredMethodPopulationQueryPlan>
            resolution =
                s_route.Resolve(request.Operation, cancellationToken);
        return resolution.IsResolved
            ? new TypeDeclaredMethodPopulationQueryResult.Accepted(
                resolution.Plan,
                request.Terminal)
            : new TypeDeclaredMethodPopulationQueryResult.IntentRejected(
                resolution.Failure);
    }

    private readonly record struct OperationPredicate;

    private sealed class OperationVocabulary
        : PortableQueryVocabulary<
            OperationPredicate,
            TypeDeclaredMethodPopulationQueryPlan>
    {
        public override string Identity => VocabularyIdentity;

        public override IReadOnlyList<string> RequiredDimensions => [];

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<OperationPredicate>?
                declaration)
        {
            declaration = null;
            return false;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<OperationPredicate>?
                declaration)
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

        public override bool IsOrderable(string key) => false;

        public override bool CollapsesDuplicateBindings => true;

        public override bool AreTermsCompatible(
            PortableQueryResolvedTerm<OperationPredicate> first,
            PortableQueryResolvedTerm<OperationPredicate> second) =>
            true;

        public override TypeDeclaredMethodPopulationQueryPlan CreatePlan(
            PortableQueryResolvedIntent<OperationPredicate> resolved) =>
            new(
                PortableQueryIntent.Create(
                    [.. resolved.Terms.Select(term => term.Term)],
                    [.. resolved.Bounds],
                    [.. resolved.Stages],
                    []));
    }
}
