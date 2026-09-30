using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class PdbSourceProvenancePathTests
{
    [Theory]
    [InlineData(
        "/repo/artifacts/obj/App/Debug/net11.0/generated/"
            + "System.Text.Json.SourceGeneration/"
            + "System.Text.Json.SourceGeneration.JsonSourceGenerator/"
            + "Context.Model.g.cs")]
    [InlineData(
        @"C:\repo\obj\Debug\net11.0\generated\Markout.SourceGeneration\"
            + @"Markout.SourceGeneration.MarkoutSourceGenerator\"
            + "Model.Markout.g.cs")]
    public void ClassifyEmbeddedDocument_RetainsExactGeneratorEvidence(
        string path)
    {
        var result = Assert.IsType<PdbGeneratedPathClassification.Generated>(
            PdbSourceProvenancePathClassifier.ClassifyEmbeddedDocument(path));

        PdbGeneratedPathEvidence evidence = result.Evidence;
        Assert.Equal(
            evidence.GeneratorAssembly.ToString(),
            evidence.GeneratorAssemblySpan.SliceRaw(path));
        Assert.Equal(
            evidence.GeneratorType.ToString(),
            evidence.GeneratorTypeSpan.SliceRaw(path));
        Assert.Equal(
            evidence.HintName.ToString(),
            evidence.HintNameSpan.SliceRaw(path));
        Assert.EndsWith("Generator", evidence.GeneratorType.ToString());
        Assert.EndsWith(".g.cs", evidence.HintName.ToString());
    }

    [Fact]
    public void ClassifyEmbeddedDocument_RequiresIntermediateOutputRoot()
    {
        const string Path =
            "/repo/generated/Tool/Tool.ExampleGenerator/Hint.g.cs";

        AssertUnknown(
            Path,
            PdbGeneratedPathUnknownReason.NoEligibleDecomposition);
    }

    [Theory]
    [InlineData("/repo/obj//Tool/Tool.ExampleGenerator/Hint.g.cs",
        PdbGeneratedPathUnknownReason.EmptySegment)]
    [InlineData("/repo/obj/../Tool/Tool.ExampleGenerator/Hint.g.cs",
        PdbGeneratedPathUnknownReason.DotSegment)]
    [InlineData("/repo/obj/Tool/Tool.ExampleGenerator/../Hint.g.cs",
        PdbGeneratedPathUnknownReason.DotSegment)]
    public void ClassifyEmbeddedDocument_RejectsTraversalLikeShapes(
        string path,
        PdbGeneratedPathUnknownReason reason)
    {
        AssertUnknown(path, reason);
    }

    [Theory]
    [InlineData("/repo/obj/Tool/Tool.ExampleGenerator/Bad:Hint.cs")]
    [InlineData("/repo/obj/Tool/Tool.ExampleGenerator/Bad*.cs")]
    [InlineData("/repo/obj/Tool/Tool.ExampleGenerator/Dir /Hint.cs")]
    [InlineData("/repo/obj/Tool/Tool.ExampleGenerator/ \t")]
    public void ClassifyEmbeddedDocument_RejectsInvalidRoslynHintNames(
        string path)
    {
        AssertUnknown(
            path,
            PdbGeneratedPathUnknownReason.InvalidHintName);
    }

    [Theory]
    [InlineData("/repo/obj/Tool/Tool.ExampleGenerator/My File.g.cs")]
    [InlineData("/repo/obj/Tool/Tool.ExampleGenerator/Dir/Hint-{A}.g.cs")]
    [InlineData("/repo/obj/Tool/Tool.ExampleGenerator/Δ/Hint.g.cs")]
    public void ClassifyEmbeddedDocument_AcceptsRoslynHintNameCharacters(
        string path)
    {
        Assert.IsType<PdbGeneratedPathClassification.Generated>(
            PdbSourceProvenancePathClassifier.ClassifyEmbeddedDocument(path));
    }

    [Fact]
    public void ClassifyEmbeddedDocument_RejectsAmbiguousDecomposition()
    {
        const string Path =
            "/repo/obj/A/A.FirstGenerator/B/B.SecondGenerator/Hint.g.cs";

        AssertUnknown(
            Path,
            PdbGeneratedPathUnknownReason.AmbiguousDecomposition);
    }

    [Fact]
    public void ClassifyEmbeddedDocument_EnforcesCharacterBound()
    {
        const string Path = "/repo/obj/Tool/Tool.ExampleGenerator/Hint.g.cs";

        PdbGeneratedPathClassification result =
            PdbSourceProvenancePathClassifier.ClassifyEmbeddedDocument(
                Path,
                limits: new(maxCharacters: Path.Length - 1));

        Assert.Equal(
            PdbGeneratedPathUnknownReason.CharacterLimitExceeded,
            Assert.IsType<PdbGeneratedPathClassification.Unknown>(result)
                .Reason);
    }

    [Fact]
    public void ClassifyEmbeddedDocument_EnforcesSegmentBound()
    {
        const string Path = "/repo/obj/Tool/Tool.ExampleGenerator/Hint.g.cs";

        PdbGeneratedPathClassification result =
            PdbSourceProvenancePathClassifier.ClassifyEmbeddedDocument(
                Path,
                limits: new(maxSegments: 4));

        Assert.Equal(
            PdbGeneratedPathUnknownReason.SegmentLimitExceeded,
            Assert.IsType<PdbGeneratedPathClassification.Unknown>(result)
                .Reason);
    }

    private static void AssertUnknown(
        string path,
        PdbGeneratedPathUnknownReason reason)
    {
        PdbGeneratedPathClassification result =
            PdbSourceProvenancePathClassifier.ClassifyEmbeddedDocument(path);

        Assert.Equal(
            reason,
            Assert.IsType<PdbGeneratedPathClassification.Unknown>(result)
                .Reason);
    }
}
