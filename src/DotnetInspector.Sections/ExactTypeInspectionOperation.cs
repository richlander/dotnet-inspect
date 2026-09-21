using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one exact package Type inspection through a fresh directly owned
/// Workspace and returns only its detached terminal envelope.
/// </summary>
public static class ExactTypeInspectionOperation
{
    public static async Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteAsync(
            ExactTypeInspectionRequest request,
            WorkspaceContextLoadOptions capabilities,
            CancellationToken cancellationToken = default)
        => await ExecuteAsyncCore(
            request,
            capabilities,
            projectionLimits: null,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Executes one cold exact-Type inspection under caller-supplied
    /// API-surface projection limits.
    /// </summary>
    public static async Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteAsync(
            ExactTypeInspectionRequest request,
            WorkspaceContextLoadOptions capabilities,
            ApiSurfaceProjectionLimits projectionLimits,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectionLimits);
        return await ExecuteAsyncCore(
            request,
            capabilities,
            projectionLimits,
            cancellationToken).ConfigureAwait(false);
    }

    static async Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteAsyncCore(
            ExactTypeInspectionRequest request,
            WorkspaceContextLoadOptions capabilities,
            ApiSurfaceProjectionLimits? projectionLimits,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capabilities);

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
        var workspace = new InspectionWorkspace(plan);
        InspectionEnvelope<ExactTypeInspectionResult> result;
        try
        {
            WorkspaceContextLoadOutcome load =
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    input,
                    capabilities,
                    cancellationToken)
                .ConfigureAwait(false);
            result = load switch
            {
                WorkspaceContextLoadOutcome.Failed failed =>
                    Envelope(
                        ExactTypeInspectionResult.ContextUnavailable(
                            request,
                            failed.Failures),
                        request),
                WorkspaceContextLoadOutcome.Loaded loaded =>
                    ExecuteCore(
                        workspace,
                        loaded,
                        request,
                        projectionLimits),
                _ => Envelope(
                    ExactTypeInspectionResult.RuntimeUnavailable(
                        request,
                        "The exact Type Workspace load returned an unsupported result."),
                    request),
            };
        }
        catch (Exception failure)
        {
            await DirectWorkspaceOperationLifetime.CloseAfterFailureAsync(
                    workspace,
                    failure)
                .ConfigureAwait(false);
            throw;
        }

        await DirectWorkspaceOperationLifetime.CloseAsync(
                workspace,
                "Exact Type inspection")
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Executes against one already admitted realization and its matching
    /// loaded assembly context.
    /// </summary>
    public static InspectionEnvelope<ExactTypeInspectionResult> Execute(
        WorkspaceRealizationOperationLease authority,
        WorkspaceContextLoadOutcome.Loaded loaded,
        ExactTypeInspectionRequest request) =>
        ExecuteCore(
            authority,
            loaded,
            request,
            projectionLimits: null);

    /// <summary>
    /// Executes against one admitted realization under caller-supplied
    /// API-surface projection limits.
    /// </summary>
    public static InspectionEnvelope<ExactTypeInspectionResult> Execute(
        WorkspaceRealizationOperationLease authority,
        WorkspaceContextLoadOutcome.Loaded loaded,
        ExactTypeInspectionRequest request,
        ApiSurfaceProjectionLimits projectionLimits)
    {
        ArgumentNullException.ThrowIfNull(projectionLimits);
        return ExecuteCore(authority, loaded, request, projectionLimits);
    }

    static InspectionEnvelope<ExactTypeInspectionResult> ExecuteCore(
        WorkspaceRealizationOperationLease authority,
        WorkspaceContextLoadOutcome.Loaded loaded,
        ExactTypeInspectionRequest request,
        ApiSurfaceProjectionLimits? projectionLimits) =>
        Envelope(
            ExactTypeInspectionQuery.Execute(
                authority,
                new ExactTypeInspectionContext(loaded),
                request,
                projectionLimits),
            request);

    static InspectionEnvelope<ExactTypeInspectionResult> ExecuteCore(
        InspectionWorkspace workspace,
        WorkspaceContextLoadOutcome.Loaded loaded,
        ExactTypeInspectionRequest request,
        ApiSurfaceProjectionLimits? projectionLimits) =>
        Envelope(
            ExactTypeInspectionQuery.Execute(
                workspace,
                new ExactTypeInspectionContext(loaded),
                request,
                projectionLimits),
            request);

    static InspectionEnvelope<ExactTypeInspectionResult> Envelope(
        ExactTypeInspectionResult result,
        ExactTypeInspectionRequest request) =>
        new(
            new ResourcePath("exact-type"),
            InspectionContentKind.Result,
            result,
            ProjectShare(request, result),
            Diagnostics(result));

    static InspectionPortableProjection ProjectShare(
        ExactTypeInspectionRequest request,
        ExactTypeInspectionResult result)
    {
        var coordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                request.PackageId,
                request.Version,
                request.TargetFramework);
        const int schemaVersion = InspectionDefinitionSchema.Version1;
        var workspace = new WorkspaceDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [
                new WorkspaceContextDefinition(
                    "g0",
                    framework: request.TargetFramework,
                    members: [coordinate]),
            ]);
        var navigation = new NavigationDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.NavigationId,
            [
                new NavigationTabDefinition(
                    "t0",
                    coordinate: coordinate),
            ],
            "t0");
        var view = new ViewDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.ViewId,
            lens: "api",
            type: result.MatchedType ?? request.Type);
        var scenario = new ScenarioDefinition(
            schemaVersion,
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
            return new InspectionPortableProjection.NonProjectable(
                failure.Kind
                    is WorkspaceSharePacketProjectionFailureKind
                        .InvalidDefinitionSet
                    ? InspectionPortableProjectionFailureReason.Invalid
                    : InspectionPortableProjectionFailureReason.NotSupported,
                location: failure.Path,
                explanation: failure.Message);
        }

        string encoded =
            WorkspaceSharePacketCodec.Encode(projection.Packet!);
        return new InspectionPortableProjection.Available(
            "https://dotnet-inspect.net/?w=" + encoded,
            encoded);
    }

    internal static ImmutableArray<InspectionDiagnostic> Diagnostics(
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
                    or ExactTypeInspectionFailureKind.ProjectionTruncated
                    ? InspectionDiagnosticSeverity.Warning
                    : InspectionDiagnosticSeverity.Error;
            diagnostics.Add(
                new InspectionDiagnostic(
                    DiagnosticCode(failure.Kind),
                    severity,
                    failure.Detail,
                    failure.Assembly?.Name));
        }

        foreach (ExactTypeApiInspectionFailure failure
                 in result.InspectionFailures)
        {
            if (!string.Equals(
                    failure.Operation,
                    ApiSurface.ConstraintResolutionOperation,
                    StringComparison.Ordinal))
            {
                continue;
            }

            string subject = failure.SubjectToken == 0
                ? string.Empty
                : $" for metadata subject 0x{failure.SubjectToken:x8}";
            diagnostics.Add(
                new InspectionDiagnostic(
                    "exact-type.constraint-resolution-incomplete",
                    InspectionDiagnosticSeverity.Warning,
                    "Generic-constraint classification was incomplete"
                        + subject
                        + $" ({failure.Mechanism}/{failure.Kind}): "
                        + failure.Detail,
                    failure.SubjectAssembly?.Name));
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
            ExactTypeInspectionFailureKind.DefiningSourceUnavailable =>
                "exact-type.defining-source-unavailable",
            ExactTypeInspectionFailureKind.InspectionIncomplete =>
                "exact-type.inspection-incomplete",
            ExactTypeInspectionFailureKind.ProjectionTruncated =>
                "exact-type.projection-truncated",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown exact Type inspection failure kind."),
        };
}
