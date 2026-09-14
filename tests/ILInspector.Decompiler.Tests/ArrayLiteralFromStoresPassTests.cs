using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #2877: ArrayLiteralFromStoresPass folds a compiler-emitted array allocation
// plus a later contiguous index-store run (the ubiquitous params-array spill)
// into one ArrayLiteral, placed at the fill run's position. These tests cover
// the fold's happy path and the guards that keep it from misfiring on shapes
// that are not a faithful array-literal initializer.
[Trait("Area", "Pass")]
public class ArrayLiteralFromStoresPassTests
{
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef ObjectArray = TypeRef.SzArray(Object);
    static readonly TypeRef StringType = TypeRef.CoreLib("System", "String");
    static readonly TypeRef StringArray = TypeRef.SzArray(StringType);
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Derived = TypeRef.CoreLib("System", "MyType");

    static MethodRef Sink(int argCount) => new(Derived, "Sink", Void, [.. Enumerable.Repeat(Object, argCount)], HasThis: false);
    static MethodRef Effect(string name) => new(Derived, name, Object, [], HasThis: false);

    static IrFunction Build(params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Void, [], HasThis: true, GenericParameterCount: 0);
        return new IrFunction("M", Derived, signature, [], container);
    }

    static void RunPass(IrFunction function) => new ArrayLiteralFromStoresPass().Run(function, PassContext.None);

    static int NewArrayCount(IrFunction function) => function.Descendants.OfType<NewArray>().Count();
    static int ArrayLiteralCount(IrFunction function) => function.Descendants.OfType<ArrayLiteral>().Count();

    // T[] tmp = new T[2]; tmp[0] = A(); tmp[1] = B(); Sink(tmp);
    // folds to T[] tmp = new T[] { A(), B() }; Sink(tmp); at the fill run's position.
    [Fact]
    public void ContiguousFillRun_FoldsToArrayLiteral()
    {
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(Object, new Constant(2, TypeRef.CoreLib("System", "Int32")))),
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Call(Effect("A"), isVirtual: false, [])),
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(1, TypeRef.CoreLib("System", "Int32")), new Call(Effect("B"), isVirtual: false, [])),
            call);

        RunPass(function);

        Assert.Equal(0, NewArrayCount(function));
        Assert.Equal(1, ArrayLiteralCount(function));
        var literal = function.Descendants.OfType<ArrayLiteral>().Single();
        Assert.Equal(2, literal.Elements.Count);
        Assert.Equal("A", ((Call)literal.Elements[0]).Callee.Name);
        Assert.Equal("B", ((Call)literal.Elements[1]).Callee.Name);
        function.CheckInvariant();
    }

    // The combined literal store must land at the fill run's position, not the
    // allocation's — the fill run may sit past an intervening effectful call
    // (e.g. a chained receiver call), and only the allocation is reorder-safe.
    [Fact]
    public void FillRunPastInterveningEffect_FoldsAtFillRunPosition()
    {
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var between = new ExpressionStatement(new Call(Effect("Between"), isVirtual: false, []));
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(Object, new Constant(1, TypeRef.CoreLib("System", "Int32")))),
            between,
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Call(Effect("A"), isVirtual: false, [])),
            call);

        RunPass(function);

        Assert.Equal(1, ArrayLiteralCount(function));
        var statements = function.Descendants.OfType<Block>().First().Children.ToList();
        int betweenIndex = statements.IndexOf(between);
        int literalIndex = statements.FindIndex(n => n is StoreLocal { Value: ArrayLiteral });
        Assert.True(literalIndex > betweenIndex, "the combined literal must fold at the fill run's position, after the intervening effect");
        function.CheckInvariant();
    }

    // A write to the array place between the allocation and the fill run means
    // the fill run is not filling the original allocation unmutated — decline.
    [Fact]
    public void WriteBetweenAllocationAndFillRun_DoesNotFold()
    {
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(Object, new Constant(1, TypeRef.CoreLib("System", "Int32")))),
            new StoreLocal(0, ObjectArray, new LoadLocal(0, ObjectArray)),
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Call(Effect("A"), isVirtual: false, [])),
            call);

        RunPass(function);

        Assert.Equal(0, ArrayLiteralCount(function));
        function.CheckInvariant();
    }

    // Non-contiguous / out-of-order element indices are not a faithful
    // array-literal initializer shape — decline.
    [Fact]
    public void OutOfOrderIndices_DoesNotFold()
    {
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(Object, new Constant(2, TypeRef.CoreLib("System", "Int32")))),
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(1, TypeRef.CoreLib("System", "Int32")), new Call(Effect("B"), isVirtual: false, [])),
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Call(Effect("A"), isVirtual: false, [])),
            call);

        RunPass(function);

        Assert.Equal(0, ArrayLiteralCount(function));
        function.CheckInvariant();
    }

    // A second read of the array between the allocation and the fill run
    // observes a partially-built array — decline.
    [Fact]
    public void ExtraLoadBeforeFillRun_DoesNotFold()
    {
        var early = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(Object, new Constant(1, TypeRef.CoreLib("System", "Int32")))),
            early,
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Call(Effect("A"), isVirtual: false, [])),
            call);

        RunPass(function);

        Assert.Equal(0, ArrayLiteralCount(function));
        function.CheckInvariant();
    }

    // A second read after the fill run (beyond the one expected consuming use)
    // means the array is used more than once downstream — the shared reference
    // could observe further mutation, so this is not a faithful literal — decline.
    [Fact]
    public void ExtraLoadAfterFillRun_DoesNotFold()
    {
        var call1 = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var call2 = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(Object, new Constant(1, TypeRef.CoreLib("System", "Int32")))),
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Call(Effect("A"), isVirtual: false, [])),
            call1,
            call2);

        RunPass(function);

        Assert.Equal(0, ArrayLiteralCount(function));
        function.CheckInvariant();
    }

    // No consuming read at all after the fill run: nothing to fold toward — decline.
    [Fact]
    public void NoConsumingLoad_DoesNotFold()
    {
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(Object, new Constant(1, TypeRef.CoreLib("System", "Int32")))),
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Call(Effect("A"), isVirtual: false, [])));

        RunPass(function);

        Assert.Equal(0, ArrayLiteralCount(function));
        function.CheckInvariant();
    }

    // A stack-slot-backed allocation (StoreStackSlot) folds the same way as a
    // local-backed one.
    [Fact]
    public void StackSlotBackedAllocation_FoldsToArrayLiteral()
    {
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadStackSlot(0, ObjectArray)]));
        var function = Build(
            new StoreStackSlot(0, new NewArray(Object, new Constant(1, TypeRef.CoreLib("System", "Int32")))),
            new StoreElement(Object, new LoadStackSlot(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Call(Effect("A"), isVirtual: false, [])),
            call);

        RunPass(function);

        Assert.Equal(1, ArrayLiteralCount(function));
        function.CheckInvariant();
    }

    // A zero-length array allocation has no fill run to look for — decline
    // (nothing to fold; the NewArray shape stays as-is, not a degenerate
    // zero-element ArrayLiteral).
    [Fact]
    public void ZeroLengthArray_DoesNotFold()
    {
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(Object, new Constant(0, TypeRef.CoreLib("System", "Int32")))),
            call);

        RunPass(function);

        Assert.Equal(0, ArrayLiteralCount(function));
        Assert.Equal(1, NewArrayCount(function));
        function.CheckInvariant();
    }

    // A fill value that reads the place itself (a self-referential
    // `tmp[0] = tmp;`) must not fold: ArrayLiteral evaluates every element
    // before the combined store commits the array reference, so a folded
    // element reading the place would observe the place's *prior* value
    // instead of the array being built (adversarial review finding).
    [Fact]
    public void FillValueReadsThePlaceItself_DoesNotFold()
    {
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(Object, new Constant(1, TypeRef.CoreLib("System", "Int32")))),
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new LoadLocal(0, ObjectArray)),
            call);

        RunPass(function);

        Assert.Equal(0, ArrayLiteralCount(function));
        function.CheckInvariant();
    }

    // Array covariance: `object[] tmp = new string[1];` is legal C#, and each
    // stelem is runtime-checked (ArrayTypeMismatchException on a non-string
    // value). Folding to `object[] tmp = new string[] { value };` would
    // require every value to typecheck against string, which the pass cannot
    // prove — decline whenever the declared local's array type differs from
    // the allocated array's element type (adversarial review finding).
    [Fact]
    public void CovariantArrayDeclaredTypeDiffersFromAllocatedElementType_DoesNotFold()
    {
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(StringType, new Constant(1, TypeRef.CoreLib("System", "Int32")))),
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Call(Effect("A"), isVirtual: false, [])),
            call);

        RunPass(function);

        Assert.Equal(0, ArrayLiteralCount(function));
        function.CheckInvariant();
    }

    // The non-covariant case (declared local type matches the allocated
    // element type exactly, here both string[]) still folds normally.
    [Fact]
    public void MatchingDeclaredAndAllocatedElementType_StillFolds()
    {
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, StringArray)]));
        var function = Build(
            new StoreLocal(0, StringArray, new NewArray(StringType, new Constant(1, TypeRef.CoreLib("System", "Int32")))),
            new StoreElement(StringType, new LoadLocal(0, StringArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Call(new MethodRef(Derived, "S", StringType, [], HasThis: false), isVirtual: false, [])),
            call);

        RunPass(function);

        Assert.Equal(1, ArrayLiteralCount(function));
        function.CheckInvariant();
    }

    // A catch clause that (re)binds a pre-existing local index to the caught
    // exception is another structured write hazard: if the array's local
    // index is reused as the exception variable between the allocation and
    // the fill run, folding onto it would discard the caught exception
    // (adversarial review finding, second pass).
    [Fact]
    public void CatchClauseRebindsPlaceBeforeFillRun_DoesNotFold()
    {
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var catchClause = new CatchClause(Object, new BlockContainer()) { VariableIndex = 0 };
        var tryCatch = new TryCatch(new BlockContainer(), [catchClause]);
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(Object, new Constant(1, TypeRef.CoreLib("System", "Int32")))),
            tryCatch,
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Constant("A", StringType)),
            call);

        RunPass(function);

        Assert.Equal(0, ArrayLiteralCount(function));
        function.CheckInvariant();
    }

    // A deconstruction assignment re-targeting a pre-existing local (not a
    // StoreLocal) between the allocation and the fill run overwrites the
    // place through a structured node the pass must also recognize as a
    // write, or it would fold the fill run onto the wrong array reference
    // (adversarial review finding).
    [Fact]
    public void DeconstructionAssignmentOverwritesPlaceBeforeFillRun_DoesNotFold()
    {
        var call = new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)]));
        var deconstruct = new DeconstructionAssignment(
            [DeconstructionTarget.Local(0, ObjectArray, isDeclared: false), DeconstructionTarget.Local(1, Object, isDeclared: true)],
            new Call(new MethodRef(Derived, "MethodReturningTuple", Object, [], HasThis: false), isVirtual: false, []));
        var function = Build(
            new StoreLocal(0, ObjectArray, new NewArray(Object, new Constant(1, TypeRef.CoreLib("System", "Int32")))),
            deconstruct,
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Constant("A", StringType)),
            call);

        RunPass(function);

        Assert.Equal(0, ArrayLiteralCount(function));
        function.CheckInvariant();
    }

    // The fold consumes the newarr, and offset-keyed facts (alloc.array) are
    // anchored by the offsets surviving in the tree. If the literal did not
    // adopt the allocation's offset, that offset would exist nowhere and the
    // fact would anchor to whatever statement precedes it.
    [Fact]
    public void FoldedLiteral_AdoptsTheAllocationsSourceOffset()
    {
        var allocation = new NewArray(Object, new Constant(1, TypeRef.CoreLib("System", "Int32")));
        allocation.SetSourceOffset(7);
        var store = new StoreLocal(0, ObjectArray, allocation);
        store.SetSourceOffset(11);
        var function = Build(
            store,
            new StoreElement(Object, new LoadLocal(0, ObjectArray), new Constant(0, TypeRef.CoreLib("System", "Int32")), new Constant("A", StringType)),
            new ExpressionStatement(new Call(Sink(1), isVirtual: false, [new LoadLocal(0, ObjectArray)])));

        RunPass(function);

        var literal = Assert.Single(function.Descendants.OfType<ArrayLiteral>());
        Assert.Equal(7, literal.SourceOffset);
        function.CheckInvariant();
    }
}
