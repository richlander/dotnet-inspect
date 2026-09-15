namespace DotnetInspector.Queries;

/// <summary>
/// Workspace-issued binding for one exact package Root occurrence.
/// </summary>
/// <remarks>
/// Equality is reference identity. <see cref="RootBinding"/> remains the
/// authoritative package coordinate, content-generation, and selection
/// correspondence; this occurrence does not mint a parallel package identity.
/// </remarks>
public sealed class PackageRootOccurrenceBinding :
    InspectionWorkspaceOccurrenceIdentity
{
    internal PackageRootOccurrenceBinding(
        InspectionWorkspaceIdentity workspaceIdentity,
        PackageRootBinding rootBinding)
        : base(workspaceIdentity)
    {
        RootBinding = rootBinding;
    }

    /// <summary>The exact acquisition-issued package Root binding.</summary>
    public PackageRootBinding RootBinding { get; }
}

public sealed partial class InspectionWorkspace
{
    internal PackageRootOccurrenceBinding IssuePackageRootOccurrence(
        PackageRootBinding rootBinding)
    {
        ArgumentNullException.ThrowIfNull(rootBinding);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            return new PackageRootOccurrenceBinding(
                _identity,
                rootBinding);
        }
    }
}
