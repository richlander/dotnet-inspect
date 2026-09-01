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
        Assert.Equal("return; -> break;", display.Detail);
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

        // The enclosing Return row is collapsed (issue #5022 item 1): its only
        // diff is the InvocationExpression's own "value" -> "ref value" change,
        // so it adds no information beyond the more specific row below.
        Assert.DoesNotContain("raise: Return", beforeBody, StringComparison.Ordinal);
        Assert.Contains(
            "raise: InvocationExpression; changed to Read(ref value)",
            beforeBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("raise: Return", afterBody, StringComparison.Ordinal);
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
        Assert.DoesNotContain('\uD83D', beforeBody);
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
        var (actual, chunkCount) = ReconstructAnnotation(beforeBody, "raise: InvocationExpression");

        Assert.True(chunkCount > 1, "the fixture must wrap annotation detail");
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

        // Detail column: Changed rows summarize the text transition inline;
        // Added/Removed rows use the same "+"/"-" convention as FormatTransition;
        // a Moved-only row (no text or kind change) carries no Detail.
        Assert.Contains(display, row => row.Change == "Changed, Moved" && row.Detail == "B() -> D()");
        Assert.Contains(display, row => row.Change == "Added" && row.Detail == "+ E()");
        Assert.Contains(display, row => row.Change == "Removed" && row.Detail == "- C()");
        Assert.Contains(display, row => row.Change == "Moved" && row.Detail == "");
    }

    [Fact]
    public void CompareStructure_CollapsesSubsumedAncestorRowsToMostSpecificNode()
    {
        // Reproduces the #4942 shape recorded in #4952 (issue #5022, item 1):
        // a receiver-qualification rewrite (instance-call -> static-call
        // syntax) nested three levels deep (Return > InvocationExpression >
        // InvocationExpression) previously produced three stacked, redundant
        // rows. Only the innermost node's text actually differs; the two
        // ancestor rows re-quote the same change inside a larger span.
        const string beforeText =
            "return receiver.Values(typeof(Attribute), true).FirstOrDefault<Attribute>();";
        const string afterText =
            "return Values(receiver, typeof(Attribute), true).FirstOrDefault<Attribute>();";

        var before = Document(
            beforeText,
            ("Return", "return receiver.Values(typeof(Attribute), true).FirstOrDefault<Attribute>();"),
            ("InvocationExpression", "receiver.Values(typeof(Attribute), true).FirstOrDefault<Attribute>()"),
            ("InvocationExpression", "receiver.Values(typeof(Attribute), true)"));
        var after = Document(
            afterText,
            ("Return", "return Values(receiver, typeof(Attribute), true).FirstOrDefault<Attribute>();"),
            ("InvocationExpression", "Values(receiver, typeof(Attribute), true).FirstOrDefault<Attribute>()"),
            ("InvocationExpression", "Values(receiver, typeof(Attribute), true)"));

        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0, 1, 2],
            [0, 1, 2],
            [
                new CSharpNodeCorrespondence(0, 0),
                new CSharpNodeCorrespondence(1, 1),
                new CSharpNodeCorrespondence(2, 2),
            ]));

        var row = Assert.Single(comparison.Rows);
        Assert.Equal(CSharpStructuralChangeKind.Changed, row.Change);
        Assert.Equal(2, row.BeforeNodeId);
        Assert.Equal(2, row.AfterNodeId);
    }

    [Fact]
    public void CompareStructure_DoesNotCollapseAncestorWhenItCarriesIndependentMovedInformation()
    {
        // Regression for round-1 review: an ancestor's Moved flag is
        // owner-issued and independent of whatever text change a nested
        // descendant explains. If the descendant satisfies the ancestor's
        // text-containment check but the ancestor is Changed|Moved (not
        // plain Changed), suppressing it would silently discard the only row
        // reporting the move. The same nested-call shape as the item-1 test
        // is reused here, but the outer InvocationExpression correspondence
        // is now also flagged Moved.
        const string beforeText =
            "return receiver.Values(typeof(Attribute), true).FirstOrDefault<Attribute>();";
        const string afterText =
            "return Values(receiver, typeof(Attribute), true).FirstOrDefault<Attribute>();";

        var before = Document(
            beforeText,
            ("Return", "return receiver.Values(typeof(Attribute), true).FirstOrDefault<Attribute>();"),
            ("InvocationExpression", "receiver.Values(typeof(Attribute), true).FirstOrDefault<Attribute>()"),
            ("InvocationExpression", "receiver.Values(typeof(Attribute), true)"));
        var after = Document(
            afterText,
            ("Return", "return Values(receiver, typeof(Attribute), true).FirstOrDefault<Attribute>();"),
            ("InvocationExpression", "Values(receiver, typeof(Attribute), true).FirstOrDefault<Attribute>()"),
            ("InvocationExpression", "Values(receiver, typeof(Attribute), true)"));

        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0, 1, 2],
            [0, 1, 2],
            [
                new CSharpNodeCorrespondence(0, 0),
                new CSharpNodeCorrespondence(1, 1, Moved: true),
                new CSharpNodeCorrespondence(2, 2),
            ]));

        Assert.Contains(comparison.Rows, row =>
            row.Change == (CSharpStructuralChangeKind.Changed | CSharpStructuralChangeKind.Moved)
            && row.BeforeNodeId == 1
            && row.AfterNodeId == 1);
        Assert.Contains(comparison.Rows, row =>
            row.Change == CSharpStructuralChangeKind.Changed
            && row.BeforeNodeId == 2
            && row.AfterNodeId == 2);
        Assert.DoesNotContain(comparison.Rows, row => row.BeforeNodeId == 0);
    }

    [Fact]
    public void CompareStructure_NarrowsUsingStatementDeclarationDroppedToTypeIdentifierEquals()
    {
        // Reproduces the #4113 shape recorded in #4952 (issue #5022, items 2,
        // 7, and 10): a disposed-only `using` resource is raised to the
        // variable-less form. The body (`{`, `n = 1;`, `}`) is untouched and
        // never reads `iDisposable`, so item 10 narrows the row's spans
        // further than items 2/7's header-only narrowing: to exactly
        // `IDisposable iDisposable =` on the declaring side, and to the bare
        // resource expression on the variable-less side -- matching #4952's
        // "agreed-better mockup" for #4113 exactly, including its side-local
        // "never read" captions.
        const string beforeText = """
            int n = 0;
            using (IDisposable iDisposable = DisposableFromObjectSpan([a, b]))
            {
                n = 1;
            }
            return n;
            """;
        const string afterText = """
            int n = 0;
            using (DisposableFromObjectSpan([a, b]))
            {
                n = 1;
            }
            return n;
            """;

        var before = UsingStatementDocument(beforeText);
        var after = UsingStatementDocument(afterText);

        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        var row = Assert.Single(comparison.Rows);
        Assert.Equal(CSharpStructuralChangeKind.Changed, row.Change);
        var beforeSpan = Assert.Single(row.BeforeSpans);
        var afterSpan = Assert.Single(row.AfterSpans);
        Assert.Equal(
            "IDisposable iDisposable =",
            before.Text.Substring(beforeSpan.Start, beforeSpan.Length));
        Assert.Equal(
            "DisposableFromObjectSpan([a, b])",
            after.Text.Substring(afterSpan.Start, afterSpan.Length));

        // The narrowed span is still contained within the printer's own
        // Header region, so the row's reported region role stays Header --
        // matching items 2/7's own invariant that the reported region must
        // reflect what the caret actually covers.
        Assert.Equal(PrintedRegionRole.Header, row.BeforeRegion);
        Assert.Equal(PrintedRegionRole.Header, row.AfterRegion);

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);
        string afterBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.After);

        AssertCaret(
            beforeBody,
            "IDisposable iDisposable =",
            "raise: UsingStatement header; declares variable `iDisposable` (never read)");
        AssertCaret(
            afterBody,
            "DisposableFromObjectSpan([a, b])",
            "raise: UsingStatement header; variable-less resource (declaration dropped; never read)");

        var display = Assert.Single(CSharpStructuralDiffPrinter.ToDisplayRows(comparison));
        Assert.Equal(
            "header: variable declaration dropped (`iDisposable` never read)",
            display.Detail);

        Assert.DoesNotContain("raise: UsingStatement construct", beforeBody, StringComparison.Ordinal);
        Assert.DoesNotContain("raise: UsingStatement construct", afterBody, StringComparison.Ordinal);
        foreach (string unchangedLine in new[] { "{", "n = 1;", "}" })
        {
            Assert.DoesNotContain($"raise: {unchangedLine}", beforeBody, StringComparison.Ordinal);
            Assert.DoesNotContain($"raise: {unchangedLine}", afterBody, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CompareStructure_DoesNotNarrowUsingDeclarationWhenVariableIsRead()
    {
        // Close negative for item 10: same declaration-dropped header shape
        // as the previous test, but the body now reads `iDisposable` (via
        // `iDisposable.Dispose()`), so item 10's liveness check must refuse
        // to narrow further or caption "never read" -- that claim would be
        // false, and dropping a variable that is actually read is not an
        // equivalent rewrite in the first place. The row falls back to
        // items 2/7's header-only narrowing and generic caption.
        const string beforeText = """
            int n = 0;
            using (IDisposable iDisposable = DisposableFromObjectSpan([a, b]))
            {
                iDisposable.Dispose();
            }
            return n;
            """;
        const string afterText = """
            int n = 0;
            using (DisposableFromObjectSpan([a, b]))
            {
                iDisposable.Dispose();
            }
            return n;
            """;

        var before = UsingStatementDocument(beforeText);
        var after = UsingStatementDocument(afterText);

        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        var row = Assert.Single(comparison.Rows);
        var beforeSpan = Assert.Single(row.BeforeSpans);
        var afterSpan = Assert.Single(row.AfterSpans);
        Assert.Equal(
            "using (IDisposable iDisposable = DisposableFromObjectSpan([a, b]))",
            before.Text.Substring(beforeSpan.Start, beforeSpan.Length));
        Assert.Equal(
            "using (DisposableFromObjectSpan([a, b]))",
            after.Text.Substring(afterSpan.Start, afterSpan.Length));

        var display = Assert.Single(CSharpStructuralDiffPrinter.ToDisplayRows(comparison));
        Assert.DoesNotContain("never read", display.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareStructure_DoesNotRefineUsingDeclarationWhenBodyAlsoChanged()
    {
        // Close negative for item 10: the declaration is dropped (as in the
        // narrowing test above), but the body also gains a statement, so
        // items 2/7's own header-narrowing (`NarrowToChangedHeader`) falls
        // back to the full node span instead of the Header region -- the
        // text outside a naive header span would then differ between before
        // and after. Item 10 must refuse to refine that fallback span: its
        // `using (` prefix would otherwise fool `UsingHeaderInnerSpan` into
        // scanning for a closing paren across the whole (differing) body,
        // narrowing to a bogus, mid-token substring instead of an honest
        // full-construct span.
        const string beforeText = """
            int n = 0;
            using (IDisposable iDisposable = DisposableFromObjectSpan([a, b]))
            {
                Bar();
            }
            return n;
            """;
        const string afterText = """
            int n = 0;
            using (DisposableFromObjectSpan([a, b]))
            {
                Bar();
                n = 1;
            }
            return n;
            """;

        var before = UsingStatementDocument(beforeText);
        var after = UsingStatementDocument(afterText);

        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        var row = Assert.Single(comparison.Rows);
        var beforeSpan = Assert.Single(row.BeforeSpans);
        var afterSpan = Assert.Single(row.AfterSpans);

        // Not narrowed to `IDisposable iDisposable =` / the bare resource
        // expression -- since the body also changed, items 2/7's own
        // header-narrowing already fell back to the full node span, and
        // item 10 must leave that fallback alone rather than reaching past
        // its own side's matching closing paren into the (differing) body.
        Assert.NotEqual(
            "IDisposable iDisposable =",
            before.Text.Substring(beforeSpan.Start, beforeSpan.Length));
        Assert.NotEqual(
            "DisposableFromObjectSpan([a, b])",
            after.Text.Substring(afterSpan.Start, afterSpan.Length));
        Assert.NotEqual(PrintedRegionRole.Header, row.BeforeRegion);
        Assert.NotEqual(PrintedRegionRole.Header, row.AfterRegion);

        var display = Assert.Single(CSharpStructuralDiffPrinter.ToDisplayRows(comparison));
        Assert.DoesNotContain("never read", display.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareStructure_NarrowsUsingStatementDeclarationAddedToTypeIdentifierEquals()
    {
        // Mirror direction of item 10: a variable-less resource gains a
        // declaration (before has none, after declares `iDisposable`, never
        // read in the untouched body). The row narrows symmetrically: the
        // bare expression on the before side, `Type identifier =` on the
        // after side, with captions swapped to match.
        const string beforeText = """
            int n = 0;
            using (DisposableFromObjectSpan([a, b]))
            {
                n = 1;
            }
            return n;
            """;
        const string afterText = """
            int n = 0;
            using (IDisposable iDisposable = DisposableFromObjectSpan([a, b]))
            {
                n = 1;
            }
            return n;
            """;

        var before = UsingStatementDocument(beforeText);
        var after = UsingStatementDocument(afterText);

        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        var row = Assert.Single(comparison.Rows);
        var beforeSpan = Assert.Single(row.BeforeSpans);
        var afterSpan = Assert.Single(row.AfterSpans);
        Assert.Equal(
            "DisposableFromObjectSpan([a, b])",
            before.Text.Substring(beforeSpan.Start, beforeSpan.Length));
        Assert.Equal(
            "IDisposable iDisposable =",
            after.Text.Substring(afterSpan.Start, afterSpan.Length));

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);
        string afterBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.After);

        AssertCaret(
            beforeBody,
            "DisposableFromObjectSpan([a, b])",
            "raise: UsingStatement header; variable-less resource (declaration added; never read)");
        AssertCaret(
            afterBody,
            "IDisposable iDisposable =",
            "raise: UsingStatement header; declares variable `iDisposable` (never read)");

        var display = Assert.Single(CSharpStructuralDiffPrinter.ToDisplayRows(comparison));
        Assert.Equal(
            "header: variable declaration added (`iDisposable` never read)",
            display.Detail);
    }

    [Fact]
    public void CompareStructure_DoesNotNarrowMovedOnlyRowsWithUnchangedHeaderText()
    {
        // Round-1 review (both reviewers, independently): NarrowToChangedHeader
        // ran for every matched pair, including Moved-only rows whose text is
        // completely unchanged on both sides -- the prefix/suffix equality
        // checks trivially pass in that case, so a moved statement's row would
        // be narrowed down to just its header even though nothing about the
        // header itself changed. Narrowing is now gated on the row actually
        // carrying the Changed flag; a Moved-only row keeps the node's full
        // span.
        const string text = """
            using (IDisposable iDisposable = DisposableFromObjectSpan([a, b]))
            {
                n = 1;
            }
            """;

        var before = UsingStatementDocument(text);
        var after = UsingStatementDocument(text);

        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            before,
            after,
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0, Moved: true)]));

        var row = Assert.Single(comparison.Rows);
        Assert.Equal(CSharpStructuralChangeKind.Moved, row.Change);
        var beforeSpan = Assert.Single(row.BeforeSpans);
        var afterSpan = Assert.Single(row.AfterSpans);
        Assert.Equal(before.Nodes[0].Spans[0], beforeSpan);
        Assert.Equal(after.Nodes[0].Spans[0], afterSpan);
    }

    [Fact]
    public void CompareStructure_DoesNotAdoptNestedConstructHeaderForHeaderlessAncestor()
    {
        // Round-1 review (both reviewers, independently): document.Regions is
        // a flat, node-identity-free list of positional spans, so containment
        // alone cannot prove a Header region belongs to the node being
        // narrowed rather than to a nested construct inside its body.
        // TryCatch/TryFinally never record their own Header region (see
        // HasNamedRegions in CSharpPrinter.cs), so a naive containment search
        // could mistake a nested UsingStatement's header for the enclosing
        // TryStatement's own header. Both node kinds are matched here, with
        // only the nested using's header text changing; the TryStatement row
        // must keep its full span (so item 1's ancestor-suppression -- which
        // requires the ancestor to strictly contain the descendant -- still
        // recognizes and suppresses it), never the nested header's span.
        const string beforeText = """
            try
            {
                using (IDisposable iDisposable = DisposableFromObjectSpan([a, b]))
                {
                    n = 1;
                }
            }
            catch
            {
                Recover();
            }
            """;
        const string afterText = """
            try
            {
                using (DisposableFromObjectSpan([a, b]))
                {
                    n = 1;
                }
            }
            catch
            {
                Recover();
            }
            """;

        var before = TryStatementWithNestedUsingDocument(beforeText);
        var after = TryStatementWithNestedUsingDocument(afterText);

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

        // Item 1 (ancestor collapsing) recognizes the nested UsingStatement's
        // narrowed header row as the sole explanation for the TryStatement's
        // text change and suppresses the redundant ancestor row, using the
        // items-2/7 header-level span (before item 10's further declaration
        // narrowing runs -- see RefineUsingResourceDeclarationRows). The
        // surviving row is then refined by item 10 same as any other, since
        // `iDisposable` is never read in the nested using's own body.
        var row = Assert.Single(comparison.Rows);
        Assert.Equal(1, row.BeforeNodeId);
        Assert.Equal(1, row.AfterNodeId);
        var beforeSpan = Assert.Single(row.BeforeSpans);
        var afterSpan = Assert.Single(row.AfterSpans);
        Assert.Equal(
            "IDisposable iDisposable =",
            before.Text.Substring(beforeSpan.Start, beforeSpan.Length));
        Assert.Equal(
            "DisposableFromObjectSpan([a, b])",
            after.Text.Substring(afterSpan.Start, afterSpan.Length));
    }

    static AnnotatedSourceDocument TryStatementWithNestedUsingDocument(string text)
    {
        int tryStart = text.IndexOf("try", StringComparison.Ordinal);
        int tryEnd = text.Length;

        int headerStart = text.IndexOf("using (", StringComparison.Ordinal);
        int headerEnd = text.IndexOf('\n', headerStart);
        int bodyStart = text.IndexOf('{', headerEnd);
        int bodyEnd = text.IndexOf('}', bodyStart) + 1;

        var tryNode = new AnnotatedSourceNode(
            0,
            "TryStatement",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(tryStart, tryEnd - tryStart)]);
        var usingNode = new AnnotatedSourceNode(
            1,
            "UsingStatement",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(headerStart, bodyEnd - headerStart)]);
        var regions = (AnnotatedSourceRegion[])
        [
            // Matches real printer output: TryCatch is in HasNamedRegions and
            // so records its own Construct region (spanning the whole
            // try/catch), but -- per HasNamedRegions -- never a Header
            // region; only the nested UsingStatement does.
            new(PrintedRegionRole.Construct, [new AnnotatedSourceSpan(tryStart, tryEnd - tryStart)]),
            new(PrintedRegionRole.Construct, [new AnnotatedSourceSpan(headerStart, bodyEnd - headerStart)]),
            new(PrintedRegionRole.Header, [new AnnotatedSourceSpan(headerStart, headerEnd - headerStart)]),
            new(PrintedRegionRole.Body, [new AnnotatedSourceSpan(bodyStart, bodyEnd - bodyStart)]),
        ];
        return new AnnotatedSourceDocument(text, [tryNode, usingNode], regions, [], []);
    }

    static AnnotatedSourceDocument UsingStatementDocument(string text)
    {
        int headerStart = text.IndexOf("using (", StringComparison.Ordinal);
        int headerEnd = text.IndexOf('\n', headerStart);
        int bodyStart = text.IndexOf('{', headerEnd);
        int bodyEnd = text.IndexOf('}', bodyStart) + 1;

        var node = new AnnotatedSourceNode(
            0,
            "UsingStatement",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(headerStart, bodyEnd - headerStart)]);
        var regions = (AnnotatedSourceRegion[])
        [
            // Matches real printer output (CSharpPrinter.cs, HasNamedRegions):
            // every header-bearing statement also records a Construct region
            // spanning the whole statement, alongside its own Header region.
            new(PrintedRegionRole.Construct, [new AnnotatedSourceSpan(headerStart, bodyEnd - headerStart)]),
            new(PrintedRegionRole.Header, [new AnnotatedSourceSpan(headerStart, headerEnd - headerStart)]),
            new(PrintedRegionRole.Body, [new AnnotatedSourceSpan(bodyStart, bodyEnd - bodyStart)]),
        ];
        return new AnnotatedSourceDocument(text, [node], regions, [], []);
    }

    [Fact]
    public void RenderAnnotatedBody_DescribesQualifierArgumentRoleTransition()
    {
        // Item 3 (issue #5022): once item 1 collapses #4942's stacked rows to
        // the single most-specific InvocationExpression row, its caption
        // should describe each side's own role ("qualifier" vs "argument 1"),
        // not dump the other side's entire text.
        const string beforeText = "receiver.Values(typeof(Attribute), true)";
        const string afterText = "Values(receiver, typeof(Attribute), true)";
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            Document(beforeText, "InvocationExpression", beforeText),
            Document(afterText, "InvocationExpression", afterText),
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);
        string afterBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.After);

        Assert.Contains(
            "raise: InvocationExpression; receiver: used as extension-call qualifier",
            beforeBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "raise: InvocationExpression; receiver: moved to argument 1 (static call)",
            afterBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("changed to", beforeBody, StringComparison.Ordinal);
        Assert.DoesNotContain("changed from", afterBody, StringComparison.Ordinal);

        var display = Assert.Single(CSharpStructuralDiffPrinter.ToDisplayRows(comparison));
        Assert.Equal(
            "receiver: qualifier -> argument 1 (extension -> static call)",
            display.Detail);
    }

    [Fact]
    public void RenderAnnotatedBody_DescribesReverseArgumentQualifierRoleTransition()
    {
        // Mirror direction: a static call's first argument becomes the
        // extension-call qualifier.
        const string beforeText = "Values(receiver, typeof(Attribute), true)";
        const string afterText = "receiver.Values(typeof(Attribute), true)";
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            Document(beforeText, "InvocationExpression", beforeText),
            Document(afterText, "InvocationExpression", afterText),
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);
        string afterBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.After);

        Assert.Contains(
            "raise: InvocationExpression; receiver: argument 1 (static call)",
            beforeBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "raise: InvocationExpression; receiver: moved to extension-call qualifier",
            afterBody,
            StringComparison.Ordinal);

        var display = Assert.Single(CSharpStructuralDiffPrinter.ToDisplayRows(comparison));
        Assert.Equal(
            "receiver: argument 1 -> qualifier (static -> extension call)",
            display.Detail);
    }

    [Fact]
    public void RenderAnnotatedBody_FallsBackToTextDumpWhenQualifierRoleShapeIsNotRecognized()
    {
        // A callee rename is not the narrow "receiver becomes an argument"
        // shape item 3 targets, so this must fall back to the honest
        // "changed to/from" text dump rather than guessing.
        const string beforeText = "receiver.Values(typeof(Attribute), true)";
        const string afterText = "receiver.OtherValues(typeof(Attribute), true)";
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            Document(beforeText, "InvocationExpression", beforeText),
            Document(afterText, "InvocationExpression", afterText),
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);

        Assert.Contains(
            $"raise: InvocationExpression; changed to {afterText}",
            beforeBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAnnotatedBody_FallsBackToTextDumpWhenArgumentChangeAccompaniesQualifierMove()
    {
        // Regression for round-1 review: the qualifier looks like it moved
        // into the argument list, but a *different* argument also changed
        // ("old" -> "new"). Confirming only that the qualifier's text occurs
        // somewhere in the other side's arguments (without checking the rest
        // of the argument list is preserved) would produce a role caption
        // that silently hides the unrelated argument change. This must fall
        // back to the literal text dump instead.
        const string beforeText = "receiver.Values(old)";
        const string afterText = "Values(new, receiver)";
        var comparison = CSharpBodyDiff.CompareStructure(new(
            "M",
            Document(beforeText, "InvocationExpression", beforeText),
            Document(afterText, "InvocationExpression", afterText),
            [0],
            [0],
            [new CSharpNodeCorrespondence(0, 0)]));

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);

        Assert.Contains(
            $"raise: InvocationExpression; changed to {afterText}",
            beforeBody,
            StringComparison.Ordinal);

        var display = Assert.Single(CSharpStructuralDiffPrinter.ToDisplayRows(comparison));
        Assert.Equal($"{beforeText} -> {afterText}", display.Detail);
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
    public void RenderAnnotatedBody_IndentedExtentAlignsCaretToFirstCoveredToken()
    {
        const string beforeText =
            "int value = 0;\n" +
            "    if (value == 0)\n" +
            "    {\n" +
            "        return value;\n" +
            "    }";
        const string afterText =
            "int value = 0;\n" +
            "    if (value == 0)\n" +
            "    {\n" +
            "        break;\n" +
            "    }";
        int beforeStart = beforeText.IndexOf("        return value;", StringComparison.Ordinal);
        int afterStart = afterText.IndexOf("        break;", StringComparison.Ordinal);
        var before = new AnnotatedSourceDocument(
            beforeText,
            [new AnnotatedSourceNode(
                0,
                "ReturnStatement",
                SourceLineKind.CSharp,
                [new(beforeStart, "        return value;".Length)])],
            [],
            [],
            []);
        var after = new AnnotatedSourceDocument(
            afterText,
            [new AnnotatedSourceNode(
                0,
                "BreakStatement",
                SourceLineKind.CSharp,
                [new(afterStart, "        break;".Length)])],
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
        Assert.Equal(
            new AnnotatedSourceSpan(beforeStart, "        return value;".Length),
            Assert.Single(Assert.Single(comparison.Rows).BeforeSpans));

        string[] rendered = CSharpStructuralDiffPrinter
            .RenderAnnotatedBody(comparison, CSharpStructuralSide.Before)
            .Split('\n');
        int sourceLineIndex = Array.FindIndex(
            rendered,
            static line => line.Contains("return value;", StringComparison.Ordinal));

        Assert.True(sourceLineIndex >= 0);
        string caretLine = rendered[sourceLineIndex + 1];
        string detailLine = rendered[sourceLineIndex + 2];
        Assert.Equal(
            rendered[sourceLineIndex].IndexOf("return", StringComparison.Ordinal),
            caretLine.IndexOf('^'));
        Assert.StartsWith("//", caretLine, StringComparison.Ordinal);
        Assert.Equal(caretLine.IndexOf('^'), detailLine.IndexOf("raise:", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderAnnotatedBody_TabIndentedExtentPreservesTabAlignment()
    {
        const string beforeText = "int value = 0;\n\t\t\treturn value;";
        const string afterText = "int value = 0;\n\t\t\tbreak;";
        int beforeStart = beforeText.IndexOf("\t\t\treturn value;", StringComparison.Ordinal);
        int afterStart = afterText.IndexOf("\t\t\tbreak;", StringComparison.Ordinal);
        var before = new AnnotatedSourceDocument(
            beforeText,
            [new AnnotatedSourceNode(
                0,
                "ReturnStatement",
                SourceLineKind.CSharp,
                [new(beforeStart, "\t\t\treturn value;".Length)])],
            [],
            [],
            []);
        var after = new AnnotatedSourceDocument(
            afterText,
            [new AnnotatedSourceNode(
                0,
                "BreakStatement",
                SourceLineKind.CSharp,
                [new(afterStart, "\t\t\tbreak;".Length)])],
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
            .RenderAnnotatedBody(comparison, CSharpStructuralSide.Before)
            .Split('\n');

        Assert.Equal("\t\t\treturn value;", rendered[1]);
        Assert.Equal("\t\t\t^^^^^^^^^^^^^", rendered[2]);
        Assert.StartsWith("\t\t\traise: Return", rendered[3], StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAnnotatedBody_TabbedMemberIndentUsesExactFallback()
    {
        const string beforeText = "\tvoid M()\n    return;";
        const string afterText = "\tvoid M()\n    break;";
        int beforeStart = beforeText.IndexOf("    return;", StringComparison.Ordinal);
        int afterStart = afterText.IndexOf("    break;", StringComparison.Ordinal);
        var before = new AnnotatedSourceDocument(
            beforeText,
            [new AnnotatedSourceNode(
                0,
                "ReturnStatement",
                SourceLineKind.CSharp,
                [new(beforeStart, "    return;".Length)])],
            [],
            [],
            []);
        var after = new AnnotatedSourceDocument(
            afterText,
            [new AnnotatedSourceNode(
                0,
                "BreakStatement",
                SourceLineKind.CSharp,
                [new(afterStart, "    break;".Length)])],
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
            .RenderAnnotatedBody(comparison, CSharpStructuralSide.Before)
            .Split('\n');

        Assert.Equal("    return;", rendered[1]);
        Assert.Equal("    ^^^^^^^", rendered[2]);
        Assert.StartsWith("    raise: Return", rendered[3], StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAnnotatedBody_EarlyColumnSpansUseExactGutterFreeCarets()
    {
        // The InvocationExpression's callee ("a" -> "z") differs independently
        // of the nested NameExpression's own text ("b" -> "c"), so its row is
        // not subsumed by item 1's ancestor-collapsing (issue #5022): both
        // rows carry genuinely distinct information and both must still stack.
        const string beforeText = "a(b);";
        const string afterText = "z(c);";
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
        Assert.Equal("^^^^", rendered[1]);
        Assert.StartsWith("raise: InvocationExpression", rendered[2], StringComparison.Ordinal);
        Assert.Equal("  ^", rendered[3]);
        Assert.StartsWith("  raise: NameExpression", rendered[4], StringComparison.Ordinal);
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
        Assert.Equal("    ^^", rendered[2]);
        Assert.StartsWith("    raise: Break", rendered[3], StringComparison.Ordinal);
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
    public void IssueCorrespondence_InfersAddedLocalFunctionDeclarationAlongsideMatchedCallSite()
    {
        // #4116's shape (issue #5022 item 5): an undeclared synthesized call
        // is rewritten to call a declared local function. The call site keeps
        // the same IL origin on both sides (matched, Changed); the new
        // declaration header has no IL origin of its own (only its body
        // would), and is the only such declaration in the document.
        var before = TrustedDocument(
            "return __NoTypeParameter_g__Own_0_0(value);",
            new NodeSpec("InvocationExpression", "__NoTypeParameter_g__Own_0_0(value)", [0x10]));
        var after = TrustedDocument(
            "return Own(value);\nstatic int Own(int input) => input + 1;",
            new NodeSpec("InvocationExpression", "Own(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static int Own(int input) => input + 1;", null));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        Assert.Empty(issued.UnmatchedBefore);
        var declaration = Assert.Single(issued.UnmatchedAfter);
        Assert.Equal(CSharpUnmatchedNodeReason.InferredDeclaration, declaration.Reason);

        var comparison = CSharpBodyDiff.CompareStructure(issued);
        Assert.True(comparison.IsCorrespondenceComplete);
        Assert.Equal(2, comparison.Rows.Length);
        var changed = Assert.Single(comparison.Rows, row => row.Change == CSharpStructuralChangeKind.Changed);
        Assert.Equal("InvocationExpression", changed.BeforeKind);
        var added = Assert.Single(comparison.Rows, row => row.Change == CSharpStructuralChangeKind.Added);
        Assert.Equal("LocalFunctionStatement", added.AfterKind);

        // End-to-end: the declaration gets its own Added display row and
        // detail, instead of being silently dropped (#3902's "zero matched
        // structural-diff rows" / #4116's "declaration falls into the gap
        // bucket" problem).
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);
        Assert.Contains(display, row =>
            row.Change == "Added" && row.Detail == "+ static int Own(int input) => input + 1;");

        // Matching #4952's corpus mockup for this exact PR shape: the added
        // declaration now gets its own caret in the rendered After body,
        // instead of appearing with no annotation at all.
        string renderedAfter = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison, CSharpStructuralSide.After);
        Assert.Contains(
            "static int Own(int input) => input + 1;\n"
            + "^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^\n"
            + "raise: LocalFunctionStatement",
            renderedAfter);
    }

    [Fact]
    public void IssueCorrespondence_InfersRemovedLocalFunctionDeclarationAlongsideMatchedCallSite()
    {
        // Symmetric removal direction: a declared local function is inlined
        // back to an undeclared synthesized call. The declaration disappears
        // from the before side with no IL origin of its own.
        var before = TrustedDocument(
            "F();\nstatic void F()\n{\n}",
            new NodeSpec("InvocationExpression", "F()", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void F()\n{\n}", null));
        var after = TrustedDocument(
            "__CallsEmpty_g__F_0_0();",
            new NodeSpec("InvocationExpression", "__CallsEmpty_g__F_0_0()", [0x10]));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        var declaration = Assert.Single(issued.UnmatchedBefore);
        Assert.Equal(CSharpUnmatchedNodeReason.InferredDeclaration, declaration.Reason);
        Assert.Empty(issued.UnmatchedAfter);

        var comparison = CSharpBodyDiff.CompareStructure(issued);
        Assert.True(comparison.IsCorrespondenceComplete);
        Assert.Equal(2, comparison.Rows.Length);
        var removed = Assert.Single(comparison.Rows, row => row.Change == CSharpStructuralChangeKind.Removed);
        Assert.Equal("LocalFunctionStatement", removed.BeforeKind);

        // Symmetric to the Added case: the removed declaration gets its own
        // caret in the rendered Before body, instead of falling through with
        // no annotation at all.
        string renderedBefore = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison, CSharpStructuralSide.Before);
        Assert.Contains(
            "static void F()\n"
            + "^^^^^^^^^^^^^^^\n"
            + "raise: LocalFunctionStatement",
            renderedBefore);
    }

    [Fact]
    public void ToDisplayRows_DescribesCallTargetRenamedToLocalFunction()
    {
        // Item 9 (issue #5022): the exact #4116 shape as item 5's own Added
        // fixture above, but now checking the Changed InvocationExpression
        // row's own Detail -- it should name the semantic role transition
        // ("call target: ... -> local function `Own`"), not a literal
        // before/after text dump, since the after-side callee is precisely
        // the declaration this same comparison reports as Added.
        var before = TrustedDocument(
            "return __NoTypeParameter_g__Own_0_0(value);",
            new NodeSpec("InvocationExpression", "__NoTypeParameter_g__Own_0_0(value)", [0x10]));
        var after = TrustedDocument(
            "return Own(value);\nstatic int Own(int input) => input + 1;",
            new NodeSpec("InvocationExpression", "Own(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static int Own(int input) => input + 1;", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        Assert.Contains(display, row =>
            row.Change == "Changed"
            && row.Detail == "call target: __NoTypeParameter_g__Own_0_0 -> local function `Own`");
    }

    [Fact]
    public void ToDisplayRows_DescribesCallTargetRenamedFromLocalFunction()
    {
        // Symmetric removal direction: the exact #4116-reverse shape as item
        // 5's own Removed fixture above, checked for the same Detail
        // captioning on the Changed invocation row.
        var before = TrustedDocument(
            "F();\nstatic void F()\n{\n}",
            new NodeSpec("InvocationExpression", "F()", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void F()\n{\n}", null));
        var after = TrustedDocument(
            "__CallsEmpty_g__F_0_0();",
            new NodeSpec("InvocationExpression", "__CallsEmpty_g__F_0_0()", [0x10]));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        Assert.Contains(display, row =>
            row.Change == "Changed"
            && row.Detail == "call target: local function `F` -> __CallsEmpty_g__F_0_0");
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeUnrelatedCalleeRenameAsLocalFunction()
    {
        // Close negative: the callee genuinely renamed (`Old` -> `New`), but
        // no Added/Removed LocalFunctionStatement row names either identifier
        // anywhere in this comparison -- an ordinary method rename, not the
        // #3902/#4116 paired-declaration shape. The caption must not fire on
        // callee identity alone; the row must fall back to the literal
        // before/after text.
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return New(value);",
            new NodeSpec("InvocationExpression", "New(value)", [0x10]));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> New(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeCalleeRenameWhenNameOnlyMatchesADeclarationParameter()
    {
        // Round-1 review (reviewers A and B): the renamed callee's identity
        // must match the local function's own *declared name*, not merely
        // any identifier occurring anywhere in the declaration's full
        // statement text. Here the sole Added LocalFunctionStatement is
        // named `Other`, but its parameter happens to be named `New` --
        // exactly the identifier the unrelated call was renamed to. This
        // must not be read as "the callee now targets a local function";
        // there is no such declaration.
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return New(value);\nstatic void Other(int New) { }",
            new NodeSpec("InvocationExpression", "New(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void Other(int New) { }", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> New(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DescribesCallTargetRenamedToEscapedKeywordLocalFunction()
    {
        // Round-2 review (reviewers A and B): a local function whose declared
        // name is a C#-keyword must be @-escaped at both its declaration and
        // its call site (#1465). The declared-name extraction must keep the
        // leading '@' so it exactly matches the callee text (which likewise
        // keeps the '@'), or the caption is silently lost for every escaped
        // name.
        var before = TrustedDocument(
            "return Synthesized(value);",
            new NodeSpec("InvocationExpression", "Synthesized(value)", [0x10]));
        var after = TrustedDocument(
            "return @return(value);\nstatic int @return(int value) => value;",
            new NodeSpec("InvocationExpression", "@return(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static int @return(int value) => value;", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Contains("local function `@return`", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeUnrelatedCalleeRenameWhenDeclarationNameIsEscapedModifierKeyword()
    {
        // Round-2 review (reviewer A): before the '@'-inclusion fix, an
        // escaped declaration literally named a modifier keyword (`@static`)
        // was wrongly rejected as if it were the unescaped modifier itself,
        // and the scan then continued past it into the declaration's body,
        // where it could find and falsely match an unrelated call's renamed
        // callee. Here the added `@static` declaration's body invokes `New`,
        // matching this comparison's own unrelated call-site rename, but the
        // declaration's own name is `@static`, not `New` -- the caption must
        // not fire.
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return New(value);\nstatic void @static() { New(0); }",
            new NodeSpec("InvocationExpression", "New(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void @static() { New(0); }", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> New(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeUnrelatedCalleeRenameWhenDeclarationIsGeneric()
    {
        // Round-3 review (reviewers A and B): before the bail-instead-of-
        // continue fix, a generic local function's type-parameter list
        // (`<T>`) left no identifier immediately before the real parameter
        // list, and the scan then fell through into the declaration's body,
        // where it could pick up an unrelated call there and misattribute
        // it as the declaration's own name. Here the added `Other<T>`
        // declaration's body happens to invoke `New`, matching this
        // comparison's own unrelated call-site rename, but the declaration's
        // actual name is `Other`, not `New` -- the caption must not fire.
        // (Generic declarations are not yet recognized by this heuristic, so
        // no caption is expected here at all -- only that none is wrongly
        // produced.)
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return New(value);\nstatic void Other<T>() { New(0); }",
            new NodeSpec("InvocationExpression", "New(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void Other<T>() { New(0); }", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> New(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeAttributeArgumentAsDeclaredName()
    {
        // Round-3 review (reviewers A and B): a local function's own
        // attribute list is a top-level parenthesized group like any other,
        // so before the leading-attribute-skip fix, an attribute with
        // arguments (`[My(1)]`) was mistaken for the declaration's parameter
        // list, and the identifier immediately preceding it (`My`) was
        // returned as the declared name instead of the declaration's real
        // name (`Other`). Here the unrelated call is renamed to exactly
        // that attribute-argument identifier (`My`), which must not trigger
        // the caption.
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return My(value);\n[My(1)] static void Other() { }",
            new NodeSpec("InvocationExpression", "My(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "[My(1)] static void Other() { }", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> My(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeAttributeArgumentContainingBracketAsDeclaredName()
    {
        // Round-4 review (reviewers A and B): before the guard on quotes
        // inside SkipLeadingAttributeLists, an attribute argument string
        // containing '[' (e.g. an attribute describing something as
        // "[deprecated]") made the bracket-balance count in
        // SkipLeadingAttributeLists appear unbalanced, so the attribute list
        // was never skipped and the attribute's own argument identifier
        // (`A`) was mistaken for the declared name -- exactly the round-3
        // false positive this was meant to have already fixed. The unrelated
        // call here is renamed to that attribute-argument identifier (`A`),
        // which must not trigger the caption.
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return A(value);\n[A(\"[deprecated]\")] static void Other() { }",
            new NodeSpec("InvocationExpression", "A(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "[A(\"[deprecated]\")] static void Other() { }", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> A(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DescribesCallTargetRenamedToAttributedTupleReturningLocalFunction()
    {
        // Round-4 review (reviewer A): before bounding the trailing-
        // whitespace scan by `start` (rather than 0), an attributed, unmodified
        // tuple-return declaration lost its own caption -- a false negative,
        // since the whitespace between the attribute list and the tuple
        // return type's own opening paren was wrongly treated as a
        // non-empty gap, causing the scan to bail instead of taking the
        // tuple-return continuation.
        var before = TrustedDocument(
            "return Synthesized(value);",
            new NodeSpec("InvocationExpression", "Synthesized(value)", [0x10]));
        var after = TrustedDocument(
            "return F(value);\n[My] (int, string) F(int value) => (value, \"\");",
            new NodeSpec("InvocationExpression", "F(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "[My] (int, string) F(int value) => (value, \"\");", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        Assert.Contains(display, row =>
            row.Change == "Changed"
            && row.Detail == "call target: Synthesized -> local function `F`");
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeAttributeCommentBracketAsDeclaredName()
    {
        // Round-5 review (reviewers A and B): a comment inside an attribute's
        // own argument list can contain a bracket character invisible to
        // SkipLeadingAttributeLists's naive counting, corrupting it exactly
        // like the round-4 string-literal case. This codebase's own printer
        // never emits comments in a declaration header, so this is defense
        // in depth rather than a reachable product scenario -- but the
        // heuristic must still not produce a wrong caption if fed this text.
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return A(value);\n[A(/* [ */ 1)] static void Other() { }",
            new NodeSpec("InvocationExpression", "A(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "[A(/* [ */ 1)] static void Other() { }", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> A(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeCommentBetweenAttributeAndDeclarationAsDeclaredName()
    {
        // Round-5 review (reviewers A and B): a comment between a leading
        // attribute and the declaration itself is invisible to the
        // whitespace-only skip that follows an attribute list, so scanning
        // resumed inside it and could return an identifier found there (one
        // that merely precedes a parenthesized group in the comment text) as
        // the declared name. As above, the decompiler's own printer never
        // emits comments here; this guards the heuristic's contract anyway.
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return New(value);\n[A] /* New() */ static void Other() { }",
            new NodeSpec("InvocationExpression", "New(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "[A] /* New() */ static void Other() { }", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> New(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeDirectiveInMainScanAsDeclaredName()
    {
        // Round-10 review (reviewer A and reviewer B, independently):
        // round-9 recognized `#` as a body-start marker, reasoning that a
        // preprocessor directive could legally separate a parameter list
        // from its body. But a directive can just as legally separate a
        // *return type* from the *name* -- here, between the tuple return
        // type's own group and `Other` -- where `#` proves nothing about a
        // body starting. Recognizing it as proof there would wrongly stop
        // the scan at the tuple-return-type's own preceding modifier
        // (`static`) instead of continuing to the real name (`Other`). The
        // decompiler's own printer never emits directives here either; `#`
        // is now an unconditional bail in the main scan, mirroring the
        // existing '/' comment bail, rather than a body-start marker.
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return New(value);\nstatic (int, int)\n#line 1\nOther() => (1, 2);",
            new NodeSpec("InvocationExpression", "New(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static (int, int)\n#line 1\nOther() => (1, 2);", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> New(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeCommentInMainScanAsDeclaredName()
    {
        // Round-6 review (reviewers A and B): the round-5 fix only bailed on
        // '/' inside SkipLeadingAttributeLists, not in the main declaration
        // scan that runs after it. A comment later in the header -- here,
        // right after the `static` modifier -- still hides a parenthesized
        // group whose preceding identifier could be misread as the declared
        // name, attributing an unrelated call (`New`) to a same-named but
        // wholly different actual local function (`Other`).
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return New(value);\nstatic /* New() */ void Other() { }",
            new NodeSpec("InvocationExpression", "New(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static /* New() */ void Other() { }", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> New(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DescribesCallTargetRenamedToRefTupleReturningLocalFunction()
    {
        // Round-6 review (reviewer A): `ref` and `ref readonly` are valid
        // tokens preceding a tuple return type's own parenthesized group
        // (`ref (int, int) F()`), just like the already-recognized
        // modifiers. Before this fix, the scan mistook `ref` itself for the
        // declared name instead of continuing on to find the real
        // parameter list and its own preceding identifier `F`.
        var before = TrustedDocument(
            "return Synthesized(value);",
            new NodeSpec("InvocationExpression", "Synthesized(value)", [0x10]));
        var after = TrustedDocument(
            "return F(value);\nref (int, int) F(int value) => throw new Exception();",
            new NodeSpec("InvocationExpression", "F(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "ref (int, int) F(int value) => throw new Exception();", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        Assert.Contains(display, row =>
            row.Change == "Changed"
            && row.Detail == "call target: Synthesized -> local function `F`");
    }

    [Fact]
    public void ToDisplayRows_DescribesCallTargetRenamedToSupplementaryPlaneLocalFunction()
    {
        // Round-6 review (reviewer B): char-based identifier classification
        // examines each half of a supplementary-plane surrogate pair (e.g.
        // U+10400, DESERET CAPITAL LETTER LONG I) independently, and neither
        // half is itself an identifier-part category, so the whole
        // character was silently excluded from the declared name -- a false
        // negative that permanently lost the caption for a name spelled with
        // one.
        var before = TrustedDocument(
            "return Synthesized(value);",
            new NodeSpec("InvocationExpression", "Synthesized(value)", [0x10]));
        var after = TrustedDocument(
            "return \U00010400F(value);\nstatic int \U00010400F(int value) => value;",
            new NodeSpec("InvocationExpression", "\U00010400F(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static int \U00010400F(int value) => value;", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Contains("local function `\U00010400F`", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DescribesCallTargetRenamedToGenericTupleReturningLocalFunction()
    {
        // Round-7 review (reviewer A): a generic return type wrapping a
        // tuple (`Task<(int, int)> F()`) opens the tuple's own group while
        // still nested inside the return type's unclosed `<...>`, so
        // paren-only depth tracking mistook that nested group for the
        // top-level parameter list; its preceding `<` is not an identifier,
        // and the scan bailed before ever reaching the real parameter list
        // and its own preceding name (`F`). Tracking angle-bracket depth
        // alongside paren depth (while at paren depth zero) fixes this
        // without touching the already-correct rejection of a generic
        // *local function itself* (`Other<T>()`, covered above).
        var before = TrustedDocument(
            "return Synthesized(value);",
            new NodeSpec("InvocationExpression", "Synthesized(value)", [0x10]));
        var after = TrustedDocument(
            "return F(value);\nTask<(int Value, int Error)> F(int value) => default;",
            new NodeSpec("InvocationExpression", "F(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "Task<(int Value, int Error)> F(int value) => default;", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        Assert.Contains(display, row =>
            row.Change == "Changed"
            && row.Detail == "call target: Synthesized -> local function `F`");
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeUnrelatedCalleeRenameWhenDeclarationNameIsModifierKeyword()
    {
        // Round-8 review (reviewer A): `async` is a contextual keyword,
        // legal unescaped as an ordinary local-function name
        // (`void async() { }`). Before this fix, the scanner mistook that
        // name for the `async` modifier it also recognizes as legitimately
        // prefixing a tuple return type, and continued scanning past what
        // was actually the declaration's own (empty) parameter list and
        // into its body, where it could pick up an unrelated call there
        // (`New`) and misattribute it as this declaration's own name. Here
        // the added `async` declaration's body happens to invoke `New`,
        // matching this comparison's own unrelated call-site rename, but
        // the declaration's actual name is `async`, not `New` -- the
        // caption must not fire.
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return New(value);\nvoid async() { New(0); }",
            new NodeSpec("InvocationExpression", "New(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "void async() { New(0); }", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> New(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DoesNotDescribeUnrelatedCalleeRenameWhenExpressionBodiedDeclarationNameIsModifierKeyword()
    {
        // Same shape as above, but expression-bodied (`=>`) rather than
        // block-bodied (`{ }`) -- the body-start probe after a
        // modifier-keyword match must recognize both, not only `{`.
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return New(value);\nint async() => New(0);",
            new NodeSpec("InvocationExpression", "New(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "int async() => New(0);", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> New(value)", changed.Detail);
    }

    [Fact]
    public void ToDisplayRows_DescribesCallTargetRenamedToLocalFunctionNamedModifierKeyword()
    {
        // Round-9 review (reviewer B): the round-8 fix bailed whenever a
        // modifier-spelled token was immediately followed by a body,
        // throwing away a legitimate caption for any local function
        // actually named after a modifier keyword (`async`, a contextual
        // keyword, is a perfectly legal unescaped identifier). But a body
        // starting right there proves, rather than guesses, that this
        // token is the declaration's own name: no valid declaration puts a
        // real modifier or tuple-return-type prefix immediately before a
        // body -- it still needs a name and that name's own parameter list
        // first. So this shape must receive the caption, not fall back to
        // the literal text.
        var before = TrustedDocument(
            "return Synthesized(value);",
            new NodeSpec("InvocationExpression", "Synthesized(value)", [0x10]));
        var after = TrustedDocument(
            "return async(value);\nstatic int async(int value) => value;",
            new NodeSpec("InvocationExpression", "async(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static int async(int value) => value;", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        Assert.Contains(display, row =>
            row.Change == "Changed"
            && row.Detail == "call target: Synthesized -> local function `async`");
    }

    [Fact]
    public void ToDisplayRows_DescribesCallTargetRenamedToLocalFunctionNamedModifierKeywordAcrossPreprocessorDirective()
    {
        // Round-9 review (reviewer B) added `#` as a third body-start
        // marker, reasoning that a preprocessor directive (e.g. `#line`)
        // between the parameter list and the body -- legal trivia inside a
        // LocalFunctionStatement's own text -- could otherwise defeat the
        // probe and let the scan misattribute a body call as the
        // declaration's own name. Round-10 review (reviewer A and reviewer
        // B, independently) showed that same `#` marker is unsound: a
        // directive can just as legally separate a *return type* from the
        // *name*, where `#` proves nothing about a body starting. Since
        // the printer never emits directives in either position, `#` is
        // now an unconditional bail instead of a body-start marker -- this
        // shape isn't recognized at all, so no caption is produced, but
        // for the safer reason (bail) rather than the unsound one (proof).
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return New(value);\nvoid async()\n#line 1\n{\n    New(0);\n}",
            new NodeSpec("InvocationExpression", "New(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "void async()\n#line 1\n{\n    New(0);\n}", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> New(value)", changed.Detail);
    }


    [Fact]
    public void ToDisplayRows_DoesNotDescribeUnrelatedCalleeRenameWhenDeclarationHasUnspellableName()
    {
        // Round-9 review (reviewer A): the decompiler deliberately
        // preserves some local-function names that are not spellable C#
        // identifiers (e.g. `bad-name`, from an original IL name with no
        // valid C# rendering). The backward identifier scan stops at the
        // first non-identifier character, so scanning back from
        // `bad-name(`'s own parameter list only captures the valid-looking
        // suffix `name` -- not the declaration's real (unspellable) name.
        // Returning that suffix could coincidentally match an unrelated
        // call renamed to exactly that suffix, which must not receive the
        // caption: there is no local function actually named `name` here.
        var before = TrustedDocument(
            "return Old(value);",
            new NodeSpec("InvocationExpression", "Old(value)", [0x10]));
        var after = TrustedDocument(
            "return name(value);\nstatic void bad-name(int value) { }",
            new NodeSpec("InvocationExpression", "name(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void bad-name(int value) { }", null));

        var comparison = CSharpBodyDiff.CompareStructure(CSharpBodyDiff.IssueCorrespondence(before, after));
        var display = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);

        var changed = Assert.Single(display, row => row.Change == "Changed");
        Assert.Equal("Old(value) -> name(value)", changed.Detail);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotInferDeclarationWithoutMatchedCallSiteRewrite()
    {
        // Close negative: a new local-function declaration with no IL origin
        // of its own, but with no matched InvocationExpression call-site
        // rewrite anywhere in the document. This is the general "declaration
        // appears out of nowhere" case item 5 deliberately excludes (no
        // paired call-site evidence to key structural uniqueness off of), so
        // it must stay Unsupported like any other correspondence gap.
        var before = TrustedDocument(
            "return;",
            new NodeSpec("ReturnStatement", "return;", [0x10]));
        var after = TrustedDocument(
            "return;\nstatic void F()\n{\n}",
            new NodeSpec("ReturnStatement", "return;", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void F()\n{\n}", null));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        var declaration = Assert.Single(issued.UnmatchedAfter);
        Assert.Equal(CSharpUnmatchedNodeReason.Unsupported, declaration.Reason);
        Assert.False(CSharpBodyDiff.CompareStructure(issued).IsCorrespondenceComplete);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotTreatArgumentOnlyInvocationChangeAsCallSiteRewrite()
    {
        // Close negative (round-7 review, reviewers A and B): a matched
        // InvocationExpression pair whose callee is unchanged but whose
        // arguments differ (Log(1) -> Log(2)) must not itself license an
        // unrelated new local-function declaration elsewhere in the document
        // as an inferred rewrite target. The call's target never changed, so
        // this is not evidence that anything was rewritten alongside it.
        var before = TrustedDocument(
            "Log(1);",
            new NodeSpec("InvocationExpression", "Log(1)", [0x10]));
        var after = TrustedDocument(
            "Log(2);\nstatic void F()\n{\n}",
            new NodeSpec("InvocationExpression", "Log(2)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void F()\n{\n}", null));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        var declaration = Assert.Single(issued.UnmatchedAfter);
        Assert.Equal(CSharpUnmatchedNodeReason.Unsupported, declaration.Reason);
        Assert.False(CSharpBodyDiff.CompareStructure(issued).IsCorrespondenceComplete);
    }

    [Fact]
    public void IssueCorrespondence_InfersDeclarationWhenCalleeRewriteHasParenthesizedReceiver()
    {
        // Close positive (round-8 review, reviewers A and B): a matched
        // InvocationExpression pair whose receiver itself contains balanced
        // parentheses (a cast, `((IFoo)value)`) must still have its true
        // callee correctly compared. A naive "first '(' in the text" split
        // would misidentify the callee split point -- the text starts with
        // '(' -- and wrongly decline to detect this as a genuine rewrite.
        var before = TrustedDocument(
            "return ((IFoo)value).Old();",
            new NodeSpec("InvocationExpression", "((IFoo)value).Old()", [0x10]));
        var after = TrustedDocument(
            "return ((IFoo)value).New();\nstatic int New()\n{\n    return 1;\n}",
            new NodeSpec("InvocationExpression", "((IFoo)value).New()", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static int New()\n{\n    return 1;\n}", null));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        var declaration = Assert.Single(issued.UnmatchedAfter);
        Assert.Equal(CSharpUnmatchedNodeReason.InferredDeclaration, declaration.Reason);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotTreatUnchangedCalleeWithNestedInvocationReceiverAsRewrite()
    {
        // Close negative, symmetric to the parenthesized-receiver positive
        // above: a call-returning receiver (`GetReceiver()`) also contains a
        // balanced paren pair before the true callee's own argument list.
        // The callee (`GetReceiver().Log`) is unchanged here -- only the
        // argument differs -- so this must not license an unrelated
        // declaration as a rewrite target, proving the balanced scan finds
        // the correct split rather than merely the first or last paren.
        var before = TrustedDocument(
            "GetReceiver().Log(1);",
            new NodeSpec("InvocationExpression", "GetReceiver().Log(1)", [0x10]));
        var after = TrustedDocument(
            "GetReceiver().Log(2);\nstatic void F()\n{\n}",
            new NodeSpec("InvocationExpression", "GetReceiver().Log(2)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void F()\n{\n}", null));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        var declaration = Assert.Single(issued.UnmatchedAfter);
        Assert.Equal(CSharpUnmatchedNodeReason.Unsupported, declaration.Reason);
        Assert.False(CSharpBodyDiff.CompareStructure(issued).IsCorrespondenceComplete);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotTreatArgumentLiteralParenthesesAsCalleeRewrite()
    {
        // Close negative (round-9 review, reviewers A and B): an
        // argument-only edit where the changed argument is a string literal
        // that itself contains a '(' character (Log("(") -> Log("changed("))
        // must decline callee comparison entirely rather than let the
        // literal's unbalanced paren either masquerade as the argument
        // list's true boundary or make the scan fail to find a balanced
        // match at all (which would otherwise silently fall back to
        // comparing full invocation text -- reintroducing the exact
        // argument-only false positive round 7 fixed). The callee ("Log")
        // is unchanged, so this must not license an unrelated declaration.
        var before = TrustedDocument(
            "Log(\"(\");",
            new NodeSpec("InvocationExpression", "Log(\"(\")", [0x10]));
        var after = TrustedDocument(
            "Log(\"changed(\");\nstatic void F()\n{\n}",
            new NodeSpec("InvocationExpression", "Log(\"changed(\")", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void F()\n{\n}", null));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        var declaration = Assert.Single(issued.UnmatchedAfter);
        Assert.Equal(CSharpUnmatchedNodeReason.Unsupported, declaration.Reason);
        Assert.False(CSharpBodyDiff.CompareStructure(issued).IsCorrespondenceComplete);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotTreatCommentParenthesesAsCalleeRewrite()
    {
        // Close negative (round-10 review, reviewer A): an argument-only
        // edit where a `//` line comment inside the invocation's own text
        // contains a misleading '(' character must still decline callee
        // comparison. Scanning backward, the comment's '(' is reached
        // *before* its own leading '/' characters, so checking for the
        // disqualifying character only as the scan reaches it (rather than
        // as a dedicated upfront pass over the whole text) would let this
        // paren reach depth zero and return a wrong "match" before the scan
        // ever saw the '/' that should have disqualified it. The callee
        // ("Log") is unchanged, so this must not license an unrelated
        // declaration.
        var before = TrustedDocument(
            "Log(1 // (\n);",
            new NodeSpec("InvocationExpression", "Log(1 // (\n)", [0x10]));
        var after = TrustedDocument(
            "Log(2 // (\n);\nstatic void F()\n{\n}",
            new NodeSpec("InvocationExpression", "Log(2 // (\n)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void F()\n{\n}", null));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        var declaration = Assert.Single(issued.UnmatchedAfter);
        Assert.Equal(CSharpUnmatchedNodeReason.Unsupported, declaration.Reason);
        Assert.False(CSharpBodyDiff.CompareStructure(issued).IsCorrespondenceComplete);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotInferDeclarationWhenMultipleCandidatesShareScope()
    {
        // Close negative: two new local-function declarations with no IL
        // origin, in the same document. Structural uniqueness is the only
        // thing this carve-out keys identity off of, so ambiguity between
        // multiple candidates must leave both Unsupported rather than
        // guessing which is "the" added declaration.
        var before = TrustedDocument(
            "F(); G();",
            new NodeSpec("InvocationExpression", "F()", [0x10]),
            new NodeSpec("InvocationExpression", "G()", [0x20]));
        var after = TrustedDocument(
            "F(); G();\nstatic void F()\n{\n}\nstatic void G()\n{\n}",
            new NodeSpec("InvocationExpression", "F()", [0x10]),
            new NodeSpec("InvocationExpression", "G()", [0x20]),
            new NodeSpec("LocalFunctionStatement", "static void F()\n{\n}", null),
            new NodeSpec("LocalFunctionStatement", "static void G()\n{\n}", null));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Equal(2, issued.Matches.Length);
        Assert.All(
            issued.UnmatchedAfter,
            node => Assert.Equal(CSharpUnmatchedNodeReason.Unsupported, node.Reason));
        Assert.False(CSharpBodyDiff.CompareStructure(issued).IsCorrespondenceComplete);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotInferDeclarationRetainedUnchangedOnBothSides()
    {
        // Close negative (round-1 review, both reviewers, same root cause): a
        // local-function declaration with no IL origin of its own is present
        // on both sides, unchanged. Before this fix, "only such declaration
        // on its own side" was checked independently per side, so this
        // unrelated retained declaration would wrongly qualify as both
        // Removed (from the before-side check) and Added (from the
        // after-side check) merely because some unrelated call-site elsewhere
        // in the document was rewritten. The declaration must stay
        // Unsupported on both sides: presence must be genuinely asymmetric
        // (absent from one side entirely), not merely "the only one on its
        // own side."
        var before = TrustedDocument(
            "__NoTypeParameter_g__Own_0_0(value);\nstatic int Own(int input) => input + 1;",
            new NodeSpec("InvocationExpression", "__NoTypeParameter_g__Own_0_0(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static int Own(int input) => input + 1;", null));
        var after = TrustedDocument(
            "Own(value);\nstatic int Own(int input) => input + 1;",
            new NodeSpec("InvocationExpression", "Own(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static int Own(int input) => input + 1;", null));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        Assert.Equal(
            CSharpUnmatchedNodeReason.Unsupported,
            Assert.Single(issued.UnmatchedBefore).Reason);
        Assert.Equal(
            CSharpUnmatchedNodeReason.Unsupported,
            Assert.Single(issued.UnmatchedAfter).Reason);
        Assert.False(CSharpBodyDiff.CompareStructure(issued).IsCorrespondenceComplete);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotInferDeclarationFromUnrelatedUnchangedCallSite()
    {
        // Close negative (round-1 review, both reviewers, same root cause): a
        // new local-function declaration with no IL origin appears alongside
        // a matched InvocationExpression call site elsewhere in the document
        // whose selected text is unchanged (an unrelated, pre-existing call).
        // Before this fix, "any matched InvocationExpression anywhere in the
        // document" was enough to satisfy the call-site-rewrite check, so
        // this unrelated unchanged call would wrongly license inferring the
        // new declaration. The matched call site must actually be rewritten
        // (its selected text differs before/after), not merely present.
        var before = TrustedDocument(
            "Log();",
            new NodeSpec("InvocationExpression", "Log()", [0x10]));
        var after = TrustedDocument(
            "Log();\nstatic void Unrelated()\n{\n}",
            new NodeSpec("InvocationExpression", "Log()", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void Unrelated()\n{\n}", null));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        Assert.Empty(issued.UnmatchedBefore);
        var declaration = Assert.Single(issued.UnmatchedAfter);
        Assert.Equal(CSharpUnmatchedNodeReason.Unsupported, declaration.Reason);
        Assert.False(CSharpBodyDiff.CompareStructure(issued).IsCorrespondenceComplete);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotInferDeclarationWhenRetainedCopyGainsProvenance()
    {
        // Close negative (round-1 review, reviewer A): the same declaration
        // is present on both sides, but the before copy happens to carry IL
        // provenance of its own (e.g. a single-expression body sharing a
        // sequence point with its header) while the after copy does not.
        // Before this fix, "the only null-provenance candidate on its own
        // side" counted only null-provenance nodes, so the before copy
        // (excluded by its provenance) made beforeDeclarationCandidates == 0
        // while afterDeclarationCandidates == 1 -- looking asymmetric even
        // though the declaration is genuinely retained. Presence must be
        // judged by total declarations on a side (any provenance), not just
        // null-provenance candidates, to prove genuine absence.
        var before = TrustedDocument(
            "__NoTypeParameter_g__Own_0_0(value);\nstatic int Own(int input) => input + 1;",
            new NodeSpec("InvocationExpression", "__NoTypeParameter_g__Own_0_0(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static int Own(int input) => input + 1;", [0x20]));
        var after = TrustedDocument(
            "Own(value);\nstatic int Own(int input) => input + 1;",
            new NodeSpec("InvocationExpression", "Own(value)", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static int Own(int input) => input + 1;", null));

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        var declaration = Assert.Single(issued.UnmatchedAfter);
        Assert.Equal(CSharpUnmatchedNodeReason.Unsupported, declaration.Reason);
        Assert.False(CSharpBodyDiff.CompareStructure(issued).IsCorrespondenceComplete);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotTreatDifferentlyInterleavedUnchangedCallAsRewrite()
    {
        // Close negative (round-1 review, reviewer B): a matched invocation
        // renders identically as C# on both sides -- "Log(" and "value);" on
        // two consecutive lines -- but the before document has a real
        // interleaved IL line woven between those two rendered pieces while
        // the after document has none. Per the real producer's own
        // convention (ResearchViews.cs), a C# span that continues onto a
        // later line keeps its trailing line break as part of the span, so
        // the before side's raw spans are (0,5)="Log(\n" and (18,7)="value);"
        // -- two spans purely because of the interleaved IL -- while the
        // after side, with nothing interleaved, is naturally already one
        // span covering the identical text "Log(\nvalue);". Comparing raw
        // spans makes the differing span *count* alone (2 vs. 1) look like a
        // rewrite even though the reconstructed text is character-for-
        // character identical, which would wrongly license inferring an
        // unrelated new declaration elsewhere in the document. Only once the
        // interleaved IL line is projected away does the before side's two
        // pieces coalesce back into the same single span, at which point the
        // projected text comparison correctly finds no difference. This is
        // the genuine regression case for "compare projected, not raw,
        // text" -- unlike a hand-built fixture that never lets the spans
        // coalesce, this one only passes because the projected comparison
        // itself reports equal text, not merely because of the multi-span
        // guard.
        const string beforeCall1 = "Log(";
        const string beforeIl = "IL_0000: nop";
        const string beforeCall2 = "value);";
        const string declarationText = "static void Unrelated()\n{\n}";
        string beforeText = $"{beforeCall1}\n{beforeIl}\n{beforeCall2}";
        int call1Start = beforeText.IndexOf(beforeCall1, StringComparison.Ordinal);
        int ilStart = beforeText.IndexOf(beforeIl, StringComparison.Ordinal);
        int call2Start = beforeText.IndexOf(beforeCall2, StringComparison.Ordinal);
        var before = new AnnotatedSourceDocument(
            beforeText,
            [
                new AnnotatedSourceNode(
                    0,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [
                        // Includes the trailing '\n': the construct continues
                        // onto "value);" on a later line, so the line break
                        // is the construct's own text, per the real
                        // producer's convention.
                        new AnnotatedSourceSpan(call1Start, beforeCall1.Length + 1),
                        new AnnotatedSourceSpan(call2Start, beforeCall2.Length),
                    ],
                    Provenance: new AnnotatedSourceNodeProvenance([0x10])),
                new AnnotatedSourceNode(
                    1,
                    AnnotatedSourceNode.InstructionKind,
                    SourceLineKind.Il,
                    [new AnnotatedSourceSpan(ilStart, beforeIl.Length)],
                    IlOffset: 0),
            ],
            [],
            [],
            [],
            Source());

        const string afterCall = "Log(\nvalue);";
        string afterText = $"{afterCall}\n{declarationText}";
        int afterCallStart = afterText.IndexOf(afterCall, StringComparison.Ordinal);
        int afterDeclarationStart = afterText.IndexOf(declarationText, StringComparison.Ordinal);
        var after = new AnnotatedSourceDocument(
            afterText,
            [
                // No interleaved IL splits this call on the after side, so
                // it is naturally already one contiguous span covering the
                // same two rendered lines as the before side.
                new AnnotatedSourceNode(
                    0,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(afterCallStart, afterCall.Length)],
                    Provenance: new AnnotatedSourceNodeProvenance([0x10])),
                new AnnotatedSourceNode(
                    1,
                    "LocalFunctionStatement",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(afterDeclarationStart, declarationText.Length)],
                    Provenance: null),
            ],
            [],
            [],
            [],
            Source());

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        Assert.Empty(issued.UnmatchedBefore);
        var declaration = Assert.Single(issued.UnmatchedAfter);
        Assert.Equal(CSharpUnmatchedNodeReason.Unsupported, declaration.Reason);
        Assert.False(CSharpBodyDiff.CompareStructure(issued).IsCorrespondenceComplete);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotProjectWhenNoDeclarationCandidateExists()
    {
        // Close negative (round-1 review, reviewer B): CSharpAnnotatedSourceProjection.Create
        // requires every IL-medium node to be exactly one contiguous rendered
        // line, but AnnotatedSourceNode itself permits a structural
        // (non-instruction) IL-medium node -- a "Block", say -- with several
        // spans and no IlOffset. Before this fix, ClassifyUnprovenancedDeclarations
        // built a projection of every document unconditionally, so
        // IssueCorrespondence would throw on such a document even though it
        // has no null-provenance declaration candidate that could ever be
        // promoted. The declaration-count checks must run first, and the
        // projection must stay unbuilt when they already rule out a
        // promotion, so this document -- which has no LocalFunctionStatement
        // node at all -- must not throw.
        var before = TrustedDocument(
            "A();",
            new NodeSpec("InvocationExpression", "A()", [0x10]));
        var beforeWithBlock = new AnnotatedSourceDocument(
            before.Text,
            [
                .. before.Nodes,
                new AnnotatedSourceNode(
                    before.Nodes.Count,
                    "Block",
                    SourceLineKind.Il,
                    [
                        new AnnotatedSourceSpan(0, 1),
                        new AnnotatedSourceSpan(2, 1),
                    ],
                    IlOffset: null),
            ],
            [],
            [],
            [],
            Source());
        var after = TrustedDocument(
            "A();",
            new NodeSpec("InvocationExpression", "A()", [0x10]));

        var issued = CSharpBodyDiff.IssueCorrespondence(beforeWithBlock, after);

        Assert.Single(issued.Matches);
        Assert.Empty(issued.UnmatchedBefore);
        Assert.Empty(issued.UnmatchedAfter);
    }

    [Fact]
    public void IssueCorrespondence_DoesNotThrowWhenDeclarationCandidateCoexistsWithMultiSpanIlNode()
    {
        // Close negative (round-1 review, reviewers A and B, on the previous
        // fix): a sole null-provenance LocalFunctionStatement makes
        // declarationAdded true, so the call-site rewrite check does build a
        // projection -- but a wholly unrelated structural "Block" IL node
        // elsewhere in the same document still violates
        // CSharpAnnotatedSourceProjection.Create's one-contiguous-span
        // requirement. The declaration-count gate alone cannot rule this out,
        // since it says nothing about the document's IL node shapes. The
        // projection attempt must fail conservatively -- leaving the
        // declaration Unsupported -- rather than let the document's
        // unrelated shape surface as a thrown exception from
        // IssueCorrespondence.
        const string invocationText = "A();";
        const string declarationText = "static void Own()\n{\n}";
        string afterText = $"{invocationText}\n{declarationText}";
        int afterInvocationStart = afterText.IndexOf(invocationText, StringComparison.Ordinal);
        int afterDeclarationStart = afterText.IndexOf(declarationText, StringComparison.Ordinal);
        var before = new AnnotatedSourceDocument(
            invocationText,
            [
                new AnnotatedSourceNode(
                    0,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, invocationText.Length)],
                    Provenance: new AnnotatedSourceNodeProvenance([0x10])),
                // Unrelated to the invocation or the declaration below: a
                // structural, offsetless IL node spanning two disjoint
                // pieces, which AnnotatedSourceNode permits but
                // CSharpAnnotatedSourceProjection.Create does not.
                new AnnotatedSourceNode(
                    1,
                    "Block",
                    SourceLineKind.Il,
                    [
                        new AnnotatedSourceSpan(0, 1),
                        new AnnotatedSourceSpan(2, 1),
                    ],
                    IlOffset: null),
            ],
            [],
            [],
            [],
            Source());
        var after = new AnnotatedSourceDocument(
            afterText,
            [
                new AnnotatedSourceNode(
                    0,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(afterInvocationStart, invocationText.Length)],
                    Provenance: new AnnotatedSourceNodeProvenance([0x10])),
                new AnnotatedSourceNode(
                    1,
                    "LocalFunctionStatement",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(afterDeclarationStart, declarationText.Length)],
                    Provenance: null),
            ],
            [],
            [],
            [],
            Source());

        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);

        Assert.Single(issued.Matches);
        Assert.Empty(issued.UnmatchedBefore);
        var declaration = Assert.Single(issued.UnmatchedAfter);
        Assert.Equal(CSharpUnmatchedNodeReason.Unsupported, declaration.Reason);
        // CompareStructure's own projection (pre-existing, unconditional, and
        // out of scope for this carve-out) would itself reject this
        // document's unrelated Block node; this test verifies only that
        // IssueCorrespondence -- the narrower surface this carve-out owns --
        // does not throw and classifies the declaration conservatively.
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
    public void IssuedCorrespondence_RoundTripsInferredDeclarationReason()
    {
        // Regression: the strict JSON converter for CSharpUnmatchedNodeReason
        // must know about InferredDeclaration too, or serializing it throws
        // (AnnotatedSourceContractJsonException) instead of round-tripping.
        var before = TrustedDocument(
            "__CallsEmpty_g__F_0_0();",
            new NodeSpec("InvocationExpression", "__CallsEmpty_g__F_0_0()", [0x10]));
        var after = TrustedDocument(
            "F();\nstatic void F() { }",
            new NodeSpec("InvocationExpression", "F()", [0x10]),
            new NodeSpec("LocalFunctionStatement", "static void F() { }", null));
        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);
        Assert.Equal(
            CSharpUnmatchedNodeReason.InferredDeclaration,
            Assert.Single(issued.UnmatchedAfter).Reason);

        string json = JsonSerializer.Serialize(
            issued,
            AnnotatedSourceDocumentJsonContext.Default.CSharpNodeCorrespondenceResult);
        var replayed = JsonSerializer.Deserialize(
            json,
            AnnotatedSourceDocumentJsonContext.Default.CSharpNodeCorrespondenceResult);

        Assert.NotNull(replayed);
        Assert.Equal(issued.UnmatchedAfter, replayed.UnmatchedAfter);
        Assert.Equal(
            CSharpUnmatchedNodeReason.InferredDeclaration,
            Assert.Single(replayed.UnmatchedAfter).Reason);
        Assert.Equal(2, CSharpBodyDiff.CompareStructure(replayed).Rows.Length);
    }

    [Fact]
    public void StructuralDiffDocument_ReissuesCorrespondenceAndDerivesRows()
    {
        var before = TrustedDocument(
            "return;",
            new NodeSpec("ReturnStatement", "return;", [0x10]));
        var after = TrustedDocument(
            "break;",
            new NodeSpec("BreakStatement", "break;", [0x10]));

        var document = CSharpStructuralDiffDocument.Create(before, after);

        Assert.Equal(CSharpStructuralDiffDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Equal(
            CSharpStructuralDiffDocument.CurrentMethodologyVersion,
            document.MethodologyVersion);
        Assert.Single(document.Rows);
        var row = Assert.Single(document.ToComparison().Rows);
        Assert.Equal(CSharpStructuralChangeKind.Changed, row.Change);
        Assert.Equal("ReturnStatement", row.BeforeKind);
        Assert.Equal("BreakStatement", row.AfterKind);
    }

    [Fact]
    public void StructuralDiffDocument_RejectsTamperedCorrespondence()
    {
        var before = TrustedDocument(
            "return;",
            new NodeSpec("ReturnStatement", "return;", [0x10]));
        var after = TrustedDocument(
            "break;",
            new NodeSpec("BreakStatement", "break;", [0x10]));
        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);
        var comparison = CSharpBodyDiff.CompareStructure(issued);

        Assert.Throws<ArgumentException>(() => new CSharpStructuralDiffDocument(
            CSharpStructuralDiffDocument.CurrentSchemaVersion,
            CSharpStructuralDiffDocument.CurrentMethodologyVersion,
            issued with
            {
                BeforeRevision = new CSharpDocumentRevision(new string('B', 64))
            },
            comparison.Before,
            comparison.After,
            comparison.Rows));
    }

    [Theory]
    [InlineData(0, CSharpStructuralDiffDocument.CurrentMethodologyVersion)]
    [InlineData(CSharpStructuralDiffDocument.CurrentSchemaVersion, 0)]
    public void StructuralDiffDocument_RejectsUnsupportedVersions(
        int schemaVersion,
        int methodologyVersion)
    {
        var document = TrustedDocument(
            "return;",
            new NodeSpec("ReturnStatement", "return;", [0x10]));
        var issued = CSharpBodyDiff.IssueCorrespondence(document, document);
        var comparison = CSharpBodyDiff.CompareStructure(issued);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CSharpStructuralDiffDocument(
            schemaVersion,
            methodologyVersion,
            issued,
            comparison.Before,
            comparison.After,
            comparison.Rows));
    }

    [Fact]
    public void StructuralDiffDocument_RejectsMalformedFidelityNote()
    {
        var document = TrustedDocument(
            "return;",
            new NodeSpec("ReturnStatement", "return;", [0x10]));
        var issued = CSharpBodyDiff.IssueCorrespondence(document, document);
        var comparison = CSharpBodyDiff.CompareStructure(issued);

        Assert.Throws<ArgumentException>(() => new CSharpStructuralDiffDocument(
            CSharpStructuralDiffDocument.CurrentSchemaVersion,
            CSharpStructuralDiffDocument.CurrentMethodologyVersion,
            issued,
            comparison.Before,
            comparison.After,
            comparison.Rows,
            new CSharpStructuralFidelityEvidence(
                IlBodyDiffOutcome.Exact,
                IlBodyDiffOutcome.Exact,
                "\uD800")));
    }

    [Fact]
    public void StructuralDiffDocument_RejectsTamperedRows()
    {
        var before = TrustedDocument(
            "return;",
            new NodeSpec("ReturnStatement", "return;", [0x10]));
        var after = TrustedDocument(
            "break;",
            new NodeSpec("BreakStatement", "break;", [0x10]));
        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);
        var comparison = CSharpBodyDiff.CompareStructure(issued);

        Assert.Throws<ArgumentException>(() => new CSharpStructuralDiffDocument(
            CSharpStructuralDiffDocument.CurrentSchemaVersion,
            CSharpStructuralDiffDocument.CurrentMethodologyVersion,
            issued,
            comparison.Before,
            comparison.After,
            comparison.Rows.SetItem(
                0,
                comparison.Rows[0] with { Change = CSharpStructuralChangeKind.Moved })));
    }

    [Fact]
    public void StructuralDiffDocument_RejectsTamperedProjection()
    {
        var before = TrustedDocument(
            "return;",
            new NodeSpec("ReturnStatement", "return;", [0x10]));
        var after = TrustedDocument(
            "break;",
            new NodeSpec("BreakStatement", "break;", [0x10]));
        var issued = CSharpBodyDiff.IssueCorrespondence(before, after);
        var comparison = CSharpBodyDiff.CompareStructure(issued);

        Assert.Throws<ArgumentException>(() => new CSharpStructuralDiffDocument(
            CSharpStructuralDiffDocument.CurrentSchemaVersion,
            CSharpStructuralDiffDocument.CurrentMethodologyVersion,
            issued,
            comparison.After,
            comparison.After,
            comparison.Rows));
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
    public void StructuralDiffDocument_ProjectsInterleavedIlWithoutInferringFromText()
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

        var document = CSharpStructuralDiffDocument.Create(before, after);
        var comparison = document.ToComparison();

        Assert.Equal("return;\n", document.Before.Text);
        Assert.Equal("break;\n", document.After.Text);
        Assert.Equal(document.Before, comparison.Before);
        Assert.Equal(document.After, comparison.After);
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
        string detailLine = lines[sourceLine + 2];
        Assert.Equal(caretLine.IndexOf('^'), detailLine.IndexOf(label, StringComparison.Ordinal));
    }

    static (string Text, int ChunkCount) ReconstructAnnotation(string body, string prefix)
    {
        string[] lines = body.Split('\n');
        int lineIndex = Array.FindIndex(lines, line => line.Contains(prefix, StringComparison.Ordinal));
        Assert.True(lineIndex >= 0);
        int start = lines[lineIndex].IndexOf(prefix, StringComparison.Ordinal);
        var chunks = new List<string> { lines[lineIndex][start..] };
        while (++lineIndex < lines.Length && lines[lineIndex].StartsWith("//", StringComparison.Ordinal))
            chunks.Add(lines[lineIndex][2..].TrimStart());
        return (string.Join(' ', chunks), chunks.Count);
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
                    Provenance: node.IlOffsets is null
                        ? null
                        : new AnnotatedSourceNodeProvenance(node.IlOffsets));
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
        IReadOnlyList<int>? IlOffsets,
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
