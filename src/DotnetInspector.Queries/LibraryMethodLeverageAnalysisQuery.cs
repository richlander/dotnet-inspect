using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Executes complete whole-library Method Leverage analysis through Method
/// Query Source.
/// </summary>
public static class LibraryMethodLeverageAnalysisQuery
{
    static readonly LibraryMethodLeverageProducer Producer =
        LibraryMethodLeverageProducer.Instance;

    static readonly WorkDescription Work =
        ProducerPlanner.Plan(
        [
            new ProducerRequest(
                Producer,
                ProducerTerminal.Rows),
        ]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new ProducerContractException(
                "The library Method Leverage request must plan.");

    public static LibraryCallGraphAnalysisResult Execute(
        AssemblyInspectionSession session,
        string sourceName,
        AssemblyReferenceBindingAccess? referenceBinding = null,
        MethodDefinitionParallelExecution? parallelExecution = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        MethodDefinitionSourceRequest<
            LibraryMethodLeverageProducerResult> request =
                MethodDefinitionSourceRequest<
                    LibraryMethodLeverageProducerResult>.Create(
                        Work,
                        Producer,
                        MethodDefinitionSourceBreadth
                            .AllDefinitions
                            .IncludeGeneratedExecutionBodies());
        var operation =
            AssemblyAnalysisOperation<
                LibraryMethodLeverageProducerResult>.Create(
                    sourceName,
                    request.AsAssemblyAnalysisSource());
        AssemblyAnalysisServiceResult<
            LibraryMethodLeverageProducerResult> serviceResult =
                referenceBinding is null
                    ? session.SnapshotOperation(
                        operation,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access,
                                parallelExecution))
                    : session.SnapshotOperation(
                        operation,
                        referenceBinding,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access,
                                parallelExecution));
        if (serviceResult
            is AssemblyAnalysisServiceResult<
                LibraryMethodLeverageProducerResult>.Rejected rejected)
        {
            throw new BadImageFormatException(
                "The library Method Leverage operation was rejected: "
                + $"{rejected.Kind}.");
        }

        AssemblyAnalysisExecution<
            LibraryMethodLeverageProducerResult> execution =
                ((AssemblyAnalysisServiceResult<
                    LibraryMethodLeverageProducerResult>.Completed)
                        serviceResult)
                .Execution;
        ProducerResult<
            LibraryMethodLeverageProducerResult> producerResult =
                execution.ResultOf(Producer);
        MethodDefinitionSourceReceipt sourceReceipt =
            execution.MethodDefinitionReceipt();
        if (!producerResult.HasValue
            && producerResult.Outcome != ProducerOutcome.SourceBounded)
        {
            throw new BadImageFormatException(
                producerResult.Failure?.Message
                    ?? producerResult.Critical?.Message
                    ?? "The library Method Leverage producer failed.");
        }

        LibraryMethodLeverageProducerResult result =
            producerResult.Value
            ?? throw new ProducerContractException(
                "The library Method Leverage producer retained no result.");
        if (sourceReceipt.SourceFailure is { } sourceFailure)
        {
            result = result with
            {
                Diagnostics = result.Diagnostics.Add(
                    new(
                        sourceFailure.UnitToken,
                        sourceFailure.Unit,
                        sourceFailure.Message)),
            };
        }

        return LibraryCallGraphAnalysisResult.FromLeverageProducer(
            operation.SourceName,
            result,
            sourceReceipt,
            execution.WorkReceipt);
    }
}
