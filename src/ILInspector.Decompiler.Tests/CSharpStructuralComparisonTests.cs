using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Research;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;

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

        AssertCaret(beforeBody, "return;", "raise: Return case body; changed to break;");
        AssertCaret(afterBody, "break;", "raise: Break case body; changed from return;");
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
    public void RenderAnnotatedBody_ExplainsRefArgumentTransition()
    {
        const string beforeText = """
            int M(int value)
            {
                return Read(value);
            }
            """;
        const string afterText = """
            int M(int value)
            {
                return Read(ref value);
            }
            """;
        var before = Document(
            beforeText,
            ("ReturnStatement", "return Read(value);"),
            ("InvocationExpression", "Read(value)"));
        var after = Document(
            afterText,
            ("ReturnStatement", "return Read(ref value);"),
            ("InvocationExpression", "Read(ref value)"));
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "LocalFunctionArgumentSamples.RefArgument",
            before,
            after,
            [0, 1],
            [0, 1],
            [
                new CSharpNodeCorrespondence(0, 0),
                new CSharpNodeCorrespondence(1, 1),
            ]));

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);
        string afterBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.After);

        Assert.Contains(
            "raise: Return; changed to return Read(ref value);",
            beforeBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "raise: InvocationExpression; changed to Read(ref value)",
            beforeBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "raise: Return; changed from return Read(value);",
            afterBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "raise: InvocationExpression; changed from Read(value)",
            afterBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAnnotatedBody_LongTransitionFallsBackSymmetrically()
    {
        const string beforeText = "void M() { Call(value); }";
        string afterInvocation = $"Call({new string('x', 121)});";
        string afterText = $"void M() {{ {afterInvocation} }}";
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            Document(beforeText, "InvocationExpression", "Call(value);"),
            Document(afterText, "InvocationExpression", afterInvocation),
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);
        string afterBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.After);

        Assert.Contains("raise: InvocationExpression; text changed", beforeBody, StringComparison.Ordinal);
        Assert.Contains("raise: InvocationExpression; text changed", afterBody, StringComparison.Ordinal);
        Assert.DoesNotContain("changed from Call(value);", afterBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAnnotatedBody_WrappingWhitespaceRunFallsBackWithoutChangingText()
    {
        string beforeInvocation = $"Call(\"{new string('a', 70)}  b\");";
        string afterInvocation = $"Call(\"{new string('c', 70)}  d\");";
        string beforeText = $"void M()\n{{\n    {beforeInvocation}\n}}";
        string afterText = $"void M()\n{{\n    {afterInvocation}\n}}";
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            Document(beforeText, "InvocationExpression", beforeInvocation),
            Document(afterText, "InvocationExpression", afterInvocation),
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);
        string afterBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.After);

        Assert.Contains("raise: InvocationExpression; text changed", beforeBody, StringComparison.Ordinal);
        Assert.Contains("raise: InvocationExpression; text changed", afterBody, StringComparison.Ordinal);
        Assert.DoesNotContain("changed to Call(", beforeBody, StringComparison.Ordinal);
        Assert.DoesNotContain("changed from Call(", afterBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAnnotatedBody_CounterpartLineSeparatorFallsBackWithoutFlatteningText()
    {
        const string beforeText = "void M()\n{\n    Call(\"a\");\n}";
        const string afterInvocation = "Call(\"x\u2028y\");";
        const string afterText = "void M()\n{\n    Call(\"x\u2028y\");\n}";
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            Document(beforeText, "InvocationExpression", "Call(\"a\");"),
            Document(afterText, "InvocationExpression", afterInvocation),
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);

        Assert.Contains("raise: InvocationExpression; text changed", beforeBody, StringComparison.Ordinal);
        Assert.DoesNotContain("changed to Call(\"x y\");", beforeBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAnnotatedBody_SurrogateSplittingSpanFallsBackWithoutReplacementText()
    {
        const string beforeText = "void M()\n{\n    Call(a);\n}";
        const string afterText = "void M()\n{\n    Call(\"x😀y\");\n}";
        int beforeStart = beforeText.IndexOf("Call(a)", StringComparison.Ordinal);
        int afterStart = afterText.IndexOf("Call(\"x", StringComparison.Ordinal);
        int splitLength = "Call(\"x".Length + 1;
        var before = new AnnotatedSourceDocument(
            beforeText,
            [
                new AnnotatedSourceNode(
                    0,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(beforeStart, "Call(a)".Length)])
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
                    [new AnnotatedSourceSpan(afterStart, splitLength)])
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

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);

        Assert.Contains("raise: InvocationExpression; text changed", beforeBody, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', beforeBody);
    }

    [Fact]
    public void RenderAnnotatedBody_WrappedExactTransitionReconstructsCounterpart()
    {
        const string beforeInvocation = "Call(value);";
        const string afterInvocation =
            "Call(alpha + beta + gamma + delta + epsilon + zeta + eta + theta + iota + kappa);";
        string beforeText = $"void M()\n{{\n    {beforeInvocation}\n}}";
        string afterText = $"void M()\n{{\n    {afterInvocation}\n}}";
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            Document(beforeText, "InvocationExpression", beforeInvocation),
            Document(afterText, "InvocationExpression", afterInvocation),
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);
        string actual = ReconstructAnnotation(beforeBody, "raise: InvocationExpression");

        Assert.Equal($"raise: InvocationExpression; changed to {afterInvocation}", actual);
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
        Assert.Contains("text changed", rendered, StringComparison.Ordinal);
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
        Assert.Contains("text changed", string.Join('\n', rendered), StringComparison.Ordinal);
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

    [Fact]
    public void IssueCorrespondence_BindsChangedNodeToExactDocumentRevisions()
    {
        var before = TrustedDocument(
            "return;",
            new NodeSpec("ReturnStatement", "return;", [0x10]));
        var after = TrustedDocument(
            "    break;",
            new NodeSpec("BreakStatement", "break;", [0x10]));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        var match = Assert.Single(issued.Matches);
        Assert.NotEqual(issued.BeforeRevision, issued.AfterRevision);
        Assert.Equal(issued.BeforeRevision, match.Before.Document);
        Assert.Equal(issued.AfterRevision, match.After.Document);
        Assert.Equal(CSharpNodeMatchProvenance.IlOriginSet, match.Provenance);
        Assert.Equal([0x10], match.Evidence.IlOffsets);
        Assert.Empty(issued.UnmatchedBefore);
        Assert.Empty(issued.UnmatchedAfter);

        var comparison = CSharpBodyDiff.CompareStructure(issued);
        var row = Assert.Single(comparison.Rows);
        Assert.Equal(CSharpStructuralChangeKind.Changed, row.Change);
        Assert.Equal("ReturnStatement", row.BeforeKind);
        Assert.Equal("BreakStatement", row.AfterKind);
        Assert.Equal("return;", comparison.Before.Text);
        Assert.Equal("    break;", comparison.After.Text);

        Assert.Throws<ArgumentException>(() => CSharpBodyDiff.CompareStructure(
            issued with
            {
                BeforeRevision = new CSharpDocumentRevision(new string('B', 64))
            }));
    }

    [Fact]
    public void IssueCorrespondence_DoesNotMatchEqualLocalIdsTextOrCoordinates()
    {
        var before = TrustedDocument(
            "Call();",
            new NodeSpec("InvocationExpression", "Call()", [0x10]));
        var after = TrustedDocument(
            "Call();",
            new NodeSpec("InvocationExpression", "Call()", [0x20]));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Empty(issued.Matches);
        Assert.Equal(
            CSharpUnmatchedNodeReason.NoCounterpart,
            Assert.Single(issued.UnmatchedBefore).Reason);
        Assert.Equal(
            CSharpUnmatchedNodeReason.NoCounterpart,
            Assert.Single(issued.UnmatchedAfter).Reason);
    }

    [Fact]
    public void IssueCorrespondence_ClassifiesPureReorderAsMovement()
    {
        var before = TrustedDocument(
            "one();\ntwo();\n",
            new NodeSpec("ExpressionStatement", "one();", [0x10]),
            new NodeSpec("ExpressionStatement", "two();", [0x20]));
        var after = TrustedDocument(
            "two();\none();\n",
            new NodeSpec("ExpressionStatement", "two();", [0x20]),
            new NodeSpec("ExpressionStatement", "one();", [0x10]));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Equal(2, issued.Matches.Length);
        Assert.Single(issued.Matches, static match => match.Moved);
        var row = Assert.Single(CSharpBodyDiff.CompareStructure(issued).Rows);
        Assert.Equal(CSharpStructuralChangeKind.Moved, row.Change);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotClassifyInsertionAsMovement()
    {
        var before = TrustedDocument(
            "one();\ntwo();\n",
            new NodeSpec("ExpressionStatement", "one();", [0x10]),
            new NodeSpec("ExpressionStatement", "two();", [0x20]));
        var after = TrustedDocument(
            "zero();\none();\ntwo();\n",
            new NodeSpec("ExpressionStatement", "zero();", [0x00]),
            new NodeSpec("ExpressionStatement", "one();", [0x10]),
            new NodeSpec("ExpressionStatement", "two();", [0x20]));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.All(issued.Matches, static match => Assert.False(match.Moved));
        Assert.Equal(
            CSharpUnmatchedNodeReason.NoCounterpart,
            Assert.Single(issued.UnmatchedAfter).Reason);
        Assert.Equal(
            CSharpStructuralChangeKind.Added,
            Assert.Single(CSharpBodyDiff.CompareStructure(issued).Rows).Change);
    }

    [Fact]
    public void IssueCorrespondence_PreservesAmbiguousDuplicateEvidence()
    {
        var before = TrustedDocument(
            "Call(); Call();",
            new NodeSpec("InvocationExpression", "Call()", [0x10]),
            new NodeSpec("InvocationExpression", "Call()", [0x10], Occurrence: 1));
        var after = TrustedDocument(
            "Call();",
            new NodeSpec("InvocationExpression", "Call()", [0x10]));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Empty(issued.Matches);
        Assert.All(
            issued.UnmatchedBefore,
            node => Assert.Equal(CSharpUnmatchedNodeReason.Ambiguous, node.Reason));
        Assert.Equal(
            CSharpUnmatchedNodeReason.Ambiguous,
            Assert.Single(issued.UnmatchedAfter).Reason);
        var comparison = CSharpBodyDiff.CompareStructure(issued);
        Assert.Empty(comparison.Rows);
        Assert.False(comparison.IsExact);
        Assert.False(comparison.IsCorrespondenceComplete);
    }

    [Fact]
    public void IssueCorrespondence_PreservesAmbiguousNestedNodesWithSameIlOrigins()
    {
        var before = TrustedDocument(
            "Call();",
            new NodeSpec("InvocationExpression", "Call();", [0x10]),
            new NodeSpec("NameExpression", "Call", [0x10]));
        var after = TrustedDocument(
            "await Call();",
            new NodeSpec("AwaitExpression", "await Call();", [0x10]),
            new NodeSpec("InvocationExpression", "Call", [0x10]));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Empty(issued.Matches);
        Assert.All(
            issued.UnmatchedBefore,
            node => Assert.Equal(CSharpUnmatchedNodeReason.Ambiguous, node.Reason));
        Assert.All(
            issued.UnmatchedAfter,
            node => Assert.Equal(CSharpUnmatchedNodeReason.Ambiguous, node.Reason));
        Assert.Empty(CSharpBodyDiff.CompareStructure(issued).Rows);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotShiftMatchesWhenSameOriginWrapperIsInserted()
    {
        var before = TrustedDocument(
            "Call();",
            new NodeSpec("ExpressionStatement", "Call();", [0x10]),
            new NodeSpec("InvocationExpression", "Call()", [0x10]));
        var after = TrustedDocument(
            "await Call();",
            new NodeSpec("ExpressionStatement", "await Call();", [0x10]),
            new NodeSpec("AwaitExpression", "await Call()", [0x10]),
            new NodeSpec("InvocationExpression", "Call()", [0x10]));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Empty(issued.Matches);
        Assert.All(
            issued.UnmatchedBefore,
            node => Assert.Equal(CSharpUnmatchedNodeReason.Ambiguous, node.Reason));
        Assert.All(
            issued.UnmatchedAfter,
            node => Assert.Equal(CSharpUnmatchedNodeReason.Ambiguous, node.Reason));
        Assert.Empty(CSharpBodyDiff.CompareStructure(issued).Rows);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotInventCounterpartAcrossUnsupportedPopulation()
    {
        var before = TrustedDocument(
            "Call();",
            new NodeSpec("InvocationExpression", "Call()", [0x10]));
        var after = new AnnotatedSourceDocument(
            "Call();",
            [
                new AnnotatedSourceNode(
                    0,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [new(0, 6)])
            ],
            [],
            [],
            [],
            Source());

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Empty(issued.Matches);
        Assert.Equal(
            CSharpUnmatchedNodeReason.Ambiguous,
            Assert.Single(issued.UnmatchedBefore).Reason);
        Assert.Equal(
            CSharpUnmatchedNodeReason.Unsupported,
            Assert.Single(issued.UnmatchedAfter).Reason);
        Assert.Empty(CSharpBodyDiff.CompareStructure(issued).Rows);
    }

    [Fact]
    public void IssuedCorrespondence_RoundTripsDocumentNodeProvenanceAndUnmatchedNodes()
    {
        var before = TrustedDocument(
            "A(); B();",
            new NodeSpec("InvocationExpression", "A()", [0x10]),
            new NodeSpec("InvocationExpression", "B()", [0x20]));
        var after = TrustedDocument(
            "A();",
            new NodeSpec("InvocationExpression", "A()", [0x10]));
        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        string json = JsonSerializer.Serialize(
            issued,
            AnnotatedSourceDocumentJsonContext.Default.CSharpNodeCorrespondenceResult);
        var replayed = JsonSerializer.Deserialize(
            json,
            AnnotatedSourceDocumentJsonContext.Default.CSharpNodeCorrespondenceResult);

        Assert.NotNull(replayed);
        Assert.Equal(issued.BeforeRevision, replayed.BeforeRevision);
        Assert.Equal(issued.AfterRevision, replayed.AfterRevision);
        Assert.Equal(issued.Matches, replayed.Matches);
        Assert.Equal(issued.UnmatchedBefore, replayed.UnmatchedBefore);
        Assert.Equal(issued.UnmatchedAfter, replayed.UnmatchedAfter);
        Assert.Single(CSharpBodyDiff.CompareStructure(replayed).Rows);
    }

    [Fact]
    public void IssueCorrespondence_RequiresEqualPhysicalMethodProvenance()
    {
        var withoutSource = Document("return;", "ReturnStatement", "return;");
        var trusted = TrustedDocument(
            "return;",
            new NodeSpec("ReturnStatement", "return;", [0x10]));

        Assert.Throws<ArgumentException>(() =>
            CSharpBodyDiff.IssueCorrespondence(withoutSource, trusted));

        var relabeled = new AnnotatedSourceDocument(
            trusted.Text,
            trusted.Nodes,
            trusted.Regions,
            trusted.Facts,
            trusted.Targets,
            new AnnotatedSourceDocumentSource(
                "Different display assembly",
                trusted.Source!.ModuleVersionId,
                trusted.Source.MethodToken,
                trusted.Source.BodyFingerprint,
                "Different display subject"));
        Assert.Single(CSharpBodyDiff.IssueCorrespondence(trusted, relabeled).Matches);

        var different = new AnnotatedSourceDocument(
            trusted.Text,
            trusted.Nodes,
            trusted.Regions,
            trusted.Facts,
            trusted.Targets,
            new AnnotatedSourceDocumentSource(
                trusted.Source!.AssemblyName,
                trusted.Source.ModuleVersionId,
                0x06000002,
                trusted.Source.BodyFingerprint,
                trusted.Source.Subject));
        Assert.Throws<ArgumentException>(() =>
            CSharpBodyDiff.IssueCorrespondence(trusted, different));
    }

    [Fact]
    public void ProductBodyFingerprint_HashesExactSignatureAndMethodBodyBytes()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var reader = source.Reader;
        var method = reader.MethodDefinitions.Single(handle =>
            reader.GetString(reader.GetMethodDefinition(handle).Name)
                == nameof(CfgSampleClass.CallsKeywordInstanceMethod));
        var definition = reader.GetMethodDefinition(method);
        var body = source.Pe.GetMethodBody(definition.RelativeVirtualAddress);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintInt32(hash, (int)definition.Attributes);
        AppendFingerprintInt32(hash, (int)definition.ImplAttributes);
        AppendFingerprintBytes(hash, reader.GetBlobBytes(definition.Signature));
        AppendFingerprintBytes(
            hash,
            source.Pe
                .GetSectionData(definition.RelativeVirtualAddress)
                .GetContent(0, body.Size)
                .AsSpan());

        Assert.Equal(
            System.Convert.ToHexString(hash.GetHashAndReset()),
            CSharpBodyDiff.ComputePhysicalMethodFingerprint(source, method));
    }

    [Fact]
    public void ProductBodyFingerprint_HashesChainedMethodDataSections()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-physical-body-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string finallyPath = Path.Combine(directory, "finally.dll");
        string faultPath = Path.Combine(directory, "fault.dll");

        try
        {
            File.WriteAllBytes(
                finallyPath,
                BuildSyntheticMethodImage(
                    new Guid("11111111-2222-3333-4444-555555555555"),
                    ChainedSectionBody(0x0002)));
            File.WriteAllBytes(
                faultPath,
                BuildSyntheticMethodImage(
                    new Guid("11111111-2222-3333-4444-555555555555"),
                    ChainedSectionBody(0x0004)));

            using var finallySource = MetadataSource.OpenWithoutSymbols(finallyPath);
            using var faultSource = MetadataSource.OpenWithoutSymbols(faultPath);
            var method = MetadataTokens.MethodDefinitionHandle(1);
            var finallyDefinition = finallySource.Reader.GetMethodDefinition(method);
            var faultDefinition = faultSource.Reader.GetMethodDefinition(method);
            var finallyBody = finallySource.Pe.GetMethodBody(finallyDefinition.RelativeVirtualAddress);
            var faultBody = faultSource.Pe.GetMethodBody(faultDefinition.RelativeVirtualAddress);

            Assert.Equal(24, finallyBody.Size);
            Assert.Equal(finallyBody.Size, faultBody.Size);
            Assert.Equal(
                finallySource.Pe.GetSectionData(finallyDefinition.RelativeVirtualAddress)
                    .GetContent(0, finallyBody.Size),
                faultSource.Pe.GetSectionData(faultDefinition.RelativeVirtualAddress)
                    .GetContent(0, faultBody.Size));

            Assert.NotEqual(
                CSharpBodyDiff.ComputePhysicalMethodFingerprint(finallySource, method),
                CSharpBodyDiff.ComputePhysicalMethodFingerprint(faultSource, method));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProductBodyFingerprint_DoesNotMaterializeTheRemainingPeSection()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-physical-body-allocation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "fixture.dll");
        var bodyStream = new byte[4 * 1024 * 1024];
        bodyStream[0] = 0x06;
        bodyStream[1] = 0x2A;

        try
        {
            File.WriteAllBytes(
                path,
                BuildSyntheticMethodImage(
                    new Guid("11111111-2222-3333-4444-555555555555"),
                    bodyStream));
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var method = MetadataTokens.MethodDefinitionHandle(1);
            _ = CSharpBodyDiff.ComputePhysicalMethodFingerprint(source, method);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 5; iteration++)
                _ = CSharpBodyDiff.ComputePhysicalMethodFingerprint(source, method);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.InRange(allocated, 0, 1024 * 1024);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProductDocument_RejectsNilModuleMvid()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-nil-mvid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "fixture.dll");

        try
        {
            File.WriteAllBytes(
                path,
                BuildSyntheticMethodImage(mvid: null, [0x06, 0x2A]));
            using var source = MetadataSource.OpenWithoutSymbols(path);

            Assert.Equal(Guid.Empty, source.ModuleVersionId);
            var projection = ResearchViews.ProjectMember(
                new ResearchViews.MemberProjectionRequest(
                    source,
                    "Fixture",
                    "M",
                    MethodToken: 0x06000001,
                    SourceDocument: true));

            Assert.Null(projection.SourceDocument);
            Assert.NotNull(projection.SourceDocumentFailure);
            Assert.Contains(
                "non-empty MVID",
                string.Join(
                    "; ",
                    projection.SourceDocumentFailure.Diagnostics
                        .Select(static diagnostic => diagnostic.ToString())),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IssuedComparison_ProjectsInterleavedIlWithoutInferringFromText()
    {
        const string beforeText = "return;\nIL_0000: ret";
        const string afterText = "break;\nIL_0000: ret";
        var source = Source();
        var provenance = new AnnotatedSourceNodeProvenance([0]);
        var before = new AnnotatedSourceDocument(
            beforeText,
            [
                new AnnotatedSourceNode(0, "ReturnStatement", SourceLineKind.CSharp, [new(0, 7)], Provenance: provenance),
                new AnnotatedSourceNode(1, AnnotatedSourceNode.InstructionKind, SourceLineKind.Il, [new(8, 12)], 0),
            ],
            [],
            [],
            [],
            source);
        var after = new AnnotatedSourceDocument(
            afterText,
            [
                new AnnotatedSourceNode(0, "BreakStatement", SourceLineKind.CSharp, [new(0, 6)], Provenance: provenance),
                new AnnotatedSourceNode(1, AnnotatedSourceNode.InstructionKind, SourceLineKind.Il, [new(7, 12)], 0),
            ],
            [],
            [],
            [],
            source);

        var comparison = CSharpBodyDiff.CompareStructure(
            CSharpBodyDiff.IssueCorrespondence(before, after));

        Assert.Equal("return;\n", comparison.Before.Text);
        Assert.Equal("break;\n", comparison.After.Text);
        Assert.Single(comparison.Rows);
        Assert.DoesNotContain("IL_0000", CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before), StringComparison.Ordinal);
    }

    [Fact]
    public void RealProductDocuments_IssueChangedStructuralCorrespondence()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var reader = source.Reader;
        var method = reader.MethodDefinitions.Single(handle =>
            reader.GetString(reader.GetMethodDefinition(handle).Name)
                == nameof(CfgSampleClass.CallsKeywordInstanceMethod));
        int token = MetadataTokens.GetToken(method);

        AnnotatedSourceDocument Project(PrinterOptions? options)
            => ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
                source,
                typeof(CfgSampleClass).FullName!,
                nameof(CfgSampleClass.CallsKeywordInstanceMethod),
                AnnotatedStage: AnnotationStage.Raised,
                MethodToken: token,
                PrinterOptions: options,
                SourceDocument: true)).SourceDocument!;

        var before = Project(options: null);
        var after = Project(new PrinterOptions { QualifyMethodAccess = true });
        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);
        var comparison = CSharpBodyDiff.CompareStructure(issued);

        Assert.NotNull(before.Source);
        Assert.NotNull(after.Source);
        Assert.NotEmpty(issued.Matches);
        Assert.Contains(comparison.Rows, row =>
            row.BeforeNodeId is not null
            && row.AfterNodeId is not null
            && row.Change.HasFlag(CSharpStructuralChangeKind.Changed));
        Assert.Contains("this.@event", comparison.After.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("IL_", comparison.After.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RealIteratorDocument_DoesNotTrustCompanionMethodOffsets()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var reader = source.Reader;
        var method = reader.MethodDefinitions.Single(handle =>
            reader.GetString(reader.GetMethodDefinition(handle).Name)
                == nameof(CfgSampleClass.YieldTwo));
        var definition = reader.GetMethodDefinition(method);
        var instructions = MethodInstructions.Decode(
            source.Pe.GetMethodBody(definition.RelativeVirtualAddress));
        Assert.True(instructions.IsComplete);
        var physicalOffsets = instructions.Instructions
            .Select(static instruction => instruction.Offset)
            .ToHashSet();

        var document = ResearchViews.ProjectMember(
            new ResearchViews.MemberProjectionRequest(
                source,
                typeof(CfgSampleClass).FullName!,
                nameof(CfgSampleClass.YieldTwo),
                MethodToken: MetadataTokens.GetToken(method),
                SourceDocument: true)).SourceDocument!;

        var yieldNodes = document.Nodes
            .Where(node => node.Medium == SourceLineKind.CSharp
                && node.Kind == "YieldReturnStatement")
            .ToArray();
        Assert.NotEmpty(yieldNodes);
        Assert.All(yieldNodes, node => Assert.Null(node.Provenance));
        Assert.All(
            document.Nodes.Where(node => node.Provenance is not null),
            node => Assert.All(
                node.Provenance!.IlOffsets,
                offset => Assert.Contains(offset, physicalOffsets)));
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

    static string ReconstructAnnotation(string body, string prefix)
    {
        string[] lines = body.Split('\n');
        int lineIndex = Array.FindIndex(lines, line => line.Contains(prefix, StringComparison.Ordinal));
        Assert.True(lineIndex >= 0);
        int start = lines[lineIndex].IndexOf(prefix, StringComparison.Ordinal);
        var chunks = new List<string> { lines[lineIndex][start..] };
        while (++lineIndex < lines.Length && lines[lineIndex].StartsWith("//", StringComparison.Ordinal))
            chunks.Add(lines[lineIndex][2..].TrimStart());
        return string.Join(' ', chunks);
    }

    static AnnotatedSourceDocument TrustedDocument(
        string text,
        params NodeSpec[] nodes)
    {
        var sourceNodes = nodes
            .Select((node, id) =>
            {
                int start = -1;
                for (int occurrence = 0; occurrence <= node.Occurrence; occurrence++)
                {
                    start = text.IndexOf(node.Text, start + 1, StringComparison.Ordinal);
                }
                Assert.True(start >= 0);
                return new AnnotatedSourceNode(
                    id,
                    node.Kind,
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(start, node.Text.Length)],
                    Provenance: new AnnotatedSourceNodeProvenance(node.IlOffsets));
            })
            .ToArray();
        return new AnnotatedSourceDocument(text, sourceNodes, [], [], [], Source());
    }

    static AnnotatedSourceDocumentSource Source()
        => new(
            "Fixture",
            new Guid("11111111-2222-3333-4444-555555555555"),
            0x06000001,
            new string('A', 64),
            "Fixture.M");

    readonly record struct NodeSpec(
        string Kind,
        string Text,
        IReadOnlyList<int> IlOffsets,
        int Occurrence = 0);

    static void AppendFingerprintBytes(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        AppendFingerprintInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    static void AppendFingerprintInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    static byte[] ChainedSectionBody(ushort handlerFlags)
    {
        string flags = handlerFlags.ToString("X4");
        return System.Convert.FromHexString(
            "0B3008000500000000000000"
            + "00DE01DC2A000000"
            + "81040000"
            + "01100000"
            + flags[2..4] + flags[0..2]
            + "00000303000100000000");
    }

    static byte[] BuildSyntheticMethodImage(Guid? mvid, byte[] methodBody)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("fixture.dll"),
            mvid is { } value ? metadata.GetOrAddGuid(value) : default,
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("fixture"),
            new Version(1, 0),
            default,
            default,
            default,
            default);

        var signature = new BlobBuilder();
        signature.WriteBytes(new byte[] { 0, 0, 1 });
        var method = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            0,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Fixture"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);

        var il = new BlobBuilder();
        il.WriteBytes(methodBody);
        var builder = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics:
                    Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            il,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }
}
