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
        /// header and there is nothing for the slicer to slice.
        /// </summary>
        TypeHeader,

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
                        text,
                        Classify(text)));
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
    /// Characterizes the dominant remaining defect population so it stays visible and cannot
    /// grow silently. These are *not* passing behavior: a positional record property currently
    /// renders a truncated type header under an "Original Source" heading, which is wrong
    /// output rather than absent output. The ceilings are deliberately loose — the corpus is
    /// this repository's own assemblies, so exact counts move with unrelated edits — but they
    /// fail if a change makes any category materially worse.
    /// </summary>
    [Fact]
    public void TypeHeaderShapes_AreNotSliceable_KnownGap()
    {
        var slices = SliceCorpus();
        Assert.NotEmpty(slices);

        var counts = slices.GroupBy(s => s.Outcome).ToDictionary(g => g.Key, g => g.Count());
        int Count(SliceOutcome outcome) => counts.GetValueOrDefault(outcome);
        double Rate(SliceOutcome outcome) => 100.0 * Count(outcome) / slices.Count;

        var summary = string.Join(
            "\n",
            counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value,6}  {100.0 * kv.Value / slices.Count,5:F2}%  {kv.Key}"));

        // Measured at 18.21%, 1.67%, and 0.30% over this corpus.
        Assert.True(Rate(SliceOutcome.TypeHeader) < 22.0, $"type-header shapes grew:\n{summary}");
        Assert.True(Rate(SliceOutcome.UnderCapture) < 3.0, $"under-capture grew:\n{summary}\n\n{Report(slices.Where(s => s.Outcome == SliceOutcome.UnderCapture))}");
        Assert.True(Rate(SliceOutcome.Malformed) < 1.5, $"malformed slices grew:\n{summary}\n\n{Report(slices.Where(s => s.Outcome == SliceOutcome.Malformed))}");
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
