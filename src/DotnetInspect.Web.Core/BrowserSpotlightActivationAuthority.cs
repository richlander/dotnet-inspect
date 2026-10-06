using DotnetInspector.Queries;

namespace DotnetInspect.Web;

internal enum BrowserSpotlightActivationStaleReason
{
    WorkspaceIdentity,
    RegistrationRevision,
    ScopeRevision,
    ScopePublicationBase,
    PackageOccurrence,
}

internal abstract record BrowserSpotlightActivationBlock
{
    private protected BrowserSpotlightActivationBlock()
    {
    }

    internal sealed record Stale(
        BrowserSpotlightActivationStaleReason Reason,
        WorkspaceScopeSnapshot? CurrentScope,
        WorkspaceRegistrationRevision? CurrentRegistrations) :
        BrowserSpotlightActivationBlock;

    internal sealed record RegistrationUnavailable(
        WorkspaceRegistrationReadResult.Unavailable Result) :
        BrowserSpotlightActivationBlock;

    internal sealed record ScopeUnavailable(
        WorkspaceScopeReadResult.Unavailable Result,
        WorkspaceRegistrationRevision CurrentRegistrations) :
        BrowserSpotlightActivationBlock;
}

internal sealed record BrowserSpotlightActivationBasisRead(
    WorkspaceScopeSnapshot? Scope = null,
    WorkspaceRegistrationRevision? Registrations = null,
    BrowserSpotlightActivationBlock? Block = null);

internal static class BrowserSpotlightActivationAuthority
{
    internal static async ValueTask<BrowserSpotlightActivationBasisRead>
        ReadAsync(
            InspectionWorkspace workspace,
            BrowserSpotlightActivationBasis expected)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(expected);

        if (!ReferenceEquals(
                workspace.Identity,
                expected.Scope.Revision.Workspace)
            || !ReferenceEquals(
                workspace.Identity,
                expected.Registrations.Workspace))
        {
            return new(
                Block: new BrowserSpotlightActivationBlock.Stale(
                    BrowserSpotlightActivationStaleReason
                        .WorkspaceIdentity,
                    CurrentScope: null,
                    CurrentRegistrations: null));
        }

        WorkspaceScopeReadResult scopeRead =
            await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);

        WorkspaceRegistrationReadResult registrationRead =
            workspace.GetRegistrationSnapshot();
        if (registrationRead
            is WorkspaceRegistrationReadResult.Unavailable
                registrationUnavailable)
        {
            return new(
                Block:
                    new BrowserSpotlightActivationBlock
                        .RegistrationUnavailable(
                            registrationUnavailable));
        }

        WorkspaceRegistrationRevision registrations =
            ((WorkspaceRegistrationReadResult.Available)registrationRead)
                .Revision;
        if (!ReferenceEquals(
                registrations.Identity,
                expected.Registrations.Identity))
        {
            return new(
                Block: new BrowserSpotlightActivationBlock.Stale(
                    BrowserSpotlightActivationStaleReason
                        .RegistrationRevision,
                    CurrentScope: null,
                    CurrentRegistrations: registrations));
        }

        if (scopeRead
            is WorkspaceScopeReadResult.Unavailable scopeUnavailable)
        {
            return new(
                Block:
                    new BrowserSpotlightActivationBlock
                        .ScopeUnavailable(
                            scopeUnavailable,
                            registrations));
        }

        WorkspaceScopeSnapshot scope =
            ((WorkspaceScopeReadResult.Available)scopeRead).Snapshot;
        if (!ReferenceEquals(
                scope.Revision.Identity,
                expected.Scope.Revision.Identity))
        {
            return new(
                Block: new BrowserSpotlightActivationBlock.Stale(
                    BrowserSpotlightActivationStaleReason
                        .ScopeRevision,
                    scope,
                    registrations));
        }
        if (!ReferenceEquals(
                scope.PublicationBase,
                expected.Scope.PublicationBase))
        {
            return new(
                Block: new BrowserSpotlightActivationBlock.Stale(
                    BrowserSpotlightActivationStaleReason
                        .ScopePublicationBase,
                    scope,
                    registrations));
        }

        return new(scope, registrations, Block: null);
    }
}
