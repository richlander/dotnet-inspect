using Analysis = ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

internal sealed record AssemblyContextCallGraphAnalysisParticipant(
    AssemblyContextParticipant ContextParticipant,
    AssemblyContextSubject Subject,
    ResolvedAssemblyReference Assembly,
    Analysis.LibraryCallGraphAnalysisResult CallGraph);

internal abstract record AssemblyContextCallGraphAnalysisResult(
    AssemblyContextSubject Subject)
{
    internal sealed record Available(
        AssemblyContextCallGraphAnalysisParticipant Participant)
        : AssemblyContextCallGraphAnalysisResult(Participant.Subject);

    internal sealed record Rejected(
        AssemblyContextSubject Subject,
        CandidateOpenFailure Failure)
        : AssemblyContextCallGraphAnalysisResult(Subject);

    internal sealed record InvalidImage(
        AssemblyContextSubject Subject,
        Exception Error)
        : AssemblyContextCallGraphAnalysisResult(Subject);
}

internal static class AssemblyContextCallGraphAnalysis
{
    static readonly LibraryCallGraphProducer Producer =
        LibraryCallGraphProducer.Instance;

    static readonly LibraryCallGraphProducer
        .LibraryRootPathCallGraphProducer RootPathProducer =
            LibraryCallGraphProducer
                .LibraryRootPathCallGraphProducer.Instance;

    static readonly LibraryMethodLeverageProducer LeverageProducer =
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
                "The library Call Graph request must plan.");

    static readonly WorkDescription RootPathWork =
        ProducerPlanner.Plan(
        [
            new ProducerRequest(
                RootPathProducer,
                ProducerTerminal.Rows),
        ]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new ProducerContractException(
                "The library root-path Call Graph request must plan.");

    static readonly WorkDescription LeverageWork =
        ProducerPlanner.Plan(
        [
            new ProducerRequest(
                LeverageProducer,
                ProducerTerminal.Rows),
        ]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new ProducerContractException(
                "The library Method Leverage request must plan.");

    internal static AssemblyContextCallGraphAnalysisResult Execute(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);

        var subject = new AssemblyContextSubject(participant.Assembly);
        AssemblyImageAccessResult<
            AssemblyContextCallGraphAnalysisResult> access =
            group.UseSnapshot<AssemblyContextCallGraphAnalysisResult>(
                participant,
                cancellationToken,
                snapshot =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Analysis.LibraryCallGraphAnalysisResult callGraph =
                            Analyze(snapshot, subject);
                        cancellationToken.ThrowIfCancellationRequested();
                        ResolvedAssemblyReference assembly =
                            snapshot.RetainAssemblyReference(
                                participant.Assembly);
                        return new AssemblyContextCallGraphAnalysisResult
                            .Available(
                                new(
                                    participant,
                                    subject,
                                    assembly,
                                    callGraph));
                    }
                    catch (Exception exception)
                        when (MemberCallGraphSession
                            .IsInvalidImageException(exception))
                    {
                        return new AssemblyContextCallGraphAnalysisResult
                            .InvalidImage(
                                subject,
                                exception);
                    }
                });

        return access switch
        {
            AssemblyImageAccessResult<
                AssemblyContextCallGraphAnalysisResult>.Available available =>
                available.Value,
            AssemblyImageAccessResult<
                AssemblyContextCallGraphAnalysisResult>.Rejected rejected =>
                new AssemblyContextCallGraphAnalysisResult.Rejected(
                    subject,
                    rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };
    }

    internal static Analysis.LibraryCallGraphAnalysisResult Analyze(
        AssemblyImageSnapshot snapshot,
        AssemblyContextSubject subject) =>
        Analyze(snapshot, subject, Producer, Work);

    internal static Analysis.LibraryCallGraphAnalysisResult AnalyzeRootPaths(
        AssemblyImageSnapshot snapshot,
        AssemblyContextSubject subject)
    {
        MethodDefinitionSourceRequest<
            LibraryRootPathCallGraphProducerResult> request =
                MethodDefinitionSourceRequest<
                    LibraryRootPathCallGraphProducerResult>.Create(
                        RootPathWork,
                        RootPathProducer,
                        MethodDefinitionSourceBreadth
                            .AllDefinitions
                            .IncludeGeneratedExecutionBodies());
        var operation =
            AssemblyAnalysisOperation<
                LibraryRootPathCallGraphProducerResult>.Create(
                    AssemblyContextAnalysisSource.Name(subject),
                    request.AsAssemblyAnalysisSource());
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(snapshot);
        AssemblyAnalysisServiceResult<
            LibraryRootPathCallGraphProducerResult> serviceResult =
                session.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));
        if (serviceResult
            is AssemblyAnalysisServiceResult<
                LibraryRootPathCallGraphProducerResult>.Rejected rejected)
        {
            throw new BadImageFormatException(
                "The library root-path Call Graph operation was rejected: "
                + $"{rejected.Kind}.");
        }

        AssemblyAnalysisExecution<
            LibraryRootPathCallGraphProducerResult> execution =
                ((AssemblyAnalysisServiceResult<
                    LibraryRootPathCallGraphProducerResult>.Completed)
                        serviceResult)
                .Execution;
        ProducerResult<
            LibraryRootPathCallGraphProducerResult> producerResult =
                execution.ResultOf(RootPathProducer);
        MethodDefinitionSourceReceipt sourceReceipt =
            execution.MethodDefinitionReceipt();
        if (!producerResult.HasValue)
        {
            throw new BadImageFormatException(
                producerResult.Failure?.Message
                    ?? producerResult.Critical?.Message
                    ?? "The library root-path Call Graph producer failed.");
        }

        return Analysis.LibraryCallGraphAnalysisResult
            .FromRootPathProducer(
                operation.SourceName,
                producerResult.Value!,
                sourceReceipt,
                execution.WorkReceipt);
    }

    internal static Analysis.LibraryCallGraphAnalysisResult AnalyzeLeverage(
        AssemblyImageSnapshot snapshot,
        AssemblyContextSubject subject)
    {
        MethodDefinitionSourceRequest<
            LibraryMethodLeverageProducerResult> request =
                MethodDefinitionSourceRequest<
                    LibraryMethodLeverageProducerResult>.Create(
                        LeverageWork,
                        LeverageProducer,
                        MethodDefinitionSourceBreadth
                            .AllDefinitions
                            .IncludeGeneratedExecutionBodies());
        var operation =
            AssemblyAnalysisOperation<
                LibraryMethodLeverageProducerResult>.Create(
                    AssemblyContextAnalysisSource.Name(subject),
                    request.AsAssemblyAnalysisSource());
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(snapshot);
        AssemblyAnalysisServiceResult<
            LibraryMethodLeverageProducerResult> serviceResult =
                session.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));
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
                execution.ResultOf(LeverageProducer);
        MethodDefinitionSourceReceipt sourceReceipt =
            execution.MethodDefinitionReceipt();
        if (!producerResult.HasValue)
        {
            throw new BadImageFormatException(
                producerResult.Failure?.Message
                    ?? producerResult.Critical?.Message
                    ?? "The library Method Leverage producer failed.");
        }

        return Analysis.LibraryCallGraphAnalysisResult
            .FromLeverageProducer(
                operation.SourceName,
                producerResult.Value!,
                sourceReceipt,
                execution.WorkReceipt);
    }

    static Analysis.LibraryCallGraphAnalysisResult Analyze(
        AssemblyImageSnapshot snapshot,
        AssemblyContextSubject subject,
        LibraryCallGraphProducer producer,
        WorkDescription work)
    {
        MethodDefinitionSourceRequest<
            LibraryCallGraphProducerResult> request =
                MethodDefinitionSourceRequest<
                    LibraryCallGraphProducerResult>.Create(
                        work,
                        producer,
                        MethodDefinitionSourceBreadth
                            .AllDefinitions
                            .IncludeGeneratedExecutionBodies());
        var operation =
            AssemblyAnalysisOperation<
                LibraryCallGraphProducerResult>.Create(
                    AssemblyContextAnalysisSource.Name(subject),
                    request.AsAssemblyAnalysisSource());
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(snapshot);
        AssemblyAnalysisServiceResult<
            LibraryCallGraphProducerResult> serviceResult =
                session.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));
        if (serviceResult
            is AssemblyAnalysisServiceResult<
                LibraryCallGraphProducerResult>.Rejected rejected)
        {
            throw new BadImageFormatException(
                "The library Call Graph operation was rejected: "
                + $"{rejected.Kind}.");
        }

        AssemblyAnalysisExecution<
            LibraryCallGraphProducerResult> execution =
                ((AssemblyAnalysisServiceResult<
                    LibraryCallGraphProducerResult>.Completed)
                        serviceResult)
                .Execution;
        ProducerResult<LibraryCallGraphProducerResult> producerResult =
            execution.ResultOf(producer);
        MethodDefinitionSourceReceipt sourceReceipt =
            execution.MethodDefinitionReceipt();
        if (!producerResult.HasValue)
        {
            throw new BadImageFormatException(
                producerResult.Failure?.Message
                    ?? producerResult.Critical?.Message
                    ?? "The library Call Graph producer failed.");
        }

        return Analysis.LibraryCallGraphAnalysisResult.FromProducer(
            operation.SourceName,
            producerResult.Value!,
            sourceReceipt,
            execution.WorkReceipt);
    }
}
