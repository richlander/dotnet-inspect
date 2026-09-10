using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyPairCallUseQueryTests
{
    [Fact]
    public void ExecuteReturnsExactCallsAcrossBothPairDirections()
    {
        using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());

        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.Second,
                context.First);

        Assert.True(result.IsComplete);
        Assert.Empty(result.Failures);
        Assert.Contains(
            result.Occurrences,
            occurrence =>
                occurrence.Source.Identity.Name
                    == "ILInspector.Analysis.CallerGraphCaller"
                && occurrence.SourceMethod.Name == "Run"
                && occurrence.Target.Identity.Name
                    == "ILInspector.Analysis.CallerGraphTarget"
                && occurrence.TargetMethod.Name == "Ping"
                && occurrence.Call.Kind == Analysis.CallKind.Call);
        Assert.Contains(
            result.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "CallBodiless"
                && occurrence.TargetMethod.Name == "Invoke"
                && occurrence.Call.Kind
                    == Analysis.CallKind.CallVirtual);
        Assert.Contains(
            result.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "UseBox"
                && occurrence.TargetMethod.Name == ".ctor"
                && occurrence.Call.Kind
                    == Analysis.CallKind.NewObject);
        Assert.DoesNotContain(
            result.Occurrences,
            occurrence =>
                occurrence.Call.Kind is
                    Analysis.CallKind.LoadFunction
                    or Analysis.CallKind.LoadVirtualFunction
                    or Analysis.CallKind.CallIndirect);
        Assert.All(
            result.Occurrences,
            occurrence =>
                Assert.Equal(
                    occurrence.SourceMethod.MetadataToken,
                    occurrence.Call.Caller.MetadataToken));
    }

    [Fact]
    public void ExecuteRejectsARegistrationOutsideTheGroup()
    {
        using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        ResolvedAssemblyReference outside =
            ResolvedAssemblyReference.CreateFromPath(
                FixtureCatalog.AnalysisCallerGraphTargetV2
                    .AssemblyPath(),
                AssemblyResolutionProvenance.Local(
                    "pairwise query outside participant"));

        Assert.Throws<AssemblyPairCallUseRequestException>(
            () => AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                outside));
    }

    [Fact]
    public void ExecuteDoesNotTurnVersionSkewIntoAnAbsenceClaim()
    {
        using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath());

        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);

        Assert.False(result.IsComplete);
        Assert.True(
            result.Diagnostics.UnresolvedCandidateCallCount > 0);
        Assert.Empty(result.Occurrences);
    }

    [Fact]
    public void ExecuteCarriesRejectedParticipantBesideAvailableEvidence()
    {
        string callerPath =
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
        string targetPath =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        ResolvedAssemblyReference caller =
            ResolvedAssemblyReference.CreateFromPath(
                callerPath,
                AssemblyResolutionProvenance.Local(
                    "pairwise call-use test"));
        ResolvedAssemblyReference targetIdentity =
            ResolvedAssemblyReference.CreateFromPath(
                targetPath,
                AssemblyResolutionProvenance.Local(
                    "pairwise call-use test"));
        ResolvedAssemblyReference malformed =
            ResolvedAssemblyReference.Create(
                targetIdentity.Identity,
                path: null,
                () => new MemoryStream([0x00, 0x01, 0x02]),
                AssemblyResolutionProvenance.Local(
                    "malformed pairwise call-use test"));
        using PairContext context =
            PairContext.Create(caller, malformed, callerPath);

        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);

        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Subjects.Length);
        Assert.Single(result.Participants);
        Assert.IsType<AssemblyPairCallUseFailure.Rejected>(
            Assert.Single(result.Failures));
        Assert.Empty(result.Occurrences);
    }

    sealed class PairContext : IDisposable
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
                        "pairwise call-use test"));
            ResolvedAssemblyReference second =
                ResolvedAssemblyReference.CreateFromPath(
                    secondPath,
                    AssemblyResolutionProvenance.Local(
                        "pairwise call-use test"));
            return Create(first, second, firstPath);
        }

        internal static PairContext Create(
            ResolvedAssemblyReference first,
            ResolvedAssemblyReference second,
            string resolutionPath)
        {
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
            var workspace = new InspectionWorkspace();
            AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    new[]
                    {
                        new AssemblyContextParticipant(first, policy),
                        new AssemblyContextParticipant(second, policy),
                    });
            return new(workspace, group, first, second);
        }

        public void Dispose() => Workspace.Dispose();
    }
}
