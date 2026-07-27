using System.Collections.Immutable;
using System.Reflection;

using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

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

    static SpanAttribution.TargetIdentity Method(string type, string name, int paramCount)
        => new(type, name, paramCount, SpanAttribution.TargetMemberKind.Method);

    [Fact]
    public void TryLocateBodySpan_LocatesMethodBlockBody()
    {
        const string source = """
            class C
            {
                int M() { return MARKER; }
            }
            """;
        var span = SpanAttribution.TryLocateBodySpan(source, Method("C", "M", 0));

        Assert.NotNull(span);
        Assert.Contains("MARKER", source[span!.Value.Start..span.Value.End]);
        Assert.DoesNotContain("class C", source[span.Value.Start..span.Value.End]);
    }

    [Fact]
    public void TryLocateBodySpan_DisambiguatesOverloadsByParameterCount()
    {
        const string source = """
            class C
            {
                int M() { return ZERO; }
                int M(int a) { return ONE; }
            }
            """;
        var zero = SpanAttribution.TryLocateBodySpan(source, Method("C", "M", 0));
        var one = SpanAttribution.TryLocateBodySpan(source, Method("C", "M", 1));

        Assert.NotNull(zero);
        Assert.NotNull(one);
        Assert.Contains("ZERO", source[zero!.Value.Start..zero.Value.End]);
        Assert.Contains("ONE", source[one!.Value.Start..one.Value.End]);
    }

    [Fact]
    public void TryLocateBodySpan_LocatesPropertyGetter()
    {
        const string source = """
            class C
            {
                int Value { get { return GETMARKER; } set { field = SETMARKER; } }
            }
            """;
        var getter = SpanAttribution.TryLocateBodySpan(
            source,
            new SpanAttribution.TargetIdentity("C", "get_Value", 0, SpanAttribution.TargetMemberKind.PropertyGet));

        Assert.NotNull(getter);
        Assert.Contains("GETMARKER", source[getter!.Value.Start..getter.Value.End]);
        Assert.DoesNotContain("SETMARKER", source[getter.Value.Start..getter.Value.End]);
    }

    [Fact]
    public void TryLocateBodySpan_LocatesConstructor()
    {
        const string source = """
            class C
            {
                public C(int a) { CTORMARKER(); }
            }
            """;
        var ctor = SpanAttribution.TryLocateBodySpan(
            source,
            new SpanAttribution.TargetIdentity("C", ".ctor", 1, SpanAttribution.TargetMemberKind.Constructor));

        Assert.NotNull(ctor);
        Assert.Contains("CTORMARKER", source[ctor!.Value.Start..ctor.Value.End]);
    }

    [Fact]
    public void TryLocateBodySpan_ReturnsNullWhenAmbiguousAcrossSameNamedTypes()
    {
        // Two unrelated types both named C with a matching member: not uniquely
        // locatable, so the locator must decline rather than guess.
        const string source = """
            namespace A { class C { int M() { return 1; } } }
            namespace B { class C { int M() { return 2; } } }
            """;
        var span = SpanAttribution.TryLocateBodySpan(source, Method("C", "M", 0));

        Assert.Null(span);
    }

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

        bool isolated = SpanAttribution.DecompiledBodyIsolatedUnderBrokenShell(
            decompiled, Compile(decompiled), authored, Compile(authored), Method("C", "M", 0));

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

        bool isolated = SpanAttribution.DecompiledBodyIsolatedUnderBrokenShell(
            decompiled, Compile(decompiled), authored, Compile(authored), Method("C", "M", 0));

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

        bool isolated = SpanAttribution.DecompiledBodyIsolatedUnderBrokenShell(
            decompiled, Compile(decompiled), authored, Compile(authored), Method("C", "M", 0));

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

        bool isolated = SpanAttribution.DecompiledBodyIsolatedUnderBrokenShell(
            decompiled, Compile(decompiled), authored, Compile(authored), Method("C", "M", 0));

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

        bool isolated = SpanAttribution.DecompiledBodyIsolatedUnderBrokenShell(
            decompiled, Compile(decompiled), authored, Compile(authored), Method("C", "M", 0));

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

        bool isolated = SpanAttribution.DecompiledBodyIsolatedUnderBrokenShell(
            decompiled, Compile(decompiled), authored, Compile(authored), Method("C", "M", 0));

        Assert.False(isolated);
    }

    [Fact]
    public void DecompiledBodyIsolated_FalseWhenBodyNotLocatable()
    {
        // Ambiguous target: locator declines, classifier must not fabricate.
        const string decompiled = """
            namespace A { class C { int M() { return undefinedInBody; } } }
            namespace B { class C { int M() { return 2; } } }
            """;
        const string authored = """
            namespace A { class C { int M() { return 1; } } }
            namespace B { class C { int M() { return 2; } } }
            """;

        bool isolated = SpanAttribution.DecompiledBodyIsolatedUnderBrokenShell(
            decompiled, Compile(decompiled), authored, Compile(authored), Method("C", "M", 0));

        Assert.False(isolated);
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
        int version = SpanAttribution.MethodologyVersion;

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
