using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one exact package Type inspection through a fresh active Workspace
/// realization and returns only its detached terminal envelope.
/// </summary>
public static class ExactTypeInspectionOperation
{
    public static async Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteAsync(
            ExactTypeInspectionRequest request,
            WorkspaceContextLoadOptions capabilities,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capabilities);

        await using var coordinator =
            new WorkspaceRealizationCoordinator();
        WorkspaceContextInput input = new()
        {
            Framework = request.TargetFramework,
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    request.PackageId,
                    request.Version,
                    request.TargetFramework),
            ],
        };
        var plan = new WorkspacePlan([], [input]);
        WorkspaceRealizationCandidateStartResult start =
            await coordinator.BeginCandidateAsync(plan)
                .ConfigureAwait(false);
        if (start
            is not WorkspaceRealizationCandidateStartResult.Prepared prepared)
        {
            return Envelope(
                ExactTypeInspectionResult.RuntimeUnavailable(
                    request,
                    "The exact Type Workspace realization could not be prepared."),
                request);
        }

        WorkspaceContextLoadOutcome load;
        using (WorkspaceRealizationConstructionLease construction =
            prepared.Candidate.EnterConstruction())
        {
            load = await WorkspaceContextLoader.LoadAsync(
                    construction.Workspace,
                    input,
                    capabilities,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        if (load is WorkspaceContextLoadOutcome.Failed failed)
        {
            WorkspaceRealizationCandidateRetirementResult retirement =
                coordinator.AbandonCandidate(prepared.Candidate);
            if (retirement
                is WorkspaceRealizationCandidateRetirementResult.Retiring
                    retiring)
            {
                await retiring.Retirement.Completion.ConfigureAwait(false);
            }
            return Envelope(
                ExactTypeInspectionResult.ContextUnavailable(
                    request,
                    failed.Failures),
                request);
        }

        WorkspaceRealizationCandidateCompletionResult completion =
            await coordinator.CompleteCandidateAsync(
                    prepared.Candidate,
                    cancellationToken)
                .ConfigureAwait(false);
        if (completion
            is not WorkspaceRealizationCandidateCompletionResult.Ready)
        {
            return Envelope(
                ExactTypeInspectionResult.RuntimeUnavailable(
                    request,
                    "The exact Type Workspace realization did not become ready."),
                request);
        }
        WorkspaceRealizationCutoverResult cutover =
            coordinator.CutOver(prepared.Candidate);
        if (cutover is not WorkspaceRealizationCutoverResult.Activated)
        {
            return Envelope(
                ExactTypeInspectionResult.RuntimeUnavailable(
                    request,
                    "The exact Type Workspace realization did not become active."),
                request);
        }

        WorkspaceRealizationOperationAdmission admission =
            await coordinator.EnterOperationAsync(cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            return Envelope(
                ExactTypeInspectionResult.RuntimeUnavailable(
                    request,
                    "The active exact Type Workspace realization did not admit the operation."),
                request);
        }

        using (WorkspaceRealizationOperationLease authority =
            admitted.Lease)
        {
            return Execute(
                authority,
                (WorkspaceContextLoadOutcome.Loaded)load,
                request);
        }
    }

    /// <summary>
    /// Executes against one already admitted realization and its matching
    /// loaded assembly context.
    /// </summary>
    public static InspectionEnvelope<ExactTypeInspectionResult> Execute(
        WorkspaceRealizationOperationLease authority,
        WorkspaceContextLoadOutcome.Loaded loaded,
        ExactTypeInspectionRequest request) =>
        Envelope(
            ExactTypeInspectionQuery.Execute(
                authority,
                new ExactTypeInspectionContext(loaded),
                request),
            request);

    static InspectionEnvelope<ExactTypeInspectionResult> Envelope(
        ExactTypeInspectionResult result,
        ExactTypeInspectionRequest request) =>
        new(
            result,
            ProjectShare(request, result),
            Diagnostics(result));

    static InspectionShare ProjectShare(
        ExactTypeInspectionRequest request,
        ExactTypeInspectionResult result)
    {
        var coordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                request.PackageId,
                request.Version,
                request.TargetFramework);
        var workspace = new WorkspaceDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [
                new WorkspaceContextDefinition(
                    "g0",
                    framework: request.TargetFramework,
                    members: [coordinate]),
            ]);
        var navigation = new NavigationDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            WorkspaceSharePacketTransposer.NavigationId,
            [
                new NavigationTabDefinition(
                    "t0",
                    coordinate: coordinate),
            ],
            "t0");
        var view = new ViewDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            WorkspaceSharePacketTransposer.ViewId,
            lens: "api",
            type: result.MatchedType ?? request.Type);
        var scenario = new ScenarioDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            WorkspaceSharePacketTransposer.ScenarioId,
            workspace: workspace.Id,
            context: "g0",
            view: view.Id,
            navigation: navigation.Id);
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                new WorkspaceSharePacketDefinitionSet(
                    workspace,
                    navigation,
                    view,
                    scenario));
        if (!projection.Succeeded)
        {
            WorkspaceSharePacketProjectionFailure failure =
                projection.Failure!;
            return new InspectionShare.NonProjectable(
                $"exact-type-share/{failure.Path}",
                failure.Message);
        }

        string encoded =
            WorkspaceSharePacketCodec.Encode(projection.Packet!);
        return new InspectionShare.Available(
            "https://dotnet-inspect.net/?w=" + encoded,
            encoded);
    }

    static ImmutableArray<InspectionDiagnostic> Diagnostics(
        ExactTypeInspectionResult result)
    {
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        foreach (ExactTypeInspectionFailure failure in result.Failures)
        {
            InspectionDiagnosticSeverity severity =
                failure.Kind
                    is ExactTypeInspectionFailureKind.InspectionIncomplete
                    or ExactTypeInspectionFailureKind.ParticipantRejected
                    ? InspectionDiagnosticSeverity.Warning
                    : InspectionDiagnosticSeverity.Error;
            diagnostics.Add(
                new InspectionDiagnostic(
                    DiagnosticCode(failure.Kind),
                    severity,
                    failure.Detail,
                    failure.Assembly?.Name));
        }

        switch (result.Outcome)
        {
            case ExactTypeInspectionOutcome.NotFound:
                diagnostics.Add(
                    new InspectionDiagnostic(
                        "exact-type.not-found",
                        InspectionDiagnosticSeverity.Error,
                        $"Type '{result.RequestedType}' was not found."));
                break;
            case ExactTypeInspectionOutcome.Ambiguous:
                diagnostics.Add(
                    new InspectionDiagnostic(
                        "exact-type.ambiguous",
                        InspectionDiagnosticSeverity.Error,
                        $"Type '{result.RequestedType}' resolved to more than one exact Metadata definition."));
                break;
        }

        return diagnostics.DrainToImmutable();
    }

    static string DiagnosticCode(
        ExactTypeInspectionFailureKind kind) =>
        kind switch
        {
            ExactTypeInspectionFailureKind.ContextLoad =>
                "exact-type.context-load",
            ExactTypeInspectionFailureKind.ParticipantRejected =>
                "exact-type.participant-rejected",
            ExactTypeInspectionFailureKind.MetadataMalformed =>
                "exact-type.metadata-malformed",
            ExactTypeInspectionFailureKind.BindingPolicyUnsupported =>
                "exact-type.binding-policy-unsupported",
            ExactTypeInspectionFailureKind.TypeResolutionUnavailable =>
                "exact-type.type-resolution-unavailable",
            ExactTypeInspectionFailureKind.SupplierUnavailable =>
                "exact-type.supplier-unavailable",
            ExactTypeInspectionFailureKind.InspectionIncomplete =>
                "exact-type.inspection-incomplete",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown exact Type inspection failure kind."),
        };
}
