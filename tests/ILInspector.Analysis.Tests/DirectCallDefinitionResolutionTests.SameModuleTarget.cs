using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class DirectCallDefinitionResolutionTests
{
    static DirectCallTarget SameModuleTarget(SyntheticOptions options)
    {
        SyntheticParticipant synthetic = CreateSynthetic(options);
        LibraryCallGraphAnalysisResult graph =
            synthetic.Participant.CallGraph;
        // Method Query Source call-target support issues the same target.
        MethodDefinitionRequestSetTests.AssertCallTargetParity(
            graph,
            MethodDefinitionRequestSetTests.ProbeCallTargets(
                [.. synthetic.Image]));
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
    public void SameModuleTarget_UnsupportedTokenBoundCalleeReceiptsItsProbes()
    {
        byte[] unreadable = new byte[SignatureBlobGuard.DefaultMaxDepth + 4];
        unreadable[0] = 0x00;
        unreadable[1] = 0x00;
        unreadable.AsSpan(2, SignatureBlobGuard.DefaultMaxDepth + 1).Fill(0x1D);
        unreadable[^1] = 0x1C;
        SyntheticParticipant synthetic = CreateSynthetic(new()
        {
            TargetSignature = unreadable,
            TargetReturnValue = SyntheticStackValue.Null,
            PopCallReturn = true,
        });
        DirectCall call = Assert.Single(
            synthetic.Participant.CallGraph.DirectCalls);
        Assert.Equal(
            HandleKind.MethodDefinition,
            MetadataTokens.EntityHandle(call.OperandToken).Kind);

        SameModuleCallTargetCoverage coverage =
            MethodDefinitionRequestSetTests.ProbeCallTargetCoverage(
                [.. synthetic.Image],
                MetadataTokens.MethodDefinitionHandle(
                    call.EvidenceMethod.MetadataToken & 0x00FFFFFF));

        // The call fails its site checks before the generic-declaration
        // probe, yet both MethodDef rows read are receipted: the caller
        // scope and the definition. Only the operand's guard reads its blob.
        Assert.Equal(1, coverage.Queries);
        Assert.Equal(
            new SameModuleCallTargetWork(
                TypeDefinitionRows: 0,
                MethodDefinitionRows: 2,
                SignatureBytes: unreadable.Length),
            coverage.QueryWork);
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
