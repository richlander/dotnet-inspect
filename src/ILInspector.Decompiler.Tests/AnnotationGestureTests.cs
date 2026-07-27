using ILInspector.Decompiler.Annotations;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the two reporting gestures (#3304). An annotation is data; the gesture is
/// a reporting choice made per render, so these tests assert the *geometry* of the
/// caret render and the invariant that the default report is unchanged.
/// </summary>
[Trait("Area", "Annotations")]
public class AnnotationGestureTests
{
    static readonly AnnotationDescriptor Alloc =
        new("alloc.new", AnnotationCategory.Allocation, "allocates");
    static readonly AnnotationDescriptor Unsafety =
        new("unsafe.stackalloc", AnnotationCategory.Unsafety, "stack allocates");

    static Annotation Fact(AnnotationDescriptor descriptor, string? detail = null)
        => new(descriptor, SourceOffset: 0, Detail: detail);

    // ---- selector: the reporting half of the split -------------------------

    [Fact]
    public void DefaultSelectorLeavesEveryFactOnTheSideGesture()
    {
        var facts = new IAnnotation[] { Fact(Alloc), Fact(Unsafety) };
        Assert.True(AnnotationGestureSelector.SideOnly.AllSide(facts));
        foreach (var fact in facts)
            Assert.Equal(AnnotationGesture.Side, AnnotationGestureSelector.SideOnly.For(fact));
    }

    [Theory]
    // category name, descriptor id, and dotted id prefix all select
    [InlineData("allocation", true)]
    [InlineData("Allocation", true)]
    [InlineData("alloc.new", true)]
    [InlineData("alloc", true)]
    // a prefix must land on a segment boundary, not mid-identifier
    [InlineData("all", false)]
    [InlineData("alloc.n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void FocusPromotesOnlyMatchingFacts(string? focus, bool promoted)
    {
        var selector = AnnotationGestureSelector.Focus(focus);
        var expected = promoted ? AnnotationGesture.Caret : AnnotationGesture.Side;
        Assert.Equal(expected, selector.For(Fact(Alloc)));

        // None of these focuses name the unsafety family, so it stays on the side
        // gesture: focusing one family must not drag another along with it.
        Assert.Equal(AnnotationGesture.Side, selector.For(Fact(Unsafety)));
    }

    [Fact]
    public void FocusSelectsTheNamedFamilyAndLeavesTheOtherBehind()
    {
        var selector = AnnotationGestureSelector.Focus("unsafety");
        Assert.Equal(AnnotationGesture.Caret, selector.For(Fact(Unsafety)));
        Assert.Equal(AnnotationGesture.Side, selector.For(Fact(Alloc)));
    }

    // ---- caret geometry ----------------------------------------------------

    [Fact]
    public void CaretUnderlinesTheTrimmedStatementAndNotItsIndent()
    {
        // Indented past the member gutter, so "//" has room to sit to its left.
        string line = "        Sink(new object());";
        var rendered = AnnotationCaret.Render(line, memberIndent: "    ", [Fact(Alloc, "obj")]);

        string caretLine = rendered[0];
        int caretStart = caretLine.IndexOf('^');
        int statementStart = line.Length - line.AsSpan().TrimStart().Length;

        Assert.Equal(statementStart, caretStart);
        Assert.Equal(line.Trim().Length, caretLine.Count(c => c == '^'));
    }

    [Fact]
    public void CaretCommentSitsOnTheMemberGutterRegardlessOfNestingDepth()
    {
        // Two statements at different depths must put their "//" in one column:
        // that single gutter is the whole point of the caret gesture's layout.
        const string memberIndent = "    ";
        string shallow = "        a();";
        string deep = "                    b();";

        string shallowLine = AnnotationCaret.Render(shallow, memberIndent, [Fact(Alloc)])[0];
        string deepLine = AnnotationCaret.Render(deep, memberIndent, [Fact(Alloc)])[0];

        Assert.Equal(memberIndent.Length, shallowLine.IndexOf("//", StringComparison.Ordinal));
        Assert.Equal(memberIndent.Length, deepLine.IndexOf("//", StringComparison.Ordinal));

        // ...while each still points at its own statement.
        Assert.Equal(shallow.Length - shallow.TrimStart().Length, shallowLine.IndexOf('^'));
        Assert.Equal(deep.Length - deep.TrimStart().Length, deepLine.IndexOf('^'));
    }

    [Fact]
    public void EveryRenderedCaretLineIsAComment()
    {
        // The block is spliced into a ```csharp fence, so no line may be bare text.
        string detail = string.Join("; ", Enumerable.Repeat("key=value-that-is-long", 8));
        var rendered = AnnotationCaret.Render("        Work();", "    ", [Fact(Alloc, detail)]);

        Assert.True(rendered.Count > 1, "a long detail must wrap onto continuation lines");
        foreach (string line in rendered)
            Assert.StartsWith("//", line.TrimStart(), StringComparison.Ordinal);
    }

    [Fact]
    public void ContinuationLinesShareOneDetailColumn()
    {
        string detail = string.Join("; ", Enumerable.Repeat("key=value-that-is-long", 8));
        var rendered = AnnotationCaret.Render("        Work();", "    ", [Fact(Alloc, detail)]);

        var continuations = rendered.Skip(1).ToList();
        Assert.NotEmpty(continuations);
        int column = continuations[0].Length - continuations[0].TrimStart().Length
            + "//".Length;
        foreach (string line in continuations)
        {
            int textStart = line.IndexOf("//", StringComparison.Ordinal) + "//".Length;
            while (textStart < line.Length && line[textStart] == ' ')
                textStart++;
            Assert.Equal(FirstTextColumn(continuations[0]), textStart);
            Assert.True(column > 0);
        }

        static int FirstTextColumn(string line)
        {
            int start = line.IndexOf("//", StringComparison.Ordinal) + "//".Length;
            while (start < line.Length && line[start] == ' ')
                start++;
            return start;
        }
    }

    [Fact]
    public void EachFactStartsItsOwnLine()
    {
        var rendered = AnnotationCaret.Render(
            "        Work();",
            "    ",
            [Fact(Alloc, "one"), Fact(Unsafety, "two")]);

        Assert.Contains(rendered, line => line.Contains("alloc.new(one)", StringComparison.Ordinal));
        Assert.Contains(rendered, line => line.Contains("unsafe.stackalloc(two)", StringComparison.Ordinal));
        Assert.DoesNotContain(
            rendered,
            line => line.Contains("alloc.new(one)", StringComparison.Ordinal)
                && line.Contains("unsafe.stackalloc(two)", StringComparison.Ordinal));
    }

    [Fact]
    public void NothingIsRenderedForABlankLineOrNoFacts()
    {
        Assert.Empty(AnnotationCaret.Render("        Work();", "    ", []));
        Assert.Empty(AnnotationCaret.Render("      ", "    ", [Fact(Alloc)]));
    }

    [Fact]
    public void MemberIndentReadsTheFirstNonBlankLine()
    {
        Assert.Equal("    ", AnnotationCaret.MemberIndent(["", "   ", "    void M()", "        x();"]));
        Assert.Equal("", AnnotationCaret.MemberIndent(["void M()", "    x();"]));
        Assert.Equal("", AnnotationCaret.MemberIndent([]));
    }

    /// <summary>
    /// Without hoisting, the gutter needs three columns ("//" plus a space) to
    /// the left of the statement, and a statement sitting *at* the gutter cannot
    /// supply them — the caret shifts right rather than emitting a non-comment
    /// line. Hoisting exists precisely to buy those columns back, so the same
    /// input must point exactly. The pair is asserted together because the
    /// second half is the reason the first half is tolerable.
    /// </summary>
    [Fact]
    public void HoistingIsWhatLetsAStatementOnTheGutterBePointedAtExactly()
    {
        string line = "return Work();";

        string flat = AnnotationCaret.Render(line, memberIndent: "", [Fact(Alloc)])[0];
        Assert.StartsWith("//", flat, StringComparison.Ordinal);
        Assert.Equal("//".Length + 1, flat.IndexOf('^'));

        string hoisted = AnnotationCaret.Render(line, memberIndent: "", [Fact(Alloc)], hoist: true)[0];
        Assert.True(AnnotationCaret.TryHoist(hoisted, out string text));
        // Rendered BodyIndentWidth columns left of the code, so the caret column
        // in the hoisted line's own coordinates is the statement column plus that.
        Assert.Equal(AnnotationCaret.BodyIndentWidth, text.IndexOf('^'));
        Assert.StartsWith("//", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("return Work();")]
    [InlineData("    if (x)")]
    [InlineData("            Sink(new object());")]
    public void EveryHoistedCaretPointsExactlyAtItsStatement(string line)
    {
        // The property the shipped views rely on: at every nesting depth,
        // including the body's own base column, the carets land on the first
        // character of the statement once the body indent is removed.
        var rendered = AnnotationCaret.Render(line, memberIndent: "", [Fact(Alloc)], hoist: true);

        foreach (string emitted in rendered)
        {
            Assert.True(AnnotationCaret.TryHoist(emitted, out string text), "every hoisted line is marked");
            Assert.StartsWith("//", text, StringComparison.Ordinal);
        }

        AnnotationCaret.TryHoist(rendered[0], out string caretLine);
        int statementColumn = line.Length - line.AsSpan().TrimStart().Length;
        Assert.Equal(statementColumn + AnnotationCaret.BodyIndentWidth, caretLine.IndexOf('^'));
        Assert.Equal(line.Trim().Length, caretLine.Count(c => c == '^'));
    }

    [Fact]
    public void OnlyHoistedLinesCarryTheMarker()
    {
        foreach (string line in AnnotationCaret.Render("        Work();", "    ", [Fact(Alloc)]))
        {
            Assert.False(AnnotationCaret.TryHoist(line, out string text));
            Assert.Equal(line, text);
            Assert.DoesNotContain(AnnotationCaret.HoistMarker, line);
        }
    }

    [Fact]
    public void HoistedLinesCarryExactlyOneMarkerAndNoneInTheirText()
    {
        // A marker surviving into rendered text would print as a control
        // character inside a csharp fence.
        string detail = string.Join("; ", Enumerable.Repeat("key=value-that-is-long", 8));
        var rendered = AnnotationCaret.Render("    Work();", "", [Fact(Alloc, detail)], hoist: true);

        Assert.True(rendered.Count > 1);
        foreach (string line in rendered)
        {
            Assert.Equal(1, line.Count(c => c == AnnotationCaret.HoistMarker));
            Assert.True(AnnotationCaret.TryHoist(line, out string text));
            Assert.DoesNotContain(AnnotationCaret.HoistMarker, text);
        }
    }
}
