using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Presentation.Tests;

public sealed class AssemblyPairDirectUseClusterPresentationTests
{
    [Fact]
    public async Task Project_IssuesDetachedClustersAndFocusedCallSites()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        InspectionEnvelope<AssemblyPairDirectUseClusterInspectionOutcome>
            inspection = Inspect(context);
        var available =
            Assert.IsType<
                AssemblyPairDirectUseClusterInspectionOutcome.Available>(
                    inspection.Content);
        AssemblyPairDirectUseCluster selected =
            available.Projection.Clusters
                .OrderByDescending(cluster => cluster.CallSiteCount)
                .First();

        DirectUseClusterPresentationResult discovery =
            AssemblyPairDirectUseClusterPresentation.Project(
                inspection,
                selectedCluster: null);
        DirectUseClusterPresentationResult focused =
            AssemblyPairDirectUseClusterPresentation.Project(
                inspection,
                selected.Ordinal);

        Assert.Equal(
            DirectUseClusterPresentationStatus.Available,
            discovery.Status);
        Assert.True(discovery.IsComplete);
        Assert.Equal(
            available.Projection.Clusters.Length,
            discovery.Clusters.Length);
        Assert.Empty(discovery.CallSites);
        Assert.Equal(selected.Ordinal, focused.SelectedCluster);
        Assert.Equal(selected.CallSiteCount, focused.CallSites.Length);
        Assert.All(
            focused.CallSites,
            call =>
            {
                Assert.Contains('(', call.SourceMember);
                Assert.Contains('(', call.TargetMember);
                Assert.Contains('(', call.EvidenceMethod);
                Assert.Contains(
                    call.CallKind,
                    new[] { "call", "callvirt", "newobj" });
                Assert.NotEqual(Guid.Empty, call.Source.ModuleVersionId);
                Assert.NotEqual(Guid.Empty, call.Target.ModuleVersionId);
                Assert.NotEqual(
                    Guid.Empty,
                    call.EvidenceModuleVersionId);
                Assert.True(call.SourceToken > 0);
                Assert.True(call.TargetToken > 0);
                Assert.True(call.EvidenceToken > 0);
                Assert.True(call.IlOffset >= 0);
            });
    }

    [Fact]
    public async Task Project_PreservesMissingAndRejectedOutcomes()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        InspectionEnvelope<AssemblyPairDirectUseClusterInspectionOutcome>
            inspection = Inspect(context);

        DirectUseClusterPresentationResult missing =
            AssemblyPairDirectUseClusterPresentation.Project(
                inspection,
                int.MaxValue);
        InspectionEnvelope<AssemblyPairCallUseInspectionOutcome> rejectedPair =
            AssemblyPairCallUseInspection.Execute(
                context.Group,
                context.First,
                context.First);
        DirectUseClusterPresentationResult rejected =
            AssemblyPairDirectUseClusterPresentation.Project(
                AssemblyPairDirectUseClusterInspection.Execute(rejectedPair),
                selectedCluster: null);

        Assert.Equal(
            DirectUseClusterPresentationStatus.ClusterNotFound,
            missing.Status);
        Assert.NotEmpty(missing.Clusters);
        Assert.Empty(missing.CallSites);
        Assert.NotNull(missing.Failure);
        Assert.Equal(
            DirectUseClusterPresentationStatus.Rejected,
            rejected.Status);
        Assert.False(rejected.IsComplete);
        Assert.Empty(rejected.Clusters);
        Assert.NotNull(rejected.Failure);
    }

    static InspectionEnvelope<AssemblyPairDirectUseClusterInspectionOutcome>
        Inspect(PairContext context) =>
        AssemblyPairDirectUseClusterInspection.Execute(
            AssemblyPairCallUseInspection.Execute(
                context.Group,
                context.First,
                context.Second));

    sealed class PairContext : IAsyncDisposable
    {
        PairContext(
            InspectionWorkspace workspace,
            AssemblyContextGroup group,
            ResolvedAssemblyReference first,
            ResolvedAssemblyReference second)
        {
            Workspace = workspace;
            Group = group;
            First = first;
            Second = second;
        }

        internal InspectionWorkspace Workspace { get; }
        internal AssemblyContextGroup Group { get; }
        internal ResolvedAssemblyReference First { get; }
        internal ResolvedAssemblyReference Second { get; }

        internal static PairContext Create(
            string firstPath,
            string secondPath)
        {
            ResolvedAssemblyReference first =
                ResolvedAssemblyReference.CreateFromPath(
                    firstPath,
                    AssemblyResolutionProvenance.Local(
                        "Direct-Use Cluster presentation test"));
            ResolvedAssemblyReference second =
                ResolvedAssemblyReference.CreateFromPath(
                    secondPath,
                    AssemblyResolutionProvenance.Local(
                        "Direct-Use Cluster presentation test"));
            var workspace = new InspectionWorkspace();
            var policy =
                new SourceRelativeAssemblyGroupBindingPolicy(
                new[]
                {
                    (
                        first,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(firstPath)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                })),
                    (
                        second,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(firstPath)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                })),
                });
            AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(first, policy),
                    new AssemblyContextParticipant(second, policy),
                ]);
            return new(workspace, group, first, second);
        }

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    }
}
