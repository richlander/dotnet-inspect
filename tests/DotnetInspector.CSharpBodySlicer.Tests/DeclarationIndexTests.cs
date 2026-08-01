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

    private static string Diff(List<string> expected, List<string> actual)
    {
        var missing = expected.Except(actual, StringComparer.Ordinal).ToList();
        var extra = actual.Except(expected, StringComparer.Ordinal).ToList();
        if (missing.Count == 0 && extra.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var m in missing.Take(6))
            sb.Append("  Roslyn only: ").AppendLine(m);
        foreach (var e in extra.Take(6))
            sb.Append("  index only:  ").AppendLine(e);
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
        var text = string.Join("\n", lines);
        if (text.Contains("#if", StringComparison.Ordinal)
            || text.Contains("#else", StringComparison.Ordinal)
            || text.Contains("#elif", StringComparison.Ordinal))
            return null;

        var tree = CSharpSyntaxTree.ParseText(
            text, new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse));
        if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
            return null;

        var result = new List<Declaration>();
        Walk(tree.GetRoot(), result);
        return result;
    }

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
                    into.Add(Make(op, DeclarationKind.Method, "operator " + op.OperatorToken.ValueText));
                    continue;

                case ConversionOperatorDeclarationSyntax co:
                    into.Add(Make(co, DeclarationKind.Method, "operator " + co.ImplicitOrExplicitKeyword.ValueText));
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
    /// A file-scoped namespace runs to the end of the file, which is what the index reports too,
    /// but Roslyn's node ends at the last token rather than the last line.
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
