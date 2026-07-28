using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Parse-validity gate for the authored-source slicer
/// (<see cref="SourceLinkResolver.ExtractMethodBody"/>).
/// <para>
/// The slicer reconstructs a member's authored text from a sequence-point line range, so it
/// has two independent boundaries: a backward scan that recovers the signature and a forward
/// scan that recovers the closing brace. Neither boundary is observable from the extracted
/// text's *signature*, which is why a member-identity round trip cannot see an end-boundary
/// defect: swallowing the enclosing type's "}" leaves the signature line untouched.
/// </para>
/// <para>
/// Roslyn is the independent oracle here. The claim is deliberately narrow — the extracted
/// text must parse as a well-formed member declaration — but it is sensitive to both
/// boundaries at once and needs no per-member expected output, so it scales over a corpus.
/// Roslyn is legitimate in a test for this: product paths stay Roslyn-free, and a hand-rolled
/// checker would only be a second copy of the heuristic under test.
/// </para>
/// <para>
/// The corpus is every PDB-bearing assembly beside the test binary whose documents resolve to
/// files on disk. Those are all built from this repository, so the sequence points are real
/// compiler output over real authored C# rather than synthesized ranges.
/// </para>
/// </summary>
public class AuthoredSourceValidityTests
{
    /// <summary>
    /// Extraction outcome for one member, classified by how the text fails to parse. The
    /// classification is diagnostic only; the assertions below name the categories they gate.
    /// </summary>
    private enum SliceOutcome
    {
        /// <summary>Parses as a well-formed member declaration.</summary>
        WellFormed,

        /// <summary>Parses once a single trailing "}" is removed — the range ran past the member.</summary>
        OverCapture,

        /// <summary>Parses once a "}" is appended — the range stopped before the member closed.</summary>
        UnderCapture,

        /// <summary>
        /// Captured an enclosing type declaration. A positional record property, a primary
        /// constructor, and a field-initializer constructor have no authored member
        /// declaration of their own, so their sequence points legitimately land on the type
        /// header. The slicer reports these as absent, so this outcome must not occur; it
        /// exists so that a regression names itself.
        /// </summary>
        TypeHeader,

        /// <summary>
        /// The slicer reported no authored declaration to isolate. This is the correct answer
        /// for the type-header shapes above, not a failure.
        /// </summary>
        NotSliceable,

        /// <summary>Anything else, including a backward scan that started mid-body.</summary>
        Malformed,
    }

    private sealed record Slice(string Member, string File, int StartLine, int EndLine, string Text, SliceOutcome Outcome);

    // A positional record's property getter, a primary constructor, and a constructor
    // synthesized from field initializers all map to the type header. Recognizing that shape
    // keeps them out of the boundary counts; it does not make them correct output. See
    // TypeHeaderShapes_AreNotSliceable_KnownGap.
    private static readonly Regex TypeDeclaration = new(
        @"^(public|internal|private|protected|sealed|abstract|static|partial|file|\[)?.*\b(record|class|struct|interface|enum)\b",
        RegexOptions.Compiled);

    private static bool ParsesAsMember(string text)
    {
        // Wrapping in a shell type is what makes a *member* declaration parseable on its own.
        var tree = CSharpSyntaxTree.ParseText(
            $"class __Shell {{\n{text}\n}}",
            new CSharpParseOptions(LanguageVersion.Preview));
        return !tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    private static SliceOutcome Classify(string text)
    {
        if (ParsesAsMember(text))
            return SliceOutcome.WellFormed;

        var trimmed = text.TrimEnd();
        if (trimmed.EndsWith('}') && ParsesAsMember(trimmed[..^1].TrimEnd()))
            return SliceOutcome.OverCapture;

        if (ParsesAsMember(text + "\n}"))
            return SliceOutcome.UnderCapture;

        var firstLine = text.TrimStart().Split('\n')[0].Trim();
        return TypeDeclaration.IsMatch(firstLine) ? SliceOutcome.TypeHeader : SliceOutcome.Malformed;
    }

    /// <summary>
    /// True when <paramref name="name"/> carries a compiler-generated segment. The compiler
    /// spells such names with a leading '&lt;' on the segment it owns — "&lt;M&gt;d__0" for an
    /// iterator, "&lt;&gt;c" for a lambda holder — which no C# identifier can spell. A generic
    /// name such as "Walk&lt;THandle&gt;" or "RelationshipChain&lt;T&gt;" also contains '&lt;',
    /// but never at the start of a segment, so it stays in the corpus.
    /// </summary>
    private static bool IsCompilerGenerated(string name)
    {
        foreach (var segment in name.Split('.', '+'))
        {
            if (segment.StartsWith('<'))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Drives the product path end to end: <see cref="PdbContext.EnumerateMemberSources"/>
    /// supplies the same anchor, line range, and finalizer flag that
    /// <c>AuthoredSourceAcquisition</c> passes to the slicer, so nothing here reconstructs a
    /// range the product would compute differently.
    /// </summary>
    private static List<Slice> SliceCorpus()
    {
        var slices = new List<Slice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

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
                List<MemberSourceInfo> members;
                try
                {
                    members = context.EnumerateMemberSources().ToList();
                }
                catch (BadImageFormatException)
                {
                    continue;
                }

                foreach (var member in members)
                {
                    // The slicer only ever runs for a member the caller selected from the API
                    // surface. Compiler-generated shapes (state machines, display classes,
                    // lambdas) spell a name segment that opens with '<' — "<M>d__0", "<>c".
                    // A generic member spells '<' too, in "Walk<THandle>", and that one is
                    // ordinary API surface the gate must keep.
                    if (IsCompilerGenerated(member.Anchor.MemberName)
                        || IsCompilerGenerated(member.Anchor.TypeFullName))
                        continue;
                    if (!File.Exists(member.FilePath))
                        continue;
                    if (!seen.Add($"{member.FilePath}|{member.StartLine}|{member.EndLine}|{member.Anchor.MemberName}"))
                        continue;

                    string sourceText;
                    try
                    {
                        sourceText = File.ReadAllText(member.FilePath);
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    var text = SourceLinkResolver.ExtractMethodBody(
                        sourceText,
                        member.StartLine,
                        member.EndLine,
                        member.Anchor.MemberName,
                        member.IsFinalizer,
                        member.IsFinalizer ? member.Anchor.TypeFullName : null);

                    slices.Add(new Slice(
                        member.Anchor.MemberName,
                        member.FilePath,
                        member.StartLine,
                        member.EndLine,
                        text ?? "",
                        text is null ? SliceOutcome.NotSliceable : Classify(text)));
                }
            }
        }

        return slices;
    }

    private static string Report(IEnumerable<Slice> offenders) =>
        string.Join("\n\n", offenders
            .OrderBy(s => s.File, StringComparer.Ordinal)
            .ThenBy(s => s.StartLine)
            .Take(10)
            .Select(s => $"{s.Member}  {Path.GetFileName(s.File)}:{s.StartLine}-{s.EndLine}\n{s.Text}"));

    /// <summary>
    /// The end boundary must not run past the member. A member that is the last one in its
    /// type has the enclosing type's "}" on the line below it, and a forward scan that runs
    /// unconditionally appends it — producing text that still carries the right signature (so
    /// an identity round trip stays green) but no longer parses.
    /// </summary>
    [Fact]
    public void SlicedMembers_DoNotCaptureTheEnclosingTypesClosingBrace()
    {
        var slices = SliceCorpus();
        Assert.NotEmpty(slices);

        var offenders = slices.Where(s => s.Outcome == SliceOutcome.OverCapture).ToList();

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} member(s) captured a trailing brace they do not own:\n\n{Report(offenders)}");
    }

    /// <summary>
    /// The corpus the sweeps above measure must stay broad enough for their rates to mean
    /// anything. <c>Assert.NotEmpty</c> alone lets it collapse to a handful of slices while
    /// every rate test still passes, and a filter change already silently dropped generics once
    /// (adversarial review, GPT). This pins the breadth those rates are computed over.
    /// <para>
    /// The floors are deliberately far below the measured counts — roughly 4,500 slices with
    /// about 60 generic ones — so ordinary fixture churn does not trip them, but a filter or
    /// acquisition failure that guts the corpus does.
    /// </para>
    /// </summary>
    [Fact]
    public void SliceCorpus_StaysBroadEnoughToMeasure()
    {
        var slices = SliceCorpus();

        Assert.True(
            slices.Count >= 1000,
            $"the corpus fell to {slices.Count} slices; the rate ceilings above are measured over it");

        var generic = slices.Where(s => s.Member.Contains('<') || s.Member.Contains('`')).ToList();
        Assert.True(
            generic.Count >= 10,
            $"the corpus holds {generic.Count} generic member(s); generic spelling has been excluded by a filter before");

        Assert.True(
            slices.Select(s => s.File).Distinct(StringComparer.Ordinal).Count() >= 10,
            "the corpus should span many files, not one fixture");
    }

    /// <summary>
    /// Non-vacuity anchor for the corpus sweep above, on a real compiled fixture rather than a
    /// synthetic string. <c>DiffAsmTarget.Api.Ping(LibB::Shared.Token)</c> is the last member of
    /// the last type in its file and has an empty body, so its whole sequence-point range is the
    /// closing brace — the exact shape the forward scan used to over-run. If the slicer's end
    /// boundary regresses, this fails on its own.
    /// </summary>
    [Fact]
    public void LastMemberOfAType_ExtractsThroughItsOwnClosingBraceOnly()
    {
        var slices = SliceCorpus()
            .Where(s => s.Member == "Ping" && Path.GetFileName(s.File) == "Api.cs")
            .ToList();

        Assert.Equal(2, slices.Count);

        var last = slices.MaxBy(s => s.StartLine)!;
        Assert.Equal(
            "public static void Ping(LibB::Shared.Token value)\n{\n}",
            last.Text);
    }

    /// <summary>
    /// A member whose sequence points map to its declaring type's header has no authored
    /// declaration to slice: a positional record's property accessor, a primary constructor,
    /// and a constructor synthesized from field initializers all land there. Rendering the
    /// header would present a truncated type declaration as the member's source — wrong output
    /// wearing the shape of success — so the slicer reports absence instead.
    /// <para>
    /// Both halves are asserted. No slice may still carry a type header, and the absent
    /// population must be non-empty, so the assertion cannot pass by the corpus simply never
    /// reaching this path.
    /// </para>
    /// </summary>
    [Fact]
    public void MembersWithNoAuthoredDeclaration_ReportAbsentSource_NotATruncatedTypeHeader()
    {
        var slices = SliceCorpus();
        Assert.NotEmpty(slices);

        var leaked = slices.Where(s => s.Outcome == SliceOutcome.TypeHeader).ToList();
        Assert.True(
            leaked.Count == 0,
            $"{leaked.Count} slice(s) rendered a type header as member source:\n\n{Report(leaked)}");

        Assert.NotEmpty(slices.Where(s => s.Outcome == SliceOutcome.NotSliceable));
    }

    /// <summary>
    /// Characterizes the defect populations that remain so they stay visible and cannot grow
    /// silently. These are not passing behavior — an under-captured property getter still
    /// renders a partial accessor under an "Original Source" heading. The ceilings are
    /// deliberately loose, because the corpus is this repository's own assemblies and exact
    /// counts move with unrelated edits, but they fail if a change makes either category
    /// materially worse.
    /// </summary>
    [Fact]
    public void RemainingBoundaryDefects_StayWithinTheirCharacterizedCeilings()
    {
        var slices = SliceCorpus();
        Assert.NotEmpty(slices);

        var counts = slices.GroupBy(s => s.Outcome).ToDictionary(g => g.Key, g => g.Count());
        int Count(SliceOutcome outcome) => counts.GetValueOrDefault(outcome);
        double Rate(SliceOutcome outcome) => 100.0 * Count(outcome) / slices.Count;

        var summary = string.Join(
            "\n",
            counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value,6}  {100.0 * kv.Value / slices.Count,5:F2}%  {kv.Key}"));

        // Measured at 1.67% and 0.30% over this corpus.
        Assert.True(Rate(SliceOutcome.UnderCapture) < 3.0, $"under-capture grew:\n{summary}\n\n{Report(slices.Where(s => s.Outcome == SliceOutcome.UnderCapture))}");
        Assert.True(Rate(SliceOutcome.Malformed) < 1.5, $"malformed slices grew:\n{summary}\n\n{Report(slices.Where(s => s.Outcome == SliceOutcome.Malformed))}");
    }

    /// <summary>
    /// Close negative cases for the type-declaration discriminator. Each of these members
    /// spells a type keyword inside an identifier — "RecordBatch", "Classify", "Structure",
    /// "Interfaces", "Enumerate", "NewClient" — so a substring test would misread every one of
    /// them as a type header and report absent source for a member that has real source.
    /// </summary>
    [Theory]
    [InlineData("public void Process(RecordBatch batch)")]
    [InlineData("public int Classify()")]
    [InlineData("private static string Structure()")]
    [InlineData("internal bool Interfaces()")]
    [InlineData("public static void Enumerate()")]
    [InlineData("protected NewClient Build()")]
    [InlineData("public sealed override int Recorded()")]
    public void MembersSpellingTypeKeywordsInsideIdentifiers_AreStillSliced(string signature)
    {
        var source = string.Join('\n', [
            "class C",                  // 1
            "{",                        // 2
            $"    {signature}",         // 3
            "    {",                    // 4  <- StartLine
            "        Use();",           // 5
            "    }",                    // 6  <- EndLine
            "}",                        // 7
        ]);

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 6, methodName: "M");

        Assert.NotNull(body);
        Assert.Equal($"{signature}\n{{\n    Use();\n}}", body);
    }

    /// <summary>
    /// Positive cases. A range that lands on a type header has no member declaration to
    /// isolate, whatever modifiers lead it, so the slicer reports absence.
    /// <para>
    /// The declaration is the first line of the source deliberately. An earlier version put
    /// "namespace N;" above it, which masked the check: "namespace" is itself a type-declaration
    /// keyword, so the first line answered for every case and the declaration under test was
    /// never read. Adversarial review (Gemini Pro) caught that, and the same-line attribute
    /// cases below fail without the trivia-stripping fix.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("public record ForwarderSummaryRow(")]
    [InlineData("public class TypeView")]
    [InlineData("internal readonly ref struct Slice")]
    [InlineData("public sealed partial record struct Point(")]
    [InlineData("file static class Helpers")]
    // A declaration may share its line with the attributes and comments that lead it.
    [InlineData("[System.Obsolete] public record R(int X)")]
    [InlineData("[A][B] public record struct R(int X)")]
    [InlineData("[Foo(new[] { 1 })] public record R(int X)")]
    [InlineData("[Foo(\"]\")] public record R(int X)")]
    [InlineData("/* leading */ public record R(int X)")]
    [InlineData("/* a */ [B] /* c */ public class R")]
    public void RangesLandingOnATypeHeader_ReportAbsence(string declaration)
    {
        var source = string.Join('\n', [
            declaration,                // 1  <- StartLine
            "    int X = 1;",           // 2  <- EndLine
        ]);

        Assert.Null(SourceLinkResolver.ExtractMethodBody(source, startLine: 1, endLine: 2, methodName: ".ctor"));
    }

    /// <summary>
    /// A function-pointer return type spells <c>delegate*</c>, which leads a member's return
    /// type rather than a delegate declaration. Reading it as a type header discarded the
    /// authored source of a member that has it — a false absence, which is the failure mode
    /// that costs a user real output. Found by adversarial review (GPT).
    /// </summary>
    [Fact]
    public void FunctionPointerReturnType_IsNotATypeDeclaration()
    {
        var source = string.Join('\n', [
            "class C",                                      // 1
            "{",                                            // 2
            "    public unsafe delegate*<int, int> Ret()",   // 3
            "    {",                                        // 4  <- StartLine
            "        return null;",                         // 5
            "    }",                                        // 6  <- EndLine
            "}",                                            // 7
        ]);

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 6, methodName: "Ret");

        Assert.Equal(
            "public unsafe delegate*<int, int> Ret()\n{\n    return null;\n}",
            body);
    }

    /// <summary>
    /// A constructor that leads with no accessibility modifier is invisible to the backward
    /// scan, because ".ctor" is not how source spells it, so the scan walks up to the enclosing
    /// type header. That header is real, but the constructor below it is real too: the member
    /// has authored source and must not be reported absent. Found by adversarial review (GPT).
    /// <para>
    /// The relocated start must also be the start the end boundary is measured from. Measured
    /// from the type header the range still has the type's block open, so the forward scan
    /// would append the type's closing brace — which is what the assertion below pins.
    /// </para>
    /// </summary>
    [Fact]
    public void ConstructorWithoutAccessibilityModifier_KeepsItsAuthoredSource()
    {
        var source = string.Join('\n', [
            "namespace N;",                                 // 1
            "readonly struct Result",                       // 2
            "{",                                            // 3
            "    Result(string name)",                      // 4
            "    {",                                        // 5  <- StartLine
            "        Name = name;",                         // 6
            "    }",                                        // 7  <- EndLine
            "}",                                            // 8
        ]);

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 7, methodName: ".ctor");

        Assert.Equal(
            "Result(string name)\n{\n    Name = name;\n}",
            body);
    }

    /// <summary>
    /// The counterpart to the case above: a primary constructor's parameters sit on the type
    /// header itself, so there is no constructor declaration below it and absence is correct.
    /// This is what keeps the constructor recovery from re-opening the bug it sits next to.
    /// </summary>
    [Theory]
    [InlineData("public class C(int x)")]
    [InlineData("public record R(int X)")]
    [InlineData("public readonly record struct P(int X)")]
    public void PrimaryConstructor_StillReportsAbsence(string declaration)
    {
        var source = string.Join('\n', [
            declaration,                // 1  <- StartLine
            "{",                        // 2
            "    int F = x;",           // 3  <- EndLine
            "}",                        // 4
        ]);

        Assert.Null(SourceLinkResolver.ExtractMethodBody(source, startLine: 1, endLine: 3, methodName: ".ctor"));
    }

    /// <summary>
    /// Constructor recovery matches a declaration, not a spelling. A statement inside a method
    /// body can spell the type's own name followed by "(" — <c>new R(1);</c>, a bare
    /// <c>R(1);</c> call — and treating one as the member's declaration would relocate the
    /// slice into the body and present a fragment of a statement as authored source.
    /// <para>
    /// This is the gate named by <c>IndexOfConstructorDeclaration</c>: a candidate counts only
    /// at member level, directly inside the type's own block, and "new" is not a constructor
    /// modifier. Found by adversarial review (MAI-Code).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("new R(1);")]
    [InlineData("R(1);")]
    [InlineData("var a = new R(1);")]
    [InlineData("string R(int x) => x.ToString();")]
    [InlineData("R(int x) => x.ToString();")]
    public void ConstructorRecovery_IgnoresStatementsThatSpellTheTypeName(string statement)
    {
        var source = string.Join('\n', [
            "public record R(int X)",   // 1  <- StartLine
            "{",                        // 2
            "    void M()",             // 3
            "    {",                    // 4
            "        " + statement,     // 5  <- EndLine
            "    }",                    // 6
            "}",                        // 7
        ]);

        Assert.Null(SourceLinkResolver.ExtractMethodBody(source, startLine: 1, endLine: 5, methodName: "get_X"));
    }

    /// <summary>
    /// A positional record's primary constructor has no authored declaration, and its sequence
    /// range can span the whole type body — a secondary constructor and the property
    /// initializers below it included. Searching that whole range found the secondary
    /// constructor and presented one member's source as another's (adversarial review, GPT).
    /// A declaration the backward scan walked past sits at or above the member's own first
    /// sequence point, so nothing below that point is a candidate.
    /// </summary>
    [Fact]
    public void ConstructorRecovery_IgnoresConstructorsBelowTheFirstSequencePoint()
    {
        var source = string.Join('\n', [
            "public sealed record Present(",                     //  1  <- StartLine
            "    int Old,",                                      //  2
            "    int New,",                                      //  3
            "    string? Detail = null) : I",                    //  4
            "{",                                                 //  5
            "    public Present(",                               //  6
            "        int Old,",                                  //  7
            "        int New)",                                  //  8
            "        : this(Old, New, Detail: null)",            //  9
            "    {",                                             // 10
            "    }",                                             // 11
            "",                                                  // 12
            "    public int Old { get; } = Old;",                // 13
            "}",                                                 // 14
        ]);

        Assert.Null(SourceLinkResolver.ExtractMethodBody(source, startLine: 1, endLine: 13, methodName: ".ctor"));
    }

    /// <summary>
    /// A function-pointer return type leads a method, not a delegate declaration, and C# allows
    /// trivia between <c>delegate</c> and <c>*</c>. Requiring them to be adjacent made the
    /// spaced spelling read as a delegate type and the method vanish (adversarial review, GPT).
    /// </summary>
    [Theory]
    [InlineData("delegate*<int, int>")]
    [InlineData("delegate *<int, int>")]
    [InlineData("delegate  *<int, int>")]
    [InlineData("delegate\t*<int, int>")]
    public void FunctionPointerReturnType_IsNotADelegateDeclaration(string returnType)
    {
        var source = string.Join('\n', [
            "unsafe class C",                       // 1
            "{",                                    // 2
            $"    public static {returnType} Ret()",// 3
            "    {",                                // 4  <- StartLine
            "        return null;",                 // 5
            "    }",                                // 6  <- EndLine
            "}",                                    // 7
        ]);

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 6, methodName: "Ret");

        Assert.Equal($"public static {returnType} Ret()\n{{\n    return null;\n}}", body);
    }

    /// <summary>
    /// An attribute list's brackets are counted structurally, and a comment inside the list may
    /// spell one of its own. Reading the comment as code closed the list early, which left a
    /// positional record's header looking like an ordinary declaration and returned the
    /// truncated header as the member's source (adversarial review, MAI-Code).
    /// </summary>
    [Theory]
    [InlineData("[Foo(/* ] */)] public record R(int X);")]
    [InlineData("[Foo(/* [ */)] public record R(int X);")]
    public void CommentsInsideAnAttributeList_DoNotCloseIt(string header)
    {
        var source = string.Join('\n', [
            header,                       // 1  <- StartLine
        ]);

        Assert.Null(SourceLinkResolver.ExtractMethodBody(source, startLine: 1, endLine: 1, methodName: ".ctor"));
    }

    /// <summary>
    /// A constructor may share its line with anything that can precede it. Asking only about
    /// the start of the line, and then only about the text after the line's first brace,
    /// reported such constructors absent (adversarial review, MAI-Code and Gemini): a brace
    /// inside a comment or a literal was taken for the type's, and an earlier member on the
    /// line was never stepped over. A member begins at the start of the line or just past a
    /// brace or semicolon, and every such position is now asked.
    /// </summary>
    [Theory]
    [InlineData("public class C { C() { } }")]
    [InlineData("public class C /* { */ { C() { } }")]
    [InlineData("public class C { C(string s = \"{\") { } }")]
    [InlineData("public class C { C(char c = '{') { } }")]
    [InlineData("public class C { int X; C() { } }")]
    [InlineData("class C { string s = \"{\"; C() { } }")]
    [InlineData("public class C { void M() { } C() { } }")]
    public void ConstructorRecovery_FindsAConstructorSharingItsLine(string header)
    {
        Assert.Equal(header, SourceLinkResolver.ExtractMethodBody(header, startLine: 1, endLine: 1, methodName: ".ctor"));
    }

    /// <summary>
    /// The type's block may open on a line below its header, and a constructor may follow it
    /// on that same line.
    /// </summary>
    [Fact]
    public void ConstructorRecovery_FindsAConstructorAfterAnOpeningBraceBelowTheHeader()
    {
        Assert.Equal(
            "{ C() { } }",
            SourceLinkResolver.ExtractMethodBody("class C\n{ C() { } }", startLine: 2, endLine: 2, methodName: ".ctor"));
    }

    /// <summary>
    /// Asking more positions must not accept more shapes. A constructor call in an initializer
    /// and a nested type's constructor both spell the type name followed by a parameter list,
    /// and neither is a constructor declared at this type's member level.
    /// </summary>
    [Theory]
    [InlineData("public class C { static C I = new C(); }")]
    [InlineData("public class C { class D { D() { } } }")]
    [InlineData("public record R(string s = \"{ R()\") ;")]
    public void ConstructorRecovery_IgnoresNamesThatAreNotMemberLevelDeclarations(string source)
    {
        Assert.Null(SourceLinkResolver.ExtractMethodBody(source, startLine: 1, endLine: 1, methodName: ".ctor"));
    }

    /// <summary>
    /// The same shape for a positional record still has no authored constructor, so the header
    /// line must not be read as one.
    /// </summary>
    [Theory]
    [InlineData("public record R(int X) { }")]
    [InlineData("public record R(int X);")]
    public void ConstructorRecovery_DoesNotReadARecordHeaderAsAConstructor(string header)
    {
        Assert.Null(SourceLinkResolver.ExtractMethodBody(header, startLine: 1, endLine: 1, methodName: ".ctor"));
    }

    /// <summary>
    /// C# allows whitespace and comments between any two tokens, so a tab-separated modifier,
    /// a comment between <c>delegate</c> and <c>*</c>, and a comment between a constructor's
    /// name and its parameter list all spell the same declarations (adversarial review, GPT).
    /// Matching only literal spaces made each of them read as something else.
    /// </summary>
    [Fact]
    public void TriviaBetweenTokens_DoesNotChangeWhatIsDeclared()
    {
        // A tab-separated type header is still a type header, so a field-initializer
        // constructor above it is still absent rather than the whole type.
        Assert.Null(SourceLinkResolver.ExtractMethodBody(
            "public\tclass C\n{\n    int X = Get();\n    static int Get() => 0;\n}",
            startLine: 1, endLine: 3, methodName: ".ctor"));

        // A commented gap does not turn a function-pointer return type into a delegate.
        Assert.Equal(
            "public static delegate /* gap */ *<int, int> Ret()\n{\n    return default;\n}",
            SourceLinkResolver.ExtractMethodBody(
                "unsafe class C\n{\n    public static delegate /* gap */ *<int, int> Ret()\n    {\n        return default;\n    }\n}",
                startLine: 4, endLine: 6, methodName: "Ret"));

        // A commented gap does not hide a constructor's parameter list.
        Assert.Equal(
            "C /* gap */ ()\n{\n}",
            SourceLinkResolver.ExtractMethodBody(
                "class C\n{\n    C /* gap */ ()\n    {\n    }\n}",
                startLine: 4, endLine: 5, methodName: ".ctor"));
    }

    /// <summary>
    /// Known gap, pinned rather than fixed. An attribute list is read one line at a time, so a
    /// declaration that follows the *closing* line of a multi-line attribute is not recognized
    /// and the header is returned instead of absence (adversarial review, GPT). This is not a
    /// regression — the base behaves identically — and closing it needs attribute-bracket state
    /// carried across lines, which is a change to the shared scanner rather than to this
    /// discriminator. The assertion records today's wrong answer so that fixing it is visible.
    /// </summary>
    [Fact]
    public void DeclarationOnAMultiLineAttributesClosingLine_IsNotRecognized_KnownGap()
    {
        var source = "[System.Obsolete( // comment with ]\n    \"why\")] public record R(int X);";

        var slice = SourceLinkResolver.ExtractMethodBody(source, startLine: 2, endLine: 2, methodName: ".ctor");

        // The right answer is null. Pin the wrong one so the gap cannot widen unnoticed.
        Assert.Equal("\"why\")] public record R(int X);", slice);
    }

    /// <summary>
    /// The positive half of the same discriminator: a constructor that really is declared at
    /// member level is still recovered, whichever accepted modifier leads it or none at all.
    /// </summary>
    [Theory]
    [InlineData("Result")]
    [InlineData("public Result")]
    [InlineData("internal Result")]
    [InlineData("protected Result")]
    public void ConstructorRecovery_AcceptsMemberLevelDeclarations(string declaration)
    {
        var source = string.Join('\n', [
            "public sealed record Result",          // 1
            "{",                                    // 2
            $"    {declaration}(string name)",      // 3
            "    {",                                // 4  <- StartLine
            "        Name = name;",                 // 5
            "    }",                                // 6  <- EndLine
            "}",                                    // 7
        ]);

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 6, methodName: ".ctor");

        Assert.Equal(
            $"{declaration}(string name)\n{{\n    Name = name;\n}}",
            body);
    }

    /// <summary>
    /// A brace inside an interpolation hole belongs to the hole, not to the enclosing block.
    /// The end-boundary decision reads brace depth to tell a range that closes its own block
    /// from one that does not, so a hole whose nested string quotes a brace must not move that
    /// count. When it did, the depth reached zero early, the range looked closed, the forward
    /// scan was suppressed, and the member lost its own closing brace.
    /// <para>
    /// Each case pairs an interpolated line with a plain-string line of the same shape: the
    /// literal must not change the slice, so both must extract identically.
    /// </para>
    /// </summary>
    [Theory]
    // A nested string containing a closing brace, inside an object initializer in the hole.
    [InlineData("return $\"{new Holder { S = \"}\" }.S}\";")]
    // Escaped braces in the literal text.
    [InlineData("return $\"{{ {Value} }}\";")]
    // A nested interpolated string inside the hole.
    [InlineData("return $\"{$\"{Value}\"}\";")]
    // A raw interpolated string whose content spells braces.
    [InlineData("return $\"\"\"{ Value }\"\"\";")]
    // A char literal spelling a brace inside the hole.
    [InlineData("return $\"{Pick('}')}\";")]
    // Verbatim interpolated, both spellings, with a quoted brace inside the hole. These used to
    // be handed to the plain verbatim path, which does not know holes (adversarial review, GPT).
    [InlineData("return $@\"{new Holder { S = \"}\" }.S}\";")]
    [InlineData("return @$\"{new Holder { S = \"}\" }.S}\";")]
    // A raw literal nested inside a raw interpolated hole: the inner quote run is not the outer
    // literal's terminator (adversarial review, GPT).
    [InlineData("return $$\"\"\"{{ new Holder { S = \"\"\"}\"\"\" }.S }}\"\"\";")]
    // A comment inside a hole may spell a brace, which belongs to neither the hole nor the
    // block (adversarial review, GPT).
    [InlineData("return $\"{Value /* { */}\";")]
    [InlineData("return $\"{Value /* } */}\";")]
    public void BracesInsideInterpolationHoles_DoNotEndTheDeclaration(string statement)
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public string M()",
            "    {",
            "        " + statement,
            "    }",
            "}",
        ];

        // The range ends on the statement, as a sequence-point range does; the member's own
        // closing brace is recovered by the forward scan.
        var body = SourceLinkResolver.ExtractMethodBody(
            string.Join("\n", lines), startLine: 5, endLine: 5, methodName: "M");

        Assert.NotNull(body);
        Assert.Equal(
            $"public string M()\n{{\n    {statement}\n}}",
            body);
    }

    /// <summary>
    /// The forward scan yields to a sibling accessor, and only to one. Asking whether any line
    /// "opens a declaration" read a <c>static</c> local function — and any other statement
    /// leading with a declaration modifier — as a sibling, and truncated the enclosing method
    /// at it (adversarial review, Gemini). Only a property or event block can hold a sibling
    /// accessor, so the question is asked only when the member being sliced is an accessor.
    /// </summary>
    [Theory]
    [InlineData("static void L() { }")]
    [InlineData("static int F() => 1;")]
    [InlineData("async Task T() => await U();")]
    [InlineData("var f = async () => await U();")]
    [InlineData("int[] a = [1, 2, 3];")]
    [InlineData("const int q = 1;")]
    public void StatementsThatLeadWithADeclarationModifier_DoNotTruncateTheMember(string statement)
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public void M()",
            "    {",
            "        int x = 1;",
            "        " + statement,
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 4, 5, "M");

        Assert.NotNull(slice);
        Assert.Equal(SliceOutcome.WellFormed, Classify(slice));
        Assert.Contains(statement, slice);
    }

    /// <summary>
    /// The other half: an accessor's slice must still stop at its sibling, including when the
    /// sibling shares its line with a comment or carries its own modifier or attribute
    /// (adversarial review, Gemini).
    /// </summary>
    [Theory]
    [InlineData("set => _ = value;")]
    [InlineData("/* c */ set => _ = value;")]
    [InlineData("private set => _ = value;")]
    [InlineData("[Foo] set => _ = value;")]
    [InlineData("/* a */ private set => _ = value;")]
    [InlineData("init => _ = value;")]
    public void AccessorSlice_StopsAtItsSibling(string sibling)
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public int P",
            "    {",
            "        get => 1;",
            "        " + sibling,
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 5, 5, "get_P");

        Assert.Equal("public int P\n{\n    get => 1;", slice);
    }

    /// <summary>
    /// Since C# 11 an interpolation hole may span lines even in a single-quoted literal. Treating
    /// every non-verbatim, non-raw literal as bound to one line marked valid source untracked and
    /// truncated the member (adversarial review, Gemini).
    /// </summary>
    [Fact]
    public void InterpolationHoleSpanningLines_DoesNotAbandonTheScan()
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public void M()",
            "    {",
            "        var s = $\"{",
            "            1",
            "        }\";",
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 4, 5, "M");

        Assert.NotNull(slice);
        Assert.Equal(SliceOutcome.WellFormed, Classify(slice));
        Assert.EndsWith("}\";\n}", slice);
    }

    /// <summary>
    /// The counterpart to the case above: a literal's own text really is bound to its line, so an
    /// unterminated one must still leave the depth unknown rather than be read across the break.
    /// </summary>
    [Fact]
    public void UnterminatedLiteralText_StillLeavesTheDepthUnknown()
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public void M()",
            "    {",
            "        var s = \"oops",
            "        int y = 2;",
            "    }",
            "}",
        ];

        // The scan must not claim to have found the member's closing brace by reading through
        // a literal it lost its place in.
        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 4, 5, "M");

        Assert.DoesNotContain("int y = 2;", slice);
    }

    /// <summary>
    /// A verbatim literal is never raw. Reading any run of three or more quotes as a raw
    /// delimiter left <c>@""""</c> — a verbatim string holding one quote — open, and the member's
    /// closing brace was lost with it (adversarial review, GPT).
    /// </summary>
    [Theory]
    [InlineData("return @\"\"\"\";")]
    [InlineData("return @\"a\"\"b\";")]
    [InlineData("return $@\"\"\"\";")]
    [InlineData("return @$\"\"\"\";")]
    [InlineData("return \"\"\"raw\"\"\";")]
    public void VerbatimQuoteRuns_AreNotReadAsRawDelimiters(string statement)
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public string M()",
            "    {",
            "        " + statement,
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 4, 5, "M");

        Assert.NotNull(slice);
        Assert.Equal(SliceOutcome.WellFormed, Classify(slice));
    }

    /// <summary>
    /// Conditional-compilation directives put braces in branches the compiler may discard, so the
    /// structural depth below one is unknowable. Counting them made a discarded <c>{</c> consume
    /// the member's own closing brace and take the enclosing type's instead (adversarial review,
    /// GPT). The slice must fall back to the range rather than reach past it on a bad count.
    /// </summary>
    [Fact]
    public void ConditionalDirective_SuppressesDepthBasedRecovery()
    {
        string[] lines =
        [
            "class C",
            "{",
            "    public void M()",
            "    {",
            "#if UNUSED",
            "        {",
            "#endif",
            "        System.Console.WriteLine();",
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 4, 8, "M");

        Assert.NotNull(slice);
        Assert.DoesNotContain("class C", slice);
        Assert.EndsWith("System.Console.WriteLine();", slice.TrimEnd());
    }

    /// <summary>
    /// Non-conditional directives do not move braces between branches, so they must not cost the
    /// member its recovery.
    /// </summary>
    [Theory]
    [InlineData("#line default")]
    [InlineData("#nullable enable")]
    [InlineData("#pragma warning disable CS0168")]
    public void NonConditionalDirective_LeavesRecoveryIntact(string directive)
    {
        string[] lines =
        [
            "class C",
            "{",
            "    public void M()",
            "    {",
            directive,
            "        System.Console.WriteLine();",
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 4, 6, "M");

        Assert.NotNull(slice);
        Assert.Equal(SliceOutcome.WellFormed, Classify(slice));
        Assert.EndsWith("}", slice.TrimEnd());
    }

    /// <summary>
    /// A region pair is the directive most likely to appear inside a member. It moves no braces,
    /// so it must not cost the member its recovery either.
    /// </summary>
    [Fact]
    public void RegionDirective_LeavesRecoveryIntact()
    {
        string[] lines =
        [
            "class C",
            "{",
            "    public void M()",
            "    {",
            "#region R",
            "        System.Console.WriteLine();",
            "#endregion",
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 4, 6, "M");

        Assert.NotNull(slice);
        Assert.Equal(SliceOutcome.WellFormed, Classify(slice));
        Assert.EndsWith("}", slice.TrimEnd());
    }

    /// <summary>
    /// An attribute list is not itself an accessor. Reading any line that opens with "[" as a
    /// sibling truncated an accessor at an attributed local function in its own body
    /// (adversarial review, MAI-Code). The list is skipped and the question asked of what
    /// follows it.
    /// </summary>
    [Theory]
    [InlineData("[System.Obsolete]", "void Local() { }")]
    [InlineData("[System.Obsolete]", "static void Local() { }")]
    [InlineData("[System.Obsolete(\"]\")]", "void Local() { }")]
    public void AttributedLocalFunction_DoesNotEndAnAccessor(string attribute, string declaration)
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public int P",
            "    {",
            "        get",
            "        {",
            "            int x = 1;",
            "            " + attribute,
            "            " + declaration,
            "            return x;",
            "        }",
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 7, 7, "get_P");

        Assert.NotNull(slice);
        Assert.Equal(SliceOutcome.WellFormed, Classify(slice));
        Assert.Contains(declaration, slice);
    }

    /// <summary>
    /// The other half: an attribute that really does precede a sibling accessor still stops the
    /// slice, whether it shares the accessor's line or sits above it.
    /// </summary>
    [Theory]
    [InlineData("[System.Obsolete] set => _ = value;")]
    [InlineData("[System.Obsolete(\"]\")] set => _ = value;")]
    [InlineData("[System.Obsolete]\n        set => _ = value;")]
    public void AttributedSiblingAccessor_StillEndsTheSlice(string sibling)
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public int P",
            "    {",
            "        get => 1;",
            "        " + sibling,
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 5, 5, "get_P");

        Assert.Equal("public int P\n{\n    get => 1;", slice);
    }

    /// <summary>
    /// The sibling-accessor question is asked before the line is scanned, so it must not be
    /// asked at all when the carried lexical state says the line is not code. An accessor
    /// keyword inside a multi-line block comment or raw string literal is text, and reading it
    /// as a sibling truncated the accessor (adversarial review, Gemini).
    /// </summary>
    [Theory]
    [InlineData("/*", "set", "*/")]
    [InlineData("/*", "get => 1;", "*/")]
    [InlineData("_ = \"\"\"", "set", "\"\"\";")]
    [InlineData("_ = \"\"\"", "[Foo] init", "\"\"\";")]
    public void AccessorKeywordInsideCommentOrLiteral_IsNotASibling(string open, string body, string close)
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public int Property",
            "    {",
            "        get",
            "        {",
            "            return 1;",
            "            " + open,
            "            " + body,
            "            " + close,
            "        }",
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 7, 7, "get_Property");

        Assert.NotNull(slice);
        Assert.Equal(SliceOutcome.WellFormed, Classify(slice));
        Assert.Contains(body, slice);
    }

    /// <summary>
    /// An accessor keyword is only an accessor when the token after it says so. Accepting a
    /// bare "=" read an assignment to a local named <c>set</c> as a sibling and truncated the
    /// getter around it (adversarial review, GPT).
    /// </summary>
    [Theory]
    [InlineData("set = 1;")]
    [InlineData("set += 1;")]
    [InlineData("get = set;")]
    public void AssignmentToALocalNamedForAnAccessor_IsNotASibling(string statement)
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public int P",
            "    {",
            "        get",
            "        {",
            "            int set = 0, get = 0;",
            "            " + statement,
            "            return set;",
            "        }",
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 5, 5, "get_P");

        Assert.Contains(statement, slice);
        Assert.Contains("return set;", slice);
    }

    /// <summary>
    /// A bracketed construct may span lines, and a line inside one is not a declaration. With
    /// no bracket state carried across lines, an attribute named for an accessor truncated the
    /// member it was attached to (adversarial review, GPT).
    /// </summary>
    [Fact]
    public void AccessorKeywordInsideAMultiLineAttributeList_IsNotASibling()
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public int P",
            "    {",
            "        get",
            "        {",
            "            int x = 1;",
            "            [",
            "            set",
            "            ]",
            "            void Local() { }",
            "            return x;",
            "        }",
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 5, 5, "get_P");

        Assert.Contains("void Local() { }", slice);
        Assert.Contains("return x;", slice);
    }

    /// <summary>
    /// The attribute list is now measured by the shared scanner rather than by a second,
    /// simpler one that started reading a literal at its quote. That copy took <c>@"x""</c>
    /// for a closed string and then read a <c>]</c> in its text as the list's terminator,
    /// hiding the sibling that followed (adversarial review, GPT).
    /// </summary>
    [Theory]
    [InlineData("[Foo(@\"x\"\" ] y\")] set => _ = value;")]
    [InlineData("[Foo(\"\"\" ] \"\"\")] set => _ = value;")]
    [InlineData("[Foo(\"a ] b\")] set => _ = value;")]
    [InlineData("[Foo(']')] set => _ = value;")]
    public void LiteralsInsideAnAttributeList_DoNotTerminateIt(string sibling)
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public int P",
            "    {",
            "        get => 1;",
            "        " + sibling,
            "    }",
            "    private int _;",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 5, 5, "get_P");

        Assert.Equal("public int P\n{\n    get => 1;", slice);
    }

    /// <summary>
    /// The mirror of the case above. A line that *closes* a multi-line comment or literal and
    /// then declares a real sibling accessor is code from that point, so suppressing the
    /// question whenever the line merely began inside one over-captured past the sibling
    /// (adversarial review, MAI-Code). The question is asked of the line's code, not of whether
    /// the line started as code.
    /// </summary>
    [Theory]
    [InlineData("/*", "         */ set => _ = value;")]
    [InlineData("/* multi", "line */ set => _ = value;")]
    [InlineData("/*", "         */ [Foo] set => _ = value;")]
    public void SiblingAccessorAfterAClosingComment_StillEndsTheSlice(string open, string close)
    {
        string[] lines =
        [
            "public class C",
            "{",
            "    public int P",
            "    {",
            "        get => 1;",
            "        " + open,
            "        " + close,
            "    }",
            "}",
        ];

        var slice = SourceLinkResolver.ExtractMethodBody(string.Join("\n", lines), 5, 5, "get_P");

        Assert.Equal("public int P\n{\n    get => 1;", slice);
    }

    /// <summary>
    /// A sequence range that ends on a statement above the member's closing brace must still
    /// recover the whole member. The forward scan used to stop at the first non-empty line
    /// below the range, so every statement after that one — and the closing brace with them —
    /// was dropped, and the slice did not parse (adversarial review, MAI-Code).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void RangeEndingAboveTheClosingBrace_RecoversTheWholeMember(int trailingStatements)
    {
        var body = Enumerable.Range(1, trailingStatements).Select(n => $"        int v{n} = {n};");
        string[] lines =
        [
            "public class C",
            "{",
            "    public void M()",
            "    {",
            "        int x = 0;",
            .. body,
            "    }",
            "}",
        ];

        var text = string.Join("\n", lines);

        // The range stops on the first statement; everything below it belongs to the member.
        var slice = SourceLinkResolver.ExtractMethodBody(text, startLine: 4, endLine: 5, methodName: "M");

        Assert.NotNull(slice);
        Assert.Equal(SliceOutcome.WellFormed, Classify(slice));
        Assert.EndsWith($"int v{trailingStatements} = {trailingStatements};\n}}", slice);
    }

    /// <summary>
    /// The boundary the scan above must not cross: a member that terminates its own declaration
    /// owns no brace below it, so the next one closes the enclosing type (issue #3278).
    /// </summary>
    [Theory]
    [InlineData("public string P => \"{\";", "get_P")]
    [InlineData("public int P { get; set; }", "get_P")]
    [InlineData("public string R() => \"\"\";\";", "R")]
    public void MemberThatClosesItsOwnDeclaration_DoesNotTakeTheTypeBrace(string member, string name)
    {
        var text = string.Join("\n", ["public class C", "{", "    " + member, "}"]);

        var slice = SourceLinkResolver.ExtractMethodBody(text, startLine: 3, endLine: 3, methodName: name);

        Assert.Equal(member, slice);
    }
}
