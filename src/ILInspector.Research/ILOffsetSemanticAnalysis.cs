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

public sealed record ILOffsetSemanticAnalysisInput(
    ILOffsetAllocationAnalysisInput? Allocation,
    ILOffsetSafetyAnalysisInput? Safety,
    ILOffsetCostAnalysisInput? Cost);

/// <summary>
/// Composes focused coordinate evidence over one exact MethodDef population.
/// </summary>
public static class ILOffsetSemanticAnalysis
{
    const string AllocationMethods = "allocation-methods";
    const string SafetyMethods = "safety-methods";
    const string CostMethods = "cost-methods";

    static readonly QuerySpaceDescriptor Descriptor = CreateDescriptor();
    static readonly WorkDescription AllocationWork =
        Plan(ExactMethodAllocationProducer.Instance);
    static readonly WorkDescription SafetyWork =
        Plan(ExactMethodAnalysisProducer.Instance);
    static readonly WorkDescription CostWork =
        Plan(FocusedMethodCallProducer.Instance);

    public static ILOffsetSemanticAnalysisInput Execute(
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

        ImmutableArray<MethodDefinitionHandle> methods =
        [
            .. methodTokens.Distinct().Select(MethodHandle),
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
        var associations = new List<MethodDefinitionSourceAssociation>();
        Binding<ExactMethodAllocationProducerResult>? allocation =
            capabilities.HasFlag(
                ILOffsetProjectionCapabilities.AllocationContext)
                ? Bind(
                    AllocationMethods,
                    AllocationWork,
                    ExactMethodAllocationProducer.Instance)
                : null;
        Binding<ExactMethodAnalysisProducerResult>? safety =
            capabilities.HasFlag(
                ILOffsetProjectionCapabilities.SafetyContext)
                ? Bind(
                    SafetyMethods,
                    SafetyWork,
                    ExactMethodAnalysisProducer.Instance)
                : null;
        Binding<FocusedMethodCallProducerResult>? cost =
            capabilities.HasFlag(
                ILOffsetProjectionCapabilities.CostContext)
                ? Bind(
                    CostMethods,
                    CostWork,
                    FocusedMethodCallProducer.Instance)
                : null;

        MethodDefinitionSourceRequestSetPlan plan =
            MethodDefinitionSourceRequestSet.Plan(
                MethodDefinitionSourceResourceIdentity.Create(),
                associations)
            is MethodDefinitionSourceRequestSetPlanResult.Accepted accepted
                ? accepted.Plan
                : throw new ProducerContractException(
                    "The IL-offset semantic request set must plan.");
        AssemblyAnalysisRequestSetOperation operation =
            AssemblyAnalysisRequestSetOperation.Create(subjectName, plan);
        AssemblyAnalysisRequestSetServiceResult serviceResult =
            session.SnapshotOperation(
                operation,
                access => AssemblyAnalysisService.Instance.Execute(
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
                .Execution.MethodDefinitionExecution();
        return new(
            allocation?.CreateInput(
                execution,
                moduleVersionId,
                ILOffsetAllocationAnalysis.CreateInput),
            safety?.CreateInput(
                execution,
                moduleVersionId,
                ILOffsetSafetyAnalysis.CreateInput),
            cost?.CreateInput(
                execution,
                moduleVersionId,
                ILOffsetCostAnalysis.CreateInput));

        Binding<TResult> Bind<TResult>(
            string rowSet,
            WorkDescription work,
            ProducerDeclaration<TResult> producer)
        {
            MethodDefinitionSourceRequest<TResult> request =
                MethodDefinitionSourceRequest<TResult>.Create(
                    QuerySpaceRequest.Create(
                        Descriptor,
                        PortableQueryIntent.Empty,
                        [rowSet],
                        [],
                        QuerySpaceTerminalRequirement.Rows),
                    work,
                    producer,
                    breadth,
                    limits);
            MethodDefinitionSourceAssociation association =
                MethodDefinitionSourceAssociation.Create(request);
            associations.Add(association);
            return new(association, request);
        }
    }

    sealed record Binding<TResult>(
        MethodDefinitionSourceAssociation Association,
        MethodDefinitionSourceRequest<TResult> Request)
    {
        internal TInput CreateInput<TInput>(
            MethodDefinitionSourceRequestSetExecution execution,
            Guid moduleVersionId,
            Func<
                Guid,
                TResult,
                MethodDefinitionSourceReceipt,
                WorkReceipt,
                TInput> create)
        {
            ProducerResult<TResult> result =
                execution.ResultOf(Association, Request);
            if (!result.HasValue)
            {
                throw new InvalidOperationException(
                    result.Failure?.Message
                        ?? result.Critical?.Message
                        ?? "The IL-offset semantic Analysis producer failed.");
            }

            MethodDefinitionSourceRequestResult requestResult =
                execution.ResultOf(Association);
            return create(
                moduleVersionId,
                result.Value!,
                requestResult.SourceReceipt,
                requestResult.WorkReceipt);
        }
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

    static WorkDescription Plan<TResult>(
        ProducerDeclaration<TResult> producer) =>
        ProducerPlanner.Plan(
            [new ProducerRequest(producer, ProducerTerminal.Rows)])
        is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new ProducerContractException(
                "The IL-offset semantic Analysis request must plan.");

    static QuerySpaceDescriptor CreateDescriptor()
    {
        string[] rowSets = [AllocationMethods, SafetyMethods, CostMethods];
        var operation = QueryOperationDefinition<Predicate, OperationPlan>.Create(
            "il-coordinate-analysis",
            new Vocabulary(),
            ["managed-assembly"],
            ["method"],
            rowSets,
            [],
            [],
            [new("default", [], [])]);
        var route = QueryOperationRoute<Predicate, OperationPlan>.Create(
            "il-coordinate-analysis/default",
            operation,
            "managed-assembly",
            "method",
            rowSets,
            "default",
            [],
            []);
        return QuerySpaceDescriptor.Create(
            "il-coordinate-analysis/query-space/v1",
            route,
            [
                new QuerySpaceRowScopeDescriptor(
                    "il-coordinate-analysis/methods/v1",
                    "il-coordinate-analysis/method-rows/v1",
                    rowSets,
                    [],
                    [],
                    []),
            ],
            [QuerySpaceTerminalRequirement.Rows],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    "il-coordinate-analysis/rows/v1"),
            ]);
    }

    readonly record struct Predicate;
    sealed record OperationPlan;

    sealed class Vocabulary : PortableQueryVocabulary<Predicate, OperationPlan>
    {
        public override string Identity =>
            "il-coordinate-analysis/operation/v1";
        public override IReadOnlyList<string> RequiredDimensions => [];
        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<Predicate>? declaration)
        {
            declaration = null;
            return false;
        }
        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<Predicate>? declaration)
        {
            declaration = null;
            return false;
        }
        public override bool AdmitsStageKind(RowSelectionStageKind kind) =>
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
            PortableQueryResolvedTerm<Predicate> first,
            PortableQueryResolvedTerm<Predicate> second) =>
            true;
        public override OperationPlan CreatePlan(
            PortableQueryResolvedIntent<Predicate> resolved) =>
            new();
    }
}
