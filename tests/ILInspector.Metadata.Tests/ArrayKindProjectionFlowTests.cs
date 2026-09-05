using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class ArrayKindProjectionFlowTests
{
    public static TheoryData<
        string,
        string,
        string,
        string?,
        string,
        string,
        string> ProjectionExpectations => new()
        {
            {
                "Vector",
                "System.Int32[]",
                "int[]",
                null,
                "SzArray(Primitive:Int32)",
                "M:N.ArrayKinds.M(System.Int32[])",
                "M:N.ArrayKinds.M(int[])"
            },
            {
                "RankOneNonSz",
                "System.Int32[*]",
                "int[*]",
                "System.Int32[*]",
                "Array(rank=1;sizes=[];lowerBounds=[];Primitive:Int32)",
                "M:N.ArrayKinds.M(System.Int32[*])",
                "M:N.ArrayKinds.M(int[*])"
            },
            {
                "RankTwo",
                "System.Int32[,]",
                "int[,]",
                null,
                "Array(rank=2;sizes=[];lowerBounds=[];Primitive:Int32)",
                "M:N.ArrayKinds.M(System.Int32[,])",
                "M:N.ArrayKinds.M(int[,])"
            },
            {
                "NestedRankOneNonSz",
                "System.Collections.Generic.List{System.Int32[*]}",
                "System.Collections.Generic.List<int[*]>",
                "System.Collections.Generic.List{System.Int32[*]}",
                "GenericInstance:System.Collections.Generic.List`1<Array(rank=1;sizes=[];lowerBounds=[];Primitive:Int32)>",
                "M:N.ArrayKinds.M(System.Collections.Generic.List`1<System.Int32[*]>)",
                "M:N.ArrayKinds.M(System.Collections.Generic.List<int[*]>)"
            },
        };

    public static TheoryData<string, string> ComparisonExpectations => new()
    {
        { "Vector", "M:N.ArrayKinds.M(int[])" },
        { "RankOneNonSz", "M:N.ArrayKinds.M(int[*])" },
        { "RankTwo", "M:N.ArrayKinds.M(int[,])" },
        {
            "NestedRankOneNonSz",
            "M:N.ArrayKinds.M(System.Collections.Generic.List<int[*]>)"
        },
    };

    [Theory]
    [MemberData(nameof(ProjectionExpectations))]
    public void MetadataProjectionFlow_MatchesRecordedStageExpectations(
        string specimen,
        string expectedStructural,
        string expectedApiType,
        string? expectedStructuralPayload,
        string expectedShape,
        string expectedDirectAnchor,
        string expectedMaterializedAnchor)
    {
        byte[] image =
            ArrayKindSignatureFixture.BuildProjectionFlowImage(specimen);
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle = TypeHandle(reader);
        MethodDefinition method = Assert.Single(
            reader.GetTypeDefinition(typeHandle)
                .GetMethods()
                .Select(reader.GetMethodDefinition));
        MethodSignature<TypeNode> decoded = method.DecodeSignature(
            TypeNodeProvider.Instance,
            GenericContext.ForMethod(
                reader,
                reader.GetTypeDefinition(typeHandle),
                method));

        Assert.Equal(
            expectedStructural,
            Assert.Single(decoded.ParameterTypes).StructuralIdentity());
        Assert.Equal(
            expectedStructural,
            decoded.ReturnType.StructuralIdentity());

        MemberAnchor directAnchor = ApiMemberIdentity.CreateMethodAnchor(
            reader,
            typeHandle,
            method);
        Assert.Equal(expectedDirectAnchor, directAnchor.CanonicalSignature);

        ApiSurface live = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType liveType = Assert.Single(
            live.Types,
            type => type.Name == ArrayKindSignatureFixture.TypeName);
        ApiMember liveMember = Assert.Single(liveType.Members);
        ApiSignature signature = Assert.IsType<ApiSignature>(
            liveMember.SignatureModel);
        ApiParameter parameter = Assert.Single(signature.Parameters);

        Assert.Equal(expectedApiType, parameter.Type);
        Assert.Equal(expectedApiType, parameter.EffectiveCanonicalType);
        Assert.Equal(expectedStructuralPayload, parameter.StructuralType);
        Assert.Equal(expectedApiType, signature.ReturnType);
        Assert.Equal(expectedApiType, signature.EffectiveCanonicalReturnType);
        Assert.Equal(
            expectedStructuralPayload,
            signature.StructuralReturnType);
        Assert.Equal(
            expectedShape,
            DescribeShape(
                Assert.IsType<ApiTypeShape>(signature.ReturnTypeShape)));

        MemberAnchor materializedAnchor = ApiMemberIdentity.GetMemberAnchor(
            liveType,
            liveMember);
        Assert.Equal(
            expectedMaterializedAnchor,
            materializedAnchor.CanonicalSignature);

        ApiSurface restored = JsonSerializer.Deserialize<ApiSurface>(
            JsonSerializer.Serialize(live))!;
        ApiType restoredType = Assert.Single(
            restored.Types,
            type => type.Name == ArrayKindSignatureFixture.TypeName);
        ApiMember restoredMember = Assert.Single(restoredType.Members);
        Assert.Null(restoredMember.SignatureModel);
        Assert.Equal(
            expectedMaterializedAnchor,
            ApiMemberIdentity
                .GetMemberAnchor(restoredType, restoredMember)
                .CanonicalSignature);
    }

    [Theory]
    [MemberData(nameof(ComparisonExpectations))]
    public void ApiComparison_UsesRecordedProjectedIdentity(
        string specimen,
        string expectedMaterializedAnchor)
    {
        ApiSurface vector = Extract("Vector");
        ApiSurface candidate = Extract(specimen);

        ApiDiff diff = ApiDiffAnalyzer.Compare(vector, candidate);

        if (specimen == "Vector")
        {
            Assert.True(diff.IsEmpty);
            return;
        }

        TypeDiff typeDiff = Assert.Single(diff.TypeDiffs);
        Assert.Contains(
            typeDiff.Changes,
            change => change.Subject?.OldMember?.CanonicalSignature
                == "M:N.ArrayKinds.M(int[])");
        Assert.Contains(
            typeDiff.Changes,
            change => change.Subject?.NewMember?.CanonicalSignature
                == expectedMaterializedAnchor);
    }

    static string DescribeShape(ApiTypeShape shape) =>
        shape.Kind switch
        {
            ApiTypeShapeKind.Primitive => $"Primitive:{shape.Primitive}",
            ApiTypeShapeKind.Named =>
                $"Named:{shape.Definition?.FullName}",
            ApiTypeShapeKind.GenericInstance =>
                $"GenericInstance:{shape.Definition?.FullName}<"
                + string.Join(
                    ",",
                    shape.TypeArguments.Select(DescribeShape))
                + ">",
            ApiTypeShapeKind.SzArray =>
                $"SzArray({DescribeShape(shape.ElementType!)})",
            ApiTypeShapeKind.Array =>
                $"Array(rank={shape.ArrayRank};"
                + $"sizes=[{string.Join(",", shape.ArraySizes)}];"
                + "lowerBounds=["
                + string.Join(",", shape.ArrayLowerBounds)
                + $"];{DescribeShape(shape.ElementType!)})",
            _ => throw new InvalidOperationException(
                $"Unexpected API type shape kind {shape.Kind}."),
        };

    static ApiSurface Extract(string specimen)
    {
        using var peReader = new PEReader(
            new MemoryStream(
                ArrayKindSignatureFixture.BuildProjectionFlowImage(specimen),
                writable: false));
        return ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    static TypeDefinitionHandle TypeHandle(MetadataReader reader) =>
        reader.TypeDefinitions.Single(
            handle => reader.GetString(
                reader.GetTypeDefinition(handle).Name)
                == ArrayKindSignatureFixture.TypeName);
}
