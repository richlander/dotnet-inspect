using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

public class MixedSourceRendererTests
{
    static string Render(string methodName)
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var result = RenderMixed(
            source, typeof(AllocSampleClass).FullName!, methodName);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    static DecompilerResult RenderMixed(
        MetadataSource source, string type, string method, AnnotationStage stage = AnnotationStage.Raised)
        => ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source, type, method, AnnotatedSource: true, AnnotatedStage: stage)).AnnotatedSource!;

    [Fact]
    public void CSharpStatement_PrecedesItsInterleavedIl()
    {
        // The mixed view is C#-primary: a C# statement renders, then the IL that
        // implements it follows beneath as comment lines.
        var output = Render(nameof(AllocSampleClass.MakeArray));
        var lines = output.Split('\n');
        int cs = Array.FindIndex(lines, l => l.Contains("new int[n]"));
        int il = Array.FindIndex(lines, l => l.Contains("newarr"));
        Assert.True(cs >= 0 && il > cs, "C# statement should precede its interleaved IL");
        Assert.StartsWith("//", lines[il].TrimStart());
    }

    [Fact]
    public void Fact_ShowsOnBothTheCSharpLineAndTheIlLine()
    {
        // One fact, projected co-equally: a trailing comment on the C# statement
        // and at the opcode beneath it.
        var output = Render(nameof(AllocSampleClass.MakeArray));
        var lines = output.Split('\n');
        var cs = lines.Single(l => l.Contains("new int[n]"));
        var il = lines.Single(l => l.Contains("newarr"));
        Assert.Contains("alloc.array(int[]; alloc=System.Int32[]; path=straight-line; path-confidence=dominates-return; post-dominance=return-post-dominates; escape=escapes; escape-kind=escapes-return; multiplicity=once)", cs);
        Assert.Contains("alloc.array(int[]; alloc=System.Int32[]; path=straight-line; path-confidence=dominates-return; post-dominance=return-post-dominates; escape=escapes; escape-kind=escapes-return; multiplicity=once)", il);
    }

    [Fact]
    public void Box_SurfacesAsATrailingCommentOnTheReturn()
    {
        // The box is invisible in the source (object BoxInt(int x) => x;) yet the
        // annotated view shows it where it happens, as a trailing C# comment.
        var output = Render(nameof(AllocSampleClass.BoxInt));
        Assert.Contains("return x;  // alloc.box(int; alloc=boxed System.Int32; path=straight-line; path-confidence=dominates-return; post-dominance=return-post-dominates; escape=escapes; escape-kind=escapes-return; multiplicity=once)", output);
    }

    [Fact]
    public void Box_FactLandsOnTheBoxOpcode()
    {
        // The IL-view dual of the C# anchoring: the fact attaches to the exact
        // opcode, not a statement.
        var output = Render(nameof(AllocSampleClass.BoxInt));
        var line = output.Split('\n').Single(l => l.Contains(": box"));
        Assert.Contains("alloc.box(int; alloc=boxed System.Int32; path=straight-line; path-confidence=dominates-return; post-dominance=return-post-dominates; escape=escapes; escape-kind=escapes-return; multiplicity=once)", line);
    }

    [Fact]
    public void FactPrecedesStructuralAnnotationsOnTheSameLine()
    {
        // Facts lead; the structural stack-type annotation follows — the
        // high-value fact is read first.
        var output = Render(nameof(AllocSampleClass.BoxInt));
        var line = output.Split('\n').Single(l => l.Contains(": box"));
        int fact = line.IndexOf("alloc.box", StringComparison.Ordinal);
        int stack = line.IndexOf("stack: [object]", StringComparison.Ordinal);
        Assert.True(fact >= 0 && stack >= 0 && fact < stack);
    }

    [Fact]
    public void AlwaysConditionality_IsNotPrintedAsNoise()
    {
        // "always" never appears as a suffix; only the non-default cases do.
        var output = Render(nameof(AllocSampleClass.BoxInt));
        Assert.DoesNotContain("always", output);
    }

    [Fact]
    public void CachedDelegate_ShowsConditionalityBecauseItIsNotAlways()
    {
        // The surprising fact — the delegate allocates only on first call — is
        // exactly what the conditionality suffix is for.
        var output = Render(nameof(AllocSampleClass.Cached));
        Assert.Contains("cached-once", output);
        Assert.Contains("alloc.delegate", output);
    }

    [Fact]
    public void CachedDelegate_FactLandsOnTheNewobjAndShowsConditionality()
    {
        var output = Render(nameof(AllocSampleClass.Cached));
        var line = output.Split('\n').Single(l => l.Contains(": newobj"));
        Assert.Contains("alloc.delegate", line);
        Assert.Contains("cached-once", line);
    }

    [Fact]
    public void StateMachine_FactLandsOnTheKickoffNewobj()
    {
        var output = Render(nameof(AllocSampleClass.Range));
        var line = output.Split('\n').Single(l => l.Contains(": newobj"));
        Assert.Contains("alloc.statemachine", line);
    }

    [Fact]
    public void RefTypeEnumerator_IsAnnotatedOnItsForeachStatement()
    {
        var output = Render(nameof(AllocSampleClass.SumEnumerable));
        var line = output.Split('\n').Single(l => l.Contains("foreach"));
        Assert.Contains("// alloc.enumerator", line);
    }

    [Fact]
    public void NoAllocation_AddsNoFactComment()
    {
        // SumList over a List<T> uses the struct enumerator (no heap alloc), so
        // the annotated view surfaces no alloc fact — positive-only, no noise.
        var output = Render(nameof(AllocSampleClass.SumList));
        Assert.DoesNotContain("alloc.", output);
    }

    [Fact]
    public void ClosureAndDelegateAllocations_BothInterleave()
    {
        var output = Render(nameof(AllocSampleClass.Capture));
        Assert.Contains("alloc.closure", output);
        Assert.Contains("alloc.delegate", output);
        // IL is interleaved, identified by IL offsets in comment lines.
        Assert.Matches(@"// IL_[0-9A-Fa-f]{4}: ", output);
    }

    [Fact]
    public void EveryInterleavedIlLine_IsACommentBeneathCSharp()
    {
        // No bare IL leaks into the C#: every IL_ line is a comment.
        var output = Render(nameof(AllocSampleClass.SumList));
        foreach (var line in output.Split('\n'))
            if (line.Contains("IL_") && line.Contains(": "))
                Assert.Contains("//", line);
    }

    [Fact]
    public void Render_LoweredStage_DeclinesLockSugar()
    {
        // Issue #636: at the lowered altitude the LockSugarPass is declined, so a
        // `lock (gate) { ... }` raised in the default view surfaces as the
        // underlying Monitor.Enter / try…finally shape it lowers from.
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);

        var raised = RenderMixed(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ClassicLock),
            AnnotationStage.Raised);
        var lowered = RenderMixed(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ClassicLock),
            AnnotationStage.Lowered);

        Assert.NotNull(raised.Output);
        Assert.NotNull(lowered.Output);
        Assert.Contains("lock (", raised.Output!);
        Assert.DoesNotContain("lock (", lowered.Output!);
        Assert.Contains("Monitor.Enter", lowered.Output!);
    }

    [Fact]
    public void Render_LoweredStage_UsesCrossMethodImportForLambda()
    {
        // Lowered Source declines only cosmetic sugar. It still needs the raised
        // path's cross-method import seam for load-bearing passes that import
        // compiler-generated companion bodies, such as LambdaRaisingPass.
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);

        var lowered = RenderMixed(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.NonCapturingLambda),
            AnnotationStage.Lowered);

        Assert.NotNull(lowered.Output);
        Assert.Contains("=>", lowered.Output!);
        Assert.DoesNotContain("return new Func", lowered.Output!);
    }

    [Fact]
    public void Render_AttachesTrace_MirroringResultOutcome()
    {
        // The decompiler returns a telemetry-free trace shape a host can convert
        // into its own diagnostics: it mirrors the result's fidelity and
        // diagnostics, and reports the symbol source actually consulted.
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var result = RenderMixed(
            source, typeof(AllocSampleClass).FullName!, nameof(AllocSampleClass.SumList));

        Assert.NotNull(result.Trace);
        Assert.Equal(result.Fidelity, result.Trace!.Fidelity);
        Assert.Equal(result.Diagnostics, result.Trace.Diagnostics);
        Assert.Equal(result.Succeeded, result.Trace.Succeeded);
        // SumList has locals and the test assembly ships a PDB, so a symbol
        // source was consulted (embedded or sidecar, depending on build config).
        Assert.NotEqual(DecompilerSymbolSource.None, result.Trace.Symbols);
    }

    [Fact]
    public void Render_WithoutSymbols_ReportsNoSymbolSource()
    {
        // OpenWithoutSymbols never consults a PDB, so the trace honestly reports
        // that no symbol source was used even when one exists on disk.
        var source = MetadataSource.OpenWithoutSymbols(typeof(AllocSampleClass).Assembly.Location);
        var result = RenderMixed(
            source, typeof(AllocSampleClass).FullName!, nameof(AllocSampleClass.SumList));

        Assert.NotNull(result.Trace);
        Assert.Equal(DecompilerSymbolSource.None, result.Trace!.Symbols);
    }

    [Fact]
    public void DecompilerResult_MetadataDoesNotChangeEquality()
    {
        var left = new DecompilerResult("return 1;\n", DecompilationFidelity.Full, [])
        {
            Metadata = new DecompilerResultMetadata(
                DecompilerOptions.Default,
                [
                    new DecompilerDecision("type-name.framework-imported", "taste", "System.Math", "test")
                    {
                        OldValue = "System.Math",
                        NewValue = "Math",
                    },
                ]),
        };
        var right = new DecompilerResult("return 1;\n", DecompilationFidelity.Full, [])
        {
            Metadata = new DecompilerResultMetadata(
                DecompilerOptions.Default with { ReadableLocalNames = true },
                []),
        };

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }
}

/// <summary>
/// A derived constructor whose implicit <c>base()</c> targets a user-defined
/// base. The chain call prints nothing, so it owns no characters, but its two
/// opcodes still run — before the first statement. See
/// <see cref="MixedPreambleTests"/>.
/// </summary>
public class MixedPreambleBase
{
    protected MixedPreambleBase() => Created = true;

    public bool Created { get; }
}

public sealed class MixedPreambleDerived : MixedPreambleBase
{
    public MixedPreambleDerived(string name) => Name = name;

    public string Name { get; }
}

public class MixedPreambleTests
{
    static string Render(Type type)
    {
        var source = MetadataSource.Open(type.Assembly.Location);
        var result = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source, type.FullName!, ".ctor", AnnotatedSource: true)).AnnotatedSource;
        Assert.NotNull(result?.Output);
        return result!.Output!;
    }

    [Fact]
    public void ImplicitBaseCall_RendersAboveTheStatementItRunsBefore()
    {
        // The implicit base() prints no C#, so it has no range and no line, and
        // the interleave reaches it only through its insertion point. Two things
        // have to hold and both have been wrong before: the opcodes must appear
        // at all (dropping the zero-width range once removed them entirely), and
        // they must appear *above* the first statement rather than beneath it
        // (bucketing them onto the next statement's line once claimed that
        // statement executed them).
        var lines = Render(typeof(MixedPreambleDerived)).Split('\n');

        int baseCall = Array.FindIndex(lines, l => l.Contains($"{nameof(MixedPreambleBase)}::.ctor()"));
        int firstStatement = Array.FindIndex(lines, l => l.Trim().StartsWith("this.Name = name;"));

        Assert.True(baseCall >= 0, $"base call IL is missing from:\n{string.Join('\n', lines)}");
        Assert.True(firstStatement >= 0, "the constructor body should print its field store");
        Assert.True(
            baseCall < firstStatement,
            $"base call IL should precede the statement it runs before:\n{string.Join('\n', lines)}");
        Assert.StartsWith("//", lines[baseCall].TrimStart());

        // The invariant is "before the first statement", not "immediately after
        // the opening brace". An insertion point is where the node's text would
        // have gone, and the printer may hoist synthesized local declarations to
        // the top of the block first, in which case the preamble lands below
        // them. Those declarations emit no IL of their own, so nothing
        // executable is ever shown as running before the base call.
    }
}
