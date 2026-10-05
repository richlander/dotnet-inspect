using System.Diagnostics.CodeAnalysis;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.ResearchQueries;

public sealed record ReturnToSenderTargetQueryPlan(
    PortableQueryIntent Intent);

public enum ReturnToSenderTargetQueryRejectionKind
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentMismatch,
    TerminalMismatch,
    ResultContractMismatch,
}

public abstract record ReturnToSenderTargetQueryResolution
{
    private ReturnToSenderTargetQueryResolution()
    {
    }

    public sealed record Accepted(
        ReturnToSenderTargetQueryPlan Plan)
        : ReturnToSenderTargetQueryResolution;

    public sealed record Rejected(
        ReturnToSenderTargetQueryRejectionKind Kind)
        : ReturnToSenderTargetQueryResolution;

    public sealed record IntentRejected(
        PortableQueryFailure Failure)
        : ReturnToSenderTargetQueryResolution;
}

public static class ReturnToSenderTargetQuery
{
    public const string OperationIdentity =
        "return-to-sender-target-population";
    public const string OperationRouteIdentity =
        "return-to-sender-target-population/default";
    public const string SubjectRole = "assembly-set";
    public const string ResultGrain = "target-candidate";
    public const string RowSet = "target-candidates";
    public const string OperationProfileIdentity = "default";
    public const string VocabularyIdentity =
        "return-to-sender-target-population/v1";
    public const string RowScopeIdentity =
        "return-to-sender-target-population/rows";
    public const string QuerySpaceIdentity =
        "return-to-sender-target-population/query-space/v1";
    public const string RowVocabularyIdentity =
        "return-to-sender-target-population/rows/v1";
    public const string CountResultContract =
        "return-to-sender-target-population/count/v1";

    private static readonly Lazy<QuerySpaceBinding> QuerySpaceValue =
        new(CreateQuerySpace);

    public static QuerySpaceBinding QuerySpace =>
        QuerySpaceValue.Value;

    public static IQueryOperationRoute OperationRoute =>
        RegisteredOperationRoute;

    public static QuerySpaceRowScopeBinding<
        ReturnToSenderTarget> RowScope =>
        (QuerySpaceRowScopeBinding<ReturnToSenderTarget>)
            QuerySpace.RowScopes.Single();

    public static QuerySpaceRequest CreateCountRequest() =>
        QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            PortableQueryIntent.Empty,
            [RowSet],
            [
                new(
                    RowScopeIdentity,
                    PortableQueryIntent.Empty,
                    [RowSet]),
            ],
            QuerySpaceTerminalRequirement.Count);

    public static ReturnToSenderTargetQueryResolution ResolveRequest(
        QuerySpaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(
                request.QuerySpace,
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return new ReturnToSenderTargetQueryResolution.Rejected(
                ReturnToSenderTargetQueryRejectionKind
                    .QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets.Count != 1
            || !string.Equals(
                request.ParticipatingRowSets[0],
                RowSet,
                StringComparison.Ordinal))
        {
            return new ReturnToSenderTargetQueryResolution.Rejected(
                ReturnToSenderTargetQueryRejectionKind
                    .ParticipatingRowSetsMismatch);
        }
        if (request.RowIntents.Count != 1
            || !string.Equals(
                request.RowIntents[0].Scope,
                RowScopeIdentity,
                StringComparison.Ordinal)
            || request.RowIntents[0].RowSets.Count != 1
            || !string.Equals(
                request.RowIntents[0].RowSets[0],
                RowSet,
                StringComparison.Ordinal)
            || !IsEmpty(request.RowIntents[0].Intent))
        {
            return new ReturnToSenderTargetQueryResolution.Rejected(
                ReturnToSenderTargetQueryRejectionKind
                    .RowIntentMismatch);
        }
        if (request.Terminal
            is not QuerySpaceTerminalRequirement.Count)
        {
            return new ReturnToSenderTargetQueryResolution.Rejected(
                ReturnToSenderTargetQueryRejectionKind
                    .TerminalMismatch);
        }
        if (!string.Equals(
                request.ResultContract,
                CountResultContract,
                StringComparison.Ordinal))
        {
            return new ReturnToSenderTargetQueryResolution.Rejected(
                ReturnToSenderTargetQueryRejectionKind
                    .ResultContractMismatch);
        }

        PortableQueryResolution<ReturnToSenderTargetQueryPlan>
            resolution =
                RegisteredOperationRoute.Resolve(
                    request.Operation);
        return resolution.IsResolved
            ? new ReturnToSenderTargetQueryResolution.Accepted(
                resolution.Plan)
            : new ReturnToSenderTargetQueryResolution.IntentRejected(
                resolution.Failure);
    }

    private static QueryOperationRoute<
        OperationPredicate,
        ReturnToSenderTargetQueryPlan> RegisteredOperationRoute
    { get; } = CreateOperationRoute();

    private static QueryOperationRoute<
        OperationPredicate,
        ReturnToSenderTargetQueryPlan> CreateOperationRoute()
    {
        var vocabulary = new OperationVocabulary();
        QueryOperationDefinition<
            OperationPredicate,
            ReturnToSenderTargetQueryPlan> operation =
                QueryOperationDefinition<
                    OperationPredicate,
                    ReturnToSenderTargetQueryPlan>.Create(
                    OperationIdentity,
                    vocabulary,
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
        return QueryOperationRoute<
            OperationPredicate,
            ReturnToSenderTargetQueryPlan>.Create(
                OperationRouteIdentity,
                operation,
                SubjectRole,
                ResultGrain,
                [RowSet],
                OperationProfileIdentity,
                [],
                []);
    }

    private static QuerySpaceBinding CreateQuerySpace()
    {
        var rowScope =
            new QuerySpaceRowScopeBinding<ReturnToSenderTarget>(
                new(
                    RowScopeIdentity,
                    RowVocabularyIdentity,
                    [RowSet],
                    [],
                    [],
                    []),
                RowQueryVocabulary<
                    ReturnToSenderTarget>.Create(
                        RowQueryVocabularyIdentity.Create(),
                        [],
                        []));
        return QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            RegisteredOperationRoute,
            [rowScope],
            [QuerySpaceTerminalRequirement.Count],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Count,
                    CountResultContract),
            ]);
    }

    private sealed record OperationPredicate;

    private static bool IsEmpty(PortableQueryIntent intent) =>
        intent.Terms.Count == 0
        && intent.Bounds.Count == 0
        && intent.Stages.Count == 0
        && intent.Order.Count == 0;

    private sealed class OperationVocabulary :
        PortableQueryVocabulary<
            OperationPredicate,
            ReturnToSenderTargetQueryPlan>
    {
        public override string Identity => VocabularyIdentity;

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<
                OperationPredicate>? declaration)
        {
            declaration = null;
            return false;
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

        public override ReturnToSenderTargetQueryPlan CreatePlan(
            PortableQueryResolvedIntent<
                OperationPredicate> resolved) =>
            new(PortableQueryIntent.Empty);
    }
}
