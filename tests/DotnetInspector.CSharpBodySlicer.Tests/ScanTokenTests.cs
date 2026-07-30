using System.Reflection;
using ILInspector.Metadata;

namespace DotnetInspector.CSharpBodySlicer.Tests;

/// <summary>
/// Pins the token stream the slicer's scanner produces.
/// <para>
/// The scanner is not new work: the slicer has always lexed each line to place braces, and it has
/// always thrown that away at the line break, leaving every predicate to re-derive "am I inside a
/// comment, a literal, or an attribute list?" from a string it was handed. These tests fix the
/// stream that scan already computes, so the predicates can be moved onto it without the move
/// itself being the first thing that ever inspected it.
/// </para>
/// </summary>
public class ScanTokenTests
{
    /// <summary>
    /// Renders the token stream for a single line as "kind:text" pairs, so an assertion reads as
    /// the sequence a person would expect rather than as a list of offsets.
    /// </summary>
    private static string Render(params string[] lines)
    {
        var tokens = BodySlicer.ScanTokens(lines);
        return string.Join(' ', tokens.Select(t => $"{Code(t.Kind)}:{t.TextIn(lines[t.Line])}"));
    }

    /// <summary>
    /// Renders the token stream as <see cref="Render"/> does, with the three state fields each
    /// token carries appended: structural depth, bracket depth, and a trailing "?" when the
    /// scanner has lost its place and the depth is meaningless.
    /// <para>
    /// <see cref="Render"/> shows only kind and text, which is why every rule that governs these
    /// three fields alone went ungated: a mutation could corrupt the depth of every token on a
    /// line and no assertion in this file could see it (adversarial review, GPT).
    /// </para>
    /// </summary>
    private static string RenderState(params string[] lines)
    {
        var tokens = BodySlicer.ScanTokens(lines);
        return string.Join(' ', tokens.Select(t =>
            $"{Code(t.Kind)}:{t.TextIn(lines[t.Line])}:d{t.Depth}:b{t.BracketDepth}{(t.DepthKnown ? "" : "?")}"));
    }

    private static char Code(ScanTokenKind kind) => kind switch
    {
        ScanTokenKind.Word => 'W',
        ScanTokenKind.Punctuator => 'P',
        ScanTokenKind.StringLiteral => 'S',
        ScanTokenKind.CharLiteral => 'H',
        ScanTokenKind.Comment => 'C',
        ScanTokenKind.Directive => 'D',
        _ => throw new InvalidOperationException($"Unhandled kind {kind}."),
    };

    [Fact]
    public void Declaration_SeparatesWordsFromPunctuation()
    {
        Assert.Equal(
            "W:public W:int W:Add P:( W:int W:a P:, W:int W:b P:)",
            Render("    public int Add(int a, int b)"));
    }

    [Fact]
    public void LineComment_RunsToEndOfLine_AndItsBracesAreNotCode()
    {
        Assert.Equal(
            "W:int W:x P:; C:// trailing { comment",
            Render("int x; // trailing { comment"));
    }

    [Fact]
    public void BlockComment_YieldsOneTokenPerLineItCovers()
    {
        // The brace on the middle line is comment text. A predicate handed that line in isolation
        // would read it as structure; a predicate handed these tokens cannot.
        Assert.Equal(
            "C:/* start C: middle { C: end */ W:int W:x P:;",
            Render("/* start", " middle {", " end */ int x;"));
    }

    [Fact]
    public void BlockComment_OpeningAndClosingOnOneLine_IsASingleToken()
    {
        Assert.Equal("W:int C:/**/ W:x P:;", Render("int /**/ x;"));
    }

    [Fact]
    public void VerbatimLiteral_KeepsItsBracesAsLiteralText()
    {
        Assert.Equal(
            "W:var W:s P:= S:@\"a{b\" P:; W:var W:t P:= W:1 P:;",
            Render("var s = @\"a{b\";", "var t = 1;"));
    }

    [Fact]
    public void RawLiteral_SpansLines_AndItsBracesAreLiteralText()
    {
        Assert.Equal(
            "W:var W:s P:= S:\"\"\" S:  raw { text } S:  \"\"\" P:;",
            Render("var s = \"\"\"", "  raw { text }", "  \"\"\";"));
    }

    /// <summary>
    /// Pins the depth and bracket depth a carried raw-literal fragment reports, absolutely.
    /// <para>
    /// <see cref="RawLiteral_SpansLines_AndItsBracesAreLiteralText"/> renders kind and text only,
    /// and the differential sweep compares the bare scan against the enclosed one, so a value
    /// that is consistently wrong on both sides satisfies both. Measured, emitting
    /// <c>depth + 1</c> — or <c>BracketDepth + 1</c> — for these fragments left the whole suite
    /// green, at ten emission sites (adversarial review, GPT).
    /// </para>
    /// <para>
    /// The carried line opens with <c>}</c> and contains <c>{</c> so that reading it as code
    /// rather than as literal text would move the depth, and the literal is carried inside both
    /// a block and a collection expression so that neither field is zero where a defect could
    /// hide in it.
    /// </para>
    /// </summary>
    [Fact]
    public void CarriedRawLiteralFragments_ReportTheDepthAndBracketDepthEnclosingThem()
    {
        var lines = new[] { "{", "    x = [", "        \"\"\"", "} raw { text", "        \"\"\"", "    ];", "}" };

        // The text is asserted with the depths so the assertion reads the very characters the
        // fixture relies on: replacing the carried line's leading `}` with plain text stops it
        // reaching the brace emission site, and pinning position alone left that silent
        // (adversarial review, GPT).
        Assert.Equal(
            [
                (0, 0, 0, "P:{"), (1, 1, 0, "W:x"), (1, 1, 0, "P:="), (1, 1, 0, "P:["),
                (2, 1, 1, "S:\"\"\""), (3, 1, 1, "S:} raw { text"), (4, 1, 1, "S:        \"\"\""),
                (5, 1, 1, "P:]"), (5, 1, 0, "P:;"), (6, 1, 0, "P:}"),
            ],
            Placed(lines));
    }

    /// <summary>
    /// The same absolute pin for the fragment an interpolated literal emits up to its hole
    /// opener, which is a different emission site and was the one site
    /// <see cref="CarriedRawLiteralFragments_ReportTheDepthAndBracketDepthEnclosingThem"/> does
    /// not reach: emitting <c>depth + 1</c> there left the whole suite green.
    /// </summary>
    [Fact]
    public void InterpolatedLiteralFragments_ReportTheDepthAndBracketDepthEnclosingThem()
    {
        var lines = new[] { "{", "    x = [", "        $\"a{b}c\"", "    ];", "}" };

        Assert.Equal(
            [
                (0, 0, 0, "P:{"), (1, 1, 0, "W:x"), (1, 1, 0, "P:="), (1, 1, 0, "P:["),
                (2, 1, 1, "S:$\"a{"), (2, 1, 1, "W:b"), (2, 1, 1, "S:}c\""),
                (3, 1, 1, "P:]"), (3, 1, 0, "P:;"), (4, 1, 0, "P:}"),
            ],
            Placed(lines));
    }

    [Fact]
    public void InterpolatedLiteral_ScansItsHoleAsCode()
    {
        // The hole's contents are ordinary C# and come back as code tokens; the delimiters stay
        // with the literal.
        Assert.Equal(
            "W:var W:s P:= S:$\"a{ W:b P:+ W:1 S:}c\" P:;",
            Render("var s = $\"a{b + 1}c\";"));
    }

    [Fact]
    public void DoubleDollarRawLiteral_TakesTwoBracesToOpenAHole()
    {
        Assert.Equal(
            "W:var W:s P:= S:$$\"\"\"x{{ W:y S:}}z\"\"\" P:;",
            Render("var s = $$\"\"\"x{{y}}z\"\"\";"));
    }

    [Fact]
    public void SingleBraceInDoubleDollarLiteral_IsContent()
    {
        Assert.Equal(
            "W:var:d0:b0 W:s:d0:b0 P:=:d0:b0 S:$$\"\"\"x{y}z\"\"\":d0:b0 P:;:d0:b0",
            RenderState("var s = $$\"\"\"x{y}z\"\"\";"));
    }

    /// <summary>
    /// The empty literal is one token, and it reports the depth and bracket depth around it.
    /// Rendering text alone left its own emission site free to report either field wrongly with
    /// the whole suite green, because the differential sweep reads both only relatively
    /// (adversarial review, GPT); it is the one site the other absolute fixtures do not reach,
    /// since every literal they carry has content.
    /// </summary>
    [Fact]
    public void EmptyLiteral_IsOneToken()
    {
        Assert.Equal("W:var:d0:b0 W:e:d0:b0 P:=:d0:b0 S:\"\":d0:b0 P:;:d0:b0", RenderState("var e = \"\";"));

        var lines = new[] { "{", "    x = [", "        \"\"", "    ];", "}" };

        Assert.Equal(
            [(0, 0, 0, "P:{"), (1, 1, 0, "W:x"), (1, 1, 0, "P:="), (1, 1, 0, "P:["), (2, 1, 1, "S:\"\""), (3, 1, 1, "P:]"), (3, 1, 0, "P:;"), (4, 1, 0, "P:}"),],
            Placed(lines));
    }

    [Fact]
    public void NestedInterpolation_KeepsTheInnerHoleAsCode()
    {
        // Two literals, and the inner one opens immediately inside the outer one's hole, so the
        // two openers coalesce into a single token. That is the documented meaning of the kind:
        // it marks text that is not code, not the bounds of one literal. What matters is that the
        // hole contents still arrive as code, and they do.
        Assert.Equal(
            "W:var W:s P:= S:$\"outer {$\"inner { W:x S:}\"} end\" P:;",
            Render("var s = $\"outer {$\"inner {x}\"} end\";"));
    }

    [Fact]
    public void ConditionalInsideAHole_YieldsItsOwnLiteralsAsSeparateTokens()
    {
        // Here the hole does contain code between the literals, so nothing coalesces across it.
        Assert.Equal(
            "W:var W:s P:= S:$\"{ P:( W:cond P:? S:\"a\" P:: S:\"b\" P:) S:}\" P:;",
            Render("var s = $\"{(cond ? \"a\" : \"b\")}\";"));
    }

    [Fact]
    public void TripleBraceRunInDoubleDollarLiteral_OpensAHoleWithTheLastTwo()
    {
        Assert.Equal(
            "W:var W:s P:= S:$$\"\"\"a{{{ W:b S:}}}c\"\"\" P:;",
            Render("var s = $$\"\"\"a{{{b}}}c\"\"\";"));
    }

    /// <summary>
    /// Both spellings of a verbatim interpolated literal open a hole.
    /// </summary>
    /// <remarks>
    /// The input and the expected rendering are spelled out per row rather than derived from a
    /// shared parameter. A theory that builds both sides from one value validates whatever it
    /// is given: substituting the row silently moves the case to a different state and the
    /// expectation follows it, leaving the suite green at the same test count while the state
    /// the row existed for goes unscanned (adversarial review, GPT).
    /// </remarks>
    [Theory]
    [InlineData("var s = $@\"a{b}c\";", "W:var W:s P:= S:$@\"a{ W:b S:}c\" P:;")]
    [InlineData("var s = @$\"a{b}c\";", "W:var W:s P:= S:@$\"a{ W:b S:}c\" P:;")]
    public void VerbatimAndInterpolatedInEitherOrder_StillInterpolates(string line, string expected)
    {
        Assert.Equal(expected, Render(line));
    }

    /// <summary>
    /// An even brace run in a single-dollar literal is escaped text, not a hole. Nothing
    /// pinned the kind that path emits: mutating it from a string fragment to a word left the
    /// whole suite green (adversarial review, Gemini). The differential invariant cannot see
    /// it, because it compares a bare scan against a wrapped one and both sides change
    /// together; only a rendering assertion can.
    /// </summary>
    /// <remarks>
    /// Each row spells out its own input and expectation, for the reason given on
    /// <see cref="VerbatimAndInterpolatedInEitherOrder_StillInterpolates"/>. The first row is
    /// the only non-verbatim case, and when both sides were derived from one prefix it could be
    /// exchanged for a second verbatim spelling with the expectation following it.
    /// </remarks>
    [Theory]
    [InlineData("var s = $\"a{{b}}c\";", "W:var:d0:b0 W:s:d0:b0 P:=:d0:b0 S:$\"a{{b}}c\":d0:b0 P:;:d0:b0")]
    [InlineData("var s = $@\"a{{b}}c\";", "W:var:d0:b0 W:s:d0:b0 P:=:d0:b0 S:$@\"a{{b}}c\":d0:b0 P:;:d0:b0")]
    [InlineData("var s = @$\"a{{b}}c\";", "W:var:d0:b0 W:s:d0:b0 P:=:d0:b0 S:@$\"a{{b}}c\":d0:b0 P:;:d0:b0")]
    public void EscapedBraceRunInAnInterpolatedLiteral_StaysStringContent(string line, string expected)
    {
        Assert.Equal(expected, RenderState(line));
    }

    [Fact]
    public void QuoteRunShorterThanTheDelimiter_IsRawLiteralContent()
    {
        Assert.Equal(
            "W:var:d0:b0 W:s:d0:b0 P:=:d0:b0 S:\"\"\"\"a \"\"\" b\"\"\"\":d0:b0 P:;:d0:b0",
            RenderState("var s = \"\"\"\"a \"\"\" b\"\"\"\";"));
    }

    [Fact]
    public void VerbatimLiteral_EndingALineOnAnEscapedQuote_StaysOpen()
    {
        Assert.Equal(
            "W:var:d0:b0 W:s:d0:b0 P:=:d0:b0 S:@\"ends with quote\"\":d0:b0 S:still literal\":d0:b0 P:;:d0:b0",
            RenderState("var s = @\"ends with quote\"\"", "still literal\";"));
    }

    [Theory]
    [InlineData("var s = \"http://not/a/comment\";", "W:var W:s P:= S:\"http://not/a/comment\" P:;")]
    [InlineData("var s = \"/* not a comment */\";", "W:var W:s P:= S:\"/* not a comment */\" P:;")]
    public void CommentOpenerInsideALiteral_IsLiteralText(string line, string expected)
    {
        Assert.Equal(expected, Render(line));
    }

    [Theory]
    [InlineData("// a \" quote in a comment", "C:// a \" quote in a comment")]
    [InlineData("/* a \" quote */ int x;", "C:/* a \" quote */ W:int W:x P:;")]
    public void QuoteInsideAComment_DoesNotOpenALiteral(string line, string expected)
    {
        Assert.Equal(expected, Render(line));
    }

    [Fact]
    public void IndexerBrackets_RaiseBracketDepthJustAsAnAttributeListDoes()
    {
        // Bracket depth says "inside square brackets", not "inside an attribute list". A predicate
        // moving onto these tokens must not read a non-zero bracket depth as an attribute.
        var lines = new[] { "var x = a[i] + b[j];" };

        // The whole stream is asserted, not three looked-up tokens: picking tokens out by text
        // leaves the rest of the input free, so it can be replaced by one that still contains
        // them and no longer exercises the shape the test is named for (adversarial review,
        // Gemini).
        Assert.Equal(
            [
                (0, 0, 0, "W:var"), (0, 0, 0, "W:x"), (0, 0, 0, "P:="), (0, 0, 0, "W:a"),
                (0, 0, 0, "P:["), (0, 0, 1, "W:i"), (0, 0, 1, "P:]"), (0, 0, 0, "P:+"),
                (0, 0, 0, "W:b"), (0, 0, 0, "P:["), (0, 0, 1, "W:j"), (0, 0, 1, "P:]"),
                (0, 0, 0, "P:;"),
            ],
            Placed(lines));
    }

    [Fact]
    public void CharLiteral_SurvivesAnEscapedQuote()
    {
        Assert.Equal("W:char W:c P:= H:'\\'' P:;", Render("char c = '\\'';"));
    }

    [Fact]
    public void VerbatimIdentifier_IsNotALiteral()
    {
        Assert.Equal("W:var W:v P:= P:@ W:class P:;", Render("var v = @class;"));
    }

    [Fact]
    public void BraceDepth_PlacesEachDelimiterWithTheTextItBounds()
    {
        var lines = new[] { "class C {", "  void M() { }", "}" };
        var tokens = BodySlicer.ScanTokens(lines);

        // An opener carries the depth outside it and a closer the depth inside, so both report
        // the block they delimit rather than each other.
        Assert.Equal(0, Single(tokens, lines, "{", line: 0).Depth);
        Assert.Equal(1, Single(tokens, lines, "void", line: 1).Depth);
        Assert.Equal(1, Single(tokens, lines, "{", line: 1).Depth);
        Assert.Equal(2, Single(tokens, lines, "}", line: 1).Depth);
        Assert.Equal(1, Single(tokens, lines, "}", line: 2).Depth);
    }

    [Fact]
    public void AttributeList_RaisesBracketDepthForItsContents()
    {
        var lines = new[] { "[Obsolete(\"x\")]", "public void M() { }" };
        var tokens = BodySlicer.ScanTokens(lines);

        Assert.Equal(0, Single(tokens, lines, "[", line: 0).BracketDepth);
        Assert.Equal(1, Single(tokens, lines, "Obsolete", line: 0).BracketDepth);
        Assert.Equal(1, Single(tokens, lines, "]", line: 0).BracketDepth);
        Assert.Equal(0, Single(tokens, lines, "public", line: 1).BracketDepth);
    }

    [Fact]
    public void AttributeListSpanningLines_KeepsItsContentsBracketed()
    {
        // An attribute argument that reads like a declaration — "set" here — is what truncated an
        // accessor before brackets were carried across lines.
        var lines = new[] { "[Obsolete(", "    set)]", "public int P { get; set; }" };

        Assert.Equal(
            [
                (0, 0, 0, "P:["), (0, 0, 1, "W:Obsolete"), (0, 0, 1, "P:("),
                (1, 0, 1, "W:set"), (1, 0, 1, "P:)"), (1, 0, 1, "P:]"),
                (2, 0, 0, "W:public"), (2, 0, 0, "W:int"), (2, 0, 0, "W:P"), (2, 0, 0, "P:{"),
                (2, 1, 0, "W:get"), (2, 1, 0, "P:;"), (2, 1, 0, "W:set"), (2, 1, 0, "P:;"),
                (2, 1, 0, "P:}"),
            ],
            Placed(lines));
    }

    [Fact]
    public void ConditionalDirective_MarksTheDepthUnknowable()
    {
        var lines = new[] { "#if DEBUG", "int x;", "#endif" };
        var tokens = BodySlicer.ScanTokens(lines);

        // The braces around a conditional branch may belong to text the compiler discards, so the
        // depth stops meaning anything — and the tokens say so rather than reporting a plausible
        // number.
        Assert.All(tokens, t => Assert.False(t.DepthKnown));
        Assert.Equal(ScanTokenKind.Directive, tokens[0].Kind);
    }

    [Fact]
    public void NonConditionalDirective_LeavesTheDepthKnown()
    {
        var lines = new[] { "#region Things", "int x;" };
        var tokens = BodySlicer.ScanTokens(lines);

        Assert.All(tokens, t => Assert.True(t.DepthKnown));
        Assert.Equal(ScanTokenKind.Directive, tokens[0].Kind);
    }

    [Fact]
    public void UnterminatedSingleLineLiteral_MarksEveryTokenOnThatLineUnknown()
    {
        var lines = new[] { "var s = \"unterminated;", "int y;" };
        var tokens = BodySlicer.ScanTokens(lines);

        // The scan loses its place at the end of the line, which is after the earlier tokens on
        // it were emitted. They are corrected rather than left reporting a depth that has since
        // become meaningless.
        Assert.All(tokens, t => Assert.False(t.DepthKnown));
        Assert.Equal("var", tokens[0].TextIn(lines[0]).ToString());
    }

    private static ScanToken Single(IReadOnlyList<ScanToken> tokens, string[] lines, string text, int line)
    {
        var matches = tokens.Where(t => t.Line == line && t.TextIn(lines[line]).SequenceEqual(text)).ToList();
        return Assert.Single(matches);
    }

    // ---- Coverage invariant -------------------------------------------------------------

    /// <summary>
    /// Describes the first place the token stream fails to account for <paramref name="lines"/>,
    /// or null when it accounts for all of them.
    /// <para>
    /// Three things are checked together because they fail together: tokens must stay in bounds,
    /// must not overlap or run backwards, and must cover every character that is not whitespace.
    /// A gap means the scan advanced over text without deciding what it was, which is exactly the
    /// state a predicate reading raw text used to be in.
    /// </para>
    /// </summary>
    /// <summary>
    /// Projects the whole token stream as line, both depths, and "kind:text", for the fixtures
    /// whose claim is about where a token sits rather than about the stream's shape.
    /// <para>
    /// These assert every token rather than filtering to the kind under test. Filtering discards
    /// exactly the evidence that the fixture reached the branch it was written for: a supplied
    /// input can be steered off that branch while every surviving row keeps its position and its
    /// text, leaving the suite green with a real defect live (adversarial review, GPT).
    /// </para>
    /// </summary>
    private static (int Line, int Depth, int BracketDepth, string Text)[] Placed(params string[] lines) =>
        [.. BodySlicer.ScanTokens(lines)
            .Select(t => (t.Line, t.Depth, t.BracketDepth, $"{Code(t.Kind)}:{t.TextIn(lines[t.Line])}"))];

    private static string? FindCoverageGap(IReadOnlyList<string> lines, IReadOnlyList<ScanToken> tokens)
    {
        int index = 0;

        for (int line = 0; line < lines.Count; line++)
        {
            string text = lines[line];
            int covered = 0;

            while (index < tokens.Count && tokens[index].Line == line)
            {
                var token = tokens[index];

                if (token.Length <= 0 || token.Column < 0 || token.End > text.Length)
                    return $"line {line}: token {token.Kind} at {token.Column}+{token.Length} is out of bounds for a {text.Length}-character line";

                if (token.Column < covered)
                    return $"line {line}: token {token.Kind} at {token.Column} overlaps or precedes the previous token, which ended at {covered}";

                for (int c = covered; c < token.Column; c++)
                {
                    if (!char.IsWhiteSpace(text[c]))
                        return $"line {line}: '{text[c]}' at column {c} is not covered by any token";
                }

                covered = token.End;
                index++;
            }

            for (int c = covered; c < text.Length; c++)
            {
                if (!char.IsWhiteSpace(text[c]))
                    return $"line {line}: '{text[c]}' at column {c} is past the last token and not covered";
            }
        }

        return index == tokens.Count ? null : $"{tokens.Count - index} tokens refer to lines that do not exist";
    }

    /// <summary>
    /// The gate for <see cref="FindCoverageGap"/> itself. Without this, a checker that returned
    /// null unconditionally would leave every coverage test below passing and say nothing, and the
    /// only way to tell that apart from a real result would be to break the scanner on purpose.
    /// </summary>
    [Fact]
    public void CoverageCheck_ReportsATokenThatWasRemoved()
    {
        var lines = new[] { "int x = 1;" };
        var tokens = BodySlicer.ScanTokens(lines);

        Assert.Null(FindCoverageGap(lines, tokens));

        for (int drop = 0; drop < tokens.Count; drop++)
        {
            var damaged = tokens.Where((_, i) => i != drop).ToList();
            Assert.NotNull(FindCoverageGap(lines, damaged));
        }
    }

    [Fact]
    public void CoverageCheck_ReportsATokenThatWasShortened()
    {
        var lines = new[] { "int identifier = 1;" };
        var tokens = BodySlicer.ScanTokens(lines);
        var damaged = tokens.ToList();

        int word = damaged.FindIndex(t => t.Kind == ScanTokenKind.Word && t.Length > 1);
        damaged[word] = damaged[word] with { Length = damaged[word].Length - 1 };

        Assert.NotNull(FindCoverageGap(lines, damaged));
    }

    /// <summary>
    /// Gates the rule that raw and verbatim literals have no escape sequences — a backslash in
    /// one is ordinary content, and in particular cannot consume the quote that closes it.
    ///
    /// The backslash has to sit at the start of a scan step for the escape branch to be consulted
    /// at all: the plain-content run stops only at <c>{</c>, <c>}</c>, <c>"</c>, and (outside a
    /// raw or verbatim literal) a backslash, so a backslash following ordinary text is swallowed
    /// by the run before the branch is reached. Putting it directly after a brace is what makes
    /// the rule observable — <c>@"a\"</c> is not affected by the mutation, only <c>@"{\"</c> is.
    /// Without that placement, dropping either <c>!frame.Raw</c> or <c>!frame.Verbatim</c> changes
    /// nothing, which is why the corpus and every other test miss both; with it, the literal
    /// swallows its own closing delimiter and the entire rest of the line.
    ///
    /// The two guards live in one condition but are independent: each mode needs its own case.
    /// </summary>
    [Theory]
    // Raw.
    [InlineData(""""var s = """{\"""; int after = 1;"""", """"W:var W:s P:= S:"""{\""" P:; W:int W:after P:= W:1 P:;"""")]
    // Raw interpolated, where the brace ending the run is a hole's closer.
    [InlineData(""""var s = $"""{b}\""";"""", """"W:var W:s P:= S:$"""{ W:b S:}\""" P:;"""")]
    // Verbatim.
    [InlineData("""var s = @"{\"; int after = 1;""", """W:var W:s P:= S:@"{\" P:; W:int W:after P:= W:1 P:;""")]
    public void LiteralsWithoutEscapes_TreatABackslashAsContent(string line, string expected)
    {
        Assert.Equal(expected, Render(line));
    }

    /// <summary>
    /// Gates both conjuncts of the guard that decides whether a line is a preprocessor directive.
    /// A line beginning with <c>#</c> is only a directive when the scan is not already inside a
    /// literal or a block comment; either conjunct can be dropped on its own, so each needs a
    /// case. Without these, a <c>#define</c> carried inside a raw literal or a block comment is
    /// emitted as a <see cref="ScanTokenKind.Directive"/> and the line's real content is lost.
    /// </summary>
    [Fact]
    public void HashLine_CarriedInsideALiteralOrAComment_IsNotADirective()
    {
        Assert.Equal(
            """"W:var W:s P:= S:""" S:#define FOO S:""" P:;"""",
            Render("var s = \"\"\"", "#define FOO", "\"\"\";"));

        Assert.Equal(
            "C:/* C:#define FOO C:*/",
            Render("/*", "#define FOO", "*/"));
    }

    /// <summary>
    /// Gates the bounds half of the guard that looks for a comment opener. A <c>/</c> is only the
    /// start of <c>//</c> or <c>/*</c> when another character follows it, and a line may legally
    /// end with one — a division split across lines does exactly that. Dropping the length check
    /// reads one past the end of the line, so this is the test standing between that guard and an
    /// <see cref="IndexOutOfRangeException"/>.
    /// </summary>
    [Fact]
    public void SlashAtEndOfLine_IsAPunctuator_NotACommentOpener()
    {
        Assert.Equal(
            "W:int W:x P:= W:a P:/ W:b P:;",
            Render("int x = a /", "    b;"));
    }

    /// <summary>
    /// Gates the underscore half of the character test that starts an identifier run. An
    /// identifier may begin with <c>_</c>, and dropping that conjunct splits <c>_value</c> into a
    /// punctuator and a word — two tokens whose spans still cover the line completely, which is
    /// why the coverage gate cannot see it.
    /// </summary>
    [Fact]
    public void IdentifierStartingWithAnUnderscore_IsASingleWord()
    {
        Assert.Equal("W:_value P:= W:1 P:;", Render("_value = 1;"));
        Assert.Equal("W:var W:_ P:= W:M P:( P:) P:;", Render("var _ = M();"));
    }

    /// <summary>
    /// Gates the column half of the adjacency rule. Two literal fragments can sit on one line
    /// with a gap between them when a hole's code is separated from its braces by whitespace:
    /// <c>$"{  "x"}"</c> emits <c>$"{</c>, then two skipped spaces, then <c>"x"}"</c>. Fusing
    /// those would produce a token whose length spans the gap but stops short of where the
    /// second fragment actually ends, leaving that fragment's last characters covered by nothing.
    ///
    /// This is the shape the sibling cross-line test cannot reach, because there the two
    /// fragments are on different lines. Both guards need their own gate.
    /// </summary>
    [Fact]
    public void LiteralFragments_DoNotCoalesceAcrossAGapOnTheSameLine()
    {
        var lines = new[] { "var s = $\"{  \"x\"}\";" };

        // Asserted over the whole stream for the same reason as the cross-line sibling: the two
        // literal columns alone are satisfied by an input with no interpolation hole in it at all
        // (adversarial review, Gemini).
        Assert.Equal(
            [
                (0, 0, 3, "W:var"), (0, 4, 1, "W:s"), (0, 6, 1, "P:="),
                (0, 8, 3, "S:$\"{"), (0, 13, 5, "S:\"x\"}\""), (0, 18, 1, "P:;"),
            ],
            BodySlicer.ScanTokens(lines)
                .Select(t => (t.Line, t.Column, t.Length, $"{Code(t.Kind)}:{t.TextIn(lines[t.Line])}")));
    }

    /// <summary>
    /// Runs the coverage invariant over every string up to six characters long drawn from the
    /// alphabet that actually drives the literal state machine — <c>$ @ " { } \</c>, a space, and
    /// one ordinary character. The authored corpus is large but it is well-formed C#; it contains
    /// almost none of the delimiter soup that distinguishes the raw, verbatim, and interpolated
    /// rules from one another. This covers that space exhaustively rather than by example.
    ///
    /// The space is load-bearing, not filler: it is what lets two tokens on one line be separated
    /// by a gap, which is the only situation in which the column half of the coalescing rule
    /// decides anything.
    ///
    /// The claim is the coverage invariant, not a token shape: whatever the scan decides these
    /// strings mean, every non-whitespace character is inside exactly one token and no token runs
    /// off its line. That is weaker than pinning output, but it holds for inputs no one wrote down,
    /// and it is an oracle rather than a re-implementation.
    ///
    /// What it deliberately does not reach, so that no one mistakes it for a general gate:
    ///
    /// It is single-line, so the line half of the coalescing rule and all carried state are out
    /// of scope.
    ///
    /// Six characters cannot open a <c>$$</c> raw interpolation hole, which needs seven, and
    /// cannot close a raw literal at all: an opener and a closer would have to be adjacent, and
    /// adjacent quotes merge into one run, so <c>""""""</c> is a six-quote opener rather than two
    /// three-quote delimiters. Raw closure therefore also needs seven, and the raw backslash shape
    /// needs eight.
    ///
    /// The oracle is blind to token *kind* while the spans stay complete, which is a separate
    /// limit from length and the more important one. The verbatim backslash shape is reachable
    /// here at six characters — <c>@"{\"a</c> — and the gate still does not catch the mutation
    /// that drops <c>!frame.Verbatim</c>, because that only turns the trailing <c>a</c> from a
    /// word into literal content and every character remains covered exactly once.
    ///
    /// Each of those is gated by a named test above instead.
    /// </summary>
    [Fact]
    public void EveryStringOverTheDelimiterAlphabet_IsFullyCovered()
    {
        // The alphabet is pinned by value, and the sweep scans exactly the set generated from
        // it. Every count above survives exchanging `\` for an ordinary letter, which keeps all
        // 299,592 cases and drops every terminal-escape path; and a set recorded alongside the
        // scan is not the set scanned, because the recording can be pointed at the value the
        // input was generated from while the scanner is handed another (adversarial review,
        // GPT, twice). There is no recording here: `swept` is both the pinned collection and
        // the collection iterated, so the two cannot be separated without rewriting the loop.
        const string Alphabet = " \"$@\\a{}";
        Assert.Equal(" \"$@\\a{}", Alphabet);

        var swept = AllStringsOver(Alphabet, 6);
        Assert.Equal(299_592, swept.Count);

        foreach (var input in swept)
        {
            // No local holds the scanned line: an in-loop substitution would have to be spelled
            // at the scan call itself, which is the visible, out-of-scope class rather than a
            // silent weakening of a value the test supplies (adversarial review, GPT).
            string? gap = FindCoverageGap([input], BodySlicer.ScanTokens([input]));

            if (gap is not null)
                Assert.Fail($"input \"{input}\": {gap}");
        }
    }

    /// <summary>
    /// Every string of length 1..<paramref name="upTo"/> over <paramref name="alphabet"/>, built
    /// from the pinned literal its caller passes rather than read back from the generator that
    /// produced the sweep. A sweep that pins only its size, its character set and its result
    /// totals does not pin the strings it scanned: one input can be exchanged for another that
    /// is the same length and draws on the same characters, leaving every count identical while
    /// the state that input existed for is never reached (adversarial review, GPT).
    /// </summary>
    private static HashSet<string> AllStringsOver(string alphabet, int upTo)
    {
        var singles = alphabet.Select(c => c.ToString()).ToArray();
        var byLength = new List<string[]> { singles };

        for (int n = 1; n < upTo; n++)
            byLength.Add([.. byLength[n - 1].SelectMany(_ => singles, (prefix, next) => prefix + next)]);

        return byLength.SelectMany(x => x).ToHashSet();
    }

    /// <summary>
    /// Every fragment of a literal carried in from an earlier line reports the depth and bracket
    /// depth in effect where that literal opened.
    /// <para>
    /// This is the absolute counterpart the fragment-emitting branches had been missing. Those
    /// branches pass a depth to <c>Emit</c>, but on the line that opens the literal their token
    /// coalesces into the fragment before it, and coalescing keeps the earlier token's depth and
    /// discards the argument entirely — so <c>Emit(depth + 1, ...)</c> at eight of them changed
    /// no output at all. Coalescing requires <c>previous.Line == lineIndex</c>, so the first
    /// token of a carried line is the one shape in which the argument survives to be read
    /// (adversarial review, Gemini).
    /// </para>
    /// <para>
    /// The tail is swept rather than hand-picked because which branch consumes it is exactly what
    /// a defect would change: a fixed tail pins one branch and lets a wrong depth at any other
    /// through. Only the line's first token is asserted, since a tail may close its literal and
    /// continue as code at a legitimately different depth.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryFragmentOfACarriedLiteral_ReportsTheDepthWhereItsLiteralOpened()
    {
        const string Alphabet = "\"$\\{}a";
        Assert.Equal("\"$\\{}a", Alphabet);

        // The two unterminated openers are here because a plain literal carries too: the scanner
        // loses its place rather than closing the frame, so the next line still arrives as literal
        // content. They are the only shape that reaches the backslash-escape branch as a line's
        // first token, that branch requiring a frame that is neither verbatim nor raw.
        string[] openers = ["@\"", "$@\"", "\"\"\"", "$\"\"\"", "$$\"\"\"", "\"", "$\""];
        Assert.Equal(["@\"", "$@\"", "\"\"\"", "$\"\"\"", "$$\"\"\"", "\"", "$\""], openers);

        var tails = AllStringsOver(Alphabet, 3);
        Assert.Equal(6 + 36 + 216, tails.Count);

        foreach (string opener in openers)
        {
            foreach (string tail in tails)
            {
                var lines = new[] { "{", "    x = [", "        " + opener, tail };
                var first = BodySlicer.ScanTokens(lines).First(t => t.Line == 3);

                Assert.Equal(
                    (ScanTokenKind.StringLiteral, 1, 1),
                    (first.Kind, first.Depth, first.BracketDepth));
            }
        }
    }

    /// <summary>
    /// Pins how often the scanner reports the depth as unknowable, and how often it does not.
    /// <para>
    /// Every other assertion on this field is either a rule about one shape — a conditional
    /// directive, an unterminated literal — or the differential invariant, which compares the
    /// bare scan against the enclosed one and therefore reads this field only *relatively*. A
    /// defect that flips <see cref="ScanToken.DepthKnown"/> on both sides at once satisfies it
    /// by construction: <c>!true == !true</c>. Measured, that left the field unpinned at seven
    /// emission sites, where a token could claim its depth was meaningless — or claim a
    /// meaningless depth was real — with the whole suite green (adversarial review, Gemini).
    /// </para>
    /// <para>
    /// A count is not a shape, and this is not trying to be one; the shapes are gated by the
    /// named rules above. What no rule above supplies is an *absolute* reading of the field over
    /// a space large enough to reach every site that emits it, which is what this pins.
    /// </para>
    /// </summary>
    [Fact]
    public void UnknowableDepth_IsCarriedByExactlyTheTokensTheScannerCouldNotPlace()
    {
        const string Alphabet = "\"$*/@\\a{}";
        Assert.Equal("\"$*/@\\a{}", Alphabet);

        var swept = AllStringsOver(Alphabet, 4);
        Assert.Equal(9 + 81 + 729 + 6561, swept.Count);

        int known = 0, unknown = 0, inputsWithUnknown = 0;

        foreach (var input in swept)
        {
            var tokens = BodySlicer.ScanTokens([input]);
            int lost = tokens.Count(t => !t.DepthKnown);

            unknown += lost;
            known += tokens.Count - lost;

            if (lost > 0)
                inputsWithUnknown++;
        }

        // Both readings are pinned, so the field cannot be moved in either direction: pinning
        // only the unknown tokens would let every token become knowable, and pinning only the
        // known ones would let every token lose its place.
        Assert.Equal(17_602, known);
        Assert.Equal(4_452, unknown);
        Assert.Equal(1_996, inputsWithUnknown);
    }

    /// <summary>
    /// Gates both guards on the rule that coalesces literal fragments. Fragments fuse when they
    /// touch, and "touch" has to mean same line and touching columns: a literal token carries a
    /// single <see cref="ScanToken.Line"/>, so fusing across a line break would produce one token
    /// claiming a span that runs off the end of the earlier line.
    ///
    /// The columns here are aligned deliberately — the second line's literal opens at exactly the
    /// column where the first line's literal ended. That is the only shape in which the column
    /// test alone would say yes, so it is the only shape that can tell whether the line test is
    /// doing anything. The first line opens with the literal so that the stream's very first
    /// token is one, which is the only shape that reaches the emptiness guard before it. The
    /// corpus contains neither; without this test both guards are unverified, and removing either
    /// one leaves every other test passing.
    /// </summary>
    [Fact]
    public void LiteralFragments_DoNotCoalesceAcrossALineBreak()
    {
        //                  columns 0..11 ─┐
        var lines = new[] { "\"xyzzyxyzzy\"", "            \"ab\"" };
        //                    column 12 ────┘  (== the first literal's end column)

        // Every token is asserted, not only the literals. Inserting a punctuator before the
        // second line's literal keeps both literals at their asserted positions but stops the
        // stream ever reaching the coalescing guard, and the filtered assertion could not see
        // that the guard had gone unexercised (adversarial review, GPT).
        Assert.Equal(
            [(0, 0, 12, "S:\"xyzzyxyzzy\""), (1, 12, 4, "S:\"ab\"")],
            BodySlicer.ScanTokens(lines)
                .Select(t => (t.Line, t.Column, t.Length, $"{Code(t.Kind)}:{t.TextIn(lines[t.Line])}")));
    }

    [Theory]
    [InlineData("int x; // c")]
    [InlineData("var s = $\"a{b}c\";")]
    [InlineData("var s = $$\"\"\"a{{b}}c\"\"\";")]
    [InlineData("var s = @\"a\"\"b\";")]
    [InlineData("char c = '\\\\';")]
    [InlineData("#if DEBUG")]
    [InlineData("[A] void M() { }")]
    [InlineData("x = y is not null ? 1 : 2;")]
    [InlineData("var s = \"unterminated")]
    [InlineData("var v = @;")]
    [InlineData("int /**/ x;")]
    [InlineData("f(a => a.B<C>(1_000, 0xFF, 1.5e3));")]
    public void EveryCharacterOfALine_IsAccountedFor(string line)
    {
        var lines = new[] { line };
        Assert.Null(FindCoverageGap(lines, BodySlicer.ScanTokens(lines)));
    }

    /// <summary>
    /// Pins the rows of every <c>[Theory]</c> in this file by value.
    /// <para>
    /// xUnit counts rows, not distinct rows, so replacing one <c>[InlineData]</c> with a
    /// duplicate of another leaves the reported test count unchanged and every assertion
    /// passing while the case that row existed for is no longer scanned. Measured, substituting
    /// the two raw-literal rows of
    /// <see cref="LiteralsWithoutEscapes_TreatABackslashAsContent"/> with copies of its verbatim
    /// row kept the suite green at the same count while a defect that treats a backslash as an
    /// escape inside a raw literal — swallowing the quote that would close it — survived
    /// (adversarial review, Gemini).
    /// </para>
    /// <para>
    /// Requiring the rows merely to be <em>distinct</em> does not close that: the same two rows
    /// can be replaced by two different variations of the verbatim row, which are distinct
    /// strings and drop exactly as much (adversarial review, Gemini, again). Distinctness is a
    /// property of the spelling and the coverage that matters is a property of the state each
    /// row reaches, and no mechanical check of the rows can bridge that. So the rows are pinned
    /// the way this file pins every other hand-written list it depends on — the delimiter
    /// alphabet, the carried-construct openers — by value.
    /// </para>
    /// <para>
    /// This is checked here rather than by splitting the theory that exposed it into facts,
    /// because the weakness belongs to the shape and not to that theory: every theory in this
    /// file, including ones not yet written, is open to the same substitution. The totals are
    /// pinned as well, so the gate cannot go quiet by finding nothing to check.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryTheoryRow_DiffersFromItsSiblings()
    {
        int theories = 0, rows = 0;
        var pinned = new List<string>();

        foreach (var method in typeof(ScanTokenTests).GetMethods().OrderBy(m => m.Name))
        {
            var data = CustomAttributeData.GetCustomAttributes(method)
                .Where(a => a.AttributeType.Name == "InlineDataAttribute")
                .Select(a => string.Join(
                    '\u001f',
                    a.ConstructorArguments.SelectMany(Flatten)))
                .ToList();

            if (data.Count == 0)
                continue;

            theories++;
            rows += data.Count;

            foreach (var row in data)
                pinned.Add($"{method.Name}\u001f{row}");
        }

        Assert.Equal(6, theories);
        Assert.Equal(24, rows);
        Assert.Equal(
            [
                "CommentOpenerInsideALiteral_IsLiteralText\u001fvar s = \"/* not a comment */\";\u001fW:var W:s P:= S:\"/* not a comment */\" P:;",
                "CommentOpenerInsideALiteral_IsLiteralText\u001fvar s = \"http://not/a/comment\";\u001fW:var W:s P:= S:\"http://not/a/comment\" P:;",
                "EscapedBraceRunInAnInterpolatedLiteral_StaysStringContent\u001fvar s = @$\"a{{b}}c\";\u001fW:var:d0:b0 W:s:d0:b0 P:=:d0:b0 S:@$\"a{{b}}c\":d0:b0 P:;:d0:b0",
                "EscapedBraceRunInAnInterpolatedLiteral_StaysStringContent\u001fvar s = $\"a{{b}}c\";\u001fW:var:d0:b0 W:s:d0:b0 P:=:d0:b0 S:$\"a{{b}}c\":d0:b0 P:;:d0:b0",
                "EscapedBraceRunInAnInterpolatedLiteral_StaysStringContent\u001fvar s = $@\"a{{b}}c\";\u001fW:var:d0:b0 W:s:d0:b0 P:=:d0:b0 S:$@\"a{{b}}c\":d0:b0 P:;:d0:b0",
                "EveryCharacterOfALine_IsAccountedFor\u001f[A] void M() { }",
                "EveryCharacterOfALine_IsAccountedFor\u001f#if DEBUG",
                "EveryCharacterOfALine_IsAccountedFor\u001fchar c = '\\\\';",
                "EveryCharacterOfALine_IsAccountedFor\u001ff(a => a.B<C>(1_000, 0xFF, 1.5e3));",
                "EveryCharacterOfALine_IsAccountedFor\u001fint /**/ x;",
                "EveryCharacterOfALine_IsAccountedFor\u001fint x; // c",
                "EveryCharacterOfALine_IsAccountedFor\u001fvar s = \"unterminated",
                "EveryCharacterOfALine_IsAccountedFor\u001fvar s = @\"a\"\"b\";",
                "EveryCharacterOfALine_IsAccountedFor\u001fvar s = $\"a{b}c\";",
                "EveryCharacterOfALine_IsAccountedFor\u001fvar s = $$\"\"\"a{{b}}c\"\"\";",
                "EveryCharacterOfALine_IsAccountedFor\u001fvar v = @;",
                "EveryCharacterOfALine_IsAccountedFor\u001fx = y is not null ? 1 : 2;",
                "LiteralsWithoutEscapes_TreatABackslashAsContent\u001fvar s = \"\"\"{\\\"\"\"; int after = 1;\u001fW:var W:s P:= S:\"\"\"{\\\"\"\" P:; W:int W:after P:= W:1 P:;",
                "LiteralsWithoutEscapes_TreatABackslashAsContent\u001fvar s = @\"{\\\"; int after = 1;\u001fW:var W:s P:= S:@\"{\\\" P:; W:int W:after P:= W:1 P:;",
                "LiteralsWithoutEscapes_TreatABackslashAsContent\u001fvar s = $\"\"\"{b}\\\"\"\";\u001fW:var W:s P:= S:$\"\"\"{ W:b S:}\\\"\"\" P:;",
                "QuoteInsideAComment_DoesNotOpenALiteral\u001f/* a \" quote */ int x;\u001fC:/* a \" quote */ W:int W:x P:;",
                "QuoteInsideAComment_DoesNotOpenALiteral\u001f// a \" quote in a comment\u001fC:// a \" quote in a comment",
                "VerbatimAndInterpolatedInEitherOrder_StillInterpolates\u001fvar s = @$\"a{b}c\";\u001fW:var W:s P:= S:@$\"a{ W:b S:}c\" P:;",
                "VerbatimAndInterpolatedInEitherOrder_StillInterpolates\u001fvar s = $@\"a{b}c\";\u001fW:var W:s P:= S:$@\"a{ W:b S:}c\" P:;",
            ],
            pinned.Order());

        static IEnumerable<string> Flatten(CustomAttributeTypedArgument argument) =>
            argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> nested
                ? nested.SelectMany(Flatten)
                : [argument.Value?.ToString() ?? "\u0000"];
    }

    /// <summary>
    /// The same invariant over real source: every C# file the corpus assemblies' PDBs point at.
    /// Hand-written cases cover the constructs someone thought of, and this covers the ones they
    /// did not.
    /// </summary>
    [Fact]
    public void EveryCharacterOfTheCorpus_IsAccountedFor()

    {
        var files = CorpusSourceFiles();

        // The corpus is whatever the test project's references drag into the output directory, so
        // it can shrink without anything turning red. Assert its size rather than trust it.
        Assert.True(files.Count >= 100, $"Corpus fell to {files.Count} source files; the invariant below would prove little.");

        int lineCount = 0;
        int tokenCount = 0;
        var kinds = new HashSet<ScanTokenKind>();

        foreach (var file in files)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (IOException)
            {
                continue;
            }

            var tokens = BodySlicer.ScanTokens(lines);
            lineCount += lines.Length;
            tokenCount += tokens.Count;

            foreach (var token in tokens)
                kinds.Add(token.Kind);

            string? gap = FindCoverageGap(lines, tokens);
            Assert.True(gap is null, $"{file}: {gap}");
        }

        Assert.True(lineCount >= 25_000, $"Corpus fell to {lineCount} lines; the invariant above would prove little.");

        // Coverage on its own would also be satisfied by a scanner that called each whole line one
        // token, which would say nothing about whether the text was understood. Require the stream
        // to be decomposed, and to have exercised every kind the corpus can produce.
        Assert.True(tokenCount > lineCount * 3, $"{tokenCount} tokens over {lineCount} lines is too coarse to be a real decomposition.");

        var expected = Enum.GetValues<ScanTokenKind>().Except(KindsTheCorpusCannotReach).ToHashSet();
        Assert.Equal(expected, kinds);
    }

    /// <summary>
    /// Kinds no corpus file happens to contain, so the corpus cannot be what proves the scanner
    /// emits them.
    /// <para>
    /// This is compared for set equality rather than merely subtracted, so the entry cannot go
    /// stale: if a corpus assembly later gains a preprocessor directive, this test fails and the
    /// entry should be deleted. Directives are exercised instead by
    /// <see cref="ConditionalDirective_MarksTheDepthUnknowable"/> and
    /// <see cref="NonConditionalDirective_LeavesTheDepthKnown"/>.
    /// </para>
    /// </summary>
    private static readonly ScanTokenKind[] KindsTheCorpusCannotReach = [ScanTokenKind.Directive];

    private static List<string> CorpusSourceFiles()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assemblyPath in Directory.GetFiles(AppContext.BaseDirectory, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
        {
            PdbContext context;
            try
            {
                context = PdbContext.Open(assemblyPath);
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                continue;
            }

            using (context)
            {
                try
                {
                    foreach (var member in context.EnumerateMemberSources())
                    {
                        if (File.Exists(member.FilePath))
                            paths.Add(member.FilePath);
                    }
                }
                catch (BadImageFormatException)
                {
                    continue;
                }
            }
        }

        return [.. paths.OrderBy(p => p, StringComparer.Ordinal)];
    }

    /// <summary>
    /// A block comment closes on "*/" and on nothing else. Each of the three parts of that test
    /// -- the "*", the bounds check, and the "/" -- was separately droppable with a green suite
    /// (adversarial review, GPT), so each gets an input that only it rejects.
    /// </summary>
    [Fact]
    public void BlockComment_DoesNotCloseOnASlashThatNoAsteriskPrecedes()
    {
        Assert.Equal(
            "W:int W:x P:; C:/* a/b */ W:int W:y P:;",
            Render("int x; /* a/b */ int y;"));
    }

    [Fact]
    public void BlockComment_DoesNotCloseOnAnAsteriskThatNoSlashFollows()
    {
        Assert.Equal(
            "W:int W:x P:; C:/* a*x */ W:int W:y P:;",
            Render("int x; /* a*x */ int y;"));
    }

    /// <summary>
    /// The bounds half of the same test. A line inside a block comment that ends in "*" has no
    /// character after it to read, and reading one throws rather than misclassifying.
    /// </summary>
    [Fact]
    public void BlockCommentLineEndingInAnAsterisk_DoesNotReadPastTheLine()
    {
        Assert.Equal(
            "C:/* a C:b* C:c */ W:int W:y P:;",
            Render("/* a", "b*", "c */ int y;"));
    }

    /// <summary>
    /// "#if" makes the structural depth unknowable, because the braces below it may belong to a
    /// branch the compiler discards. "#ifdef" is not a C# directive and names no branch, so
    /// matching it as a conditional would discard the depth for every token that follows.
    /// </summary>
    [Fact]
    public void DirectiveWhoseNameOnlyStartsWithAConditional_KeepsTheDepthKnown()
    {
        Assert.Equal(
            "D:#ifdef X:d0:b0 W:int:d0:b0 W:x:d0:b0 P:;:d0:b0",
            RenderState("#ifdef X", "int x;"));

        // The close negative: the real directive must still give the depth up.
        Assert.Equal(
            "D:#if X:d0:b0? W:int:d0:b0? W:x:d0:b0? P:;:d0:b0?",
            RenderState("#if X", "int x;"));
    }

    /// <summary>
    /// A hole opens on "{". A "}" in literal text closes nothing -- the hole is closed from
    /// inside it -- so it is content, and the text after it is still the literal's.
    /// </summary>
    [Fact]
    public void ClosingBraceInAnInterpolatedLiteral_IsContentNotAHoleOpener()
    {
        Assert.Equal(
            "W:var W:s P:= S:$\"}x\" P:;",
            Render("var s = $\"}x\";"));
    }

    /// <summary>
    /// The escaped quote is the reason the plain-content run stops at a backslash at all. Losing
    /// that stop closes the literal early and reports its remaining text as code.
    /// </summary>
    [Fact]
    public void EscapedQuoteInsideALiteral_DoesNotCloseIt()
    {
        Assert.Equal(
            "W:var:d0:b0 W:s:d0:b0 P:=:d0:b0 S:\"a\\\"b\":d0:b0 P:;:d0:b0 W:int:d0:b0 W:y:d0:b0 P:;:d0:b0",
            RenderState("var s = \"a\\\"b\"; int y;"));
    }

    /// <summary>
    /// An unterminated character literal runs to the end of the line and stops there. Scanning
    /// for its closing quote without a bounds check reads past the line instead of ending.
    /// </summary>
    [Fact]
    public void UnterminatedCharLiteral_DoesNotReadPastTheLine()
    {
        Assert.Equal("W:char W:c P:= H:'x", Render("char c = 'x"));
    }

    /// <summary>
    /// Bracket depth is how an attribute list spanning lines is told from the code around it, so
    /// a bracket that belongs to an expression inside an interpolation hole must not move it.
    /// </summary>
    [Fact]
    public void BracketInsideAnInterpolationHole_DoesNotMoveTheOuterBracketDepth()
    {
        Assert.Equal(
            "P:[:d0:b0 W:A:d0:b1 P:(:d0:b1 S:$\"{:d0:b1 W:xs:d0:b1 P:[:d0:b1 W:i:d0:b1 " +
            "P:]:d0:b1 S:}\":d0:b1 P:):d0:b1 P:]:d0:b1 W:int:d0:b0 W:f:d0:b0 P:;:d0:b0",
            RenderState("[A($\"{xs[i]}\")] int f;"));
    }

    /// <summary>
    /// A slice can begin below the "[" that opened a list. The unmatched "]" that follows must
    /// leave the depth at zero rather than drive it negative, which would read as "inside a
    /// list" to every predicate that asks.
    /// </summary>
    [Fact]
    public void ClosingBracketWithoutAnOpener_DoesNotDriveTheBracketDepthNegative()
    {
        Assert.Equal("P:]:d0:b0 W:x:d0:b0", RenderState("] x"));
    }

    /// <summary>
    /// A verbatim literal spans lines by design, so carrying one is not losing the place. Only a
    /// literal that cannot span a line -- an unterminated single-quoted one -- is.
    /// </summary>
    [Fact]
    public void MultilineVerbatimLiteral_KeepsTheDepthKnown()
    {
        Assert.Equal(
            "W:var:d0:b0 W:s:d0:b0 P:=:d0:b0 S:@\"a:d0:b0 S:b\":d0:b0 P:;:d0:b0 " +
            "W:int:d0:b0 W:y:d0:b0 P:;:d0:b0",
            RenderState("var s = @\"a", "b\"; int y;"));
    }

    /// <summary>
    /// A hole closes on the brace that matches its opener, not on the first "}" inside it. An
    /// object initializer in a hole spells braces of its own, and closing on those turns the
    /// rest of the expression into literal text.
    /// </summary>
    [Fact]
    public void BracesInsideAHole_DoNotCloseTheInterpolation()
    {
        Assert.Equal(
            "W:var W:s P:= S:$\"{ W:new P:{ W:X P:= W:1 P:} S:}\" P:; W:int W:y P:;",
            Render("var s = $\"{new { X = 1 }}\"; int y;"));
    }


    /// <summary>
    /// Every token records the structural depth in effect where it sits, but only words and
    /// structural punctuators were ever asserted at a depth other than zero, so five emission
    /// paths could report zero from inside a block and no test could tell (adversarial review,
    /// GPT). This covers each of them at depth one: a directive, a line comment, a single-line
    /// block comment, a character literal, the "@" of a verbatim identifier, and a literal.
    /// </summary>
    [Fact]
    public void EveryKindOfTokenInsideABlock_ReportsTheEnclosingDepth()
    {
        Assert.Equal(
            "P:{:d0:b0 D:#region X:d1:b0 C:// c:d1:b0 C:/* b */:d1:b0 H:'x':d1:b0 " +
            "P:@:d1:b0 W:class:d1:b0 S:\"s\":d1:b0 P:;:d1:b0 P:}:d1:b0",
            RenderState("{", "#region X", "// c", "/* b */", "'x'", "@class", "\"s\";", "}"));
    }

    /// <summary>
    /// A block comment carried in from an earlier line is emitted by a different path than the
    /// one that opens it, and it too must report the depth it sits at.
    /// </summary>
    [Fact]
    public void BlockCommentCarriedIntoALine_ReportsTheEnclosingDepth()
    {
        Assert.Equal(
            "P:{:d0:b0 C:/* a:d1:b0 C:b */:d1:b0 P:}:d1:b0",
            RenderState("{", "/* a", "b */", "}"));
    }

    /// <summary>
    /// Losing the place is discovered at the end of the line that loses it, so the correction
    /// must reach back only as far as that line's own tokens. Reaching further would retract a
    /// depth that was known when it was recorded, and the lines above stay answerable.
    /// </summary>
    [Fact]
    public void LosingThePlaceOnALine_DoesNotUnknowTheLinesAboveIt()
    {
        Assert.Equal(
            "W:int:d0:b0 W:x:d0:b0 P:;:d0:b0 " +
            "W:var:d0:b0? W:s:d0:b0? P:=:d0:b0? S:\"unterminated:d0:b0?",
            RenderState("int x;", "var s = \"unterminated"));
    }


    /// <summary>
    /// Enclosing any content in a block and an attribute list shifts every token's structural
    /// depth and bracket depth by exactly one, and changes nothing else about it.
    /// <para>
    /// This is the self-policing form of the two rounds of findings that preceded it. Gating
    /// depth by naming the emission paths and writing a fixture for each failed twice: first
    /// because the helper could not show the field, then because every fixture that showed it
    /// sat at depth zero. Both times the fix covered the paths that had been named and left the
    /// ones that had not (adversarial review, GPT). A path does not have to be known to this
    /// test to be covered by it: any emission site reachable from the alphabet that hardcodes a
    /// depth, or that reads the wrong one, breaks the shift.
    /// </para>
    /// <para>
    /// The alphabet is chosen so the shift is exact rather than approximate. It spells a
    /// directive, a comment in both forms, a character literal, every string-literal form and
    /// its escape, an interpolation hole, and the brace that closes one. It excludes "]" alone:
    /// bracket depth clamps at zero, so an unmatched "]" leaves the bare and enclosed runs at
    /// depths that differ by less than one. That clamp is a rule in its own right, gated by
    /// <see cref="ClosingBracketWithoutAnOpener_DoesNotDriveTheBracketDepthNegative"/>.
    /// Structural depth has no such clamp -- it is allowed to go negative when a slice begins
    /// below the brace that opened its block -- so "}" keeps the shift exact and is included.
    /// </para>
    /// <para>
    /// The exhaustive arms are bounded by length, so on their own they cannot spell an opener
    /// longer than the bound: a raw literal needs three characters and a multi-dollar raw
    /// opener five. That gap is not cosmetic. It decides whether the first token on a carried
    /// line can be a raw-literal fragment, and two emission paths are reachable only that way.
    /// Measuring inertness over the bounded arms alone concluded, wrongly, that those two
    /// paths could not be observed (adversarial review, GPT). A third arm therefore seeds each
    /// literal and comment opener explicitly rather than waiting for the sweep to spell one.
    /// </para>
    /// <para>
    /// The one site this gate does not reach is the hole closer, which is gated by
    /// <see cref="BraceClosingAHole_ReportsTheDepthOutsideIt"/>.
    /// </para>
    /// </summary>
    [Fact]
    public void EnclosingContent_ShiftsEveryTokensDepthByExactlyOne()
    {
        const string Alphabet = "#/*'\"@$\\{}a";
        int checked_ = 0;
        HashSet<string>? singleSeen = null;
        HashSet<(string First, string Second)>? pairSeen = null;
        HashSet<(string First, string Second)>? seedPairs = null;

        var kindsReached = new HashSet<ScanTokenKind>();
        int deepest = 0;
        int shallowest = 0;

        void Check(string[] content)
        {
            // Build the wrapped input first and derive the bare input from it, so both scans
            // provably consume the same lines: there is exactly one array in the flow, and the
            // comparison below ties it to the content the recording observes.
            //
            // The invariant is only differential if both sides scan the same content, and
            // nothing said so. Substituting the content between the two scans -- exchanging a
            // verbatim interpolated opener for a raw one of the same length, so kind, column,
            // length and the expected shift all still match -- left every assertion passing
            // while the two sides exercised different frame states, and measured, a depth
            // defect on the frame only one side carried then survived (adversarial review,
            // GPT). A single substitution of either value is now caught: before this line both
            // scans see it and the recorded sequences fail their derivation pins; after it,
            // `wrapped` still holds the original element references.
            string[] wrapped = ["{", "[", .. content];

            var bare = BodySlicer.ScanTokens(wrapped[2..]);
            var enclosed = BodySlicer.ScanTokens(wrapped).Where(t => t.Line >= 2).ToList();

            Assert.Equal(wrapped[2..], content);

            // Record after the scan, not before. The derivation pins below are only worth
            // anything if they observe what the invariant actually ran on: recording first
            // lets the scanned content be substituted afterwards while every pin still sees
            // the canonical inputs, and measured, a carried-verbatim depth defect then
            // survives (adversarial review, GPT). Recording here means a substitution before
            // the scan is recorded too and fails the sequence pins, and one after the scan
            // fails them without weakening the scan.
            if (singleSeen is not null && content.Length == 1)
                singleSeen.Add(content[0]);

            if (pairSeen is not null && content.Length == 2)
                pairSeen.Add((content[0], content[1]));

            if (seedPairs is not null && content.Length == 2)
                seedPairs.Add((content[0], content[1]));

            foreach (var token in bare)
            {
                kindsReached.Add(token.Kind);
                deepest = Math.Max(deepest, token.Depth);
                shallowest = Math.Min(shallowest, token.Depth);
            }

            Assert.Equal(bare.Count, enclosed.Count);

            for (int i = 0; i < bare.Count; i++)
            {
                var b = bare[i];
                var e = enclosed[i];
                var where = $"[{string.Join("\\n", content)}] token {i}";

                Assert.Equal((b.Kind, b.Line + 2, b.Column, b.Length, b.DepthKnown), (e.Kind, e.Line, e.Column, e.Length, e.DepthKnown));
                Assert.True(b.Depth + 1 == e.Depth, $"{where}: depth {b.Depth} -> {e.Depth}");
                Assert.True(b.BracketDepth + 1 == e.BracketDepth, $"{where}: bracket depth {b.BracketDepth} -> {e.BracketDepth}");
            }

            checked_++;
        }

        void Walk(char[] buffer, int depth, int limit, Action<string> body)
        {
            for (int i = 0; i < Alphabet.Length; i++)
            {
                buffer[depth] = Alphabet[i];
                body(new string(buffer, 0, depth + 1));

                if (depth + 1 < limit)
                    Walk(buffer, depth + 1, limit, body);
            }
        }

        // One line reaches every path that opens a construct.
        singleSeen = [];
        Walk(new char[4], 0, 4, line => Check([line]));
        int checkedSingle = checked_;

        // Two lines reach the paths that continue one carried in from the line above, which no
        // single-line input can: a literal's later lines, and a block comment's.
        var seconds = new List<string>();
        Walk(new char[2], 0, 2, seconds.Add);

        pairSeen = [];

        foreach (var first in seconds)
        {
            foreach (var second in seconds)
                Check([first, second]);
        }

        int checkedPairs = checked_ - checkedSingle;
        var sweptPairs = pairSeen!;
        pairSeen = null;

        // A construct carried across a line break can be opened by a delimiter longer than the
        // exhaustive arms reach. Seed each one, so that the first token on the second line is a
        // fragment of every literal and comment form rather than only the short ones.
        string[] openers =
        [
            "\"", "@\"", "$\"", "$@\"", "\"\"\"", "$\"\"\"", "$$\"\"\"", "$$$\"\"\"\"", "/*",
        ];

        // The seeds are hand-written, which is the failure mode this test exists to avoid, so
        // they police themselves twice. They must be distinct, or the pinned count can be met
        // by repeating one. And each must actually open something that carries: if a seed
        // stops carrying, the "a" below it is code rather than literal or comment text, and
        // the seed is no longer reaching the paths it was added for (adversarial review, GPT).
        Assert.Equal(openers.Length, openers.Distinct().Count());

        var carriedKinds = new List<ScanTokenKind>();

        foreach (var opener in openers)
        {
            var carried = BodySlicer.ScanTokens([opener, "a"]).Last();
            Assert.True(
                carried.Kind is ScanTokenKind.StringLiteral or ScanTokenKind.Comment,
                $"opener [{opener}] no longer carries: 'a' below it scanned as {carried.Kind}");
            carriedKinds.Add(carried.Kind);
        }

        // "Carries" is not the property the arm needs, only the nearest visible one. Nine
        // distinct unterminated comments all carry, satisfy the count, the distinctness, and
        // the concatenation check, and leave the suite green -- while deleting every carried
        // *string* fragment the arm exists to reach. Measured, that swap lets a wrong depth
        // survive at two emit sites that nothing else in the suite catches. One seed must
        // therefore still open a comment, and the rest must open strings; with nine seeds and
        // the string-or-comment check above, pinning the comment pins both halves.
        Assert.Equal(1, carriedKinds.Count(k => k is ScanTokenKind.Comment));

        // Kind and length are still not the whole of why these forms: an opener list of nine
        // distinct seeds, eight of them strings, five longer than two characters, can be spelled
        // entirely without raw literals (`$$"`, `$$$"`, `$$$$"` for `"""`, `$"""`, `$$"""`), and
        // measured, that erases the carried raw-literal path at BodySlicer.cs:1413 while every
        // pin above and the suite stay green (adversarial review, GPT). Pin the raw family by
        // the behaviour that makes it a separate path rather than by its spelling: a lone quote
        // on the next line closes a quoted or verbatim literal, and does not close a raw one.
        var rawSeeds = openers.Count(o =>
            BodySlicer.ScanTokens([o, "\"a"]).Last().Kind is ScanTokenKind.StringLiteral);

        Assert.Equal(4, rawSeeds);

        // The arm's whole reason for existing is seeds the exhaustive arms cannot spell, so pin
        // how many exceed their reach. Without this, `$@"` can be exchanged for `"b` -- still
        // nine distinct seeds, eight strings, one comment, four raw -- which drops the verbatim
        // interpolated path and replaces it with a two-character opener the pair arm already
        // sweeps, adding nothing (adversarial review, GPT). I had removed this pin as redundant
        // on the grounds that the raw pin already forces four seeds of three characters or
        // more; that was wrong, and this mutation is the counterexample: it forces four, and
        // the fifth was unpinned.
        Assert.Equal(5, openers.Count(o => o.Length > 2));

        // Length is not reach either. `$@"` can be exchanged for `"bbb` -- nine distinct seeds,
        // eight strings, one comment, four raw, five longer than two characters, value pin
        // updated in step -- and the only seed that opens a literal which is *both* verbatim
        // and interpolated is gone. Measured, a wrong depth on that frame's fragment
        // (BodySlicer.cs:1431, guarded on `frame.Verbatim && frame.DollarRun > 0`) then
        // survives (adversarial review, GPT). Pin that family the same behavioural way: on the
        // next line a backslash does not escape in a verbatim literal, so the quote after it
        // closes and what follows is code; and a brace opens a hole only in an interpolated
        // one. A seed that does both is verbatim *and* interpolated, whatever it is spelled.
        bool CodeOnSecondLine(string opener, string second) =>
            BodySlicer.ScanTokens([opener, second]).Any(t => t.Line == 1 && t.Kind is ScanTokenKind.Word);

        bool Verbatim(string opener) => CodeOnSecondLine(opener, "\\\"a");

        // Four raw seeds can be four *non-interpolated* raw seeds, which would erase the
        // dollar-run ladder: the scanner tracks how many braces open a hole (`frame.DollarRun`),
        // and only `$"""`, `$$"""` and `$$$""""` exercise runs of one, two and three. The same
        // argument applies to the quote run a raw literal needs in order to close
        // (`frame.QuoteRun`): the four raw seeds can all be spelled with runs of five, seven,
        // nine and eleven, leaving no carried frame with a run of three.
        //
        // Pin the two together rather than one at a time. Separate distributions pin only the
        // marginals, and the pairing is free between them: exchanging `$$"""` for `$$""""` and
        // `$$$""""` for `$$$"""b` holds both marginals while the carried combination of dollar
        // run three with quote run four disappears, after which a wrong depth on exactly that
        // frame survives (adversarial review, GPT). The joint multiset implies both marginals,
        // so it replaces them rather than joining them.
        //
        // Verbatim belongs in the same tuple for the same reason. Pinning only how many seeds
        // are verbatim *and* interpolated leaves the non-interpolated verbatim frame free: `@"`
        // can be exchanged for an ordinary carrier while `$@"` keeps that count at one, and
        // measured, a wrong depth guarded on `frame.Verbatim && frame.DollarRun == 0` at
        // BodySlicer.cs:1431 then survives (adversarial review, GPT). Pinning the three
        // together is what stops the state from being traded away one projection at a time.
        int MinBraceRun(string opener) =>
            CodeOnSecondLine(opener, "{a}") ? 1
            : CodeOnSecondLine(opener, "{{a}}") ? 2
            : CodeOnSecondLine(opener, "{{{a}}}") ? 3
            : 0;

        int MinCloseRun(string opener)
        {
            for (int run = 1; run <= 5; run++)
            {
                if (CodeOnSecondLine(opener, new string('"', run) + "a"))
                    return run;
            }

            return 0;
        }

        Assert.Equal(
            [(false, 0, 0), (false, 0, 1), (false, 0, 3), (false, 1, 1), (false, 1, 3),
             (false, 2, 3), (false, 3, 4), (true, 0, 1), (true, 1, 1)],
            openers.Select(o => (Verbatim(o), MinBraceRun(o), MinCloseRun(o))).Order());

        // Those properties still describe the seeds rather than name them, and a seed can be
        // exchanged for another of the same kind and length -- `$@"` for `@$"` -- without
        // disturbing any of them, which drops one interpolation-order path and keeps the suite
        // green (adversarial review, Gemini). The seeds are a hand-written list, so pin the
        // list, for the same reason the alphabet is pinned by value: the properties above are
        // why these forms, and this is which.
        Assert.Equal(
            ["\"", "@\"", "$\"", "$@\"", "\"\"\"", "$\"\"\"", "$$\"\"\"", "$$$\"\"\"\"", "/*"],
            openers);

        var tails = new List<string> { "" };
        Walk(new char[1], 0, 1, tails.Add);

        seedPairs = [];
        var seededOpeners = new List<string>();

        foreach (var opener in openers)
        {
            seededOpeners.Add(opener);

            foreach (var tail in tails)
            {
                foreach (var second in seconds)
                    Check([opener + tail, second]);
            }
        }

        // The list is pinned by value from what the loop above actually seeded, not from the
        // array as it stood when the properties were computed. Every assertion on `openers` runs
        // before this loop, so assigning `openers[3] = "\"bbb"` in between satisfies all of them
        // and still drops the carried verbatim-interpolated seed, leaving the suite green while
        // a depth defect on that path survives (adversarial review, GPT). This is the same shape
        // as `Check` recording its input before scanning it: a pin that observes the intended
        // value rather than the consumed one pins nothing about what ran.
        Assert.Equal(
            ["\"", "@\"", "$\"", "$@\"", "\"\"\"", "$\"\"\"", "$$\"\"\"", "$$$\"\"\"\"", "/*"],
            seededOpeners);

        // The sweep is only as good as its size; pin it so a shrunken alphabet is visible. One
        // total across three arms is not that pin: a drop in one arm can be paid for with
        // padding in another, or with padding inside the same arm, and both trades were
        // measured to survive -- lowering the single-line limit to 3 and replacing the 14,641
        // lost calls with repeats of `Check(["a"])`, and dropping the `/*` seed and padding the
        // single-line arm by 1,584 (adversarial review, Gemini). Pin each arm's own count, and
        // pin what each arm actually swept, so padding cannot stand in for reach.
        int checkedSeeded = checked_ - checkedSingle - checkedPairs;

        Assert.Equal(16_104, checkedSingle);
        Assert.Equal(132 * 132, checkedPairs);
        Assert.Equal(9 * 12 * 132, checkedSeeded);
        Assert.Equal(16_104 + (132 * 132) + (9 * 12 * 132), checked_);

        // The single-line arm's own inputs, derived from the pinned alphabet rather than read
        // back from the `Walk` that produced them, which pins its limit of 4 as well.
        var alpha = Alphabet.Select(c => c.ToString()).ToArray();
        var byLength = new List<string[]> { alpha };

        for (int n = 1; n < 4; n++)
            byLength.Add([.. byLength[n - 1].SelectMany(_ => alpha, (prefix, next) => prefix + next)]);

        Assert.Equal(byLength.SelectMany(x => x).ToHashSet(), singleSeen);
        Assert.Equal(seconds.SelectMany(_ => seconds, (first, second) => (first, second)).ToHashSet(), sweptPairs);

        // Size and seed quality are still not the same as reach: the arm only does its work if
        // each seeded line is scanned in the position that carries, and against every second
        // line, because scanner state runs forward. Neither is implied by the count. Swapping
        // the two lines, or collapsing the inner loop onto one repeated second line, preserves
        // the count and every assertion above while dropping the carried construct or the paths
        // its continuation reaches -- the latter far enough to let a wrong depth on a carried
        // interpolated fragment go unnoticed (adversarial review, GPT and Gemini).
        //
        // Recording the ordered pair rather than the first line alone pins both at once. The
        // pair is ordered, so a swap no longer matches; it is the whole first line, so a second
        // line short enough for the exhaustive pair arm to spell cannot vouch for a seed the
        // arm cannot reach; and it names the second line, so the inner loop cannot collapse.
        // Only two-line calls are recorded, so a single-line call cannot stand in either.
        //
        // The expected set is built from the same three collections the arm iterates, so on its
        // own it would shrink in step with them: emptying `seconds` after the pair arm has used
        // it, and padding the seeded arm with repeats to hold the count, would satisfy a smaller
        // demand with less coverage (adversarial review, GPT). Its size is therefore pinned to
        // literals, and the comparison is set equality rather than membership, so the demand
        // cannot quietly shrink and the arm cannot quietly scan something else instead.
        var expected = openers
            .SelectMany(_ => tails, (opener, tail) => opener + tail)
            .SelectMany(_ => seconds, (first, second) => (first, second))
            .ToHashSet();

        Assert.Equal(9 * 12 * 132, expected.Count);
        Assert.Equal(expected, seedPairs);

        // A single product pins only the product: 12 tails against 132 seconds and 1,584 tails
        // against 1 second are the same number and not the same test (adversarial review, GPT).
        // Pin each dimension, so one cannot be spent to buy another.
        Assert.Equal(9, openers.Length);

        // Counting the swept lines says nothing about what they spell: 132 arbitrary unique
        // strings satisfy a count as well as the 132 exhaustive ones, and carry none of the
        // alphabet's meaning with them (adversarial review, GPT). Derive both sets here from the
        // pinned alphabet, independently of the `Walk` that produced them, and compare in order.
        // A second derivation is worth its duplication precisely because it is not the first:
        // it pins the contents, the ordering, and `Walk`'s limits at once, and it is anchored,
        // because the alphabet it reads is itself pinned just above.
        var expectedTails = new[] { "" }.Concat(Alphabet.Select(c => c.ToString()));
        var expectedSeconds = Alphabet.SelectMany(first =>
            new[] { first.ToString() }.Concat(Alphabet.Select(second => $"{first}{second}")));

        Assert.Equal(expectedTails, tails);
        Assert.Equal(expectedSeconds, seconds);

        // Every count above can be met by an alphabet that spells nothing: replacing `{` with a
        // letter keeps all three dimensions and the pinned total while deleting the only input
        // that opens a block, and with it the depth movement this whole test is about
        // (adversarial review, GPT). Pin what the alphabet must reach rather than which
        // characters spell it, so the demand is on the coverage and not on the notation.
        Assert.Equal(Enum.GetValues<ScanTokenKind>().ToHashSet(), kindsReached);
        Assert.True(deepest > 0, "no input opened a block, so no token was ever scanned inside one");
        Assert.True(shallowest < 0, "no input closed an unopened block, so depth never went below its start");

        // Those three say what the alphabet must accomplish without saying which characters
        // spell it, and that is both their strength and their blind spot: a character whose
        // whole job is invisible to a summary of kinds and depths can be dropped without any of
        // them noticing. `\` opens no construct, closes none, and produces no kind of its own;
        // it only changes what the next character means. Removing it leaves every count and
        // every reach intact while deleting the escape path from the sweep, after which a wrong
        // depth on an escaped fragment survives (adversarial review, GPT). Pin the alphabet
        // itself as well.
        //
        // The pin does not make the reach assertions redundant, and the division is measured
        // rather than assumed. Replacing a character *and* updating this pin in step -- the
        // shape a well-meaning edit takes -- is still caught for `{`, `}`, `#`, and `'` by the
        // three assertions above, which is what keeps editing the alphabet honest. `\` is the
        // one such edit they do not catch, and this is the assertion that does.
        Assert.Equal("#/*'\"@$\\{}a", Alphabet);
    }


    /// <summary>
    /// The brace that closes an interpolation hole reports the depth and bracket depth in effect
    /// outside the hole, which is why it is emitted with the values captured before the hole was
    /// left rather than with the current ones. It begins a literal token rather than joining one,
    /// so unlike the fragment paths its own depth survives coalescing and is observable.
    /// </summary>
    [Fact]
    public void BraceClosingAHole_ReportsTheDepthOutsideIt()
    {
        Assert.Equal(
            "P:{:d0:b0 P:[:d1:b0 W:var:d1:b1 W:s:d1:b1 P:=:d1:b1 S:$\"{:d1:b1 W:a:d1:b1 " +
            "S:}\":d1:b1 P:;:d1:b1",
            RenderState("{", "[", "var s = $\"{a}\";"));
    }

}
