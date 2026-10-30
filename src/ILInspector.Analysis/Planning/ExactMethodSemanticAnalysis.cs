using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace ILInspector.Analysis.Planning;

/// <summary>Semantic products available over one exact MethodDef population.</summary>
[Flags]
public enum ExactMethodSemanticProducts
{
    None = 0,
    Allocation = 1 << 0,
    Safety = 1 << 1,
    Calls = 1 << 2,
}

/// <summary>One independently receipted exact Method semantic result.</summary>
public sealed record ExactMethodSemanticLane<TResult>(
    TResult Result,
    MethodDefinitionSourceReceipt SourceReceipt,
    WorkReceipt WorkReceipt)
    where TResult : class;

/// <summary>
/// Independent exact Method semantic results served by one physical source
/// group.
/// </summary>
public sealed record ExactMethodSemanticAnalysisResult(
    ExactMethodSemanticLane<
        ExactMethodAllocationProducerResult>? Allocation,
    ExactMethodSemanticLane<
        ExactMethodAnalysisProducerResult>? Safety,
    ExactMethodSemanticLane<
        FocusedMethodCallProducerResult>? Calls,
    MethodDefinitionSourceGroupReceipt SourceGroupReceipt);

/// <summary>
/// Plans selected semantic products as one QuerySpace request set over an
/// already-open assembly image.
/// </summary>
public static class ExactMethodSemanticAnalysis
{
    static readonly ExactMethodAllocationProducer AllocationProducer =
        ExactMethodAllocationProducer.Instance;
    static readonly ExactMethodAnalysisProducer SafetyProducer =
        ExactMethodAnalysisProducer.Instance;
    static readonly FocusedMethodCallProducer CallsProducer =
        FocusedMethodCallProducer.Instance;

    static readonly WorkDescription AllocationWork =
        CreateWork(AllocationProducer);
    static readonly WorkDescription SafetyWork =
        CreateWork(SafetyProducer);
    static readonly WorkDescription CallsWork =
        CreateWork(CallsProducer);

    public static ExactMethodSemanticAnalysisResult Execute(
        AssemblyInspectionSession session,
        string subjectName,
        IEnumerable<int> methodTokens,
        ExactMethodSemanticProducts products,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);
        ArgumentNullException.ThrowIfNull(methodTokens);
        cancellationToken.ThrowIfCancellationRequested();

        bool wantsAllocation = Includes(
            products,
            ExactMethodSemanticProducts.Allocation);
        bool wantsSafety = Includes(
            products,
            ExactMethodSemanticProducts.Safety);
        bool wantsCalls = Includes(
            products,
            ExactMethodSemanticProducts.Calls);
        if (!wantsAllocation && !wantsSafety && !wantsCalls)
        {
            throw new ArgumentException(
                "Exact Method semantic analysis requires at least one "
                    + "product.",
                nameof(products));
        }

        ImmutableArray<MethodDefinitionHandle> methods =
        [
            .. methodTokens
                .Distinct()
                .Select(MethodHandle),
        ];
        if (methods.IsEmpty)
        {
            throw new ArgumentException(
                "Exact Method semantic analysis requires at least one "
                    + "MethodDef.",
                nameof(methodTokens));
        }

        MethodDefinitionSourceBreadth breadth =
            MethodDefinitionSourceBreadth.ExactMethods(methods);
        var limits = new MethodDefinitionTerminalWorkLimits(
            maximumBodies: methods.Length,
            maximumEncodedIlBytes: session.ImageLength);
        var associations =
            ImmutableArray.CreateBuilder<
                MethodDefinitionSourceAssociation>(3);

        MethodDefinitionSourceRequest<
            ExactMethodAllocationProducerResult>? allocationRequest = null;
        MethodDefinitionSourceAssociation? allocationAssociation = null;
        if (wantsAllocation)
        {
            allocationRequest =
                MethodDefinitionSourceRequest<
                    ExactMethodAllocationProducerResult>.Create(
                        ExactMethodSemanticQuerySpace.CreateRequest(
                            ExactMethodSemanticProduct.Allocation),
                        AllocationWork,
                        AllocationProducer,
                        breadth,
                        limits);
            allocationAssociation =
                MethodDefinitionSourceAssociation.Create(
                    allocationRequest);
            associations.Add(allocationAssociation);
        }

        MethodDefinitionSourceRequest<
            ExactMethodAnalysisProducerResult>? safetyRequest = null;
        MethodDefinitionSourceAssociation? safetyAssociation = null;
        if (wantsSafety)
        {
            safetyRequest =
                MethodDefinitionSourceRequest<
                    ExactMethodAnalysisProducerResult>.Create(
                        ExactMethodSemanticQuerySpace.CreateRequest(
                            ExactMethodSemanticProduct.Safety),
                        SafetyWork,
                        SafetyProducer,
                        breadth,
                        limits);
            safetyAssociation =
                MethodDefinitionSourceAssociation.Create(
                    safetyRequest);
            associations.Add(safetyAssociation);
        }

        MethodDefinitionSourceRequest<
            FocusedMethodCallProducerResult>? callsRequest = null;
        MethodDefinitionSourceAssociation? callsAssociation = null;
        if (wantsCalls)
        {
            callsRequest =
                MethodDefinitionSourceRequest<
                    FocusedMethodCallProducerResult>.Create(
                        ExactMethodSemanticQuerySpace.CreateRequest(
                            ExactMethodSemanticProduct.Calls),
                        CallsWork,
                        CallsProducer,
                        breadth,
                        limits);
            callsAssociation =
                MethodDefinitionSourceAssociation.Create(
                    callsRequest);
            associations.Add(callsAssociation);
        }

        MethodDefinitionSourceRequestSetPlan plan =
            MethodDefinitionSourceRequestSet.Plan(
                MethodDefinitionSourceResourceIdentity.Create(),
                associations)
            is MethodDefinitionSourceRequestSetPlanResult.Accepted accepted
                ? accepted.Plan
                : throw new ProducerContractException(
                    "The exact Method semantic QuerySpace request set must "
                        + "plan.");
        AssemblyAnalysisRequestSetOperation operation =
            AssemblyAnalysisRequestSetOperation.Create(
                subjectName,
                plan);
        AssemblyAnalysisRequestSetServiceResult serviceResult =
            session.SnapshotOperation(
                operation,
                access =>
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access));
        cancellationToken.ThrowIfCancellationRequested();
        if (serviceResult
            is AssemblyAnalysisRequestSetServiceResult.Rejected rejected)
        {
            throw new InvalidOperationException(
                "The exact Method semantic Analysis operation was rejected: "
                    + $"{rejected.Kind}.");
        }

        MethodDefinitionSourceRequestSetExecution execution =
            ((AssemblyAnalysisRequestSetServiceResult.Completed)serviceResult)
                .Execution
                .MethodDefinitionExecution();
        MethodDefinitionSourceGroupReceipt sourceGroupReceipt =
            execution.GroupReceipts.Length == 1
                ? execution.GroupReceipts[0]
                : throw new ProducerContractException(
                    "The selected exact Method semantic products must share "
                        + "one source execution group.");

        return new(
            wantsAllocation
                ? Lane(
                    execution,
                    allocationAssociation!,
                    allocationRequest!,
                    "allocation")
                : null,
            wantsSafety
                ? Lane(
                    execution,
                    safetyAssociation!,
                    safetyRequest!,
                    "safety")
                : null,
            wantsCalls
                ? Lane(
                    execution,
                    callsAssociation!,
                    callsRequest!,
                    "calls")
                : null,
            sourceGroupReceipt);
    }

    static ExactMethodSemanticLane<TResult> Lane<TResult>(
        MethodDefinitionSourceRequestSetExecution execution,
        MethodDefinitionSourceAssociation association,
        MethodDefinitionSourceRequest<TResult> request,
        string product)
        where TResult : class
    {
        TResult result =
            RequireResult(
                execution.ResultOf(association, request),
                product);
        MethodDefinitionSourceRequestResult requestResult =
            execution.ResultOf(association);
        return new(
            result,
            requestResult.SourceReceipt,
            requestResult.WorkReceipt);
    }

    static TResult RequireResult<TResult>(
        ProducerResult<TResult> producerResult,
        string product)
        where TResult : class
    {
        if (producerResult.HasValue)
            return producerResult.Value!;

        throw new InvalidOperationException(
            producerResult.Failure?.Message
                ?? producerResult.Critical?.Message
                ?? $"The exact Method {product} Analysis producer failed.");
    }

    static MethodDefinitionHandle MethodHandle(int token)
    {
        EntityHandle entity = MetadataTokens.EntityHandle(token);
        if (entity.Kind != HandleKind.MethodDefinition)
        {
            throw new ArgumentException(
                $"Token 0x{token:X8} is not a MethodDef token.",
                nameof(token));
        }

        return (MethodDefinitionHandle)entity;
    }

    static WorkDescription CreateWork<TResult>(
        ProducerDeclaration<TResult> producer) =>
        ProducerPlanner.Plan(
            [
                new ProducerRequest(
                    producer,
                    ProducerTerminal.Rows),
            ])
        is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new ProducerContractException(
                "The exact Method semantic Analysis request must plan.");

    static bool Includes(
        ExactMethodSemanticProducts products,
        ExactMethodSemanticProducts product) =>
        (products & product) != 0;
}

internal enum ExactMethodSemanticProduct
{
    Allocation,
    Safety,
    Calls,
}

internal readonly record struct ExactMethodSemanticOperationPredicate;

internal readonly record struct ExactMethodSemanticEvidenceRow;

internal sealed record ExactMethodSemanticOperationPlan;

internal sealed class ExactMethodSemanticOperationVocabulary
    : PortableQueryVocabulary<
        ExactMethodSemanticOperationPredicate,
        ExactMethodSemanticOperationPlan>
{
    public override string Identity =>
        "exact-method-semantic/operation/v1";

    public override IReadOnlyList<string> RequiredDimensions => [];

    public override bool TryGetKey(
        string key,
        [NotNullWhen(true)]
        out PortableQueryKeyDeclaration<
            ExactMethodSemanticOperationPredicate>? declaration)
    {
        declaration = null;
        return false;
    }

    public override bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)]
        out PortableQueryDimensionDeclaration<
            ExactMethodSemanticOperationPredicate>? declaration)
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

    public override bool CollapsesDuplicateBindings => true;

    public override bool AreTermsCompatible(
        PortableQueryResolvedTerm<
            ExactMethodSemanticOperationPredicate> first,
        PortableQueryResolvedTerm<
            ExactMethodSemanticOperationPredicate> second) =>
        true;

    public override ExactMethodSemanticOperationPlan CreatePlan(
        PortableQueryResolvedIntent<
            ExactMethodSemanticOperationPredicate> resolved) =>
        new();
}

internal static class ExactMethodSemanticQuerySpace
{
    static readonly ExactMethodSemanticOperationVocabulary Vocabulary = new();

    static readonly QuerySpaceBinding Allocation =
        Create(
            "allocation",
            "exact-method-allocation/rows/v1");

    static readonly QuerySpaceBinding Safety =
        Create(
            "safety",
            "exact-method-safety/rows/v1");

    static readonly QuerySpaceBinding Calls =
        Create(
            "calls",
            "exact-method-calls/rows/v1");

    internal static QuerySpaceRequest CreateRequest(
        ExactMethodSemanticProduct product)
    {
        QuerySpaceBinding querySpace = product switch
        {
            ExactMethodSemanticProduct.Allocation => Allocation,
            ExactMethodSemanticProduct.Safety => Safety,
            ExactMethodSemanticProduct.Calls => Calls,
            _ => throw new ArgumentOutOfRangeException(nameof(product)),
        };
        return QuerySpaceRequest.Create(
            querySpace.Descriptor,
            PortableQueryIntent.Empty,
            [RowSet(product)],
            [],
            QuerySpaceTerminalRequirement.Rows);
    }

    static QuerySpaceBinding Create(
        string product,
        string resultContract)
    {
        string rowSet = RowSet(product);
        var definition =
            QueryOperationDefinition<
                ExactMethodSemanticOperationPredicate,
                ExactMethodSemanticOperationPlan>.Create(
                    $"exact-method-{product}",
                    Vocabulary,
                    ["managed-assembly"],
                    ["method"],
                    [rowSet],
                    [],
                    [],
                    [
                        new(
                            "default",
                            [],
                            []),
                    ]);
        QueryOperationRoute<
            ExactMethodSemanticOperationPredicate,
            ExactMethodSemanticOperationPlan> route =
                QueryOperationRoute<
                    ExactMethodSemanticOperationPredicate,
                    ExactMethodSemanticOperationPlan>.Create(
                        $"exact-method-{product}/default",
                        definition,
                        "managed-assembly",
                        "method",
                        [rowSet],
                        "default",
                        [],
                        []);
        var rowScope =
            new QuerySpaceRowScopeBinding<
                ExactMethodSemanticEvidenceRow>(
                    new QuerySpaceRowScopeDescriptor(
                        $"exact-method-{product}/evidence/v1",
                        $"exact-method-{product}/evidence-rows/v1",
                        [rowSet],
                        [],
                        [],
                        []),
                    RowQueryVocabulary<
                        ExactMethodSemanticEvidenceRow>.Create(
                            RowQueryVocabularyIdentity.Create(),
                            [],
                            []));
        return QuerySpaceBinding.Create(
            $"exact-method-{product}/query-space/v1",
            route,
            [rowScope],
            [QuerySpaceTerminalRequirement.Rows],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    resultContract),
            ]);
    }

    static string RowSet(
        ExactMethodSemanticProduct product) =>
        RowSet(product switch
        {
            ExactMethodSemanticProduct.Allocation => "allocation",
            ExactMethodSemanticProduct.Safety => "safety",
            ExactMethodSemanticProduct.Calls => "calls",
            _ => throw new ArgumentOutOfRangeException(nameof(product)),
        });

    static string RowSet(string product) =>
        $"exact-method-{product}-evidence";
}
