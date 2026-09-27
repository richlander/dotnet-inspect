using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

public sealed partial class DirectCallDefinitionResolutionTests
{
    static DirectCallTarget SameModuleTarget(SyntheticOptions options)
    {
        LibraryCallGraphAnalysisResult graph =
            CreateSynthetic(options).Participant.CallGraph;
        return graph.ResolveTarget(Assert.Single(graph.DirectCalls));
    }

    [Fact]
    public void SameModuleTarget_MemberReferenceToMissingMethodIsUnmatched()
    {
        DirectCallTarget.Unresolved target =
            Assert.IsType<DirectCallTarget.Unresolved>(
                SameModuleTarget(new()
                {
                    CallName = "Missing",
                    CallViaMemberReference = true,
                }));

        Assert.Equal(DirectCallTargetUnresolvedReason.Unmatched, target.Reason);
    }

    [Fact]
    public void SameModuleTarget_DuplicateDefinitionsAreAmbiguous()
    {
        DirectCallTarget.Unresolved target =
            Assert.IsType<DirectCallTarget.Unresolved>(
                SameModuleTarget(new()
                {
                    TargetCount = 2,
                    CallViaMemberReference = true,
                }));

        Assert.Equal(DirectCallTargetUnresolvedReason.Ambiguous, target.Reason);
    }

    [Fact]
    public void SameModuleTarget_MemberReferenceToSingleDefinitionResolves()
    {
        DirectCallTarget.CurrentModule target =
            Assert.IsType<DirectCallTarget.CurrentModule>(
                SameModuleTarget(new()
                {
                    CallViaMemberReference = true,
                }));

        Assert.Equal("Target", target.Method.Name);
    }
}
