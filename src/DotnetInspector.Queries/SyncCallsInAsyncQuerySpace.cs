using System.Diagnostics.CodeAnalysis;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

internal readonly record struct SyncCallsInAsyncOperationPredicate;

internal readonly record struct SyncCallsInAsyncCallRow;

internal sealed record SyncCallsInAsyncOperationPlan;

internal sealed class SyncCallsInAsyncOperationVocabulary
    : PortableQueryVocabulary<
        SyncCallsInAsyncOperationPredicate,
        SyncCallsInAsyncOperationPlan>
{
    public override string Identity =>
        SyncCallsInAsyncQuerySpace.OperationVocabularyIdentity;

    public override IReadOnlyList<string> RequiredDimensions => [];

    public override bool TryGetKey(
        string key,
        [NotNullWhen(true)]
        out PortableQueryKeyDeclaration<
            SyncCallsInAsyncOperationPredicate>? declaration)
    {
        declaration = null;
        return false;
    }

    public override bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)]
        out PortableQueryDimensionDeclaration<
            SyncCallsInAsyncOperationPredicate>? declaration)
    {
        declaration = null;
        return false;
    }

    public override bool AdmitsStageKind(RowSelectionStageKind kind) => false;

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
        PortableQueryResolvedTerm<
            SyncCallsInAsyncOperationPredicate> first,
        PortableQueryResolvedTerm<
            SyncCallsInAsyncOperationPredicate> second) =>
        true;

    public override SyncCallsInAsyncOperationPlan CreatePlan(
        PortableQueryResolvedIntent<
            SyncCallsInAsyncOperationPredicate> resolved) =>
        new();
}

/// <summary>
/// QuerySpace declarations for independently closed Sync Calls in Async
/// requests over one managed-assembly resource.
/// </summary>
internal static class SyncCallsInAsyncQuerySpace
{
    internal const string OperationVocabularyIdentity =
        "sync-calls-in-async/operation/v1";

    const string CallsRowSet = "sync-calls-in-async";

    const string CallsRowScopeIdentity =
        "sync-calls-in-async/calls/v1";

    const string CallsRowVocabularyIdentity =
        "sync-calls-in-async/call-rows/v1";

    static readonly string[] CallRowSets = [CallsRowSet];

    const string RowsResultContract = "sync-calls-in-async/rows/v1";

    const string CountResultContract = "sync-calls-in-async/count/v1";

    const string ExistsResultContract = "sync-calls-in-async/exists/v1";

    static readonly QueryOperationDefinition<
        SyncCallsInAsyncOperationPredicate,
        SyncCallsInAsyncOperationPlan> OperationDefinition =
        QueryOperationDefinition<
            SyncCallsInAsyncOperationPredicate,
            SyncCallsInAsyncOperationPlan>.Create(
                "sync-calls-in-async",
                new SyncCallsInAsyncOperationVocabulary(),
                ["managed-assembly"],
                ["method"],
                CallRowSets,
                [],
                [],
                [
                    new(
                        "default",
                        [],
                        []),
                ]);

    static readonly QueryOperationRoute<
        SyncCallsInAsyncOperationPredicate,
        SyncCallsInAsyncOperationPlan> Route =
        QueryOperationRoute<
            SyncCallsInAsyncOperationPredicate,
            SyncCallsInAsyncOperationPlan>.Create(
                "sync-calls-in-async/default",
                OperationDefinition,
                "managed-assembly",
                "method",
                CallRowSets,
                "default",
                [],
                []);

    static readonly QuerySpaceRowScopeBinding<
        SyncCallsInAsyncCallRow> CallsRowScope =
            new(
                new QuerySpaceRowScopeDescriptor(
                    CallsRowScopeIdentity,
                    CallsRowVocabularyIdentity,
                    CallRowSets,
                    [],
                    [],
                    []),
                RowQueryVocabulary<
                    SyncCallsInAsyncCallRow>.Create(
                        RowQueryVocabularyIdentity.Create(),
                        [],
                        []));

    static QuerySpaceBinding QuerySpace { get; } =
        QuerySpaceBinding.Create(
            "sync-calls-in-async/query-space/v1",
            Route,
            [CallsRowScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
                QuerySpaceTerminalRequirement.Exists,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    RowsResultContract),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    CountResultContract),
                new(
                    QuerySpaceTerminalRequirement.Exists,
                    ExistsResultContract),
            ]);

    internal static QuerySpaceRequest CreateRequest(
        SyncCallsInAsyncClosing closing) =>
        QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            PortableQueryIntent.Empty,
            [CallsRowSet],
            [],
            closing switch
            {
                SyncCallsInAsyncClosing.Rows =>
                    QuerySpaceTerminalRequirement.Rows,
                SyncCallsInAsyncClosing.Count =>
                    QuerySpaceTerminalRequirement.Count,
                SyncCallsInAsyncClosing.Exists =>
                    QuerySpaceTerminalRequirement.Exists,
                _ => throw new ArgumentOutOfRangeException(nameof(closing)),
            });
}
