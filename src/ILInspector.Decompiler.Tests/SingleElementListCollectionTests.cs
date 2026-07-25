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
    public void SingleElementList_AsExtensionMethodArgument_RaisesToCollectionExpression()
    {
        // The real witness: option.Aliases.Concat<string>([option.Name]). Enumerable.Concat
        // is an extension method, so the list is the SECOND argument (arg1), not the
        // reduced receiver (arg0). Its parameter type IEnumerable<string> is a
        // constructible read-only target, so it raises.
        var concat = new MethodRef(Host, "Concat", IEnumerableOf(String), [IEnumerableOf(String), IEnumerableOf(String)], HasThis: false)
        {
            IsExtension = MetadataFactState.Yes,
        };
        var call = new Call(concat, isVirtual: false,
            [new Constant(null, IEnumerableOf(String)), SingleElementList(String, new Constant("x", String))]);
        var function = Wrap(Container(new Return(call)));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NewObject>());
        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(IEnumerableOf(String), collection.TargetType);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains(".Concat", output);
        Assert.Contains("[\"x\"]", output);
        Assert.DoesNotContain("ReadOnlySingleElementList", output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void SingleElementList_AsExtensionMethodReceiver_LeftFlat()
    {
        // An extension method's static call renders in reduced instance form,
        // making arg0 a member-access receiver. `[x].Concat(...)` is not legal C#
        // (CS9176), so the receiver slot must stay flat even though it maps to the
        // `this` parameter.
        var concat = new MethodRef(Host, "Concat", IEnumerableOf(String), [IEnumerableOf(String), IEnumerableOf(String)], HasThis: false)
        {
            IsExtension = MetadataFactState.Yes,
        };
        var call = new Call(concat, isVirtual: false,
            [SingleElementList(String, new Constant("x", String)), new Constant(null, IEnumerableOf(String))]);
        var function = Wrap(Container(new Return(call)));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void NestedSingleElementLists_BothRaised()
    {
        // A nested collection expression `[[x]]` lowers to nested single-element
        // lists. The outer raises first; the inner is re-parented under the outer
        // collection expression and target-typed to its element type, so it raises
        // too rather than leaking the unspellable inner construction.
        var inner = SingleElementList(String, new Constant("x", String));
        var outer = SingleElementList(IEnumerableOf(String), inner);
        var store = new StoreLocal(0, IEnumerableOf(IEnumerableOf(String)), outer);
        var function = Wrap(Container(store, new Return(null)));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NewObject>());
        Assert.Equal(2, function.Descendants.OfType<CollectionExpression>().Count());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("[[\"x\"]]", output);
        Assert.DoesNotContain("ReadOnlySingleElementList", output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void SingleElementList_WithWiderSinkTarget_LeftFlat()
    {
        // Consume(object): the reference conversion IEnumerable<string> -> object
        // emits no IL, so the construction's use-site is an `object` parameter.
        // `[x]` cannot construct object (CS9174), so leave it flat.
        var consume = new MethodRef(Host, "Consume", Void, [TypeRef.CoreLib("System", "Object")], HasThis: false);
        var call = new Call(consume, isVirtual: false, [SingleElementList(String, new Constant("x", String))]);
        var function = Wrap(Container(new ExpressionStatement(call), new Return(null)));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void SingleElementList_WithCovariantSinkTarget_LeftFlat()
    {
        // IEnumerable<object> x = (IEnumerable<string>)["x"]: the list element type
        // is string but the sink is IEnumerable<object>. Raising to `[x]` would
        // retype the elements to object, so leave it flat.
        var store = new StoreLocal(0, IEnumerableOf(TypeRef.CoreLib("System", "Object")), SingleElementList(String, new Constant("x", String)));
        var function = Wrap(Container(store, new Return(null)));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void LookalikeSingleElementListType_NotRaised()
    {
        // A CLI type whose name merely starts with the reserved prefix (ILAsm can
        // emit one) is not the compiler's single-element list; the match is on the
        // exact metadata name, not a prefix.
        var fakeType = TypeRef.GenericInstance(TypeRef.Definition("UserAssembly", "", "<>z__ReadOnlySingleElementListFake`1"), [String]);
        var fakeCtor = new MethodRef(fakeType, ".ctor", Void, [String], HasThis: true);
        var concat = new MethodRef(Host, "Concat", IEnumerableOf(String), [IEnumerableOf(String), IEnumerableOf(String)], HasThis: false);
        var call = new Call(concat, isVirtual: false,
            [new Constant(null, IEnumerableOf(String)), new NewObject(fakeCtor, [new Constant("x", String)])]);
        var function = Wrap(Container(new Return(call)));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
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
