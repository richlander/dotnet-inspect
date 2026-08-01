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
        DeclarationKind Kind, string Name, int TriviaStartLine, int SignatureStartLine, int EndLine);

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
                    into.Add(Make(ns, DeclarationKind.Namespace, ns.Name.ToString()));
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
            EndLine(node));
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
