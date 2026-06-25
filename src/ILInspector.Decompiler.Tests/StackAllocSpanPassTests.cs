using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StackAllocSpanPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");

    [Fact]
    public void CorelibSpanDirectStackalloc_Raises()
    {
        var function = Build(StackAllocSpanConstructor(
            TypeRef.CoreLib("System", "Span`1"),
            new StackAllocate(new Constant(4, Int32))));

        new StackAllocSpanPass().Run(function, PassContext.None);

        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal("int", raised.ElementType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void CorelibReadOnlySpanDirectStackalloc_Raises()
    {
        var function = Build(StackAllocSpanConstructor(
            TypeRef.CoreLib("System", "ReadOnlySpan`1"),
            new StackAllocate(new Constant(4, Int32))));

        new StackAllocSpanPass().Run(function, PassContext.None);

        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal("int", raised.ElementType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void UserSystemSpanLookalike_DoesNotRaise()
    {
        var function = Build(StackAllocSpanConstructor(
            TypeRef.Definition("UserAssembly", "System", "Span`1"),
            new StackAllocate(new Constant(4, Int32))));

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Equal("UserAssembly", construction.Constructor.DeclaringType.ElementType?.Assembly);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
        function.CheckInvariant();
    }

    [Fact]
    public void WrappedStackallocPointer_DoesNotRaise()
    {
        var wrapper = new Call(
            new MethodRef(Holder, "Wrap", TypeRef.Pointer(Void), [TypeRef.Pointer(Byte)], HasThis: false),
            isVirtual: false,
            [new StackAllocate(new Constant(4, Int32))]);
        var function = Build(StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), wrapper));

        new StackAllocSpanPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Single(function.Descendants.OfType<Call>());
        Assert.Single(function.Descendants.OfType<StackAllocate>());
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
        function.CheckInvariant();
    }

    static NewObject StackAllocSpanConstructor(TypeRef spanDefinition, IrExpression pointer)
    {
        var span = TypeRef.GenericInstance(spanDefinition, [Int32]);
        var ctor = new MethodRef(span, ".ctor", Void, [TypeRef.Pointer(Void), Int32], HasThis: true);
        return new NewObject(ctor, [pointer, new Constant(1, Int32)]);
    }

    static IrFunction Build(IrExpression value)
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(value));
        body.Add(block);
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(value.ResultType ?? Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
