namespace DotnetInspector.Queries;

public sealed partial class InspectionWorkspace
{
    WorkspaceDefinitionSnapshot? _definitionSnapshot;

    internal async ValueTask<
        ArtifactRootResult<WorkspaceRealizationOperationSnapshot>>
        CaptureRealizationOperationSnapshotAsync(
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArtifactRootResult<ArtifactRootCompositionReadLease> read =
            await ReadArtifactRootCompositionAsync(
                _identity).ConfigureAwait(false);
        if (read
            is ArtifactRootResult<ArtifactRootCompositionReadLease>
                .Rejected rejected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ArtifactRootResult<
                WorkspaceRealizationOperationSnapshot>.Rejected(
                    rejected.Failure);
        }

        using ArtifactRootCompositionReadLease lease =
            ((ArtifactRootResult<ArtifactRootCompositionReadLease>
                .Available)read).Value;
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (RootWorkspaceFailure(_identity) is { } unavailable)
            {
                return new ArtifactRootResult<
                    WorkspaceRealizationOperationSnapshot>.Rejected(
                        unavailable);
            }

            WorkspaceScopeSnapshot scope = ObserveScope(lease);
            if (_definitionSnapshot is null
                || !ReferenceEquals(
                    _definitionSnapshot.Registrations,
                    _registrationRevision)
                || !ReferenceEquals(
                    _definitionSnapshot.Scope,
                    scope.Revision))
            {
                _definitionSnapshot = new WorkspaceDefinitionSnapshot(
                    _identity,
                    _registrationRevision,
                    scope.Revision);
            }

            return new ArtifactRootResult<
                WorkspaceRealizationOperationSnapshot>.Available(
                    new(_definitionSnapshot, scope));
        }
    }
}
