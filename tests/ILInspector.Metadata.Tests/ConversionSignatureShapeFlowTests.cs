using CSharpText;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class ConversionSignatureShapeFlowTests
{
    const string ParameterDeclaration = "global::ILInspector.Metadata.Tests.ConversionFlowSample value";
    const string ParameterTransport =
        "mss1:0(1:vn26:ILInspector.Metadata.Tests1:20:ConversionFlowSample0:0:)";

    static readonly Specimen[] Conversions =
    [
        new(
            "ImplicitInt32", "op_Implicit", typeof(int), "System.Int32",
            $"public static implicit operator int({ParameterDeclaration}) => 0;",
            ParameterTransport + "yp12:System.Int32"),
        new(
            "ImplicitInt64", "op_Implicit", typeof(long), "System.Int64",
            $"public static implicit operator long({ParameterDeclaration}) => 0;",
            ParameterTransport + "yp12:System.Int64"),
        new(
            "ExplicitInt16", "op_Explicit", typeof(short), "System.Int16",
            $"public static explicit operator short({ParameterDeclaration}) => 0;",
            ParameterTransport + "yp12:System.Int16"),
        new(
            "ExplicitByte", "op_Explicit", typeof(byte), "System.Byte",
            $"public static explicit operator byte({ParameterDeclaration}) => 0;",
            ParameterTransport + "yp11:System.Byte"),
        new(
            "CheckedInt16", "op_CheckedExplicit", typeof(short), "System.Int16",
            $"public static explicit operator checked short({ParameterDeclaration}) => 0;",
            ParameterTransport + "yp12:System.Int16",
            SourceAvailable: false),
        new(
            "CheckedByte", "op_CheckedExplicit", typeof(byte), "System.Byte",
            $"public static explicit operator checked byte({ParameterDeclaration}) => 0;",
            ParameterTransport + "yp11:System.Byte",
            SourceAvailable: false),
    ];

    public static TheoryData<string> ConversionNames =>
        new(Conversions.Select(specimen => specimen.Name));

    [Theory]
    [MemberData(nameof(ConversionNames))]
    public void ConversionFlow_PreservesRecordedReturnShapeAndCandidate(string specimenName)
    {
        Specimen specimen = Conversions.Single(candidate => candidate.Name == specimenName);
        using var peReader = OpenFixture();
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle expected = ExpectedHandle(specimen.MetadataName, specimen.RuntimeReturnType);
        var candidates = MetadataCandidates(reader, specimen.MetadataName);
        Assert.Equal(2, candidates.Length);
        MemberSignatureShapeResult metadata = Assert.Single(
            candidates, candidate => candidate.Candidate == expected).Shape;
        MemberSignatureShapeResult restoredMetadata =
            AssertStages(metadata, specimen.ReturnType, specimen.Transport);
        var restoredCandidates = candidates
            .Select(candidate => (candidate.Candidate, Shape: Transport(candidate.Shape)))
            .ToArray();

        AssertUnique(MemberSignatureShapeMatcher.Match(restoredMetadata, restoredCandidates), expected);

        MemberSignatureShapeResult source = SourceMemberSignatureShape.Create(
            specimen.Declaration, SourceMemberSignatureKind.ConversionOperator);
        if (!specimen.SourceAvailable)
        {
            Assert.False(source.IsAvailable);
            Assert.Null(source.Shape);
            Assert.False(string.IsNullOrWhiteSpace(source.UnavailableReason));
            AssertUnavailable(MemberSignatureShapeMatcher.Match(
                restoredMetadata, [(specimen.Name, source)]));
            return;
        }

        MemberSignatureShapeResult restoredSource =
            AssertStages(source, specimen.ReturnType, specimen.Transport);
        AssertUnique(MemberSignatureShapeMatcher.Match(source, candidates), expected);
        AssertUnique(MemberSignatureShapeMatcher.Match(restoredSource, restoredCandidates), expected);

        var sourceCandidates = Conversions
            .Where(candidate => candidate.MetadataName == specimen.MetadataName)
            .Select(candidate => (
                Candidate: candidate.Name,
                Shape: AssertStages(
                    SourceMemberSignatureShape.Create(
                        candidate.Declaration, SourceMemberSignatureKind.ConversionOperator),
                    candidate.ReturnType,
                    candidate.Transport)))
            .ToArray();
        AssertUnique(
            MemberSignatureShapeMatcher.Match(restoredMetadata, sourceCandidates),
            specimen.Name);
    }

    [Fact]
    public void ConversionReturnWithoutSameNameCandidate_RemainsUnavailableAfterTransport()
    {
        using var peReader = OpenFixture();
        MetadataReader reader = peReader.GetMetadataReader();
        MemberSignatureShapeResult source = AssertStages(
            SourceMemberSignatureShape.Create(
                $"public static implicit operator byte({ParameterDeclaration}) => 0;",
                SourceMemberSignatureKind.ConversionOperator),
            "System.Byte",
            ParameterTransport + "yp11:System.Byte");
        var implicitCandidates = MetadataCandidates(reader, "op_Implicit")
            .Select(candidate => (candidate.Candidate, Shape: Transport(candidate.Shape)))
            .ToArray();
        Assert.Equal(2, implicitCandidates.Length);
        MemberSignatureCorrespondence<MethodDefinitionHandle> result =
            MemberSignatureShapeMatcher.Match(source, implicitCandidates);

        Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, result.Kind);
        Assert.True(result.Match.IsNil);
        Assert.Empty(result.Candidates);
        Assert.False(string.IsNullOrWhiteSpace(result.UnavailableReason));

        // The return type exists in a different operator-name group.
        var explicitCandidates = MetadataCandidates(reader, "op_Explicit")
            .Select(candidate => (candidate.Candidate, Shape: Transport(candidate.Shape)))
            .ToArray();
        AssertUnique(
            MemberSignatureShapeMatcher.Match(source, explicitCandidates),
            ExpectedHandle("op_Explicit", typeof(byte)));
    }

    [Theory]
    [InlineData(nameof(ConversionFlowSample.ReadInt32), typeof(int), "int", "long")]
    [InlineData(nameof(ConversionFlowSample.ReadInt64), typeof(long), "long", "int")]
    public void OrdinaryReturnTypes_AreErasedRatherThanUsedAsIdentity(
        string methodName,
        Type runtimeReturnType,
        string sourceReturnType,
        string otherReturnType)
    {
        using var peReader = OpenFixture();
        MethodDefinitionHandle expected = ExpectedHandle(methodName, runtimeReturnType);
        MemberSignatureShapeResult metadata = AssertStages(
            MetadataMemberSignatureShape.Create(peReader.GetMetadataReader(), expected),
            returnType: null,
            ParameterTransport + "n");
        MemberSignatureShapeResult original = AssertStages(
            SourceMemberSignatureShape.Create(
                $"public static {sourceReturnType} {methodName}({ParameterDeclaration}) => 0;",
                SourceMemberSignatureKind.Method),
            returnType: null,
            ParameterTransport + "n");
        MemberSignatureShapeResult differentReturn = AssertStages(
            SourceMemberSignatureShape.Create(
                $"public static {otherReturnType} {methodName}({ParameterDeclaration}) => 0;",
                SourceMemberSignatureKind.Method),
            returnType: null,
            ParameterTransport + "n");

        AssertUnique(
            MemberSignatureShapeMatcher.Match(metadata, [("different-return", differentReturn)]),
            "different-return");
        MemberSignatureCorrespondence<string> ambiguous = MemberSignatureShapeMatcher.Match(
            metadata, [("original", original), ("different-return", differentReturn)]);
        Assert.Equal(MemberSignatureCorrespondenceKind.Ambiguous, ambiguous.Kind);
        Assert.Equal(["original", "different-return"], ambiguous.Candidates);
        Assert.Null(ambiguous.Match);
        Assert.Null(ambiguous.UnavailableReason);
    }

    static (MethodDefinitionHandle Candidate, MemberSignatureShapeResult Shape)[] MetadataCandidates(
        MetadataReader reader, string name)
    {
        var typeHandle = (TypeDefinitionHandle)MetadataTokens.EntityHandle(typeof(ConversionFlowSample).MetadataToken);
        return reader.GetTypeDefinition(typeHandle).GetMethods()
            .Where(handle => reader.StringComparer.Equals(reader.GetMethodDefinition(handle).Name, name))
            .Select(handle => (
                Candidate: handle,
                Shape: MetadataMemberSignatureShape.Create(reader, handle)))
            .ToArray();
    }

    static MethodDefinitionHandle ExpectedHandle(string name, Type returnType)
    {
        // Reflection supplies an oracle independent of the SRM shape adapter.
        MethodInfo method = typeof(ConversionFlowSample)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == name && method.ReturnType == returnType);
        return (MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MetadataToken);
    }

    static MemberSignatureShapeResult AssertStages(
        MemberSignatureShapeResult result, string? returnType, string expectedTransport)
    {
        var parameterType = new NamedTypeSignatureShape(
            "ILInspector.Metadata.Tests",
            new([new NamedTypeSegment("ConversionFlowSample", 0, SignatureShapeList<TypeSignatureShape>.Empty)]));
        var expected = new MemberSignatureShape(
            0,
            new([new MemberParameterSignatureShape(ParameterPassingKind.Value, parameterType)]),
            returnType is null ? null : new PrimitiveTypeSignatureShape(returnType));

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Null(result.UnavailableReason);
        Assert.Equal(expected, result.Shape);
        string text = MemberSignatureShapeCodec.Encode(result.Shape!);
        Assert.Equal(expectedTransport, text);
        MemberSignatureShapeResult restored = MemberSignatureShapeCodec.Decode(text);
        Assert.True(restored.IsAvailable, restored.UnavailableReason);
        Assert.Null(restored.UnavailableReason);
        Assert.Equal(expected, restored.Shape);
        return restored;
    }

    static MemberSignatureShapeResult Transport(MemberSignatureShapeResult result)
    {
        Assert.True(result.IsAvailable, result.UnavailableReason);
        MemberSignatureShapeResult restored =
            MemberSignatureShapeCodec.Decode(MemberSignatureShapeCodec.Encode(result.Shape!));
        Assert.True(restored.IsAvailable, restored.UnavailableReason);
        return restored;
    }

    static void AssertUnique<T>(MemberSignatureCorrespondence<T> result, T expected)
    {
        Assert.Equal(MemberSignatureCorrespondenceKind.Unique, result.Kind);
        Assert.Equal(expected, result.Match);
        Assert.Empty(result.Candidates);
        Assert.Null(result.UnavailableReason);
    }

    static void AssertUnavailable(MemberSignatureCorrespondence<string> result)
    {
        Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, result.Kind);
        Assert.Null(result.Match);
        Assert.Empty(result.Candidates);
        Assert.False(string.IsNullOrWhiteSpace(result.UnavailableReason));
    }

    static PEReader OpenFixture() =>
        new(File.OpenRead(typeof(ConversionFlowSample).Assembly.Location));

    sealed record Specimen(
        string Name,
        string MetadataName,
        Type RuntimeReturnType,
        string ReturnType,
        string Declaration,
        string Transport,
        bool SourceAvailable = true);
}
