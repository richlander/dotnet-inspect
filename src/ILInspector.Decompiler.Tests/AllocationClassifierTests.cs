using System.Collections.Generic;
using ILInspector.Decompiler.Analysis;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class AllocationClassifierTests
{
    static IReadOnlyList<Annotation> Classify(string methodName)
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(AllocSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return new AllocationClassifier().Classify(function!);
    }

    static IReadOnlyList<string> Ids(IReadOnlyList<Annotation> annotations)
        => annotations.Select(a => a.Descriptor.Id).ToList();

    [Fact]
    public void Box_IsDetected_EvenThoughItIsInvisibleInRaisedCSharp()
    {
        // object BoxInt(int x) => x;  raises to `return x;` — the box has no C#
        // token, yet the alloc is real and must be reported at the IL level.
        var annotations = Classify(nameof(AllocSampleClass.BoxInt));

        var box = Assert.Single(annotations, a => a.Descriptor.Id == "alloc.box");
        Assert.Equal("int", box.Detail);
        Assert.True(box.SourceOffset >= 0, "the annotation should carry IL provenance");
        Assert.Equal(AnnotationCategory.Allocation, box.Descriptor.Category);
    }

    [Fact]
    public void NewArrayAndNewObject_AreDetected()
    {
        Assert.Contains("alloc.array", Ids(Classify(nameof(AllocSampleClass.MakeArray))));
        Assert.Contains("alloc.new", Ids(Classify(nameof(AllocSampleClass.MakeObject))));
    }

    [Fact]
    public void ValueTypeNewObject_IsNotAnAllocation()
    {
        // new KeyValuePair<int, int>(...) is a struct constructor — it constructs
        // in place and allocates nothing on the heap, so it must not be reported.
        // (Generic value types carry the VALUETYPE hint; a bare cross-assembly
        // struct token such as new DateTime(...) is a known precision gap.)
        Assert.DoesNotContain("alloc.new", Ids(Classify(nameof(AllocSampleClass.MakeStruct))));
    }

    [Fact]
    public void CapturingLambda_AllocatesClosureAndDelegate_EveryCall()
    {
        // x => x + k captures k, so a display-class closure is built and the
        // delegate over it cannot be cached — both allocate on every call.
        var annotations = Classify(nameof(AllocSampleClass.Capture));
        var ids = Ids(annotations);

        Assert.Contains("alloc.closure", ids);
        var del = Assert.Single(annotations, a => a.Descriptor.Id == "alloc.delegate");
        Assert.Equal(AnnotationConditionality.Always, del.Conditionality);
    }

    [Fact]
    public void StatelessLambda_AllocatesADelegate_CachedOnce()
    {
        // x => x + 1 captures nothing, so the compiler caches the delegate in a
        // <>9__ field — it allocates at most once, not per call. The
        // conditionality dimension keeps this from crying wolf.
        var annotations = Classify(nameof(AllocSampleClass.Cached));

        var del = Assert.Single(annotations, a => a.Descriptor.Id == "alloc.delegate");
        Assert.Equal(AnnotationConditionality.CachedOnce, del.Conditionality);
    }

    [Fact]
    public void StructEnumerator_IsNotReportedAsAnAllocation()
    {
        // foreach over List<int> binds the struct List<T>.Enumerator, returned by
        // value — no heap allocation. The value-type hint must suppress it.
        var ids = Ids(Classify(nameof(AllocSampleClass.SumList)));

        Assert.DoesNotContain("alloc.enumerator", ids);
    }

    [Fact]
    public void ReferenceTypeEnumerator_IsReportedAsAnAllocation()
    {
        // foreach over IEnumerable<int> dispatches to a reference-type
        // IEnumerator<int> — a real allocation, surfaced via the call return.
        var annotations = Classify(nameof(AllocSampleClass.SumEnumerable));

        var enumerator = Assert.Single(annotations, a => a.Descriptor.Id == "alloc.enumerator");
        Assert.True(enumerator.SourceOffset >= 0);
    }

    [Fact]
    public void IteratorKickoff_AllocatesAStateMachine()
    {
        // An iterator's state machine is a class, newobj'd in the kickoff method.
        Assert.Contains("alloc.statemachine", Ids(Classify(nameof(AllocSampleClass.Range))));
    }

    [Fact]
    public void Classifier_ObservesTheImportedStage()
    {
        var classifier = new AllocationClassifier();
        Assert.Equal(AnnotationCategory.Allocation, classifier.Category);
        Assert.Equal(AnnotationStage.Imported, classifier.Stage);
    }
}

#pragma warning disable CA1822 // instance method used to exercise an instance-delegate path is intentional
public static class AllocSampleClass
{
    public static object BoxInt(int x) => x;

    public static int[] MakeArray(int n) => new int[n];

    public static object MakeObject() => new object();

    // A value-type newobj: a struct constructor allocates nothing on the heap.
    public static KeyValuePair<int, int> MakeStruct(int key, int value) => new(key, value);

    public static System.Func<int, int> Capture(int k) => x => x + k;

    public static System.Func<int, int> Cached() => x => x + 1;

    public static int SumList(List<int> xs)
    {
        int sum = 0;
        foreach (var x in xs)
            sum += x;
        return sum;
    }

    public static int SumEnumerable(IEnumerable<int> xs)
    {
        int sum = 0;
        foreach (var x in xs)
            sum += x;
        return sum;
    }

    public static IEnumerable<int> Range(int n)
    {
        for (int i = 0; i < n; i++)
            yield return i;
    }
}
#pragma warning restore CA1822
