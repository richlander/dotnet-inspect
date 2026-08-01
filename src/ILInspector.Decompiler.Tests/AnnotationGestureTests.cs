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
    public void CaretsStackWhenTwoFactsOnTheLineDisagreeAboutTheExtent()
    {
        // Both facts narrow, but to different characters. Widening to the
        // statement was the old answer; it points at everything and therefore
        // at nothing, and leaves the details below unattributable. Each extent
        // now gets its own numbered caret. See docs/design/caret-stacking.md.
        const string Line = "        Sink(new object(), new int[1]);";
        var first = Fact(Alloc, "object");
        var second = Fact(Unsafety, "array");

        int firstColumn = Line.IndexOf("new object()", StringComparison.Ordinal);
        int secondColumn = Line.IndexOf("new int[1]", StringComparison.Ordinal);
        var extents = new Dictionary<IAnnotation, AnnotationAnchor.CaretExtent>
        {
            [first] = new AnnotationAnchor.CaretExtent(firstColumn, "new object()".Length),
            [second] = new AnnotationAnchor.CaretExtent(secondColumn, "new int[1]".Length),
        };

        // Non-vacuity: each extent narrows on its own, and to a different place.
        Assert.NotEqual(firstColumn, secondColumn);
        Assert.Equal(firstColumn, AnnotationCaret.Render(Line, "    ", [first], extents: extents)[0].IndexOf('^'));
        Assert.Equal(secondColumn, AnnotationCaret.Render(Line, "    ", [second], extents: extents)[0].IndexOf('^'));

        var rendered = AnnotationCaret.Render(Line, "    ", [first, second], extents: extents);
        string caretLine = rendered[0];

        // Two carets on one row, each still at the column of its own extent and
        // at its own true width -- not one widened underline.
        Assert.Equal(2, caretLine.Count(c => c == '.'));
        Assert.Equal(firstColumn, caretLine.IndexOf('^'));
        Assert.Equal(secondColumn, caretLine.LastIndexOf('^') - "new int[1]".Length + 1);

        // The statement underline is precisely what must no longer appear.
        int statementColumn = Line.Length - Line.TrimStart().Length;
        Assert.NotEqual(Line.Trim().Length, caretLine.Count(c => c == '^'));
        Assert.NotEqual(statementColumn, caretLine.IndexOf('^'));

        // Each caret is labelled, and each label has a matching numbered detail
        // row, so a reader can attribute every fact to a specific expression.
        Assert.Contains("1.", caretLine, StringComparison.Ordinal);
        Assert.Contains("2.", caretLine, StringComparison.Ordinal);
        Assert.Contains(rendered, l => l.Contains("1. alloc.new(object)", StringComparison.Ordinal));
        Assert.Contains(rendered, l => l.Contains("2. unsafe.stackalloc(array)", StringComparison.Ordinal));
    }

    [Fact]
    public void CaretIsDrawnAndFactsWithoutAnExtentAreMarkedWhenTheLineIsMixed()
    {
        // The PARTIAL case: one fact narrows, another has no printed node to
        // point at. This used to widen to the statement, on the reasoning that
        // narrowing would underline an expression true of only some of the
        // facts sharing the caret. Marking the extent-less fact '-' removes the
        // ambiguity that justified widening, so the surviving extent is drawn.
        // 355 lines of System.Private.CoreLib, as the annotated-source view
        // prints it, mix facts with and without extents; 254 of them have
        // exactly one surviving extent, so this is the majority shape. That is
        // summed over the five focus families: --focus promotes only one family
        // to carets, so a count over every collected fact would describe a
        // render no invocation produces. The two categories below are a unit
        // convenience -- Stack is category-agnostic and never sees the filter.
        const string Line = "        Sink(new object());";
        var narrowed = Fact(Alloc, "has-extent");
        var bare = Fact(Unsafety, "no-extent");

        int column = Line.IndexOf("new", StringComparison.Ordinal);
        var extents = new Dictionary<IAnnotation, AnnotationAnchor.CaretExtent>
        {
            [narrowed] = new AnnotationAnchor.CaretExtent(column, "new object()".Length),
        };

        // Non-vacuity: alone, this fact really does narrow, and the statement
        // underline it must not fall back to is a different geometry entirely.
        string alone = AnnotationCaret.Render(Line, "    ", [narrowed], extents: extents)[0];
        Assert.Equal(column, alone.IndexOf('^'));
        Assert.Equal("new object()".Length, alone.Count(c => c == '^'));

        var rendered = AnnotationCaret.Render(Line, "    ", [narrowed, bare], extents: extents);
        string caretLine = rendered[0];

        // The surviving extent keeps its own column and width.
        Assert.Equal(column, caretLine.IndexOf('^'));
        Assert.Equal("new object()".Length, caretLine.Count(c => c == '^'));

        int statementColumn = Line.Length - Line.TrimStart().Length;
        Assert.NotEqual(statementColumn, caretLine.IndexOf('^'));
        Assert.NotEqual(Line.Trim().Length, caretLine.Count(c => c == '^'));

        // The placed fact is numbered; the fact with nowhere to point is listed
        // under the unplaced marker rather than silently sharing the caret.
        Assert.Contains(rendered, l => l.Contains("1. alloc.new(has-extent)", StringComparison.Ordinal));
        Assert.Contains(rendered, l => l.Contains("-  unsafe.stackalloc(no-extent)", StringComparison.Ordinal));
        Assert.DoesNotContain(rendered, l => l.Contains("1. unsafe.stackalloc", StringComparison.Ordinal));
    }

    [Fact]
    public void CaretStillWidensWhenNoFactOnTheLineHasAnExtent()
    {
        // The boundary of the rule above: stacking needs at least one placeable
        // fact. With none, there is nothing to number and nothing to point at,
        // so the statement underline remains the only honest answer.
        const string Line = "        Sink(new object());";
        var first = Fact(Alloc, "one");
        var second = Fact(Unsafety, "two");

        // The dictionary is member-wide in production, so it is non-empty even
        // when no fact on *this* line has an extent. That is the shape worth
        // gating: it makes Stack return an empty group list, which is what the
        // Count: > 0 guard has to reject. An empty dictionary would reach the
        // same guard today, but only because the short-circuit that used to
        // intercept it inside Stack has since been deleted.
        var elsewhere = Fact(Alloc, "on some other line");
        var extents = new Dictionary<IAnnotation, AnnotationAnchor.CaretExtent>
        {
            [elsewhere] = new AnnotationAnchor.CaretExtent(0, 4),
        };

        string caretLine = AnnotationCaret.Render(Line, "    ", [first, second], extents: extents)[0];

        int statementColumn = Line.Length - Line.TrimStart().Length;
        Assert.Equal(statementColumn, caretLine.IndexOf('^'));
        Assert.Equal(Line.Trim().Length, caretLine.Count(c => c == '^'));
    }

    [Fact]
    public void FactsSharingAnExtentShareOneCaretAndOneNumber()
    {
        // Rule 1 of the display model: the unit is the extent, not the fact.
        // Two facts about the same characters get one caret and one number,
        // with both texts listed under it -- otherwise a line would grow a
        // redundant caret per fact and re-underline the same characters.
        const string Line = "        Sink(new object(), new int[1]);";
        var first = Fact(Alloc, "one");
        var second = Fact(Alloc, "two");
        var third = Fact(Unsafety, "three");

        int shared = Line.IndexOf("new object()", StringComparison.Ordinal);
        int other = Line.IndexOf("new int[1]", StringComparison.Ordinal);
        var extents = new Dictionary<IAnnotation, AnnotationAnchor.CaretExtent>
        {
            [first] = new AnnotationAnchor.CaretExtent(shared, "new object()".Length),
            [second] = new AnnotationAnchor.CaretExtent(shared, "new object()".Length),
            [third] = new AnnotationAnchor.CaretExtent(other, "new int[1]".Length),
        };

        var rendered = AnnotationCaret.Render(Line, "    ", [first, second, third], extents: extents);
        string caretLine = rendered[0];

        // Three facts, two extents, so two numbered carets -- not three. The
        // group count is pinned structurally by the caret runs below; this
        // pins that the numbering agrees with it.
        Assert.Contains("1.", caretLine, StringComparison.Ordinal);
        Assert.Contains("2.", caretLine, StringComparison.Ordinal);
        Assert.DoesNotContain("3.", caretLine, StringComparison.Ordinal);

        // Non-vacuity: the two carets really are at the two distinct extents.
        // The first trail collides with the second caret's label, so rule 5
        // clips its last column to the truncation glyph rather than overrun it;
        // the second trail is last on the row and keeps its true width.
        Assert.Equal(shared, caretLine.IndexOf('^'));
        Assert.Equal(1, caretLine.Count(c => c == '~'));

        var runs = new List<int>();
        for (int c = 0; c < caretLine.Length; c++)
        {
            if (caretLine[c] != '^' || (c > 0 && caretLine[c - 1] == '^'))
                continue;
            int width = caretLine.AsSpan(c).IndexOfAnyExcept('^');
            runs.Add(width < 0 ? caretLine.Length - c : width);
        }

        Assert.Equal(2, runs.Count);
        Assert.True(runs[0] < "new object()".Length, "the colliding trail must be clipped");
        Assert.Equal("new int[1]".Length, runs[1]);

        // Both facts sharing the extent are listed, and only the first carries
        // the number: the second is indented to sit under it.
        Assert.Contains(rendered, l => l.Contains("1. alloc.new(one)", StringComparison.Ordinal));
        Assert.Contains(rendered, l => l.Contains("alloc.new(two)", StringComparison.Ordinal)
            && !l.Contains("2.", StringComparison.Ordinal));
        Assert.Contains(rendered, l => l.Contains("2. unsafe.stackalloc(three)", StringComparison.Ordinal));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoRenderedCaretLineExceedsTheWidthBudget(bool hoist)
    {
        // Measured in *rendered* columns, which for a hoisted line means after
        // the marker is stripped and the body indent is skipped. Getting this
        // wrong is invisible in a unit test that only inspects the raw string:
        // the hoist adjustment leaked into the wrap arithmetic once already and
        // produced a 104-column line against a 100-column budget.
        string detail = string.Join("; ", Enumerable.Repeat("key=some-fairly-long-value", 10));
        foreach (string indent in new[] { "", "    " })
        {
            foreach (string line in new[] { indent + "Work();", indent + "        Nested(new object());" })
            {
                foreach (string emitted in AnnotationCaret.Render(line, indent, [Fact(Alloc, detail)], hoist))
                {
                    AnnotationCaret.TryHoist(emitted, out string text);
                    Assert.True(
                        text.Length <= AnnotationCaret.Budget,
                        $"{text.Length} columns exceeds the {AnnotationCaret.Budget} budget: {text}");
                }
            }
        }
    }

    [Fact]
    public void FlattenRemovesEveryMarkerForOutputPathsThatCannotHoist()
    {
        // Not every consumer of a projected body applies the body indent, and
        // one that does not must still never emit the control character.
        var rendered = AnnotationCaret.Render("    Work();", "", [Fact(Alloc, "d")], hoist: true);
        string joined = string.Join("\n", rendered);

        Assert.Contains(AnnotationCaret.HoistMarker, joined);
        Assert.DoesNotContain(AnnotationCaret.HoistMarker, AnnotationCaret.Flatten(joined));
        Assert.Equal("plain", AnnotationCaret.Flatten("plain"));
    }

    [Fact]
    public void OnlyHoistedLinesCarryTheMarker()    {
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
