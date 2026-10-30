using System.Collections.Immutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class DirectCallDefinitionResolutionTests
{
    static SemanticIdentityObservation SemanticIdentityOfTarget(
        SyntheticOptions options)
    {
        SyntheticParticipant synthetic = CreateSynthetic(options);
        ImmutableDictionary<int, SemanticIdentityObservation> identities =
            MethodDefinitionRequestSetTests.ProbeSemanticIdentities(
                [.. synthetic.Image]);
        MethodDefinitionRequestSetTests.AssertSemanticIdentityParity(
            synthetic.Participant.CallGraph,
            identities);
        return Assert.Single(
                identities,
                pair => pair.Value.MethodIdentity?.Name == options.TargetName)
            .Value;
    }

    [Fact]
    public void SemanticIdentity_UnreadableSignatureIsTypedMalformed()
    {
        // A class element whose TypeDefOrRef coded index is truncated passes
        // the depth guard but cannot be decoded.
        SyntheticParticipant synthetic = CreateSynthetic(new()
        {
            TargetSignature = [0x00, 0x01, 0x01, 0x12, 0x7F],
        });
        ImmutableDictionary<int, SemanticIdentityObservation> identities =
            MethodDefinitionRequestSetTests.ProbeSemanticIdentities(
                [.. synthetic.Image]);
        MethodDefinitionRequestSetTests.AssertSemanticIdentityParity(
            synthetic.Participant.CallGraph,
            identities);

        // The analysis publishes no identity for the target; the source
        // publishes a malformed outcome instead of failing the producer.
        KeyValuePair<int, SemanticIdentityObservation> malformed =
            Assert.Single(
                identities,
                pair => pair.Value.Result
                    is MethodSemanticIdentityResult.Malformed);
        Assert.DoesNotContain(
            synthetic.Participant.CallGraph.DeclaredMethods,
            method => method.MetadataToken == malformed.Key);
        Assert.Null(malformed.Value.MethodIdentity);

        MethodSemanticIdentityCoverage coverage =
            MethodDefinitionRequestSetTests.ProbeSemanticIdentityCoverage(
                [.. synthetic.Image]);
        Assert.Equal(1, coverage.Malformed);
        Assert.Equal(identities.Count - 1, coverage.Decoded);
        Assert.Equal(identities.Count, coverage.Methods.Count);
    }

    [Fact]
    public void SemanticIdentity_VarargDefinitionKeepsRequiredParameterCount()
    {
        SemanticIdentityObservation observation = SemanticIdentityOfTarget(
            new()
            {
                // vararg, one parameter, void (int32)
                TargetSignature = [0x05, 0x01, 0x01, 0x08],
                CallArgumentKinds = [SyntheticStackValue.Int32],
            });

        MethodSemanticIdentity identity =
            Assert.IsType<MethodSemanticIdentityResult.Decoded>(
                    observation.Result)
                .Identity;
        Assert.Equal(0x05, identity.SignatureHeader & 0x0F);
        Assert.Equal(1, identity.RequiredParameterCount);
        Assert.Single(identity.ParameterTypes);
    }

    [Fact]
    public void SemanticIdentity_SignaturePastDecodeDepthIsBoundedNotMalformed()
    {
        byte[] unreadable = new byte[SignatureBlobGuard.DefaultMaxDepth + 4];
        unreadable[0] = 0x00;
        unreadable[1] = 0x00;
        unreadable.AsSpan(2, SignatureBlobGuard.DefaultMaxDepth + 1).Fill(0x1D);
        unreadable[^1] = 0x1C;

        SemanticIdentityObservation observation = SemanticIdentityOfTarget(
            new()
            {
                TargetSignature = unreadable,
                TargetReturnValue = SyntheticStackValue.Null,
                PopCallReturn = true,
            });

        MethodSemanticIdentity identity =
            Assert.IsType<MethodSemanticIdentityResult.SignatureBoundExceeded>(
                    observation.Result)
                .Identity;
        Assert.Empty(identity.ParameterTypes);
        Assert.Equal(0, identity.SignatureHeader);
        Assert.Equal(-1, identity.RequiredParameterCount);
        Assert.Equal(TypeRefKind.Unsupported, identity.ReturnType.Kind);
    }
}
