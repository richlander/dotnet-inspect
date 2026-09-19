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
    private static string Format(Declaration d) =>
        $"{d.Kind} {d.Name} {d.SignatureStartLine}:{d.SignatureStartColumn}-{d.EndLine}";

    private static string Format(DeclarationSpan s) =>
        $"{s.Kind} {s.Name} {s.SignatureStartLine}:{s.SignatureStartColumn}-{s.EndLine}";

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
        DeclarationKind Kind,
        string Name,
        int TriviaStartLine,
        int SignatureStartLine,
        int SignatureStartColumn,
        int EndLine)
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

    private static DeclarationKind TypeKind(TypeDeclarationSyntax type)
    {
        // UnionDeclarationSyntax is experimental in Roslyn 5.9; the stable token
        // text keeps this compiler oracle warning-free.
        if (type.Keyword.ValueText == "union")
            return DeclarationKind.Struct;

        return type switch
        {
            RecordDeclarationSyntax => DeclarationKind.Record,
            StructDeclarationSyntax => DeclarationKind.Struct,
            InterfaceDeclarationSyntax => DeclarationKind.Interface,
            _ => DeclarationKind.Class,
        };
    }

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
            Column(node.SyntaxTree, signatureStart.SpanStart),
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

    private static int Column(SyntaxTree tree, int position) =>
        tree.GetLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(position, 0)).StartLinePosition.Character;

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
    /// <see cref="Corpus"/> and has to be, because this repository has almost none. The owned
    /// fixture is in this test binary's dependency closure, but the broad population is not, so
    /// the PDB-discovered corpus is too small to gate the recovery behavior on its own.
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
                List<PdbMemberDocumentInfo> members;
                try
                {
                    members = context.EnumerateMemberDocuments().ToList();
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
