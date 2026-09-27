using ILInspector.Analysis;
using ILInspector.Metadata;

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
    public void SameModuleTarget_UndecodableCalleeIsUnsupportedNotMalformed()
    {
        // A target signature past the decode depth budget: well-formed input
        // that the decoder does not support.
        byte[] unreadable = new byte[SignatureBlobGuard.DefaultMaxDepth + 4];
        unreadable[0] = 0x00;
        unreadable[1] = 0x00;
        unreadable.AsSpan(2, SignatureBlobGuard.DefaultMaxDepth + 1).Fill(0x1D);
        unreadable[^1] = 0x1C;

        DirectCallTarget.Unresolved target =
            Assert.IsType<DirectCallTarget.Unresolved>(
                SameModuleTarget(new()
                {
                    TargetSignature = unreadable,
                    TargetReturnValue = SyntheticStackValue.Null,
                    PopCallReturn = true,
                }));

        Assert.Equal(DirectCallTargetUnresolvedReason.UnsupportedSignature, target.Reason);
    }

    [Fact]
    public void SameModuleTarget_MalformedGenericDeclaringTypeIsMalformed()
    {
        DirectCallTarget.Unresolved target =
            Assert.IsType<DirectCallTarget.Unresolved>(
                SameModuleTarget(new()
                {
                    CallViaMalformedGenericDeclaringType = true,
                }));

        Assert.Equal(DirectCallTargetUnresolvedReason.MalformedSignature, target.Reason);
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
