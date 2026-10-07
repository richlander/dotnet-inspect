using System.Collections.Immutable;
using System.Reflection;

using ILInspector.CSharp;
using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "RoundTrip")]
public class SpanAttributionTests
{
    static ImmutableArray<Diagnostic> Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "span-attribution-test",
            [tree],
            tpa,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetDiagnostics();
    }

    // Stands in for the product-issued body range: the block that follows the
    // given member head. RTS receives these ranges from CSharpSourceArtifact
    // (ReplaceableBodyRange and CSharpBodyReplacement.BodyRange).
    static CSharpSourceRange BodyAfter(string source, string memberHead, int occurrence = 0)
    {
        int head = -1;
        for (int i = 0; i <= occurrence; i++)
            head = source.IndexOf(memberHead, head + 1, StringComparison.Ordinal);
        Assert.True(head >= 0, $"member head '{memberHead}' not found");
        int open = source.IndexOf('{', head);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return new CSharpSourceRange(open, i - open + 1);
        }

        throw new InvalidOperationException("unbalanced body");
    }

    static bool Isolated(string decompiled, string authored, string memberHead = "int M()", int occurrence = 0)
        => SpanAttribution.IsolatingBodyError(
            decompiled,
            BodyAfter(decompiled, memberHead, occurrence),
            Compile(decompiled),
            BodyAfter(authored, memberHead, occurrence),
            Compile(authored)) is not null;

    [Fact]
    public void DecompiledBodyIsolated_TrueWhenDecompiledBodyHasSyntaxError()
    {
        // Broken shell (undefined Shell symbol outside every body) makes both
        // compiles fail, but the decompiled body carries a SYNTAX error — the
        // decompiler emitted body text that does not parse, which no shell state
        // can cause. This is a sound, shell-independent attribution.
        const string decompiled = """
            class C
            {
                int M() { int x = ; return 0; }
                int Filler = Shell.Broken;
            }
            """;
        const string authored = """
            class C
            {
                int M() { return 42; }
                int Filler = Shell.Broken;
            }
            """;

        bool isolated = Isolated(decompiled, authored);

        Assert.True(isolated);
    }

    [Fact]
    public void DecompiledBodyIsolated_TrueWhenDecompiledBodyHasIntrinsicSemanticError()
    {
        // CS0128 (duplicate local declaration) requires two local declarations
        // sharing a name inside the body — no shell member, type, or reference
        // can create it — so it is a sound attribution even under a broken shell.
        const string decompiled = """
            class C
            {
                int M() { int x = 1; int x = 2; return x; }
                int Filler = Shell.Broken;
            }
            """;
        const string authored = """
            class C
            {
                int M() { return 42; }
                int Filler = Shell.Broken;
            }
            """;

        bool isolated = Isolated(decompiled, authored);

        Assert.True(isolated);
    }

    [Fact]
    public void DecompiledBodyIsolated_FalseWhenDecompiledBodyHasUnassignedLocalError()
    {
        // Close negative (PR #3231 adversarial review). CS0165 (use of unassigned
        // local) can be induced by a shell-reconstruction miss of a compile-time
        // const that drives definite assignment (e.g. `if (Const) x = 1;` where
        // the shell dropped `const`), so it is NOT shell-independent and must be
        // declined even though the authored body is clean.
        const string decompiled = """
            class C
            {
                int M() { int x; if (Always) x = 1; return x; }
                int Filler = Shell.Broken;
            }
            """;
        const string authored = """
            class C
            {
                int M() { return 42; }
                int Filler = Shell.Broken;
            }
            """;

        bool isolated = Isolated(decompiled, authored);

        Assert.False(isolated);
    }

    [Fact]
    public void DecompiledBodyIsolated_FalseWhenDecompiledBodyHasOnlyResolutionError()
    {
        // Close negative (PR #3231 adversarial review). A broken shell reconstructor
        // that fails to synthesize a compiler-generated member the decompiled body
        // references produces an in-body CS0103/CS0246 identical to a real body
        // defect. The authored body uses high-level syntax and never names that
        // member, so it stays clean. Crediting this would break the lower-bound
        // guarantee, so the sound rule must DECLINE it.
        const string decompiled = """
            class C
            {
                int M() { return undefinedInBody; }
                int Filler = Shell.Broken;
            }
            """;
        const string authored = """
            class C
            {
                int M() { return 42; }
                int Filler = Shell.Broken;
            }
            """;

        bool isolated = Isolated(decompiled, authored);

        Assert.False(isolated);
    }

    [Fact]
    public void DecompiledBodyIsolated_FalseWhenBothBodiesShareCascadeError()
    {
        // Same missing symbol used inside both bodies: the error appears in both
        // body spans and cancels, so this stays a shell/closure defect.
        const string decompiled = """
            class C
            {
                int M() { return Shell.Missing; }
            }
            """;
        const string authored = """
            class C
            {
                int M() { return Shell.Missing; }
            }
            """;

        bool isolated = Isolated(decompiled, authored);

        Assert.False(isolated);
    }

    [Fact]
    public void DecompiledBodyIsolated_FalseWhenAuthoredBodyAlsoErrors()
    {
        const string decompiled = """
            class C
            {
                int M() { return undefinedA; }
            }
            """;
        const string authored = """
            class C
            {
                int M() { return undefinedB; }
            }
            """;

        bool isolated = Isolated(decompiled, authored);

        Assert.False(isolated);
    }

    [Fact]
    public void DecompiledBodyIsolated_UsesTheProductRangeAcrossSameNamedMembers()
    {
        // Two types named C each declare M(). A name-based search cannot tell
        // them apart; the product range names the target exactly. The syntax
        // error in the second C.M is credited, and the resolution error in the
        // first C.M (outside the range) is not considered.
        const string decompiled = """
            namespace A { class C { int M() { return undefinedInBody; } } }
            namespace B { class C { int M() { int x = ; return 0; } } }
            """;
        const string authored = """
            namespace A { class C { int M() { return 1; } } }
            namespace B { class C { int M() { return 2; } } }
            """;

        Assert.True(Isolated(decompiled, authored, occurrence: 1));
        Assert.False(Isolated(decompiled, authored, occurrence: 0));
    }

    [Fact]
    public void DecompiledBodyIsolated_IgnoresErrorsOutsideTheProductRange()
    {
        // A syntax error in a sibling member is outside the target's product
        // range, so it is never credited to the target body.
        const string decompiled = """
            class C
            {
                int M() { return 0; }
                int N() { int y = ; return y; }
                int Filler = Shell.Broken;
            }
            """;
        const string authored = """
            class C
            {
                int M() { return 42; }
                int N() { return 1; }
                int Filler = Shell.Broken;
            }
            """;

        Assert.False(Isolated(decompiled, authored));
    }

    // The allowlist that each methodology version is defined by. A version's entry is
    // historical once stamped: rows carrying that stamp were produced by exactly this set,
    // so an entry may never be edited — only a new version added.
    static readonly ImmutableDictionary<int, ImmutableHashSet<string>> AllowlistByMethodologyVersion =
        ImmutableDictionary.CreateRange(
        [
            // v2: syntax errors in the body span, plus duplicate-local only. Every
            // context-dependent class (resolution, conversion, overload, scope collision)
            // is excluded because a broken shell reconstructs them identically to a real
            // body defect — see SpanAttribution.IsolatingBodyError.
            KeyValuePair.Create(2, ImmutableHashSet.Create(StringComparer.Ordinal, "CS0128")),
            // v3 adds the ValidDifferent fidelity control without changing the
            // invalid-row span rule, so it inherits the exact v2 allow list.
            KeyValuePair.Create(3, ImmutableHashSet.Create(StringComparer.Ordinal, "CS0128")),
            // v4 changes source-outcome admission and compilation context without
            // changing the invalid-row span rule.
            KeyValuePair.Create(4, ImmutableHashSet.Create(StringComparer.Ordinal, "CS0128")),
            // v5 takes body ranges from the product artifact instead of a name
            // search; the allow list is unchanged.
            KeyValuePair.Create(5, ImmutableHashSet.Create(StringComparer.Ordinal, "CS0128")),
        ]);

    [Fact]
    public void BodyIntrinsicAllowlist_IsPinnedToCurrentMethodologyVersion()
    {
        // The allowlist is the operative definition of productBodyDefect under the stamped
        // methodologyVersion, but it lives in a different file from the stamp with no code
        // path between them. Without this gate a contributor can widen the soundness rule —
        // adding, say, CS0136, which this PR's own review identified as shell-dependent
        // (a shell parameter collision produces it) — and ship it under an unchanged v2.
        // The damage is not just a wrong count: rows sharing a stamp are supposed to be
        // comparable, so a silent widening makes the history card chart a v2 -> v2 step
        // across two different methodologies, defeating the boundary split that
        // Render_MovementSplitsProductDefectAcrossMethodologyBoundaryWithoutCharting exists
        // to enforce.
        //
        // Set equality (not a subset check) is the point: any addition, removal, or
        // substitution fails here until the version is bumped and a new pin recorded.
        int version = AuthoredCorpusMethodology.Version;

        Assert.True(
            AllowlistByMethodologyVersion.ContainsKey(version),
            $"methodologyVersion {version} has no pinned body-intrinsic allowlist. Bumping the "
                + "version requires recording the allowlist that defines it here.");

        Assert.Equal(AllowlistByMethodologyVersion[version], SpanAttribution.BodyIntrinsicSemanticErrorIds);
    }

    [Fact]
    public void BodyIntrinsicAllowlist_ExcludesContextDependentErrorClasses()
    {
        // The README forbids whole categories, not just the IDs the close-negative tests
        // happen to exercise. This pins the categories themselves: one representative of
        // each class a broken shell can manufacture. CS0136 is the sharp end — Gemini's
        // review probe showed a shell parameter collision yields exactly CS0136 — and the
        // conversion/overload IDs were reachable additions that no other gate caught.
        string[] shellReachable =
        [
            "CS0103", "CS0246", "CS0234", "CS1061", "CS1069", // resolution
            "CS0029", "CS1503",                               // conversion / overload
            "CS0136",                                         // scope collision
            "CS0165",                                         // definite assignment (const-dependent)
        ];

        foreach (string id in shellReachable)
        {
            Assert.False(
                SpanAttribution.BodyIntrinsicSemanticErrorIds.Contains(id),
                $"{id} is producible by shell reconstruction, so crediting it would break the "
                    + "lower-bound guarantee on productBodyDefect.");
        }
    }
}
