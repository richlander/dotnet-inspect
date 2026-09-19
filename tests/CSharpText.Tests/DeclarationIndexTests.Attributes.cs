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
}
