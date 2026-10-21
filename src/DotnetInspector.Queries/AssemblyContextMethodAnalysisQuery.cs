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
    static readonly ExactMethodAnalysisProducer FocusedProducer =
        ExactMethodAnalysisProducer.Instance;
    static readonly WorkDescription FocusedWork =
        CreateFocusedWork();

    public static InspectionQuery<
        AssemblyContextEntry<AssemblyMethodAnalysis>> Definition { get; } =
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
                group,
                subject,
                snapshot,
                methodToken,
                cancellationToken));
    }

    static AssemblyMethodAnalysis Analyze(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        int methodToken,
        CancellationToken cancellationToken)
    {
        LibraryCallGraphAnalysisResult? callGraph = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExactMethodAnalysisBody focused =
                ExecuteFocused(
                    snapshot,
                    subject,
                    methodToken);
            if (focused.Method is not { } method)
            {
                throw new InvalidOperationException(
                    focused.Diagnostics.FirstOrDefault()?.Message
                        ?? $"Method 0x{methodToken:X8} in "
                        + $"'{subject.Identity.Name}' could not be decoded.");
            }
            if (!focused.HasManagedBody)
            {
                throw new InvalidOperationException(
                    $"Method '{method.Name}' (0x{methodToken:X8}) in "
                        + $"'{subject.Identity.Name}' does not have an IL body.");
            }
            if (focused.ExceptionRegionError is not null)
            {
                throw new InvalidOperationException(
                    focused.ExceptionRegionError);
            }

            var resolver = AssemblyContextAnalysisSource.Resolver(
                group,
                subject);
            cancellationToken.ThrowIfCancellationRequested();
            LibraryBodyAnalysisRequest request =
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    new HashSet<int> { methodToken });
            LibraryBodyAnalysisExecution execution =
                LibraryBodyAnalysisService.ExecuteImage(
                    AssemblyContextAnalysisSource.Name(subject),
                    snapshot.Content,
                    request,
                    resolver);
            callGraph = execution.CallGraph;
            cancellationToken.ThrowIfCancellationRequested();

            callGraph.MethodSignals.TryGetValue(
                methodToken,
                out MethodSignals? signals);
            execution.Allocations.Occurrences.TryGetValue(
                methodToken,
                out ImmutableArray<AllocationOccurrence> allocations);
            callGraph.DirectCallsByEvidenceMethod.TryGetValue(
                methodToken,
                out ImmutableArray<DirectCall> directCalls);

            var result = new AssemblyMethodAnalysis(
                methodToken,
                method,
                signals ?? MethodSignals.None,
                EmptyIfDefault(allocations),
                EmptyIfDefault(directCalls),
                focused.UnsafeOccurrences,
                focused.SafetyEvidence,
                focused.ExceptionRegions,
                [
                    .. execution.Optimization.Opportunities.Where(
                        opportunity =>
                            (opportunity.EvidenceMethodToken
                                ?? opportunity.Method.MetadataToken)
                            == methodToken),
                ],
                [
                    .. focused.Diagnostics
                        .Concat(
                            execution.Receipt.Diagnostics.Where(
                                diagnostic =>
                                    diagnostic.MethodToken == methodToken))
                        .Distinct(),
                ]);
            resolver.ValidateForPublication();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            callGraph?.ReleaseCaches();
        }
    }

    static ExactMethodAnalysisBody ExecuteFocused(
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

        MethodDefinitionSourceRequest<
            ExactMethodAnalysisProducerResult> request =
                MethodDefinitionSourceRequest<
                    ExactMethodAnalysisProducerResult>.Create(
                        FocusedWork,
                        FocusedProducer,
                        MethodDefinitionSourceBreadth.ExactMethods(
                            (MethodDefinitionHandle)entity),
                        new(
                            maximumBodies: 1,
                            maximumEncodedIlBytes: int.MaxValue));
        AssemblyAnalysisOperation<
            ExactMethodAnalysisProducerResult> operation =
                AssemblyAnalysisOperation<
                    ExactMethodAnalysisProducerResult>.Create(
                        AssemblyContextAnalysisSource.Name(subject),
                        request);

        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(snapshot);
        AssemblyAnalysisServiceResult<
            ExactMethodAnalysisProducerResult> serviceResult =
                session.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));
        if (serviceResult
            is AssemblyAnalysisServiceResult<
                ExactMethodAnalysisProducerResult>.Rejected rejected)
        {
            throw new InvalidOperationException(
                "The exact-method Analysis operation was rejected: "
                    + $"{rejected.Kind}.");
        }

        AssemblyAnalysisExecution<
            ExactMethodAnalysisProducerResult> execution =
                ((AssemblyAnalysisServiceResult<
                    ExactMethodAnalysisProducerResult>.Completed)serviceResult)
                .Execution;
        ProducerResult<ExactMethodAnalysisProducerResult> producerResult =
            execution.ResultOf(FocusedProducer);
        if (!producerResult.HasValue)
        {
            throw new InvalidOperationException(
                producerResult.Failure?.Message
                    ?? producerResult.Critical?.Message
                    ?? "The exact-method Analysis producer failed.");
        }

        ExactMethodAnalysisBody? body =
            producerResult.Value!.Bodies.FirstOrDefault(
                candidate => candidate.MethodToken == methodToken);
        return body
            ?? throw new ArgumentException(
                $"Token 0x{methodToken:X8} is not a MethodDef in "
                    + $"'{subject.Identity.Name}'.",
                nameof(methodToken));
    }

    static WorkDescription CreateFocusedWork() =>
        ProducerPlanner.Plan(
            [
                new ProducerRequest(
                    FocusedProducer,
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
