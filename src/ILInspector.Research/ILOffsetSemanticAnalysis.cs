using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace ILInspector.Research;

/// <summary>
/// Exact Method-source semantic evidence selected for IL-coordinate projection.
/// </summary>
public sealed record ILOffsetSemanticAnalysisResult(
    ILOffsetAllocationAnalysisInput? Allocation,
    ILOffsetSafetyAnalysisInput? Safety,
    ILOffsetCostAnalysisInput? Cost,
    MethodDefinitionSourceGroupReceipt SourceGroupReceipt);

/// <summary>
/// Plans selected IL-coordinate semantic products as one QuerySpace request
/// set over an already-open assembly image.
/// </summary>
public static class ILOffsetSemanticAnalysis
{
    static readonly ExactMethodAllocationProducer AllocationProducer =
        ExactMethodAllocationProducer.Instance;
    static readonly ExactMethodAnalysisProducer SafetyProducer =
        ExactMethodAnalysisProducer.Instance;
    static readonly FocusedMethodCallProducer CostProducer =
        FocusedMethodCallProducer.Instance;

    static readonly WorkDescription AllocationWork =
        CreateWork(AllocationProducer);
    static readonly WorkDescription SafetyWork =
        CreateWork(SafetyProducer);
    static readonly WorkDescription CostWork =
        CreateWork(CostProducer);

    public static ILOffsetSemanticAnalysisResult Execute(
        AssemblyInspectionSession session,
        string subjectName,
        Guid moduleVersionId,
        IEnumerable<int> methodTokens,
        ILOffsetProjectionCapabilities capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);
        ArgumentNullException.ThrowIfNull(methodTokens);
        cancellationToken.ThrowIfCancellationRequested();

        bool wantsAllocation = Includes(
            capabilities,
            ILOffsetProjectionCapabilities.AllocationContext);
        bool wantsSafety = Includes(
            capabilities,
            ILOffsetProjectionCapabilities.SafetyContext);
        bool wantsCost = Includes(
            capabilities,
            ILOffsetProjectionCapabilities.CostContext);
        if (!wantsAllocation && !wantsSafety && !wantsCost)
        {
            throw new ArgumentException(
                "IL-offset semantic analysis requires at least one semantic "
                    + "projection capability.",
                nameof(capabilities));
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
                "IL-offset semantic analysis requires at least one MethodDef.",
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
                        ILOffsetSemanticQuerySpace.CreateRequest(
                            ILOffsetSemanticProduct.Allocation),
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
                        ILOffsetSemanticQuerySpace.CreateRequest(
                            ILOffsetSemanticProduct.Safety),
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
            FocusedMethodCallProducerResult>? costRequest = null;
        MethodDefinitionSourceAssociation? costAssociation = null;
        if (wantsCost)
        {
            costRequest =
                MethodDefinitionSourceRequest<
                    FocusedMethodCallProducerResult>.Create(
                        ILOffsetSemanticQuerySpace.CreateRequest(
                            ILOffsetSemanticProduct.Cost),
                        CostWork,
                        CostProducer,
                        breadth,
                        limits);
            costAssociation =
                MethodDefinitionSourceAssociation.Create(
                    costRequest);
            associations.Add(costAssociation);
        }

        MethodDefinitionSourceRequestSetPlan plan =
            MethodDefinitionSourceRequestSet.Plan(
                MethodDefinitionSourceResourceIdentity.Create(),
                associations)
            is MethodDefinitionSourceRequestSetPlanResult.Accepted accepted
                ? accepted.Plan
                : throw new ProducerContractException(
                    "The IL-offset semantic QuerySpace request set must plan.");
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
                "The IL-offset semantic Analysis operation was rejected: "
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
                    "The selected IL-offset semantic products must share one "
                        + "exact Method-source execution group.");

        return new(
            wantsAllocation
                ? AllocationInput(
                    execution,
                    allocationAssociation!,
                    allocationRequest!,
                    moduleVersionId)
                : null,
            wantsSafety
                ? SafetyInput(
                    execution,
                    safetyAssociation!,
                    safetyRequest!,
                    moduleVersionId)
                : null,
            wantsCost
                ? CostInput(
                    execution,
                    costAssociation!,
                    costRequest!,
                    moduleVersionId)
                : null,
            sourceGroupReceipt);
    }

    static ILOffsetAllocationAnalysisInput AllocationInput(
        MethodDefinitionSourceRequestSetExecution execution,
        MethodDefinitionSourceAssociation association,
        MethodDefinitionSourceRequest<
            ExactMethodAllocationProducerResult> request,
        Guid moduleVersionId)
    {
        ExactMethodAllocationProducerResult result =
            RequireResult(
                execution.ResultOf(association, request),
                "allocation");
        MethodDefinitionSourceRequestResult requestResult =
            execution.ResultOf(association);
        return new(
            moduleVersionId,
            result.Bodies.ToDictionary(
                static body => body.MethodToken,
                static body => body.Allocations),
            [
                .. result.Bodies.SelectMany(
                    static body => body.Diagnostics),
            ],
            requestResult.SourceReceipt,
            requestResult.WorkReceipt);
    }

    static ILOffsetSafetyAnalysisInput SafetyInput(
        MethodDefinitionSourceRequestSetExecution execution,
        MethodDefinitionSourceAssociation association,
        MethodDefinitionSourceRequest<
            ExactMethodAnalysisProducerResult> request,
        Guid moduleVersionId)
    {
        ExactMethodAnalysisProducerResult result =
            RequireResult(
                execution.ResultOf(association, request),
                "safety");
        MethodDefinitionSourceRequestResult requestResult =
            execution.ResultOf(association);
        return new(
            moduleVersionId,
            result.Bodies.ToDictionary(
                static body => body.MethodToken,
                static body => body.UnsafeOccurrences),
            result.Bodies.ToDictionary(
                static body => body.MethodToken,
                static body => body.SafetyEvidence),
            [
                .. result.Bodies.SelectMany(
                    static body => body.Diagnostics),
            ],
            requestResult.SourceReceipt,
            requestResult.WorkReceipt);
    }

    static ILOffsetCostAnalysisInput CostInput(
        MethodDefinitionSourceRequestSetExecution execution,
        MethodDefinitionSourceAssociation association,
        MethodDefinitionSourceRequest<
            FocusedMethodCallProducerResult> request,
        Guid moduleVersionId)
    {
        FocusedMethodCallProducerResult result =
            RequireResult(
                execution.ResultOf(association, request),
                "cost");
        MethodDefinitionSourceRequestResult requestResult =
            execution.ResultOf(association);
        return new(
            moduleVersionId,
            result.Bodies
                .SelectMany(static body => body.Calls)
                .GroupBy(static site => site.Identity.MethodToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .Select(static site => site.Call)
                        .ToImmutableArray()),
            [
                .. result.Bodies.SelectMany(
                    static body => body.Diagnostics),
            ],
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
                ?? $"The IL-offset {product} Analysis producer failed.");
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
                "The IL-offset semantic Analysis request must plan.");

    static bool Includes(
        ILOffsetProjectionCapabilities capabilities,
        ILOffsetProjectionCapabilities capability) =>
        (capabilities & capability) != 0;
}

internal enum ILOffsetSemanticProduct
{
    Allocation,
    Safety,
    Cost,
}

internal readonly record struct ILOffsetSemanticOperationPredicate;

internal readonly record struct ILOffsetSemanticEvidenceRow;

internal sealed record ILOffsetSemanticOperationPlan;

internal sealed class ILOffsetSemanticOperationVocabulary
    : PortableQueryVocabulary<
        ILOffsetSemanticOperationPredicate,
        ILOffsetSemanticOperationPlan>
{
    public override string Identity =>
        "il-offset-semantic/operation/v1";

    public override IReadOnlyList<string> RequiredDimensions => [];

    public override bool TryGetKey(
        string key,
        [NotNullWhen(true)]
        out PortableQueryKeyDeclaration<
            ILOffsetSemanticOperationPredicate>? declaration)
    {
        declaration = null;
        return false;
    }

    public override bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)]
        out PortableQueryDimensionDeclaration<
            ILOffsetSemanticOperationPredicate>? declaration)
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
            ILOffsetSemanticOperationPredicate> first,
        PortableQueryResolvedTerm<
            ILOffsetSemanticOperationPredicate> second) =>
        true;

    public override ILOffsetSemanticOperationPlan CreatePlan(
        PortableQueryResolvedIntent<
            ILOffsetSemanticOperationPredicate> resolved) =>
        new();
}

internal static class ILOffsetSemanticQuerySpace
{
    static readonly ILOffsetSemanticOperationVocabulary Vocabulary = new();

    static readonly QuerySpaceBinding Allocation =
        Create(
            "allocation",
            "il-offset-allocation/rows/v1");

    static readonly QuerySpaceBinding Safety =
        Create(
            "safety",
            "il-offset-safety/rows/v1");

    static readonly QuerySpaceBinding Cost =
        Create(
            "cost",
            "il-offset-cost/rows/v1");

    internal static QuerySpaceRequest CreateRequest(
        ILOffsetSemanticProduct product)
    {
        QuerySpaceBinding querySpace = product switch
        {
            ILOffsetSemanticProduct.Allocation => Allocation,
            ILOffsetSemanticProduct.Safety => Safety,
            ILOffsetSemanticProduct.Cost => Cost,
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
                ILOffsetSemanticOperationPredicate,
                ILOffsetSemanticOperationPlan>.Create(
                    $"il-offset-{product}",
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
            ILOffsetSemanticOperationPredicate,
            ILOffsetSemanticOperationPlan> route =
                QueryOperationRoute<
                    ILOffsetSemanticOperationPredicate,
                    ILOffsetSemanticOperationPlan>.Create(
                        $"il-offset-{product}/default",
                        definition,
                        "managed-assembly",
                        "method",
                        [rowSet],
                        "default",
                        [],
                        []);
        var rowScope =
            new QuerySpaceRowScopeBinding<
                ILOffsetSemanticEvidenceRow>(
                    new QuerySpaceRowScopeDescriptor(
                        $"il-offset-{product}/evidence/v1",
                        $"il-offset-{product}/evidence-rows/v1",
                        [rowSet],
                        [],
                        [],
                        []),
                    RowQueryVocabulary<
                        ILOffsetSemanticEvidenceRow>.Create(
                            RowQueryVocabularyIdentity.Create(),
                            [],
                            []));
        return QuerySpaceBinding.Create(
            $"il-offset-{product}/query-space/v1",
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
        ILOffsetSemanticProduct product) =>
        RowSet(product switch
        {
            ILOffsetSemanticProduct.Allocation => "allocation",
            ILOffsetSemanticProduct.Safety => "safety",
            ILOffsetSemanticProduct.Cost => "cost",
            _ => throw new ArgumentOutOfRangeException(nameof(product)),
        });

    static string RowSet(string product) =>
        $"il-offset-{product}-evidence";
}
