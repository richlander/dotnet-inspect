using ILInspector.CSharp;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class ConstructorBodyFactExtractorTests
{
    static ConstructorBodyFacts ExtractFor(string typeFullName, string methodName, int overloadIndex = 0)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeFullName, methodName, overloadIndex);
        Assert.NotNull(function);
        return ConstructorBodyFactExtractor.Extract(function);
    }

    [Fact]
    public void ChainedConstructor_ReportsChainCalleeParameterTypes()
    {
        // The parameterless overload chains to `this(int, string)`, so the chain
        // fact carries the chained-to constructor's parameter type displays and
        // the body is NOT primary-constructor-prologue shaped.
        var facts = ExtractFor(typeof(ChainedCtorSample).FullName!, ".ctor", overloadIndex: 0);

        Assert.NotNull(facts.ChainParameterTypes);
        Assert.Equal(["int", "string"], facts.ChainParameterTypes);
        Assert.Null(facts.PrimaryConstructorPrologue);
    }

    [Fact]
    public void PrimaryConstructor_ReportsOrderedFieldStores()
    {
        // A primary constructor lowers to `this.<field> = argN;` stores followed
        // by a parameterless base call, so the prologue fact carries one store
        // per captured parameter in source-argument order.
        var facts = ExtractFor(typeof(PrimaryCtorSample).FullName!, ".ctor");

        Assert.NotNull(facts.PrimaryConstructorPrologue);
        var prologue = facts.PrimaryConstructorPrologue!;
        Assert.Equal(2, prologue.Count);
        Assert.Equal([1, 2], prologue.Select(store => store.SourceArgumentIndex).ToArray());
        Assert.All(prologue, store => Assert.False(string.IsNullOrEmpty(store.FieldName)));
        Assert.NotEqual(prologue[0].FieldName, prologue[1].FieldName);
    }

    [Fact]
    public void NonConstructorMethod_ReportsNoFacts()
    {
        // A plain method has neither a chain call nor a primary-constructor
        // prologue, so both facts are absent.
        var facts = ExtractFor(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Add));

        Assert.Null(facts.ChainParameterTypes);
        Assert.Null(facts.PrimaryConstructorPrologue);
    }
}
