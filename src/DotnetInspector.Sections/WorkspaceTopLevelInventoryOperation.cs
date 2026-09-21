using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Sections;

public enum WorkspaceTopLevelInventoryShareRequestKind
{
    PacketInput,
    DefinitionInput,
    RealizedWorkspace,
}

public enum WorkspaceTopLevelInventoryShareNonProjectableReason
{
    NoRetainedDefinitionProjection,
    DefinitionsProjectionUnavailable,
}

public abstract record WorkspaceTopLevelInventoryShareProjection
{
    private protected WorkspaceTopLevelInventoryShareProjection()
    {
    }

    public sealed record Projectable : WorkspaceTopLevelInventoryShareProjection
    {
        public Projectable(string canonicalPacket)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPacket);
            CanonicalPacket = canonicalPacket;
        }

        public string CanonicalPacket { get; }
    }

    public sealed record NonProjectable(
        WorkspaceTopLevelInventoryShareNonProjectableReason Reason,
        string? Detail = null)
        : WorkspaceTopLevelInventoryShareProjection;
}

public sealed class WorkspaceTopLevelInventoryShareBasis
{
    WorkspaceTopLevelInventoryShareBasis(
        WorkspaceDefinitionSnapshot definition,
        WorkspaceTopLevelInventoryShareRequestKind requestKind,
        WorkspaceTopLevelInventoryShareProjection projection)
        : this(
            (definition
                ?? throw new ArgumentNullException(nameof(definition)))
                .Workspace,
            definition.Registrations.Identity,
            definition.Scope.Identity,
            requestKind,
            projection)
    {
    }

    public InspectionWorkspaceIdentity Workspace { get; }

    public WorkspaceRegistrationRevisionIdentity RegistrationRevision { get; }

    public WorkspaceScopeRevisionIdentity ScopeRevision { get; }

    public WorkspaceTopLevelInventoryShareRequestKind RequestKind { get; }

    public WorkspaceTopLevelInventoryShareProjection Projection { get; }

    public static WorkspaceTopLevelInventoryShareBasis CreateProjectable(
        WorkspaceDefinitionShareProjectionReceipt projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new(
            projection.Workspace,
            projection.RegistrationRevision,
            projection.ScopeRevision,
            projection.Source switch
            {
                WorkspaceDefinitionShareProjectionSource.PacketInput =>
                    WorkspaceTopLevelInventoryShareRequestKind.PacketInput,
                WorkspaceDefinitionShareProjectionSource.DefinitionInput =>
                    WorkspaceTopLevelInventoryShareRequestKind.DefinitionInput,
                _ => throw new InvalidOperationException(
                    "Unknown Workspace Definition Share projection source."),
            },
            new WorkspaceTopLevelInventoryShareProjection.Projectable(
                projection.CanonicalPacket));
    }

    public static WorkspaceTopLevelInventoryShareBasis
        CreateCompleteRestoration(
            CompleteWorkspaceActivation activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        WorkspaceDefinitionSnapshot definition =
            activation.Snapshot.Definition;
        return new(
            definition,
            activation.Request switch
            {
                CompleteRestorationRequestBasis.PacketInput =>
                    WorkspaceTopLevelInventoryShareRequestKind.PacketInput,
                CompleteRestorationRequestBasis.DefinitionInput =>
                    WorkspaceTopLevelInventoryShareRequestKind.DefinitionInput,
                _ => throw new InvalidOperationException(
                    "Unknown complete-restoration request basis."),
            },
            activation.Projection switch
            {
                CompleteRestorationProjection.Projectable projectable =>
                    new WorkspaceTopLevelInventoryShareProjection.Projectable(
                        projectable.CanonicalPacket),
                CompleteRestorationProjection.NonProjectable nonProjectable =>
                    new WorkspaceTopLevelInventoryShareProjection.NonProjectable(
                        WorkspaceTopLevelInventoryShareNonProjectableReason
                            .DefinitionsProjectionUnavailable,
                        nonProjectable.Reason),
                _ => throw new InvalidOperationException(
                    "Unknown complete-restoration Share projection."),
            });
    }

    public static WorkspaceTopLevelInventoryShareBasis CreateRealizedWorkspace(
        WorkspaceDefinitionSnapshot definition) =>
        new(
            definition,
            WorkspaceTopLevelInventoryShareRequestKind.RealizedWorkspace,
            new WorkspaceTopLevelInventoryShareProjection.NonProjectable(
                WorkspaceTopLevelInventoryShareNonProjectableReason
                    .NoRetainedDefinitionProjection));

    WorkspaceTopLevelInventoryShareBasis(
        InspectionWorkspaceIdentity workspace,
        WorkspaceRegistrationRevisionIdentity registrationRevision,
        WorkspaceScopeRevisionIdentity scopeRevision,
        WorkspaceTopLevelInventoryShareRequestKind requestKind,
        WorkspaceTopLevelInventoryShareProjection projection)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(registrationRevision);
        ArgumentNullException.ThrowIfNull(scopeRevision);
        if (!Enum.IsDefined(requestKind))
            throw new ArgumentOutOfRangeException(nameof(requestKind));

        Workspace = workspace;
        RegistrationRevision = registrationRevision;
        ScopeRevision = scopeRevision;
        RequestKind = requestKind;
        Projection =
            projection ?? throw new ArgumentNullException(nameof(projection));
    }

    internal bool Matches(WorkspaceDefinitionSnapshot definition) =>
        ReferenceEquals(Workspace, definition.Workspace)
        && ReferenceEquals(
            RegistrationRevision,
            definition.Registrations.Identity)
        && ReferenceEquals(ScopeRevision, definition.Scope.Identity);
}

public sealed record WorkspaceTopLevelInventoryExecution
{
    public WorkspaceTopLevelInventoryExecution(
        InspectionEnvelope<WorkspaceTopLevelInventoryOutcome> inspection,
        WorkspaceTopLevelInventorySelectionReceipt selection)
    {
        Inspection =
            inspection ?? throw new ArgumentNullException(nameof(inspection));
        Selection =
            selection ?? throw new ArgumentNullException(nameof(selection));
    }

    public InspectionEnvelope<WorkspaceTopLevelInventoryOutcome> Inspection
    {
        get;
    }

    [JsonIgnore]
    public WorkspaceTopLevelInventorySelectionReceipt Selection { get; }
}

public static class WorkspaceTopLevelInventoryOperation
{
    const string SharePath = "workspace-top-level-inventory/share";

    public static async ValueTask<WorkspaceTopLevelInventoryExecution>
        ExecuteAsync(
            InspectionWorkspace workspace,
            WorkspaceTopLevelInventoryRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);

        ArtifactRootResult<WorkspaceRealizationOperationSnapshot> captured =
            await workspace.CaptureRealizationOperationSnapshotAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        if (captured
            is not ArtifactRootResult<WorkspaceRealizationOperationSnapshot>
                .Available available)
        {
            return Complete(
                WorkspaceTopLevelInventoryQuery.Unavailable(
                    WorkspaceTopLevelInventoryUnavailableReason
                        .InvalidAuthority),
                request,
                shareBasis: null);
        }

        return Execute(
            available.Value.Definition,
            available.Value.Scope,
            request,
            WorkspaceTopLevelInventoryShareBasis.CreateRealizedWorkspace(
                available.Value.Definition));
    }

    public static async ValueTask<WorkspaceTopLevelInventoryExecution>
        ExecuteAsync(
            InspectionWorkspace workspace,
            WorkspaceTopLevelInventoryRequest request,
            WorkspaceTopLevelInventoryShareBasis shareBasis,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(shareBasis);

        ArtifactRootResult<WorkspaceRealizationOperationSnapshot> captured =
            await workspace.CaptureRealizationOperationSnapshotAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        if (captured
            is not ArtifactRootResult<WorkspaceRealizationOperationSnapshot>
                .Available available)
        {
            return Complete(
                WorkspaceTopLevelInventoryQuery.Unavailable(
                    WorkspaceTopLevelInventoryUnavailableReason
                        .InvalidAuthority),
                request,
                shareBasis);
        }

        return Execute(
            available.Value.Definition,
            available.Value.Scope,
            request,
            shareBasis);
    }

    public static WorkspaceTopLevelInventoryExecution Execute(
        WorkspaceRealizationOperationLease authority,
        WorkspaceTopLevelInventoryRequest request,
        WorkspaceTopLevelInventoryShareBasis shareBasis)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(shareBasis);

        using WorkspaceRealizationOperationUse use = authority.EnterUse();
        return Execute(
            use.Definition,
            use.Scope,
            request,
            shareBasis);
    }

    static WorkspaceTopLevelInventoryExecution Execute(
        WorkspaceDefinitionSnapshot definition,
        WorkspaceScopeSnapshot scope,
        WorkspaceTopLevelInventoryRequest request,
        WorkspaceTopLevelInventoryShareBasis shareBasis)
    {
        if (!WorkspaceTopLevelInventoryQuery.IsValidAuthority(
            definition,
            scope))
        {
            return Complete(
                WorkspaceTopLevelInventoryQuery.Unavailable(
                    WorkspaceTopLevelInventoryUnavailableReason
                        .InvalidAuthority),
                request,
                shareBasis);
        }

        if (!shareBasis.Matches(definition))
        {
            return Complete(
                WorkspaceTopLevelInventoryQuery.Unavailable(
                    WorkspaceTopLevelInventoryUnavailableReason
                        .InvalidShareBasis),
                request,
                shareBasis);
        }

        return Complete(
            WorkspaceTopLevelInventoryQuery.Execute(
                definition,
                scope,
                request),
            request,
            shareBasis);
    }

    static WorkspaceTopLevelInventoryExecution Complete(
        WorkspaceTopLevelInventoryQueryExecution execution,
        WorkspaceTopLevelInventoryRequest request,
        WorkspaceTopLevelInventoryShareBasis? shareBasis)
    {
        var inspection =
            new InspectionEnvelope<WorkspaceTopLevelInventoryOutcome>(
                InspectionContentKind.Outcome,
                execution.Outcome,
                ProjectPortableProjection(
                    execution.Outcome,
                    request,
                    shareBasis),
                ProjectDiagnostics(execution.Outcome));
        return new(inspection, execution.Selection);
    }

    static InspectionPortableProjection ProjectPortableProjection(
        WorkspaceTopLevelInventoryOutcome outcome,
        WorkspaceTopLevelInventoryRequest request,
        WorkspaceTopLevelInventoryShareBasis? shareBasis)
    {
        if (outcome
            is WorkspaceTopLevelInventoryOutcome.Unavailable unavailable)
        {
            (
                InspectionPortableProjectionFailureReason reason,
                string explanation) = unavailable.Reason switch
                {
                    WorkspaceTopLevelInventoryUnavailableReason
                        .InvalidAuthority =>
                        (
                            InspectionPortableProjectionFailureReason.Invalid,
                            "The admitted Workspace definition and Scope do not share one exact revision basis."),
                    WorkspaceTopLevelInventoryUnavailableReason
                        .InvalidShareBasis =>
                        (
                            InspectionPortableProjectionFailureReason.Invalid,
                            "The retained Share basis does not name the admitted Workspace revisions."),
                    _ => throw new InvalidOperationException(
                        "Unknown Workspace inventory unavailable reason."),
                };
            return new InspectionPortableProjection.NonProjectable(
                SharePath,
                reason,
                explanation);
        }

        if (outcome is WorkspaceTopLevelInventoryOutcome.Rejected)
        {
            return new InspectionPortableProjection.NonProjectable(
                SharePath,
                InspectionPortableProjectionFailureReason.Invalid,
                "The Workspace inventory request contains an invalid kind filter.");
        }

        if (request.Filter is not null)
        {
            return new InspectionPortableProjection.NonProjectable(
                SharePath,
                InspectionPortableProjectionFailureReason.NotSupported,
                "Workspace packets do not represent inventory kind filters.");
        }

        return (shareBasis
            ?? throw new InvalidOperationException(
                "Available Workspace inventory requires one Share basis."))
            .Projection switch
        {
            WorkspaceTopLevelInventoryShareProjection.Projectable projected =>
                new InspectionPortableProjection.Available(
                    $"https://dotnet-inspect.net/?w={projected.CanonicalPacket}",
                    projected.CanonicalPacket),
            WorkspaceTopLevelInventoryShareProjection.NonProjectable
                nonProjectable =>
                new InspectionPortableProjection.NonProjectable(
                    SharePath,
                    nonProjectable.Reason switch
                    {
                        WorkspaceTopLevelInventoryShareNonProjectableReason
                            .NoRetainedDefinitionProjection =>
                            InspectionPortableProjectionFailureReason
                                .Unavailable,
                        WorkspaceTopLevelInventoryShareNonProjectableReason
                            .DefinitionsProjectionUnavailable =>
                            InspectionPortableProjectionFailureReason.Failed,
                        _ => throw new InvalidOperationException(
                            "Unknown Workspace inventory Share projection reason."),
                    },
                    nonProjectable.Reason switch
                    {
                        WorkspaceTopLevelInventoryShareNonProjectableReason
                            .NoRetainedDefinitionProjection =>
                            "The realized Workspace has no retained Definitions-owned projection.",
                        WorkspaceTopLevelInventoryShareNonProjectableReason
                            .DefinitionsProjectionUnavailable =>
                            nonProjectable.Detail
                            ?? "Workspace Definitions could not project the restored state.",
                        _ => throw new InvalidOperationException(
                            "Unknown Workspace inventory Share projection reason."),
                    }),
            _ => throw new InvalidOperationException(
                "Unknown Workspace inventory Share projection."),
        };
    }

    static ImmutableArray<InspectionDiagnostic> ProjectDiagnostics(
        WorkspaceTopLevelInventoryOutcome outcome) =>
        outcome switch
        {
            WorkspaceTopLevelInventoryOutcome.Available => [],
            WorkspaceTopLevelInventoryOutcome.Rejected =>
            [
                new(
                    "workspace-top-level-inventory.invalid-filter",
                    InspectionDiagnosticSeverity.Error,
                    "The Workspace inventory kind filter is empty or contains an unknown kind."),
            ],
            WorkspaceTopLevelInventoryOutcome.Unavailable unavailable =>
            [
                new(
                    unavailable.Reason switch
                    {
                        WorkspaceTopLevelInventoryUnavailableReason
                            .InvalidAuthority =>
                            "workspace-top-level-inventory.invalid-authority",
                        WorkspaceTopLevelInventoryUnavailableReason
                            .InvalidShareBasis =>
                            "workspace-top-level-inventory.invalid-share-basis",
                        _ => throw new InvalidOperationException(
                            "Unknown Workspace inventory unavailable reason."),
                    },
                    InspectionDiagnosticSeverity.Error,
                    unavailable.Reason switch
                    {
                        WorkspaceTopLevelInventoryUnavailableReason
                            .InvalidAuthority =>
                            "The admitted Workspace definition and Scope observation are inconsistent.",
                        WorkspaceTopLevelInventoryUnavailableReason
                            .InvalidShareBasis =>
                            "The Workspace inventory Share basis names different revisions.",
                        _ => throw new InvalidOperationException(
                            "Unknown Workspace inventory unavailable reason."),
                    }),
            ],
            _ => throw new InvalidOperationException(
                "Unknown Workspace inventory outcome."),
        };
}
