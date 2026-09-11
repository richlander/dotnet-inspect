using CSharpText;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class MemberSignatureShapeFlowTests
{
    static readonly TypeSignatureShape Int32 = new PrimitiveTypeSignatureShape("System.Int32");
    static readonly TypeSignatureShape String = new PrimitiveTypeSignatureShape("System.String");
    static readonly TypeSignatureShape MethodParameter =
        new GenericParameterTypeSignatureShape(SignatureGenericParameterKind.Method, 0);

    static readonly Specimen[] Specimens =
    [
        new(
            "Vector", 1, "void M(int[] value);",
            Shape(new ArrayTypeSignatureShape(Int32, 1, IsSzArray: true)),
            "mss1:0(1:vz1:p12:System.Int32)n"),
        new(
            "RankOneNonSz", 2, null,
            Shape(new ArrayTypeSignatureShape(Int32, 1, IsSzArray: false)),
            "mss1:0(1:va1:p12:System.Int32)n"),
        new(
            "RankTwo", 3, "void M(int[,] value);",
            Shape(new ArrayTypeSignatureShape(Int32, 2, IsSzArray: false)),
            "mss1:0(1:va2:p12:System.Int32)n"),
        new(
            "VectorOfRankTwo", 4, "void M(int[][,] value);",
            Shape(new ArrayTypeSignatureShape(
                new ArrayTypeSignatureShape(Int32, 2, IsSzArray: false),
                1, IsSzArray: true)),
            "mss1:0(1:vz1:a2:p12:System.Int32)n"),
        new(
            "RankTwoOfVector", 5, "void M(int[,][] value);",
            Shape(new ArrayTypeSignatureShape(
                new ArrayTypeSignatureShape(Int32, 1, IsSzArray: true),
                2, IsSzArray: false)),
            "mss1:0(1:va2:z1:p12:System.Int32)n"),
        new(
            "GenericVector", 6, "void M<TItem>(TItem[] value);",
            Shape(new ArrayTypeSignatureShape(MethodParameter, 1, IsSzArray: true), genericArity: 1),
            "mss1:1(1:vz1:m0;)n"),
        new(
            "GenericRankTwo", 7, "void M<TItem>(TItem[,] value);",
            Shape(new ArrayTypeSignatureShape(MethodParameter, 2, IsSzArray: false), genericArity: 1),
            "mss1:1(1:va2:m0;)n"),
        new(
            "Tuple", 8, "void M((int count, string text) value);",
            Shape(new TupleTypeSignatureShape(new([Int32, String]))),
            "mss1:0(1:vu2:p12:System.Int32p13:System.String)n"),
        new(
            "TupleReversed", 9, "void M((string text, int count) value);",
            Shape(new TupleTypeSignatureShape(new([String, Int32]))),
            "mss1:0(1:vu2:p13:System.Stringp12:System.Int32)n"),
    ];

    public static TheoryData<string> SpecimenNames =>
        new(Specimens.Select(specimen => specimen.Name));

    [Theory]
    [MemberData(nameof(SpecimenNames))]
    public void ShapeFlow_MatchesRecordedStagesAndCandidates(string specimenName)
    {
        Specimen specimen = FindSpecimen(specimenName);
        using var peReader = OpenFixture();
        MetadataReader reader = peReader.GetMetadataReader();
        var expectedHandle = MetadataTokens.MethodDefinitionHandle(specimen.MethodRow);
        var metadataCandidates = reader.MethodDefinitions
            .Select(handle => (
                Candidate: handle,
                Shape: MetadataMemberSignatureShape.Create(reader, handle)))
            .ToArray();
        foreach (var candidate in metadataCandidates)
            Assert.Equal("M", reader.GetString(reader.GetMethodDefinition(candidate.Candidate).Name));

        MemberSignatureShapeResult metadata = Assert.Single(
            metadataCandidates,
            candidate => candidate.Candidate == expectedHandle).Shape;
        MemberSignatureShapeResult restoredMetadata = AssertStages(metadata, specimen);
        var transportedMetadataCandidates = metadataCandidates
            .Select(candidate => (candidate.Candidate, Shape: Transport(candidate.Shape)))
            .ToArray();

        AssertUnique(
            MemberSignatureShapeMatcher.Match(restoredMetadata, transportedMetadataCandidates),
            expectedHandle);

        var sourceCandidates = Specimens
            .Where(candidate => candidate.Source is not null)
            .Select(candidate => (
                Candidate: candidate.Name,
                Shape: AssertStages(
                    SourceMemberSignatureShape.Create(candidate.Source!, SourceMemberSignatureKind.Method),
                    candidate)))
            .ToArray();
        MemberSignatureCorrespondence<string> sourceMatch =
            MemberSignatureShapeMatcher.Match(restoredMetadata, sourceCandidates);
        if (specimen.Source is null)
        {
            // ECMA-335 rank-one ARRAY has no ordinary C# declaration syntax.
            AssertUnavailable(sourceMatch);
            return;
        }

        AssertUnique(sourceMatch, specimen.Name);
        MemberSignatureShapeResult source = SourceMemberSignatureShape.Create(
            specimen.Source, SourceMemberSignatureKind.Method);
        MemberSignatureShapeResult restoredSource = AssertStages(source, specimen);
        AssertUnique(
            MemberSignatureShapeMatcher.Match(source, metadataCandidates),
            expectedHandle);
        AssertUnique(
            MemberSignatureShapeMatcher.Match(restoredSource, transportedMetadataCandidates),
            expectedHandle);
    }

    [Theory]
    [InlineData("GenericVector", "void M<TRenamed>(TRenamed[] items);")]
    [InlineData("Tuple", "void M((int number, string label) item);")]
    public void ErasedNames_PreserveUniqueAndAmbiguousCorrespondenceAfterTransport(
        string specimenName,
        string renamedDeclaration)
    {
        Specimen specimen = FindSpecimen(specimenName);
        using var peReader = OpenFixture();
        MemberSignatureShapeResult target = AssertStages(
            MetadataMemberSignatureShape.Create(
                peReader.GetMetadataReader(),
                MetadataTokens.MethodDefinitionHandle(specimen.MethodRow)),
            specimen);
        MemberSignatureShapeResult original = AssertStages(
            SourceMemberSignatureShape.Create(specimen.Source!, SourceMemberSignatureKind.Method),
            specimen);
        MemberSignatureShapeResult renamed = AssertStages(
            SourceMemberSignatureShape.Create(renamedDeclaration, SourceMemberSignatureKind.Method),
            specimen);
        Specimen neighbor = FindSpecimen(specimenName == "Tuple" ? "TupleReversed" : "GenericRankTwo");
        MemberSignatureShapeResult different = AssertStages(
            SourceMemberSignatureShape.Create(neighbor.Source!, SourceMemberSignatureKind.Method),
            neighbor);

        AssertUnique(
            MemberSignatureShapeMatcher.Match(target, [("different", different), ("renamed", renamed)]),
            "renamed");
        MemberSignatureCorrespondence<string> ambiguous = MemberSignatureShapeMatcher.Match(
            target,
            [("original", original), ("different", different), ("renamed", renamed)]);
        Assert.Equal(MemberSignatureCorrespondenceKind.Ambiguous, ambiguous.Kind);
        Assert.Equal(["original", "renamed"], ambiguous.Candidates);
        Assert.Null(ambiguous.Match);
        Assert.Null(ambiguous.UnavailableReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableSourceSibling_PreventsUniqueMatchAfterTransport(bool unavailableFirst)
    {
        Specimen specimen = FindSpecimen("Vector");
        using var peReader = OpenFixture();
        MemberSignatureShapeResult target = AssertStages(
            MetadataMemberSignatureShape.Create(
                peReader.GetMetadataReader(),
                MetadataTokens.MethodDefinitionHandle(specimen.MethodRow)),
            specimen);
        MemberSignatureShapeResult matching = AssertStages(
            SourceMemberSignatureShape.Create(specimen.Source!, SourceMemberSignatureKind.Method),
            specimen);
        MemberSignatureShapeResult unavailable = SourceMemberSignatureShape.Create(
            "void M(VectorAlias value);", SourceMemberSignatureKind.Method);
        Assert.False(unavailable.IsAvailable);
        Assert.Null(unavailable.Shape);
        Assert.Contains("not globally qualified", unavailable.UnavailableReason);

        // Refusal has no shape to encode; retain it rather than dropping the candidate.
        (string Candidate, MemberSignatureShapeResult Shape)[] candidates = unavailableFirst
            ? [("unavailable", unavailable), ("matching", matching)]
            : [("matching", matching), ("unavailable", unavailable)];
        MemberSignatureCorrespondence<string> result = MemberSignatureShapeMatcher.Match(target, candidates);
        AssertUnavailable(result);
        Assert.Equal(unavailable.UnavailableReason, result.UnavailableReason);
    }

    [Fact]
    public void NonSzMetadataNotation_IsNotAcceptedAsCSharpSource()
    {
        MemberSignatureShapeResult source = SourceMemberSignatureShape.Create(
            "void M(int[*] value);", SourceMemberSignatureKind.Method);

        Assert.False(source.IsAvailable);
        Assert.Null(source.Shape);
        Assert.False(string.IsNullOrWhiteSpace(source.UnavailableReason));
    }

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

    static void AssertUnavailable(MemberSignatureCorrespondence<string> result)
    {
        Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, result.Kind);
        Assert.Null(result.Match);
        Assert.Empty(result.Candidates);
        Assert.False(string.IsNullOrWhiteSpace(result.UnavailableReason));
    }

    static Specimen FindSpecimen(string name) => Specimens.Single(specimen => specimen.Name == name);

    static PEReader OpenFixture() =>
        new(new MemoryStream(ArrayKindSignatureFixture.BuildShapeCorrespondenceImage(), writable: false));

    static MemberSignatureShape Shape(TypeSignatureShape parameter, int genericArity = 0) =>
        new(genericArity, new([new MemberParameterSignatureShape(ParameterPassingKind.Value, parameter)]));

    sealed record Specimen(
        string Name,
        int MethodRow,
        string? Source,
        MemberSignatureShape ExpectedShape,
        string ExpectedTransport);
}
