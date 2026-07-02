using System.Collections.Generic;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

public class AllocationOccurrenceFactTests
{
    static IReadOnlyList<Annotation> Classify(string methodName)
        => Classify(methodName, locator: null);

    static IReadOnlyList<Annotation> Classify(string methodName, ILInspector.Metadata.AssemblyLocator? locator)
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location, null, locator);
        return ResearchViews.CollectFacts(source, typeof(AllocSampleClass).FullName!, methodName)
            .Where(annotation => annotation.Descriptor.Category == AnnotationCategory.Allocation)
            .ToList();
    }

    // Resolves referenced assemblies from the running runtime directory, where
    // System.Private.CoreLib (and the rest of the shared framework) lives. The
    // default sibling locator cannot find corelib beside the test assembly.
    static readonly ILInspector.Metadata.AssemblyLocator RuntimeLocator = (name, trust) =>
    {
        string dir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string path = System.IO.Path.Combine(dir, name + ".dll");
        return System.IO.File.Exists(path) ? path : null;
    };

    static IReadOnlyList<string> Ids(IReadOnlyList<Annotation> annotations)
        => annotations.Select(a => a.Descriptor.Id).ToList();

    [Fact]
    public void Box_IsDetected_EvenThoughItIsInvisibleInRaisedCSharp()
    {
        // object BoxInt(int x) => x;  raises to `return x;` — the box has no C#
        // token, yet the alloc is real and must be reported at the IL level.
        var annotations = Classify(nameof(AllocSampleClass.BoxInt));

        var box = Assert.Single(annotations, a => a.Descriptor.Id == "alloc.box");
        Assert.Equal("int; alloc=boxed System.Int32; path=straight-line; path-confidence=dominates-return; post-dominance=return-post-dominates; escape=escapes; escape-kind=escapes-return", box.Detail);
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
    public void RectangularArrayNewObject_IsAnArrayAllocation()
    {
        // Multidimensional arrays are constructed with a newobj array
        // constructor, not newarr. They are still array allocations, not generic
        // object allocations.
        var annotations = Classify(nameof(AllocSampleClass.MakeRectangularArray));

        var array = Assert.Single(annotations, a => a.Descriptor.Id == "alloc.array");
        Assert.Equal("int[,]; alloc=System.Int32[,]; path=straight-line; path-confidence=dominates-return; post-dominance=return-post-dominates; escape=escapes; escape-kind=escapes-return", array.Detail);
        Assert.DoesNotContain(annotations, a => a.Descriptor.Id == "alloc.new");
    }

    [Fact]
    public void ValueTypeNewObject_IsNotAnAllocation()
    {
        // new KeyValuePair<int, int>(...) is a struct constructor — it constructs
        // in place and allocates nothing on the heap, so it must not be reported.
        // (Generic value types carry the VALUETYPE hint; a bare cross-assembly
        // struct token such as new DateTime(...) is suppressed only once
        // cross-assembly resolution confirms it — see the two tests below.)
        Assert.DoesNotContain("alloc.new", Ids(Classify(nameof(AllocSampleClass.MakeStruct))));
    }

    [Fact]
    public void CrossAssemblyValueTypeNewObject_PreservesLegacyAnnotationParity_WithLocator()
    {
        // Track A preserves the legacy visible allocation annotation shape for
        // bare cross-assembly framework value-type constructors. The occurrence
        // is non-counting for Analysis performance signals, but still projects
        // to alloc.new for annotation parity.
        Assert.Contains("alloc.new", Ids(Classify(nameof(AllocSampleClass.MakeDateTime), RuntimeLocator)));
    }

    [Fact]
    public void CrossAssemblyValueTypeNewObject_PreservesLegacyAnnotationParity_WithoutLocator()
    {
        Assert.Contains("alloc.new", Ids(Classify(nameof(AllocSampleClass.MakeDateTime), locator: (name, trust) => null)));
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
    public void ResearchRegistry_ProducesAllocationFacts()
    {
        Assert.Contains("alloc.box", Ids(Classify(nameof(AllocSampleClass.BoxInt))));
    }
}

#pragma warning disable CA1822 // instance method used to exercise an instance-delegate path is intentional
public static class AllocSampleClass
{
    public static object BoxInt(int x) => x;

    public static int[] MakeArray(int n) => new int[n];

    public static int[,] MakeRectangularArray(int rows, int columns) => new int[rows, columns];

    public static object MakeObject() => new object();

    // A value-type newobj: a struct constructor allocates nothing on the heap.
    public static KeyValuePair<int, int> MakeStruct(int key, int value) => new(key, value);

    // A bare cross-assembly value-type newobj: System.DateTime is a non-generic
    // struct defined in corelib, so the token carries no VALUETYPE byte and the
    // importing assembly's metadata cannot state its value-type-ness — only
    // cross-assembly resolution can.
    public static System.DateTime MakeDateTime(int year, int month, int day) => new(year, month, day);

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
