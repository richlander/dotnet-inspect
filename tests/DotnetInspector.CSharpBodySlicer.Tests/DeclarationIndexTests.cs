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
    /// Pins a known limitation, so that fixing it is visible rather than silent.
    /// <para>
    /// A conditional directive sets the lexer's untracked flag and nothing ever clears it, so the
    /// place is lost for the rest of the file rather than for the region the directive guards. Here
    /// the conditional is brace-balanced and <c>Always</c> is declared after the <c>#endif</c>, so
    /// no branch can affect it — and it still reports an unknown span and cannot be found by body
    /// line. Identical code without the directive resolves.
    /// </para>
    /// <para>
    /// This is conservative rather than wrong. It is also expensive: on dotnet/runtime's libraries a
    /// conditional appears in 8.3% of files but costs 12.1% of declarations, because the loss runs
    /// to end of file. <see href="https://github.com/richlander/dotnet-inspect/issues/3668">#3668</see>
    /// tracks recovering the depth across a conditional whose every branch is brace-balanced. When
    /// that lands this test should fail, and its assertions become the new behavior's.
    /// </para>
    /// <para>
    /// The corpus differential cannot cover this: <c>RoslynDeclarations</c> declines every file
    /// carrying a conditional directive, because Roslyn discards disabled branches while the index,
    /// being lexical, indexes them.
    /// </para>
    /// </summary>
    [Fact]
    public void AConditionalDirective_LosesEveryLaterRowToEndOfFile()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if DEBUG
                void Debug() { }
            #endif
                void Always() { }
            }
            """);

        // Every row, including the one no branch can reach.
        Assert.All(index.Declarations, s => Assert.False(s.SpanKnown));
        Assert.Contains(index.Declarations, s => s.Name == "Always");
        Assert.Null(index.FindByBodyLine(6));

        // The same class without the directive resolves, so the directive is the whole cause.
        var plain = DeclarationIndex.Build("""
            class C
            {
                void Always() { }
            }
            """);

        Assert.All(plain.Declarations, s => Assert.True(s.SpanKnown));
        Assert.Equal("Always", plain.FindByBodyLine(3)?.Name);
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
    private static List<Declaration>? RoslynDeclarations(string[] lines)
    {
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
        if (root.ContainsDirectives && root.DescendantTrivia(descendIntoTrivia: true).Any(t =>
                t.IsKind(SyntaxKind.IfDirectiveTrivia)
                || t.IsKind(SyntaxKind.ElifDirectiveTrivia)
                || t.IsKind(SyntaxKind.ElseDirectiveTrivia)))
            return null;

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
