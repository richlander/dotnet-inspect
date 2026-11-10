using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;

using DotnetInspector.Libraries;
using DotnetInspector.PackageQueries;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.ResearchQueries;
using ILInspector.Metadata;
using Inspector.Artifacts;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlatformProjection_CoalescesOnlyGraphInputsAndPrefersFocus(
        bool supportFirst)
    {
        await using var workspace = new InspectionWorkspace();
        object authority = new();
        PackageDependencyMemberCallGraphPlatformLibrary focus =
            await AdmitPlatformAsync(workspace, authority);
        PackageDependencyMemberCallGraphPlatformLibrary support =
            await AdmitPlatformAsync(
                workspace,
                authority,
                support: true);

        ImmutableArray<PackageAssemblyContextPlatformLibrary> projected =
            new PackageDependencyMemberCallGraphPlatformProjection().Project(
                supportFirst ? [support, focus] : [focus, support]);

        PackageAssemblyContextPlatformLibrary selected =
            Assert.Single(projected);
        Assert.Same(focus.Library, selected.Library);
        Assert.Equal(focus.Member.Target, selected.Target);
        Assert.Equal(PlatformFamily.DotNetRuntime, selected.Target.Family);
        Assert.False(selected.DefinesCoreLibraryRoot);
        Assert.Equal(
            PlatformPopulationMemberRole.BindingSupport,
            support.Member.Role);
        foreach (var library in new[] { focus, support })
        {
            var issued =
                Assert.IsType<WorkspaceLibraryOperationIssueOutcome.Issued>(
                    workspace.IssueLibraryOperation(library.Library));
            using LibraryOperationLease operation = issued.Lease;
            Assert.Same(library.Library.Library, operation.Reference);
        }

        var binding =
            PackageAssemblyContextCompletionTests.SharedBinding("Graph.Root");
        PackageAssemblyContextCompletionOperation preparation =
            workspace.PreparePackageAssemblyContextCompletion(
                [binding],
                [],
                projected,
                options: null);
        PackageAssemblyContextCompletion completion =
            await preparation.ExecuteAsync(preparation.Identity);
        Assert.All(
            (await completion.CloseAsync()).Groups,
            group => Assert.IsType<PackageRoleGroupCleanupRecord.Released>(
                group));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PlatformProjection_PreservesRealAndUncertifiedAmbiguity(
        int difference)
    {
        await using var workspace = new InspectionWorkspace();
        object authority = new();
        PackageDependencyMemberCallGraphPlatformLibrary first =
            await AdmitPlatformAsync(
                workspace,
                authority,
                certified: difference != 2);
        PackageDependencyMemberCallGraphPlatformLibrary second =
            await AdmitPlatformAsync(
                workspace,
                difference == 1 ? new object() : authority,
                support: true,
                imageRevision: difference == 0 ? 1 : 0,
                certified: difference != 2);
        ImmutableArray<PackageAssemblyContextPlatformLibrary> projected =
            new PackageDependencyMemberCallGraphPlatformProjection()
                .Project([first, second]);
        Assert.Equal(2, projected.Length);

        var binding =
            PackageAssemblyContextCompletionTests.SharedBinding("Graph.Root");
        PackageAssemblyContextCompletionOperation preparation =
            workspace.PreparePackageAssemblyContextCompletion(
                [binding],
                [],
                projected,
                options: null);
        PackageAssemblyRoleCorrespondenceException error =
            await Assert.ThrowsAsync<PackageAssemblyRoleCorrespondenceException>(
                async () =>
                    await preparation.ExecuteAsync(preparation.Identity));
        Assert.Contains(
            "more than one assembly with the same assembly identity",
            error.Message);
    }

    [Fact]
    public async Task PlatformProjection_PreservesExplicitCoreLibraryAcrossGenerations()
    {
        object authority = new();
        var projection =
            new PackageDependencyMemberCallGraphPlatformProjection();
        await using (var predecessor = new InspectionWorkspace())
        {
            var library = await AdmitPlatformAsync(predecessor, authority);
            Assert.False(
                Assert.Single(projection.Project([library]))
                    .DefinesCoreLibraryRoot);
            var certified =
                new PackageDependencyMemberCallGraphPlatformLibrary(
                    library.Member,
                    library.Library,
                    definesCoreLibraryRoot: true);
            Assert.True(
                Assert.Single(projection.Project([certified]))
                    .DefinesCoreLibraryRoot);
        }

        await using var successor = new InspectionWorkspace();
        var support = await AdmitPlatformAsync(
            successor,
            authority,
            support: true);
        var focus = await AdmitPlatformAsync(successor, authority);
        PackageAssemblyContextPlatformLibrary selected =
            Assert.Single(projection.Project([support, focus]));
        Assert.Same(focus.Library, selected.Library);
        Assert.True(selected.DefinesCoreLibraryRoot);

        var changed = await AdmitPlatformAsync(
            successor,
            authority,
            imageRevision: 1);
        Assert.False(
            Assert.Single(projection.Project([changed]))
                .DefinesCoreLibraryRoot);
        Assert.False(
            Assert.Single(
                new PackageDependencyMemberCallGraphPlatformProjection()
                    .Project([focus]))
                .DefinesCoreLibraryRoot);
    }

    static async Task<PackageDependencyMemberCallGraphPlatformLibrary>
        AdmitPlatformAsync(
            InspectionWorkspace workspace,
            object authority,
            bool support = false,
            int imageRevision = 0,
            bool certified = true)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        var actualTarget = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        var requestedTarget = support
            ? new PlatformFamilyTarget(
                PlatformFamily.AspNetCore,
                actualTarget.TargetFramework,
                actualTarget.Version)
            : actualTarget;
        var capability = PlatformSourceCapabilityIdentity.Create("graph-test");
        var population = new PlatformPopulationDemand.CompletePopulation();
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("graph-test"),
            new PlatformTargetDemand.Exact(requestedTarget),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("graph-test")),
            new PlatformHouseOperation.Realize(
                population,
                PlatformViewDemand.Implementation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("graph-test"),
                PlatformSourcePolicyGeneration.Create("graph-test"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Precedence,
                        [capability]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 1,
                maxTargetCandidates: 1,
                maxAssemblies: 1,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 16 * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            token);
        byte[] image = File.ReadAllBytes(
            typeof(PackageDependencyMemberCallGraphOperation).Assembly.Location);
        if (imageRevision != 0)
            image = [.. image, (byte)imageRevision];
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                pe.GetMetadataReader());
        var contribution = new PlatformSourceContribution.Realization(
            PlatformSourceFacet.Implementation,
            capability,
            request.Snapshot,
            PlatformSourceGeneration.Create("graph-test"),
            requestedTarget,
            PlatformSourceCoordinateIdentity.Create("graph-test"),
            population,
            PlatformSourceContributionCompleteness.Authoritative);
        var materialized =
            Assert.IsType<
                PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await PlatformHousePopulationArtifactMaterializer
                    .MaterializeImplementationsAsync(
                        request,
                        [
                            new PlatformPopulationLibraryArtifactMaterializationItem(
                                new PlatformLibraryArtifactMaterializationItem(
                                    contribution,
                                    new GraphTestProvenance(),
                                    identity,
                                    image.Length,
                                    _ => new MemoryStream(image, writable: false),
                                    implementationArtifactIdentity: certified
                                        ? new GraphTestImplementationIdentity(
                                            identity,
                                            authority,
                                            imageRevision)
                                        : null),
                                new PlatformPopulationMemberAttribution(
                                    actualTarget,
                                    support
                                        ? PlatformPopulationMemberRole.BindingSupport
                                        : PlatformPopulationMemberRole.Focus)),
                        ],
                        new PlatformHouseConsumedWork(
                            sourceOperations: 1,
                            targetCandidates: 0,
                            assemblies: 1,
                            xmlDocuments: 0,
                            portablePdbs: 0,
                            sourceDocuments: 0,
                            bytes: image.Length,
                            forwardingHops: 0,
                            targetComparisons: 0,
                            elapsed: TimeSpan.Zero),
                        "graph-test"));
        var registrations =
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                workspace.GetRegistrationSnapshot());
        var admitted =
            Assert.IsType<WorkspaceLibraryAdmissionOutcome.Accepted>(
                await workspace.AdmitLibraryBatchAsync(
                    registrations.Revision,
                    materialized.Artifacts,
                    materialized.Population.Owners));
        return new(
            Assert.Single(materialized.Population.Value.Members),
            Assert.Single(admitted.Receipt.Occurrences));
    }

    sealed record GraphTestProvenance : IArtifactProvenance;

    sealed class GraphTestImplementationIdentity(
        AssemblyReferenceIdentity identity,
        object authority,
        int imageRevision) : PlatformImplementationArtifactIdentity(identity)
    {
        readonly object _authority = authority;
        readonly int _imageRevision = imageRevision;

        public override bool Equals(
            PlatformImplementationArtifactIdentity? other) =>
            other is GraphTestImplementationIdentity identity
            && ReferenceEquals(_authority, identity._authority)
            && _imageRevision == identity._imageRevision
            && AssemblyReferenceIdentity.EquivalentComparer.Equals(
                AssemblyIdentity,
                identity.AssemblyIdentity);

        public override int GetHashCode() =>
            HashCode.Combine(
                _authority,
                _imageRevision,
                AssemblyReferenceIdentity.EquivalentComparer.GetHashCode(
                    AssemblyIdentity));
    }
}
