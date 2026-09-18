using ILInspector.Decompiler.Annotations;

namespace ILInspector.Decompiler.Tests;

public class CSharpAnnotatedSourceProjectionTests
{
    [Fact]
    public void Create_PreservesRetainedPlanesAndExactUtf16Coordinates()
    {
        const string text = "😀A\r\nIL_0000: nop\r\nB\nIL_0001: ret\nC";
        int firstIl = text.IndexOf("IL_0000", StringComparison.Ordinal);
        int secondCSharp = text.IndexOf("B", StringComparison.Ordinal);
        int secondIl = text.IndexOf("IL_0001", StringComparison.Ordinal);
        int finalCSharp = text.LastIndexOf("C", StringComparison.Ordinal);
        var source = new AnnotatedSourceDocumentSource(
            "Tests",
            new Guid("00112233-4455-6677-8899-AABBCCDDEEFF"),
            0x06000001,
            new string('A', 64),
            "Tests.M()");
        var provenance = new AnnotatedSourceNodeProvenance([0, 1]);
        var document = new AnnotatedSourceDocument(
            text,
            [
                new AnnotatedSourceNode(
                    0,
                    "Block",
                    SourceLineKind.CSharp,
                    [
                        new(0, 5),
                        new(secondCSharp, 2),
                    ],
                    Provenance: provenance),
                new AnnotatedSourceNode(
                    1,
                    AnnotatedSourceNode.InstructionKind,
                    SourceLineKind.Il,
                    [new(firstIl, "IL_0000: nop".Length)],
                    0),
                new AnnotatedSourceNode(
                    2,
                    "IdentifierName",
                    SourceLineKind.CSharp,
                    [new(finalCSharp, 1)],
                    Provenance: new AnnotatedSourceNodeProvenance([1])),
                new AnnotatedSourceNode(
                    3,
                    AnnotatedSourceNode.InstructionKind,
                    SourceLineKind.Il,
                    [new(secondIl, "IL_0001: ret".Length)],
                    1),
            ],
            [
                new AnnotatedSourceRegion(
                    PrintedRegionRole.Body,
                    [
                        new(0, 5),
                        new(secondCSharp, 2),
                    ]),
            ],
            [
                new AnnotatedSourceFact(
                    0,
                    "cost.callee",
                    "Performance",
                    AnnotationConditionality.Always,
                    "mixed target",
                    0,
                    AnnotatedSourceFactOrigin.Body),
                new AnnotatedSourceFact(
                    1,
                    "safety.callee",
                    "Safety",
                    AnnotationConditionality.Always,
                    "IL only",
                    1,
                    AnnotatedSourceFactOrigin.Body),
                new AnnotatedSourceFact(
                    2,
                    "unanchored.body",
                    "Evidence",
                    AnnotationConditionality.Always,
                    null,
                    -1,
                    AnnotatedSourceFactOrigin.Body),
                new AnnotatedSourceFact(
                    3,
                    "member.header",
                    "Evidence",
                    AnnotationConditionality.Always,
                    null,
                    -1,
                    AnnotatedSourceFactOrigin.MemberHeader),
            ],
            [
                new AnnotatedSourceTarget(0, 1),
                new AnnotatedSourceTarget(1, 3),
                new AnnotatedSourceTarget(0, 0),
            ],
            source);

        var projection = CSharpAnnotatedSourceProjection.Create(document);

        Assert.Equal("😀A\r\nB\nC", projection.Document.Text);
        Assert.Equal(source, projection.Document.Source);
        Assert.Equal(2, projection.OriginalToProjectedNodeIds.Count);
        Assert.Equal(0, projection.OriginalToProjectedNodeIds[0]);
        Assert.Equal(1, projection.OriginalToProjectedNodeIds[2]);
        Assert.Collection(
            projection.Document.Nodes,
            node =>
            {
                Assert.Equal(0, node.Id);
                Assert.Equal(SourceLineKind.CSharp, node.Medium);
                Assert.Equal(provenance, node.Provenance);
                Assert.Equal([new AnnotatedSourceSpan(0, 7)], node.Spans);
            },
            node =>
            {
                Assert.Equal(1, node.Id);
                Assert.Equal([new AnnotatedSourceSpan(7, 1)], node.Spans);
            });
        Assert.Collection(
            projection.Document.Regions,
            region => Assert.Equal([new AnnotatedSourceSpan(0, 7)], region.Spans));
        Assert.Collection(
            projection.Document.Facts,
            fact =>
            {
                Assert.Equal(0, fact.Id);
                Assert.Equal("cost.callee", fact.Descriptor);
            },
            fact =>
            {
                Assert.Equal(1, fact.Id);
                Assert.Equal("unanchored.body", fact.Descriptor);
            },
            fact =>
            {
                Assert.Equal(2, fact.Id);
                Assert.Equal("member.header", fact.Descriptor);
            });
        Assert.Equal([new AnnotatedSourceTarget(0, 0)], projection.Document.Targets);
    }

    [Fact]
    public void Create_CSharpOnlyDocumentPreservesDocumentAndIdentity()
    {
        var document = new AnnotatedSourceDocument(
            "return;",
            [
                new AnnotatedSourceNode(
                    0,
                    "ReturnStatement",
                    SourceLineKind.CSharp,
                    [new(0, 7)]),
            ],
            [],
            [],
            []);

        var projection = CSharpAnnotatedSourceProjection.Create(document);

        Assert.Same(document, projection.Document);
        Assert.Single(projection.OriginalToProjectedNodeIds);
        Assert.Equal(0, projection.OriginalToProjectedNodeIds[0]);
    }

    [Fact]
    public void Create_UsesUnionOfIlNodesToProveCompleteLineOwnership()
    {
        const string text = "value;\nIL_0000: nop\nreturn;";
        int il = text.IndexOf("IL_0000", StringComparison.Ordinal);
        int result = text.IndexOf("return;", StringComparison.Ordinal);
        var document = new AnnotatedSourceDocument(
            text,
            [
                new AnnotatedSourceNode(0, "ExpressionStatement", SourceLineKind.CSharp, [new(0, 6)]),
                new AnnotatedSourceNode(1, "IlPrefix", SourceLineKind.Il, [new(il, 5)]),
                new AnnotatedSourceNode(2, "IlSuffix", SourceLineKind.Il, [new(il + 5, 7)]),
                new AnnotatedSourceNode(3, "ReturnStatement", SourceLineKind.CSharp, [new(result, 7)]),
            ],
            [],
            [],
            []);

        var projection = CSharpAnnotatedSourceProjection.Create(document);

        Assert.Equal("value;\nreturn;", projection.Document.Text);
        Assert.Equal(0, projection.OriginalToProjectedNodeIds[0]);
        Assert.Equal(1, projection.OriginalToProjectedNodeIds[3]);
    }

    [Fact]
    public void Create_PreservesStandaloneCrTerminator()
    {
        const string text = "A\rIL\rB";
        var document = new AnnotatedSourceDocument(
            text,
            [
                new AnnotatedSourceNode(0, "IdentifierName", SourceLineKind.CSharp, [new(0, 1)]),
                new AnnotatedSourceNode(1, "IlLine", SourceLineKind.Il, [new(2, 2)]),
                new AnnotatedSourceNode(2, "IdentifierName", SourceLineKind.CSharp, [new(5, 1)]),
            ],
            [],
            [],
            []);

        var projection = CSharpAnnotatedSourceProjection.Create(document);

        Assert.Equal("A\rB", projection.Document.Text);
        Assert.Equal(0, projection.OriginalToProjectedNodeIds[0]);
        Assert.Equal(1, projection.OriginalToProjectedNodeIds[2]);
    }

    [Fact]
    public void Create_RejectsPartialIlLineOwnership()
    {
        const string text = "IL_0000: nop";
        var document = new AnnotatedSourceDocument(
            text,
            [new AnnotatedSourceNode(0, "IlFragment", SourceLineKind.Il, [new(0, text.Length - 1)])],
            [],
            [],
            []);

        var error = Assert.Throws<InvalidOperationException>(
            () => CSharpAnnotatedSourceProjection.Create(document));

        Assert.Contains("partial or mixed line ownership", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RejectsRetainedStructureOnRemovedIlText()
    {
        const string text = "value;\nIL_0000: nop";
        int il = text.IndexOf("IL_0000", StringComparison.Ordinal);
        var document = new AnnotatedSourceDocument(
            text,
            [
                new AnnotatedSourceNode(0, "ExpressionStatement", SourceLineKind.CSharp, [new(0, text.Length)]),
                new AnnotatedSourceNode(1, "IlLine", SourceLineKind.Il, [new(il, "IL_0000: nop".Length)]),
            ],
            [],
            [],
            []);

        var error = Assert.Throws<InvalidOperationException>(
            () => CSharpAnnotatedSourceProjection.Create(document));

        Assert.Contains("cannot be clipped", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RejectsRegionOnRemovedIlText()
    {
        const string text = "value;\nIL_0000: nop";
        int il = text.IndexOf("IL_0000", StringComparison.Ordinal);
        var document = new AnnotatedSourceDocument(
            text,
            [
                new AnnotatedSourceNode(0, "ExpressionStatement", SourceLineKind.CSharp, [new(0, 6)]),
                new AnnotatedSourceNode(1, "IlLine", SourceLineKind.Il, [new(il, "IL_0000: nop".Length)]),
            ],
            [new AnnotatedSourceRegion(PrintedRegionRole.Body, [new(0, text.Length)])],
            [],
            []);

        var error = Assert.Throws<InvalidOperationException>(
            () => CSharpAnnotatedSourceProjection.Create(document));

        Assert.Contains("Body region", error.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be clipped", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RejectsIlSpanThatSelectsLineTerminator()
    {
        const string text = "IL_0000: nop\nreturn;";
        int csharpStart = "IL_0000: nop\n".Length;
        var document = new AnnotatedSourceDocument(
            text,
            [
                new AnnotatedSourceNode(
                    0,
                    "IlLine",
                    SourceLineKind.Il,
                    [new(0, csharpStart)]),
                new AnnotatedSourceNode(
                    1,
                    "ReturnStatement",
                    SourceLineKind.CSharp,
                    [new(csharpStart, 7)]),
            ],
            [],
            [],
            []);

        var error = Assert.Throws<InvalidOperationException>(
            () => CSharpAnnotatedSourceProjection.Create(document));

        Assert.Contains("line terminator", error.Message, StringComparison.Ordinal);
    }
}
