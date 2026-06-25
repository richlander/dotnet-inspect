using System.Linq;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class IncrementDecrementPassTests
{
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef EnumType = TypeRef.Definition("Test", "Synthetic", "Mode", ValueTypeHint.ValueType);
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef UnknownValueType = TypeRef.Definition("External", "Synthetic", "Mode", ValueTypeHint.ValueType);
    static readonly TypeRef ValueType = TypeRef.Definition("Test", "Synthetic", "StructLike", ValueTypeHint.ValueType);

    // Builds `tempStore; placeUpdate;` as the two statements of a single block,
    // declaring the place (slot 0) and the temporary (slot 1).
    static IrFunction Function(params IrNode[] statements)
        => FunctionWithSignature(TypeRef.CoreLib("System", "Void"), [], statements);

    static IrFunction FunctionWithSignature(TypeRef returnType, TypeRef[] locals, params IrNode[] statements)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        foreach (var statement in statements)
            block.Add(statement);
        var signature = new MethodSignature(returnType, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("System", "Object"), signature, [.. locals], container);
    }

    static IReadOnlyList<IrNode> Run(IrFunction function)
    {
        new IncrementDecrementPass().Run(function, PassContext.None);
        return function.Body.Blocks[0].Children;
    }

    static StoreLocal TempStore() => new(1, Int32, new LoadLocal(0, Int32));

    static StoreLocal Update(BinaryKind kind) =>
        new(0, Int32, new Binary(kind, isChecked: false, isUnsigned: false, new LoadLocal(1, Int32), new Constant(1, Int32)));

    static StoreLocal BoolTempStore() => new(1, Boolean, new LoadLocal(0, Boolean));

    static StoreLocal BoolUpdateFromTemp() =>
        new(0, Boolean, new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadLocal(1, Boolean), new Constant(1, Int32)));

    static StoreLocal EnumTempStore() => new(1, EnumType, new LoadLocal(0, EnumType));

    static StoreLocal EnumUpdateFromTemp() =>
        new(0, EnumType, new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadLocal(1, EnumType), new Constant(1, Int32)));

    static StoreLocal UnknownValueTypeTempStore() => new(1, UnknownValueType, new LoadLocal(0, UnknownValueType));

    static StoreLocal UnknownValueTypeUpdateFromTemp() =>
        new(0, UnknownValueType, new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadLocal(1, UnknownValueType), new Constant(1, Int32)));

    static StoreLocal ValueTypeTempStore() => new(1, ValueType, new LoadLocal(0, ValueType));

    static StoreLocal ValueTypeUpdateFromTemp() =>
        new(0, ValueType, new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadLocal(1, ValueType), new Constant(1, Int32)));

    [Fact]
    public void DeadTempPostIncrement_FoldsToOperator()
    {
        // V_1 = x; x = V_1 + 1;  ->  x++;
        var statements = Run(Function(TempStore(), Update(BinaryKind.Add)));

        var statement = Assert.Single(statements);
        var increment = Assert.IsType<IncrementDecrement>(Assert.IsType<ExpressionStatement>(statement).Expression);
        Assert.True(increment.IsIncrement);
        Assert.False(increment.IsPrefix);
        var target = Assert.IsType<LoadLocal>(increment.Target);
        Assert.Equal(0, target.Index);
    }

    [Fact]
    public void DeadTempPostDecrement_FoldsToOperator()
    {
        // V_1 = x; x = V_1 - 1;  ->  x--;
        var statements = Run(Function(TempStore(), Update(BinaryKind.Subtract)));

        var increment = Assert.IsType<IncrementDecrement>(Assert.IsType<ExpressionStatement>(Assert.Single(statements)).Expression);
        Assert.False(increment.IsIncrement);
    }

    [Fact]
    public void KnownEnumDeadTempPostIncrement_FoldsToOperator()
    {
        var function = Function(EnumTempStore(), EnumUpdateFromTemp());
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [EnumType] = TypeShape.Enum };

        var statements = Run(function);

        var increment = Assert.IsType<IncrementDecrement>(Assert.IsType<ExpressionStatement>(Assert.Single(statements)).Expression);
        Assert.True(increment.IsIncrement);
    }

    [Fact]
    public void UnknownValueTypeDeadTempPostIncrement_FoldsAsPotentialEnum()
    {
        var function = Function(UnknownValueTypeTempStore(), UnknownValueTypeUpdateFromTemp());
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [UnknownValueType] = TypeShape.Unknown };

        var statements = Run(function);

        var increment = Assert.IsType<IncrementDecrement>(Assert.IsType<ExpressionStatement>(Assert.Single(statements)).Expression);
        Assert.True(increment.IsIncrement);
    }

    [Fact]
    public void KnownValueTypeDeadTempPostIncrement_IsMarkedUnsupported()
    {
        var function = Function(ValueTypeTempStore(), ValueTypeUpdateFromTemp());
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [ValueType] = TypeShape.ValueType };

        var statements = Run(function);

        var unsupported = Assert.IsType<UnsupportedNode>(Assert.IsType<ExpressionStatement>(Assert.Single(statements)).Expression);
        Assert.Contains("++", unsupported.Reason);
    }

    [Fact]
    public void ReusedTemp_IsNotFolded()
    {
        // The temporary is written twice (V_1 also feeds an unrelated store), so
        // it is not a single-use dead temp — folding would be unsound.
        var statements = Run(Function(
            TempStore(),
            Update(BinaryKind.Add),
            new StoreLocal(1, Int32, new Constant(7, Int32))));

        Assert.Equal(3, statements.Count);
        Assert.IsType<StoreLocal>(statements[0]);
    }

    [Fact]
    public void NonUnitStep_IsNotFolded()
    {
        // V_1 = x; x = V_1 + 2; is a `+= 2`, not a post-increment.
        var update = new StoreLocal(0, Int32,
            new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadLocal(1, Int32), new Constant(2, Int32)));
        var statements = Run(Function(TempStore(), update));

        Assert.Equal(2, statements.Count);
    }

    [Fact]
    public void MismatchedPlace_IsNotFolded()
    {
        // V_1 = x; y = V_1 + 1; updates a different place than it captured.
        var temp = new StoreLocal(1, Int32, new LoadLocal(0, Int32));
        var update = new StoreLocal(2, Int32,
            new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadLocal(1, Int32), new Constant(1, Int32)));
        var statements = Run(Function(temp, update));

        Assert.Equal(2, statements.Count);
    }

    [Fact]
    public void CheckedStep_IsNotFolded()
    {
        // A checked increment is left alone — the printer would not fold it and
        // the operator would lose the overflow context.
        var update = new StoreLocal(0, Int32,
            new Binary(BinaryKind.Add, isChecked: true, isUnsigned: false, new LoadLocal(1, Int32), new Constant(1, Int32)));
        var statements = Run(Function(TempStore(), update));

        Assert.Equal(2, statements.Count);
    }

    [Fact]
    public void BoolValueProducingDupShape_IsMarkedUnsupported()
    {
        var statements = Run(Function(
            new StoreStackSlot(0, new LoadLocal(0, Boolean)),
            new StoreLocal(0, Boolean,
                new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadStackSlot(0, Boolean), new Constant(1, Int32))),
            new Return(new LoadStackSlot(0, Boolean))));

        Assert.Equal(3, statements.Count);
        Assert.IsType<StoreStackSlot>(statements[0]);
        Assert.IsType<UnsupportedNode>(Assert.IsType<ExpressionStatement>(statements[1]).Expression);
        Assert.Empty(statements.OfType<Return>().SelectMany(r => r.Descendants).OfType<IncrementDecrement>());
    }

    [Fact]
    public void BoolValueProducingDupShape_RendersPartialUnsupported()
    {
        var function = FunctionWithSignature(Boolean, [Boolean],
            new StoreStackSlot(0, new LoadLocal(0, Boolean)),
            new StoreLocal(0, Boolean,
                new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadStackSlot(0, Boolean), new Constant(1, Int32))),
            new Return(new LoadStackSlot(0, Boolean)));

        var result = CSharpPrinter.PrintRaised(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.DoesNotContain("V_0++", result.Output);
        Assert.DoesNotContain("S_0 + 1", result.Output);
        Assert.Contains("Unsupported", result.Output);
    }

    [Fact]
    public void BoolDeadTempStatement_IsMarkedUnsupported()
    {
        var statements = Run(Function(BoolTempStore(), BoolUpdateFromTemp()));

        var unsupported = Assert.IsType<UnsupportedNode>(Assert.IsType<ExpressionStatement>(Assert.Single(statements)).Expression);
        Assert.Contains("++", unsupported.Reason);
        Assert.Empty(statements.SelectMany(s => s.Descendants).OfType<IncrementDecrement>());
    }

    // Builds a `for (place = 0; place < 2; place = temp + 1) { ...head; temp = place; }`
    // loop — the post-increment-through-spill shape iterator reconstruction leaves.
    static ForLoop ForLoopWithTemp(int place, int temp, params IrNode[] bodyHead)
    {
        var init = new StoreLocal(place, Int32, new Constant(0, Int32));
        var condition = new Comparison(ComparisonKind.LessThan, isUnsigned: false,
            new LoadLocal(place, Int32), new Constant(2, Int32));
        var increment = new StoreLocal(place, Int32,
            new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadLocal(temp, Int32), new Constant(1, Int32)));
        var body = new Block(0);
        foreach (var statement in bodyHead)
            body.Add(statement);
        body.Add(new StoreLocal(temp, Int32, new LoadLocal(place, Int32)));
        return new ForLoop(init, condition, increment, body);
    }

    static int IncrementOperandIndex(ForLoop loop) =>
        ((LoadLocal)((Binary)((StoreLocal)loop.Increment).Value).Left).Index;

    [Fact]
    public void ForLoopTempIncrement_InlinesTempAndDropsCapture()
    {
        var loop = ForLoopWithTemp(place: 0, temp: 1, new StoreLocal(8, Int32, new Constant(5, Int32)));
        new IncrementDecrementPass().Run(Function(loop), PassContext.None);

        // The update reads the place itself now (self-referential `place = place + 1`,
        // which the printer spells `place++`), and the capture statement is gone.
        Assert.Equal(0, IncrementOperandIndex(loop));
        Assert.DoesNotContain(loop.Body.Children.OfType<StoreLocal>(), s => s.Index == 1);
        Assert.Single(loop.Body.Children);
    }

    [Fact]
    public void SharedTempAcrossNestedForLoops_FoldsBothLoops()
    {
        // A single temp slot (1) serves both the outer (place 0) and inner
        // (place 2) loops, as in the reconstructed YieldGrid nest. The fold proves
        // the temp is used only in these dup idioms and inlines both.
        var inner = ForLoopWithTemp(place: 2, temp: 1);
        var outer = ForLoopWithTemp(place: 0, temp: 1, inner);
        new IncrementDecrementPass().Run(Function(outer), PassContext.None);

        Assert.Equal(0, IncrementOperandIndex(outer));
        Assert.Equal(2, IncrementOperandIndex(inner));
        Assert.DoesNotContain(outer.Body.Children.OfType<StoreLocal>(), s => s.Index == 1);
        Assert.DoesNotContain(inner.Body.Children.OfType<StoreLocal>(), s => s.Index == 1);
    }

    [Fact]
    public void ForLoopTemp_ReadElsewhere_IsNotFolded()
    {
        // The temp also feeds an unrelated read, so it is not purely the dup spill
        // — inlining could drop an observed value, so the loop is left alone.
        var loop = ForLoopWithTemp(place: 0, temp: 1, new StoreLocal(8, Int32, new Constant(5, Int32)));
        var otherRead = new StoreLocal(9, Int32, new LoadLocal(1, Int32));
        new IncrementDecrementPass().Run(Function(loop, otherRead), PassContext.None);

        Assert.Equal(1, IncrementOperandIndex(loop));
        Assert.Equal(2, loop.Body.Children.Count);
    }

    [Fact]
    public void BoolForLoopTempIncrement_IsMarkedUnsupported()
    {
        var init = new StoreLocal(0, Boolean, new Constant(false, Boolean));
        var condition = new LoadArgument(0, "keepGoing", Boolean);
        var increment = new StoreLocal(0, Boolean,
            new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadLocal(1, Boolean), new Constant(1, Int32)));
        var body = new Block(0);
        body.Add(new StoreLocal(1, Boolean, new LoadLocal(0, Boolean)));
        var loop = new ForLoop(init, condition, increment, body);

        new IncrementDecrementPass().Run(Function(loop), PassContext.None);

        var unsupported = Assert.IsType<UnsupportedNode>(Assert.IsType<ExpressionStatement>(loop.Increment).Expression);
        Assert.Contains("++", unsupported.Reason);
        Assert.Empty(loop.Body.Children);
    }
}
