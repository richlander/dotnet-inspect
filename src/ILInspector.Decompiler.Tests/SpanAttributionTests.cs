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
        // CS0165 (use of unassigned local) depends only on the body's own locals
        // and control flow — never a shell member — so it is a sound attribution
        // even though the shell is broken.
        const string decompiled = """
            class C
            {
                int M() { int x; return x; }
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
}
