using ILInspector.CSharp;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class MemberBodyFactsTests
{
    static IReadOnlySet<string> NamespacesFor(string typeFullName, string methodName, int overloadIndex = 0)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeFullName, methodName, overloadIndex);
        Assert.NotNull(function);
        return MemberBodyFacts.ReferencedNamespaces(function);
    }

    static ConstructorBodyFacts ConstructorFor(string typeFullName, string methodName, int overloadIndex = 0)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeFullName, methodName, overloadIndex);
        Assert.NotNull(function);
        return MemberBodyFacts.Constructor(function);
    }

    [Fact]
    public void ReferencedNamespaces_ReportsEveryDistinctReferencedNamespace()
    {
        // The body constructs a List<int> and a StringBuilder, so it references
        // System.Collections.Generic and System.Text in addition to System.
        var namespaces = NamespacesFor(
            typeof(NamespaceRefSample).FullName!,
            nameof(NamespaceRefSample.ReferencesTextAndGenerics));

        Assert.Contains("System", namespaces);
        Assert.Contains("System.Text", namespaces);
        Assert.Contains("System.Collections.Generic", namespaces);
    }

    [Fact]
    public void ReferencedNamespaces_OmitsUnreferencedNamespaces()
    {
        // A body that only touches Int32 reports System and nothing more specific.
        var namespaces = NamespacesFor(
            typeof(NamespaceRefSample).FullName!,
            nameof(NamespaceRefSample.ReferencesOnlySystem));

        Assert.Contains("System", namespaces);
        Assert.DoesNotContain("System.Text", namespaces);
        Assert.DoesNotContain("System.Collections.Generic", namespaces);
    }

    [Fact]
    public void ReferencedNamespaces_ResultIsOrdinalSorted()
    {
        // The usings the harness builds from this set depend on a stable ordinal
        // ordering, so the query must return its namespaces sorted.
        var namespaces = NamespacesFor(
            typeof(NamespaceRefSample).FullName!,
            nameof(NamespaceRefSample.ReferencesTextAndGenerics));

        Assert.Equal(
            namespaces.OrderBy(ns => ns, StringComparer.Ordinal).ToArray(),
            namespaces.ToArray());
    }

    [Fact]
    public void Constructor_ChainedConstructor_ReportsChainCalleeParameterTypes()
    {
        // The parameterless overload chains to `this(int, string)`, so the chain
        // fact carries the chained-to constructor's parameter type displays and
        // the body is NOT primary-constructor-prologue shaped.
        var facts = ConstructorFor(typeof(ChainedCtorSample).FullName!, ".ctor", overloadIndex: 0);

        Assert.NotNull(facts.ChainParameterTypes);
        Assert.Equal(["int", "string"], facts.ChainParameterTypes);
        Assert.Null(facts.PrimaryConstructorPrologue);
    }

    [Fact]
    public void Constructor_PrimaryConstructor_ReportsOrderedFieldStores()
    {
        // A primary constructor lowers to `this.<field> = argN;` stores followed
        // by a parameterless base call, so the prologue fact carries one store
        // per captured parameter in source-argument order.
        var facts = ConstructorFor(typeof(PrimaryCtorSample).FullName!, ".ctor");

        Assert.NotNull(facts.PrimaryConstructorPrologue);
        var prologue = facts.PrimaryConstructorPrologue!;
        Assert.Equal(2, prologue.Count);
        Assert.Equal([1, 2], prologue.Select(store => store.SourceArgumentIndex).ToArray());
        Assert.All(prologue, store => Assert.False(string.IsNullOrEmpty(store.FieldName)));
        Assert.NotEqual(prologue[0].FieldName, prologue[1].FieldName);
    }

    [Fact]
    public void Constructor_NonConstructorMethod_ReportsNoFacts()
    {
        // A plain method has neither a chain call nor a primary-constructor
        // prologue, so both facts are absent.
        var facts = ConstructorFor(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Add));

        Assert.Null(facts.ChainParameterTypes);
        Assert.Null(facts.PrimaryConstructorPrologue);
    }
}
