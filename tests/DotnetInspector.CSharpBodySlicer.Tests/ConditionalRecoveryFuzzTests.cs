using System;
using System.Collections.Generic;
using System.Linq;
using DotnetInspector.CSharpBodySlicer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DotnetInspector.CSharpBodySlicer.Tests;

// ---------------------------------------------------------------------------
// Conditional-recovery differential fuzzer for DeclarationIndex (PR #3680).
//
// It generates small C# files sprinkled with #if/#elif/#else/#endif groups,
// runs the product lexical index (DeclarationIndex.Build) once, and runs Roslyn
// as an independent oracle FOUR times -- with preprocessor symbols {}, {X},
// {Y}, {X,Y}. For every product row the index VOUCHES for (SpanKnown == true)
// it asks: does Roslyn place that same declaration on different lines (or give
// it different attribute lists) in two builds that both compile? If so the
// vouch is a lie -- a single physical-line range cannot describe a span that
// moves per build -- and the case is flagged.
//
// The oracle is deliberately the same one the product's own gate uses
// (DeclarationIndexTests.RoslynDeclarations / Make / TriviaStartLine): the
// product contract for TriviaStartLine, SignatureStartLine, EndLine and
// AttributeLists is DEFINED as "what Roslyn's parse tree reports for the
// compiled build", so Roslyn parse (frontend, symbol-aware) is the authority,
// not an argument.
//
// ----------------------------- "fair case" ---------------------------------
// This is the part a future reader most easily gets wrong, so read it before
// trusting a zero.
//
// A generated file is a FAIR CASE only when at least TWO of the four symbol
// configurations parse with no error-severity diagnostic. The differential
// question is "do two VALID builds disagree about a vouched row's lines?"; a
// file that compiles under fewer than two configurations offers no pair to
// compare, so it is generated, run, and then DISCARDED -- it is not counted as
// fair and can never flag. (Random generation produces many syntactically
// invalid files; excluding them is why `fair` is well below the case count.)
//
// Within a fair case, a vouched product row is compared to a Roslyn config
// ONLY when that config contains EXACTLY ONE declaration matching the row's
// (Kind, Name):
//   * zero matches  -> the declaration lives only in a branch this config
//                       dropped; there is nothing to compare, skip this config.
//   * two or more    -> the name is ambiguous in this build (the generator can
//                       emit the same name in two compiled branches); matching
//                       by name would invent a correspondence, so skip it.
// A row flags only when >= 2 configs each supply a unique match AND those
// matches disagree on (TriviaStartLine, SignatureStartLine, EndLine, attribute
// line ranges). Only SpanKnown == true rows are ever inspected: a row the index
// already declines is out of scope by construction -- the whole point of the
// feature is that the DECLINE is allowed to be broad, only the VOUCH must be
// exact.
//
// Consequently the fuzzer is sound in one direction only: every flag is a real
// over-vouch, but a clean run is evidence, not proof -- it cannot flag a defect
// the generator never spells (it emits no #line, no verbatim/interpolated
// literals, no nested-namespace crossings, etc.).
// ---------------------------------------------------------------------------

public class ConditionalRecoveryFuzzTests
{
    /// <summary>
    /// Seeds run in CI. Each is a full independent generation; the count is a CI-time budget, not
    /// a claim about coverage. The deep runs that justify the fix used 20,000 cases per seed --
    /// see eng/conditional-recovery-fuzz.cs, which is this same generator and oracle behind a
    /// command line, for reproducing a flag or sweeping a new seed.
    /// </summary>
    public static TheoryData<int> Seeds => new() { 1, 2, 777, 12345, 20240607 };

    /// <summary>
    /// The differential gate for conditional recovery. Every flag is a real over-vouch; a clean
    /// run is evidence, not proof, because the generator only spells the shapes it knows. It found
    /// the seventh and eighth ways a vouched span can be wrong, at 3,146 flags against the
    /// pre-fix build on seed 12345 alone (adversarial review round 6, Claude Opus 4.8).
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void NoVouchedRowMovesBetweenBuilds(int seed)
    {
        var (fair, flagged, report) = Run(seed, 4000);

        Assert.True(fair > 2000, $"only {fair} fair cases; the generator or the fair-case gate has drifted");
        Assert.True(flagged == 0, report);
    }

    /// <summary>
    /// Public so eng/conditional-recovery-fuzz.cs can drive deep runs without a second copy of
    /// the generator or the oracle. A harness that reimplemented either would stop testing this
    /// one.
    /// </summary>
    public static (int Fair, int Flagged, string Report) Run(int seed, int cases)
    {
        var rnd = new Random(seed);
        int tested = 0, fair = 0, flagged = 0;
        var reported = new List<string>();

        for (int iter = 0; iter < cases; iter++)
        {
            var src = Generate(rnd, iter);
            tested++;

            DeclarationIndex index;
            try { index = DeclarationIndex.Build(src); }
            catch { continue; }

            var configs = new Dictionary<string, List<Decl>?>();
            foreach (var syms in new[] { new string[0], new[] { "X" }, new[] { "Y" }, new[] { "X", "Y" } })
                configs[syms.Length == 0 ? "{}" : "{" + string.Join(",", syms) + "}"] = RoslynDeclarations(src, syms);

            // FAIR CASE gate: need two valid builds to compare (see header).
            if (configs.Values.Count(v => v != null) < 2)
                continue;
            fair++;

            foreach (var pr in index.Declarations.Where(d => d.SpanKnown))
            {
                var seen = new List<(string cfg, Decl d)>();
                foreach (var kv in configs)
                {
                    if (kv.Value == null) continue;
                    var m = kv.Value.Where(x => x.Kind == pr.Kind && x.Name == pr.Name).ToList();
                    if (m.Count == 1) seen.Add((kv.Key, m[0])); // unique match only
                }

                for (int a = 0; a < seen.Count; a++)
                for (int b = a + 1; b < seen.Count; b++)
                {
                    var (ca, da) = seen[a];
                    var (cb, db) = seen[b];
                    if (Same(da, db)) continue;

                    flagged++;
                    if (reported.Count < 40)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"===== FLAG (iter={iter}) {pr.Kind} \"{pr.Name}\" =====");
                        sb.AppendLine("SOURCE:");
                        var sl = src.Split('\n');
                        for (int li = 0; li < sl.Length; li++) sb.AppendLine($"  {li + 1,3}: {sl[li]}");
                        sb.AppendLine($"PRODUCT (SpanKnown=true): trivia={pr.TriviaStartLine} sig={pr.SignatureStartLine} end={pr.EndLine} attrs=[{AttrsL(pr.AttributeLists)}]");
                        sb.AppendLine($"ROSLYN {ca}: trivia={da.TriviaStartLine} sig={da.SignatureStartLine} end={da.EndLine} attrs=[{Attrs(da.AttributeLists)}]");
                        sb.AppendLine($"ROSLYN {cb}: trivia={db.TriviaStartLine} sig={db.SignatureStartLine} end={db.EndLine} attrs=[{Attrs(db.AttributeLists)}]");
                        sb.Append("  --> a single line range cannot describe both builds; the vouch is wrong.");
                        reported.Add(sb.ToString());
                    }
                }
            }
        }



        return (fair, flagged, $"seed={seed} tested={tested} fair={fair} flagged={flagged}"
            + Environment.NewLine + string.Join(Environment.NewLine, reported));
    }


    // ---------------------------------------------------------------------------
    // Generator. Emits a leading member (maybe), one to three conditional groups
    // each optionally with an #else and 0-2 members per branch, an optional member
    // after each #endif, and always a uniquely named trailing type -- the row the
    // recovery is meant to vouch for. Names carry a counter so a member outside a
    // group is unambiguous across builds; members inside two compiled branches can
    // still collide, which the unique-match rule above handles.
    // ---------------------------------------------------------------------------
    static string Generate(Random rnd, int iter)
    {
        int n = 0;
        string[] Pool()
        {
            int a = n++;
            return new[]
            {
                $"int f{a};",
                $"void m{a}() {{ }}",
                $"class t{a} {{ }}",
                $"struct u{a} {{ }}",
                $"int p{a} {{ get; set; }}",
                $"// doc{a}",
                $"/* c{a} */",
                "[System.Obsolete]",
                "[assembly: System.CLSCompliant(true)]",
                $"namespace ns{a};",
                $"int g{a}, h{a};",
                "public",
            };
        }

        var lines = new List<string>();
        var pool = Pool();
        int blocks = rnd.Next(1, 4);
        for (int bl = 0; bl < blocks; bl++)
        {
            if (rnd.Next(2) == 0) lines.Add(pool[rnd.Next(pool.Length)]);
            string sym = rnd.Next(2) == 0 ? "X" : "Y";
            lines.Add($"#if {sym}");
            for (int k = 0, kn = rnd.Next(0, 3); k < kn; k++) lines.Add(pool[rnd.Next(pool.Length)]);
            if (rnd.Next(2) == 0)
            {
                lines.Add("#else");
                for (int k = 0, kn = rnd.Next(0, 3); k < kn; k++) lines.Add(pool[rnd.Next(pool.Length)]);
            }
            lines.Add("#endif");
            if (rnd.Next(2) == 0) lines.Add(pool[rnd.Next(pool.Length)]);
            pool = Pool();
        }
        lines.Add($"class Tail{iter} {{ }}");
        return string.Join("\n", lines);
    }

    // ---------------------------------------------------------------------------
    // The Roslyn oracle -- a faithful copy of DeclarationIndexTests' Walk / Make /
    // TriviaStartLine / EndLine, parametrised by preprocessor symbols.
    // ---------------------------------------------------------------------------
    static List<Decl>? RoslynDeclarations(string src, string[] symbols)
    {
        var opts = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse)
            .WithPreprocessorSymbols(symbols);
        var tree = CSharpSyntaxTree.ParseText(src, opts);
        if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
            return null;
        var result = new List<Decl>();
        Walk(tree.GetRoot(), result);
        return result;
    }

    static void Walk(SyntaxNode node, List<Decl> into)
    {
        foreach (var child in node.ChildNodes())
        {
            switch (child)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    into.Add(Make(ns, DeclarationKind.Namespace, ns.Name.ToString().Replace("@", "")));
                    Walk(ns, into); continue;
                case TypeDeclarationSyntax type:
                    if (type.Identifier.ValueText.Length > 0)
                        into.Add(Make(type, TypeKind(type), type.Identifier.ValueText));
                    Walk(type, into); continue;
                case EnumDeclarationSyntax e:
                    into.Add(Make(e, DeclarationKind.Enum, e.Identifier.ValueText)); Walk(e, into); continue;
                case DelegateDeclarationSyntax d:
                    into.Add(Make(d, DeclarationKind.Delegate, d.Identifier.ValueText)); continue;
                case EnumMemberDeclarationSyntax em:
                    into.Add(Make(em, DeclarationKind.EnumMember, em.Identifier.ValueText)); continue;
                case MethodDeclarationSyntax m:
                    into.Add(Make(m, DeclarationKind.Method, m.Identifier.ValueText)); continue;
                case ConstructorDeclarationSyntax cc:
                    into.Add(Make(cc, DeclarationKind.Constructor, cc.Identifier.ValueText)); continue;
                case DestructorDeclarationSyntax dt:
                    into.Add(Make(dt, DeclarationKind.Destructor, "~" + dt.Identifier.ValueText)); continue;
                case OperatorDeclarationSyntax op:
                    into.Add(Make(op, DeclarationKind.Method, OperatorName(op.CheckedKeyword, op.OperatorToken))); continue;
                case ConversionOperatorDeclarationSyntax co:
                    into.Add(Make(co, DeclarationKind.Method, OperatorName(co.CheckedKeyword, co.ImplicitOrExplicitKeyword))); continue;
                case IndexerDeclarationSyntax ix:
                    into.Add(Make(ix, DeclarationKind.Property, "this")); continue;
                case PropertyDeclarationSyntax p:
                    into.Add(Make(p, DeclarationKind.Property, p.Identifier.ValueText)); continue;
                case EventDeclarationSyntax ev:
                    into.Add(Make(ev, DeclarationKind.Event, ev.Identifier.ValueText)); continue;
                case EventFieldDeclarationSyntax evf:
                    foreach (var v in evf.Declaration.Variables) into.Add(Make(evf, DeclarationKind.Event, v.Identifier.ValueText)); continue;
                case FieldDeclarationSyntax f:
                    foreach (var v in f.Declaration.Variables) into.Add(Make(f, DeclarationKind.Field, v.Identifier.ValueText)); continue;
            }
        }
    }

    static string OperatorName(SyntaxToken chk, SyntaxToken spelling) =>
        chk.IsKind(SyntaxKind.CheckedKeyword) ? "operator checked " + spelling.ValueText : "operator " + spelling.ValueText;

    static DeclarationKind TypeKind(TypeDeclarationSyntax type) => type switch
    {
        RecordDeclarationSyntax => DeclarationKind.Record,
        StructDeclarationSyntax => DeclarationKind.Struct,
        InterfaceDeclarationSyntax => DeclarationKind.Interface,
        _ => DeclarationKind.Class,
    };

    static Decl Make(SyntaxNode node, DeclarationKind kind, string name)
    {
        var attributes = node switch { MemberDeclarationSyntax m => m.AttributeLists, _ => default };
        var signatureStart = attributes.Count > 0 ? attributes.Last().GetLastToken().GetNextToken() : node.GetFirstToken();
        return new Decl(kind, name, TriviaStartLine(node), Line(node.SyntaxTree, signatureStart.SpanStart), EndLine(node))
        {
            AttributeLists = attributes.Select(a => (Line(node.SyntaxTree, a.SpanStart), Line(node.SyntaxTree, a.Span.End))).ToList(),
        };
    }

    static int TriviaStartLine(SyntaxNode node)
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
            if (line > previousEnd) return line;
        }
        return Line(node.SyntaxTree, node.SpanStart);
    }

    static int Line(SyntaxTree tree, int position) =>
        tree.GetLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(position, 0)).StartLinePosition.Line + 1;

    static int EndLine(SyntaxNode node) =>
        node.SyntaxTree.GetLineSpan(node.Span).EndLinePosition.Line + 1;

    static bool Same(Decl a, Decl b) =>
        a.TriviaStartLine == b.TriviaStartLine
        && a.SignatureStartLine == b.SignatureStartLine
        && a.EndLine == b.EndLine
        && Attrs(a.AttributeLists) == Attrs(b.AttributeLists);

    static string Attrs(IEnumerable<(int, int)> lists) => string.Join(",", lists.Select(l => $"{l.Item1}-{l.Item2}"));
    static string AttrsL(IEnumerable<LineRange> lists) => string.Join(",", lists.Select(l => $"{l.StartLine}-{l.EndLine}"));

    sealed record Decl(DeclarationKind Kind, string Name, int TriviaStartLine, int SignatureStartLine, int EndLine)
    {
        public IReadOnlyList<(int, int)> AttributeLists { get; init; } = new List<(int, int)>();
    }

}
