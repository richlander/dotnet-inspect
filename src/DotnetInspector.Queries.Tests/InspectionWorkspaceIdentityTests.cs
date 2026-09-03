using DotnetInspector.Queries.Definitions;

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
    public void EqualPortableContextAddresses_DoNotEstablishWorkspaceIdentity()
    {
        var firstAddress =
            new WorkspaceContextAddress("workspace", "context");
        var secondAddress =
            new WorkspaceContextAddress("workspace", "context");
        using var first = new InspectionWorkspace();
        using var second = new InspectionWorkspace();

        Assert.Equal(firstAddress, secondAddress);
        Assert.NotSame(first.Identity, second.Identity);
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
}
