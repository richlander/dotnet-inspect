namespace DotnetInspector.Queries;

/// <summary>
/// Opaque process-local identity for one exact inspection Workspace instance.
/// </summary>
/// <remarks>
/// Equality is reference identity. The identity remains comparable after the
/// Workspace closes, but does not by itself authorize any Workspace operation.
/// </remarks>
public sealed class InspectionWorkspaceIdentity
{
    internal InspectionWorkspaceIdentity()
    {
    }
}

/// <summary>
/// Opaque process-local identity for one coordinate occurrence issued by an
/// inspection Workspace.
/// </summary>
/// <remarks>
/// Equality is reference identity. Each issuance identifies a distinct
/// occurrence, including repeated issuance for the same root currency.
/// </remarks>
public abstract class InspectionWorkspaceOccurrenceIdentity
{
    private protected InspectionWorkspaceOccurrenceIdentity(
        InspectionWorkspaceIdentity workspaceIdentity)
    {
        WorkspaceIdentity = workspaceIdentity;
    }

    /// <summary>The exact Workspace that issued this occurrence.</summary>
    public InspectionWorkspaceIdentity WorkspaceIdentity { get; }
}

/// <summary>
/// Opaque identity for one non-package Root occurrence.
/// </summary>
public sealed class NonPackageRootOccurrenceIdentity :
    InspectionWorkspaceOccurrenceIdentity
{
    internal NonPackageRootOccurrenceIdentity(
        InspectionWorkspaceIdentity workspaceIdentity)
        : base(workspaceIdentity)
    {
    }
}

public sealed partial class InspectionWorkspace
{
    readonly InspectionWorkspaceIdentity _identity = new();

    /// <summary>
    /// Gets the stable process-local identity of this exact Workspace.
    /// </summary>
    public InspectionWorkspaceIdentity Identity => _identity;

    internal NonPackageRootOccurrenceIdentity
        IssueNonPackageRootOccurrence()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            return new NonPackageRootOccurrenceIdentity(_identity);
        }
    }
}
