using ILInspector.Decompiler.Analysis;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class AnnotatedCSharpRendererTests
{
    static string Render(string methodName)
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(AllocSampleClass).FullName!, methodName)!;
        var result = AnnotatedCSharpRenderer.Render(function);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    [Fact]
    public void Box_SurfacesAsATrailingCommentOnTheReturn()
    {
        // The box is invisible in the source (object BoxInt(int x) => x;) yet the
        // annotated view shows it where it happens.
        var output = Render(nameof(AllocSampleClass.BoxInt));
        Assert.Contains("return x;  // alloc.box(int)", output);
    }

    [Fact]
    public void Array_SurfacesTheAllocatedType()
    {
        var output = Render(nameof(AllocSampleClass.MakeArray));
        Assert.Contains("// alloc.array(int[])", output);
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
    public void AlwaysConditionality_IsNotPrintedAsNoise()
    {
        // "always" never appears as a suffix; only the non-default cases do.
        var output = Render(nameof(AllocSampleClass.BoxInt));
        Assert.DoesNotContain("always", output);
    }

    [Fact]
    public void RefTypeEnumerator_IsAnnotatedOnItsGetEnumeratorStatement()
    {
        var output = Render(nameof(AllocSampleClass.SumEnumerable));
        var line = output.Split('\n').Single(l => l.Contains("GetEnumerator()"));
        Assert.Contains("// alloc.enumerator", line);
    }

    [Fact]
    public void NoAnnotations_LeavesTheOutputUntouched()
    {
        // SumList over a List<T> uses the struct enumerator (no heap alloc), so
        // the annotated view equals the plain view — no comments, positive-only.
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(AllocSampleClass).FullName!, nameof(AllocSampleClass.SumList))!;
        var plainFunction = IrImporter.Import(source, typeof(AllocSampleClass).FullName!, nameof(AllocSampleClass.SumList))!;

        var annotated = AnnotatedCSharpRenderer.Render(function).Output;
        var plain = CSharpPrinter.PrintRaised(plainFunction).Output;

        Assert.DoesNotContain("//", annotated);
        Assert.Equal(plain, annotated);
    }
}
