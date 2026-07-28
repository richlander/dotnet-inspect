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
}
