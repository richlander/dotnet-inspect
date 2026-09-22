using System.Runtime.ExceptionServices;

using DotnetInspector.PackageQueries;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageDependencyMemberCallGraphOperationTests
{
    [Fact]
    public void CleanupFailurePrecedesGraphCancellation()
    {
        var operation = new PackageRoleRealizationOperationId();
        var group = new PackageRoleGroupId(operation);
        var cleanup = new PackageRoleCleanupReport(
            operation,
            [
                new PackageRoleGroupCleanupRecord.Failed(
                    group,
                    new PackageRoleGroupReleaseDiagnostic()),
            ]);
        var cancellation = new OperationCanceledException(
            "Synthetic graph cancellation.");

        PackageDependencyMemberCallGraphOutcome.Failed outcome =
            Assert.IsType<
                PackageDependencyMemberCallGraphOutcome.Failed>(
                PackageDependencyMemberCallGraphOperation
                    .SettleGraphPhase(
                        cleanup,
                        ExceptionDispatchInfo.Capture(
                            cancellation)));

        Assert.Equal(
            PackageDependencyMemberCallGraphFailureReason
                .PackageContextCleanupFailed,
            outcome.Reason);
        Assert.Same(cleanup, outcome.Cleanup);
    }

    [Fact]
    public void GraphCancellationRethrowsAfterSuccessfulCleanup()
    {
        var operation = new PackageRoleRealizationOperationId();
        var group = new PackageRoleGroupId(operation);
        var cleanup = new PackageRoleCleanupReport(
            operation,
            [new PackageRoleGroupCleanupRecord.Released(group)]);
        var cancellation = new OperationCanceledException(
            "Synthetic graph cancellation.");

        OperationCanceledException thrown =
            Assert.Throws<OperationCanceledException>(
                () =>
                    PackageDependencyMemberCallGraphOperation
                        .SettleGraphPhase(
                            cleanup,
                            ExceptionDispatchInfo.Capture(
                                cancellation)));

        Assert.Same(cancellation, thrown);
    }
}
