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
            "W:var W:s P:= S:$$\"\"\"x{y}z\"\"\" P:;",
            Render("var s = $$\"\"\"x{y}z\"\"\";"));
    }

    [Fact]
    public void EmptyLiteral_IsOneToken()
    {
        Assert.Equal("W:var W:e P:= S:\"\" P:;", Render("var e = \"\";"));
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
        var tokens = BodySlicer.ScanTokens(lines);

        Assert.Equal(1, Single(tokens, lines, "set", line: 1).BracketDepth);
        Assert.Equal(0, Single(tokens, lines, "get", line: 2).BracketDepth);
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
}
