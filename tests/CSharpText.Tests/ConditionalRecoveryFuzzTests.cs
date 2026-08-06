using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CSharpText.Tests;

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
// question is "do two valid PARSES disagree about a vouched row's lines?"; a
// file that parses under fewer than two configurations offers no pair to
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
// matches disagree on the compared facts. Only SpanKnown == true rows are ever
// inspected: a row the index already declines is out of scope by construction --
// the whole point of the feature is that the DECLINE is allowed to be broad,
// only the VOUCH must be exact.
//
// --------------------------- widened in round 7 ----------------------------
// The round-6 generator emitted every group at FILE SCOPE. At file scope the
// only rows Roslyn's Walk and the product's Allowed both produce are types and
// namespaces, so in 140,000 clean cases it had never once compared a Method,
// Property, Field, Event, Constructor, EnumMember or Indexer row -- and
// EmitBodiless, the enum terminator paths and the initializer path are all
// member-only code. A clean run was therefore evidence about a third of the
// product. Round 7 (Claude Opus 5) built the widened generator adopted here and
// round 7 (Gemini 3.1 Pro) found the ninth way living in exactly that gap.
//
// It now also places groups inside type bodies, splits single declarations
// across group boundaries (base lists, parameter lists, constraints, accessor
// lists, declarator lists, initializer tails, terminators), nests groups,
// spells #elif chains and negated/compound conditions, hides directive text in
// verbatim and raw string literals, and compares SignatureEndLine and
// BodyStartLine -- both documented on DeclarationSpan as part of the span, and
// both absent from the round-6 comparison.
//
// Consequently the fuzzer is sound in one direction only: every flag is a real
// over-vouch, but a clean run is evidence, not proof -- it cannot flag a defect
// the generator never spells (it emits no #line and no #define/#undef).
//
// Seven consecutive rounds have now found a defect in the generator's REACH
// rather than in its case count, so treat that as the expected failure mode:
//   - round 7: every group sat at file scope, so no member row was ever
//     compared across 140,000 clean cases.
//   - round 8: Group spelled both branches from one body, so asymmetric
//     branches were unreachable at any case count; enums appeared only as
//     one-line declarations, so no enum interior was ever conditional.
//   - round 9: DifferFromProduct silently omitted SignatureEndLine and
//     BodyStartLine, so product mode vouched for fields it never read;
//     constructors were emitted only in forms that can never be vouched, and
//     so were never compared at all; and no composition of the pools could
//     place a bare ";" after a "}" that closed a type, which is where the
//     twelfth way lived.
//   - round 10: no case put a BRACE-LESS scope opener between a closed type
//     and a trailing ";", and cases 19-21 spelled only "class" though the rule
//     names six kinds. Both holes in the twelfth way's refusal set lived there.
//   - round 11: neither differential compared ParentIndex or Depth, so a vouched
//     row whose parent differed between builds while every line stayed fixed was
//     invisible by construction. That separate defect class is tracked in #3725.
//   - round 12: no case put a brace-bodied NON-TYPE member before a trailing
//     ";", and no case put a C# 14 extension block there. The fifteenth way and
//     the extension-block starting-point defect both lived in those gaps.
//   - round 13: no case wrapped top-level generation in a BLOCK namespace, so
//     TopPool's file-scoped namespace always landed at file scope and could
//     never strand a brace-less scope entry above a physical namespace "}".
// Before citing a clean run, read the cmp1/cmp2 buckets it printed and check
// that the rows you care about are actually in them. Note also that "fair"
// means "parses", not "compiles": the oracle reads parse diagnostics, so
// bind-time errors such as CS1520 still count as fair cases.
//
// And note which gate can see what. NoVouchedRowMovesBetweenBuilds needs TWO
// configurations that each supply a unique match, so a file that parses in only
// one configuration is invisible to it no matter how wrong the product is.
// Round 10's two defects were both that shape, and only the product-mode gate
// failed on them.
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
    /// pre-fix build on seed 12345 alone (adversarial review round 6, Claude Opus 4.8); widened in
    /// round 7, it flags the ninth way unaided and kills two guards in EmitBodiless that the
    /// round-6 generator could not reach at all; widened again in round 8 for asymmetric branches
    /// and conditional enum members, where it flags the tenth and eleventh ways.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void NoVouchedRowMovesBetweenBuilds(int seed)
    {
        var (fair, flagged, report) = Run(seed, 5000);

        Assert.True(fair > 2000, $"only {fair} fair cases; the generator or the fair-case gate has drifted");
        Assert.True(flagged == 0, report);
    }

    /// <summary>
    /// The companion gate, and a strictly different question.
    /// <see cref="NoVouchedRowMovesBetweenBuilds"/> compares the BUILDS against each other and so
    /// only ever asks whether a vouched row is ambiguous; it never reads the product's own line
    /// numbers. This asks the other half: that the numbers the product reports are the numbers
    /// Roslyn reports, for every vouched row, in every build where that row exists.
    ///
    /// Adversarial review round 8 (Gemini 3.1 Pro) proposed this to cover rows present in only one
    /// build, which the pairwise loop skips. Round 9 (Claude Opus 5) showed that rationale is
    /// wrong as stated and the set is empty by construction: a vouched row requires
    /// <c>pending.All(t =&gt; t.DepthKnown)</c>, so it lies outside every conditional group, so it
    /// exists in every configuration that parses. The <c>cmp1</c> and <c>cmp2</c> buckets confirm
    /// it — they are equal for every kind in every sweep.
    ///
    /// Round 10 then showed the instinct was right one level up: what the pairwise loop cannot see
    /// is not a row present in one BUILD but a file with only one parsing CONFIGURATION. That loop
    /// needs two configs each supplying a unique match, so a file whose other configs fail to parse
    /// is invisible to it however wrong the product is. Both defects round 10 found in the
    /// trailing-<c>;</c> refusal set are exactly that shape — <c>class Sr { }</c> then a group
    /// containing <c>namespace Nr;</c> then <c>;</c> parses only with the group dropped — and this
    /// gate is the only one of the two that fails on either. So the test now has three distinct
    /// justifications, and the round-8 note stated the weakest of them.
    ///
    /// Round 9 (GPT-5.6 Sol) also found this gate reading only four of the six span fields:
    /// <c>DifferFromProduct</c> omitted <c>SignatureEndLine</c> and <c>BodyStartLine</c>, so
    /// setting <c>BodyStartLine</c> to a constant left all five cases green. Both are compared
    /// now, and that mutation fails all five.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void EveryVouchedRowMatchesRoslynInEveryBuildItExistsIn(int seed)
    {
        var (fair, flagged, report) = Run(seed, 5000, "product");

        Assert.True(fair > 2000, $"only {fair} fair cases; the generator or the fair-case gate has drifted");
        Assert.True(flagged == 0, report);
    }

    /// <summary>
    /// Public so eng/conditional-recovery-fuzz.cs can drive deep runs without a second copy of
    /// the generator or the oracle. A harness that reimplemented either would stop testing this
    /// one. <paramref name="mode"/> selects the comparison: "diff" compares every fact,
    /// "legacy" drops SignatureEndLine and BodyStartLine (the round-6 comparison), and "product"
    /// checks the product's own numbers against each valid build rather than the builds against
    /// each other.
    /// </summary>
    public static (int Fair, int Flagged, string Report) Run(int seed, int cases, string mode = "diff")
    {
        bool legacy = mode == "legacy";
        bool product = mode == "product";
        var rnd = new Random(seed);
        int tested = 0, fair = 0, flagged = 0;
        var reported = new List<string>();
        var buckets = new Dictionary<string, int>();

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
                    if (m.Count == 1) seen.Add((kv.Key, m[0]));
                }

                for (int a = 0; a < seen.Count; a++)
                for (int b = a + 1; b < seen.Count; b++)
                {
                    var (ca, da) = seen[a];
                    var (cb, db) = seen[b];
                    string why = Differ(da, db, legacy);
                    if (why.Length == 0) continue;
                    flagged++;
                    Bump(buckets, "ORACLE/" + why);
                    Report(reported, iter, src, pr, ca, da, cb, db, "ORACLE-DISAGREE:" + why);
                }

                if (seen.Count > 0) Bump(buckets, "cmp1/" + pr.Kind);
                if (seen.Count > 1) Bump(buckets, "cmp2/" + pr.Kind);

                if (product && seen.Count > 0)
                {
                    // Every config that supplies a unique match agrees (otherwise it flagged
                    // above); the product must agree with them.
                    var (cfg, only) = seen[0];
                    string why = DifferFromProduct(pr, only);
                    if (why.Length > 0)
                    {
                        flagged++;
                        Bump(buckets, "PRODUCT/" + why);
                        Report(reported, iter, src, pr, cfg, only, cfg, only, "PRODUCT-DISAGREE:" + why);
                    }
                }
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(Environment.NewLine, reported));
        foreach (var kv in buckets.OrderByDescending(k => k.Value))
            sb.AppendLine($"  bucket {kv.Key}: {kv.Value}");
        sb.AppendLine($"seed={seed} tested={tested} fair={fair} flagged={flagged}");
        return (fair, flagged, sb.ToString());
    }

    static void Bump(Dictionary<string, int> b, string k) => b[k] = b.TryGetValue(k, out var v) ? v + 1 : 1;

    static void Report(List<string> reported, int iter, string src, DeclarationSpan pr,
        string ca, Decl da, string cb, Decl db, string why)
    {
        if (reported.Count >= 25) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"===== FLAG (iter={iter}) {pr.Kind} \"{pr.Name}\" [{why}] =====");
        var sl = src.Split('\n');
        for (int li = 0; li < sl.Length; li++) sb.AppendLine($"  {li + 1,3}: {sl[li]}");
        sb.AppendLine($"PRODUCT (SpanKnown=true): trivia={pr.TriviaStartLine} sig={pr.SignatureStartLine} sigEnd={pr.SignatureEndLine} body={pr.BodyStartLine} end={pr.EndLine} attrs=[{AttrsL(pr.AttributeLists)}]");
        sb.AppendLine($"ROSLYN {ca}: trivia={da.TriviaStartLine} sig={da.SignatureStartLine} sigEnd={da.SignatureEndLine} body={da.BodyStartLine} end={da.EndLine} attrs=[{Attrs(da.AttributeLists)}]");
        sb.AppendLine($"ROSLYN {cb}: trivia={db.TriviaStartLine} sig={db.SignatureStartLine} sigEnd={db.SignatureEndLine} body={db.BodyStartLine} end={db.EndLine} attrs=[{Attrs(db.AttributeLists)}]");
        reported.Add(sb.ToString());
    }

    static string Differ(Decl a, Decl b, bool legacy)
    {
        var w = new List<string>();
        if (a.TriviaStartLine != b.TriviaStartLine) w.Add("trivia");
        if (a.SignatureStartLine != b.SignatureStartLine) w.Add("sig");
        if (a.EndLine != b.EndLine) w.Add("end");
        if (Attrs(a.AttributeLists) != Attrs(b.AttributeLists)) w.Add("attrs");
        if (!legacy)
        {
            if (a.SignatureEndLine != b.SignatureEndLine) w.Add("sigEnd");
            if (a.BodyStartLine != b.BodyStartLine) w.Add("body");
        }
        return string.Join("+", w);
    }

    static string DifferFromProduct(DeclarationSpan p, Decl o)
    {
        var w = new List<string>();
        if (p.TriviaStartLine != o.TriviaStartLine) w.Add("trivia");
        if (p.SignatureStartLine != o.SignatureStartLine) w.Add("sig");
        if (p.EndLine != o.EndLine) w.Add("end");
        if (p.SignatureEndLine != o.SignatureEndLine) w.Add("sigEnd");
        if (p.BodyStartLine != o.BodyStartLine) w.Add("body");
        if (AttrsL(p.AttributeLists) != Attrs(o.AttributeLists)) w.Add("attrs");
        return string.Join("+", w);
    }

    // -----------------------------------------------------------------------
    // Widened generator.
    // -----------------------------------------------------------------------
    static int n;

    // The type MemberPool is generating members for, or null at file scope. A constructor is only
    // a constructor if its name matches its enclosing type: the product classifies a mismatched
    // one as a Method while Roslyn's parser still calls it a ConstructorDeclaration, so the two
    // never match by (Kind, Name) and the row is silently never compared. That is why naming these
    // "Ctor{a}" produced zero Constructor comparisons.
    static string? curType;

    static string[] MemberPool()
    {
        int a = n++;
        return new[]
        {
            $"int f{a};",
            $"int g{a}, h{a};",
            $"void m{a}() {{ }}",
            $"void e{a}() => M{a}();",
            $"int p{a} {{ get; set; }}",
            $"int q{a} => 1;",
            $"class t{a} {{ }}",
            $"struct u{a} {{ }}",
            $"record r{a}(int V);",
            $"enum n{a} {{ A{a}, B{a} }}",
            $"interface i{a} {{ }}",
            $"event System.Action ev{a};",
            $"public int c{a} {{ get {{ return 1; }} }}",
            // A constructor and a destructor that no group splits. EmitSplit case 9 already emits
            // a constructor, but always with its initializer inside a conditional, so it is never
            // vouched and therefore never compared -- a 50,000-case sweep covered zero of either
            // kind (adversarial review round 9, GPT-5.6 Sol). The constructor has to carry the
            // enclosing type's name to be classified as one by both sides; see curType.
            curType is null ? $"void mc{a}() {{ }}" : $"public {curType}() {{ }}",
            $"~Dtor{a}() {{ }}",
            // (W3b) A bare initializer continuation. A property whose ACCESSOR BLOCK closed
            // inside a conditional group, followed by "= v;" after the #endif, makes which
            // declaration the initializer binds to branch-dependent while the ";" itself is
            // outside the group and reads as known.
            "= 5;",
            $"int P{a} {{ get; }}",
            $"// doc{a}",
            $"/* c{a} */",
            "[System.Obsolete]",
            "[return: System.Obsolete]",
            "[field: System.Obsolete]",
            "[method: System.Obsolete]",
            "public",
            "static",
            $"string s{a} = @\"#endif\";",
            $"string v{a} = \"\"\"" + "\n#if X\n" + "\"\"\";",
            $"int k{a} = 1; // {{",
            $"void L{a}() {{ void Inner{a}() {{ }} Inner{a}(); }}",
            $"public T{a} Gen{a}<T{a}>(T{a} x) where T{a} : struct {{ return x; }}",
            "#region R",
            "#endregion",
        };
    }

    static string[] TopPool()
    {
        int a = n++;
        return new[]
        {
            $"class t{a} {{ }}",
            $"struct u{a} {{ }}",
            $"partial class w{a} {{ }}",
            $"record r{a}(int V);",
            $"class gg{a}<T> {{ }}",
            $"enum n{a} {{ A{a} }}",
            $"delegate void d{a}();",
            $"// doc{a}",
            $"/* c{a} */",
            "[System.Obsolete]",
            "[assembly: System.CLSCompliant(true)]",
            "[module: System.CLSCompliant(true)]",
            $"namespace ns{a};",
            $"namespace nb{a} {{ }}",
            "using System;",
            "public",
            "#region R",
            "#endregion",
            $"int f{a};",
            $"int g{a}, h{a};",
        };
    }

    static string Cond(Random rnd)
    {
        switch (rnd.Next(6))
        {
            case 0: return "X";
            case 1: return "Y";
            case 2: return "!X";
            case 3: return "X && Y";
            case 4: return "X || Y";
            default: return "!Y";
        }
    }

    static void EmitGroup(Random rnd, List<string> lines, Func<string[]> pool, int depth)
    {
        lines.Add($"#if {Cond(rnd)}");
        EmitBranch(rnd, lines, pool, depth);
        for (int e = 0, en = rnd.Next(0, 3); e < en; e++)
        {
            lines.Add($"#elif {Cond(rnd)}");
            EmitBranch(rnd, lines, pool, depth);
        }
        if (rnd.Next(2) == 0)
        {
            lines.Add("#else");
            EmitBranch(rnd, lines, pool, depth);
        }
        lines.Add("#endif");
    }

    static void EmitBranch(Random rnd, List<string> lines, Func<string[]> pool, int depth)
    {
        var p = pool();
        for (int k = 0, kn = rnd.Next(0, 3); k < kn; k++)
        {
            if (depth < 2 && rnd.Next(6) == 0)
                EmitGroup(rnd, lines, pool, depth + 1);
            else
                foreach (var line in p[rnd.Next(p.Length)].Split('\n'))
                    lines.Add(line);
        }
    }

    static string Generate(Random rnd, int iter)
    {
        n = 0;
        var lines = new List<string>();
        bool inType = rnd.Next(2) == 0;
        curType = inType ? $"Outer{iter}" : null;

        // Round 13: without block-namespace wrappers, TopPool's file-scoped namespace always
        // lands at file scope. That leaves no enclosing physical "}" for its brace-less scope
        // entry to steal, so the seventeenth way was unreachable at every seed and case count.
        int namespaceWrappers = inType ? 0 : rnd.Next(3);
        for (int w = 0; w < namespaceWrappers; w++)
        {
            lines.Add($"namespace NsW{iter}_{w}");
            lines.Add("{");
        }

        if (inType)
            lines.Add($"class Outer{iter}");
        if (inType)
            lines.Add("{");

        var pool = inType ? (Func<string[]>)MemberPool : TopPool;
        int blocks = rnd.Next(1, 4);
        for (int bl = 0; bl < blocks; bl++)
        {
            var p = pool();
            if (rnd.Next(2) == 0)
                foreach (var line in p[rnd.Next(p.Length)].Split('\n')) lines.Add(line);
            if (rnd.Next(3) == 0)
                EmitSplit(rnd, lines, inType);
            else
                EmitGroup(rnd, lines, pool, 0);
            if (rnd.Next(2) == 0)
            {
                p = pool();
                foreach (var line in p[rnd.Next(p.Length)].Split('\n')) lines.Add(line);
            }
        }

        if (inType)
        {
            lines.Add($"    void Tail{iter}() {{ }}");
            lines.Add("}");
        }
        else
        {
            lines.Add($"class Tail{iter} {{ }}");
            for (int w = namespaceWrappers - 1; w >= 0; w--)
                lines.Add("}");
        }
        return string.Join("\n", lines);
    }

    // (W3c) A declaration whose own text is SPLIT BY a conditional group: a base list, a
    // parameter list, a generic constraint, an initializer, an enum member list, an attribute
    // list or an accessor list with the group in the middle. The shipped generator emits whole
    // pool lines only, so no group it produces can ever fall inside a declaration.
    static void EmitSplit(Random rnd, List<string> lines, bool inType)
    {
        int a = n++;
        string sym = Cond(rnd);
        void Group(params string[] body)
        {
            lines.Add($"#if {sym}");
            foreach (var b in body) lines.Add(b);
            if (rnd.Next(2) == 0)
            {
                lines.Add("#else");
                foreach (var b in body) lines.Add(b.Replace("__", "Else"));
            }
            lines.Add("#endif");
        }

        // Every shape above pairs a branch with ITSELF: Group spells one body and, when it adds an
        // "#else", spells the same body again with the names changed. That symmetry is a structural
        // limit, not a stylistic one -- the shapes where the two branches carry DIFFERENT grammar
        // are unreachable, and adversarial review round 8 (GPT-5.6 Sol) found the tenth way living
        // in exactly that gap. Group2 spells the branches independently so they can.
        void Group2(string[] then, string[] els)
        {
            lines.Add($"#if {sym}");
            foreach (var b in then) lines.Add(b);
            lines.Add("#else");
            foreach (var b in els) lines.Add(b);
            lines.Add("#endif");
        }

        // Cases 16-26 are enum and type shapes, all legal at both scopes, so the non-member pick
        // is remapped onto them rather than leaving them reachable only inside a type. Cases
        // 27-28 are member-only. Case 29 contains its own static class because an extension block
        // has stricter ownership rules, so it is selected only at file scope.
        int pick = rnd.Next(inType ? 30 : 16);
        if (!inType)
            pick = pick switch
            {
                >= 4 and <= 14 => pick + 12,
                15 => 29,
                _ => pick,
            };
        switch (pick)
        {
            case 0:
                lines.Add($"class Sa{a}");
                Group("    : System.IDisposable");
                lines.Add("{");
                lines.Add("    public void Dispose() { }");
                lines.Add("}");
                return;
            case 1:
                lines.Add("[System.Obsolete]");
                Group($"    class Mid{a} {{ }}");
                lines.Add($"class Sd{a} {{ }}");
                return;
            case 2:
                lines.Add($"enum Ef{a}");
                lines.Add("{");
                lines.Add($"    A{a}");
                Group($"    , B{a}");
                lines.Add("}");
                return;
            case 3:
                lines.Add($"class Sg{a}<T>");
                Group("    where T : struct");
                lines.Add("{");
                lines.Add("}");
                return;
            case 4:
                lines.Add($"    void Mc{a}(");
                Group("        int x");
                lines.Add("    ) { }");
                return;
            case 5:
                lines.Add($"    int Pb{a} {{ get; }}");
                Group($"    int Pc{a} {{ get; }}");
                lines.Add("    = 5;");
                return;
            case 6:
                lines.Add($"    int Pd{a}");
                Group("        // note");
                lines.Add("    { get; set; }");
                return;
            case 7:
                lines.Add($"    event System.Action Ev{a}");
                Group("        // note");
                lines.Add("    { add { } remove { } }");
                return;
            case 8:
                lines.Add($"    int Fa{a}");
                Group($"        , Fb{a}");
                lines.Add("    ;");
                return;
            case 9:
                lines.Add($"    public Outer{a}(int q)");
                Group("        : this()");
                lines.Add("    { }");
                lines.Add($"    public Outer{a}() {{ }}");
                return;
            case 10:
                lines.Add($"    int this[int i{a}]");
                Group("        // note");
                lines.Add("    { get { return 1; } }");
                return;
            case 11:
                lines.Add($"    void Mb{a}() {{");
                Group($"        int loc{a} = 1;");
                lines.Add("    }");
                return;
            case 12:
                // The TERMINATOR itself inside the group, both branches spelling it, header
                // outside. Balanced, both builds parse, and the ";" the row is measured at is
                // branch-dependent.
                lines.Add($"    int Fs{a} = 1");
                lines.Add($"#if {sym}");
                lines.Add("    ;");
                lines.Add("#else");
                lines.Add("    ;");
                lines.Add("#endif");
                return;
            case 13:
                // The tenth way: one branch carries a complete declaration whose "=" is ordinary,
                // the other carries a bare tail that binds to the row ABOVE the group. The two "="
                // tokens are what makes it interesting -- the first one masks the second.
                lines.Add($"    int Pt{a} {{ get; }}");
                Group2([$"    int Qt{a} = 0"], ["    = 1"]);
                lines.Add("    ;");
                return;
            case 14:
                // Same masking, but the reaching-back tail comes FIRST, so a search that takes the
                // last qualifying "=" rather than the first is wrong in the mirror direction.
                lines.Add($"    int Pu{a} {{ get; }}");
                Group2(["    = 1"], [$"    int Qu{a} = 0"]);
                lines.Add("    ;");
                return;
            case 15:
                // Asymmetric branches that are NOT an initializer at all: a declaration on one
                // side against a bare continuation of the header on the other.
                lines.Add($"    int Pv{a}");
                Group2([$"    {{ get; }} int Qv{a} {{ get; }}"], ["    { get; }"]);
                return;
            case 16:
                // The eleventh way: an enum member's initializer reaching back through a group.
                // An enum member is terminated by "," or "}", never ";", so this shape cannot be
                // reached through the field and property paths above no matter how they are
                // widened. Here the enum's closing brace is the terminator.
                lines.Add($"enum Ea{a} {{");
                lines.Add($"    Am{a}");
                Group($"    , Bm{a}");
                lines.Add("    = 1");
                lines.Add("}");
                return;
            case 17:
                // The same, terminated by the comma before a following member rather than by the
                // enum's closing brace, so both enum emit sites are exercised.
                lines.Add($"enum Eb{a} {{");
                lines.Add($"    An{a}");
                Group($"    , Bn{a}");
                lines.Add($"    = 1, Cn{a}");
                lines.Add("}");
                return;
            case 18:
                // The benign counterpart: a conditional member and its comma, but no initializer
                // reaching back. Every build ends the first member on the same line, so a rule
                // that refused on the conditional comma alone would show up here as lost recall
                // rather than as a flag.
                lines.Add($"enum Ec{a} {{");
                lines.Add($"    Ao{a}");
                Group($"    , Bo{a}");
                lines.Add("}");
                return;
            case 19:
                // The twelfth way: a type's optional trailing ";" claimed by a conditional
                // neighbour. No composition of the pools could place a bare ";" after a "}" that
                // closed a TYPE -- every ";" the generator emits terminates a field whose
                // declarator text precedes it -- so this shape was unreachable at any seed and any
                // case count (adversarial review round 9, Claude Opus 5).
                lines.Add($"class Sy{a}");
                lines.Add("{");
                lines.Add("}");
                Group($"class Sz{a} {{ }}");
                lines.Add(";");
                return;
            case 20:
                // The symmetric form, where the vouched answer matches neither build.
                lines.Add($"class Sw{a} {{ }}");
                Group2(["    ;"], ["    ;"]);
                return;
            case 21:
                // The benign counterpart: a trailing ";" with no group between it and the type it
                // belongs to. It must stay vouched, and its span must include the ";".
                lines.Add($"class Sv{a} {{ }}");
                lines.Add(";");
                return;
            case 22:
                // The thirteenth way, which SHIELDS the twelfth: a declaration written in a branch
                // this build discards is still pending when the ";" arrives, so the trailing-";"
                // rule -- which requires an empty pending run -- never fires, and the type before
                // the group keeps a vouch that is wrong in the build without the branch. Case 19
                // could not reach it: it emits a COMPLETE declaration in the group, which consumes
                // its own terminator (adversarial review round 10, Gemini 3.1 Pro).
                lines.Add($"class Su{a} {{ }}");
                Group($"    int Fu{a} = 1");
                lines.Add(";");
                return;
            case 23:
                // The same shield spelled with a delegate, whose terminator is mandatory for a
                // different reason than a field's, and which reaches the ";" through a different
                // classification path.
                lines.Add($"class St{a} {{ }}");
                Group($"    delegate void Dt{a}()");
                lines.Add(";");
                return;
            case 24:
                // The refusal set for the twelfth way was too narrow: a file-scoped namespace
                // inside the group opens a scope with no brace, re-parenting the row the ";"
                // appears to follow, so the row it can actually reach at the outer scope was never
                // visited. Nothing else in the generator puts a brace-less scope opener between a
                // closed type and a trailing ";" (adversarial review round 10, Claude Opus 5).
                lines.Add($"class Sr{a} {{ }}");
                Group($"namespace Nr{a};");
                lines.Add(";");
                return;
            case 25:
                // Cases 19-24 spell only "class", yet the trailing-";" rule names Struct,
                // Interface, Record, Enum and Namespace as well. Rotate the kind so the list is
                // exercised rather than assumed (adversarial review round 10, Claude Opus 5).
                lines.Add((a % 4) switch
                {
                    0 => $"struct Sp{a} {{ }}",
                    1 => $"interface Sn{a} {{ }}",
                    2 => $"enum Sm{a} {{ Ea{a} }}",
                    _ => $"record Sl{a} {{ }}",
                });
                Group($"int Fl{a} = 1");
                lines.Add(";");
                return;
            case 26:
                // A delegate split by a group. No pool entry and no split case ever put one in a
                // conditional position, and Classify has a dedicated delegate path (adversarial
                // review round 10, Claude Opus 5).
                lines.Add($"delegate void Dk{a}(");
                Group($"    int pk{a}");
                lines.Add(");");
                return;
            case 27:
                // The fifteenth way: a brace-bodied NON-TYPE member and a pending declaration
                // mask every trailing-";" rule at once. Before round 12, cases 19-25 placed only a
                // type, field, delegate or brace-less namespace before the ";", so no case count
                // could make lastClosed name Method/Property/Event/Constructor/Destructor while
                // a second declaration was pending (adversarial review round 12, Claude Opus 5).
                lines.Add($"    class Sh{a} {{ }}");
                Group($"    void Mh{a}() {{ }}", $"    int Fh{a} = 1");
                lines.Add("    ;");
                return;
            case 28:
                // The same kind-test hole without a pending run. Only the build that drops the
                // method parses, so the product gate is the one that can compare the vouched row
                // against Roslyn; this keeps the empty-run half independently reachable.
                lines.Add($"    class Si{a} {{ }}");
                Group($"    void Mi{a}() {{ }}");
                lines.Add("    ;");
                return;
            case 29:
                // A C# 14 extension block is transparent for parenting, but before round 12 its
                // carried parent index made the closing brace look as if it closed the enclosing
                // class. The trailing-";" refusal then started one scope too high and refused the
                // class's siblings rather than the earlier nested type it could reach.
                lines.Add($"static class Ce{a}");
                lines.Add("{");
                lines.Add($"    class Sj{a} {{ }}");
                Group($"    extension(int x{a}) {{ }}");
                lines.Add("    ;");
                lines.Add("}");
                return;
            default:
                lines.Add($"    [System.Obsolete]");
                Group("    [System.CLSCompliant(true)]");
                lines.Add($"    void Ma{a}() {{ }}");
                return;
        }
    }

    // -----------------------------------------------------------------------
    // Oracle.
    // -----------------------------------------------------------------------
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
        var (sigEnd, bodyStart) = Shape(node);
        return new Decl(kind, name, TriviaStartLine(node), Line(node.SyntaxTree, signatureStart.SpanStart), EndLine(node))
        {
            AttributeLists = attributes.Select(a => (Line(node.SyntaxTree, a.SpanStart), Line(node.SyntaxTree, a.Span.End))).ToList(),
            SignatureEndLine = sigEnd,
            BodyStartLine = bodyStart,
        };
    }

    // (W1) The line the signature ends on and the line the body opens on, computed from the
    // parse tree. Only ever compared oracle-to-oracle, so the convention need only be
    // deterministic; it does not have to reproduce the product's spelling exactly.
    static (int SigEnd, int BodyStart) Shape(SyntaxNode node)
    {
        var tree = node.SyntaxTree;
        int L(SyntaxToken t) => t == default ? -1 : Line(tree, t.SpanStart);

        switch (node)
        {
            case BaseMethodDeclarationSyntax m:
                if (m.Body is not null) return (L(m.Body.OpenBraceToken), L(m.Body.OpenBraceToken));
                if (m.ExpressionBody is not null) return (L(m.SemicolonToken), L(m.ExpressionBody.ArrowToken));
                return (L(m.SemicolonToken), -1);
            case PropertyDeclarationSyntax p:
                if (p.AccessorList is not null) return (L(p.AccessorList.OpenBraceToken), L(p.AccessorList.OpenBraceToken));
                if (p.ExpressionBody is not null) return (L(p.SemicolonToken), L(p.ExpressionBody.ArrowToken));
                return (L(p.SemicolonToken), -1);
            case IndexerDeclarationSyntax ix:
                if (ix.AccessorList is not null) return (L(ix.AccessorList.OpenBraceToken), L(ix.AccessorList.OpenBraceToken));
                if (ix.ExpressionBody is not null) return (L(ix.SemicolonToken), L(ix.ExpressionBody.ArrowToken));
                return (L(ix.SemicolonToken), -1);
            case EventDeclarationSyntax ev:
                return ev.AccessorList is not null
                    ? (L(ev.AccessorList.OpenBraceToken), L(ev.AccessorList.OpenBraceToken))
                    : (L(ev.SemicolonToken), -1);
            case BaseFieldDeclarationSyntax f:
                return (L(f.SemicolonToken), -1);
            case DelegateDeclarationSyntax d:
                return (L(d.SemicolonToken), -1);
            case BaseTypeDeclarationSyntax t:
                return t.OpenBraceToken != default
                    ? (L(t.OpenBraceToken), L(t.OpenBraceToken))
                    : (L(t.SemicolonToken), -1);
            case FileScopedNamespaceDeclarationSyntax fns:
                return (L(fns.SemicolonToken), -1);
            case NamespaceDeclarationSyntax bns:
                return (L(bns.OpenBraceToken), L(bns.OpenBraceToken));
            case EnumMemberDeclarationSyntax em:
                return (Line(tree, em.Span.End), -1);
            default:
                return (-1, -1);
        }
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

    static string Attrs(IEnumerable<(int, int)> lists) => string.Join(",", lists.Select(l => $"{l.Item1}-{l.Item2}"));
    static string AttrsL(IEnumerable<LineRange> lists) => string.Join(",", lists.Select(l => $"{l.StartLine}-{l.EndLine}"));

    public sealed record Decl(DeclarationKind Kind, string Name, int TriviaStartLine, int SignatureStartLine, int EndLine)
    {
        public IReadOnlyList<(int, int)> AttributeLists { get; init; } = new List<(int, int)>();
        public int SignatureEndLine { get; init; } = -1;
        public int BodyStartLine { get; init; } = -1;
    }
}
