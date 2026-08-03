using System.Collections.Immutable;
using System.Text;
using DotnetInspector.CSharpBodySlicer;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetInspector.CSharpBodySlicer.Tests;

/// <summary>
/// Gates <see cref="DeclarationIndex"/> against Roslyn over the real source of every PDB-bearing
/// assembly beside the test binary.
/// </summary>
/// <remarks>
/// Roslyn is the independent oracle, exactly as it is for the parse-validity gate: the product
/// stays Roslyn-free, and a hand-written expectation table would only prove that the index agrees
/// with whatever the index happened to do when the table was written. The index is a lexical scan
/// plus declaration recognition — it is not a parser — so the claim gated here is deliberately the
/// one a lexical scan can own: for each declaration Roslyn reports, the index reports the same
/// kind, the same name, and the same first and last line.
/// </remarks>
public class DeclarationIndexTests
{
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

        foreach (var file in ConditionalCorpus())
        {
            var lines = File.ReadAllLines(file);
            var expected = RoslynDeclarations(lines, requireNoConditionals: false, out var regions);
            if (expected is null || regions.Count == 0)
                continue;

            files++;

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

        // Non-vacuity, and it is load-bearing twice over. The first two floors catch a corpus that
        // stopped containing conditional files at all, which would make this gate pass by
        // comparing nothing -- the precise way its predecessor failed. The third asserts the
        // population it is *not* comparing is non-empty too, so a change that quietly widened the
        // skip until every declaration fell inside a region could not pass here.
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

    [Fact]
    public void FindByBodyLine_ReturnsTheInnermostDeclarationCoveringThatLine()
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

        var deep = index.FindByBodyLine(9);
        Assert.NotNull(deep);
        Assert.Equal(DeclarationKind.Method, deep.Kind);
        Assert.Equal("Deep", deep.Name);

        var inner = index.FindByBodyLine(8);
        Assert.NotNull(inner);
        Assert.Equal(DeclarationKind.Class, inner.Kind);
        Assert.Equal("Inner", inner.Name);
    }

    /// <summary>
    /// The selector for members the PDB cannot reach. An interface method has no body, so it has no
    /// sequence point and no line range — a name is the only way in.
    /// </summary>
    [Fact]
    public void FindByName_ReachesADeclarationThatHasNoBody()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public interface IThing
            {
                /// <summary>Writes it.</summary>
                void WriteTo(int x);
            }
            """);

        var found = Assert.Single(index.FindByName(DeclarationKind.Method, "WriteTo"));
        Assert.Equal(5, found.SignatureStartLine);
        Assert.Equal(4, found.TriviaStartLine);
        Assert.False(found.HasBody);
    }

    [Fact]
    public void AFileScopedNamespace_EnclosesTheRestOfTheFileJustAsABlockNamespaceDoes()
    {
        var scoped = DeclarationIndex.Build("""
            namespace N;
            public class C { }
            """);
        var block = DeclarationIndex.Build("""
            namespace N
            {
                public class C { }
            }
            """);

        var a = Assert.Single(scoped.Declarations.Where(s => s.Kind == DeclarationKind.Class));
        var b = Assert.Single(block.Declarations.Where(s => s.Kind == DeclarationKind.Class));
        Assert.Equal(1, a.Depth);
        Assert.Equal(1, b.Depth);
        Assert.Equal("N", scoped.ParentOf(a)!.Name);
        Assert.Equal("N", block.ParentOf(b)!.Name);
    }

    /// <summary>
    /// Metadata sees three fields, so the index owes three rows. They share one span because they
    /// share one declaration.
    /// </summary>
    [Fact]
    public void EachDeclaratorOfAMultiNameFieldGetsItsOwnRow()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
                public int A, B, C2;
                public const int P = 1, Q = 2;
                public System.Collections.Generic.Dictionary<string, int> Map = new();
            }
            """);

        Assert.Equal(
            ["A", "B", "C2", "P", "Q", "Map"],
            index.Declarations.Where(s => s.Kind == DeclarationKind.Field).Select(s => s.Name));
    }

    /// <summary>
    /// An expression-bodied property and a field initialized to a lambda both spell "=&gt;", and
    /// the difference is which one the header was cut at.
    /// </summary>
    [Fact]
    public void AnArrowIsAnExpressionBodyOnlyWhenItIsTheHeadersOwn()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
                public int P => 1;
                public System.Func<int, int> F = x => x;
                public T G<T>(T x) where T : new() => x;
            }
            """);

        Assert.Equal(DeclarationKind.Property, Assert.Single(index.FindByName(DeclarationKind.Property, "P")).Kind);
        var f = Assert.Single(index.FindByName(DeclarationKind.Field, "F"));
        Assert.False(f.HasBody);
        Assert.True(Assert.Single(index.FindByName(DeclarationKind.Method, "G")).HasBody);
    }

    /// <summary>
    /// A generic type argument list carries commas of its own, and they are not declarator
    /// boundaries. Two arguments happen to survive a lookahead-only rule; three do not, because
    /// the first comma is then followed by a name and another comma — the exact shape a declarator
    /// list has.
    /// </summary>
    [Fact]
    public void CommasInsideATypeArgumentList_AreNotDeclaratorBoundaries()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
                public System.Action<string, int, float> A;
                public System.Func<int, int, int, int, string> B, B2;
                public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>> D;
            }
            """);

        Assert.Equal(
            ["A", "B", "B2", "D"],
            index.Declarations.Where(d => d.Kind == DeclarationKind.Field).Select(d => d.Name));
    }

    /// <summary>
    /// A verbatim identifier is a name that merely spells a keyword. Reading it as the keyword
    /// turns a field into a type declaration and a parameter into a delegate.
    /// </summary>
    [Fact]
    public void AnIdentifierThatSpellsAKeyword_IsNotThatKeyword()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
                public int @class;
                public int @where => 1;
                public void M(int @delegate) { }
                public void N2(int @this) { }
                public int @event;
            }
            """);

        Assert.Equal(["class", "event"], index.Declarations.Where(d => d.Kind == DeclarationKind.Field).Select(d => d.Name));
        Assert.Equal(["where"], index.Declarations.Where(d => d.Kind == DeclarationKind.Property).Select(d => d.Name));
        Assert.Equal(["M", "N2"], index.Declarations.Where(d => d.Kind == DeclarationKind.Method).Select(d => d.Name));
        Assert.Empty(index.Declarations.Where(d => d.Kind is DeclarationKind.Delegate or DeclarationKind.Event));
        Assert.Single(index.Declarations.Where(d => d.IsType));
    }

    /// <summary>
    /// Two declarations can be spelled identically — a partial method's defining and implementing
    /// halves share a name and differ only in span. The differential compares multisets, so a row
    /// emitted twice or dropped once is a failure; this is the fixture that has cardinality to
    /// lose.
    /// </summary>
    [Fact]
    public void TwoDeclarationsSpelledAlike_AreBothReported()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public partial class C
            {
                public partial void M();
                public partial void M() { }
            }
            """);

        var found = index.FindByName(DeclarationKind.Method, "M");
        Assert.Equal(2, found.Length);
        Assert.False(found[0].HasBody);
        Assert.True(found[1].HasBody);
    }

    /// <summary>
    /// The oracle's decline rule, gated directly. The corpus currently contains no file with a
    /// conditional directive, so nothing else exercises either arm: a rule that declined every
    /// file, or none, would look identical from the corpus. It declines on a real directive and
    /// only on a real directive — <c>#if</c> spelled in a comment, a string, or a
    /// <c>#region</c>/<c>#pragma</c>/<c>#nullable</c> directive is not conditional compilation.
    /// </summary>
    [Fact]
    public void TheOracleDeclines_OnAConditionalDirectiveAndOnlyOnOne()
    {
        Assert.Null(RoslynDeclarations(["#if NET", "class A { }", "#endif"]));
        Assert.Null(RoslynDeclarations(["#if NET", "class A { }", "#else", "class B { }", "#endif"]));

        Assert.NotNull(RoslynDeclarations(["// mentions #if and #else", "class A { }"]));
        Assert.NotNull(RoslynDeclarations(["class A { const string S = \"#if\"; }"]));
        Assert.NotNull(RoslynDeclarations(["#nullable enable", "#region R", "#pragma warning disable", "class A { }", "#endregion"]));
    }

    /// <summary>
    /// An extension block is a scope, not a declaration, in both its plain and its generic form.
    /// Getting this wrong does not cost one bad row: the block is indexed as a method, and every
    /// member inside it is then rejected for sitting in a method rather than a type, so the
    /// extension members vanish from the index entirely.
    /// </summary>
    [Fact]
    public void AGenericExtensionBlock_IsTransparentJustLikeAPlainOne()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public static class C
            {
                extension(string receiver)
                {
                    public int Plain => 1;
                }

                extension<T>(System.Collections.Generic.IEnumerable<T> source)
                {
                    public System.Collections.Generic.IEnumerable<T> Page(int n) => source;
                    public bool IsEmpty => true;
                }
            }
            """);

        Assert.Empty(index.Declarations.Where(d => d.Name == "extension"));
        Assert.Equal(
            ["Plain", "Page", "IsEmpty"],
            index.Declarations.Where(d => d.Kind is DeclarationKind.Method or DeclarationKind.Property)
                .Select(d => d.Name));

        // Every extension member's parent is the enclosing class, not a row for the block.
        var owner = index.Declarations.Single(d => d.Kind == DeclarationKind.Class);
        Assert.All(
            index.Declarations.Where(d => d.Kind is DeclarationKind.Method or DeclarationKind.Property),
            d => Assert.Equal(owner, index.ParentOf(d)));
    }

    /// <summary>
    /// A checked operator carries <c>checked</c> in its name, because it is a distinct metadata
    /// member: <c>operator checked +</c> emits <c>op_CheckedAddition</c> and may be declared
    /// alongside <c>op_Addition</c> in the same type. The oracle derives the same name
    /// independently, from Roslyn's <c>CheckedKeyword</c>.
    /// </summary>
    [Fact]
    public void ACheckedOperator_IsNamedForItsSymbolAlone()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class D
            {
                public static D operator +(D x, D y) => x;
                public static D operator checked +(D x, D y) => x;
                public static explicit operator int(D d) => 0;
                public static explicit operator checked int(D d) => 0;
            }
            """);

        Assert.Equal(
            ["operator +", "operator checked +", "operator explicit", "operator checked explicit"],
            index.Declarations.Where(d => d.Kind == DeclarationKind.Method).Select(d => d.Name));
    }

    /// <summary>
    /// A generic type argument list in an <em>initializer</em> also carries commas, and the angle
    /// counter cannot run there because a relational <c>&lt;</c> never closes. The speculative
    /// match must skip the real type argument list without swallowing a relational comparison —
    /// the last two fields here are the negative case, and losing them would be as wrong as
    /// inventing a row from the first.
    /// </summary>
    [Fact]
    public void CommasInsideAGenericInitializer_AreNotDeclaratorBoundaries()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            using System;
            public class C
            {
                public Action A = new Action<int, int, int>(null), B = null;
                public object F = Foo<int, string>.Bar, G = null;
                public bool X = 1 < 2, Y = 3 > 2;
                public bool P = A2 < B2, Q = C2 > D2;
            }
            """);

        Assert.Equal(
            ["A", "B", "F", "G", "X", "Y", "P", "Q"],
            index.Declarations.Where(d => d.Kind == DeclarationKind.Field).Select(d => d.Name));
    }

    /// <summary>
    /// A file-scoped namespace <em>scopes</em> the rest of the file, but its declaration ends where
    /// its last member ends. Trailing trivia belongs to the file, not to the namespace — Roslyn's
    /// span says so, and no corpus file happens to have any, so without this the gate agreed only
    /// by luck and one added trailing comment anywhere in the repository would have turned it red.
    /// </summary>
    [Fact]
    public void AFileScopedNamespace_EndsAtItsLastMemberNotAtTheLastLine()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            class C { }
            // trailing comment


            """);

        var ns = index.Declarations.Single(d => d.Kind == DeclarationKind.Namespace);
        Assert.Equal(2, ns.EndLine);
        Assert.True(ns.SpanKnown);

        var empty = DeclarationIndex.Build("namespace N;");
        Assert.Equal(1, empty.Declarations.Single().EndLine);
    }

    /// <summary>
    /// A file-scoped namespace's span reaches every later declaration, so it cannot be better known
    /// than they are. Its EOF-closing special case used to skip the unknown-span marking entirely,
    /// which reported a measured span for a file whose brace structure the scan could not follow —
    /// exactly the guess the type exists to avoid.
    /// </summary>
    [Fact]
    public void AFileScopedNamespaceOverAConditionalRegion_ReportsAnUnknownSpan()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            #if FEATURE
            class A { }
            #else
            class B { }
            #endif
            """);

        Assert.All(index.Declarations, d => Assert.False(d.SpanKnown));
    }

    /// <summary>
    /// A file-scoped namespace's end is a maximum over the rows it encloses, so it is only as good
    /// as the worst of them. A member whose brace never closes reports the last line as a guess;
    /// the namespace used to adopt that guess and still call its own span measured, because the
    /// unknown-span marking keyed on lost lexical depth and an unclosed brace never loses it.
    /// </summary>
    [Fact]
    public void AFileScopedNamespaceOverAnUnclosedMember_ReportsAnUnknownSpan()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            class A {
            // trailing comment
            """);

        Assert.All(index.Declarations, d => Assert.False(d.SpanKnown));
    }

    /// <summary>
    /// The punctuators <see cref="DeclarationIndexBuilder"/> accepts inside a speculatively matched
    /// type argument list are load-bearing: drop the array, tuple, pointer, or qualified-name
    /// entries and each of these initializers stops matching, so its commas split declarators and
    /// invent fields named for a type. Every entry in that allow list appears below.
    /// </summary>
    [Fact]
    public void ATypeArgumentListInAnInitializer_MayContainAnyTypeSyntax()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            using System;
            public unsafe class C
            {
                public object a = new Func<int[], string, int>(null), b = null;
                public object c = new Func<(int, string), string, int>(null), d = null;
                public object e = new Func<int*[], string, int>(null), f = null;
                public object g = new Func<System.Text.Rune, string, int>(null), h = null;
                public object i = new Func<global::System.Guid, string, int>(null), j = null;
                public object k = new Func<int?, string, int>(null), l = null;
                public object m = new Func<Func<int, int>, string, int>(null), n = null;
            }
            """);

        Assert.Equal(
            ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n"],
            index.Declarations.Where(d => d.Kind == DeclarationKind.Field).Select(d => d.Name));
    }

    /// <summary>
    /// A speculative type argument list must leave its own groups balanced. <c>a &lt; b(name: c &gt; d)</c>
    /// reaches a <c>&gt;</c> with a <c>(</c> still open; accepting it skipped the <c>(</c> and left the
    /// matching <c>)</c> to drive the caller's group depth negative, after which no later comma could
    /// separate a declarator and every trailing declarator vanished from the index. All three shapes
    /// below compile.
    /// </summary>
    [Fact]
    public void ARelationalComparisonThatOpensAGroup_DoesNotMatchATypeArgumentList()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                static int x = 1, c = 2, d = 3;
                static int b(bool name) => 1;
                static int p = 1, q = 2, r = 3, s = 4;
                static int m(bool v) => 0;
                static int[] arr = new int[9];

                static bool a = x < b(name: c > d), f = true;
                static int g = x < p ? q : m(r > s), h = 1;
                static bool k = x < arr[c > d ? 1 : 0], n = false;
            }
            """);

        var fields = index.Declarations.Where(d => d.Kind == DeclarationKind.Field).Select(d => d.Name);
        Assert.Equal(
            ["x", "c", "d", "p", "q", "r", "s", "arr", "a", "f", "g", "h", "k", "n"],
            fields);
    }

    /// <summary>
    /// A block comment yields one scanner token per line it covers, so the token's own line is not
    /// where the comment began. Trivia attribution used the token's line, which put the second line
    /// of <c>int A; /* x</c> after the previous declaration's terminator and made it the *next*
    /// declaration's trivia start — a slice from there would begin inside the comment, past its
    /// <c>/*</c>, and produce source that does not compile.
    /// <para>
    /// Which token opens a comment cannot be read off the text: a continuation line may itself
    /// start with <c>//</c> or <c>/*</c>, and both shapes are below. Roslyn is the oracle here
    /// rather than a hand-written line number, because the whole question is what counts as
    /// leading trivia and that is Roslyn's answer to give. Every fixture compiles.
    /// </para>
    /// <para>
    /// Several fixtures exist only to make a sub-rule's misreading change an ANSWER rather than
    /// merely shift state, which is what a mutation can see: the last two put a comment after a
    /// block that must have closed, and the single-line <c>/* … */</c> before a further comment
    /// gates the opening test in both directions — treating <c>/*/</c> as closed, and treating a
    /// closed one-line block as still open.
    /// </para>
    /// </summary>
    [Fact]
    public void ABlockCommentSpanningATerminatorLine_TrailsTheDeclarationItStartedOn()
    {
        string[] fixtures =
        [
            "class C\n{\n    int A; /* trailing\n    comment */\n    int B;\n}",
            "class C\n{\n    int A;\n    /* leading\n    comment */\n    int B;\n}",
            "class C\n{\n    int A; // trailing\n    // leading\n    int B;\n}",
            "class C\n{\n    int A; /* one\n// still inside\n*/\n    int B;\n}",
            "class C\n{\n    int A; /* x\n/* y */\n    int B;\n}",
            "class C\n{\n    int A; /* a */ /* b\n c */\n    int B;\n}",
            "class C\n{\n    int A;\n    /*/ still open\n    */ int B;\n}",
            "class C\n{\n    int A; /* trailing\n    comment */\n    // leading B\n    int B;\n}",
            "class C\n{\n    int A; /*/\n    still inside */\n    int B;\n}",
            "class C\n{\n    /* single line */ int A;\n    /* next comment */\n    int B;\n}",
        ];

        foreach (var fixture in fixtures)
        {
            var lines = fixture.Split('\n');
            var expected = RoslynDeclarations(lines);
            Assert.NotNull(expected);

            var actual = DeclarationIndex.Build(lines).Declarations;
            Assert.Equal(
                expected.Select(d => $"{d.Kind} {d.Name} trivia={d.TriviaStartLine} sig={d.SignatureStartLine}"),
                actual.Select(d => $"{d.Kind} {d.Name} trivia={d.TriviaStartLine} sig={d.SignatureStartLine}"));
        }
    }

    /// <summary>
    /// An <c>assembly:</c> or <c>module:</c> attribute list belongs to the compilation unit, not to
    /// whatever declaration follows it. It was treated as leading trivia of that declaration, so a
    /// slice would open with an assembly attribute that has nothing to do with the member selected.
    /// Roslyn also puts a file header comment above such a list inside the list's own trivia, so a
    /// unit attribute has to clear what came before it rather than merely decline to extend it.
    /// <para>
    /// The close negatives are the point: <c>[Obsolete]</c>, <c>[type: Obsolete]</c> and
    /// <c>[return: ...]</c> are part of the declaration that follows and must still be kept. Roslyn
    /// is the oracle rather than hand-written line numbers. Every fixture compiles.
    /// </para>
    /// </summary>
    [Fact]
    public void ACompilationUnitAttribute_IsNotTheNextDeclarationsTrivia()
    {
        string[] fixtures =
        [
            "using System;\n[assembly: CLSCompliant(true)]\nclass A1 { }",
            "[module: System.CLSCompliant(true)]\nclass A2 { }",
            "// file header\nusing System;\n[assembly: System.Reflection.AssemblyMetadata(\"k\",\"v\")]\nclass A3 { }",
            "using System;\n[Obsolete]\nclass A4 { }",
            "using System;\n[type: Obsolete]\nclass A5 { }",
            "class A6\n{\n    [return: System.Diagnostics.CodeAnalysis.NotNull]\n    string M() => \"\";\n}",
            "using System;\n[assembly: System.Reflection.AssemblyDescription(\"d\")]\n[Obsolete]\nclass A7 { }",
            "using System;\n[assembly: System.Reflection.AssemblyProduct(\"p\")]\n\n[assembly: System.Reflection.AssemblyCompany(\"c\")]\nclass A8 { }",
            "using System;\nclass assemblyAttribute : Attribute { }\n[assembly]\nclass A9 { }",
            "using System;\nclass assemblyAttribute : Attribute { }\n[assembly()]\nclass A10 { }",
            "using System;\nclass moduleAttribute : Attribute { }\n[module, Obsolete]\nclass A11 { }",
        ];

        foreach (var fixture in fixtures)
        {
            var lines = fixture.Split('\n');
            var expected = RoslynDeclarations(lines);
            Assert.NotNull(expected);

            var actual = DeclarationIndex.Build(lines).Declarations;
            Assert.Equal(
                expected.Select(d => $"{d.Kind} {d.Name} trivia={d.TriviaStartLine} sig={d.SignatureStartLine}"),
                actual.Select(d => $"{d.Kind} {d.Name} trivia={d.TriviaStartLine} sig={d.SignatureStartLine}"));
        }
    }

    /// <summary>
    /// The corpus contains no <c>delegate</c> type and no destructor, so both classifications are
    /// gated here or not at all: with these fixtures absent, <c>DeclaresADelegate</c> and the
    /// <c>~</c> branch of <c>Classify</c> can each be deleted outright with the suite still green.
    /// The close negatives matter as much as the positives — a function pointer spells
    /// <c>delegate</c> too, and so does an anonymous method in a field initializer, and neither
    /// declares a type. The fixture compiles.
    /// </summary>
    [Fact]
    public void DelegatesFunctionPointersAndDestructors_AreClassifiedApart()
    {
        var index = DeclarationIndex.Build("""
            using System;

            namespace N;

            public delegate int Handler(int x, int y);

            public unsafe class C
            {
                public delegate int Nested(int x);
                public void M(delegate*<int, int> p) { }
                public Action<int> a = delegate (int x) { }, b = null;
                public Action nop = delegate { };
                ~C() { }
            }
            """);

        Assert.Equal(
            [
                (DeclarationKind.Namespace, "N"),
                (DeclarationKind.Delegate, "Handler"),
                (DeclarationKind.Class, "C"),
                (DeclarationKind.Delegate, "Nested"),
                (DeclarationKind.Method, "M"),
                (DeclarationKind.Field, "a"),
                (DeclarationKind.Field, "b"),
                (DeclarationKind.Field, "nop"),
                (DeclarationKind.Destructor, "~C"),
            ],
            index.Declarations.Select(d => (d.Kind, d.Name)));
    }

    /// <summary>
    /// An <c>extern alias</c> is not a declaration, and the corpus contains none. This gates the
    /// behavior, not the branch: mutating <c>Classify</c>'s <c>extern alias</c> skip leaves the
    /// suite green, because <c>Allowed</c> independently rejects the field the skip prevents — a
    /// file cannot put an <c>extern alias</c> inside a type. The construct is parse-valid on its
    /// own; resolving <c>LibA</c> would need an aliased reference, which nothing here consults.
    /// </summary>
    [Fact]
    public void AnExternAlias_IsNotADeclaration()
    {
        var index = DeclarationIndex.Build("""
            extern alias LibA;
            class C { }
            """);

        Assert.Equal([(DeclarationKind.Class, "C")], index.Declarations.Select(d => (d.Kind, d.Name)));
    }

    /// <summary>
    /// A local function is a declaration Roslyn recognizes and the index deliberately does not: it
    /// is not a member, and reporting one would let a body-line lookup return something that has no
    /// metadata counterpart. Nothing here recognizes a local function — the enclosing scope of a
    /// method body simply is not a type.
    /// </summary>
    [Fact]
    public void ALocalFunctionAndALambdaAreNotDeclarations()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
                public int M()
                {
                    int Helper(int x) => x + 1;
                    System.Func<int, int> f = y => y;
                    return Helper(1) + f(2);
                }
            }
            """);

        Assert.Equal(
            ["M"],
            index.Declarations.Where(s => s.Kind is DeclarationKind.Method or DeclarationKind.Field).Select(s => s.Name));
    }

    /// <summary>
    /// The scan cannot decide which branch of a conditional compiles, so a span it cannot vouch for
    /// must report unknown rather than a guess.
    /// </summary>
    [Fact]
    public void ASpanTheScanCannotVouchFor_ReportsUnknown()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
            #if FEATURE
                public void M() {
            #else
                public void M() {
            #endif
                }
            }
            """);

        Assert.Contains(index.Declarations, s => !s.SpanKnown);
    }

    /// <summary>
    /// A conditional group compiles exactly one branch, or none. If every branch returns to the
    /// brace depth the <c>#if</c> started at, the depth after the <c>#endif</c> is that depth
    /// whichever branch the compiler keeps — so declarations below the group are unaffected by it
    /// and their spans are knowable. Recovering that is #3668.
    /// <para>
    /// Before it, a conditional directive set the lexer's untracked flag and nothing cleared it, so
    /// the place was lost for the rest of the file rather than for the region the directive guards.
    /// On dotnet/runtime's libraries at revision <c>e614b717a9d</c> a conditional appears in 8.3% of
    /// files but cost 12.1% of declarations, because the loss ran to end of file.
    /// </para>
    /// <para>
    /// Rows are asserted through both emit paths. A bodiless row — a field, an interface method —
    /// takes a separate <c>SpanKnown</c> assignment in <c>EmitBodiless</c> from the one a
    /// body-bearing row takes, and a fixture of methods with bodies leaves that assignment ungated:
    /// hard-coding it to <see langword="true"/> passed the entire suite before this test named
    /// <c>Field</c> and <c>Bodiless</c>.
    /// </para>
    /// <para>
    /// What stays lost is asserted by
    /// <see cref="AnUnbalancedConditional_StillLosesEveryLaterRow"/> and
    /// <see cref="AConditionalInitializer_ReportsUnknownRatherThanOneBranchsEnd"/>. Brace balance
    /// makes a *following* span knowable; it says nothing about a span whose own terminator sits
    /// inside a branch.
    /// </para>
    /// </summary>
    [Fact]
    public void ABalancedConditional_CostsOnlyTheRowsInsideIt()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if DEBUG
                void Debug() { }
            #endif
                void Always() { }
                int Field;
            }
            interface I
            {
                void Bodiless();
            }
            """);

        // Named rather than counted: a row that vanished would otherwise pass an Assert.All, and
        // Field and Bodiless are the two that reach EmitBodiless.
        foreach (var name in new[] { "C", "Always", "Field", "I", "Bodiless" })
        {
            var row = Assert.Single(index.Declarations, s => s.Name == name);
            Assert.True(row.SpanKnown, $"'{name}' sits outside the group and should resolve");
        }

        // The row *inside* the branch is still withheld. Its text is indexed -- the index is
        // lexical and reports what is written -- but whether it compiles depends on a symbol the
        // index does not know, and #3672 is where that question is answered.
        var conditional = Assert.Single(index.Declarations, s => s.Name == "Debug");
        Assert.False(conditional.SpanKnown, "a row inside a branch is not known to compile");

        Assert.Equal("Always", index.FindByBodyLine(6)?.Name);

        // The same declarations without the directive resolve identically, so the group now costs
        // nothing outside itself.
        var plain = DeclarationIndex.Build("""
            class C
            {
                void Always() { }
                int Field;
            }
            interface I
            {
                void Bodiless();
            }
            """);

        Assert.All(plain.Declarations, s => Assert.True(s.SpanKnown));
        Assert.Equal("Always", plain.FindByBodyLine(3)?.Name);
    }

    /// <summary>
    /// The negative half, and the reason balance is measured per branch rather than over the group
    /// as scanned. A structural conditional — one that opens a brace in one branch and closes it in
    /// another, or declares a different signature per branch — leaves a depth after its
    /// <c>#endif</c> that really does depend on which branch compiles, so the loss must stand.
    /// These are 26.6% of the directive groups in dotnet/runtime's libraries and are what #3672
    /// addresses with the PDB; the remaining 0.8% are body-only and unbalanced, and are undecidable
    /// without knowing the symbol set.
    /// </summary>
    [Theory]
    // A brace opened in one branch and closed after the #endif.
    [InlineData("class C\n{\n#if NET8\n    void M() {\n#else\n    void M() {\n#endif\n    }\n    void After() { }\n}")]
    // A brace opened inside a body in one branch only.
    [InlineData("class C\n{\n    void M()\n    {\n#if DEBUG\n        if (x) {\n#endif\n        }\n    }\n    void After() { }\n}")]
    // Balance judged over the last branch too: the #else arm is the one that does not return.
    [InlineData("class C\n{\n#if A\n    void M() { }\n#else\n    void M() {\n#endif\n    }\n    void After() { }\n}")]
    // An unbalanced group followed by a balanced one. This is coverage of a real shape, NOT a gate
    // on the stickiness of the loss: an unbalanced group mangles the brace structure enough that
    // these rows are unknown for other reasons too, so the fixture passes whether or not a
    // balanced close clears the flag. Stickiness is gated by the two hidden-directive tests, which
    // set the flag inside a group that then closes balanced. Recorded because two successive
    // attempts to cite this test for stickiness were wrong (adversarial review round 3).
    [InlineData("class C\n{\n#if A\n    void M() {\n#else\n    void M() {\n#endif\n    }\n#if B\n    void N() { }\n#endif\n    void After() { }\n}")]
    public void AnUnbalancedConditional_StillLosesEveryLaterRow(string source)
    {
        var index = DeclarationIndex.Build(source.Split('\n'));

        // Asserted over every row rather than over "After" alone: an unbalanced group can mangle
        // the brace structure badly enough that the trailing declaration is never emitted as a row
        // at all, and a test naming it would then fail for the wrong reason. What must hold is that
        // nothing in the file claims a span the scan cannot vouch for.
        Assert.NotEmpty(index.Declarations);
        Assert.All(index.Declarations, s => Assert.False(s.SpanKnown));
    }

    /// <summary>
    /// <para>
    /// In preprocessor-disabled text the compiler does not lex code: <c>/*</c> opens no comment
    /// and a quote opens no string, but directives are still recognized and still nest. So a
    /// conditional directive sitting in what this lexical scan believes is a comment is real if
    /// the surrounding branch is disabled, and skipping it makes a later <c>#endif</c> close the
    /// wrong group.
    /// </para>
    /// <para>
    /// That is the one failure the index may not have: with <c>OUTER</c> undefined the class ends
    /// on the last line, but skipping the commented <c>#if</c> closes the outer group early, makes
    /// the brace on line 8 look live, and vouches for a span two lines short. Found by adversarial
    /// review; the rule refuses instead, and only while a group is open, since outside one the
    /// text cannot be disabled and <c>#if</c> in a comment is unambiguously prose.
    /// </para>
    /// </summary>
    [Fact]
    public void ADirectiveHiddenInsideACommentWithinAGroup_LosesTheDepth()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if OUTER
            /*
            #if INNER
            */
            #endif
            }
            #endif
            }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "a directive the scan could only skip by believing itself in a comment is ambiguous");

        // The same shape with no group open is not ambiguous at all, and must keep working --
        // this repository's own sources write "#if" inside comments and would otherwise be lost.
        var prose = DeclarationIndex.Build("""
            class C
            {
            /*
            #if INNER
            */
                void M() { }
            }
            """);

        Assert.True(Assert.Single(prose.Declarations, s => s.Name == "M").SpanKnown);
    }

    /// <summary>
    /// The comment fixture above exercises only the <c>InBlockComment</c> half of that rule. A
    /// directive hidden inside a <em>literal</em> is the same ambiguity -- in disabled text a quote
    /// opens no string either -- and deleting just that arm silently restores the wrong-span defect
    /// the rule exists to prevent (adversarial review round 2, GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void ADirectiveHiddenInsideALiteralWithinAGroup_LosesTheDepth()
    {
        var index = DeclarationIndex.Build(""""
            #if OUTER
            class A
            {
                string s = """
                    #if INNER
                    """;
            }
            #endif
            class C { }
            """");

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "a directive the scan could only skip by believing itself in a literal is ambiguous");
    }

    /// <summary>
    /// A skipped section is the one place the compiler processes <em>only</em> conditional
    /// directives: <c>#pragma</c>, <c>#region</c>, <c>#nullable</c> and <c>#line</c> are text
    /// whichever way the branch falls, and cannot open, close or renumber a group. Refusing them
    /// would poison a whole file for a directive that changes nothing, so the ambiguity rule tests
    /// which directive it found rather than that it found one (adversarial review round 2,
    /// Gemini 3.1 Pro).
    /// </summary>
    [Fact]
    public void ANonConditionalDirectiveHiddenInsideALiteral_KeepsTheDepth()
    {
        var index = DeclarationIndex.Build("""
            class C {
            #if DEBUG
                string s = @"
            #pragma warning disable
                ";
            #endif
                void After() { }
            }
            """);

        Assert.True(
            Assert.Single(index.Declarations, s => s.Name == "After").SpanKnown,
            "a #pragma inside a literal cannot change a group's structure in either build");
    }

    /// <summary>
    /// Trivia opens a row's span, and <c>SpanKnown</c> is a claim about the row's lines. A doc
    /// comment written inside a conditional group leads a declaration outside it with a
    /// branch-dependent start line: below, the row's trivia is line 2 in one build and line 4 in
    /// the other. Comment tokens never reach the pending-signature list, so neither
    /// <c>SpanKnown</c> expression consulted them (adversarial review round 2, GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void AConditionalDocComment_LosesTheRowItLeads()
    {
        var index = DeclarationIndex.Build("""
            #if X
            /// X docs
            #else
            /// Y docs
            #endif
            class C { }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "the row's trivia starts on a line only one build compiles");

        // A declaration with no scope of its own is emitted by a different site, with its own
        // SpanKnown expression. Covering only the braced one left that site ungated: the mutation
        // that drops the trivia term from it survived until this fixture existed.
        var bodiless = DeclarationIndex.Build("""
            class C
            {
            #if X
                /// X docs
            #else
                /// Y docs
            #endif
                int F;
            }
            """);

        Assert.False(
            Assert.Single(bodiless.Declarations, s => s.Name == "F").SpanKnown,
            "a field's trivia starts on a line only one build compiles");
    }

    /// <summary>
    /// An attribute list is leading trivia too, and its tokens do not reach the pending-signature
    /// list either, so it is the second door to the same defect as
    /// <see cref="AConditionalDocComment_LosesTheRowItLeads"/>.
    /// </summary>
    [Fact]
    public void AConditionalAttributeList_LosesTheRowItLeads()
    {
        var index = DeclarationIndex.Build("""
            #if X
            [Foo]
            #endif
            class C { }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "the row's trivia starts on a line only one build compiles");
    }

    /// <summary>
    /// <para>
    /// A file-scoped namespace is the one scope opener in C# that uses no brace, so neither the
    /// balance rule nor the opening-depth floor can see it. A group whose branches declare
    /// different file-scoped namespaces opens and closes at depth 0 and is judged balanced, while
    /// the enclosing declaration of every row below the <c>#endif</c> differs by build -- and the
    /// scope runs to end of file, so no <c>#endif</c> repairs it.
    /// </para>
    /// <para>
    /// Found independently by both reviewers in round 2. This is the third distinct way branches
    /// can agree on depth and disagree on meaning, after the comment-hidden directive and the
    /// opening-depth floor, which is why the rule is stated as a refusal rather than as a depth
    /// correction.
    /// </para>
    /// </summary>
    [Fact]
    public void ConditionalFileScopedNamespaces_LoseEveryRowBelowThem()
    {
        var index = DeclarationIndex.Build("""
            #if X
            namespace B;
            #else
            namespace C;
            #endif

            class D { }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "D").SpanKnown,
            "D is in namespace B in one build and C in the other");

        // An unconditional file-scoped namespace is the overwhelmingly common case and must keep
        // vouching for what it encloses, including across a balanced group below it.
        var plain = DeclarationIndex.Build("""
            namespace B;

            #if X
            class Inner { }
            #endif

            class D { }
            """);

        Assert.True(Assert.Single(plain.Declarations, s => s.Name == "D").SpanKnown);

        // One conditional namespace, no alternative. The refusal starts at the row after the
        // namespace, and with two namespaces the alternative occupies that row -- so a fixture
        // with two masks an off-by-one that this one catches, because here the refusal's first
        // row is D itself (adversarial review round 3, GPT-5.6 Sol).
        var single = DeclarationIndex.Build("""
            #if X
            namespace B;
            #endif

            class D { }
            """);

        Assert.False(
            Assert.Single(single.Declarations, s => s.Name == "D").SpanKnown,
            "D is in namespace B in one build and at file scope in the other");
    }

    /// <summary>
    /// An "assembly:" or "module:" attribute list belongs to the compilation unit, so it ends the
    /// trivia run above it rather than carrying it down: Roslyn reports the next declaration's
    /// leading trivia as starting after such a list. The knownness of the trivia it consumed must
    /// be dropped with it, or a conditional comment above a unit attribute would refuse a
    /// declaration whose own trivia begins below it. This is the one path that resets trivia
    /// knownness rather than setting it, and nothing else reaches it (adversarial review round 3,
    /// GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void AUnitAttributeAfterConditionalTrivia_StillVouchesForWhatFollows()
    {
        var index = DeclarationIndex.Build("""
            #if X
            /// conditional assembly docs
            #endif
            [assembly: System.CLSCompliant(true)]

            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.True(c.SpanKnown, "C's own trivia starts below the unit attribute");
        Assert.Equal(6, c.TriviaStartLine);
    }

    /// <summary>
    /// <para>
    /// An attribute list inside a conditional group is reported in <c>AttributeLists</c> even
    /// though only one build compiles it. When an unconditional list comes first the row's lines
    /// do not move -- trivia still starts at the first list, and the conditional one falls inside
    /// the range -- so the line-based rule alone vouches for the row while its list set is
    /// build-dependent. Unlike a comment, a list is not merely a line inside the range: it is a
    /// claim about what is applied to the declaration, so knownness is intersected over every
    /// list rather than taken from the one that opened the trivia.
    /// </para>
    /// <para>
    /// The contrasting comment case is deliberately <em>not</em> refused, and
    /// <see cref="AnUnconditionalCommentAboveAConditionalOne_StillVouchesForTheRow"/> holds that
    /// line (adversarial review round 3, Gemini 3.1 Pro).
    /// </para>
    /// </summary>
    [Fact]
    public void AConditionalAttributeListAfterAnUnconditionalOne_LosesTheRow()
    {
        var index = DeclarationIndex.Build("""
            [Attr1]
            #if X
            [Attr2]
            #endif
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(1, c.TriviaStartLine);
        Assert.False(c.SpanKnown, "only one build applies Attr2, and the row reports it either way");
    }

    /// <summary>
    /// The comment counterpart of
    /// <see cref="AConditionalAttributeListAfterAnUnconditionalOne_LosesTheRow"/>, and the reason
    /// the two are treated differently. A conditional comment below an unconditional one changes
    /// no line this row reports: trivia still starts at line 1, the row still ends at line 5, and
    /// the conditional comment's lines already fall inside that range. <c>SpanKnown</c> is a claim
    /// about the row's lines, so refusing here would cost recall for nothing. Verified by
    /// rendering both builds with line numbers preserved: every reported line is identical
    /// (adversarial review round 3, reported by Gemini 3.1 Pro as a defect and dismissed by
    /// measurement).
    /// </summary>
    [Fact]
    public void AnUnconditionalCommentAboveAConditionalOne_StillVouchesForTheRow()
    {
        var index = DeclarationIndex.Build("""
            // comment 1
            #if X
            // comment 2
            #endif
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(1, c.TriviaStartLine);
        Assert.Equal(5, c.EndLine);
        Assert.True(c.SpanKnown, "both builds report the same first and last line for this row");
    }

    /// <summary>
    /// <para>
    /// An attribute list can CROSS a conditional group, and the tokens inside the group can decide
    /// whether the list binds to this declaration at all. With <c>X</c> the list is a
    /// compilation-unit attribute and <c>C</c> starts on line 6 with no attributes; without
    /// <c>X</c> the same list is <c>C</c>'s own and <c>C</c> starts on line 1 (confirmed against
    /// Roslyn in both symbol configurations). Knownness sampled at the <c>[</c> found that token
    /// outside the group and vouched for the first answer.
    /// </para>
    /// <para>
    /// So knownness is accumulated over every token of the list, not just its opener
    /// (adversarial review round 4, GPT-5.6 Terra).
    /// </para>
    /// </summary>
    [Fact]
    public void AConditionalAttributeTarget_LosesTheRowBelowIt()
    {
        var index = DeclarationIndex.Build("""
            [
            #if X
            assembly:
            #endif
            System.CLSCompliant(true)]
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.False(c.SpanKnown, "the target decides whether this list is C's own trivia");
    }

    /// <summary>
    /// The bodiless-emit counterpart of
    /// <see cref="AConditionalAttributeTarget_LosesTheRowBelowIt"/>. There are two
    /// <c>SpanKnown</c> expressions, and a fixture written with <c>class C { }</c> reaches only
    /// the braced one -- which is how a round-2 fix shipped with the bodiless site ungated. A
    /// field is emitted through the other.
    /// </summary>
    [Fact]
    public void AConditionalAttributeTarget_LosesABodilessRowBelowIt()
    {
        var index = DeclarationIndex.Build("""
            class Outer {
            [
            #if X
            field:
            #endif
            System.Obsolete]
            int F;
            }
            """);

        var f = Assert.Single(index.Declarations, s => s.Name == "F");
        Assert.False(f.SpanKnown, "the bodiless emit site must consult the same knownness");
    }

    /// <summary>
    /// A literal inside a conditional group inside an attribute list. This is the negative that
    /// bounds the accumulation: a broader placement that also consumed comment and literal tokens
    /// refused this row, and refusing it costs recall for nothing, because the list's ends are
    /// punctuators and its target is a word, so the literal changes no line this row reports
    /// (adversarial review round 4; the broad placement was written first and this fixture is
    /// what falsified it).
    /// </summary>
    [Fact]
    public void AConditionalLiteralInsideAnAttributeList_StillVouchesForTheRowBelowIt()
    {
        var index = DeclarationIndex.Build("""
            [System.Obsolete(
            #if X
            "a"
            #else
            "b"
            #endif
            )]
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(1, c.TriviaStartLine);
        Assert.Equal(8, c.EndLine);
        Assert.True(c.SpanKnown, "both builds report the same first and last line for this row");
    }

    /// <summary>
    /// The negative that bounds the three above. An unconditional compilation-unit attribute still
    /// resets the trivia and still vouches for what follows, so the round-4 rule refuses crossing
    /// lists rather than attribute lists in conditional files generally.
    /// </summary>
    [Fact]
    public void AnUnconditionalUnitAttributeInAConditionalFile_StillVouchesForWhatFollows()
    {
        var index = DeclarationIndex.Build("""
            #if X
            #endif
            [assembly: System.CLSCompliant(true)]
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(4, c.TriviaStartLine);
        Assert.True(c.SpanKnown, "nothing about this list crosses the group");
    }

    /// <summary>
    /// A directive name is an identifier, so <c>#endif_foo</c> spells <c>endif_foo</c> and is not
    /// the <c>#endif</c> directive: Roslyn reports CS1024 and CS1027 and leaves the group open in
    /// every symbol configuration. Reading it as an <c>#endif</c> closed the group, recovered the
    /// depth, and vouched for what followed. <c>char.IsLetterOrDigit</c> misses underscore, which
    /// is the whole gap -- <c>#endif-</c> and <c>#endif//note</c> are recognized by Roslyn and are
    /// still recognized here (adversarial review round 3, Gemini 3.1 Pro). Round 5 added the rest
    /// of what C# allows to continue an identifier: a combining mark (U+0301), connector
    /// punctuation (U+203F) and a format character (U+200C) all behave exactly like
    /// <c>_foo</c> for Roslyn, and letters/digits/underscore alone missed all three
    /// (adversarial review round 5, GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void ADirectiveNameRunningIntoAnIdentifier_DoesNotCloseTheGroup()
    {
        // Every suffix here was checked against Roslyn: the first four report CS1024 and CS1027
        // in every symbol configuration, and the last two are accepted.
        foreach (var open in (string[])["#endif_foo", "#endif\u0301", "#endif\u203F", "#endif\u200C"])
        {
            var index = DeclarationIndex.Build($"#if X\nclass C {{ }}\n{open}\nclass D {{ }}");
            Assert.False(
                Assert.Single(index.Declarations, s => s.Name == "D").SpanKnown,
                $"'{open}' is not an #endif, so the group is still open");
        }

        // The forms Roslyn does accept must keep closing the group, or this costs real recovery.
        foreach (var closer in (string[])["#endif", "#endif//note", "#endif /* note */", "#endif-"])
        {
            var closed = DeclarationIndex.Build($"#if X\nclass C {{ }}\n{closer}\nclass D {{ }}");
            Assert.True(
                Assert.Single(closed.Declarations, s => s.Name == "D").SpanKnown,
                $"'{closer}' closes the group for Roslyn and must close it here");
        }
    }

    /// <summary>
    /// <para>
    /// A terminator in one branch discards trivia recorded in another, and the row below the group
    /// then reports a trivia start only one build agrees with: with <c>X</c> the comment documents
    /// <c>C</c>, and without it the <c>using</c> ends a declaration and <c>C</c> has no
    /// documentation at all. Confirmed against Roslyn in both configurations.
    /// </para>
    /// <para>
    /// Resetting the header unconditionally forgot the comment <em>and</em> restored knownness
    /// (adversarial review round 5, GPT-5.6 Sol). This is the sixth distinct way two branches can
    /// agree on brace depth and disagree on meaning.
    /// </para>
    /// </summary>
    [Fact]
    public void ATerminatorInOneBranchDiscardingAnothersTrivia_LosesTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            #if X
            // X docs
            #else
            using System;
            #endif
            class C { }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "one build documents C and the other does not");
    }

    /// <summary>
    /// The attribute form of <see cref="ATerminatorInOneBranchDiscardingAnothersTrivia_LosesTheRowBelow"/>.
    /// With <c>X</c> Roslyn reports one attribute list on <c>C</c>; without it, none.
    /// </summary>
    [Fact]
    public void ATerminatorInOneBranchDiscardingAnothersAttribute_LosesTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            #if X
            [System.Obsolete]
            #else
            using System;
            #endif
            class C { }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "one build applies the attribute and the other does not");
    }

    /// <summary>
    /// The negative that bounds the two above. A terminator inside a group discards nothing when
    /// no trivia was recorded, and both builds report the same first line for <c>C</c>, so the
    /// rule refuses branch-dependent discards rather than conditional terminators generally.
    /// </summary>
    [Fact]
    public void ATerminatorInsideAGroupDiscardingNothing_StillVouchesForTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            #if X
            using System;
            #endif
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(4, c.TriviaStartLine);
        Assert.True(c.SpanKnown, "nothing branch-dependent was discarded");
    }

    /// <summary>
    /// The poison from a branch-dependent discard must survive a later trivia record. Here the
    /// comment below the <c>#endif</c> is on a known line, so assigning knownness at that point
    /// rather than intersecting it restored a vouch the discard had just removed. Roslyn reports
    /// <c>C</c>'s first comment on line 6 without <c>X</c> and line 2 with it.
    /// </summary>
    [Fact]
    public void TriviaRecordedAfterABranchDependentDiscard_DoesNotRestoreTheVouch()
    {
        var index = DeclarationIndex.Build("""
            #if X
            // X docs
            #else
            using System;
            #endif
            // C docs
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(6, c.TriviaStartLine);
        Assert.False(c.SpanKnown, "with X the comment on line 2 is C's documentation instead");
    }

    /// <summary>
    /// The recovered closing line of a type that CONTAINS a balanced group -- the row the whole
    /// rule exists to keep. The corpus gate skipped every such declaration until round 5, so a
    /// mutation reporting their end one line short passed it (adversarial review round 5,
    /// GPT-5.6 Sol); this pins the same property on a fixture, and the gate's
    /// <c>containingVouched</c> floor keeps the corpus path non-vacuous.
    /// </summary>
    [Fact]
    public void ATypeContainingABalancedGroup_ReportsItsRealClosingLine()
    {
        var index = DeclarationIndex.Build("""
            class Outer
            {
            #if X
                void M() { }
            #else
                void M() { }
            #endif
            }
            """);

        var outer = Assert.Single(index.Declarations, s => s.Name == "Outer");
        Assert.Equal(1, outer.SignatureStartLine);
        Assert.Equal(8, outer.EndLine);
        Assert.True(outer.SpanKnown, "every branch returns to the depth the group opened at");
    }

    /// <summary>
    /// The unit-attribute close path ends a header too, so inside a group it may only take
    /// knownness away. The line-3 list poisons; <c>t1</c>'s reset empties the list set but rightly
    /// keeps the poison; then the <c>[assembly:]</c> path ASSIGNED it away. Without <c>Y</c> Roslyn
    /// binds the line-3 list to <c>Tail</c> and the unit list with it (CS0657), so <c>Tail</c>'s
    /// trivia is line 3 and it carries two lists; with <c>Y</c> its trivia is line 9 and it carries
    /// none. Found by the differential fuzzer after the <c>ResetHeader</c> fix had cut its flag
    /// count from 3,146 to 6, every survivor this one site (adversarial review round 6,
    /// Claude Opus 4.8).
    /// </summary>
    [Fact]
    public void AUnitAttributeAfterADiscardedAttribute_DoesNotRestoreTheVouch()
    {
        var index = DeclarationIndex.Build("""
            #if Y
            #else
            [System.Obsolete]
            #endif
            #if X
            class t1 { }
            #endif
            [assembly: System.CLSCompliant(true)]
            class Tail { }
            """);

        var tail = Assert.Single(index.Declarations, s => s.Name == "Tail");
        Assert.False(tail.SpanKnown, "without Y the line-3 list binds to Tail, and the unit list with it");
    }

    /// <summary>
    /// A poison raised inside a group must survive every later reset until the group closes. The
    /// comment is discarded by <c>struct s {</c>, which poisons; then <c>}</c> resets again with
    /// nothing recorded and nothing crossing, and ASSIGNING knownness there declared the header
    /// clean while still inside the group. But with <c>X</c> there is no <c>struct s</c> to have
    /// eaten the comment, so it is <c>Tail</c>'s documentation: Roslyn reports trivia line 2 with
    /// <c>X</c> and line 6 without, both configurations parsing with zero errors. Found by a
    /// differential fuzzer over 16,673 fair cases (adversarial review round 6, Claude Opus 4.8).
    /// </summary>
    [Fact]
    public void ADiscardedHeaderInsideAGroup_StaysLostAcrossALaterCleanReset()
    {
        var index = DeclarationIndex.Build("""
            #if X
            // doc
            #else
            struct s { }
            #endif
            class Tail { }
            """);

        var tail = Assert.Single(index.Declarations, s => s.Name == "Tail");
        Assert.False(tail.SpanKnown, "with X the comment on line 2 is Tail's documentation");
    }

    /// <summary>
    /// The same restore, reached through an attribute list rather than a comment, and costing a
    /// semantic claim rather than a line: with <c>Y</c> the class carries TWO <c>[Obsolete]</c>
    /// lists and the row reported one. The brace scope that resets a second time is a property's
    /// <c>{ get; set; }</c> here, which is why round 5's single-reset fixture missed the shape
    /// (adversarial review round 6, Claude Opus 4.8).
    /// </summary>
    [Fact]
    public void ADiscardedAttributeInsideAGroup_StaysLostAcrossALaterCleanReset()
    {
        var index = DeclarationIndex.Build("""
            #if Y
            [System.Obsolete]
            #else
            int p0 { get; set; }
            #endif
            [System.Obsolete]
            class Tail { }
            """);

        var tail = Assert.Single(index.Declarations, s => s.Name == "Tail");
        Assert.False(tail.SpanKnown, "with Y the class carries the line-2 list as well");
    }

    /// <summary>
    /// Sections must advance at the <c>#if</c>, not only at the <c>#else</c>. Here the discarded
    /// header sits BEFORE the group at a known depth -- so nothing else in the builder condemns it
    /// -- and only the opening directive separates it from the terminator that eats it. Roslyn
    /// reports <c>C</c>'s trivia at line 4 with <c>X</c> and line 1 without, both configurations
    /// parsing with zero errors. Verified by mutation: without the increment at <c>#if</c> this is
    /// the only test that fails.
    /// </summary>
    [Fact]
    public void AHeaderBeforeAGroupEatenInsideIt_LosesTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            // docs
            #if X
            using System;
            #endif
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.False(c.SpanKnown, "without X the comment on line 1 is C's documentation");
    }

    /// <summary>
    /// A terminator in one branch discarding a MODIFIER recorded in another. Round 5 keyed the
    /// rule on recorded trivia alone, so with nothing to lose on the trivia side the reset
    /// declared the row below known -- and got its SIGNATURE start wrong instead. Compiled in both
    /// configurations: with <c>X</c>, <c>C</c>'s signature starts at line 2 (<c>public</c>);
    /// without, at line 6 (adversarial review round 6, GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void ATerminatorInOneBranchDiscardingAnothersModifier_LosesTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            #if X
            public
            #else
            using System;
            #endif
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.False(c.SpanKnown, "with X the modifier on line 2 is part of C's signature");
    }

    /// <summary>
    /// The same rule at the OTHER reset site: a property initializer's terminator. The reset there
    /// was ungated -- neutralizing it left all 398 tests passing -- while its doc comment claimed
    /// otherwise (adversarial review round 6, GPT-5.6 Sol). With <c>X</c> the property has no
    /// initializer and the comment on line 4 documents <c>D</c>; without it the <c>= 1;</c>
    /// terminator eats that comment and <c>D</c> has none. Both configurations compile.
    /// </summary>
    [Fact]
    public void AnInitializerInOneBranchDiscardingAnothersTrivia_LosesTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            class Outer {
                int P { get; }
            #if X
                // X docs
            #else
                = 1;
            #endif
                class D { }
            }
            """);

        var d = Assert.Single(index.Declarations, s => s.Name == "D");
        Assert.False(d.SpanKnown, "with X the comment on line 4 is D's documentation");
    }

    /// <summary>
    /// The over-refusal guard for the round-6 widening. A header and the terminator that discards
    /// it, written in the SAME branch, lose nothing: whichever build compiles, either both are
    /// present or neither is. Keying the rule on the terminator's <c>DepthKnown</c> instead of on
    /// section identity would condemn every declaration below every group containing a statement,
    /// which is most of a conditional file.
    /// </summary>
    [Fact]
    public void AStatementInsideAGroup_StillVouchesForTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            class Outer {
            #if X
                int Conditional = 1;
                void M() { }
            #endif
                class D { }
            }
            """);

        var d = Assert.Single(index.Declarations, s => s.Name == "D");
        Assert.Equal(6, d.SignatureStartLine);
        Assert.True(d.SpanKnown, "nothing crossed a branch boundary");
    }

    /// <summary>
    /// A UTF-8 byte order mark is not whitespace, so trimming left it in front of the <c>#</c> and
    /// the opening directive was scanned as code. Roslyn strips the preamble and reports no error
    /// for this file, selecting <c>C</c> on line 3 with <c>X</c> and line 5 without it, so the
    /// misread vouched for one branch's declaration (adversarial review round 5, GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void AByteOrderMarkBeforeAnOpeningDirective_DoesNotHideTheGroup()
    {
        var index = DeclarationIndex.Build("\uFEFF#if X\nusing System;\nclass C { }\n#else\nclass C { }\n#endif\n");

        Assert.All(
            index.Declarations.Where(s => s.Name == "C"),
            s => Assert.False(s.SpanKnown, "both C rows are inside a conditional group"));
    }

    /// <summary>
    /// <para>
    /// Equal brace depth does not prove equal enclosing declaration. A branch that closes a brace
    /// its group did not open is closing a scope from outside the group, so the branches can agree
    /// on the depth after the <c>#endif</c> while disagreeing about which type encloses the text
    /// there -- below, the trailing member is inside <c>B</c> in one build and <c>C</c> in the
    /// other, at identical depth.
    /// </para>
    /// <para>
    /// Both reviewers found this independently, from opposite ends. The balance rule measures
    /// depth, so the fix is a floor: a group may not reach below its own opening depth.
    /// </para>
    /// </summary>
    [Fact]
    public void ABranchThatClosesAScopeItsGroupDidNotOpen_LosesTheDepth()
    {
        var index = DeclarationIndex.Build("""
            class A {
            #if X
            }
            class B {
            #else
            }
            class C {
            #endif
                void M() { }
            }
            """);

        Assert.NotEmpty(index.Declarations);
        Assert.All(index.Declarations, s => Assert.False(s.SpanKnown));

        // Reaching below the opening depth is the discriminator, not nesting: a group opened
        // inside a nested type that stays at or above its own opening depth still recovers.
        var nested = DeclarationIndex.Build("""
            class C {
                class D {
            #if X
                    void Inner() { }
            #endif
                    void After() { }
                }
            }
            """);

        Assert.True(Assert.Single(nested.Declarations, s => s.Name == "After").SpanKnown);
    }

    /// <summary>
    /// <para>
    /// A branch that does not return to the depth its group opened at makes the group unbalanced,
    /// even when the group's <em>closing</em> depth is right. This is the direction that matters:
    /// each branch is measured from the opening depth and the depth is reset at every branch
    /// boundary, so by the time the <c>#endif</c> is reached the discrepancy has been erased and
    /// the group looks balanced. The flag raised at the branch boundary is the only record that
    /// it was not, and it is what stops a span being vouched for on the strength of one branch.
    /// </para>
    /// <para>
    /// Written because the mutation battery found the flag ungated: deleting it left the suite
    /// green while turning every such group into a false <c>SpanKnown</c>, which is the one
    /// outcome the index is not allowed to produce.
    /// </para>
    /// </summary>
    [Fact]
    public void ABranchThatDoesNotReturnToTheOpeningDepth_UnbalancesTheGroup()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if A
                void Extra() {
            #else
                void Extra() { }
            #endif
                void After() { }
            }
            }
            """);

        // The group closes at the depth it opened at -- the reset saw to that -- so nothing the
        // #endif can measure distinguishes this from a group whose branches balance.
        //
        // The trailing brace balances the file for the branch that leaves one open, so that a row
        // after the #endif closes cleanly and would be vouched for if the group were judged
        // balanced. Without it the imbalance wrecks the row structure instead, and every row
        // reports unknown for a reason that has nothing to do with the rule under test -- which
        // is how the first draft of this fixture passed against a build with the rule deleted.
        //
        // Asserted over the whole file rather than by naming the trailing declaration, since the
        // row set differs between the two readings and a test naming one row can fail on its
        // absence rather than on its knownness.
        Assert.NotEmpty(index.Declarations);
        Assert.All(index.Declarations, s => Assert.False(s.SpanKnown));
    }

    /// <summary>
    /// An unbalanced group nested inside a balanced one poisons the outer group: the enclosing
    /// group cannot return to its own opening depth if something inside it did not. Asserted
    /// because propagation is a separate line from the balance check and a fixture with one level
    /// of nesting leaves it ungated.
    /// </summary>
    [Fact]
    public void AnUnbalancedInnerConditional_PoisonsTheGroupAroundIt()
    {
        var nested = DeclarationIndex.Build("""
            class C
            {
            #if A
            #if B
                void X() {
            #endif
                }
            #endif
                void After() { }
            }
            """);

        Assert.False(
            Assert.Single(nested.Declarations, s => s.Name == "After").SpanKnown,
            "an unbalanced inner group must not be forgotten when the outer one closes");

        // The fixture above does not actually reach the propagation line: the inner group's stray
        // brace also drives the outer group off its own opening depth, so the outer #endif catches
        // it unaided. Propagation is only load-bearing when the outer group looks balanced on its
        // own, which needs the inner group to have two branches -- the branch reset returns the
        // depth to the inner group's base, hiding the discrepancy from every later measurement, so
        // the flag raised at the #else is the only surviving evidence.
        var masked = DeclarationIndex.Build("""
            class C
            {
            #if A
            #if B
                void X() {
            #else
                void X() { }
            #endif
            #endif
                void After() { }
            }
            }
            """);

        // Balanced for the open branch, for the same reason as the fixture above.
        Assert.NotEmpty(masked.Declarations);
        Assert.All(masked.Declarations, s => Assert.False(s.SpanKnown));

        // The same nesting with both groups balanced resolves, so nesting alone is not the cause.
        var balanced = DeclarationIndex.Build("""
            class C
            {
            #if A
            #if B
                void X() { }
            #endif
            #endif
                void After() { }
            }
            """);

        Assert.True(Assert.Single(balanced.Declarations, s => s.Name == "After").SpanKnown);
    }

    /// <summary>
    /// <para>
    /// A group with more than one branch, each of which balances, is still balanced — most groups
    /// have a second branch, so a rule that only handled <c>#if</c>/<c>#endif</c> would recover
    /// almost nothing. <c>#elif</c> needs no separate handling: it ends the branch above it and
    /// starts another, exactly as <c>#else</c> does, which is why both spellings are asserted
    /// against one fixture.
    /// </para>
    /// <para>
    /// This does <em>not</em> gate the depth reset at the branch boundary, and an earlier version
    /// of this comment claimed it did. The reset is unobservable: a branch that fails to return to
    /// the group's opening depth raises the unbalanced flag in the same breath, and the flag
    /// condemns the group whatever the depth counter goes on to say, so deleting the reset leaves
    /// every assertion in this suite green. It is recorded as an equivalent mutation and kept for
    /// the invariant, not for the answer — without it a later branch's check would be measured
    /// against an earlier branch's leftovers, which is a worse thing for the code to mean.
    /// <see cref="ABranchThatDoesNotReturnToTheOpeningDepth_UnbalancesTheGroup"/> is what gates
    /// the flag.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("#else")]
    [InlineData("#elif OTHER")]
    public void AMultiBranchConditional_IsBalancedWhenEveryBranchIs(string middle)
    {
        var index = DeclarationIndex.Build(string.Join('\n',
        [
            "class C",
            "{",
            "    void M()",
            "    {",
            "#if FEATURE",
            "        if (a) { X(); }",
            middle,
            "        if (b) { Y(); }",
            "#endif",
            "    }",
            "    void After() { }",
            "}",
        ]));

        Assert.True(Assert.Single(index.Declarations, s => s.Name == "After").SpanKnown);
        Assert.True(Assert.Single(index.Declarations, s => s.Name == "M").SpanKnown);
    }

    /// <summary>
    /// A stray <c>#else</c>, <c>#elif</c> or <c>#endif</c> with no group open is malformed source.
    /// The scan has no opening depth to measure against, so it refuses rather than guessing — the
    /// alternative is an index-out-of-range on the frame stack.
    /// </summary>
    [Theory]
    [InlineData("#endif")]
    [InlineData("#else")]
    [InlineData("#elif X")]
    public void AConditionalDirectiveWithNoGroupOpen_LosesTheDepth(string stray)
    {
        var index = DeclarationIndex.Build(string.Join('\n',
            ["class C", "{", stray, "    void After() { }", "}"]));

        Assert.False(Assert.Single(index.Declarations, s => s.Name == "After").SpanKnown);
    }

    /// <summary>
    /// A property's initializer is terminated after its accessor block has already closed, so that
    /// terminator <i>extends</i> a span that was measured and marked known a moment earlier. It is
    /// the only path that mutates an already-measured span, and so the only one that can turn a
    /// known span into a wrong one rather than into a lost one.
    /// <para>
    /// A conditional between the accessor block and the initializer puts each candidate terminator
    /// in a different branch, so the end this reads is one branch's end. Before this was gated the
    /// row below reported a span ending at line 5 and claimed it was known, which is the answer for
    /// a <c>FEATURE</c> build and off by two lines for every other build. That falsified this
    /// suite's standing claim that an unresolvable conditional costs rows rather than corrupting
    /// them, so it is asserted here rather than left to the sibling test's <c>Assert.All</c>: the
    /// row is present and known-looking, which is exactly what a sweep over rows cannot catch.
    /// </para>
    /// </summary>
    [Fact]
    public void AConditionalInitializer_ReportsUnknownRatherThanOneBranchsEnd()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int P { get; }
            #if FEATURE
                = 1;
            #else
                = 2;
            #endif
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.False(p.SpanKnown, "a span whose end is one branch's must not report as known");

        // The property is withheld, so line 3 selects the enclosing class instead. The class is
        // legitimately known: its own braces sit outside the group, and the group's branches each
        // balance, so its end does not depend on which branch compiles. Brace balance is what
        // makes a *following* span knowable; it says nothing about a span whose terminator is
        // inside a branch, which is why P stays lost while C does not.
        Assert.Equal("C", index.FindByBodyLine(3)?.Name);

        // Without the conditional the same shape resolves, so the directive is the whole cause and
        // the trailing-initializer path still extends the span it belongs to.
        var plain = DeclarationIndex.Build("""
            class C
            {
                int P { get; }
                = 1;
            }
            """);

        var q = Assert.Single(plain.Declarations, s => s.Name == "P");
        Assert.True(q.SpanKnown);
        Assert.Equal(4, q.EndLine);
    }

    /// <summary>
    /// The attribute lists applied to a declaration, compared against Roslyn's own
    /// <c>AttributeLists</c> over every file in the corpus. Ranges are compared, not attribute
    /// names: this layer is a lexical scan, and what a consumer needs from it is where the authored
    /// text sits.
    /// <para>
    /// A corpus differential rather than fixtures because the shapes that break it are the ones
    /// nobody writes deliberately — a list spanning lines, a comment between two lists, an
    /// attribute argument containing a bracket or a string with a <c>]</c> in it. The
    /// non-vacuity floor is separate from the declaration gate's: a corpus could hold thousands of
    /// declarations and few attributes, and this gate would then pass by comparing nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryAttributeListRoslynReports_IsReportedIdenticallyByTheIndex()
    {
        var mismatches = new List<string>();
        int lists = 0;

        foreach (var file in Corpus())
        {
            var lines = File.ReadAllLines(file);
            var expected = RoslynDeclarations(lines);
            if (expected is null)
                continue;

            lists += expected.Sum(d => d.AttributeLists.Count);

            var actual = DeclarationIndex.Build(lines).Declarations
                .Where(s => s.SpanKnown)
                .Select(FormatWithAttributes)
                .ToList();

            var diff = Diff(expected.Select(FormatWithAttributes).ToList(), actual);
            if (diff.Length > 0)
                mismatches.Add($"{file}\n{diff}");
        }

        Assert.True(lists >= 200, $"corpus carries too few attribute lists to gate anything: {lists}");
        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} files disagree with Roslyn about attribute lists "
                + $"({lists} lists compared):\n\n"
                + string.Join("\n\n", mismatches.Take(8)));
    }

    /// <summary>
    /// An <c>[assembly:]</c> list is not the following declaration's, but everything around it
    /// still is. Three ways the single collapsed "trivia start" cell got this wrong, each of which
    /// reads a compiling file: a comment trailing the list re-opened trivia on the list's own line,
    /// because nothing marked the list as ended; a verbatim <c>[@assembly:]</c> target was not
    /// recognized, since the scanner emits <c>@</c> as its own token and it took the word position;
    /// and <c>[Obsolete][assembly: X]</c> cleared the trivia the real attribute had set, although
    /// C# binds both lists to the class (CS0657). Roslyn is the oracle.
    /// </summary>
    [Fact]
    public void ACompilationUnitAttributeEndsWhereItsListEnds()
    {
        string[] fixtures =
        [
            "using System.Runtime.CompilerServices;\n[assembly: InternalsVisibleTo(\"T\")] // for tests\npublic class W1\n{\n    public int V;\n}",
            "using System.Runtime.CompilerServices;\n[assembly: InternalsVisibleTo(\"T\")] /* note */\npublic class W2 { }",
            "using System;\n[@assembly: CLSCompliant(true)]\nclass W3 { }",
            "using System;\n[Obsolete][assembly: CLSCompliant(true)]\nclass W4 { }",
            "using System;\n[assembly: CLSCompliant(true)]\n// leading W5\nclass W5 { }",
            "using System;\n[Obsolete] /* note */\nclass W6 { }",
        ];

        foreach (var fixture in fixtures)
        {
            var lines = fixture.Split('\n');
            var expected = RoslynDeclarations(lines);
            Assert.NotNull(expected);

            var actual = DeclarationIndex.Build(lines).Declarations;
            Assert.Equal(
                expected.Select(FormatWithAttributes),
                actual.Select(FormatWithAttributes));
        }
    }

    /// <summary>
    /// <c>union</c> declares a type — Roslyn reports a struct — so a file using one must not lose
    /// the type and every member inside it. It is a contextual keyword, which is the trap:
    /// <c>int union;</c> is a field whose NAME is the keyword, and reading it as a type emits a
    /// nameless type and loses the field -- and a word after the keyword is NOT enough to tell them
    /// apart, because <c>int record, union;</c> and <c>M(int record, int x)</c> both have one. What
    /// separates them is that everything a type spells before its keyword is a modifier. When the
    /// hallucinated type adopted a method's <c>{</c> as its body it swallowed the statements inside
    /// it as fields, so the close positives here are every modifier a type may carry.
    /// <c>record</c> had the same hazard before <c>union</c> was recognized at all. Roslyn is the
    /// oracle.
    /// </summary>
    [Fact]
    public void AUnionIsAType_ButAFieldNamedUnionIsNot()
    {
        string[] fixtures =
        [
            "public union PetUnion(Cat, Dog);",
            "public union Result\n{\n    int Ok;\n}",
            "class C\n{\n    int union;\n    int record;\n    void M() { int union = 1; }\n}",

            // A word after the keyword is not enough: these all have one, and none is a type.
            "class C2\n{\n    public int record, union;\n    public int M(int record, int name) { return 0; }\n}",
            "class C3\n{\n    public C3(int record, int x) { }\n}",
            "class C4\n{\n    public int M(int union, int x) => union;\n}",

            // The close positives: every modifier a type may spell before its keyword.
            "public sealed partial record class Foo { }",
            "public readonly ref struct Bar { }",
            "file static class Baz { }",
            "public record struct Vec(double X);",
            "class Outer { protected internal new class Inner { } }",
        ];

        foreach (var fixture in fixtures)
        {
            var lines = fixture.Split('\n');
            var expected = RoslynDeclarations(lines);
            Assert.NotNull(expected);

            Assert.Equal(
                expected.Select(Format),
                DeclarationIndex.Build(lines).Declarations.Select(Format));
        }
    }

    /// <summary>
    /// A name is reported without its <c>@</c>, because it exists to correlate a row with a
    /// metadata member and metadata never carries the escape. The index already did this for types
    /// and members; a namespace kept the escape in the ORACLE only, because Roslyn's
    /// <c>Name.ToString()</c> reproduces the source spelling while a token's <c>Text</c> does not.
    /// Gating the rule in one place keeps the two Roslyn APIs from being mistaken for one rule.
    /// </summary>
    [Fact]
    public void AVerbatimIdentifier_IsNamedWithoutItsEscape()
    {
        var lines = """
            namespace @event.Models;

            class @class
            {
                public int @int;
                public void @void() { }
            }
            """.Split('\n');

        var expected = RoslynDeclarations(lines);
        Assert.NotNull(expected);

        var actual = DeclarationIndex.Build(lines).Declarations;
        Assert.Equal(expected.Select(Format), actual.Select(Format));
        Assert.Equal(
            ["event.Models", "class", "int", "void"],
            actual.Select(d => d.Name));
    }

    // Trivia is included deliberately. Format omits it, and an attribute-list comparison that
    // omitted it too would let a list bind correctly while the trivia start it governs was still
    // wrong -- which is exactly what a trailing comment after a unit attribute does.
    private static string FormatWithAttributes(Declaration d) =>
        $"{Format(d)} trivia={d.TriviaStartLine} attrs=[{string.Join(",", d.AttributeLists)}]";

    private static string FormatWithAttributes(DeclarationSpan s) =>
        $"{Format(s)} trivia={s.TriviaStartLine} attrs=[{string.Join(",", s.AttributeLists)}]";

    /// <summary>
    /// An operator whose symbol contains <c>=</c> — <c>==</c>, <c>!=</c>, <c>&lt;=</c>, <c>&gt;=</c>,
    /// and the C# 14 compound family — spells its NAME in punctuation, and the header cut that
    /// looks for an assignment used to cut there. That discarded the parameter list, and a header
    /// with no parameter list is not an operator: it became a field named <c>operator</c>, with the
    /// real member lost. Block-bodied, it was worse — the cut made the body look like an
    /// initializer and swallowed the members that followed.
    /// <para>
    /// This is gated here or nowhere: the corpus contains no equality or comparison operator at
    /// all, so <c>EveryDeclarationRoslynReports_IsReportedIdenticallyByTheIndex</c> passed for the
    /// wrong reason. The close negatives are the operators that do NOT contain <c>=</c> and the
    /// real assignments that do. Roslyn is the oracle; the fixture compiles.
    /// </para>
    /// </summary>
    [Fact]
    public void AnOperatorWhoseSymbolContainsEquals_IsNotCutAtIt()
    {
        string[] fixtures =
        [
            """
            namespace N;
            public struct V
            {
                public int X;
                public static bool operator ==(V a, V b) => a.X == b.X;
                public static bool operator !=(V a, V b) => a.X != b.X;
                public static bool operator <=(V a, V b) => a.X <= b.X;
                public static bool operator >=(V a, V b) => a.X >= b.X;
                public override bool Equals(object? o) => o is V v && v.X == X;
                public override int GetHashCode() => X;
            }
            """,
            """
            namespace N;
            public struct W
            {
                public int X;
                public static bool operator ==(W a, W b) { return a.X == b.X; }
                public static bool operator !=(W a, W b) { return a.X != b.X; }
                public override bool Equals(object? o) => o is W v && v.X == X;
                public override int GetHashCode() => X;
            }
            """,
            """
            namespace N;
            public struct U
            {
                public static U operator +(U a, U b) => a;
                public static bool operator <(U a, U b) => true;
                public static bool operator >(U a, U b) => true;
                public static U operator >>>(U a, int b) => a;
                public static U operator checked +(U a, U b) => a;
                public int Field = 1;
                public System.Func<int, bool> Ge = x => x >= 0;
            }
            """,
        ];

        foreach (var fixture in fixtures)
        {
            var lines = fixture.Split('\n');
            var expected = RoslynDeclarations(lines);
            Assert.NotNull(expected);

            Assert.Equal(
                expected.Select(Format),
                DeclarationIndex.Build(lines).Declarations.Select(Format));
        }
    }

    /// <summary>
    /// Which declaration an initializer extends is itself branch-dependent when a conditional
    /// group sits between the accessor block and the <c>=</c>. Roslyn reports <c>P</c> as
    /// lines 2-2 with <c>X</c> and 2-6 without, both configurations parsing with zero errors,
    /// so the row before the group cannot be vouched for. Every other conditional rule asks
    /// whether a header written BEFORE a group survives it; this is a token written after the
    /// group reaching back through it. Found by adversarial review round 7 (Gemini 3.1 Pro).
    /// </summary>
    [Fact]
    public void AnInitializerReachingBackThroughAGroup_LosesTheDeclarationBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C {
                public int P { get; }
            #if X
                public int Q { get; }
            #endif
                = 1;
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.False(p.SpanKnown, "without X the initializer belongs to P, which then ends on line 6");
    }

    /// <summary>
    /// An <c>#elif</c> chain offers more than one alternative target, so the refusal has to take
    /// the whole preceding sibling run rather than only the nearest one: the initializer belongs
    /// to <c>Q</c> with <c>X</c>, to <c>R</c> with <c>Y</c>, and to <c>P</c> with neither.
    /// </summary>
    [Fact]
    public void AnInitializerReachingBackThroughAnElifChain_LosesEverySiblingItCouldBindTo()
    {
        var index = DeclarationIndex.Build("""
            class C {
                public int P { get; }
            #if X
                public int Q { get; }
            #elif Y
                public int R { get; }
            #endif
                = 1;
            }
            """);

        Assert.All(
            index.Declarations.Where(s => s.Name is "P" or "Q" or "R"),
            s => Assert.False(s.SpanKnown, $"{s.Name} is a possible target of the line-8 initializer"));
    }

    /// <summary>
    /// The guard against over-refusing: an initializer that shares its branch with the block it
    /// closes reaches back through nothing, so the siblings around it keep their vouch. Without
    /// this the ninth-way rule would condemn every declaration preceding any conditional
    /// initializer in a file.
    /// </summary>
    [Fact]
    public void AnInitializerSharingItsBranch_StillVouchesForTheDeclarationBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C {
                public int A { get; } = 1;
            #if X
                public int Q { get; } = 2;
            #endif
            }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.True(a.SpanKnown, "the line-4 initializer never reaches past its own branch");
    }

    /// <summary>
    /// The tenth way. A branch can carry a complete declaration of its own, whose <c>=</c> is an
    /// ordinary same-section initializer, while the branch beside it carries a bare tail that binds
    /// to the row ABOVE the group. Roslyn ends <c>P</c> at line 3 with <c>X</c> and at line 9
    /// without it, both with zero errors, so no single span is right.
    ///
    /// The point of this test is the FIRST <c>=</c>: it belongs to <c>Q</c> and disqualifies
    /// itself, and a search that stopped there never examined the <c>= 1</c> behind it. The
    /// builder therefore takes the first <c>=</c> that qualifies, not the first that exists.
    /// Neutralize that by restoring the <c>break</c> on the first <c>=</c> and this fails, as do
    /// all five <see cref="ConditionalRecoveryFuzzTests"/> seeds. Found by adversarial review
    /// round 8 (GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void AnInitializerMaskedByAnotherBranchsInitializer_LosesTheDeclarationBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int P { get; }
            #if X
                int Q = 0
            #else
                = 1
            #endif
                ;
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.False(p.SpanKnown, "P ends on line 3 with X and line 9 without it");
    }

    /// <summary>
    /// The mirror of the shape above, with the reaching-back tail spelled first and the branch
    /// carrying its own complete declaration second.
    ///
    /// This shape passes BEFORE the round-8 fix as well: round 7 already handled a bare tail at
    /// <c>pending[0]</c>, and each of these two sources contains exactly one QUALIFYING <c>=</c>,
    /// so taking the first or the last is indistinguishable here. It is therefore coverage of the
    /// mirror ordering, not a gate on the round-8 change -- the gate is the test above, and
    /// <see cref="ConditionalRecoveryFuzzTests"/> generates both orderings.
    /// </summary>
    [Fact]
    public void AnInitializerMaskingAnotherBranchsInitializer_LosesTheDeclarationBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int P { get; }
            #if X
                = 1
            #else
                int Q = 0
            #endif
                ;
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.False(p.SpanKnown, "P ends on line 9 with X and line 3 without it");
    }

    /// <summary>
    /// The over-refusal guard for the pair above. Both branches carry a complete declaration with
    /// its own initializer, so nothing reaches back and <c>P</c> keeps its vouch. Without this,
    /// "refuse whenever a group contains an <c>=</c>" would pass both tests above and cost the
    /// corpus every conditionally-declared field.
    /// </summary>
    [Fact]
    public void TwoBranchesWithTheirOwnInitializers_StillVouchForTheDeclarationBeforeThem()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int P { get; }
            #if X
                int Q = 0;
            #else
                int R = 1;
            #endif
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.True(p.SpanKnown, "neither branch's initializer reaches past its own section");
    }

    /// <summary>
    /// The eleventh way. An enum member's initializer reaches back exactly as a field's does, but
    /// an enum member is terminated by <c>,</c> or <c>}</c> and so never passes through the
    /// <c>;</c> path that refuses it. Roslyn ends <c>A</c> at line 2 with <c>X</c> and line 6
    /// without it, both with zero errors, and the product had already emitted and vouched
    /// <c>A</c> at the branch-local <c>,</c> before the <c>=</c> was ever read.
    ///
    /// Neutralize either <c>ReachingBackEquals</c> call in the enum paths and this fails. Found by
    /// adversarial review round 8 (Gemini 3.1 Pro).
    /// </summary>
    [Fact]
    public void AnEnumInitializerReachingBackThroughAGroup_LosesTheMemberBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            enum E {
                A
            #if X
                , B
            #endif
                = 1
            }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.False(a.SpanKnown, "A ends on line 2 with X and line 6 without it");
    }

    /// <summary>
    /// The same reaching-back enum initializer, but terminated by a following <c>,</c> rather than
    /// by the enum's closing brace, so it pins the comma path rather than the brace path.
    /// </summary>
    [Fact]
    public void AnEnumInitializerReachingBackBeforeAnotherMember_LosesTheMemberBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            enum E {
                A
            #if X
                , B
            #endif
                = 1, C
            }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.False(a.SpanKnown, "A absorbs the line-6 initializer when X is undefined");
    }

    /// <summary>
    /// The over-refusal guard for the two enum tests above, and the reason they test for a
    /// reaching-back <c>=</c> rather than simply for a conditional comma. Here the group carries a
    /// member and its comma but no initializer reaches back, so every build ends <c>A</c> on
    /// line 2 and the vouch stands. Refusing on the conditional comma alone would pass both tests
    /// above while costing the corpus every conditionally-extended enum.
    /// </summary>
    [Fact]
    public void AConditionalEnumMemberWithNoInitializer_StillVouchesForTheMemberBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            enum E {
                A
            #if X
                , B
            #endif
            }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.True(a.SpanKnown, "A ends on line 2 in both builds");
    }

    /// <summary>
    /// The twelfth way, and the third direction: ways 1-8 ask whether a header written before a
    /// group survives it, ways 9-11 are a tail reaching back through one, and this is a closed and
    /// already-vouched declaration reaching FORWARD to claim a terminator written after it.
    ///
    /// A type declaration takes an optional trailing <c>;</c>. With <c>Y</c> it belongs to
    /// <c>B</c> and <c>A</c> ends at its own brace on line 6; without <c>Y</c> there is no
    /// <c>B</c>, the <c>;</c> is <c>A</c>'s own, and <c>A</c> ends on line 10. All four symbol
    /// configurations parse with zero errors.
    ///
    /// This is the one defect in the series that the PR itself introduced: before balanced-group
    /// recovery the leading group poisoned the rest of the file, so <c>A</c> was declined for an
    /// unrelated reason. Found by adversarial review round 9 (Claude Opus 5).
    /// </summary>
    [Fact]
    public void ATrailingSemicolonAfterAGroup_LosesTheTypeItCouldBelongTo()
    {
        var index = DeclarationIndex.Build("""
            #if X
            class Z { }
            #endif
            class A
            {
            }
            #if Y
            class B { }
            #endif
            ;
            class Tail { }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.False(a.SpanKnown, "A ends on line 6 with Y and line 10 without it");
    }

    /// <summary>
    /// The same token with no conditional anywhere, which is where the defect actually lived: the
    /// scan did not model the optional trailing <c>;</c> at all, so it reported a span that was
    /// simply wrong rather than branch-dependent. Roslyn ends <c>A</c> on line 4.
    ///
    /// This one is a pre-existing wrong span rather than a wrong vouch, and it is why the
    /// conditional case above exists. It is also not a deliberate convention: every bodiless row
    /// the scan emits already includes its terminating <c>;</c>.
    /// </summary>
    [Fact]
    public void ATrailingSemicolonAfterAType_ExtendsThatTypesSpan()
    {
        var index = DeclarationIndex.Build("""
            class A
            {
            }
            ;
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.Equal(4, a.EndLine);
        Assert.True(a.SpanKnown, "no conditional is involved, so the span is provable");
    }

    /// <summary>
    /// The both-branches-symmetric form, which is worse than the asymmetric one: the scan's answer
    /// matches neither build. With <c>X</c> the <c>;</c> is on line 5 and <c>A</c> ends there;
    /// without it the <c>;</c> is on line 7. The unfixed scan reported line 3.
    /// </summary>
    [Fact]
    public void ATrailingSemicolonSpelledInBothBranches_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class A
            {
            }
            #if X
                ;
            #else
                ;
            #endif
            class Tail { }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.False(a.SpanKnown, "A ends on line 5 with X and line 7 without it");
    }

    /// <summary>
    /// The thirteenth way, and it shields the twelfth: the trailing-<c>;</c> rule requires an
    /// empty pending run, but the scan lexes every branch, so a declaration written in a branch
    /// this build discards is still pending when the <c>;</c> arrives and hides it. With <c>X</c>
    /// the <c>;</c> terminates <c>Field</c> and <c>Sy</c> ends on line 2; without <c>X</c> there
    /// is no <c>Field</c>, so the <c>;</c> is <c>Sy</c>'s own optional trailer and <c>Sy</c> ends
    /// on line 6. Both builds parse with zero errors. Found by adversarial review round 10
    /// (Gemini 3.1 Pro).
    /// </summary>
    [Fact]
    public void ATrailingSemicolonShieldedByAConditionalMember_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C {
                class Sy { }
            #if X
                int Field = 1
            #endif
                ;
            }
            """);

        var sy = Assert.Single(index.Declarations, s => s.Name == "Sy");
        Assert.False(sy.SpanKnown, "Sy ends on line 2 with X and line 6 without it");
    }

    /// <summary>
    /// The same shield spelled with a delegate, which reaches the terminator through a different
    /// classification path than a field does. At file scope, where the enclosing type cannot be
    /// what supplies the refusal.
    /// </summary>
    [Fact]
    public void ATrailingSemicolonShieldedByAConditionalDelegate_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class Sy { }
            #if X
            delegate void D()
            #endif
            ;
            """);

        var sy = Assert.Single(index.Declarations, s => s.Name == "Sy");
        Assert.False(sy.SpanKnown, "Sy ends on line 1 with X and line 5 without it");
    }

    /// <summary>
    /// The twelfth way's refusal set was too narrow, and a brace-less scope opener is what
    /// exposed it. A file-scoped namespace inside the group re-parents the row the <c>;</c>
    /// appears to follow, so a walk over that row's siblings never visits <c>A</c> at the outer
    /// scope. Without <c>Y</c> the file is <c>class A {\n}\n;</c> — a legal program with zero
    /// errors in which <c>A</c> ends at the <c>;</c> on line 10 — and <c>A</c> was vouched at
    /// 1..3. Found by adversarial review round 10 (Claude Opus 5).
    /// </summary>
    [Fact]
    public void ATrailingSemicolonBehindAConditionalFileScopedNamespace_LosesTheTypeAtTheOuterScope()
    {
        var index = DeclarationIndex.Build("""
            class A
            {
            }
            #if Y
            namespace NS;
            class B
            {
            }
            #endif
            ;
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.False(a.SpanKnown, "A ends at the \";\" on line 10 in the build without Y");
    }

    /// <summary>
    /// The same hole one step further out, and the arm round 10 predicted but could not build a
    /// parsing case for. A file-scoped namespace ends a declaration without closing a block, so
    /// <c>lastClosed</c> is <c>-1</c> and the trailing-<c>;</c> test does not run at all. Without
    /// <c>X</c> the file is <c>class Sr { }\n;</c> and <c>Sr</c> ends on line 5; the build WITH
    /// <c>X</c> does not parse, so only one configuration is fair and the pairwise build-vs-build
    /// gate cannot see this at all — the product gate caught it. Found by the widened generator
    /// (adversarial review round 10, Claude Opus 5).
    /// </summary>
    [Fact]
    public void ATrailingSemicolonAfterAConditionalBracelessDeclaration_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class Sr { }
            #if X
            namespace Nr;
            #endif
            ;
            """);

        var sr = Assert.Single(index.Declarations, s => s.Name == "Sr");
        Assert.False(sr.SpanKnown, "Sr ends at the \";\" on line 5 in the build without X");
    }

    /// <summary>
    /// The same arm where the branch-dependent declaration leaves NO ROW AT ALL: a namespace
    /// inside a type is not an allowed row, so a test on the last row's vouch cannot see it and
    /// the rule reads the last terminator's section instead. This is the shape the widened
    /// generator produced that the file-scope form did not cover.
    /// </summary>
    [Fact]
    public void ATrailingSemicolonAfterABracelessDeclarationThatEmitsNoRow_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class Outer
            {
            class Sr { }
            #if X
            namespace Nr;
            #endif
            ;
            }
            """);

        var sr = Assert.Single(index.Declarations, s => s.Name == "Sr");
        Assert.False(sr.SpanKnown, "Sr ends at the \";\" on line 7 in the build without X");
    }

    /// <summary>
    /// The recall side of that arm, and the reason it compares sections rather than simply
    /// refusing whenever <c>lastClosed</c> is <c>-1</c>. A stray <c>;</c> written in the same
    /// branch as the terminator before it can reach nothing new, even in a file that has an
    /// unrelated group earlier in it.
    /// </summary>
    [Fact]
    public void AStraySemicolonInTheSameBranchAsItsPredecessor_KeepsItsNeighboursVouches()
    {
        var index = DeclarationIndex.Build("""
            #if X
            #endif
            class C
            {
                int Keep;
                ;
            }
            """);

        var keep = Assert.Single(index.Declarations, s => s.Name == "Keep");
        Assert.True(keep.SpanKnown, "the stray \";\" shares its predecessor's branch");
    }

    /// <summary>
    /// The recall side of the outward walk, and the reason it stops at a VOUCHED parent.
    /// <c>Outer</c>
    /// exists identically in every build, so the scope it opens exists in every build and no
    /// terminator inside it can reach a declaration outside it. <c>Before</c> must keep its vouch.
    /// </summary>
    [Fact]
    public void ARefusalInsideAVouchedScope_DoesNotEscapeToTheScopeAboveIt()
    {
        var index = DeclarationIndex.Build("""
            class Before { }
            class Outer
            {
                class A { }
            #if Y
                class B { }
            #endif
                ;
            }
            """);

        var before = Assert.Single(index.Declarations, s => s.Name == "Before");
        Assert.True(before.SpanKnown, "Outer exists in every build, so the \";\" cannot escape it");
    }

    /// <summary>
    /// The shield with the terminator in a DIFFERENT group, which is why the rule compares
    /// sections instead of asking whether the <c>;</c> itself is at a known depth. With <c>X</c>
    /// and <c>Y</c> the <c>;</c> terminates <c>F</c> and <c>Sy</c> ends on line 1; with <c>Y</c>
    /// alone there is no <c>F</c>, so the <c>;</c> is <c>Sy</c>'s trailer and <c>Sy</c> ends on
    /// line 6. Both parse cleanly. The <c>;</c> is inside a group here, so a <c>DepthKnown</c>
    /// test on it would have let this through.
    /// </summary>
    [Fact]
    public void ATrailingSemicolonShieldedFromAnotherGroup_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class Sy { }
            #if X
            int F = 1
            #endif
            #if Y
            ;
            #endif
            """);

        var sy = Assert.Single(index.Declarations, s => s.Name == "Sy");
        Assert.False(sy.SpanKnown, "Sy ends on line 1 with X and Y, and line 6 with Y alone");
    }

    /// <summary>
    /// The recall side of the same rule, and the reason it is not simply "a conditional member
    /// precedes the <c>;</c>". A declaration and its terminator written in ONE branch vanish
    /// together, so nothing can reach past them: <c>Sy</c> ends on line 1 in every build. This is
    /// the overwhelmingly common shape of a conditional member, and refusing it would cost the
    /// corpus far more than the defect it guards against.
    /// </summary>
    [Fact]
    public void AConditionalMemberCarryingItsOwnTerminator_KeepsTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class Sy { }
            #if X
            int F = 1;
            #endif
            """);

        var sy = Assert.Single(index.Declarations, s => s.Name == "Sy");
        Assert.True(sy.SpanKnown, "F and its \";\" are in one branch and vanish together");
    }

    /// <summary>
    /// The benign counterpart, and the gate on the <c>DepthKnown</c> term of the shield test.
    /// <c>Field</c>'s header is written outside the group, so it exists in every build and owns
    /// the <c>;</c> in every build; only a second declarator is conditional. <c>Sy</c> ends on
    /// line 2 in both builds and must keep its vouch. Dropping the <c>DepthKnown</c> term and
    /// leaving only the section comparison fails this test, because the header, the group and the
    /// <c>;</c> are all in different sections.
    /// </summary>
    [Fact]
    public void AConditionalDeclaratorOnAnUnconditionalMember_KeepsTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C {
                class Sy { }
                int Field
            #if X
                    , Other
            #endif
                ;
            }
            """);

        var sy = Assert.Single(index.Declarations, s => s.Name == "Sy");
        Assert.True(sy.SpanKnown, "Field exists in every build, so the \";\" never reaches Sy");
    }

    /// <summary>
    /// A trivia poison that crosses no branch is spent once the declaration that raised it ends,
    /// and <c>ResetHeader</c> discharging it is what keeps the rest of the file vouched. With
    /// <c>X</c> the comment documents <c>s</c>; without <c>X</c> neither exists, so <c>Tail</c>
    /// starts on its own signature line in every build. This is the over-refusal side of the
    /// round-6 stickiness rule, and it is the only gate on the discharge: round 7 (Gemini 3.1
    /// Pro) showed all 410 tests passing with the assignment neutralized.
    /// </summary>
    [Fact]
    public void ATriviaPoisonCrossingNoBranch_IsDischargedByTheNextReset()
    {
        var index = DeclarationIndex.Build("""
            #if X
            // doc
            class s { }
            #endif
            class Tail { }
            """);

        var tail = Assert.Single(index.Declarations, s => s.Name == "Tail");
        Assert.True(tail.SpanKnown, "the comment is consumed inside the branch that contains it");
    }

    /// <summary>
    /// The second shape of the ninth way, and the one that walks past
    /// <c>AConditionalInitializer_ReportsUnknownRatherThanOneBranchsEnd</c> entirely: the
    /// initializer sits INSIDE the group, and the sibling branch has already consumed the row it
    /// would have extended, so the guarded path is never entered at all. Roslyn reports <c>P</c>
    /// as lines 3-3 with <c>X</c> and 3-7 without. Found independently by adversarial review
    /// round 7 (Gemini 3.1 Pro via a widened fuzzer, Claude Opus 5 as repro B).
    /// </summary>
    [Fact]
    public void AnInitializerConsumedByAnotherBranch_LosesTheDeclarationBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int P { get; set; }
            #if X
                int Q;
            #else
                = 5;
            #endif
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.False(p.SpanKnown, "without X the line-7 initializer belongs to P, which then ends there");
    }

    /// <summary>
    /// The bodiless emit path consults its terminator's depth flag, and nothing enforced it: the
    /// whole suite, differential fuzzer included, stayed green with the term deleted, because the
    /// shipped generator never placed a group inside a type body and so had never compared a
    /// field, method, property or event row at all. Roslyn reports <c>f</c> as lines 3-5 with
    /// <c>X</c> and 3-7 without. Found by adversarial review round 7 (Claude Opus 5).
    /// </summary>
    [Fact]
    public void ABodilessRowWhoseTerminatorIsInABranch_IsNotVouchedFor()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int f
            #if X
                ;
            #else
                ;
            #endif
            }
            """);

        var f = Assert.Single(index.Declarations, s => s.Name == "f");
        Assert.False(f.SpanKnown, "which \";\" terminates the field depends on the branch");
    }

    /// <summary>
    /// The other half of the bodiless emit path: a signature token left in a branch moves the
    /// row's start, and that term was ungated for the same reason. Roslyn reports <c>f</c> as
    /// lines 4-6 with <c>X</c> and 6-6 without. Found by adversarial review round 7
    /// (Claude Opus 5).
    /// </summary>
    [Fact]
    public void ABodilessRowWhoseModifierIsInABranch_IsNotVouchedFor()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if X
                public
            #endif
                int f;
            }
            """);

        var f = Assert.Single(index.Declarations, s => s.Name == "f");
        Assert.False(f.SpanKnown, "without X the field's signature starts on line 6, not line 4");
    }

    private static string Format(Declaration d) =>
        $"{d.Kind} {d.Name} {d.SignatureStartLine}-{d.EndLine}";

    private static string Format(DeclarationSpan s) =>
        $"{s.Kind} {s.Name} {s.SignatureStartLine}-{s.EndLine}";

    /// <summary>
    /// A multiset difference, not a set difference. Cardinality is the point: a builder that emits
    /// the same declaration twice, or drops one of two identically-spelled rows, is exactly the
    /// regression this gate exists to catch, and a set comparison reports both as agreement.
    /// </summary>
    private static string Diff(List<string> expected, List<string> actual)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in expected)
            counts[e] = counts.GetValueOrDefault(e) + 1;
        foreach (var a in actual)
            counts[a] = counts.GetValueOrDefault(a) - 1;

        var missing = counts.Where(kv => kv.Value > 0).ToList();
        var extra = counts.Where(kv => kv.Value < 0).ToList();
        if (missing.Count == 0 && extra.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var m in missing.OrderBy(kv => kv.Key, StringComparer.Ordinal).Take(6))
            sb.Append("  Roslyn only: ").Append(m.Key).AppendLine(m.Value > 1 ? $" (x{m.Value})" : "");
        foreach (var e in extra.OrderBy(kv => kv.Key, StringComparer.Ordinal).Take(6))
            sb.Append("  index only:  ").Append(e.Key).AppendLine(e.Value < -1 ? $" (x{-e.Value})" : "");
        return sb.ToString();
    }

    private sealed record Declaration(
        DeclarationKind Kind, string Name, int TriviaStartLine, int SignatureStartLine, int EndLine)
    {
        public IReadOnlyList<LineRange> AttributeLists { get; init; } = [];
    }

    /// <summary>
    /// The oracle. Returns <see langword="null"/> for a file the differential cannot fairly judge:
    /// one Roslyn cannot parse, or one whose conditional compilation makes the two disagree about
    /// which text is even present. Roslyn parses with no preprocessor symbols defined, so it treats
    /// a <c>#if</c> body as disabled text and reports no declarations from it, while the index — by
    /// design, because it is lexical — indexes the text it can see and marks what it cannot vouch
    /// for unknown. Neither is wrong; they are answering different questions.
    /// </summary>
    private static List<Declaration>? RoslynDeclarations(string[] lines) =>
        RoslynDeclarations(lines, requireNoConditionals: true, out _);

    /// <param name="requireNoConditionals">
    /// When true the file is declined if it carries any conditional directive, which is what an
    /// equality comparison requires: Roslyn drops the disabled branches and the lexical index
    /// keeps them. The subset gate passes false and reads <paramref name="regions"/> instead.
    /// </param>
    /// <param name="regions">
    /// The outermost conditional regions, as 1-based inclusive line ranges from each <c>#if</c>
    /// to the <c>#endif</c> that closes it. Empty for a file with no conditional.
    /// </param>
    private static List<Declaration>? RoslynDeclarations(
        string[] lines,
        bool requireNoConditionals,
        out List<(int Start, int End)> regions)
    {
        regions = [];

        var tree = CSharpSyntaxTree.ParseText(
            string.Join("\n", lines),
            new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse));
        if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
            return null;

        var root = tree.GetRoot();

        // Ask Roslyn which files actually carry a conditional directive rather than searching the
        // text for one. "#if" appears inside comments and string literals — ScanTokenTests.cs spells
        // it nine times and has no directive at all — and a substring test declines those files,
        // shrinking the corpus for no reason and doing it invisibly. Only conditional directives
        // matter here; #region, #pragma, and #nullable do not change which text is present.
        if (root.ContainsDirectives)
        {
            int open = 0;
            int start = 0;

            foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
            {
                bool isIf = trivia.IsKind(SyntaxKind.IfDirectiveTrivia);
                if (!isIf && !trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
                    continue;

                if (requireNoConditionals && isIf)
                    return null;

                int line = trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                if (isIf)
                {
                    if (open++ == 0)
                        start = line;
                }
                else if (open > 0 && --open == 0)
                {
                    regions.Add((start, line));
                }
            }

            // A group left open at end of file closes nowhere, so its region runs to the last line.
            if (open > 0)
                regions.Add((start, lines.Length));
        }

        var result = new List<Declaration>();
        Walk(root, result);
        return result;
    }

    /// <summary>
    /// A checked operator is a distinct metadata member — <c>op_CheckedAddition</c>, not
    /// <c>op_Addition</c> — and both may be declared in one type, so the name has to carry
    /// <c>checked</c> or the two declarations become indistinguishable.
    /// </summary>
    private static string OperatorName(SyntaxToken checkedKeyword, SyntaxToken spelling) =>
        checkedKeyword.IsKind(SyntaxKind.CheckedKeyword)
            ? "operator checked " + spelling.ValueText
            : "operator " + spelling.ValueText;

    private static void Walk(SyntaxNode node, List<Declaration> into)
    {
        foreach (var child in node.ChildNodes())
        {
            switch (child)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    // Roslyn's Name.ToString() reproduces the source spelling, escape included,
                    // while an identifier's Text does not -- SyntaxToken.Text for "@class" is
                    // "class". Left as it comes, the oracle would demand "@event.Models" from a
                    // namespace and "class" from a type, which is not one rule. The index reports
                    // the declared name, so strip the escape here and let the difference be a real
                    // disagreement rather than an artefact of two Roslyn APIs.
                    into.Add(Make(ns, DeclarationKind.Namespace, ns.Name.ToString().Replace("@", "")));
                    Walk(ns, into);
                    continue;

                case TypeDeclarationSyntax type:
                    // A C# 14 extension block is spelled as an unnamed type declaration. It has no
                    // metadata counterpart — its members are emitted onto the enclosing static
                    // class — so the index deliberately makes it transparent, and the oracle walks
                    // through it to reach the members that do exist.
                    if (type.Identifier.ValueText.Length > 0)
                        into.Add(Make(type, TypeKind(type), type.Identifier.ValueText));
                    Walk(type, into);
                    continue;

                case EnumDeclarationSyntax e:
                    into.Add(Make(e, DeclarationKind.Enum, e.Identifier.ValueText));
                    Walk(e, into);
                    continue;

                case DelegateDeclarationSyntax d:
                    into.Add(Make(d, DeclarationKind.Delegate, d.Identifier.ValueText));
                    continue;

                case EnumMemberDeclarationSyntax em:
                    into.Add(Make(em, DeclarationKind.EnumMember, em.Identifier.ValueText));
                    continue;

                case MethodDeclarationSyntax m:
                    into.Add(Make(m, DeclarationKind.Method, m.Identifier.ValueText));
                    continue;

                case ConstructorDeclarationSyntax c:
                    into.Add(Make(c, DeclarationKind.Constructor, c.Identifier.ValueText));
                    continue;

                case DestructorDeclarationSyntax dt:
                    into.Add(Make(dt, DeclarationKind.Destructor, "~" + dt.Identifier.ValueText));
                    continue;

                case OperatorDeclarationSyntax op:
                    into.Add(Make(op, DeclarationKind.Method, OperatorName(op.CheckedKeyword, op.OperatorToken)));
                    continue;

                case ConversionOperatorDeclarationSyntax co:
                    into.Add(Make(co, DeclarationKind.Method, OperatorName(co.CheckedKeyword, co.ImplicitOrExplicitKeyword)));
                    continue;

                case IndexerDeclarationSyntax ix:
                    into.Add(Make(ix, DeclarationKind.Property, "this"));
                    continue;

                case PropertyDeclarationSyntax p:
                    into.Add(Make(p, DeclarationKind.Property, p.Identifier.ValueText));
                    continue;

                case EventDeclarationSyntax ev:
                    into.Add(Make(ev, DeclarationKind.Event, ev.Identifier.ValueText));
                    continue;

                case EventFieldDeclarationSyntax evf:
                    foreach (var v in evf.Declaration.Variables)
                        into.Add(Make(evf, DeclarationKind.Event, v.Identifier.ValueText));
                    continue;

                case FieldDeclarationSyntax f:
                    foreach (var v in f.Declaration.Variables)
                        into.Add(Make(f, DeclarationKind.Field, v.Identifier.ValueText));
                    continue;
            }
        }
    }

    private static DeclarationKind TypeKind(TypeDeclarationSyntax type) => type switch
    {
        RecordDeclarationSyntax => DeclarationKind.Record,
        StructDeclarationSyntax => DeclarationKind.Struct,
        InterfaceDeclarationSyntax => DeclarationKind.Interface,
        _ => DeclarationKind.Class,
    };

    private static Declaration Make(SyntaxNode node, DeclarationKind kind, string name)
    {
        // The signature begins after any attribute list: an attribute is leading trivia, and a
        // slice that started at the attribute would report a different first line than the PDB.
        var attributes = node switch
        {
            MemberDeclarationSyntax m => m.AttributeLists,
            _ => default,
        };
        var signatureStart = attributes.Count > 0
            ? attributes.Last().GetLastToken().GetNextToken()
            : node.GetFirstToken();

        return new Declaration(
            kind,
            name,
            TriviaStartLine(node),
            Line(node.SyntaxTree, signatureStart.SpanStart),
            EndLine(node))
        {
            AttributeLists = [.. attributes.Select(a => new LineRange(
                Line(node.SyntaxTree, a.SpanStart),
                Line(node.SyntaxTree, a.Span.End)))],
        };
    }

    /// <summary>
    /// Where a slice of this declaration has to begin: at its documentation comment, or at the
    /// declaration itself when it carries none. A comment sitting on the same line as the previous
    /// token trails that token — it documents what came before, not what comes after.
    /// </summary>
    private static int TriviaStartLine(SyntaxNode node)
    {
        var first = node.GetFirstToken();
        var previous = first.GetPreviousToken();
        int previousEnd = previous == default ? 0 : Line(node.SyntaxTree, previous.Span.End);

        foreach (var trivia in first.LeadingTrivia)
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                && !trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                && !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                continue;

            int line = Line(node.SyntaxTree, trivia.SpanStart);
            if (line > previousEnd)
                return line;
        }

        return Line(node.SyntaxTree, node.SpanStart);
    }

    private static int Line(SyntaxTree tree, int position) =>
        tree.GetLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(position, 0)).StartLinePosition.Line + 1;

    /// <summary>
    /// The last line of a declaration, from its span, which excludes trailing trivia. A file-scoped
    /// namespace ends at its last member for the same reason every other row does — it scopes the
    /// rest of the file, but a trailing comment is not part of the declaration.
    /// </summary>
    private static int EndLine(SyntaxNode node) =>
        node.SyntaxTree.GetLineSpan(node.Span).EndLinePosition.Line + 1;

    /// <summary>
    /// Real C# from this repository, discovered the same way the parse-validity corpus is: every
    /// PDB beside the test binary names the source files its assembly was built from.
    /// </summary>
    /// <summary>
    /// <para>
    /// The files carrying a conditional directive, which is a different corpus from
    /// <see cref="Corpus"/> and has to be, because this repository has almost none:
    /// five files in fourteen hundred, and not one of them is in this test binary's
    /// dependency closure, so the PDB-discovered corpus contains zero. A gate built on that
    /// corpus would pass by comparing nothing.
    /// </para>
    /// <para>
    /// The <c>#if</c> text search is a candidate filter, not the answer -- Roslyn confirms every
    /// candidate. Searching text to <em>decline</em> a file is unsound, because <c>#if</c> occurs
    /// in comments and string literals; searching it to <em>select</em> candidates is sound in
    /// the direction that matters, since a file with a real directive always contains the text.
    /// </para>
    /// </summary>
    private static List<string> ConditionalCorpus()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "dotnet-inspect.slnx")))
            root = root.Parent;

        // Deliberately not a skip. A gate that cannot find its corpus has to say so; returning an
        // empty list would be indistinguishable from a corpus with no conditional files, which is
        // the exact failure this test exists to rule out.
        Assert.NotNull(root);

        var files = new List<string>();

        foreach (var directory in new[] { "src", "tests" })
        {
            var path = Path.Combine(root.FullName, directory);
            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    continue;

                if (File.ReadAllText(file).Contains("#if", StringComparison.Ordinal))
                    files.Add(file);
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static List<string> Corpus()
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var assemblyPath in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
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
                    if (member.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(member.FilePath))
                        files.Add(member.FilePath);
                }
            }
        }

        return [.. files];
    }
}
