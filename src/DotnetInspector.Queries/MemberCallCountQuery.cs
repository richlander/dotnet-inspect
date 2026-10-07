using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>One completed scalar count for a physical MethodDef body.</summary>
public sealed record MemberCallCountEvidence(
    int EvidenceMethodToken,
    int Count);

/// <summary>One physical MethodDef body that could not issue a scalar count.</summary>
public sealed record MemberCallCountUnavailableBody(
    int MethodToken,
    AnalysisDiagnostic? Diagnostic);

/// <summary>Actual physical-body participation for one scalar Count request.</summary>
public sealed record MemberCallCountParticipationReceipt(
    bool WasPlanned,
    int AttemptedBodies,
    int CompletedBodies,
    int FailedBodies);

/// <summary>
/// Detached source and producer evidence for one exact-member scalar Count.
/// </summary>
public sealed record MemberCallCountAnalysis(
    MethodDefinitionSourceReceipt SourceReceipt,
    WorkReceipt WorkReceipt,
    ImmutableArray<MemberCallCountEvidence> Counts,
    ImmutableArray<int> BodylessMethodTokens,
    ImmutableArray<MemberCallCountUnavailableBody> UnavailableBodies,
    MemberCallCountParticipationReceipt Participation,
    ImmutableArray<AnalysisDiagnostic> Diagnostics)
{
    public bool ScopeComplete =>
        SourceReceipt.Completion
            is MethodDefinitionSourceCompletion.Exhausted
                or MethodDefinitionSourceCompletion.Satisfied;

    public bool IsComplete =>
        ScopeComplete
        && UnavailableBodies.IsEmpty
        && Participation.FailedBodies == 0;
}

internal enum MemberCallCountExecutionStatus
{
    Available,
    Incomplete,
    Failed,
}

internal sealed record MemberCallCountExecution(
    MemberCallCountExecutionStatus Status,
    int Count,
    MemberCallCountAnalysis? Analysis,
    Exception? Error);

internal static class MemberCallCountQuery
{
    internal static MemberCallCountExecution Execute(
        string assemblyPath,
        int methodToken,
        ImplementationMetricWorkLimits limits,
        MethodCallCountProducer producer,
        string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        try
        {
            EntityHandle entity = MetadataTokens.EntityHandle(methodToken);
            if (entity.Kind != HandleKind.MethodDefinition)
            {
                throw new ArgumentException(
                    $"Token 0x{methodToken:X8} is not a MethodDef token.",
                    nameof(methodToken));
            }

            WorkDescription work =
                ProducerPlanner.Plan(
                    [
                        new ProducerRequest(
                            producer,
                            ProducerTerminal.Count),
                    ])
                is ProducerPlanResult.Accepted accepted
                    ? accepted.Description
                    : throw new ProducerContractException(
                        "The exact-member call Count request must plan.");
            MethodDefinitionSourceBreadth breadth =
                MethodDefinitionSourceBreadth
                    .ExactMethods((MethodDefinitionHandle)entity)
                    .IncludeGeneratedExecutionBodies(
                        new(
                            maximumCandidateDefinitions:
                                MethodDefinitionGeneratedExpansionLimits
                                    .Default
                                    .MaximumCandidateDefinitions,
                            maximumGeneratedMethods:
                                MethodDefinitionGeneratedExpansionLimits
                                    .Default
                                    .MaximumGeneratedMethods,
                            maximumProbeBodies:
                                limits.MaximumAttributionProbeBodies,
                            maximumProbeEncodedIlBytes:
                                limits.MaximumAttributionProbeIlBytes,
                            maximumRelationshipNodes:
                                MethodDefinitionGeneratedExpansionLimits
                                    .Default
                                    .MaximumRelationshipNodes));
            var request =
                MethodDefinitionSourceRequest<
                    MethodCallCountProducerResult>.Create(
                        work,
                        producer,
                        breadth,
                        new(
                            limits.MaximumPhysicalBodies,
                            limits.MaximumEncodedIlBytes));
            var operation =
                AssemblyAnalysisOperation<
                    MethodCallCountProducerResult>.Create(
                        sourceName,
                        request);

            using PdbContext context =
                PdbContext.OpenMetadataOnly(assemblyPath);
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Borrow(context);
            AssemblyAnalysisServiceResult<
                MethodCallCountProducerResult> serviceResult =
                    session.SnapshotOperation(
                        operation,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access));
            if (serviceResult
                is AssemblyAnalysisServiceResult<
                    MethodCallCountProducerResult>.Rejected rejected)
            {
                return Failed(
                    new InvalidOperationException(
                        "The exact-member call Count operation was rejected: "
                        + $"{rejected.Kind}."));
            }

            AssemblyAnalysisExecution<
                MethodCallCountProducerResult> execution =
                    ((AssemblyAnalysisServiceResult<
                        MethodCallCountProducerResult>.Completed)serviceResult)
                    .Execution;
            ProducerResult<MethodCallCountProducerResult> result =
                execution.ResultOf(producer);
            MemberCallCountAnalysis analysis =
                CreateAnalysis(
                    execution.SourceReceipt,
                    execution.WorkReceipt,
                    result.Value);

            if (execution.SourceReceipt.Completion
                == MethodDefinitionSourceCompletion.SourceIncomplete)
            {
                return new(
                    MemberCallCountExecutionStatus.Incomplete,
                    Count: 0,
                    analysis,
                    Error: null);
            }

            if (!result.HasValue)
            {
                return Failed(
                    new InvalidOperationException(
                        ResultFailure(
                            execution.SourceReceipt,
                            result)));
            }

            int count = 0;
            foreach (MemberCallCountEvidence body in analysis.Counts)
                count = checked(count + body.Count);

            return analysis.IsComplete
                ? new(
                    MemberCallCountExecutionStatus.Available,
                    count,
                    analysis,
                    Error: null)
                : new(
                    MemberCallCountExecutionStatus.Incomplete,
                    Count: 0,
                    analysis,
                    Error: null);
        }
        catch (Exception exception)
        {
            return Failed(exception);
        }
    }

    static MemberCallCountAnalysis CreateAnalysis(
        MethodDefinitionSourceReceipt sourceReceipt,
        WorkReceipt workReceipt,
        MethodCallCountProducerResult? result)
    {
        var counts =
            ImmutableArray.CreateBuilder<MemberCallCountEvidence>();
        var bodyless = ImmutableArray.CreateBuilder<int>();
        var unavailable =
            ImmutableArray.CreateBuilder<MemberCallCountUnavailableBody>();
        var diagnostics =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        int attemptedBodies = 0;
        int completedBodies = 0;
        int failedBodies = 0;

        foreach (MethodCallCountBody body in result?.Bodies ?? [])
        {
            if (!body.HasManagedBody)
            {
                bodyless.Add(body.MethodToken);
                continue;
            }

            attemptedBodies++;
            if (body.Count is { } count)
            {
                completedBodies++;
                counts.Add(new(body.MethodToken, count));
                continue;
            }

            failedBodies++;
            unavailable.Add(
                new(body.MethodToken, body.Diagnostic));
            if (body.Diagnostic is { } diagnostic)
                diagnostics.Add(diagnostic);
        }

        if (sourceReceipt.SourceFailure is { } sourceFailure)
        {
            diagnostics.Add(
                new(
                    sourceFailure.UnitToken,
                    sourceFailure.Unit,
                    sourceFailure.Message));
        }

        return new(
            sourceReceipt,
            workReceipt,
            counts.ToImmutable(),
            bodyless.ToImmutable(),
            unavailable.ToImmutable(),
            new(
                WasPlanned: true,
                attemptedBodies,
                completedBodies,
                failedBodies),
            diagnostics.ToImmutable());
    }

    static string ResultFailure(
        MethodDefinitionSourceReceipt sourceReceipt,
        ProducerResult<MethodCallCountProducerResult> result)
    {
        if (result.Failure is { } failure)
            return failure.Message;
        if (result.Critical is { } critical)
            return critical.Message;
        if (result.FailedPrerequisite is { } prerequisite)
        {
            return $"Required producer '{prerequisite}' failed.";
        }
        return "The exact-member call Count producer did not publish a result "
            + $"after source completion '{sourceReceipt.Completion}'.";
    }

    static MemberCallCountExecution Failed(Exception error) =>
        new(
            MemberCallCountExecutionStatus.Failed,
            Count: 0,
            Analysis: null,
            error);
}
