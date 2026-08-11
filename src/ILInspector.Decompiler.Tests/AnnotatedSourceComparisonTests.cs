using ILInspector.Decompiler;

namespace ILInspector.Decompiler.Tests;

public class AnnotatedSourceComparisonTests
{
    [Fact]
    public void CompareReportsKindChangeInsideCaseAndRendersBothViews()
    {
        const string beforeText = """
            void M(int value)
            {
                switch (value)
                {
                    case 0:
                        return;
                }
            }
            """;
        const string afterText = """
            void M(int value)
            {
                switch (value)
                {
                    case 0:
                        break;
                }
            }
            """;
        var before = Document(
            beforeText,
            [Node(0, "ReturnStatement", beforeText, "return;")],
            [CaseRegion(beforeText, "case 0:", "return;")]);
        var after = Document(
            afterText,
            [Node(0, "BreakStatement", afterText, "break;")],
            [CaseRegion(afterText, "case 0:", "break;")]);

        var result = AnnotatedSourceComparer.Compare(before, after);

        var change = Assert.Single(result.Changes);
        Assert.Equal(AnnotatedSourceChangeKind.Changed, change.Kind);
        Assert.Equal("ReturnStatement", change.Before!.Kind);
        Assert.Equal("BreakStatement", change.After!.Kind);
        Assert.Equal("Case", change.Before.RegionPath);
        Assert.Empty(change.Evidence);

        string markdown = AnnotatedSourceComparisonRenderer.RenderMarkdown(result);
        Assert.Contains("return;", markdown);
        Assert.Contains("break;", markdown);
        Assert.Contains("ReturnStatement -> BreakStatement [Case]", markdown);
        Assert.Contains("| Changed | ReturnStatement | BreakStatement | Case |", markdown);
        Assert.DoesNotContain("IL fidelity", markdown);
    }

    [Fact]
    public void RendererStacksMultipleChangesOnOneLine()
    {
        const string beforeText = "void M() { return; new C(); }";
        const string afterText = "void M() { break; C.Create(); }";
        var before = Document(
            beforeText,
            [
                Node(0, "ReturnStatement", beforeText, "return;"),
                Node(1, "ObjectCreationExpression", beforeText, "new C()"),
            ]);
        var after = Document(
            afterText,
            [
                Node(0, "BreakStatement", afterText, "break;"),
                Node(1, "InvocationExpression", afterText, "C.Create()"),
            ]);

        var result = AnnotatedSourceComparer.Compare(before, after);
        string rendered = AnnotatedSourceComparisonRenderer.RenderBefore(result);

        Assert.Equal(2, result.Changes.Length);
        Assert.Contains("1.", rendered);
        Assert.Contains("2.", rendered);
        Assert.Contains("ReturnStatement -> BreakStatement", rendered);
        Assert.Contains("ObjectCreationExpression -> InvocationExpression", rendered);
    }

    [Fact]
    public void RendererProjectsEveryPieceOfMultiSpanNode()
    {
        const string beforeText = """
            void M()
            {
                if (ready)
                    return;
            }
            """;
        const string afterText = """
            void M()
            {
                while (ready)
                    break;
            }
            """;
        var before = Document(beforeText,
            [Node(0, "IfStatement", beforeText, "if", "return;")]);
        var after = Document(afterText,
            [Node(0, "WhileStatement", afterText, "while", "break;")]);

        var result = AnnotatedSourceComparer.Compare(before, after);
        string rendered = AnnotatedSourceComparisonRenderer.RenderBefore(result);

        Assert.Equal(2, rendered.Split('\n').Count(line => line.Contains('^')));
        Assert.Equal(2, Assert.Single(result.Changes).Before!.Spans.Length);
    }

    [Fact]
    public void TextMapUsesAbsoluteUtf16Coordinates()
    {
        const string text = "a😀b\r\nreturn;";
        var map = new AnnotatedSourceTextMap(text);

        var emoji = Assert.Single(map.Project(new AnnotatedSourceSpan(1, 2)));
        var statement = Assert.Single(map.Project(
            new AnnotatedSourceSpan(text.IndexOf("return;", StringComparison.Ordinal), "return;".Length)));

        Assert.Equal(new AnnotatedSourceLineSpan(0, 1, 2), emoji);
        Assert.Equal(new AnnotatedSourceLineSpan(1, 0, 7), statement);
    }

    [Fact]
    public void CompareReportsMovedUnknownFutureKind()
    {
        const string beforeText = "alpha\nbeta";
        const string afterText = "beta\nalpha";
        var before = Document(
            beforeText,
            [
                Node(0, "FutureShape", beforeText, "alpha"),
                Node(1, "InvocationExpression", beforeText, "beta"),
            ]);
        var after = Document(
            afterText,
            [
                Node(0, "InvocationExpression", afterText, "beta"),
                Node(1, "FutureShape", afterText, "alpha"),
            ]);

        var result = AnnotatedSourceComparer.Compare(before, after);

        var move = Assert.Single(result.Changes);
        Assert.Equal(AnnotatedSourceChangeKind.Moved, move.Kind);
        Assert.Equal("FutureShape", move.Before!.Kind);
        Assert.Contains("FutureShape", AnnotatedSourceComparisonRenderer.RenderRichDiff(result));
    }

    [Fact]
    public void IdenticalDocumentsHaveNoStructuralChanges()
    {
        const string text = "void M() { return; }";
        var document = Document(
            text,
            [Node(0, "ReturnStatement", text, "return;")]);

        var result = AnnotatedSourceComparer.Compare(document, document);

        Assert.Empty(result.Changes);
        Assert.Equal("No structural changes.", AnnotatedSourceComparisonRenderer.RenderRichDiff(result));
    }

    [Fact]
    public void CompareReportsAddedAndRemovedNodes()
    {
        const string text = "return;";
        var empty = Document(text, []);
        var populated = Document(text, [Node(0, "ReturnStatement", text, text)]);

        var added = AnnotatedSourceComparer.Compare(empty, populated);
        var removed = AnnotatedSourceComparer.Compare(populated, empty);

        Assert.Equal(AnnotatedSourceChangeKind.Added, Assert.Single(added.Changes).Kind);
        Assert.Equal(AnnotatedSourceChangeKind.Removed, Assert.Single(removed.Changes).Kind);
    }

    [Fact]
    public void CompareRejectsIlDocuments()
    {
        const string text = "IL_0000: ret";
        var document = Document(
            text,
            [new AnnotatedSourceNode(
                0,
                AnnotatedSourceNode.InstructionKind,
                SourceLineKind.Il,
                [new AnnotatedSourceSpan(0, text.Length)],
                IlOffset: 0)]);

        var exception = Assert.Throws<ArgumentException>(
            () => AnnotatedSourceComparer.Compare(document, document));
        Assert.Contains("C#-only", exception.Message);
    }

    static AnnotatedSourceDocument Document(
        string text,
        IReadOnlyList<AnnotatedSourceNode> nodes,
        IReadOnlyList<AnnotatedSourceRegion>? regions = null)
        => new(text, nodes, regions ?? [], [], []);

    static AnnotatedSourceNode Node(
        int id,
        string kind,
        string text,
        params string[] selections)
        => new(
            id,
            kind,
            SourceLineKind.CSharp,
            [.. selections.Select(selection =>
            {
                int start = text.IndexOf(selection, StringComparison.Ordinal);
                Assert.True(start >= 0, $"Selection '{selection}' was not found.");
                return new AnnotatedSourceSpan(start, selection.Length);
            })]);

    static AnnotatedSourceRegion CaseRegion(
        string text,
        string startText,
        string endText)
    {
        int start = text.IndexOf(startText, StringComparison.Ordinal);
        int end = text.IndexOf(endText, StringComparison.Ordinal) + endText.Length;
        return new AnnotatedSourceRegion(
            PrintedRegionRole.Case,
            [new AnnotatedSourceSpan(start, end - start)]);
    }
}
