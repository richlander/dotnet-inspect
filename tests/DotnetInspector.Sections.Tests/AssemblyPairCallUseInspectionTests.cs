using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class AssemblyPairCallUseInspectionTests
{
    [Fact]
    public async Task Execute_ReturnsExactProjectionInCompletedEnvelope()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());

        InspectionEnvelope<AssemblyPairCallUseInspectionOutcome> envelope =
            AssemblyPairCallUseInspection.Execute(
                context.Group,
                context.First,
                context.Second);

        var available =
            Assert.IsType<AssemblyPairCallUseInspectionOutcome.Available>(
                envelope.Content);
        Assert.True(available.Projection.IsComplete);
        Assert.NotEmpty(available.Projection.Pair.Occurrences);
        Assert.Empty(envelope.Diagnostics);
        var share =
            Assert.IsType<InspectionShare.NonProjectable>(
                envelope.Share);
        Assert.Equal("assembly-pair/call-use", share.Path);
    }

    [Fact]
    public async Task Execute_ReturnsTypedRejectionForForeignParticipant()
    {
        string callerPath =
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
        string targetPath =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        await using PairContext context =
            PairContext.Create(callerPath, targetPath);
        ResolvedAssemblyReference foreign =
            ResolvedAssemblyReference.CreateFromPath(
                targetPath,
                AssemblyResolutionProvenance.Local(
                    "foreign pairwise call-use inspection test"));

        InspectionEnvelope<AssemblyPairCallUseInspectionOutcome> envelope =
            AssemblyPairCallUseInspection.Execute(
                context.Group,
                context.First,
                foreign);

        var rejected =
            Assert.IsType<AssemblyPairCallUseInspectionOutcome.Rejected>(
                envelope.Content);
        Assert.Equal(
            AssemblyPairCallUseRequestFailureKind.ParticipantOutsideGroup,
            rejected.Kind);
        InspectionDiagnostic diagnostic =
            Assert.Single(envelope.Diagnostics);
        Assert.Equal(
            "assembly-pair-call-use.request-rejected",
            diagnostic.Code);
        Assert.Equal(
            InspectionDiagnosticSeverity.Error,
            diagnostic.Severity);
    }

    [Fact]
    public async Task Execute_ReturnsTypedRejectionForSameParticipant()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());

        InspectionEnvelope<AssemblyPairCallUseInspectionOutcome> envelope =
            AssemblyPairCallUseInspection.Execute(
                context.Group,
                context.First,
                context.First);

        var rejected =
            Assert.IsType<AssemblyPairCallUseInspectionOutcome.Rejected>(
                envelope.Content);
        Assert.Equal(
            AssemblyPairCallUseRequestFailureKind.SameParticipant,
            rejected.Kind);
    }

    [Fact]
    public async Task Execute_ReturnsTypedRejectionForSamePhysicalArtifact()
    {
        string path =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        await using PairContext context =
            PairContext.Create(path, path);

        InspectionEnvelope<AssemblyPairCallUseInspectionOutcome> envelope =
            AssemblyPairCallUseInspection.Execute(
                context.Group,
                context.First,
                context.Second);

        var rejected =
            Assert.IsType<AssemblyPairCallUseInspectionOutcome.Rejected>(
                envelope.Content);
        Assert.Equal(
            AssemblyPairCallUseRequestFailureKind
                .SamePhysicalAssemblyArtifact,
            rejected.Kind);
    }

    [Fact]
    public async Task Execute_PreservesIncompletePositiveEvidenceDiagnostics()
    {
        string callerPath =
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
        string targetPath =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        ResolvedAssemblyReference caller =
            ResolvedAssemblyReference.CreateFromPath(
                callerPath,
                AssemblyResolutionProvenance.Local(
                    "pairwise call-use inspection test"));
        ResolvedAssemblyReference targetIdentity =
            ResolvedAssemblyReference.CreateFromPath(
                targetPath,
                AssemblyResolutionProvenance.Local(
                    "pairwise call-use inspection test"));
        ResolvedAssemblyReference malformed =
            ResolvedAssemblyReference.Create(
                targetIdentity.Identity,
                path: null,
                () => new MemoryStream([0x00, 0x01, 0x02]),
                AssemblyResolutionProvenance.Local(
                    "malformed pairwise call-use inspection test"));
        await using PairContext context =
            PairContext.Create(caller, malformed, callerPath);

        InspectionEnvelope<AssemblyPairCallUseInspectionOutcome> envelope =
            AssemblyPairCallUseInspection.Execute(
                context.Group,
                context.First,
                context.Second);

        var available =
            Assert.IsType<AssemblyPairCallUseInspectionOutcome.Available>(
                envelope.Content);
        Assert.False(available.Projection.IsComplete);
        Assert.Single(available.Projection.Pair.Participants);
        InspectionDiagnostic diagnostic =
            Assert.Single(envelope.Diagnostics);
        Assert.Equal(
            "assembly-pair-call-use.participant-rejected",
            diagnostic.Code);
        Assert.Equal(
            InspectionDiagnosticSeverity.Error,
            diagnostic.Severity);
    }

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
                        "pairwise call-use inspection test"));
            ResolvedAssemblyReference second =
                ResolvedAssemblyReference.CreateFromPath(
                    secondPath,
                    AssemblyResolutionProvenance.Local(
                        "pairwise call-use inspection test"));
            return Create(first, second, firstPath);
        }

        internal static PairContext Create(
            ResolvedAssemblyReference first,
            ResolvedAssemblyReference second,
            string resolutionPath)
        {
            var workspace = new InspectionWorkspace();
            var policy =
                new SourceRelativeAssemblyGroupBindingPolicy(
                new[]
                {
                    (
                        first,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(resolutionPath)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                })),
                    (
                        second,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(resolutionPath)
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
