namespace DotnetInspector.Queries;

internal static class WorkspaceLogicalScopeCorrespondence
{
    internal static bool Matches(
        WorkspaceScopeRevision predecessor,
        WorkspaceScopeRevision successor)
    {
        ArgumentNullException.ThrowIfNull(predecessor);
        ArgumentNullException.ThrowIfNull(successor);
        if (predecessor.Packages.Length != successor.Packages.Length)
            return false;

        for (int index = 0; index < predecessor.Packages.Length; index++)
        {
            WorkspacePackageOccurrence first = predecessor.Packages[index];
            WorkspacePackageOccurrence second = successor.Packages[index];
            WorkspacePackageDescriptor firstPackage = first.Package;
            WorkspacePackageDescriptor secondPackage = second.Package;
            if (firstPackage.Coordinate != secondPackage.Coordinate
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    firstPackage.PackageId,
                    secondPackage.PackageId)
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    firstPackage.PackageVersion,
                    secondPackage.PackageVersion)
                || !string.Equals(
                    firstPackage.TargetFramework,
                    secondPackage.TargetFramework,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    firstPackage.RequestedTargetFramework,
                    secondPackage.RequestedTargetFramework,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    firstPackage.SelectedTargetFramework,
                    secondPackage.SelectedTargetFramework,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    firstPackage.RuntimeIdentifier,
                    secondPackage.RuntimeIdentifier,
                    StringComparison.OrdinalIgnoreCase)
                || firstPackage.SelectionStatus
                    != secondPackage.SelectionStatus
                || ReferenceEquals(first.Identity, second.Identity))
            {
                return false;
            }
        }

        return true;
    }
}
