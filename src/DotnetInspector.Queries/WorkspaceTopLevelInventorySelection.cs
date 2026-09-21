using System.Collections.Immutable;

namespace DotnetInspector.Queries;

public abstract record WorkspaceTopLevelInventorySelection
{
    private protected WorkspaceTopLevelInventorySelection()
    {
    }

    public sealed record Package : WorkspaceTopLevelInventorySelection
    {
        internal Package(
            WorkspacePackageOccurrenceIdentity occurrence,
            int sourceIndex)
        {
            Occurrence = occurrence;
            SourceIndex = sourceIndex;
        }

        public WorkspacePackageOccurrenceIdentity Occurrence { get; }

        public int SourceIndex { get; }
    }

    public sealed record Registration : WorkspaceTopLevelInventorySelection
    {
        internal Registration(
            WorkspaceTopLevelInventoryEntryKind registrationKind,
            int sourceIndex)
        {
            if (registrationKind
                is WorkspaceTopLevelInventoryEntryKind.Package)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(registrationKind));
            }

            RegistrationKind = registrationKind;
            SourceIndex = sourceIndex;
        }

        public WorkspaceTopLevelInventoryEntryKind RegistrationKind { get; }

        public int SourceIndex { get; }
    }
}

public abstract record WorkspaceTopLevelInventorySelectionResolution
{
    private protected WorkspaceTopLevelInventorySelectionResolution()
    {
    }

    public sealed record Selected : WorkspaceTopLevelInventorySelectionResolution
    {
        internal Selected(
            WorkspaceTopLevelInventorySelection selection,
            WorkspaceScopeSnapshot scope)
        {
            Selection = selection;
            Scope = scope;
        }

        public WorkspaceTopLevelInventorySelection Selection { get; }

        public WorkspaceScopeSnapshot Scope { get; }
    }

    public sealed record Stale
        : WorkspaceTopLevelInventorySelectionResolution;

    public sealed record Absent
        : WorkspaceTopLevelInventorySelectionResolution;
}

public sealed class WorkspaceTopLevelInventorySelectionReceipt
{
    readonly ImmutableDictionary<
        WorkspaceTopLevelInventoryEntryKey,
        WorkspaceTopLevelInventorySelection> _selections;

    WorkspaceTopLevelInventorySelectionReceipt()
    {
        _selections = ImmutableDictionary<
            WorkspaceTopLevelInventoryEntryKey,
            WorkspaceTopLevelInventorySelection>.Empty;
    }

    WorkspaceTopLevelInventorySelectionReceipt(
        WorkspaceDefinitionSnapshot definition,
        ImmutableDictionary<
            WorkspaceTopLevelInventoryEntryKey,
            WorkspaceTopLevelInventorySelection> selections)
    {
        Workspace = definition.Workspace;
        RegistrationRevision = definition.Registrations.Identity;
        ScopeRevision = definition.Scope.Identity;
        _selections = selections;
    }

    internal static WorkspaceTopLevelInventorySelectionReceipt Empty { get; } =
        new();

    public InspectionWorkspaceIdentity? Workspace { get; }

    public WorkspaceRegistrationRevisionIdentity? RegistrationRevision { get; }

    public WorkspaceScopeRevisionIdentity? ScopeRevision { get; }

    public int Count => _selections.Count;

    public bool HasAuthority => Workspace is not null;

    public WorkspaceTopLevelInventorySelectionResolution Resolve(
        WorkspaceRealizationOperationLease authority,
        WorkspaceTopLevelInventoryEntryKey key)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(key);

        if (!HasAuthority)
            return new WorkspaceTopLevelInventorySelectionResolution.Absent();

        using WorkspaceRealizationOperationUse use = authority.EnterUse();
        return Resolve(use.Definition, use.Scope, key);
    }

    public async ValueTask<WorkspaceTopLevelInventorySelectionResolution>
        ResolveAsync(
            InspectionWorkspace workspace,
            WorkspaceTopLevelInventoryEntryKey key,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(key);

        if (!HasAuthority)
            return new WorkspaceTopLevelInventorySelectionResolution.Absent();

        ArtifactRootResult<WorkspaceRealizationOperationSnapshot> captured =
            await workspace.CaptureRealizationOperationSnapshotAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        if (captured
            is not ArtifactRootResult<WorkspaceRealizationOperationSnapshot>
                .Available available)
        {
            return new WorkspaceTopLevelInventorySelectionResolution.Stale();
        }

        return Resolve(
            available.Value.Definition,
            available.Value.Scope,
            key);
    }

    WorkspaceTopLevelInventorySelectionResolution Resolve(
        WorkspaceDefinitionSnapshot definition,
        WorkspaceScopeSnapshot scope,
        WorkspaceTopLevelInventoryEntryKey key)
    {
        if (!Matches(definition, scope))
            return new WorkspaceTopLevelInventorySelectionResolution.Stale();

        return _selections.TryGetValue(key, out var selection)
            ? new WorkspaceTopLevelInventorySelectionResolution.Selected(
                selection,
                scope)
            : new WorkspaceTopLevelInventorySelectionResolution.Absent();
    }

    internal static WorkspaceTopLevelInventorySelectionReceipt Create(
        WorkspaceDefinitionSnapshot definition,
        ImmutableDictionary<
            WorkspaceTopLevelInventoryEntryKey,
            WorkspaceTopLevelInventorySelection> selections) =>
        new(definition, selections);

    bool Matches(
        WorkspaceDefinitionSnapshot definition,
        WorkspaceScopeSnapshot scope) =>
        WorkspaceTopLevelInventoryQuery.IsValidAuthority(definition, scope)
        && ReferenceEquals(Workspace, definition.Workspace)
        && ReferenceEquals(
            RegistrationRevision,
            definition.Registrations.Identity)
        && ReferenceEquals(ScopeRevision, definition.Scope.Identity);
}
