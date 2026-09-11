namespace DotnetInspector.Queries.Tests;

public sealed class InspectionWorkspaceIdentityTests
{
    [Fact]
    public void WorkspaceIdentity_IsStableAndExactPerInstance()
    {
        using var first = new InspectionWorkspace();
        using var second = new InspectionWorkspace();

        InspectionWorkspaceIdentity identity = first.Identity;

        Assert.Same(identity, first.Identity);
        Assert.NotSame(identity, second.Identity);
    }

    [Fact]
    public void PackageOccurrence_IsExactPerIssuanceAndCarriesBinding()
    {
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Repeated.Occurrence");
        using var workspace = new InspectionWorkspace();

        PackageRootOccurrenceBinding first =
            workspace.IssuePackageRootOccurrence(binding);
        PackageRootOccurrenceBinding second =
            workspace.IssuePackageRootOccurrence(binding);

        Assert.NotSame(first, second);
        Assert.NotEqual(first, second);
        Assert.Same(workspace.Identity, first.WorkspaceIdentity);
        Assert.Same(workspace.Identity, second.WorkspaceIdentity);
        Assert.Same(binding, first.RootBinding);
        Assert.Same(binding, second.RootBinding);
    }

    [Fact]
    public void PackageOccurrence_DistinguishesWorkspaceAndBindingGeneration()
    {
        PackageRootBinding firstBinding =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Replacement.Generation");
        PackageRootBinding replacementBinding =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Replacement.Generation");
        using var firstWorkspace = new InspectionWorkspace();
        using var secondWorkspace = new InspectionWorkspace();

        PackageRootOccurrenceBinding first =
            firstWorkspace.IssuePackageRootOccurrence(firstBinding);
        PackageRootOccurrenceBinding replacement =
            firstWorkspace.IssuePackageRootOccurrence(replacementBinding);
        PackageRootOccurrenceBinding otherWorkspace =
            secondWorkspace.IssuePackageRootOccurrence(firstBinding);

        Assert.Equal(firstBinding.Coordinate, replacementBinding.Coordinate);
        Assert.NotSame(
            firstBinding.ContentGenerationIdentity,
            replacementBinding.ContentGenerationIdentity);
        Assert.NotSame(
            firstBinding.SelectionIdentity,
            replacementBinding.SelectionIdentity);
        Assert.NotSame(first, replacement);
        Assert.NotSame(first.WorkspaceIdentity, otherWorkspace.WorkspaceIdentity);
        Assert.Same(firstBinding, first.RootBinding);
        Assert.Same(replacementBinding, replacement.RootBinding);
    }

    [Fact]
    public void NonPackageOccurrence_IsExactAndWorkspaceScoped()
    {
        using var firstWorkspace = new InspectionWorkspace();
        using var secondWorkspace = new InspectionWorkspace();

        NonPackageRootOccurrenceIdentity first =
            firstWorkspace.IssueNonPackageRootOccurrence();
        NonPackageRootOccurrenceIdentity second =
            firstWorkspace.IssueNonPackageRootOccurrence();
        NonPackageRootOccurrenceIdentity otherWorkspace =
            secondWorkspace.IssueNonPackageRootOccurrence();

        Assert.NotSame(first, second);
        Assert.NotEqual(first, second);
        Assert.Same(firstWorkspace.Identity, first.WorkspaceIdentity);
        Assert.Same(firstWorkspace.Identity, second.WorkspaceIdentity);
        Assert.Same(secondWorkspace.Identity, otherWorkspace.WorkspaceIdentity);
        Assert.NotSame(first.WorkspaceIdentity, otherWorkspace.WorkspaceIdentity);
    }

    [Fact]
    public void SynchronousClose_StopsOccurrenceIssuanceButKeepsIdentity()
    {
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Synchronous.Close");
        var workspace = new InspectionWorkspace();
        InspectionWorkspaceIdentity identity = workspace.Identity;
        PackageRootOccurrenceBinding package =
            workspace.IssuePackageRootOccurrence(binding);
        NonPackageRootOccurrenceIdentity root =
            workspace.IssueNonPackageRootOccurrence();

        workspace.Dispose();

        Assert.Same(identity, workspace.Identity);
        Assert.Same(identity, package.WorkspaceIdentity);
        Assert.Same(identity, root.WorkspaceIdentity);
        Assert.Throws<ObjectDisposedException>(
            () => workspace.IssuePackageRootOccurrence(binding));
        Assert.Throws<ObjectDisposedException>(
            () => workspace.IssueNonPackageRootOccurrence());
    }

    [Fact]
    public async Task AsynchronousClose_StopsOccurrenceIssuanceImmediately()
    {
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Asynchronous.Close");
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        InspectionWorkspaceIdentity identity = workspace.Identity;
        PackageRootOccurrenceBinding package =
            workspace.IssuePackageRootOccurrence(binding);
        NonPackageRootOccurrenceIdentity root =
            workspace.IssueNonPackageRootOccurrence();

        Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();

        Assert.Same(identity, workspace.Identity);
        Assert.Same(identity, package.WorkspaceIdentity);
        Assert.Same(identity, root.WorkspaceIdentity);
        Assert.Throws<ObjectDisposedException>(
            () => workspace.IssuePackageRootOccurrence(binding));
        Assert.Throws<ObjectDisposedException>(
            () => workspace.IssueNonPackageRootOccurrence());
        await close;
    }

    [Fact]
    public void PackageOccurrenceView_PreservesOrderAndBindingFacts()
    {
        PackageRootBinding first =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "First.View");
        PackageRootBinding second =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Second.View");
        using var workspace = new InspectionWorkspace();

        InspectionWorkspacePackageOccurrenceView view =
            workspace.CreatePackageOccurrenceView([second, first]);

        Assert.Same(workspace.Identity, view.Workspace);
        Assert.Collection(
            view.Occurrences,
            descriptor =>
            {
                Assert.Equal("second.view", descriptor.PackageId);
                Assert.Same(second, descriptor.Occurrence.RootBinding);
                Assert.Same(
                    workspace.Identity,
                    descriptor.Occurrence.WorkspaceIdentity);
            },
            descriptor =>
            {
                Assert.Equal("first.view", descriptor.PackageId);
                Assert.Same(first, descriptor.Occurrence.RootBinding);
                Assert.Same(
                    workspace.Identity,
                    descriptor.Occurrence.WorkspaceIdentity);
            });
    }

    [Fact]
    public void PackageOccurrenceView_EmptyInputProducesTypedEmptyView()
    {
        using var workspace = new InspectionWorkspace();

        InspectionWorkspacePackageOccurrenceView view =
            workspace.CreatePackageOccurrenceView([]);

        Assert.Same(workspace.Identity, view.Workspace);
        Assert.Empty(view.Occurrences);
    }

    [Fact]
    public void PackageOccurrenceView_RepeatedBindingIssuesDistinctOccurrences()
    {
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Repeated.View");
        using var workspace = new InspectionWorkspace();

        InspectionWorkspacePackageOccurrenceView view =
            workspace.CreatePackageOccurrenceView([binding, binding]);

        Assert.Equal(2, view.Occurrences.Length);
        Assert.NotSame(
            view.Occurrences[0].Occurrence,
            view.Occurrences[1].Occurrence);
        Assert.Same(
            binding,
            view.Occurrences[0].Occurrence.RootBinding);
        Assert.Same(
            binding,
            view.Occurrences[1].Occurrence.RootBinding);
    }

    [Fact]
    public void PackageOccurrenceView_ActivationResolvesOnlyItsOwnAction()
    {
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Activation.View");
        using var workspace = new InspectionWorkspace();
        InspectionWorkspacePackageOccurrenceView first =
            workspace.CreatePackageOccurrenceView([binding]);
        InspectionWorkspacePackageOccurrenceView second =
            workspace.CreatePackageOccurrenceView([binding]);

        var activated = Assert.IsType<
            InspectionWorkspacePackageOccurrenceActivation.Activated>(
                first.Activate(first.Occurrences[0].Action));
        var rejected = Assert.IsType<
            InspectionWorkspacePackageOccurrenceActivation.Rejected>(
                first.Activate(second.Occurrences[0].Action));

        Assert.Same(first.Occurrences[0].Occurrence, activated.Occurrence);
        Assert.Equal(
            InspectionWorkspacePackageOccurrenceActivationRejection
                .ViewMismatch,
            rejected.Reason);
    }

    [Fact]
    public void PackageOccurrenceView_ActivationRejectsClosedWorkspace()
    {
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Closed.View");
        var workspace = new InspectionWorkspace();
        InspectionWorkspacePackageOccurrenceView view =
            workspace.CreatePackageOccurrenceView([binding]);

        workspace.Dispose();

        var rejected = Assert.IsType<
            InspectionWorkspacePackageOccurrenceActivation.Rejected>(
                view.Activate(view.Occurrences[0].Action));
        Assert.Equal(
            InspectionWorkspacePackageOccurrenceActivationRejection
                .WorkspaceClosed,
            rejected.Reason);
    }
}
