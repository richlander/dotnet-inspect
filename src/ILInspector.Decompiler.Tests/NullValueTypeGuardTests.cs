using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class NullValueTypeGuardTests
{
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_dateTime = TypeRef.CoreLib("System", "DateTime");
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
    static readonly TypeRef s_shapeOnlyStruct = TypeRef.Definition("Synthetic", "Samples", "ShapeOnlyStruct");

    [Fact]
    public void NullConditional_ReevaluableValueTypeReceiver_StaysConditional()
    {
        var toString = new MethodRef(s_bool, "ToString", s_string, [], HasThis: true);
        var function = FunctionReturning(
            new Conditional(
                new LoadLocal(0, s_bool),
                new Call(toString, isVirtual: true, [new LoadLocal(0, s_bool)]),
                new Constant(null, s_string)),
            s_string,
            [s_bool]);

        new NullConditionalPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
        Assert.Single(function.Descendants.OfType<Conditional>());
        function.CheckInvariant();
    }

    [Fact]
    public void NullConditional_SpilledValueTypeReceiver_StaysConditional()
    {
        var toString = new MethodRef(s_bool, "ToString", s_string, [], HasThis: true);
        var block = new Block();
        block.Add(new StoreStackSlot(1, new LoadStackSlot(0, s_bool)));
        block.Add(new StoreStackSlot(2, new Conditional(
            new LoadStackSlot(0, s_bool),
            new Call(toString, isVirtual: true, [new LoadStackSlot(1, s_bool)]),
            new Constant(null, s_string))));
        block.Add(new Return(new LoadStackSlot(2, s_string)));
        var function = FunctionWithBlock(block, s_string, []);

        new NullConditionalPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
        Assert.Single(function.Descendants.OfType<Conditional>());
        function.CheckInvariant();
    }

    [Fact]
    public void NullCoalescingAssignment_ValueTypeLocal_StaysIf()
    {
        var thenBlock = new Block();
        thenBlock.Add(new StoreLocal(0, s_dateTime, new LoadArgument(0, "fallback", s_dateTime)));
        var block = new Block();
        block.Add(new IfStatement(
            new Comparison(ComparisonKind.Equal, isUnsigned: false, new LoadLocal(0, s_dateTime), new Constant(null, s_object)),
            thenBlock,
            elseArm: null));
        var function = FunctionWithBlock(block, s_void, [s_dateTime], [new Parameter("fallback", s_dateTime)]);

        new NullCoalescingAssignmentPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullCoalescingAssignment>());
        Assert.Single(function.Descendants.OfType<IfStatement>());
        function.CheckInvariant();
    }

    [Fact]
    public void BooleanFolding_CoalesceValueType_StaysIf()
    {
        var block = new Block();
        block.Add(new StoreLocal(1, s_dateTime, new LoadLocal(0, s_dateTime)));
        var thenBlock = new Block();
        thenBlock.Add(new StoreLocal(1, s_dateTime, new LoadArgument(0, "fallback", s_dateTime)));
        block.Add(new IfStatement(
            new Comparison(ComparisonKind.Equal, isUnsigned: false, new LoadLocal(0, s_dateTime), new Constant(null, s_object)),
            thenBlock,
            elseArm: null));
        block.Add(new Return(new LoadLocal(1, s_dateTime)));
        var function = FunctionWithBlock(block, s_dateTime, [s_dateTime, s_dateTime], [new Parameter("fallback", s_dateTime)]);

        new BooleanFoldingPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Coalesce>());
        Assert.Single(function.Descendants.OfType<IfStatement>());
        function.CheckInvariant();
    }

    [Fact]
    public void NullConditionalCoalesce_ConcreteValueTypeReceiver_StaysFlat()
    {
        var function = ConcreteValueTypeNullConditionalCoalesceShape();

        new NullConditionalCoalescePass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
        Assert.NotEmpty(function.Descendants.OfType<Box>());
        function.CheckInvariant();
    }

    [Fact]
    public void SlotDiamond_NullArmWithValueTypeSlot_StaysFlat()
    {
        var body = new BlockContainer();
        var head = new Block(0);
        head.Add(new ConditionalBranch(new LoadArgument(0, "c", s_bool), 20));
        var falseArm = new Block(10);
        falseArm.Add(new StoreStackSlot(0, new Constant(null, s_object)));
        falseArm.Add(new Branch(30));
        var trueArm = new Block(20);
        trueArm.Add(new StoreStackSlot(0, new LoadArgument(1, "value", s_dateTime)));
        var merge = new Block(30);
        merge.Add(new Return(new LoadStackSlot(0, s_dateTime)));
        foreach (var block in (Block[])[head, falseArm, trueArm, merge])
            body.Add(block);
        var function = new IrFunction(
            "M",
            s_owner,
            new MethodSignature(s_dateTime, [new Parameter("c", s_bool), new Parameter("value", s_dateTime)], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new SlotDiamondPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Conditional>());
        Assert.Equal(4, function.Body.Blocks.Count);
        function.CheckInvariant();
    }

    [Fact]
    public void SlotDiamond_NullArmWithShapeOnlyValueTypeSlot_StaysFlat()
    {
        var body = new BlockContainer();
        var head = new Block(0);
        head.Add(new ConditionalBranch(new LoadArgument(0, "c", s_bool), 20));
        var falseArm = new Block(10);
        falseArm.Add(new StoreStackSlot(0, new Constant(null, s_object)));
        falseArm.Add(new Branch(30));
        var trueArm = new Block(20);
        trueArm.Add(new StoreStackSlot(0, new LoadArgument(1, "value", s_shapeOnlyStruct)));
        var merge = new Block(30);
        merge.Add(new Return(new LoadStackSlot(0, s_shapeOnlyStruct)));
        foreach (var block in (Block[])[head, falseArm, trueArm, merge])
            body.Add(block);
        var function = new IrFunction(
            "M",
            s_owner,
            new MethodSignature(s_shapeOnlyStruct, [new Parameter("c", s_bool), new Parameter("value", s_shapeOnlyStruct)], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [s_shapeOnlyStruct] = TypeShape.ValueType },
        };

        new SlotDiamondPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Conditional>());
        Assert.Equal(4, function.Body.Blocks.Count);
        function.CheckInvariant();
    }

    [Fact]
    public void BooleanFolding_SlotDiamondNullArmWithValueTypeSlot_StaysIf()
    {
        var thenBlock = new Block();
        thenBlock.Add(new StoreStackSlot(0, new LoadArgument(1, "value", s_dateTime)));
        var elseBlock = new Block();
        elseBlock.Add(new StoreStackSlot(0, new Constant(null, s_object)));
        var block = new Block();
        block.Add(new IfStatement(new LoadArgument(0, "c", s_bool), thenBlock, elseBlock));
        block.Add(new Return(new LoadStackSlot(0, s_dateTime)));
        var function = FunctionWithBlock(
            block,
            s_dateTime,
            [],
            [new Parameter("c", s_bool), new Parameter("value", s_dateTime)]);

        new BooleanFoldingPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Conditional>());
        Assert.Single(function.Descendants.OfType<IfStatement>());
        function.CheckInvariant();
    }

    [Fact]
    public void SlotStoreDiamond_NullArmWithValueTypeSlot_StaysFlat()
    {
        var body = new BlockContainer();
        var head = new Block(0);
        head.Add(new ConditionalBranch(new LoadArgument(0, "c", s_bool), 20));
        var falseArm = new Block(10);
        falseArm.Add(new StoreStackSlot(0, new Constant(null, s_object)));
        falseArm.Add(new Branch(30));
        var trueArm = new Block(20);
        trueArm.Add(new StoreStackSlot(0, new LoadArgument(1, "value", s_dateTime)));
        var merge = new Block(30);
        merge.Add(new StoreLocal(0, s_dateTime, new LoadStackSlot(0, s_dateTime)));
        merge.Add(new Return(new LoadLocal(0, s_dateTime)));
        foreach (var block in (Block[])[head, falseArm, trueArm, merge])
            body.Add(block);
        var function = new IrFunction(
            "M",
            s_owner,
            new MethodSignature(s_dateTime, [new Parameter("c", s_bool), new Parameter("value", s_dateTime)], HasThis: false, GenericParameterCount: 0),
            [s_dateTime],
            body);

        new SlotStoreDiamondPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Conditional>());
        Assert.Equal(4, function.Body.Blocks.Count);
        function.CheckInvariant();
    }

    [Fact]
    public void SlotStoreDiamond_NullArmWithShapeOnlyValueTypeSlot_StaysFlat()
    {
        var body = new BlockContainer();
        var head = new Block(0);
        head.Add(new ConditionalBranch(new LoadArgument(0, "c", s_bool), 20));
        var falseArm = new Block(10);
        falseArm.Add(new StoreStackSlot(0, new Constant(null, s_object)));
        falseArm.Add(new Branch(30));
        var trueArm = new Block(20);
        trueArm.Add(new StoreStackSlot(0, new LoadArgument(1, "value", s_shapeOnlyStruct)));
        var merge = new Block(30);
        merge.Add(new StoreLocal(0, s_shapeOnlyStruct, new LoadStackSlot(0, s_shapeOnlyStruct)));
        merge.Add(new Return(new LoadLocal(0, s_shapeOnlyStruct)));
        foreach (var block in (Block[])[head, falseArm, trueArm, merge])
            body.Add(block);
        var function = new IrFunction(
            "M",
            s_owner,
            new MethodSignature(s_shapeOnlyStruct, [new Parameter("c", s_bool), new Parameter("value", s_shapeOnlyStruct)], HasThis: false, GenericParameterCount: 0),
            [s_shapeOnlyStruct],
            body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [s_shapeOnlyStruct] = TypeShape.ValueType },
        };

        new SlotStoreDiamondPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Conditional>());
        Assert.Equal(4, function.Body.Blocks.Count);
        function.CheckInvariant();
    }

    static IrFunction ConcreteValueTypeNullConditionalCoalesceShape()
    {
        const int temp = 0, receiverSlot = 1, resultSlot = 2;
        var getHashCode = new MethodRef(s_object, "GetHashCode", s_int, [], HasThis: true);
        var a = new Block(0);
        a.Add(new InitObject(s_dateTime, new LoadLocalAddress(temp, s_dateTime)));
        a.Add(new StoreStackSlot(receiverSlot, new LoadArgumentAddress(0, "value", s_dateTime)));
        a.Add(new ConditionalBranch(new Box(s_dateTime, new LoadLocal(temp, s_dateTime)), 30));

        var b = new Block(10);
        b.Add(new StoreLocal(temp, s_dateTime, new LoadIndirect(s_dateTime, new LoadStackSlot(receiverSlot, TypeRef.ByRef(s_dateTime)))));
        b.Add(new StoreStackSlot(receiverSlot, new LoadLocalAddress(temp, s_dateTime)));
        b.Add(new ConditionalBranch(new Box(s_dateTime, new LoadLocal(temp, s_dateTime)), 30));

        var falseArm = new Block(20);
        falseArm.Add(new StoreStackSlot(resultSlot, new Constant(0, s_int)));
        falseArm.Add(new Branch(40));

        var trueArm = new Block(30);
        trueArm.Add(new StoreStackSlot(resultSlot, new Call(getHashCode, isVirtual: true, [new LoadStackSlot(receiverSlot, TypeRef.ByRef(s_dateTime))]) { ConstrainedTo = s_dateTime }));

        var merge = new Block(40);
        merge.Add(new Return(new LoadStackSlot(resultSlot, s_int)));

        var body = new BlockContainer();
        foreach (var block in (Block[])[a, b, falseArm, trueArm, merge])
            body.Add(block);
        return new IrFunction(
            "M",
            s_owner,
            new MethodSignature(s_int, [new Parameter("value", s_dateTime)], HasThis: false, GenericParameterCount: 0),
            [s_dateTime],
            body);
    }

    static IrFunction FunctionReturning(IrExpression expression, TypeRef returnType, IReadOnlyList<TypeRef> locals)
    {
        var block = new Block();
        block.Add(new Return(expression));
        return FunctionWithBlock(block, returnType, locals);
    }

    static IrFunction FunctionWithBlock(
        Block block,
        TypeRef returnType,
        IReadOnlyList<TypeRef> locals,
        IReadOnlyList<Parameter>? parameters = null)
    {
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            s_owner,
            new MethodSignature(returnType, parameters is null ? [] : [.. parameters], HasThis: false, GenericParameterCount: 0),
            [.. locals],
            body);
    }
}
