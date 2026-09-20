using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpText.Tests;

public partial class DeclarationIndexTests
{
    private const string StableConditionalCorpusFile = "ConditionalCorpusFixture.cs";

    [Fact]
    public void EveryDeclarationRoslynReports_IsReportedIdenticallyByTheIndex()
    {
        var corpus = Corpus();
        var mismatches = new List<string>();
        int files = 0;
        int declarations = 0;

        foreach (var file in corpus)
        {
            var lines = File.ReadAllLines(file);
            var expected = RoslynDeclarations(lines);
            if (expected is null)
                continue;

            files++;
            declarations += expected.Count;

            var actual = DeclarationIndex.Build(lines).Declarations
                .Where(s => s.SpanKnown)
                .Select(Format)
                .ToList();

            var diff = Diff(expected.Select(Format).ToList(), actual);
            if (diff.Length > 0)
                mismatches.Add($"{file}\n{diff}");
        }

        // Non-vacuity. The floors catch a corpus that collapsed; the skip ceiling catches the
        // subtler failure, where the oracle starts declining files — a wrong parse option, a
        // language feature it stops recognizing — and the gate passes by comparing almost nothing.
        Assert.True(files >= 100, $"corpus too small to gate anything: {files} files");
        Assert.True(declarations >= 2500, $"corpus too small to gate anything: {declarations} declarations");
        Assert.True(
            corpus.Count - files <= corpus.Count / 10,
            $"the oracle declined {corpus.Count - files} of {corpus.Count} files");

        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} of {files} files disagree with Roslyn "
                + $"({declarations} declarations compared):\n\n"
                + string.Join("\n\n", mismatches.Take(8)));
    }

    /// <summary>
    /// <para>
    /// The differential above compares only files with no conditional directive, and it declines
    /// the rest by construction: Roslyn with no symbols defined discards the disabled branches
    /// while the index, being lexical, indexes them, so the two lists cannot be equal. That
    /// exemption is what let a conditional cost every later declaration in the file, to end of
    /// file, with the suite green -- the gate could not see the collateral after the
    /// <c>#endif</c> because it was not looking at those files at all.
    /// </para>
    /// <para>
    /// This is the same differential restricted to a population where the two <em>are</em>
    /// comparable, and weakened from equality to containment. A declaration lying wholly outside
    /// every conditional region is present in every build, so Roslyn reports it and the index
    /// must report it identically, with a span it vouches for. Extra index rows -- the disabled
    /// branches' declarations -- are expected and are not failures, which is exactly why this is
    /// a subset comparison and the other one is not.
    /// </para>
    /// </summary>
    [Fact]
    public void InAConditionalFile_EveryDeclarationOutsideTheConditionals_IsStillVouchedFor()
    {
        var mismatches = new List<string>();
        int files = 0;
        int compared = 0;
        int inside = 0;
        int containing = 0;
        int containingVouched = 0;
        bool stableFixtureCovered = false;

        foreach (var file in ConditionalCorpus())
        {
            var lines = File.ReadAllLines(file);
            var expected = RoslynDeclarations(lines, requireNoConditionals: false, out var regions);
            if (expected is null || regions.Count == 0)
                continue;

            files++;
            stableFixtureCovered |= Path.GetFileName(file).Equals(
                StableConditionalCorpusFile,
                StringComparison.Ordinal);

            var actual = DeclarationIndex.Build(lines).Declarations.ToList();

            foreach (var e in expected)
            {
                // Roslyn reports a declaration once, at the lines it occupies in this build. A
                // declaration whose own header sits in conditional text is not that stable -- it
                // may be one branch's spelling, or it may straddle a directive -- so it is skipped.
                //
                // A declaration that merely CONTAINS a group is a different case, and skipping it
                // was a hole: those are exactly the rows whose closing line this PR recovers, and
                // a mutation that reported their EndLine one line short passed the gate
                // (adversarial review round 5, GPT-5.6 Sol). They are compared, but the index is
                // allowed to refuse them -- an unbalanced group in the corpus is a legitimate
                // refusal -- so the claim is that a row it DOES vouch for is right.
                bool startsInside = regions.Any(r =>
                    (e.TriviaStartLine >= r.Start && e.TriviaStartLine <= r.End)
                    || (e.SignatureStartLine >= r.Start && e.SignatureStartLine <= r.End));

                if (startsInside)
                {
                    inside++;
                    continue;
                }

                bool containsRegion = regions.Any(r => e.TriviaStartLine <= r.End && r.Start <= e.EndLine);

                compared++;

                var match = actual.FirstOrDefault(a =>
                    a.Kind == e.Kind && a.Name == e.Name && a.SignatureStartLine == e.SignatureStartLine);

                if (match is null)
                {
                    mismatches.Add($"{Path.GetFileName(file)}: {Format(e)} is missing from the index");
                    continue;
                }

                if (containsRegion)
                {
                    containing++;
                    if (!match.SpanKnown)
                        continue;

                    containingVouched++;
                    if (Format(match) != Format(e))
                        mismatches.Add($"{Path.GetFileName(file)}: expected {Format(e)}, got {Format(match)}");

                    continue;
                }

                if (!match.SpanKnown)
                    mismatches.Add($"{Path.GetFileName(file)}: {Format(e)} is present but not vouched for");
                else if (Format(match) != Format(e))
                    mismatches.Add($"{Path.GetFileName(file)}: expected {Format(e)}, got {Format(match)}");
            }
        }

        // Non-vacuity, and it is load-bearing twice over. The owned fixture keeps this gate from
        // depending only on incidental directives elsewhere in the repository. The population
        // floors prevent that fixture from replacing the broad real-source corpus, which would
        // make a green result much weaker. The inside floor asserts the population this test is
        // deliberately not comparing is non-empty too, so quietly widening the skip cannot pass.
        Assert.True(stableFixtureCovered, $"{StableConditionalCorpusFile} was not compared");
        Assert.True(files >= 5, $"no conditional corpus to gate anything: {files} files");
        Assert.True(compared >= 300, $"conditional corpus too small to gate anything: {compared} declarations");
        Assert.True(inside > 0, $"the skip is vacuous: no declaration touched a conditional region");
        Assert.True(
            containingVouched > 0,
            $"no declaration that CONTAINS a conditional group was vouched for, so the recovered "
                + $"closing spans are not gated here ({containing} containing, {containingVouched} vouched)");

        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} declarations outside a conditional region are not reported "
                + $"({compared} compared across {files} conditional files, {inside} skipped as conditional):\n\n"
                + string.Join("\n", mismatches.Take(12)));
    }

    /// <summary>
    /// The reason the index exists: the declaration, not just the body. A member's slice has to
    /// start at its documentation comment, which sits above the first sequence point and so is
    /// invisible to the PDB.
    /// </summary>
    [Fact]
    public void ADeclarationsTriviaStart_MatchesRoslynsLeadingTrivia()
    {
        var mismatches = new List<string>();
        int compared = 0;

        foreach (var file in Corpus())
        {
            var lines = File.ReadAllLines(file);
            var expected = RoslynDeclarations(lines);
            if (expected is null)
                continue;

            var actual = DeclarationIndex.Build(lines).Declarations.Where(s => s.SpanKnown).ToList();
            foreach (var e in expected)
            {
                var match = actual.FirstOrDefault(a =>
                    a.Kind == e.Kind && a.Name == e.Name && a.SignatureStartLine == e.SignatureStartLine);
                if (match is null)
                    continue;

                compared++;
                if (match.TriviaStartLine != e.TriviaStartLine)
                    mismatches.Add(
                        $"{Path.GetFileName(file)} {e.Kind} {e.Name} @{e.SignatureStartLine}: "
                            + $"expected trivia {e.TriviaStartLine}, got {match.TriviaStartLine}");
            }
        }

        Assert.True(compared >= 1000, $"corpus too small to gate anything: {compared} declarations");
        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} of {compared} declarations start their trivia elsewhere:\n"
                + string.Join("\n", mismatches.Take(20)));
    }

    /// <summary>
    /// The index is only usable as a containment substrate if its rows genuinely nest: a child's
    /// span inside its parent's, and no two siblings overlapping.
    /// </summary>
    [Fact]
    public void RowsNestWithinTheirParentAndNeverOverlapASibling()
    {
        var offenders = new List<string>();
        int checkedRows = 0;

        foreach (var file in Corpus())
        {
            var spans = DeclarationIndex.Build(File.ReadAllLines(file)).Declarations;
            for (int i = 0; i < spans.Length; i++)
            {
                var s = spans[i];
                if (!s.SpanKnown)
                    continue;

                checkedRows++;
                if (s.TriviaStartLine > s.SignatureStartLine
                    || s.SignatureStartLine > s.SignatureEndLine
                    || s.SignatureEndLine > s.EndLine)
                    offenders.Add($"{Path.GetFileName(file)} {s.Kind} {s.Name}: line order {s.TriviaStartLine}/{s.SignatureStartLine}/{s.SignatureEndLine}/{s.EndLine}");

                if (s.ParentIndex >= 0)
                {
                    var p = spans[s.ParentIndex];
                    if (p.SpanKnown && (s.SignatureStartLine < p.SignatureStartLine || s.EndLine > p.EndLine))
                        offenders.Add($"{Path.GetFileName(file)} {s.Kind} {s.Name} escapes parent {p.Kind} {p.Name}");
                    if (s.Depth != p.Depth + 1)
                        offenders.Add($"{Path.GetFileName(file)} {s.Kind} {s.Name}: depth {s.Depth} under parent depth {p.Depth}");
                }
                else if (s.Depth != 0)
                {
                    offenders.Add($"{Path.GetFileName(file)} {s.Kind} {s.Name}: depth {s.Depth} with no parent");
                }
            }
        }

        Assert.True(checkedRows >= 1000, $"corpus too small to gate anything: {checkedRows} rows");
        Assert.True(offenders.Count == 0, string.Join("\n", offenders.Take(20)));
    }

    /// <summary>
    /// Containment runs from the signature, not the body, so a line selects the declaration a
    /// reader would say it belongs to. Line 8 is <c>Deep</c>'s signature: it is inside
    /// <c>Inner</c>'s body but not inside <c>Deep</c>'s, so body-only containment answered
    /// <c>Inner</c> — the enclosing type — for a line that plainly declares a method.
    /// </summary>
    [Fact]
    public void FindByLine_ReturnsTheInnermostDeclarationCoveringThatLine()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class Outer
            {
                public void Before() { }

                public class Inner
                {
                    public int Deep()
                    {
                        return 1;
                    }
                }
            }
            """);

        // Inside the body.
        var body = index.FindByLine(10);
        Assert.NotNull(body);
        Assert.Equal(DeclarationKind.Method, body.Kind);
        Assert.Equal("Deep", body.Name);

        // On the signature. Body-only containment answered "Inner" here.
        var signature = index.FindByLine(8);
        Assert.NotNull(signature);
        Assert.Equal(DeclarationKind.Method, signature.Kind);
        Assert.Equal("Deep", signature.Name);

        // A line belonging to no member still resolves to the innermost type that owns it, so
        // widening containment did not make every line answer with a member.
        var inner = index.FindByLine(7);
        Assert.NotNull(inner);
        Assert.Equal(DeclarationKind.Class, inner.Kind);
        Assert.Equal("Inner", inner.Name);

        var before = index.FindByLine(4);
        Assert.NotNull(before);
        Assert.Equal("Before", before.Name);
    }

    /// <summary>
    /// The reason containment starts at the signature. A constructor's first sequence point can
    /// land on its declaration line rather than inside its body — the compiler attributes
    /// parameter capture and a base initializer there — so body-only containment falls through to
    /// the enclosing type and names the type header as the constructor's source.
    /// <para>
    /// Both halves matter. The constructor's own signature line must select the constructor, and
    /// the type's header line must still select the type — a selector that simply preferred the
    /// deepest row overlapping anything would satisfy the first and break the second.
    /// </para>
    /// </summary>
    [Fact]
    public void AConstructorsSignatureLine_SelectsTheConstructorNotTheType()
    {
        var index = DeclarationIndex.Build("""
            public class C
            {
                private readonly int _x;

                private C(
                    int x,
                    int y)
                {
                    _x = x + y;
                }
            }
            """);

        var ctor = Assert.Single(index.Declarations, d => d.Kind == DeclarationKind.Constructor);
        Assert.Equal(5, ctor.SignatureStartLine);
        Assert.Equal(8, ctor.BodyStartLine);

        Assert.Equal(DeclarationKind.Constructor, index.FindByLine(5)?.Kind);
        Assert.Equal(DeclarationKind.Constructor, index.FindByLine(6)?.Kind);
        Assert.Equal(DeclarationKind.Constructor, index.FindByLine(9)?.Kind);
        Assert.Equal(DeclarationKind.Class, index.FindByLine(1)?.Kind);

        // A field's line selects the field, which is what a static constructor synthesized from
        // field initializers reports its sequence point against.
        Assert.Equal(DeclarationKind.Field, index.FindByLine(3)?.Kind);
    }
}
