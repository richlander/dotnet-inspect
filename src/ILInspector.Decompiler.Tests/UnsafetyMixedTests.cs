using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

public class UnsafetyMixedTests
{
    static IReadOnlyList<Annotation> Classify(string methodName)
    {
        var source = MetadataSource.Open(typeof(MixedSampleClass).Assembly.Location);
        return ResearchViews.CollectFacts(source, typeof(MixedSampleClass).FullName!, methodName)
            .Where(fact => fact.Descriptor.Category == AnnotationCategory.Unsafety)
            .ToList();
    }

    [Fact]
    public void SafeRefDeref_InUnsafeMethod_IsNotFlagged()
    {
        var facts = Classify(nameof(MixedSampleClass.MixedRead));
        var derefs = facts.Where(f => f.Descriptor.Id == "unsafe.deref").ToList();
        var deref = Assert.Single(derefs);
        Assert.Equal("int", deref.Detail);
    }
}

public static class MixedSampleClass
{
    public static unsafe int MixedRead(ref int r, int* p) 
    {
        int x = r; // SAFE DEREF
        int y = *p; // UNSAFE DEREF
        return x + y;
    }
}
