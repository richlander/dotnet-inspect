using System.Collections.Immutable;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one exact package Type inspection through PackageHouse and an
/// admitted Workspace realization, returning only its detached terminal
/// envelope.
/// </summary>
public static class ExactTypeInspectionOperation
{
    public static async Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteAsync(
            ExactTypeInspectionRequest request,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation,
            CancellationToken cancellationToken = default)
        => await ExecuteAsyncCore(
            request,
            house,
            sourceOperation,
            projectionLimits: null,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Executes one cold exact-Type inspection under caller-supplied
    /// API-surface projection limits.
    /// </summary>
    public static async Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteAsync(
            ExactTypeInspectionRequest request,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation,
            ApiSurfaceProjectionLimits projectionLimits,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectionLimits);
        return await ExecuteAsyncCore(
            request,
            house,
            sourceOperation,
            projectionLimits,
            cancellationToken).ConfigureAwait(false);
    }

    static async Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteAsyncCore(
            ExactTypeInspectionRequest request,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation,
            ApiSurfaceProjectionLimits? projectionLimits,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(sourceOperation);

        PackageEndpointScopeRequest endpointRequest;
        try
        {
            endpointRequest = new(
                PackageSourceCoordinate.Create(
                    request.PackageId,
                    request.Version),
                request.TargetFramework,
                PackageAssetDemand.Surface);
        }
        catch
        {
            sourceOperation.Dispose();
            throw;
        }
        PackageEndpointScopeOutcome opened =
            await PackageEndpointScope.OpenAsync(
                    house,
                    sourceOperation,
                    endpointRequest,
                    cancellationToken)
                .ConfigureAwait(false);
        if (opened is PackageEndpointScopeOutcome.Failed failed)
        {
            return Envelope(
                ExactTypeInspectionResult.ContextUnavailable(
                    request,
                    $"{failed.Kind}: {failed.Message}"),
                request);
        }

        await using PackageEndpointScope scope =
            ((PackageEndpointScopeOutcome.Opened)opened).Scope;
        ArtifactRootResult<
            InspectionEnvelope<ExactTypeInspectionResult>> execution =
            await scope.UsePackageAssemblyRolesAsync(
                    async (workspace, package, realization, token) =>
                        await ExecuteRetainedCoreAsync(
                                workspace,
                                package,
                                realization,
                                request,
                                projectionLimits,
                                token)
                            .ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(false);
        return execution switch
        {
            ArtifactRootResult<
                InspectionEnvelope<ExactTypeInspectionResult>>.Available
                    available =>
                available.Value,
            ArtifactRootResult<
                InspectionEnvelope<ExactTypeInspectionResult>>.Rejected
                    rejected =>
                Envelope(
                    ExactTypeInspectionResult.ContextUnavailable(
                        request,
                        "The admitted package Root rejected exact Type "
                            + $"inspection: {rejected.Failure}"),
                    request),
            _ => throw new InvalidOperationException(
                "Package endpoint execution returned an unknown result."),
        };
    }

    /// <summary>
    /// Executes against one already admitted package Root and its matching
    /// assembly-role realization.
    /// </summary>
    public static Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteAsync(
        InspectionWorkspace workspace,
        PackageRootBinding package,
        PackageAssemblyContextRealization realization,
        ExactTypeInspectionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteRetainedCoreAsync(
            workspace,
            package,
            realization,
            request,
            projectionLimits: null,
            cancellationToken);

    /// <summary>
    /// Executes against one already admitted package Root under caller-supplied
    /// API-surface projection limits.
    /// </summary>
    public static Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteAsync(
        InspectionWorkspace workspace,
        PackageRootBinding package,
        PackageAssemblyContextRealization realization,
        ExactTypeInspectionRequest request,
        ApiSurfaceProjectionLimits projectionLimits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectionLimits);
        return ExecuteRetainedCoreAsync(
            workspace,
            package,
            realization,
            request,
            projectionLimits,
            cancellationToken);
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

    static async Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteRetainedCoreAsync(
        InspectionWorkspace workspace,
        PackageRootBinding package,
        PackageAssemblyContextRealization realization,
        ExactTypeInspectionRequest request,
        ApiSurfaceProjectionLimits? projectionLimits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(realization);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ImmutableArray<PackageAssemblyRoleParticipant> participants =
        [
            .. realization.SurfaceParticipants.Where(participant =>
                ReferenceEquals(
                    participant.Package,
                    package.Root.Identity)),
        ];
        if (participants.IsEmpty)
        {
            return Envelope(
                ExactTypeInspectionResult.ContextUnavailable(
                    request,
                    "The admitted package Root has no surface participant."),
                request);
        }

        var selected = new HashSet<AssemblyContextParticipant>(
            participants.Select(static participant =>
                participant.Participant),
            ReferenceEqualityComparer.Instance);
        if (realization.SurfaceGroup.Participants.Length == selected.Count
            && realization.SurfaceGroup.Participants.All(selected.Contains))
        {
            return ExecuteCore(
                workspace,
                Loaded(
                    workspace.Identity,
                    realization.SurfaceGroup,
                    selected,
                    package,
                    request),
                request,
                projectionLimits);
        }

        var retained =
            ImmutableArray.CreateBuilder<AssemblyContextParticipant>(
                participants.Length);
        foreach (PackageAssemblyRoleParticipant participant
            in participants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyImageAccessResult<ResolvedAssemblyReference> access =
                realization.SurfaceGroup.RetainAssemblyReference(
                    participant.Participant.Assembly);
            if (access
                is AssemblyImageAccessResult<ResolvedAssemblyReference>
                    .Rejected rejected)
            {
                return Envelope(
                    new ExactTypeInspectionResult(
                        ExactTypeInspectionOutcome.Unavailable,
                        request.Type,
                        MatchedType: null,
                        Type: null,
                        RequestedAssembly: null,
                        SupplierAssembly: null,
                        ForwardingHops: [],
                        Suggestions: [],
                        InspectionFailures: [],
                        Failures:
                        [
                            new ExactTypeInspectionFailure(
                                ExactTypeInspectionFailureKind
                                    .ParticipantRejected,
                                rejected.Failure.Detail,
                                participant.Participant.Assembly.Identity),
                        ]),
                    request);
            }
            retained.Add(
                new AssemblyContextParticipant(
                    ((AssemblyImageAccessResult<ResolvedAssemblyReference>
                        .Available)access).Value,
                    participant.Participant.BindingPolicy));
        }

        await using var selectedWorkspace = new InspectionWorkspace();
        using AssemblyContextGroup selectedGroup =
            selectedWorkspace.CreateAssemblyContextGroup(retained);
        return ExecuteCore(
            selectedWorkspace,
            Loaded(
                selectedWorkspace.Identity,
                selectedGroup,
                retained,
                package,
                request),
            request,
            projectionLimits);
    }

    static WorkspaceContextLoadOutcome.Loaded Loaded(
        InspectionWorkspaceIdentity workspace,
        AssemblyContextGroup group,
        IEnumerable<AssemblyContextParticipant> participants,
        PackageRootBinding package,
        ExactTypeInspectionRequest request)
    {
        WorkspaceMemberCoordinate declared =
            WorkspaceMemberCoordinate.Package(
                request.PackageId,
                request.Version,
                request.TargetFramework,
                package.Coordinate.RuntimeIdentifier);
        return new(
            workspace,
            group,
            [
                .. participants.Select(participant =>
                    new WorkspaceContextMember(
                        declared,
                        package.Coordinate,
                        participant)),
            ],
            [package],
            [],
            package.Coordinate.Framework,
            package.Coordinate.RuntimeIdentifier);
    }

    static InspectionEnvelope<ExactTypeInspectionResult> Envelope(
        ExactTypeInspectionResult result,
        ExactTypeInspectionRequest request) =>
        new(
            result,
            ProjectShare(request, result),
            Diagnostics(result));

    internal static InspectionShare ProjectShare(
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
            return new InspectionShare.NonProjectable(
                $"exact-type-share/{failure.Path}",
                failure.Message);
        }

        string encoded =
            WorkspaceSharePacketCodec.Encode(projection.Packet!);
        return new InspectionShare.Available(
            WorkspaceShareUrl.Create(encoded),
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
