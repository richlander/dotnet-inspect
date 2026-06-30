using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

public class UnsafetyOccurrenceFactTests
{
    static IReadOnlyList<Annotation> Classify(string methodName)
    {
        var source = MetadataSource.Open(typeof(UnsafeSampleClass).Assembly.Location);
        return ResearchViews.CollectFacts(source, typeof(UnsafeSampleClass).FullName!, methodName)
            .Where(fact => fact.Descriptor.Category == AnnotationCategory.Unsafety)
            .ToList();
    }

    [Fact]
    public void PointerRead_IsADeref()
    {
        var facts = Classify(nameof(UnsafeSampleClass.ReadPtr));
        Assert.Contains(facts, f => f.Descriptor.Id == "unsafe.deref" && f.Detail == "int");
    }

    [Fact]
    public void PointerWrite_IsADeref()
    {
        var facts = Classify(nameof(UnsafeSampleClass.WritePtr));
        Assert.Contains(facts, f => f.Descriptor.Id == "unsafe.deref");
    }

    [Fact]
    public void StackAlloc_IsReported()
    {
        var facts = Classify(nameof(UnsafeSampleClass.StackScratch));
        Assert.Contains(facts, f => f.Descriptor.Id == "unsafe.stackalloc");
    }

    [Fact]
    public void FunctionPointerCall_IsCalli()
    {
        var facts = Classify(nameof(UnsafeSampleClass.CallPtr));
        Assert.Contains(facts, f => f.Descriptor.Id == "unsafe.calli");
    }

    [Fact]
    public void SafeRefDeref_IsNotFlagged()
    {
        // A managed ref (ByRef) is safe; the classifier must not confuse it with a
        // pointer dereference. This is the precision guarantee.
        var facts = Classify(nameof(UnsafeSampleClass.ReadRef));
        Assert.DoesNotContain(facts, f => f.Descriptor.Id == "unsafe.deref");
    }

    [Fact]
    public void SafeMethod_ProducesNoUnsafetyFacts()
    {
        var facts = Classify(nameof(UnsafeSampleClass.Add));
        Assert.Empty(facts);
    }
}

public static class UnsafeSampleClass
{
    public static unsafe int ReadPtr(int* p) => *p;

    public static unsafe void WritePtr(int* p, int v) => *p = v;

    public static unsafe int StackScratch(int seed)
    {
        int* scratch = stackalloc int[4];
        scratch[0] = seed;
        return scratch[0];
    }

    public static unsafe int CallPtr(delegate*<int, int> f, int x) => f(x);

    public static int ReadRef(ref int r) => r;

    public static int Add(int a, int b) => a + b;
}
