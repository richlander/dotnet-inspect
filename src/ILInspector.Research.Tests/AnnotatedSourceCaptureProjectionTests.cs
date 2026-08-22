using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Research.Tests;

/// <summary>
/// The mixed C#/IL document is where the capture rows have to survive the one
/// transform that could invalidate them: the printer's body-local coordinates
/// are rebased into an interleaved buffer, and instruction nodes are minted
/// alongside the C# ones. A capture row is node ids, so a rebasing that
/// renumbered or reordered them would leave the rows pointing at instructions or
/// at unrelated syntax while still looking well-formed.
/// </summary>
public class AnnotatedSourceCaptureProjectionTests
{
    static AnnotatedSourceDocument Document(string method)
    {
        using var source = MetadataSource.Open(typeof(CaptureProjectionFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(CaptureProjectionFixture).FullName!,
            method,
            SourceDocument: true));
        return Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);
    }

    static string Selected(AnnotatedSourceDocument document, int nodeId)
        => string.Concat(document.Nodes[nodeId].Spans
            .Select(span => document.Text.Substring(span.Start, span.Length)));

    [Theory]
    [InlineData(nameof(CaptureProjectionFixture.CapturingLambda), "=>")]
    [InlineData(nameof(CaptureProjectionFixture.CapturingLocalFunction), "Combine")]
    public void MixedDocument_KeepsCaptureRowsPointingAtTheSameRenderedNames(
        string method,
        string parentMarker)
    {
        var document = Document(method);

        // The document really is the interleaved artifact, so the rows below
        // survived instruction nodes being minted into the same id space.
        Assert.Contains(document.Nodes, node => node.Medium == SourceLineKind.Il);

        Assert.NotNull(document.Captures);
        Assert.Equal(2, document.Captures!.Count);
        Assert.Equal(["first", "second"], document.Captures.Select(capture => capture.DisplayName));

        foreach (var capture in document.Captures)
        {
            var parent = document.Nodes[capture.ParentNodeId];
            Assert.Equal(SourceLineKind.CSharp, parent.Medium);
            Assert.Contains(
                parent.Kind,
                (string[])
                [
                    AnnotatedSourceNodeKinds.LambdaExpression,
                    AnnotatedSourceNodeKinds.LocalFunctionStatement,
                ]);
            Assert.Contains(parentMarker, Selected(document, capture.ParentNodeId));

            Assert.Equal(2, capture.UseNodeIds.Count);
            foreach (int use in capture.UseNodeIds)
            {
                var node = document.Nodes[use];
                Assert.Equal(AnnotatedSourceNodeKinds.NameExpression, node.Kind);
                Assert.Equal(SourceLineKind.CSharp, node.Medium);
                Assert.Null(node.IlOffset);

                // The exact characters, sliced out of the interleaved buffer:
                // this is the whole claim a consumer acts on.
                Assert.Equal(capture.DisplayName, Selected(document, use));
            }
        }

        // Every capture use is inside the text of the nested function it is
        // claimed against, even after the IL lines split those runs apart.
        foreach (var capture in document.Captures)
        {
            var parentSpans = document.Nodes[capture.ParentNodeId].Spans;
            foreach (int use in capture.UseNodeIds)
            {
                Assert.All(document.Nodes[use].Spans, span => Assert.Contains(
                    parentSpans,
                    parent => parent.Start <= span.Start
                        && span.Start + span.Length <= parent.Start + parent.Length));
            }
        }
    }

    [Fact]
    public void MixedDocument_RoundTripsCaptureRowsThroughTheStrictReader()
    {
        var document = Document(nameof(CaptureProjectionFixture.CapturingLambda));

        string json = System.Text.Json.JsonSerializer.Serialize(
            document,
            AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument);
        var replayed = AnnotatedSourceJson.DeserializeDocument(json);

        Assert.Equal(document, replayed);
        Assert.Equal(document.Captures, replayed.Captures);
    }

    [Fact]
    public void MixedDocument_RecordsNoCaptureForANonCapturingLambda()
    {
        var document = Document(nameof(CaptureProjectionFixture.NonCapturingLambda));

        Assert.Contains(
            document.Nodes,
            node => node.Kind == AnnotatedSourceNodeKinds.LambdaExpression);
        Assert.Null(document.Captures);
    }
}

/// <summary>
/// Compiler-produced closures for the portable projection. Each captured
/// variable is read twice under different sub-expressions, so both reads are
/// separately addressable in the printed text.
/// </summary>
public static class CaptureProjectionFixture
{
    public static System.Func<int, int> CapturingLambda(int first, int second)
        => x => x * first - second + (second - first);

    public static int CapturingLocalFunction(int first, int second)
    {
        return Combine(5);

        int Combine(int v) => v * first - second + (second - first);
    }

    public static System.Func<int, int> NonCapturingLambda() => x => x * 3 + 1;
}
