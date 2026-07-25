using System.Collections.Immutable;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Roslyn lowers a one-element collection expression targeting a read-only
/// collection interface to <c>new &lt;&gt;z__ReadOnlySingleElementList&lt;T&gt;(x)</c>,
/// whose angle-bracketed type name never parses.
/// <see cref="InlineArrayCollectionPass"/> raises it back to <c>[x]</c> when
/// the use-site supplies a spellable target type, and leaves it flat otherwise.
/// </summary>
[Trait("Area", "Pass")]
public class SingleElementListCollectionTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Host = TypeRef.Definition("UserAssembly", "Samples", "Host");

    static TypeRef IEnumerableOf(TypeRef element)
        => TypeRef.GenericInstance(TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"), [element]);

    static TypeRef SingleElementListOf(TypeRef element)
        => TypeRef.GenericInstance(TypeRef.Definition("UserAssembly", "", "<>z__ReadOnlySingleElementList`1"), [element]);

    static NewObject SingleElementList(TypeRef element, IrExpression value)
        => new(new MethodRef(SingleElementListOf(element), ".ctor", Void, [element], HasThis: true), [value]);

    static IrFunction Wrap(BlockContainer body)
    {
        var signature = new MethodSignature(IEnumerableOf(String), [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Host, signature, [], body);
    }

    static BlockContainer Container(params IrNode[] statements)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        foreach (var statement in statements)
            block.Add(statement);
        return container;
    }

    [Fact]
    public void SingleElementList_AsCallArgument_RaisesToCollectionExpression()
    {
        // IEnumerable<string> Concat(IEnumerable<string> first, IEnumerable<string> second)
        var concat = new MethodRef(Host, "Concat", IEnumerableOf(String), [IEnumerableOf(String), IEnumerableOf(String)], HasThis: false);
        var call = new Call(concat, isVirtual: false,
            [new Constant(null, IEnumerableOf(String)), SingleElementList(String, new Constant("x", String))]);
        var function = Wrap(Container(new Return(call)));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NewObject>());
        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(String, collection.ElementType);
        Assert.Equal(IEnumerableOf(String), collection.TargetType);
        var element = Assert.IsType<Constant>(Assert.Single(collection.Elements));
        Assert.Equal("x", element.Value);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("[\"x\"]", output);
        Assert.DoesNotContain("ReadOnlySingleElementList", output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void SingleElementList_AsConstructorArgument_RaisesToCollectionExpression()
    {
        var consumerCtor = new MethodRef(Host, ".ctor", Void, [IEnumerableOf(String)], HasThis: true);
        var newConsumer = new NewObject(consumerCtor, [SingleElementList(String, new Constant("x", String))]);
        var function = Wrap(Container(new StoreLocal(0, Host, newConsumer), new Return(null)));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(IEnumerableOf(String), collection.TargetType);
        Assert.DoesNotContain(function.Descendants.OfType<NewObject>(), n => n.Constructor.DeclaringType.Name.Contains("ReadOnlySingleElementList"));
        function.CheckInvariant();
    }

    [Fact]
    public void SingleElementList_AsTypedLocalStore_RaisesWithLocalTargetType()
    {
        var store = new StoreLocal(0, IEnumerableOf(String), SingleElementList(String, new Constant("x", String)));
        var function = Wrap(Container(store, new Return(null)));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(IEnumerableOf(String), collection.TargetType);
        Assert.Empty(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void SingleElementList_InReceiverPosition_LeftFlat()
    {
        // A call whose receiver is the construction: [x].Method() has no legal C#
        // spelling, so the receiver slot must not be raised.
        var method = new MethodRef(SingleElementListOf(String), "GetEnumerator", Void, [], HasThis: true);
        var call = new Call(method, isVirtual: true, [SingleElementList(String, new Constant("x", String))]);
        var function = Wrap(Container(new ExpressionStatement(call), new Return(null)));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void SingleElementList_InReturnPosition_LeftFlat()
    {
        // Return target-type resolution is out of scope for this slice; the flat
        // construction stays and fidelity degrades honestly rather than guessing.
        var function = Wrap(Container(new Return(SingleElementList(String, new Constant("x", String)))));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void OrdinaryListType_AsCallArgument_NotRaised()
    {
        var listType = TypeRef.GenericInstance(TypeRef.CoreLib("System.Collections.Generic", "List`1"), [String]);
        var listCtor = new MethodRef(listType, ".ctor", Void, [String], HasThis: true);
        var concat = new MethodRef(Host, "Concat", IEnumerableOf(String), [IEnumerableOf(String), IEnumerableOf(String)], HasThis: false);
        var call = new Call(concat, isVirtual: false,
            [new Constant(null, IEnumerableOf(String)), new NewObject(listCtor, [new Constant("x", String)])]);
        var function = Wrap(Container(new Return(call)));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        function.CheckInvariant();
    }
}
