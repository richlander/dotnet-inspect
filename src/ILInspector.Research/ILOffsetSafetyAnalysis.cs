using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Research;

/// <summary>
/// Runs safety-only IL-coordinate preparation through one exact Method-source
/// operation over an already-open assembly image.
/// </summary>
public static class ILOffsetSafetyAnalysis
{
    static readonly ExactMethodAnalysisProducer Producer =
        ExactMethodAnalysisProducer.Instance;
    static readonly WorkDescription Work = CreateWork();

    public static ILOffsetSafetyAnalysisInput Execute(
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
                "IL-offset safety analysis requires at least one MethodDef.",
                nameof(methodTokens));
        }

        MethodDefinitionSourceRequest<
            ExactMethodAnalysisProducerResult> request =
                MethodDefinitionSourceRequest<
                    ExactMethodAnalysisProducerResult>.Create(
                        Work,
                        Producer,
                        MethodDefinitionSourceBreadth.ExactMethods(methods),
                        new(
                            maximumBodies: methods.Length,
                            maximumEncodedIlBytes: session.ImageLength));
        AssemblyAnalysisOperation<
            ExactMethodAnalysisProducerResult> operation =
                AssemblyAnalysisOperation<
                    ExactMethodAnalysisProducerResult>.Create(
                        subjectName,
                        request.AsAssemblyAnalysisSource());

        AssemblyAnalysisServiceResult<
            ExactMethodAnalysisProducerResult> serviceResult =
                session.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));
        cancellationToken.ThrowIfCancellationRequested();
        if (serviceResult
            is AssemblyAnalysisServiceResult<
                ExactMethodAnalysisProducerResult>.Rejected rejected)
        {
            throw new InvalidOperationException(
                "The IL-offset safety Analysis operation was rejected: "
                    + $"{rejected.Kind}.");
        }

        AssemblyAnalysisExecution<
            ExactMethodAnalysisProducerResult> execution =
                ((AssemblyAnalysisServiceResult<
                    ExactMethodAnalysisProducerResult>.Completed)serviceResult)
                .Execution;
        ProducerResult<ExactMethodAnalysisProducerResult> producerResult =
            execution.ResultOf(Producer);
        if (!producerResult.HasValue)
        {
            throw new InvalidOperationException(
                producerResult.Failure?.Message
                    ?? producerResult.Critical?.Message
                    ?? "The IL-offset safety Analysis producer failed.");
        }

        ImmutableArray<ExactMethodAnalysisBody> bodies =
            producerResult.Value!.Bodies;
        return new(
            moduleVersionId,
            bodies.ToDictionary(
                body => body.MethodToken,
                body => body.UnsafeOccurrences),
            bodies.ToDictionary(
                body => body.MethodToken,
                body => body.SafetyEvidence),
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
                "The IL-offset safety Analysis request must plan.");
}
