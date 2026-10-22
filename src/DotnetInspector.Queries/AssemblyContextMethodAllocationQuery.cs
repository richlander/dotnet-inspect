using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Exact allocation evidence for one physical MethodDef body.
/// </summary>
public sealed record AssemblyMethodAllocationAnalysis(
    int RequestedMethodToken,
    MethodIdentity Method,
    ImmutableArray<AllocationOccurrence> Allocations,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

/// <summary>
/// Reads only allocation evidence for one exact method while the query layer
/// owns the retained workspace snapshot.
/// </summary>
public static class AssemblyContextMethodAllocationQuery
{
    static readonly ExactMethodAllocationProducer Producer =
        ExactMethodAllocationProducer.Instance;
    static readonly WorkDescription Work =
        CreateWork();

    public static InspectionQuery<
        AssemblyContextEntry<AssemblyMethodAllocationAnalysis>>
        Definition { get; } =
        new(
            "Assembly context method allocation analysis",
            InspectionCost.Unbounded);

    public static AssemblyContextEntry<AssemblyMethodAllocationAnalysis>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            int methodToken,
            CancellationToken cancellationToken = default)
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

    static AssemblyMethodAllocationAnalysis Analyze(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        int methodToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExactMethodAllocationBody focused =
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

        var resolver = AssemblyContextAnalysisSource.Resolver(
            group,
            subject);
        var result = new AssemblyMethodAllocationAnalysis(
            methodToken,
            method,
            focused.Allocations,
            focused.Diagnostics);
        resolver.ValidateForPublication();
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    static ExactMethodAllocationBody ExecuteFocused(
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
            ExactMethodAllocationProducerResult> request =
                MethodDefinitionSourceRequest<
                    ExactMethodAllocationProducerResult>.Create(
                        Work,
                        Producer,
                        MethodDefinitionSourceBreadth.ExactMethods(
                            (MethodDefinitionHandle)entity),
                        new(
                            maximumBodies: 1,
                            maximumEncodedIlBytes: int.MaxValue));
        AssemblyAnalysisOperation<
            ExactMethodAllocationProducerResult> operation =
                AssemblyAnalysisOperation<
                    ExactMethodAllocationProducerResult>.Create(
                        AssemblyContextAnalysisSource.Name(subject),
                        request);

        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(snapshot);
        AssemblyAnalysisServiceResult<
            ExactMethodAllocationProducerResult> serviceResult =
                session.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));
        if (serviceResult
            is AssemblyAnalysisServiceResult<
                ExactMethodAllocationProducerResult>.Rejected rejected)
        {
            throw new InvalidOperationException(
                "The exact-method allocation operation was rejected: "
                    + $"{rejected.Kind}.");
        }

        AssemblyAnalysisExecution<
            ExactMethodAllocationProducerResult> execution =
                ((AssemblyAnalysisServiceResult<
                    ExactMethodAllocationProducerResult>.Completed)
                        serviceResult)
                .Execution;
        ProducerResult<ExactMethodAllocationProducerResult> producerResult =
            execution.ResultOf(Producer);
        if (!producerResult.HasValue)
        {
            throw new InvalidOperationException(
                producerResult.Failure?.Message
                    ?? producerResult.Critical?.Message
                    ?? "The exact-method allocation producer failed.");
        }

        ExactMethodAllocationBody? body =
            producerResult.Value!.Bodies.FirstOrDefault(
                candidate => candidate.MethodToken == methodToken);
        return body
            ?? throw new ArgumentException(
                $"Token 0x{methodToken:X8} is not a MethodDef in "
                    + $"'{subject.Identity.Name}'.",
                nameof(methodToken));
    }

    static WorkDescription CreateWork() =>
        ProducerPlanner.Plan(
            [
                new ProducerRequest(
                    Producer,
                    ProducerTerminal.Rows),
            ])
        is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new ProducerContractException(
                "The exact-method allocation request must plan.");
}
