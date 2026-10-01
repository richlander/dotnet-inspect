using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

internal readonly record struct UnsafeEvidencePresenceOperationPredicate;

internal readonly record struct UnsafeEvidenceMethodDefinitionRow;

public sealed record UnsafeEvidencePresenceQueryPlan(
    PortableQueryIntent Intent);

internal sealed class UnsafeEvidencePresenceOperationVocabulary
    : PortableQueryVocabulary<
        UnsafeEvidencePresenceOperationPredicate,
        UnsafeEvidencePresenceQueryPlan>
{
    public override string Identity =>
        UnsafeEvidencePresenceQuery.OperationVocabularyIdentity;

    public override IReadOnlyList<string> RequiredDimensions => [];

    public override bool TryGetKey(
        string key,
        [NotNullWhen(true)]
        out PortableQueryKeyDeclaration<
            UnsafeEvidencePresenceOperationPredicate>? declaration)
    {
        declaration = null;
        return false;
    }

    public override bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)]
        out PortableQueryDimensionDeclaration<
            UnsafeEvidencePresenceOperationPredicate>? declaration)
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
            UnsafeEvidencePresenceOperationPredicate> first,
        PortableQueryResolvedTerm<
            UnsafeEvidencePresenceOperationPredicate> second) =>
        true;

    public override UnsafeEvidencePresenceQueryPlan CreatePlan(
        PortableQueryResolvedIntent<
            UnsafeEvidencePresenceOperationPredicate> resolved) =>
        new(
            PortableQueryIntent.Create(
                [.. resolved.Terms.Select(term => term.Term)],
                [.. resolved.Bounds],
                [.. resolved.Stages],
                []));
}

public enum UnsafeEvidencePresenceRequestRejectionKind
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentNotSupported,
    TerminalMismatch,
    ResultContractMismatch,
}

public abstract record UnsafeEvidencePresenceRequestResolution
{
    private UnsafeEvidencePresenceRequestResolution()
    {
    }

    public sealed record Accepted(
        UnsafeEvidencePresenceQueryPlan Plan,
        WorkDescription Work)
        : UnsafeEvidencePresenceRequestResolution;

    public sealed record Rejected(
        UnsafeEvidencePresenceRequestRejectionKind Kind)
        : UnsafeEvidencePresenceRequestResolution;

    public sealed record IntentRejected(PortableQueryFailure Failure)
        : UnsafeEvidencePresenceRequestResolution;
}

/// <summary>Typed result of probing whether an assembly contains any unsafe evidence.</summary>
public abstract record UnsafeEvidencePresenceResult
{
    private UnsafeEvidencePresenceResult()
    {
    }

    /// <summary>The probe completed and reports whether at least one finding exists.</summary>
    public sealed record Available(
        bool HasEvidence,
        MethodDefinitionSourceReceipt SourceReceipt,
        WorkReceipt Receipt)
        : UnsafeEvidencePresenceResult;

    /// <summary>The producer did not complete after execution began.</summary>
    public sealed record ExecutionIncomplete(
        InvalidDataException Error,
        ProducerOutcome Outcome,
        MethodDefinitionSourceReceipt SourceReceipt,
        WorkReceipt Receipt)
        : UnsafeEvidencePresenceResult;

    /// <summary>The probe failed while reading the retained assembly context.</summary>
    public sealed record Failed(Exception Error) : UnsafeEvidencePresenceResult;
}

/// <summary>
/// Stops at the first unsafe finding without materializing the complete unsafe-evidence census.
/// </summary>
public static class UnsafeEvidencePresenceQuery
{
    public const string OperationIdentity =
        "unsafe-evidence-presence";

    public const string OperationRouteIdentity =
        "unsafe-evidence-presence/default";

    public const string OperationVocabularyIdentity =
        "unsafe-evidence-presence/operation/v1";

    public const string OperationSubjectRole = "managed-assembly";

    public const string OperationResultGrain = "library";

    public const string OperationProfileIdentity = "default";

    public const string QuerySpaceIdentity =
        "unsafe-evidence-presence/query-space/v1";

    public const string MethodDefinitionsRowSet =
        "method-definitions";

    public const string MethodDefinitionsRowScopeIdentity =
        "unsafe-evidence-presence/method-definitions/v1";

    public const string MethodDefinitionsRowVocabularyIdentity =
        "unsafe-evidence-presence/method-definition-rows/v1";

    public const string ResultContract =
        "unsafe-evidence-presence/result/v1";

    private static readonly QueryOperationDefinition<
        UnsafeEvidencePresenceOperationPredicate,
        UnsafeEvidencePresenceQueryPlan> OperationDefinition =
        QueryOperationDefinition<
            UnsafeEvidencePresenceOperationPredicate,
            UnsafeEvidencePresenceQueryPlan>.Create(
                OperationIdentity,
                new UnsafeEvidencePresenceOperationVocabulary(),
                [OperationSubjectRole],
                [OperationResultGrain],
                [MethodDefinitionsRowSet],
                [],
                [],
                [
                    new(
                        OperationProfileIdentity,
                        [],
                        []),
                ]);

    private static readonly QueryOperationRoute<
        UnsafeEvidencePresenceOperationPredicate,
        UnsafeEvidencePresenceQueryPlan> Route =
        QueryOperationRoute<
            UnsafeEvidencePresenceOperationPredicate,
            UnsafeEvidencePresenceQueryPlan>.Create(
                OperationRouteIdentity,
                OperationDefinition,
                OperationSubjectRole,
                OperationResultGrain,
                [MethodDefinitionsRowSet],
                OperationProfileIdentity,
                [],
                []);

    private static readonly QuerySpaceRowScopeBinding<
        UnsafeEvidenceMethodDefinitionRow> MethodDefinitionsRowScope =
            new(
                new QuerySpaceRowScopeDescriptor(
                    MethodDefinitionsRowScopeIdentity,
                    MethodDefinitionsRowVocabularyIdentity,
                    [MethodDefinitionsRowSet],
                    [],
                    [],
                    []),
                RowQueryVocabulary<
                    UnsafeEvidenceMethodDefinitionRow>.Create(
                        RowQueryVocabularyIdentity.Create(),
                        [],
                        []));

    public static QuerySpaceBinding QuerySpace { get; } =
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            Route,
            [MethodDefinitionsRowScope],
            [QuerySpaceTerminalRequirement.Exists],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Exists,
                    ResultContract),
            ]);

    private static readonly QuerySpaceRequest OwnerRequest =
        CreateOwnerRequest();

    private static readonly UnsafeEvidencePresenceRequestResolution.Accepted
        OwnerPlan =
            ResolveRequest(OwnerRequest)
                as UnsafeEvidencePresenceRequestResolution.Accepted
            ?? throw new InvalidOperationException(
                "The owner-issued unsafe-evidence QuerySpace request "
                + "must resolve.");

    private static readonly MethodDefinitionSourceRequest<int>
        OwnerMethodSource =
            CreateMethodSource(
                MethodDefinitionSourceBreadth.AllDefinitions);

    public static InspectionQuery<UnsafeEvidencePresenceResult> Definition { get; } =
        new("Unsafe evidence presence", InspectionCost.NetworkFree);

    public static QuerySpaceRequest CreateRequest() => OwnerRequest;

    private static QuerySpaceRequest CreateOwnerRequest() =>
        QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            PortableQueryIntent.Create(
                [],
                [],
                [],
                []),
            [MethodDefinitionsRowSet],
            [],
            QuerySpaceTerminalRequirement.Exists);

    public static UnsafeEvidencePresenceRequestResolution ResolveRequest(
        QuerySpaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(
                request.QuerySpace,
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return new UnsafeEvidencePresenceRequestResolution.Rejected(
                UnsafeEvidencePresenceRequestRejectionKind
                    .QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets.Count != 1
            || !string.Equals(
                request.ParticipatingRowSets[0],
                MethodDefinitionsRowSet,
                StringComparison.Ordinal))
        {
            return new UnsafeEvidencePresenceRequestResolution.Rejected(
                UnsafeEvidencePresenceRequestRejectionKind
                    .ParticipatingRowSetsMismatch);
        }
        if (request.RowIntents.Count != 0)
        {
            return new UnsafeEvidencePresenceRequestResolution.Rejected(
                UnsafeEvidencePresenceRequestRejectionKind
                    .RowIntentNotSupported);
        }
        if (request.Terminal != QuerySpaceTerminalRequirement.Exists)
        {
            return new UnsafeEvidencePresenceRequestResolution.Rejected(
                UnsafeEvidencePresenceRequestRejectionKind
                    .TerminalMismatch);
        }
        if (!string.Equals(
                request.ResultContract,
                ResultContract,
                StringComparison.Ordinal))
        {
            return new UnsafeEvidencePresenceRequestResolution.Rejected(
                UnsafeEvidencePresenceRequestRejectionKind
                    .ResultContractMismatch);
        }

        PortableQueryResolution<UnsafeEvidencePresenceQueryPlan> resolution =
            Route.Resolve(request.Operation);
        if (!resolution.IsResolved)
        {
            return new UnsafeEvidencePresenceRequestResolution.IntentRejected(
                resolution.Failure);
        }

        return new UnsafeEvidencePresenceRequestResolution.Accepted(
            resolution.Plan,
            UnsafeEvidencePresence.Description);
    }

    public static UnsafeEvidencePresenceResult Execute(
        string path,
        PdbContext context)
        => Execute(path, context, OwnerMethodSource);

    public static UnsafeEvidencePresenceResult ExecuteExactTypes(
        string path,
        PdbContext context,
        params TypeDefinitionHandle[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return Execute(
            path,
            context,
            CreateMethodSource(
                MethodDefinitionSourceBreadth.ExactTypes(types)));
    }

    static MethodDefinitionSourceRequest<int> CreateMethodSource(
        MethodDefinitionSourceBreadth breadth) =>
        MethodDefinitionSourceRequest<int>.Create(
            OwnerPlan.Work,
            UnsafeEvidencePresenceProducer.Instance,
            breadth);

    static UnsafeEvidencePresenceResult Execute(
        string path,
        PdbContext context,
        MethodDefinitionSourceRequest<int> methodSource)
    {
        try
        {
            var operation = AssemblyAnalysisOperation<int>.Create(
                path,
                methodSource);
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Borrow(context);
            return session.SnapshotOperation<
                AssemblyAnalysisOperation<int>,
                UnsafeEvidencePresenceResult>(
                operation,
                access =>
                {
                    AssemblyAnalysisServiceResult<int> serviceResult =
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access);
                    if (serviceResult
                        is not AssemblyAnalysisServiceResult<int>.Completed
                            completed)
                    {
                        var rejected =
                            (AssemblyAnalysisServiceResult<int>.Rejected)
                                serviceResult;
                        return new UnsafeEvidencePresenceResult.Failed(
                            new InvalidOperationException(
                                "Unsafe evidence presence could not bind its "
                                + $"assembly analysis operation: {rejected.Kind}."));
                    }

                    UnsafeEvidencePresenceInspection inspection =
                        access.InspectImage(
                            peReader =>
                                UnsafeEvidencePresence.Project(
                                    completed.Execution.ResultOf(
                                        UnsafeEvidencePresenceProducer.Instance),
                                    completed.Execution.WorkReceipt,
                                    peReader));
                    return inspection switch
                    {
                        UnsafeEvidencePresenceInspection.Available available =>
                            new UnsafeEvidencePresenceResult.Available(
                                available.HasEvidence,
                                completed.Execution.SourceReceipt,
                                available.Receipt),
                        UnsafeEvidencePresenceInspection.Incomplete incomplete =>
                            new UnsafeEvidencePresenceResult.ExecutionIncomplete(
                                incomplete.Error,
                                incomplete.Outcome,
                                completed.Execution.SourceReceipt,
                                incomplete.Receipt),
                        _ => throw new InvalidOperationException(
                            "Unknown unsafe-evidence inspection outcome."),
                    };
                });
        }
        catch (Exception ex)
        {
            return new UnsafeEvidencePresenceResult.Failed(ex);
        }
    }
}
