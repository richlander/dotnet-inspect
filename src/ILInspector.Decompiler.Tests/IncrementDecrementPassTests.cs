using System.Linq;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class IncrementDecrementPassTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    // Builds `tempStore; placeUpdate;` as the two statements of a single block,
    // declaring the place (slot 0) and the temporary (slot 1).
    static IrFunction Function(params IrNode[] statements)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        foreach (var statement in statements)
            block.Add(statement);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("System", "Object"), signature, [], container);
    }

    static IReadOnlyList<IrNode> Run(IrFunction function)
    {
        new IncrementDecrementPass().Run(function, PassContext.None);
        return function.Body.Blocks[0].Children;
    }

    static StoreLocal TempStore() => new(1, Int32, new LoadLocal(0, Int32));

    static StoreLocal Update(BinaryKind kind) =>
        new(0, Int32, new Binary(kind, isChecked: false, isUnsigned: false, new LoadLocal(1, Int32), new Constant(1, Int32)));

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
}
