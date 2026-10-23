using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Exact Analysis and metadata evidence for one physical MethodDef body in an
/// assembly-context participant.
/// </summary>
public sealed record AssemblyMethodAnalysis(
    int RequestedMethodToken,
    MethodIdentity Method,
    MethodSignals Signals,
    ImmutableArray<AllocationOccurrence> Allocations,
    ImmutableArray<DirectCall> DirectCalls,
    ImmutableArray<UnsafetyOccurrence> UnsafetyOccurrences,
    ImmutableArray<UnsafeEvidence> UnsafeEvidence,
    ImmutableArray<MethodExceptionRegionInfo> ExceptionRegions,
    ImmutableArray<OptimizationOpportunity> OptimizationOpportunities,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

/// <summary>
/// Reads exact method-body evidence while the query layer owns the retained
/// workspace snapshot and Analysis execution.
/// </summary>
public static class AssemblyContextMethodAnalysisQuery
{
    static readonly ExactMethodAnalysisBundleProducer FocusedProducer =
        ExactMethodAnalysisBundleProducer.Instance;
    static readonly WorkDescription FocusedWork =
        CreateFocusedWork(FocusedProducer);

    public static InspectionQuery<
        AssemblyContextEntry<AssemblyMethodAnalysis>> Definition
    { get; } =
        new(
            "Assembly context method analysis",
            InspectionCost.Unbounded);

    public static AssemblyContextEntry<AssemblyMethodAnalysis> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        int methodToken) =>
        ExecuteParticipant(
            group,
            participant,
            methodToken,
            CancellationToken.None);

    public static AssemblyContextEntry<AssemblyMethodAnalysis> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        int methodToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        cancellationToken.ThrowIfCancellationRequested();

        return AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            cancellationToken,
            (subject, snapshot) => Analyze(
                subject,
                snapshot,
                methodToken,
                cancellationToken));
    }

    static AssemblyMethodAnalysis Analyze(
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        int methodToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExactMethodAnalysisBundleBody focused =
            ExecuteFocused(
                snapshot,
                subject,
                methodToken);
        if (focused.Method.Method is not { } method)
        {
            throw new InvalidOperationException(
                focused.Method.Diagnostics.FirstOrDefault()?.Message
                    ?? $"Method 0x{methodToken:X8} in "
                    + $"'{subject.Identity.Name}' could not be decoded.");
        }
        if (!focused.Method.HasManagedBody)
        {
            throw new InvalidOperationException(
                $"Method '{method.Name}' (0x{methodToken:X8}) in "
                    + $"'{subject.Identity.Name}' does not have an IL body.");
        }
        if (focused.Method.ExceptionRegionError is not null)
        {
            throw new InvalidOperationException(
                focused.Method.ExceptionRegionError);
        }

        ImmutableArray<AllocationOccurrence> allocations =
            focused.Optimization.Allocation.Allocations;

        var result = new AssemblyMethodAnalysis(
            methodToken,
            method,
            focused.Signals,
            EmptyIfDefault(allocations),
            focused.DirectCalls,
            focused.Method.UnsafeOccurrences,
            focused.Method.SafetyEvidence,
            focused.Method.ExceptionRegions,
            LibraryOptimizationAnalysisResult.ProjectExactMethod(
                method,
                allocations,
                focused.DirectCalls,
                focused.Optimization.Opportunities,
                focused.Optimization
                    .SuppressesDerivedOpportunities,
                focused.Optimization
                    .RequiresDeclaredOwner,
                focused.DeclaredOwner,
                focused
                    .DeclaredOwnerSuppressesOpportunities,
                focused.Optimization.Allocation
                    .InAssemblyTypeIsException,
                focused.StringReceiverSources),
            [
                .. focused.Method.Diagnostics
                    .Concat(
                        focused.Optimization.Allocation.Diagnostics)
                    .Distinct(),
            ]);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    static ExactMethodAnalysisBundleBody ExecuteFocused(
        AssemblyImageSnapshot snapshot,
        AssemblyContextSubject subject,
        int methodToken)
    {
        EntityHandle entity = MetadataTokens.EntityHandle(methodToken);
        if (entity.Kind != HandleKind.MethodDefinition)
        {
            throw new ArgumentException(
                $"Token 0x{methodToken:X8} is not a MethodDef token.",
                nameof(methodToken));
        }

        MethodDefinitionSourceBreadth breadth =
            MethodDefinitionSourceBreadth.ExactMethods(
                (MethodDefinitionHandle)entity);
        var limits = new MethodDefinitionTerminalWorkLimits(
            maximumBodies: 1,
            maximumEncodedIlBytes: int.MaxValue);
        MethodDefinitionSourceRequest<
            ExactMethodAnalysisBundleProducerResult> request =
                MethodDefinitionSourceRequest<
                    ExactMethodAnalysisBundleProducerResult>.Create(
                        FocusedWork,
                        FocusedProducer,
                        breadth,
                        limits);
        AssemblyAnalysisOperation<
            ExactMethodAnalysisBundleProducerResult> operation =
                AssemblyAnalysisOperation<
                    ExactMethodAnalysisBundleProducerResult>.Create(
                        AssemblyContextAnalysisSource.Name(subject),
                        request.AsAssemblyAnalysisSource());

        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(snapshot);
        AssemblyAnalysisServiceResult<
            ExactMethodAnalysisBundleProducerResult> serviceResult =
                session.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));
        if (serviceResult
            is AssemblyAnalysisServiceResult<
                ExactMethodAnalysisBundleProducerResult>.Rejected rejected)
        {
            throw new InvalidOperationException(
                "The exact-method Analysis operation was rejected: "
                    + $"{rejected.Kind}.");
        }

        AssemblyAnalysisExecution<
            ExactMethodAnalysisBundleProducerResult> execution =
                ((AssemblyAnalysisServiceResult<
                    ExactMethodAnalysisBundleProducerResult>.Completed)
                        serviceResult)
                .Execution;
        ProducerResult<ExactMethodAnalysisBundleProducerResult>
            producerResult =
                execution.ResultOf(FocusedProducer);
        if (!producerResult.HasValue)
        {
            throw new InvalidOperationException(
                producerResult.Failure?.Message
                    ?? producerResult.Critical?.Message
                    ?? "The exact-method Analysis producer failed.");
        }

        ExactMethodAnalysisBundleBody? body =
            producerResult.Value!.Bodies.FirstOrDefault(
                candidate =>
                    candidate.Method.MethodToken == methodToken);
        return body
            ?? throw new ArgumentException(
                $"Token 0x{methodToken:X8} is not a MethodDef in "
                    + $"'{subject.Identity.Name}'.",
                nameof(methodToken));
    }

    static WorkDescription CreateFocusedWork<TResult>(
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
                "The exact-method Analysis request must plan.");

    static ImmutableArray<T> EmptyIfDefault<T>(
        ImmutableArray<T> values)
        => values.IsDefault
            ? []
            : values;
}
