using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StackSlotReuseRenderingTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Exception = TypeRef.CoreLib("System", "Exception");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void FoldedDiamondUsesPostDiamondStackSlotType()
    {
        var contains = new MethodRef(String, "Contains", Bool, [String], HasThis: true);
        var block = new Block(0);
        block.Add(new StoreStackSlot(256, new LoadArgument(0, "text", String)));
        block.Add(new StoreStackSlot(0, new LoadStackSlot(256, String)));
        block.Add(new IfStatement(
            new LoadStackSlot(256, String),
            BlockOf(new StoreStackSlot(0, new Call(
                contains,
                isVirtual: false,
                [new LoadStackSlot(0, String), new Constant("x", String)]))),
            BlockOf(new StoreStackSlot(0, new Constant(0, Int32)))));
        block.Add(new Return(new LoadStackSlot(0, Int32)));

        var function = Function(Int32, block);
        new BooleanFoldingPass().Run(function, PassContext.None);
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("string S_0;", output);
        Assert.Contains("int S_0_1", output);
        Assert.Contains("S_0_1 = S_256 is not null", output);
        Assert.Contains("S_0.Contains(\"x\") ? 1 : 0", output);
        Assert.Contains("return S_0_1;", output);
    }

    [Fact]
    public void BooleanSlotMaterializationIgnoresUnrelatedSameNumberLiveRange()
    {
        var consumeObject = new MethodRef(Holder, "ConsumeObject", Void, [Object], HasThis: false);
        var consumeBool = new MethodRef(Holder, "ConsumeBool", Void, [Bool], HasThis: false);
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", Object)));
        block.Add(new ExpressionStatement(new Call(consumeObject, isVirtual: false, [new LoadStackSlot(0, Object)])));
        block.Add(new StoreStackSlot(0, new Constant(0, Int32)));
        block.Add(new StoreStackSlot(0, new LoadArgument(1, "flag", Bool)));
        block.Add(new ExpressionStatement(new Call(
            consumeBool,
            isVirtual: false,
            [new Binary(BinaryKind.Or, isChecked: false, isUnsigned: false, new LoadStackSlot(0, Int32), new LoadArgument(2, "other", Bool))])));
        block.Add(new Return(null));

        var function = Function(Void, block);
        new BooleanFoldingPass().Run(function, PassContext.None);
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("object S_0;", output);
        Assert.Contains("bool S_0_1;", output);
        Assert.DoesNotContain("int S_0_1", output);
        Assert.Contains("S_0_1 = false;", output);
        Assert.Contains("S_0_1 = flag;", output);
        Assert.Contains("ConsumeBool(S_0_1 | other);", output);
    }

    [Fact]
    public void BoolSlotWithIntegerTypedConsumerUsesBoolNameWhenTargetIsBool()
    {
        var field = new FieldRef(Holder, "Flag", Bool);
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Conditional(
            new LoadArgument(0, "isValueType", Bool),
            new Constant(false, Bool),
            new LoadArgument(1, "hasDefaultConstructor", Bool))
        { MergedType = Bool }));
        block.Add(new StoreField(field, null, new LoadStackSlot(0, Int32)));
        block.Add(new Return(null));

        var output = CSharpPrinter.Print(Function(Void, block)).Output!;

        Assert.Contains("bool S_0", output);
        Assert.Contains("Flag = S_0;", output);
        Assert.DoesNotContain("int S_0_1", output);
        Assert.DoesNotContain("Flag = S_0_1;", output);
    }

    [Fact]
    public void SubtypeStoreSupertypeLoadStaysOneVariable()
    {
        var consumeObject = new MethodRef(Holder, "ConsumeObject", Void, [Object], HasThis: false);
        var block = new Block(0);
        block.Add(new IfStatement(
            new LoadArgument(0, "flag", Bool),
            BlockOf(new StoreStackSlot(0, new LoadArgument(1, "s", String))),
            BlockOf(new StoreStackSlot(0, new LoadArgument(2, "o", Object)))));
        block.Add(new IfStatement(
            new LoadArgument(3, "useException", Bool),
            BlockOf(new StoreStackSlot(0, new LoadArgument(4, "e", Exception))),
            null));
        block.Add(new ExpressionStatement(new Call(consumeObject, isVirtual: false, [new LoadStackSlot(0, Object)])));
        block.Add(new Return(null));

        var output = CSharpPrinter.Print(Function(Void, block)).Output!;

        Assert.Contains("object S_0;", output);
        Assert.DoesNotContain("S_0_1", output);
        Assert.Contains("S_0 = s;", output);
        Assert.Contains("S_0 = o;", output);
        Assert.Contains("S_0 = e;", output);
        Assert.Contains("ConsumeObject(S_0);", output);
    }

    static Block BlockOf(IrNode statement)
    {
        var block = new Block(0);
        block.Add(statement);
        return block;
    }

    static IrFunction Function(TypeRef returnType, Block block)
    {
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction("M", Holder, new MethodSignature(returnType, [], HasThis: false, GenericParameterCount: 0), [], body);
    }
}
