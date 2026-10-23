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
public static partial class ExactTypeInspectionOperation
{
    public static async Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteAsync(
            ExactTypeInspectionRequest request,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation,
            CancellationToken cancellationToken = default)
        => await ExecuteWithLiveTargetAsyncCore(
                request,
                house,
                sourceOperation,
                projectionLimits: null,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                static (inspection, _, _) =>
                    ValueTask.FromResult(inspection),
                cancellationToken)
            .ConfigureAwait(false);

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
        return await ExecuteWithLiveTargetAsyncCore(
                request,
                house,
                sourceOperation,
                projectionLimits,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                static (inspection, _, _) =>
                    ValueTask.FromResult(inspection),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes one exact package Type inspection and lends its live defining
    /// target to one asynchronous consumer while the admitted package scope is
    /// still alive.
    /// </summary>
    public static Task<TResult> ExecuteWithLiveTargetAsync<TResult>(
        ExactTypeInspectionRequest request,
        PackageHouse house,
        PackageSourceOperationLease sourceOperation,
        Func<
            InspectionEnvelope<ExactTypeInspectionResult>,
            SelectedContextExactTypeLiveTarget?,
            CancellationToken,
            ValueTask<TResult>> use,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(use);
        return ExecuteWithLiveTargetAsyncCore(
            request,
            house,
            sourceOperation,
            projectionLimits: null,
            ApiSurfaceScope.PublicWithNonPublicTypes,
            use,
            cancellationToken);
    }

    /// <summary>
    /// Executes one exact package Type inspection under the requested API
    /// surface scope and lends its live defining target to one asynchronous
    /// consumer while the admitted package scope is still alive.
    /// </summary>
    public static Task<TResult> ExecuteWithLiveTargetAsync<TResult>(
        ExactTypeInspectionRequest request,
        PackageHouse house,
        PackageSourceOperationLease sourceOperation,
        ApiSurfaceScope scope,
        Func<
            InspectionEnvelope<ExactTypeInspectionResult>,
            SelectedContextExactTypeLiveTarget?,
            CancellationToken,
            ValueTask<TResult>> use,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(use);
        return ExecuteWithLiveTargetAsyncCore(
            request,
            house,
            sourceOperation,
            projectionLimits: null,
            scope,
            use,
            cancellationToken);
    }

    /// <summary>
    /// Executes ordered exact package Type candidates under one admitted
    /// package scope, stopping at the first outcome other than not found, and
    /// lends any live defining target to one asynchronous consumer.
    /// </summary>
    public static Task<TResult> ExecuteFirstAvailableWithLiveTargetAsync<TResult>(
        IReadOnlyList<ExactTypeInspectionRequest> requests,
        PackageHouse house,
        PackageSourceOperationLease sourceOperation,
        ApiSurfaceScope scope,
        Func<
            InspectionEnvelope<ExactTypeInspectionResult>,
            SelectedContextExactTypeLiveTarget?,
            CancellationToken,
            ValueTask<TResult>> use,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(use);
        return ExecuteWithLiveTargetAsyncCore(
            requests,
            house,
            sourceOperation,
            projectionLimits: null,
            scope,
            use,
            cancellationToken);
    }

    static async Task<TResult> ExecuteWithLiveTargetAsyncCore<TResult>(
            ExactTypeInspectionRequest request,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation,
            ApiSurfaceProjectionLimits? projectionLimits,
            ApiSurfaceScope surfaceScope,
            Func<
                InspectionEnvelope<ExactTypeInspectionResult>,
                SelectedContextExactTypeLiveTarget?,
                CancellationToken,
                ValueTask<TResult>> use,
            CancellationToken cancellationToken)
        => await ExecuteWithLiveTargetAsyncCore(
                [request],
                house,
                sourceOperation,
                projectionLimits,
                surfaceScope,
                use,
                cancellationToken)
            .ConfigureAwait(false);

    static async Task<TResult> ExecuteWithLiveTargetAsyncCore<TResult>(
            IReadOnlyList<ExactTypeInspectionRequest> requests,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation,
            ApiSurfaceProjectionLimits? projectionLimits,
            ApiSurfaceScope surfaceScope,
            Func<
                InspectionEnvelope<ExactTypeInspectionResult>,
                SelectedContextExactTypeLiveTarget?,
                CancellationToken,
                ValueTask<TResult>> use,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(sourceOperation);
        ArgumentNullException.ThrowIfNull(use);

        ExactTypeInspectionRequest request;
        PackageEndpointScopeRequest endpointRequest;
        try
        {
            request = ValidateCandidateRequests(requests);
            endpointRequest = PackageEndpointScopeRequest
                .ForCompileLibraries(
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
            return await use(
                    Envelope(
                        ExactTypeInspectionResult.ContextUnavailable(
                            request,
                            $"{failed.Kind}: {failed.Message}"),
                        request),
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await using PackageEndpointScope scope =
            ((PackageEndpointScopeOutcome.Opened)opened).Scope;
        ArtifactRootResult<TResult> execution =
            await scope.UsePackageAssemblyRolesAsync(
                    async (workspace, package, realization, token) =>
                    {
                        ExactTypeInspectionExecution retained =
                            await ExecuteRetainedExecutionCoreAsync(
                                workspace,
                                package,
                                realization,
                                request,
                                projectionLimits,
                                surfaceScope,
                                token)
                            .ConfigureAwait(false);
                        ExactTypeInspectionRequest selectedRequest = request;
                        if (retained.Result.Outcome
                            == ExactTypeInspectionOutcome.NotFound)
                        {
                            for (int index = 1;
                                index < requests.Count;
                                index++)
                            {
                                ExactTypeInspectionRequest candidate =
                                    requests[index];
                                ExactTypeInspectionExecution candidateResult =
                                    await ExecuteRetainedExecutionCoreAsync(
                                        workspace,
                                        package,
                                        realization,
                                        candidate,
                                        projectionLimits,
                                        surfaceScope,
                                        token)
                                    .ConfigureAwait(false);
                                if (candidateResult.Result.Outcome
                                    == ExactTypeInspectionOutcome.NotFound)
                                {
                                    continue;
                                }

                                retained = candidateResult;
                                selectedRequest = candidate;
                                break;
                            }
                        }
                        return await use(
                                Envelope(
                                    retained.Result,
                                    selectedRequest),
                                LiveTarget(retained.Target),
                                token)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        return execution switch
        {
            ArtifactRootResult<TResult>.Available available =>
                available.Value,
            ArtifactRootResult<TResult>.Rejected rejected =>
                await use(
                        Envelope(
                            ExactTypeInspectionResult.ContextUnavailable(
                                request,
                                "The admitted package Root rejected exact Type "
                                    + $"inspection: {rejected.Failure}"),
                            request),
                        null,
                        cancellationToken)
                    .ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "Package endpoint execution returned an unknown result."),
        };
    }

    static ExactTypeInspectionRequest ValidateCandidateRequests(
        IReadOnlyList<ExactTypeInspectionRequest> requests)
    {
        if (requests.Count == 0)
        {
            throw new ArgumentException(
                "At least one exact Type request is required.",
                nameof(requests));
        }

        ExactTypeInspectionRequest first =
            requests[0]
            ?? throw new ArgumentException(
                "Exact Type requests cannot contain null.",
                nameof(requests));
        foreach (ExactTypeInspectionRequest? candidate in requests)
        {
            if (candidate is null)
            {
                throw new ArgumentException(
                    "Exact Type requests cannot contain null.",
                    nameof(requests));
            }
            if (!candidate.PackageId.Equals(
                    first.PackageId,
                    StringComparison.OrdinalIgnoreCase)
                || !candidate.Version.Equals(
                    first.Version,
                    StringComparison.OrdinalIgnoreCase)
                || !candidate.TargetFramework.Equals(
                    first.TargetFramework,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Exact Type candidates must share one package coordinate "
                        + "and target framework.",
                    nameof(requests));
            }
        }

        return first;
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
        ExactTypeInspectionExecution execution =
            await ExecuteRetainedExecutionCoreAsync(
                    workspace,
                    package,
                    realization,
                    request,
                    projectionLimits,
                    ApiSurfaceScope.PublicWithNonPublicTypes,
                    cancellationToken)
                .ConfigureAwait(false);
        return Envelope(execution.Result, request);
    }

    static async Task<ExactTypeInspectionExecution>
        ExecuteRetainedExecutionCoreAsync(
        InspectionWorkspace workspace,
        PackageRootBinding package,
        PackageAssemblyContextRealization realization,
        ExactTypeInspectionRequest request,
        ApiSurfaceProjectionLimits? projectionLimits,
        ApiSurfaceScope scope,
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
            return new(
                ExactTypeInspectionResult.ContextUnavailable(
                    request,
                    "The admitted package Root has no surface participant."),
                [],
                Target: null);
        }

        var selected = new HashSet<AssemblyContextParticipant>(
            participants.Select(static participant =>
                participant.Participant),
            ReferenceEqualityComparer.Instance);
        if (realization.SurfaceGroup.Participants.Length == selected.Count
            && realization.SurfaceGroup.Participants.All(selected.Contains))
        {
            return ExecuteExecutionCore(
                workspace,
                Loaded(
                    workspace.Identity,
                    realization.SurfaceGroup,
                    selected,
                    package,
                    request),
                request,
                scope,
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
                return new(
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
                    [],
                    Target: null);
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
        return ExecuteExecutionCore(
            selectedWorkspace,
            Loaded(
                selectedWorkspace.Identity,
                selectedGroup,
                retained,
                package,
                request),
            request,
            scope,
            projectionLimits);
    }

    static ExactTypeInspectionExecution ExecuteExecutionCore(
        InspectionWorkspace workspace,
        WorkspaceContextLoadOutcome.Loaded loaded,
        ExactTypeInspectionRequest request,
        ApiSurfaceScope scope,
        ApiSurfaceProjectionLimits? projectionLimits) =>
        ExactTypeInspectionQuery.ExecutePackageContext(
            workspace.Identity,
            new ExactTypeInspectionContext(loaded),
            request,
            scope,
            projectionLimits);

    static SelectedContextExactTypeLiveTarget? LiveTarget(
        ExactTypeInspectionTarget? target) =>
        target is null
            ? null
            : new(
                target.Surface.Surface,
                target.Type,
                target.Occurrence.Assembly,
                target.Occurrence,
                target.BindingPolicy,
                target.AssemblyPath,
                target.PackageAssetPath,
                target.PackageExtractPath)
            {
                OpenCompiledDocumentation =
                    target.OpenCompiledDocumentation,
            };

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
