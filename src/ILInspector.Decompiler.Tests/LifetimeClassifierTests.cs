using ILInspector.Decompiler.Analysis;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LifetimeClassifierTests
{
    static IReadOnlyList<Annotation> Classify(string methodName)
    {
        var source = MetadataSource.Open(typeof(LifetimeSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(LifetimeSampleClass).FullName!, methodName)!;
        return new LifetimeClassifier().Classify(function);
    }

    [Fact]
    public void RefReturn_IsReported()
    {
        var facts = Classify(nameof(LifetimeSampleClass.FirstElement));
        Assert.Contains(facts, f => f.Descriptor.Id == "lifetime.ref-return" && f.Detail == "int");
    }

    [Fact]
    public void RefReadonlyReturn_IsAlsoARefReturn()
    {
        var facts = Classify(nameof(LifetimeSampleClass.FirstReadonly));
        Assert.Contains(facts, f => f.Descriptor.Id == "lifetime.ref-return");
    }

    [Fact]
    public void StackallocBackedSpan_IsStackBound()
    {
        // The headline fact: source says `Span<int> s = stackalloc int[4]`, hiding
        // that the span's memory is the frame and cannot escape.
        var facts = Classify(nameof(LifetimeSampleClass.StackSpanSum));
        Assert.Contains(facts, f => f.Descriptor.Id == "lifetime.stack-bound" && f.Detail == "Span<int>");
    }

    [Fact]
    public void RefStructReturn_IsReported()
    {
        var facts = Classify(nameof(LifetimeSampleClass.Tail));
        Assert.Contains(facts, f => f.Descriptor.Id == "lifetime.ref-struct-return" && f.Detail == "ReadOnlySpan<char>");
    }

    [Fact]
    public void PointerReturn_IsReported()
    {
        // The dangerous twin of the stack-bound span: a returned raw pointer opts
        // out of ref-safety, so the caller inherits an unverifiable lifetime
        // obligation. Detail is the pointed-to type.
        var facts = Classify(nameof(LifetimeSampleClass.EscapingStackPointer));
        Assert.Contains(facts, f => f.Descriptor.Id == "lifetime.pointer-return" && f.Detail == "int");
    }

    [Fact]
    public void PointerReturn_LandsOnTheReturnStatement()
    {
        var source = MetadataSource.Open(typeof(LifetimeSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(LifetimeSampleClass).FullName!, nameof(LifetimeSampleClass.EscapingStackPointer))!;
        var fact = new LifetimeClassifier().Classify(function).Single(f => f.Descriptor.Id == "lifetime.pointer-return");
        Assert.IsType<Return>(fact.Node);
    }

    [Fact]
    public void RefStructParameter_AloneIsNotReported()
    {
        // A Span parameter is visible in the source signature — not a hidden fact.
        // ReadSpan returns int, takes Span<int>; nothing here is lifetime-hidden.
        var facts = Classify(nameof(LifetimeSampleClass.ReadSpan));
        Assert.Empty(facts);
    }

    [Fact]
    public void PlainValueMethod_ProducesNoLifetimeFacts()
    {
        var facts = Classify(nameof(LifetimeSampleClass.Add));
        Assert.Empty(facts);
    }

    [Fact]
    public void RefReturn_LandsOnTheReturnStatement()
    {
        var source = MetadataSource.Open(typeof(LifetimeSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(LifetimeSampleClass).FullName!, nameof(LifetimeSampleClass.FirstElement))!;
        var fact = new LifetimeClassifier().Classify(function).Single(f => f.Descriptor.Id == "lifetime.ref-return");
        Assert.IsType<Return>(fact.Node);
    }
}
