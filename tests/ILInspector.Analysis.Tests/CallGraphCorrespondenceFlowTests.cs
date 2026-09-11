using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class CallGraphCorrespondenceFlowTests
{
    const int MethodToken = 0x06000001;

    public static TheoryData<
        string,
        string,
        string?,
        string,
        string,
        string> ProjectionExpectations => new()
        {
            {
                "Vector",
                "int[]",
                null,
                "System.Int32[]",
                "System.Int32[]",
                "1:MI;0;1;14:System.Int32[]14:System.Int32[]"
            },
            {
                "RankOneNonSz",
                "int[*]",
                "System.Int32[*]",
                "System.Int32[]",
                "System.Int32[*]",
                "1:MI;0;1;15:System.Int32[*]15:System.Int32[*]"
            },
            {
                "RankTwo",
                "int[,]",
                null,
                "System.Int32[,]",
                "System.Int32[,]",
                "1:MI;0;1;15:System.Int32[,]15:System.Int32[,]"
            },
            {
                "NestedRankOneNonSz",
                "System.Collections.Generic.List<int[*]>",
                "System.Collections.Generic.List{System.Int32[*]}",
                "System.Collections.Generic.List{System.Int32[]}",
                "System.Collections.Generic.List{System.Int32[*]}",
                "1:MI;0;1;48:System.Collections.Generic.List{System.Int32[*]}48:System.Collections.Generic.List{System.Int32[*]}"
            },
        };

    [Theory]
    [MemberData(nameof(ProjectionExpectations))]
    public void CorrespondenceFlow_MatchesRecordedStageExpectations(
        string specimen,
        string expectedApiType,
        string? expectedStructuralPayload,
        string expectedDisplayIdentity,
        string expectedStructuralIdentity,
        string expectedSelectorKey)
    {
        byte[] image =
            CallGraphArrayKindIdentityTests.BuildProjectionFlowImage(specimen);
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        ApiSurface live = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType liveType = Assert.Single(
            live.Types,
            type => type.Name == "ArrayCallGraphKinds");
        ApiMember liveMember = Assert.Single(liveType.Members);
        Assert.Equal(MethodToken, liveMember.MetadataToken);
        ApiSignature signature = Assert.IsType<ApiSignature>(
            liveMember.SignatureModel);
        ApiParameter parameter = Assert.Single(signature.Parameters);

        Assert.Equal(expectedApiType, parameter.Type);
        Assert.Equal(expectedStructuralPayload, parameter.StructuralType);
        Assert.Equal(expectedApiType, signature.ReturnType);
        Assert.Equal(
            expectedStructuralPayload,
            signature.StructuralReturnType);

        MemberRef reference = MemberResolver.ResolveMethod(
            reader,
            MetadataTokens.EntityHandle(MethodToken),
            GenericScope.Empty);
        CallGraphMemberSelector apiSelector =
            CallGraphMemberResolver.CreateSelector(liveType, liveMember);
        CallGraphMemberSelector referenceSelector =
            CallGraphMemberResolver.CreateSelector(reference);

        AssertSelector(
            apiSelector,
            expectedDisplayIdentity,
            expectedStructuralIdentity,
            expectedSelectorKey);
        AssertSelector(
            referenceSelector,
            expectedDisplayIdentity,
            expectedStructuralIdentity,
            expectedSelectorKey);

        AssertResolution(
            liveMember,
            Assert.IsType<CallGraphMemberResolution>(
                CallGraphMemberResolver.Resolve(
                    liveType,
                    referenceSelector.Name,
                    referenceSelector.Key)));
        AssertResolution(
            liveMember,
            Assert.IsType<CallGraphMemberResolution>(
                CallGraphMemberResolver.Resolve(
                    liveType,
                    referenceSelector.Name,
                    referenceSelector.Key,
                    metadataToken: MethodToken)));

        ApiSurface restored = JsonSerializer.Deserialize<ApiSurface>(
            JsonSerializer.Serialize(live))!;
        ApiType restoredType = Assert.Single(
            restored.Types,
            type => type.Name == "ArrayCallGraphKinds");
        ApiMember restoredMember = Assert.Single(restoredType.Members);
        Assert.Null(restoredMember.SignatureModel);
        Assert.Null(
            CallGraphMemberResolver.Resolve(
                restoredType,
                referenceSelector.Name,
                referenceSelector.Key));
        AssertResolution(
            restoredMember,
            Assert.IsType<CallGraphMemberResolution>(
                CallGraphMemberResolver.Resolve(
                    restoredType,
                    referenceSelector.Name,
                    referenceSelector.Key,
                    metadataToken: MethodToken)));
    }

    static void AssertSelector(
        CallGraphMemberSelector selector,
        string expectedDisplayIdentity,
        string expectedStructuralIdentity,
        string expectedKey)
    {
        Assert.Equal("M", selector.Name);
        Assert.Equal(0, selector.GenericArity);
        Assert.Equal(
            expectedDisplayIdentity,
            Assert.Single(selector.ParameterTypes));
        Assert.Equal(expectedDisplayIdentity, selector.ReturnType);
        Assert.Equal(
            expectedStructuralIdentity,
            Assert.Single(selector.StructuralParameterTypes));
        Assert.Equal(
            expectedStructuralIdentity,
            selector.StructuralReturnType);
        Assert.Equal(expectedKey, selector.Key);
    }

    static void AssertResolution(
        ApiMember expectedMember,
        CallGraphMemberResolution resolution)
    {
        Assert.Same(expectedMember, resolution.Member);
        Assert.Equal(MethodToken, resolution.BodyToken);
    }
}
