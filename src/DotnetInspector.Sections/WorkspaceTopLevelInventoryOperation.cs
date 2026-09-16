using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Queries;

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
        WorkspaceTopLevelInventoryShareNonProjectableReason Reason)
        : WorkspaceTopLevelInventoryShareProjection;
}

public sealed class WorkspaceTopLevelInventoryShareBasis
{
    WorkspaceTopLevelInventoryShareBasis(
        WorkspaceDefinitionSnapshot definition,
        WorkspaceTopLevelInventoryShareRequestKind requestKind,
        WorkspaceTopLevelInventoryShareProjection projection)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!Enum.IsDefined(requestKind))
            throw new ArgumentOutOfRangeException(nameof(requestKind));

        Workspace = definition.Workspace;
        RegistrationRevision = definition.Registrations.Identity;
        ScopeRevision = definition.Scope.Identity;
        RequestKind = requestKind;
        Projection =
            projection ?? throw new ArgumentNullException(nameof(projection));
    }

    public InspectionWorkspaceIdentity Workspace { get; }

    public WorkspaceRegistrationRevisionIdentity RegistrationRevision { get; }

    public WorkspaceScopeRevisionIdentity ScopeRevision { get; }

    public WorkspaceTopLevelInventoryShareRequestKind RequestKind { get; }

    public WorkspaceTopLevelInventoryShareProjection Projection { get; }

    public static WorkspaceTopLevelInventoryShareBasis CreatePacketInput(
        WorkspaceDefinitionSnapshot definition,
        string canonicalPacket) =>
        new(
            definition,
            WorkspaceTopLevelInventoryShareRequestKind.PacketInput,
            new WorkspaceTopLevelInventoryShareProjection.Projectable(
                canonicalPacket));

    public static WorkspaceTopLevelInventoryShareBasis CreateDefinitionInput(
        WorkspaceDefinitionSnapshot definition,
        string canonicalPacket) =>
        new(
            definition,
            WorkspaceTopLevelInventoryShareRequestKind.DefinitionInput,
            new WorkspaceTopLevelInventoryShareProjection.Projectable(
                canonicalPacket));

    public static WorkspaceTopLevelInventoryShareBasis CreateRealizedWorkspace(
        WorkspaceDefinitionSnapshot definition) =>
        new(
            definition,
            WorkspaceTopLevelInventoryShareRequestKind.RealizedWorkspace,
            new WorkspaceTopLevelInventoryShareProjection.NonProjectable(
                WorkspaceTopLevelInventoryShareNonProjectableReason
                    .NoRetainedDefinitionProjection));

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

    public static WorkspaceTopLevelInventoryExecution Execute(
        WorkspaceRealizationOperationLease authority,
        WorkspaceTopLevelInventoryRequest request,
        WorkspaceTopLevelInventoryShareBasis shareBasis)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(shareBasis);

        using WorkspaceRealizationOperationUse use = authority.EnterUse();
        if (!WorkspaceTopLevelInventoryQuery.IsValidAuthority(
            use.Definition,
            use.Scope))
        {
            return Complete(
                WorkspaceTopLevelInventoryQuery.Unavailable(
                    WorkspaceTopLevelInventoryUnavailableReason
                        .InvalidAuthority),
                request,
                shareBasis);
        }

        if (!shareBasis.Matches(use.Definition))
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
                use.Definition,
                use.Scope,
                request),
            request,
            shareBasis);
    }

    static WorkspaceTopLevelInventoryExecution Complete(
        WorkspaceTopLevelInventoryQueryExecution execution,
        WorkspaceTopLevelInventoryRequest request,
        WorkspaceTopLevelInventoryShareBasis shareBasis)
    {
        var inspection =
            new InspectionEnvelope<WorkspaceTopLevelInventoryOutcome>(
                execution.Outcome,
                ProjectShare(execution.Outcome, request, shareBasis),
                ProjectDiagnostics(execution.Outcome));
        return new(inspection, execution.Selection);
    }

    static InspectionShare ProjectShare(
        WorkspaceTopLevelInventoryOutcome outcome,
        WorkspaceTopLevelInventoryRequest request,
        WorkspaceTopLevelInventoryShareBasis shareBasis)
    {
        if (outcome
            is WorkspaceTopLevelInventoryOutcome.Unavailable unavailable)
        {
            return new InspectionShare.NonProjectable(
                SharePath,
                unavailable.Reason switch
                {
                    WorkspaceTopLevelInventoryUnavailableReason
                        .InvalidAuthority =>
                        "The admitted Workspace definition and Scope do not share one exact revision basis.",
                    WorkspaceTopLevelInventoryUnavailableReason
                        .InvalidShareBasis =>
                        "The retained Share basis does not name the admitted Workspace revisions.",
                    _ => throw new InvalidOperationException(
                        "Unknown Workspace inventory unavailable reason."),
                });
        }

        if (outcome is WorkspaceTopLevelInventoryOutcome.Rejected)
        {
            return new InspectionShare.NonProjectable(
                SharePath,
                "The Workspace inventory request contains an invalid kind filter.");
        }

        if (request.Filter is not null)
        {
            return new InspectionShare.NonProjectable(
                SharePath,
                "Workspace packets do not represent inventory kind filters.");
        }

        return shareBasis.Projection switch
        {
            WorkspaceTopLevelInventoryShareProjection.Projectable projected =>
                new InspectionShare.Available(
                    $"https://dotnet-inspect.net/?w={projected.CanonicalPacket}",
                    projected.CanonicalPacket),
            WorkspaceTopLevelInventoryShareProjection.NonProjectable
                nonProjectable =>
                new InspectionShare.NonProjectable(
                    SharePath,
                    nonProjectable.Reason switch
                    {
                        WorkspaceTopLevelInventoryShareNonProjectableReason
                            .NoRetainedDefinitionProjection =>
                            "The realized Workspace has no retained Definitions-owned projection.",
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
