using ILInspector.Decompiler;
using ILInspector.Instructions;

namespace ILInspector.Decompiler.Tests;

public class CSharpStructuralComparisonTests
{
    [Fact]
    public void NloptReturnToBreak_ProducesFullBodyCaretsAndRichDiff()
    {
        const string beforeText = """
            protected void CheckEqualityConstraintAvailability()
            {
                NLoptAlgorithm nLoptAlgorithm = Algorithm;
                switch (nLoptAlgorithm)
                {
                    case NLoptAlgorithm.LN_COBYLA:
                    case NLoptAlgorithm.LN_AUGLAG:
                    case NLoptAlgorithm.LD_AUGLAG:
                    case NLoptAlgorithm.LN_AUGLAG_EQ:
                    case NLoptAlgorithm.LD_AUGLAG_EQ:
                    case NLoptAlgorithm.GN_ISRES:
                    case NLoptAlgorithm.AUGLAG:
                    case NLoptAlgorithm.AUGLAG_EQ:
                    case NLoptAlgorithm.LD_SLSQP:
                        return;
                    case NLoptAlgorithm.LN_NEWUOA:
                    case NLoptAlgorithm.LN_NEWUOA_BOUND:
                    case NLoptAlgorithm.LN_NELDERMEAD:
                    case NLoptAlgorithm.LN_SBPLX:
                    case NLoptAlgorithm.LN_BOBYQA:
                    case NLoptAlgorithm.G_MLSL:
                    case NLoptAlgorithm.G_MLSL_LDS:
                    default:
                        throw new ArgumentException(string.Concat("Algorithm ", nLoptAlgorithm.ToString(), " does not support equality constraint."));
                }
            }
            """;
        string afterText = beforeText.Replace("return;", "break;", StringComparison.Ordinal);

        var before = Document(beforeText, "ReturnStatement", "return;");
        var after = Document(afterText, "BreakStatement", "break;");
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "NLoptNet.NLoptSolver.CheckEqualityConstraintAvailability",
            before,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)],
            new CSharpStructuralFidelityEvidence(
                IlBodyDiffOutcome.OpcodeDiff,
                IlBodyDiffOutcome.Exact,
                "terminal IL_0072: ret")));

        var row = Assert.Single(comparison.Rows);
        Assert.Equal(CSharpStructuralChangeKind.Changed, row.Change);
        Assert.Equal("ReturnStatement", row.BeforeKind);
        Assert.Equal("BreakStatement", row.AfterKind);
        Assert.Equal("Return", row.BeforeLabel);
        Assert.Equal("Break", row.AfterLabel);
        Assert.Equal(PrintedRegionRole.Case, row.BeforeRegion);
        Assert.Equal(PrintedRegionRole.Case, row.AfterRegion);

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);
        string afterBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.After);

        AssertCaret(beforeBody, "return;", "raise: Return case body");
        AssertCaret(afterBody, "break;", "raise: Break case body");
        Assert.Contains("case NLoptAlgorithm.G_MLSL_LDS:", beforeBody, StringComparison.Ordinal);
        Assert.Contains("throw new ArgumentException", beforeBody, StringComparison.Ordinal);
        Assert.DoesNotContain("raise: Throw", beforeBody, StringComparison.Ordinal);

        var display = Assert.Single(CSharpStructuralDiffPrinter.ToDisplayRows(comparison));
        Assert.Equal("Changed", display.Change);
        Assert.Equal("Return -> Break", display.Structure);
        Assert.Equal("Case", display.Region);
        Assert.Equal("OpcodeDiff -> Exact; terminal IL_0072: ret", display.Fidelity);
    }

    [Fact]
    public void CompareStructure_ReportsAddedRemovedChangedAndMovedDeterministically()
    {
        const string beforeText = """
            void M()
            {
                A(); B();
                C();
            }
            """;
        const string afterText = """
            void M()
            {
                D(); E();
                A();
            }
            """;
        var before = Document(
            beforeText,
            ("InvocationExpression", "A()"),
            ("InvocationExpression", "B()"),
            ("InvocationExpression", "C()"));
        var after = Document(
            afterText,
            ("InvocationExpression", "D()"),
            ("InvocationExpression", "E()"),
            ("InvocationExpression", "A()"));

        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0, 1, 2],
            [0, 1, 2],
            [
                new CSharpNodeCorrespondence(0, 2, Moved: true),
                new CSharpNodeCorrespondence(1, 0, Moved: true),
            ]));

        Assert.Equal(4, comparison.Rows.Length);
        Assert.Contains(comparison.Rows, row =>
            row.Change == CSharpStructuralChangeKind.Moved
            && row.BeforeNodeId == 0
            && row.AfterNodeId == 2);
        Assert.Contains(comparison.Rows, row =>
            row.Change == (CSharpStructuralChangeKind.Changed | CSharpStructuralChangeKind.Moved)
            && row.BeforeNodeId == 1
            && row.AfterNodeId == 0);
        Assert.Contains(comparison.Rows, row =>
            row.Change == CSharpStructuralChangeKind.Removed
            && row.BeforeNodeId == 2);
        Assert.Contains(comparison.Rows, row =>
            row.Change == CSharpStructuralChangeKind.Added
            && row.AfterNodeId == 1);

        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);
        Assert.Contains(display, row => row.Change == "Changed, Moved");
        Assert.Contains(display, row => row.Change == "Added");
        Assert.Contains(display, row => row.Change == "Removed");
        Assert.Contains(display, row => row.Change == "Moved");
    }

    [Fact]
    public void RenderAnnotatedBody_StacksMultiSpanCaretsAndUsesUtf16Coordinates()
    {
        const string beforeText = """
            void M()
            {
                Use("😀", left, right);
            }
            """;
        string afterText = beforeText.Replace("right", "other", StringComparison.Ordinal);
        int emojiStart = beforeText.IndexOf("\"😀\"", StringComparison.Ordinal);
        int leftStart = beforeText.IndexOf("left", StringComparison.Ordinal);
        int rightStart = beforeText.IndexOf("right", StringComparison.Ordinal);
        int otherStart = afterText.IndexOf("other", StringComparison.Ordinal);

        var before = new AnnotatedSourceDocument(
            beforeText,
            [
                new AnnotatedSourceNode(
                    0,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [
                        new AnnotatedSourceSpan(emojiStart, "\"😀\"".Length),
                        new AnnotatedSourceSpan(leftStart, "left".Length),
                        new AnnotatedSourceSpan(rightStart, "right".Length),
                    ])
            ],
            [],
            [],
            []);
        var after = new AnnotatedSourceDocument(
            afterText,
            [
                new AnnotatedSourceNode(
                    0,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [
                        new AnnotatedSourceSpan(emojiStart, "\"😀\"".Length),
                        new AnnotatedSourceSpan(leftStart, "left".Length),
                        new AnnotatedSourceSpan(otherStart, "other".Length),
                    ])
            ],
            [],
            [],
            []);
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        string rendered = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);
        string[] lines = rendered.Split('\n');
        int sourceLine = Array.FindIndex(lines, line => line.Contains("Use(", StringComparison.Ordinal));
        Assert.True(sourceLine >= 0);
        Assert.Contains("1.", lines[sourceLine + 1], StringComparison.Ordinal);
        Assert.Contains("2.", lines[sourceLine + 1], StringComparison.Ordinal);
        Assert.Contains("3.", lines[sourceLine + 1], StringComparison.Ordinal);

        int sourceEmojiColumn = lines[sourceLine].IndexOf("\"😀\"", StringComparison.Ordinal);
        int firstCaretColumn = lines[sourceLine + 1].IndexOf('^');
        Assert.Equal(sourceEmojiColumn, firstCaretColumn);
        Assert.Contains("^^^^", lines[sourceLine + 1], StringComparison.Ordinal);
    }

    [Fact]
    public void UnchangedPair_ProducesFullBodiesWithoutCaretsAndNoRows()
    {
        const string text = """
            void M()
            {
                return;
            }
            """;
        var before = Document(text, "ReturnStatement", "return;");
        var after = Document(text, "ReturnStatement", "return;");

        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        Assert.True(comparison.IsExact);
        Assert.Empty(CSharpStructuralDiffPrinter.ToDisplayRows(comparison));
        Assert.Equal(
            text,
            CSharpStructuralDiffPrinter.RenderAnnotatedBody(comparison, CSharpStructuralSide.Before));
        Assert.Equal(
            text,
            CSharpStructuralDiffPrinter.RenderAnnotatedBody(comparison, CSharpStructuralSide.After));
    }

    [Fact]
    public void RenderAnnotatedBody_MultilineSpanNeverOverrunsContinuationLine()
    {
        const string beforeText = """
            void M()
            {
                Call(
                    first,
                    second);
            }
            """;
        string afterText = beforeText.Replace("second", "changed", StringComparison.Ordinal);
        int beforeStart = beforeText.IndexOf("Call(", StringComparison.Ordinal);
        int afterStart = afterText.IndexOf("Call(", StringComparison.Ordinal);
        int beforeEnd = beforeText.IndexOf(");", beforeStart, StringComparison.Ordinal) + 2;
        int afterEnd = afterText.IndexOf(");", afterStart, StringComparison.Ordinal) + 2;
        var before = new AnnotatedSourceDocument(
            beforeText,
            [
                new AnnotatedSourceNode(
                    0,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(beforeStart, beforeEnd - beforeStart)])
            ],
            [new AnnotatedSourceRegion(PrintedRegionRole.Body, [new(0, beforeText.Length)])],
            [],
            []);
        var after = new AnnotatedSourceDocument(
            afterText,
            [
                new AnnotatedSourceNode(
                    0,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(afterStart, afterEnd - afterStart)])
            ],
            [new AnnotatedSourceRegion(PrintedRegionRole.Body, [new(0, afterText.Length)])],
            [],
            []);
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        string[] rendered = CSharpStructuralDiffPrinter
            .RenderAnnotatedBody(comparison, CSharpStructuralSide.Before)
            .Split('\n');
        int continuation = Array.FindIndex(rendered, line => line.Contains("first,", StringComparison.Ordinal));
        Assert.True(continuation >= 0);
        string sourceLine = rendered[continuation];
        string caretLine = rendered[continuation + 1];
        int firstCaret = caretLine.IndexOf('^');
        int lastCaret = caretLine.LastIndexOf('^');
        Assert.True(firstCaret >= 0);
        Assert.True(lastCaret < sourceLine.Length);
    }

    [Fact]
    public void RenderAnnotatedBody_EarlyColumnSpansUseExactGutterFreeCarets()
    {
        const string beforeText = "a(b);";
        const string afterText = "a(c);";
        var before = new AnnotatedSourceDocument(
            beforeText,
            [
                new AnnotatedSourceNode(0, "InvocationExpression", SourceLineKind.CSharp, [new(0, 4)]),
                new AnnotatedSourceNode(1, "NameExpression", SourceLineKind.CSharp, [new(2, 1)]),
            ],
            [],
            [],
            []);
        var after = new AnnotatedSourceDocument(
            afterText,
            [
                new AnnotatedSourceNode(0, "InvocationExpression", SourceLineKind.CSharp, [new(0, 4)]),
                new AnnotatedSourceNode(1, "NameExpression", SourceLineKind.CSharp, [new(2, 1)]),
            ],
            [],
            [],
            []);
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0, 1],
            [0, 1],
            [
                new CSharpNodeCorrespondence(0, 0),
                new CSharpNodeCorrespondence(1, 1),
            ]));

        string[] rendered = CSharpStructuralDiffPrinter
            .RenderAnnotatedBody(comparison, CSharpStructuralSide.Before)
            .Split('\n');
        Assert.Equal(beforeText, rendered[0]);
        Assert.StartsWith("^^^^ raise: InvocationExpression", rendered[1], StringComparison.Ordinal);
        Assert.StartsWith("  ^ raise: NameExpression", rendered[2], StringComparison.Ordinal);
        Assert.Equal("^^^^", new string([.. rendered[1].TakeWhile(character => character == '^')]));
        Assert.Equal("^", new string([.. rendered[2].Skip(2).TakeWhile(character => character == '^')]));
    }

    [Fact]
    public void RenderAnnotatedBody_WhitespaceOnlyLineUsesExactFallbackCaret()
    {
        const string beforeText = "x\n      y";
        const string afterText = "x\n       ";
        var before = new AnnotatedSourceDocument(
            beforeText,
            [new AnnotatedSourceNode(0, "ReturnStatement", SourceLineKind.CSharp, [new(6, 2)])],
            [],
            [],
            []);
        var after = new AnnotatedSourceDocument(
            afterText,
            [new AnnotatedSourceNode(0, "BreakStatement", SourceLineKind.CSharp, [new(6, 2)])],
            [],
            [],
            []);
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        string[] rendered = CSharpStructuralDiffPrinter
            .RenderAnnotatedBody(comparison, CSharpStructuralSide.After)
            .Split('\n');

        Assert.Equal(afterText.Split('\n')[1], rendered[1]);
        Assert.StartsWith("    ^^ raise: Break", rendered[2], StringComparison.Ordinal);
    }

    [Fact]
    public void CompareStructure_RejectsUnknownFidelityOutcome()
    {
        var before = Document("return;", "ReturnStatement", "return;");
        var after = Document("break;", "BreakStatement", "break;");

        Assert.Throws<ArgumentException>(() => CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)],
            new CSharpStructuralFidelityEvidence(
                (IlBodyDiffOutcome)999,
                IlBodyDiffOutcome.Exact))));
    }

    [Fact]
    public void CompareStructure_RejectsDocumentLocalIdentityViolations()
    {
        var before = Document("return;", "ReturnStatement", "return;");
        var after = Document("break;", "BreakStatement", "break;");

        Assert.Throws<ArgumentException>(() => CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0],
            [0],
            [
                new CSharpNodeCorrespondence(0, 0),
                new CSharpNodeCorrespondence(0, 0),
            ])));
        Assert.Throws<ArgumentException>(() => CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [],
            [0],
            [new CSharpNodeCorrespondence(0, 0)])));

        var ilDocument = new AnnotatedSourceDocument(
            "IL_0000: ret",
            [
                new AnnotatedSourceNode(
                    0,
                    AnnotatedSourceNode.InstructionKind,
                    SourceLineKind.Il,
                    [new AnnotatedSourceSpan(0, "IL_0000: ret".Length)],
                    IlOffset: 0)
            ],
            [],
            [],
            []);
        Assert.Throws<ArgumentException>(() => CSharpBodyDiff.CompareStructure(new(
            "M",
            ilDocument,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)])));
    }

    static AnnotatedSourceDocument Document(
        string text,
        string kind,
        string selectedText)
        => Document(text, (kind, selectedText));

    static AnnotatedSourceDocument Document(
        string text,
        params (string Kind, string Text)[] nodes)
    {
        var sourceNodes = nodes
            .Select((node, id) => new AnnotatedSourceNode(
                id,
                node.Kind,
                SourceLineKind.CSharp,
                [new AnnotatedSourceSpan(text.IndexOf(node.Text, StringComparison.Ordinal), node.Text.Length)]))
            .ToArray();
        int caseStart = text.IndexOf("case ", StringComparison.Ordinal);
        var regions = caseStart < 0
            ? []
            :
            (AnnotatedSourceRegion[])
            [
                new(
                    PrintedRegionRole.Case,
                    [new AnnotatedSourceSpan(caseStart, sourceNodes[0].Spans[0].Start + sourceNodes[0].Spans[0].Length - caseStart)])
            ];
        return new AnnotatedSourceDocument(text, sourceNodes, regions, [], []);
    }

    static void AssertCaret(string body, string source, string label)
    {
        string[] lines = body.Split('\n');
        int sourceLine = Array.FindIndex(lines, line => line.Contains(source, StringComparison.Ordinal));
        Assert.True(sourceLine >= 0);
        string caretLine = lines[sourceLine + 1];
        Assert.Equal(lines[sourceLine].IndexOf(source, StringComparison.Ordinal), caretLine.IndexOf('^'));
        Assert.Equal(source.Length, caretLine.Count(character => character == '^'));
        Assert.Contains(label, caretLine, StringComparison.Ordinal);
    }
}
