using CSharpText;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class ParameterPassingSignatureShapeFlowTests
{
    static readonly TypeSignatureShape Int32 = new PrimitiveTypeSignatureShape("System.Int32");
    static readonly TypeSignatureShape Int64 = new PrimitiveTypeSignatureShape("System.Int64");
    static readonly MemberParameterSignatureShape Int32Value = new(ParameterPassingKind.Value, Int32);
    static readonly MemberParameterSignatureShape Int32Reference = new(ParameterPassingKind.ByReference, Int32);
    static readonly MemberParameterSignatureShape Int32ArrayValue =
        new(ParameterPassingKind.Value, new ArrayTypeSignatureShape(Int32, 1, IsSzArray: true));
    static readonly MemberParameterSignatureShape Int32ArrayReference =
        new(ParameterPassingKind.ByReference, new ArrayTypeSignatureShape(Int32, 1, IsSzArray: true));
    static readonly MemberParameterSignatureShape Int64ArrayReference =
        new(ParameterPassingKind.ByReference, new ArrayTypeSignatureShape(Int64, 1, IsSzArray: true));

    static readonly Specimen[] Specimens =
    [
        new(
            "RefValue", nameof(ParameterPassingFlowSamples.Ref), "int value",
            [typeof(int)], Shape(Int32Value),
            "mss1:0(1:vp12:System.Int32)n"),
        new(
            "RefReference", nameof(ParameterPassingFlowSamples.Ref), "ref int value",
            [typeof(int).MakeByRefType()], Shape(Int32Reference),
            "mss1:0(1:rp12:System.Int32)n"),
        new(
            "OutValue", nameof(ParameterPassingFlowSamples.Out), "int value",
            [typeof(int)], Shape(Int32Value),
            "mss1:0(1:vp12:System.Int32)n"),
        new(
            "OutReference", nameof(ParameterPassingFlowSamples.Out), "out int value",
            [typeof(int).MakeByRefType()], Shape(Int32Reference),
            "mss1:0(1:rp12:System.Int32)n"),
        new(
            "InValue", nameof(ParameterPassingFlowSamples.In), "int value",
            [typeof(int)], Shape(Int32Value),
            "mss1:0(1:vp12:System.Int32)n"),
        new(
            "InReference", nameof(ParameterPassingFlowSamples.In), "in int value",
            [typeof(int).MakeByRefType()], Shape(Int32Reference),
            "mss1:0(1:rp12:System.Int32)n"),
        new(
            "ReadOnlyValue", nameof(ParameterPassingFlowSamples.ReadOnly), "int value",
            [typeof(int)], Shape(Int32Value),
            "mss1:0(1:vp12:System.Int32)n"),
        new(
            "ReadOnlyReference", nameof(ParameterPassingFlowSamples.ReadOnly), "ref readonly int value",
            [typeof(int).MakeByRefType()], Shape(Int32Reference),
            "mss1:0(1:rp12:System.Int32)n"),
        new(
            "ArrayValue", nameof(ParameterPassingFlowSamples.Array), "int[] values",
            [typeof(int[])], Shape(Int32ArrayValue),
            "mss1:0(1:vz1:p12:System.Int32)n"),
        new(
            "ArrayReference", nameof(ParameterPassingFlowSamples.Array), "ref int[] values",
            [typeof(int[]).MakeByRefType()], Shape(Int32ArrayReference),
            "mss1:0(1:rz1:p12:System.Int32)n"),
        new(
            "LongArrayReference", nameof(ParameterPassingFlowSamples.Array), "ref long[] values",
            [typeof(long[]).MakeByRefType()], Shape(Int64ArrayReference),
            "mss1:0(1:rz1:p12:System.Int64)n"),
        new(
            "BothValues", nameof(ParameterPassingFlowSamples.Position), "int first, int second",
            [typeof(int), typeof(int)], Shape(Int32Value, Int32Value),
            "mss1:0(2:vp12:System.Int32vp12:System.Int32)n"),
        new(
            "FirstReference", nameof(ParameterPassingFlowSamples.Position), "ref int first, int second",
            [typeof(int).MakeByRefType(), typeof(int)], Shape(Int32Reference, Int32Value),
            "mss1:0(2:rp12:System.Int32vp12:System.Int32)n"),
        new(
            "SecondReference", nameof(ParameterPassingFlowSamples.Position), "int first, ref int second",
            [typeof(int), typeof(int).MakeByRefType()], Shape(Int32Value, Int32Reference),
            "mss1:0(2:vp12:System.Int32rp12:System.Int32)n"),
        new(
            "BothReferences", nameof(ParameterPassingFlowSamples.Position), "ref int first, ref int second",
            [typeof(int).MakeByRefType(), typeof(int).MakeByRefType()], Shape(Int32Reference, Int32Reference),
            "mss1:0(2:rp12:System.Int32rp12:System.Int32)n"),
    ];

    public static TheoryData<string> SpecimenNames =>
        new(Specimens.Select(specimen => specimen.Name));

    [Theory]
    [MemberData(nameof(SpecimenNames))]
    public void PassingFlow_PreservesRecordedStagesAndCandidate(string specimenName)
    {
        Specimen specimen = FindSpecimen(specimenName);
        using var peReader = OpenFixture();
        MethodDefinitionHandle expected = ExpectedHandle(specimen);
        var metadataCandidates = MetadataCandidates(peReader.GetMetadataReader(), specimen.MethodName);
        Assert.True(metadataCandidates.Length > 1);
        MemberSignatureShapeResult metadata = Assert.Single(
            metadataCandidates, candidate => candidate.Candidate == expected).Shape;
        MemberSignatureShapeResult restoredMetadata = AssertStages(metadata, specimen);
        var restoredMetadataCandidates = metadataCandidates
            .Select(candidate => (candidate.Candidate, Shape: Transport(candidate.Shape)))
            .ToArray();

        AssertUnique(
            MemberSignatureShapeMatcher.Match(restoredMetadata, restoredMetadataCandidates),
            expected);

        MemberSignatureShapeResult source = SourceShape(specimen);
        MemberSignatureShapeResult restoredSource = AssertStages(source, specimen);
        AssertUnique(MemberSignatureShapeMatcher.Match(source, metadataCandidates), expected);
        AssertUnique(
            MemberSignatureShapeMatcher.Match(restoredSource, restoredMetadataCandidates),
            expected);

        var sourceCandidates = Specimens
            .Where(candidate => candidate.MethodName == specimen.MethodName)
            .Select(candidate => (
                Candidate: candidate.Name,
                Shape: AssertStages(SourceShape(candidate), candidate)))
            .ToArray();
        AssertUnique(
            MemberSignatureShapeMatcher.Match(restoredMetadata, sourceCandidates),
            specimen.Name);
    }

    [Theory]
    [InlineData("out")]
    [InlineData("in")]
    [InlineData("ref readonly")]
    public void DirectionModifiers_AreErasedRatherThanUsedAsIdentity(string modifier)
    {
        Specimen specimen = FindSpecimen("RefReference");
        using var peReader = OpenFixture();
        MemberSignatureShapeResult metadata = AssertStages(
            MetadataMemberSignatureShape.Create(peReader.GetMetadataReader(), ExpectedHandle(specimen)),
            specimen);
        MemberSignatureShapeResult original = AssertStages(SourceShape(specimen), specimen);
        MemberSignatureShapeResult alternative = AssertStages(
            SourceMemberSignatureShape.Create(
                $"void Ref({modifier} int value);", SourceMemberSignatureKind.Method),
            specimen);

        AssertUnique(
            MemberSignatureShapeMatcher.Match(metadata, [("alternative", alternative)]),
            "alternative");

        // Alternative declarations, not a legal C# overload set differing only in direction.
        MemberSignatureCorrespondence<string> ambiguous = MemberSignatureShapeMatcher.Match(
            metadata, [("original", original), ("alternative", alternative)]);
        Assert.Equal(MemberSignatureCorrespondenceKind.Ambiguous, ambiguous.Kind);
        Assert.Equal(["original", "alternative"], ambiguous.Candidates);
        Assert.Null(ambiguous.Match);
        Assert.Null(ambiguous.UnavailableReason);
    }

    [Theory]
    [InlineData("RefReference", "RefValue")]
    [InlineData("RefValue", "RefReference")]
    public void OppositePassingCandidate_RemainsUnavailableAfterTransport(
        string targetName,
        string candidateName)
    {
        Specimen target = FindSpecimen(targetName);
        Specimen candidate = FindSpecimen(candidateName);
        using var peReader = OpenFixture();
        MethodDefinitionHandle candidateHandle = ExpectedHandle(candidate);
        MemberSignatureShapeResult source = AssertStages(SourceShape(target), target);
        MemberSignatureShapeResult metadata = AssertStages(
            MetadataMemberSignatureShape.Create(peReader.GetMetadataReader(), candidateHandle),
            candidate);

        MemberSignatureCorrespondence<MethodDefinitionHandle> result = MemberSignatureShapeMatcher.Match(
            source, [(candidateHandle, metadata)]);

        Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, result.Kind);
        Assert.True(result.Match.IsNil);
        Assert.Empty(result.Candidates);
        Assert.False(string.IsNullOrWhiteSpace(result.UnavailableReason));
    }

    static (MethodDefinitionHandle Candidate, MemberSignatureShapeResult Shape)[] MetadataCandidates(
        MetadataReader reader,
        string methodName)
    {
        var typeHandle = (TypeDefinitionHandle)MetadataTokens.EntityHandle(typeof(ParameterPassingFlowSamples).MetadataToken);
        return reader.GetTypeDefinition(typeHandle).GetMethods()
            .Where(handle => reader.StringComparer.Equals(reader.GetMethodDefinition(handle).Name, methodName))
            .Select(handle => (
                Candidate: handle,
                Shape: MetadataMemberSignatureShape.Create(reader, handle)))
            .ToArray();
    }

    static MethodDefinitionHandle ExpectedHandle(Specimen specimen)
    {
        // Reflection locates the compiled overload independently of the shape projection.
        MethodInfo method = typeof(ParameterPassingFlowSamples)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Single(method => method.Name == specimen.MethodName
                && method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(specimen.RuntimeParameterTypes));
        return (MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MetadataToken);
    }

    static MemberSignatureShapeResult SourceShape(Specimen specimen) =>
        SourceMemberSignatureShape.Create(
            $"void {specimen.MethodName}({specimen.Parameters});",
            SourceMemberSignatureKind.Method);

    static MemberSignatureShapeResult AssertStages(MemberSignatureShapeResult result, Specimen specimen)
    {
        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Null(result.UnavailableReason);
        Assert.Equal(specimen.ExpectedShape, result.Shape);
        string text = MemberSignatureShapeCodec.Encode(result.Shape!);
        Assert.Equal(specimen.ExpectedTransport, text);
        MemberSignatureShapeResult restored = MemberSignatureShapeCodec.Decode(text);
        Assert.True(restored.IsAvailable, restored.UnavailableReason);
        Assert.Null(restored.UnavailableReason);
        Assert.Equal(specimen.ExpectedShape, restored.Shape);
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

    static Specimen FindSpecimen(string name) => Specimens.Single(specimen => specimen.Name == name);

    static PEReader OpenFixture() =>
        new(File.OpenRead(typeof(ParameterPassingFlowSamples).Assembly.Location));

    static MemberSignatureShape Shape(params MemberParameterSignatureShape[] parameters) =>
        new(0, new(parameters));

    sealed record Specimen(
        string Name,
        string MethodName,
        string Parameters,
        Type[] RuntimeParameterTypes,
        MemberSignatureShape ExpectedShape,
        string ExpectedTransport);
}
