using System.Text.Json;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Capture evidence is the one fact about a recovered closure that cannot be
/// recovered from its rendered text: after the raising passes substitute the
/// captured values back in and erase the <c>&lt;&gt;c__DisplayClass</c>, a
/// captured read is spelled exactly like any other variable read. These pin the
/// producer's answer from the pass that discovers it, through the
/// <see cref="PrintedBodyMap"/> seam that binds it to printed node ids, against
/// compiler-produced closures.
/// </summary>
[Trait("Area", "Printer")]
public class CaptureEvidenceTests
{
    static PrintedBodyMap Map(Type fixtureType, string methodName)
    {
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(
            function!,
            out var ranges,
            importMethodBody: method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        function!.CheckInvariant();
        return PrintedBodyMap.Create(ranges);
    }

    static string Text(PrintedBodyMap map, int nodeId)
    {
        var extent = map.Nodes[nodeId].Extent;
        if (extent.StartLine == extent.EndLine)
            return map.Lines[extent.StartLine][extent.StartColumn..extent.EndColumn];

        var selected = new List<string> { map.Lines[extent.StartLine][extent.StartColumn..] };
        for (int line = extent.StartLine + 1; line < extent.EndLine; line++)
            selected.Add(map.Lines[line]);
        selected.Add(map.Lines[extent.EndLine][..extent.EndColumn]);
        return string.Join('\n', selected);
    }

    static string[] Uses(PrintedBodyMap map, PrintedCapture capture)
        => [.. capture.UseNodeIds.Select(use => Text(map, use))];

    [Fact]
    public void CapturingLambda_BindsEveryCapturedVariableToThePrintedNamesThatReadIt()
    {
        var map = Map(typeof(CaptureEvidenceFixture), nameof(CaptureEvidenceFixture.TwoCaptureLambda));

        Assert.Equal(
            "return x => x * first - second + (second - first);",
            string.Join("\n", map.Lines).Trim());
        Assert.Equal(2, map.Captures.Count);

        // One parent, and it is the lambda itself -- not the return statement it
        // sits in, and not a wrapper that happens to share its characters.
        int parent = Assert.Single(map.Captures.Select(capture => capture.ParentNodeId).Distinct());
        Assert.Equal(AnnotatedSourceNodeKinds.LambdaExpression, map.Nodes[parent].Kind);
        Assert.Equal("x => x * first - second + (second - first)", Text(map, parent));

        // Names come from the host's own argument spelling, never from the
        // display-class field the compiler hoisted them into.
        Assert.Equal(["first", "second"], map.Captures.Select(capture => capture.DisplayName));
        Assert.DoesNotContain(map.Captures, capture => capture.DisplayName.Contains('<'));

        // Each captured parameter is read twice, under different sub-expressions,
        // so both reads are addressable and both must be named -- as the exact
        // characters of that variable, not merely as some node.
        foreach (var capture in map.Captures)
        {
            Assert.Equal([capture.DisplayName, capture.DisplayName], Uses(map, capture));
            Assert.Equal(capture.UseNodeIds.Order(), capture.UseNodeIds);
            Assert.All(capture.UseNodeIds, use => Assert.Equal(
                AnnotatedSourceNodeKinds.NameExpression,
                map.Nodes[use].Kind));
            Assert.All(capture.UseNodeIds, use =>
                Assert.True(Contains(map.Nodes[parent].Extent, map.Nodes[use].Extent)));
        }

        // The two rows must not describe the same characters: `first` and
        // `second` are different variables at different places.
        Assert.Empty(map.Captures[0].UseNodeIds.Intersect(map.Captures[1].UseNodeIds));
    }

    [Fact]
    public void CapturingLocalFunction_BindsEveryCapturedVariableToThePrintedNamesThatReadIt()
    {
        var map = Map(
            typeof(CaptureEvidenceFixture),
            nameof(CaptureEvidenceFixture.TwoCaptureLocalFunction));

        Assert.Equal(2, map.Captures.Count);
        int parent = Assert.Single(map.Captures.Select(capture => capture.ParentNodeId).Distinct());
        Assert.Equal(AnnotatedSourceNodeKinds.LocalFunctionStatement, map.Nodes[parent].Kind);
        Assert.Equal(
            "int Combine(int v) => v * first - second + (second - first);",
            Text(map, parent));

        Assert.Equal(["first", "second"], map.Captures.Select(capture => capture.DisplayName));
        foreach (var capture in map.Captures)
        {
            Assert.Equal([capture.DisplayName, capture.DisplayName], Uses(map, capture));
            Assert.All(capture.UseNodeIds, use => Assert.Equal(
                AnnotatedSourceNodeKinds.NameExpression,
                map.Nodes[use].Kind));
            Assert.All(capture.UseNodeIds, use =>
                Assert.True(Contains(map.Nodes[parent].Extent, map.Nodes[use].Extent)));
        }
    }

    // The boundary the printer imposes, stated as behavior rather than left to
    // be discovered: a name repeated inside one window owns no characters in any
    // projection, so the row names the read it can address and nothing else.
    [Fact]
    public void RepeatedUseInsideOneStatement_NamesTheAddressableReadOnly()
    {
        var map = Map(
            typeof(CaptureEvidenceFixture),
            nameof(CaptureEvidenceFixture.RepeatedUseInOneStatementLambda));

        Assert.Equal(
            "return x => x * only + only;",
            string.Join("\n", map.Lines).Trim());

        var capture = Assert.Single(map.Captures);
        Assert.Equal("only", capture.DisplayName);
        Assert.Equal(AnnotatedSourceNodeKinds.LambdaExpression, map.Nodes[capture.ParentNodeId].Kind);
        Assert.Equal("only", Text(map, Assert.Single(capture.UseNodeIds)));

        // The unnamed second read is unaddressable for everyone, not dropped
        // here: no node covers it at all.
        Assert.Single(
            map.Nodes,
            node => node.Kind == AnnotatedSourceNodeKinds.NameExpression
                && Text(map, node.Id) == "only");
    }

    [Theory]
    [InlineData(nameof(CaptureEvidenceFixture.NonCapturingLambda))]
    [InlineData(nameof(CaptureEvidenceFixture.StaticLocalFunction))]
    public void NonCapturingNestedFunction_RecordsNoCapture(string methodName)
    {
        var map = Map(typeof(CaptureEvidenceFixture), methodName);

        // The nested function is present and raised -- the empty capture set is
        // the producer's answer, not a missing projection.
        Assert.Contains(
            map.Nodes,
            node => node.Kind is AnnotatedSourceNodeKinds.LambdaExpression
                or AnnotatedSourceNodeKinds.LocalFunctionStatement);
        Assert.Empty(map.Captures);
    }

    // Two lambdas over one hoisted variable: each row must name its own lambda,
    // or a consumer would light up the wrong closure.
    [Fact]
    public void SharedEnvironmentLambdas_BindEachCaptureToItsOwnLambda()
    {
        var map = Map(typeof(CfgSampleClass), nameof(CfgSampleClass.SharedCaptureLambdas));

        Assert.Equal(2, map.Captures.Count);
        Assert.All(map.Captures, capture =>
        {
            Assert.Equal("n", capture.DisplayName);
            Assert.Equal(
                AnnotatedSourceNodeKinds.LambdaExpression,
                map.Nodes[capture.ParentNodeId].Kind);
            Assert.Equal("n", Text(map, Assert.Single(capture.UseNodeIds)));
        });

        Assert.Equal(
            2,
            map.Captures.Select(capture => capture.ParentNodeId).Distinct().Count());
        Assert.True(map.Captures[0].ParentNodeId < map.Captures[1].ParentNodeId);
        Assert.Contains("=>", Text(map, map.Captures[0].ParentNodeId));
        Assert.Contains("=>", Text(map, map.Captures[1].ParentNodeId));
    }

    [Fact]
    public void PrintedCaptures_RejectMalformedReferences()
    {
        var lines = new[] { "x => x + n;" };
        PrintedNodeSpan[] nodes =
        [
            new(0, AnnotatedSourceNodeKinds.LambdaExpression, new PrintedExtent(0, 0, 0, 10)),
            new(1, AnnotatedSourceNodeKinds.NameExpression, new PrintedExtent(0, 9, 0, 10)),
        ];

        // A parent that is not a nested function.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            lines, nodes, [], [], [new PrintedCapture(1, "n", [1])]));

        // A use node that does not exist.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            lines, nodes, [], [], [new PrintedCapture(0, "n", [7])]));

        // A use node that is not a printed name.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            lines, nodes, [], [], [new PrintedCapture(0, "n", [0])]));

        // A capture with no use is not evidence of anything.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            lines, nodes, [], [], [new PrintedCapture(0, "n", [])]));

        // Repeated use ids would make "which names are this variable"
        // unanswerable from the payload.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            lines, nodes, [], [], [new PrintedCapture(0, "n", [1, 1])]));

        // A display name must be the exact text its use selected.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            lines, nodes, [], [], [new PrintedCapture(0, "x", [1])]));

        // A valid name elsewhere in the body is not a use by this lambda.
        PrintedNodeSpan[] outsideNodes =
        [
            .. nodes,
            new(2, AnnotatedSourceNodeKinds.NameExpression, new PrintedExtent(0, 12, 0, 13)),
        ];
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["x => x + n; n"],
            outsideNodes,
            [],
            [],
            [new PrintedCapture(0, "n", [2])]));

        // One parent cannot capture the same name twice.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            lines,
            nodes,
            [],
            [],
            [new PrintedCapture(0, "n", [1]), new PrintedCapture(0, "n", [1])]));

        var capture = Assert.Single(new PrintedBodyMap(
            lines, nodes, [], [], [new PrintedCapture(0, "n", [1])]).Captures);
        Assert.Equal(new PrintedCapture(0, "n", [1]), capture);
    }

    [Fact]
    public void StaleCaptureEvidence_ResolvesToNothingRatherThanAnotherNodesCoordinates()
    {
        using var source = MetadataSource.Open(typeof(CaptureEvidenceFixture).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(CaptureEvidenceFixture).FullName!,
            nameof(CaptureEvidenceFixture.TwoCaptureLambda));
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(
            function!,
            out _,
            importMethodBody: method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded);

        var lambda = Assert.Single(function!.Descendants.OfType<Lambda>());
        Assert.Equal(2, lambda.Captures.Length);
        Assert.All(
            lambda.Captures.SelectMany(capture => capture.Uses),
            use => Assert.Contains(lambda, Ancestors(use)));

        // IrNode.Clone copies the capture list by reference, so a clone's uses
        // still point into the original body. Resolving those against the clone
        // would hand a consumer the ORIGINAL lambda's coordinates under the
        // clone's name; the projection's subtree test is what refuses that.
        var clone = (Lambda)lambda.Clone();
        Assert.Equal(lambda.Captures, clone.Captures);
        Assert.All(
            clone.Captures.SelectMany(capture => capture.Uses),
            use => Assert.DoesNotContain(clone, Ancestors(use)));

        static IEnumerable<IrNode> Ancestors(IrNode node)
        {
            for (var current = node.Parent; current is not null; current = current.Parent)
                yield return current;
        }
    }

    static bool Contains(PrintedExtent outer, PrintedExtent inner)
        => Compare(outer.StartLine, outer.StartColumn, inner.StartLine, inner.StartColumn) <= 0
            && Compare(inner.EndLine, inner.EndColumn, outer.EndLine, outer.EndColumn) <= 0;

    static int Compare(int line, int column, int otherLine, int otherColumn)
    {
        int c = line.CompareTo(otherLine);
        return c != 0 ? c : column.CompareTo(otherColumn);
    }
}

/// <summary>
/// The portable half of the same evidence: a capture row is only useful if it
/// survives serialization exactly and refuses to describe structure that is not
/// there. A capture-free document must keep the wire shape it had before this
/// plane existed, because retained documents are replayed against their recorded
/// revisions.
/// </summary>
public class AnnotatedSourceCaptureDocumentTests
{
    static AnnotatedSourceDocument Document(params AnnotatedSourceCapture[] captures)
        => new(
            "x => x + n",
            [
                new AnnotatedSourceNode(
                    0,
                    AnnotatedSourceNodeKinds.LambdaExpression,
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 10)]),
                new AnnotatedSourceNode(
                    1,
                    AnnotatedSourceNodeKinds.NameExpression,
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(9, 1)]),
                new AnnotatedSourceNode(
                    2,
                    AnnotatedSourceNodeKinds.NameExpression,
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(5, 1)]),
            ],
            [],
            [],
            [],
            Source: null,
            Captures: captures);

    [Fact]
    public void CaptureBearingDocument_RoundTripsThroughTheStrictReader()
    {
        var expected = Document(new AnnotatedSourceCapture(0, "n", [1]));

        string json = JsonSerializer.Serialize(
            expected,
            AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument);
        var actual = AnnotatedSourceJson.DeserializeDocument(json);

        Assert.Contains(
            "\"captures\":[{\"parent_node_id\":0,\"display_name\":\"n\",\"use_node_ids\":[1]}]",
            json);
        Assert.Equal(expected, actual);
        var capture = Assert.Single(actual.Captures!);
        Assert.Equal("n", capture.DisplayName);
        Assert.Equal(
            AnnotatedSourceNodeKinds.LambdaExpression,
            actual.Nodes[capture.ParentNodeId].Kind);
        Assert.Equal([1], capture.UseNodeIds);
    }

    [Fact]
    public void CaptureFreeDocument_KeepsItsPreCaptureWireShape()
    {
        string json = JsonSerializer.Serialize(
            Document(),
            AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument);

        Assert.DoesNotContain("captures", json);
        Assert.Null(AnnotatedSourceJson.DeserializeDocument(json).Captures);

        // Null and empty are the same statement, so they cannot produce two
        // different payloads or two unequal documents.
        Assert.Equal(Document(), AnnotatedSourceJson.DeserializeDocument(json));
    }

    [Fact]
    public void MalformedCaptureReferences_AreRejected()
    {
        // A parent that is not a nested function.
        Assert.Throws<ArgumentException>(() => Document(new AnnotatedSourceCapture(1, "n", [2])));

        // Nodes that do not exist.
        Assert.Throws<ArgumentException>(() => Document(new AnnotatedSourceCapture(9, "n", [1])));
        Assert.Throws<ArgumentException>(() => Document(new AnnotatedSourceCapture(0, "n", [9])));

        // A use that is not a rendered name.
        Assert.Throws<ArgumentException>(() => Document(new AnnotatedSourceCapture(0, "n", [0])));

        // The row must agree with the rendered spelling.
        Assert.Throws<ArgumentException>(() => Document(new AnnotatedSourceCapture(0, "x", [1])));

        // A valid rendered name outside the nested function is not its capture.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            "x => x + n",
            [
                new AnnotatedSourceNode(
                    0,
                    AnnotatedSourceNodeKinds.LambdaExpression,
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 5)]),
                new AnnotatedSourceNode(
                    1,
                    AnnotatedSourceNodeKinds.NameExpression,
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(9, 1)]),
            ],
            [],
            [],
            [],
            Source: null,
            Captures: [new AnnotatedSourceCapture(0, "n", [1])]));

        // Capture structure belongs to rendered C#, never the IL plane.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            "x => x + n",
            [
                new AnnotatedSourceNode(
                    0,
                    AnnotatedSourceNodeKinds.LambdaExpression,
                    SourceLineKind.Il,
                    [new AnnotatedSourceSpan(0, 10)]),
                new AnnotatedSourceNode(
                    1,
                    AnnotatedSourceNodeKinds.NameExpression,
                    SourceLineKind.Il,
                    [new AnnotatedSourceSpan(9, 1)]),
            ],
            [],
            [],
            [],
            Source: null,
            Captures: [new AnnotatedSourceCapture(0, "n", [1])]));

        // Shape rules the row owns itself.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceCapture(0, "n", []));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceCapture(0, "", [1]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceCapture(0, "n", [1, 1]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceCapture(0, "n", [2, 1]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceCapture(0, "\ud800", [1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceCapture(-1, "n", [1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceCapture(0, "n", [-1]));

        // One parent cannot capture one name twice, and rows are canonically
        // ordered so two payloads cannot differ by row order alone.
        Assert.Throws<ArgumentException>(() => Document(
            new AnnotatedSourceCapture(0, "n", [1]),
            new AnnotatedSourceCapture(0, "n", [1])));
        Assert.Throws<ArgumentException>(() => Document(
            new AnnotatedSourceCapture(0, "x", [2]),
            new AnnotatedSourceCapture(0, "n", [1])));
        Assert.Equal(
            2,
            Document(
                new AnnotatedSourceCapture(0, "n", [1]),
                new AnnotatedSourceCapture(0, "x", [2])).Captures!.Count);
    }

    [Fact]
    public void StrictReader_RejectsMalformedCapturePayloads()
    {
        string json = JsonSerializer.Serialize(
            Document(new AnnotatedSourceCapture(0, "n", [1])),
            AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument);

        // A missing required capture property is a contract violation, not a
        // tolerated omission.
        Assert.Throws<JsonException>(() => AnnotatedSourceJson.DeserializeDocument(
            json.Replace("\"display_name\":\"n\",", "", StringComparison.Ordinal)));

        // A row that names structure the document does not contain, or names it
        // as the wrong kind, is rejected by the model contract behind the reader.
        Assert.Throws<JsonException>(() => AnnotatedSourceJson.DeserializeDocument(
            json.Replace("\"use_node_ids\":[1]", "\"use_node_ids\":[9]", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => AnnotatedSourceJson.DeserializeDocument(
            json.Replace("\"use_node_ids\":[1]", "\"use_node_ids\":[0]", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => AnnotatedSourceJson.DeserializeDocument(
            json.Replace("\"parent_node_id\":0", "\"parent_node_id\":1", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => AnnotatedSourceJson.DeserializeDocument(
            json.Replace("\"use_node_ids\":[1]", "\"use_node_ids\":[]", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => AnnotatedSourceJson.DeserializeDocument(
            json.Replace("\"display_name\":\"n\"", "\"display_name\":\"x\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void CSharpProjection_RemapsCaptureRowsWithTheirNodes()
    {
        const string csharp = "x => x + n\n";
        const string il = "IL_0000: nop\n";
        var source = new AnnotatedSourceDocument(
            csharp + il,
            [
                new AnnotatedSourceNode(
                    0,
                    AnnotatedSourceNodeKinds.NameExpression,
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(5, 1)]),
                new AnnotatedSourceNode(
                    1,
                    AnnotatedSourceNodeKinds.Instruction,
                    SourceLineKind.Il,
                    [new AnnotatedSourceSpan(csharp.Length, il.Length - 1)],
                    IlOffset: 0),
                new AnnotatedSourceNode(
                    2,
                    AnnotatedSourceNodeKinds.LambdaExpression,
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, csharp.Length - 1)]),
                new AnnotatedSourceNode(
                    3,
                    AnnotatedSourceNodeKinds.NameExpression,
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(9, 1)]),
            ],
            [],
            [],
            [],
            Source: null,
            Captures: [new AnnotatedSourceCapture(2, "n", [3])]);

        CSharpAnnotatedSourceProjection projection =
            CSharpAnnotatedSourceProjection.Create(source);

        Assert.Equal(csharp, projection.Document.Text);
        Assert.Equal(3, projection.NodeIds.Count);
        Assert.Equal(0, projection.NodeIds[0]);
        Assert.Equal(1, projection.NodeIds[2]);
        Assert.Equal(2, projection.NodeIds[3]);
        var capture = Assert.Single(projection.Document.Captures!);
        Assert.Equal(new AnnotatedSourceCapture(1, "n", [2]), capture);
        Assert.Equal(
            AnnotatedSourceNodeKinds.LambdaExpression,
            projection.Document.Nodes[capture.ParentNodeId].Kind);
        Assert.Equal(
            AnnotatedSourceNodeKinds.NameExpression,
            projection.Document.Nodes[Assert.Single(capture.UseNodeIds)].Kind);
    }
}
