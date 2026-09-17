using System.Collections.Immutable;

using DotnetInspector.Queries;

namespace DotnetInspector.PackageQueries;

static class PackageVersionCellWorkspaceCleanup
{
    internal static PackageVersionCellWorkspaceCleanupEvidence Describe(
        InspectionWorkspaceCloseReport? report,
        bool scopeCommitted,
        bool closeFaulted)
    {
        var failures = ImmutableArray.CreateBuilder<
            PackageVersionCellWorkspaceCleanupFailure>();
        if (report is not null)
        {
            if (!scopeCommitted && !report.Groups.IsEmpty)
            {
                Add(
                    PackageVersionCellWorkspaceCleanupStage
                        .CloseReportContract,
                    report.Groups.Length);
            }
            else if (scopeCommitted)
            {
                Add(
                    PackageVersionCellWorkspaceCleanupStage
                        .CloseReportContract,
                    report.Groups.Count(group =>
                        group is not
                            InspectionWorkspaceDirectGroupCloseResult));
                Add(
                    PackageVersionCellWorkspaceCleanupStage.GroupRelease,
                    report.Groups
                        .OfType<
                            InspectionWorkspaceDirectGroupCloseResult>()
                        .Count(group => !group.Succeeded));
            }
            Add(
                PackageVersionCellWorkspaceCleanupStage.ArtifactRelease,
                report.ArtifactSessionCleanupFailures.Length);
        }
        if (closeFaulted)
        {
            Add(
                PackageVersionCellWorkspaceCleanupStage.CloseOrchestration,
                1);
        }
        return new(failures.ToImmutable());

        void Add(
            PackageVersionCellWorkspaceCleanupStage stage,
            int count)
        {
            if (count > 0)
            {
                failures.Add(
                    new PackageVersionCellWorkspaceCleanupFailure(
                        stage,
                        count));
            }
        }
    }
}
