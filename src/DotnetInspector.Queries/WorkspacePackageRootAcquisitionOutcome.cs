using System.Collections.Immutable;

namespace DotnetInspector.Queries;

public abstract record WorkspacePackageRootAcquisitionOutcome
{
    private protected WorkspacePackageRootAcquisitionOutcome() { }

    public sealed record Acquired(PackageRootBinding Root)
        : WorkspacePackageRootAcquisitionOutcome;

    public sealed record Failed(
        ImmutableArray<WorkspaceContextLoadFailure> Failures)
        : WorkspacePackageRootAcquisitionOutcome;
}
