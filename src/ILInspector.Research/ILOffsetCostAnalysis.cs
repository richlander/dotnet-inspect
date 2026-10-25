using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Research;

/// <summary>
/// Runs cost-only IL-coordinate preparation through one exact Method-source
/// operation over an already-open assembly image.
/// </summary>
public static class ILOffsetCostAnalysis
{
    static readonly FocusedMethodCallProducer Producer =
        FocusedMethodCallProducer.Instance;
    static readonly WorkDescription Work = CreateWork();

    public static ILOffsetCostAnalysisInput Execute(
        AssemblyInspectionSession session,
        string subjectName,
        Guid moduleVersionId,
        IEnumerable<int> methodTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);
        ArgumentNullException.ThrowIfNull(methodTokens);
        cancellationToken.ThrowIfCancellationRequested();

        ImmutableArray<MethodDefinitionHandle> methods =
        [
            .. methodTokens
                .Distinct()
                .Select(MethodHandle),
        ];
        if (methods.IsEmpty)
        {
            throw new ArgumentException(
                "IL-offset cost analysis requires at least one MethodDef.",
                nameof(methodTokens));
        }

        MethodDefinitionSourceRequest<
            FocusedMethodCallProducerResult> request =
                MethodDefinitionSourceRequest<
                    FocusedMethodCallProducerResult>.Create(
                        Work,
                        Producer,
                        MethodDefinitionSourceBreadth.ExactMethods(methods),
                        new(
                            maximumBodies: methods.Length,
                            maximumEncodedIlBytes: session.ImageLength));
        AssemblyAnalysisOperation<
            FocusedMethodCallProducerResult> operation =
                AssemblyAnalysisOperation<
                    FocusedMethodCallProducerResult>.Create(
                        subjectName,
                        request.AsAssemblyAnalysisSource());

        AssemblyAnalysisServiceResult<
            FocusedMethodCallProducerResult> serviceResult =
                session.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));
        cancellationToken.ThrowIfCancellationRequested();
        if (serviceResult
            is AssemblyAnalysisServiceResult<
                FocusedMethodCallProducerResult>.Rejected rejected)
        {
            throw new InvalidOperationException(
                "The IL-offset cost Analysis operation was rejected: "
                    + $"{rejected.Kind}.");
        }

        AssemblyAnalysisExecution<
            FocusedMethodCallProducerResult> execution =
                ((AssemblyAnalysisServiceResult<
                    FocusedMethodCallProducerResult>.Completed)serviceResult)
                .Execution;
        ProducerResult<FocusedMethodCallProducerResult> producerResult =
            execution.ResultOf(Producer);
        if (!producerResult.HasValue)
        {
            throw new InvalidOperationException(
                producerResult.Failure?.Message
                    ?? producerResult.Critical?.Message
                    ?? "The IL-offset cost Analysis producer failed.");
        }

        ImmutableArray<FocusedMethodCallBody> bodies =
            producerResult.Value!.Bodies;
        return new(
            moduleVersionId,
            bodies
                .SelectMany(static body => body.Calls)
                .GroupBy(static site => site.Identity.MethodToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .Select(static site => site.Call)
                        .ToImmutableArray()),
            [
                .. bodies.SelectMany(
                    static body => body.Diagnostics),
            ],
            execution.MethodDefinitionReceipt(),
            execution.WorkReceipt);
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
                "The IL-offset cost Analysis request must plan.");
}
