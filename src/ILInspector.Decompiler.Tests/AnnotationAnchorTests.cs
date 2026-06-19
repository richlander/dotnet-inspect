using System.Collections.Generic;
using ILInspector.Decompiler.Analysis;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class AnnotationAnchorTests
{
    // Classify the imported tree, then raise it in place and anchor the
    // annotations onto the raised statements — the real pipeline order.
    static (IrFunction Raised, IReadOnlyDictionary<IrNode, IReadOnlyList<Annotation>> Map) AnchorFor(string methodName)
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(AllocSampleClass).FullName!, methodName);
        Assert.NotNull(function);

        var annotations = AnnotationEngine.Default.ClassifyImported(function!);
        CSharpPrinter.PrintRaised(function!);   // raises in place
        return (function!, AnnotationAnchor.Anchor(function!, annotations));
    }

    [Fact]
    public void Box_AnchorsToTheReturnStatement_EvenThoughTheBoxWasRaisedAway()
    {
        // object BoxInt(int x) => x;  the box is erased by the raise, so no node
        // carries its exact offset — range anchoring still lands it on `return`.
        var (_, map) = AnchorFor(nameof(AllocSampleClass.BoxInt));

        var owner = Assert.Single(map);
        Assert.IsType<Return>(owner.Key);
        Assert.Contains(owner.Value, a => a.Descriptor.Id == "alloc.box");
    }

    [Fact]
    public void EveryAnnotation_IsAnchoredSomewhere()
    {
        // Positive-only: a fact is never silently dropped.
        foreach (var method in new[]
        {
            nameof(AllocSampleClass.MakeArray),
            nameof(AllocSampleClass.Capture),
            nameof(AllocSampleClass.SumEnumerable),
            nameof(AllocSampleClass.Range),
        })
        {
            var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
            var function = IrImporter.Import(source, typeof(AllocSampleClass).FullName!, method)!;
            var annotations = AnnotationEngine.Default.ClassifyImported(function);
            CSharpPrinter.PrintRaised(function);

            var map = AnnotationAnchor.Anchor(function, annotations);
            int anchored = map.Values.Sum(list => list.Count);

            Assert.Equal(annotations.Count, anchored);
        }
    }

    [Fact]
    public void Array_AnchorsToAStatementThatStillContainsTheNewArray()
    {
        // The array survives the raise, so its fact should land on the very
        // statement whose subtree holds the NewArray.
        var (_, map) = AnchorFor(nameof(AllocSampleClass.MakeArray));

        var entry = Assert.Single(map, kv => kv.Value.Any(a => a.Descriptor.Id == "alloc.array"));
        var statementNodes = new List<IrNode> { entry.Key };
        statementNodes.AddRange(entry.Key.Descendants);
        Assert.Contains(statementNodes, n => n is NewArray);
    }

    [Fact]
    public void Engine_Default_RunsTheAllocationClassifier()
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(AllocSampleClass).FullName!, nameof(AllocSampleClass.BoxInt))!;

        var annotations = AnnotationEngine.Default.ClassifyImported(function);

        Assert.Contains(annotations, a => a.Descriptor.Id == "alloc.box");
    }
}
