using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// <see cref="EhStructuringPass"/> is transactional: a function either raises
/// completely into try/catch/finally/filter or keeps the always-correct flat form with
/// <see cref="IrFunction.Regions"/> intact. These tests pin the legality-preserving
/// bails — unsupported filter, fault, and filterless (null catch type) handlers stay flat
/// rather than emit a partial or illegal structuring — which is why those methods
/// surface as the "eh-entangled" shape instead of a structured (but wrong) shell.
/// </summary>
public class EhStructuringPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef ExceptionType = TypeRef.CoreLib("System", "Exception");
    static readonly TypeRef InvalidOperationExceptionType = TypeRef.CoreLib("System", "InvalidOperationException");
    static readonly TypeRef CustomNamedException = TypeRef.Definition("Synthetic", "Synthetic", "FakeException");
    static readonly MethodRef PredicateMethod = new(Holder, "Predicate", Bool, [], HasThis: false);
    static readonly MethodRef MakeExceptionMethod = new(Holder, "MakeException", ExceptionType, [], HasThis: false);

    static IrFunction LeaveRetryOutsideTry()
    {
        var body = new BlockContainer();

        var retry = new Block(0x0000);
        retry.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));
        body.Add(retry);

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        tryBlock.Add(new Leave(0x0000));
        body.Add(tryBlock);

        var finallyBlock = new Block(0x0020);
        finallyBlock.Add(new StoreLocal(0, Int32, new Constant(2, Int32)));
        finallyBlock.Add(new EndFinally());
        body.Add(finallyBlock);

        var tail = new Block(0x0030);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Finally,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0020,
                    HandlerLength: 0x0010,
                    FilterOffset: 0,
                    CatchType: null),
            ],
        };
    }

    static IrFunction LeaveIntoSameTry()
    {
        var body = new BlockContainer();

        var tryEntry = new Block(0x0010);
        tryEntry.Add(new Leave(0x0018));
        body.Add(tryEntry);

        var tryTarget = new Block(0x0018);
        tryTarget.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        body.Add(tryTarget);

        var finallyBlock = new Block(0x0030);
        finallyBlock.Add(new EndFinally());
        body.Add(finallyBlock);

        var tail = new Block(0x0040);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Finally,
                    TryOffset: 0x0010,
                    TryLength: 0x0020,
                    HandlerOffset: 0x0030,
                    HandlerLength: 0x0010,
                    FilterOffset: 0,
                    CatchType: null),
            ],
        };
    }

    static IrFunction LeaveFromFinally()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        body.Add(tryBlock);

        var finallyBlock = new Block(0x0020);
        finallyBlock.Add(new Leave(0x0030));
        body.Add(finallyBlock);

        var tail = new Block(0x0030);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Finally,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0020,
                    HandlerLength: 0x0010,
                    FilterOffset: 0,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegion()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0040));
        body.Add(tryBlock);

        var filterBlock = new Block(0x0020);
        filterBlock.Add(new EndFilter(new Constant(0, Int32)));
        body.Add(filterBlock);

        var handlerBlock = new Block(0x0030);
        handlerBlock.Add(new Leave(0x0040));
        body.Add(handlerBlock);

        var tail = new Block(0x0040);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0030,
                    HandlerLength: 0x0010,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithLocalPredicate()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new StoreStackSlot(0, new LoadStackSlot(256, ExceptionType)));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreLocal(0, Bool, new Constant(true, Bool)));
        trueFilter.Add(new StoreStackSlot(1, new LoadLocal(0, Bool)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new ExpressionStatement(new CaughtException(TypeRef.CoreLib("System", "Object"))));
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Bool],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithIgnoredIsInst()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new Branch(0x0030));
        body.Add(filterEntry);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithUnmodeledSideEffect()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreLocal(0, Int32, new Constant(42, Int32)));
        trueFilter.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new ExpressionStatement(new LoadLocalAddress(0, Int32)));
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithHandlerEntryVariable()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new StoreLocal(0, ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object"))));
        handlerBlock.Add(new ExpressionStatement(new LoadLocal(0, ExceptionType)));
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [ExceptionType],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithUntestedIsInst()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new StoreStackSlot(257, new IsInstance(InvalidOperationExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithPredicateUsingUntestedIsInst()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new StoreStackSlot(257, new IsInstance(InvalidOperationExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreStackSlot(1, new LoadStackSlot(257, InvalidOperationExceptionType)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithSideEffectingPredicateTemp()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreLocal(0, Bool, new Call(PredicateMethod, isVirtual: false, [])));
        trueFilter.Add(new StoreStackSlot(1, new LoadLocal(0, Bool)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Bool],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithHandlerEntryAndRepeatedFilterLocal()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreLocal(1, ExceptionType, new LoadStackSlot(256, ExceptionType)));
        trueFilter.Add(new StoreStackSlot(
            1,
            new LogicalBinary(
                LogicalKind.And,
                new Comparison(ComparisonKind.NotEqual, isUnsigned: false, new LoadLocal(1, ExceptionType), new Constant(null, ExceptionType)),
                new Comparison(ComparisonKind.NotEqual, isUnsigned: false, new LoadLocal(1, ExceptionType), new Constant(null, ExceptionType)))));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new StoreLocal(0, ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object"))));
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [ExceptionType, ExceptionType],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithFilterLocalUsedAfterHandler()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreLocal(0, ExceptionType, new LoadStackSlot(256, ExceptionType)));
        trueFilter.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new ExpressionStatement(new LoadLocal(0, ExceptionType)));
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [ExceptionType],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithHandlerEntryAndConflictingFilterLocal()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreLocal(0, ExceptionType, new Constant(null, ExceptionType)));
        trueFilter.Add(new StoreStackSlot(
            1,
            new Comparison(
                ComparisonKind.NotEqual,
                isUnsigned: false,
                new LoadLocal(0, ExceptionType),
                new Constant(null, ExceptionType))));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new StoreLocal(0, ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object"))));
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [ExceptionType],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithIgnoredAliasIndexThenSideEffectStore()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(Object))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreLocal(1, ExceptionType, new LoadStackSlot(256, ExceptionType)));
        trueFilter.Add(new StoreLocal(1, ExceptionType, new Call(MakeExceptionMethod, isVirtual: false, [])));
        trueFilter.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new StoreLocal(0, ExceptionType, new CaughtException(Object)));
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [ExceptionType, ExceptionType],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithIllegalCatchType()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(CustomNamedException, new CaughtException(Object))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, CustomNamedException), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithMismatchedHandlerEntryVariableType()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(Object))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new StoreLocal(0, Object, new CaughtException(Object)));
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Object],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithObservedPredicateLocal()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreLocal(0, Int32, new Constant(42, Int32)));
        trueFilter.Add(new StoreStackSlot(
            1,
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadLocal(0, Int32),
                new Constant(42, Int32))));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handlerBlock = new Block(0x0048);
        handlerBlock.Add(new ExpressionStatement(new LoadLocal(0, Int32)));
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0008,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterRegionWithNestedCatch()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0090));
        body.Add(tryBlock);

        var filterEntry = new Block(0x0020);
        filterEntry.Add(new StoreStackSlot(256, new IsInstance(ExceptionType, new CaughtException(TypeRef.CoreLib("System", "Object")))));
        filterEntry.Add(new ConditionalBranch(new LoadStackSlot(256, ExceptionType), 0x0030));
        body.Add(filterEntry);

        var falseFilter = new Block(0x0028);
        falseFilter.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseFilter.Add(new Branch(0x0040));
        body.Add(falseFilter);

        var trueFilter = new Block(0x0030);
        trueFilter.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        body.Add(trueFilter);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var nestedTry = new Block(0x0048);
        nestedTry.Add(new Leave(0x0060));
        body.Add(nestedTry);

        var nestedCatch = new Block(0x0050);
        nestedCatch.Add(new ExpressionStatement(new CaughtException(TypeRef.CoreLib("System", "Object"))));
        nestedCatch.Add(new Leave(0x0060));
        body.Add(nestedCatch);

        var handlerTail = new Block(0x0060);
        handlerTail.Add(new ExpressionStatement(new CaughtException(TypeRef.CoreLib("System", "Object"))));
        handlerTail.Add(new Leave(0x0090));
        body.Add(handlerTail);

        var tail = new Block(0x0090);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0048,
                    HandlerLength: 0x0048,
                    FilterOffset: 0x0020,
                    CatchType: null),
                new HandlerRegion(
                    HandlerKind.Catch,
                    TryOffset: 0x0048,
                    TryLength: 0x0008,
                    HandlerOffset: 0x0050,
                    HandlerLength: 0x0010,
                    FilterOffset: 0,
                    CatchType: ExceptionType),
            ],
        };
    }

    static IrFunction FaultRegion()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0030));
        body.Add(tryBlock);

        var faultBlock = new Block(0x0020);
        faultBlock.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        faultBlock.Add(new EndFinally());
        body.Add(faultBlock);

        var tail = new Block(0x0030);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Fault,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0020,
                    HandlerLength: 0x0010,
                    FilterOffset: 0,
                    CatchType: null),
            ],
        };
    }

    static IrFunction FilterlessCatchRegion()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0030));
        body.Add(tryBlock);

        var handlerBlock = new Block(0x0020);
        handlerBlock.Add(new Leave(0x0030));
        body.Add(handlerBlock);

        var tail = new Block(0x0030);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Catch,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0020,
                    HandlerLength: 0x0010,
                    FilterOffset: 0,
                    CatchType: null),
            ],
        };
    }

    [Fact]
    public void LeaveRetryOutsideTry_ConsumesFinallyRegion()
    {
        var function = LeaveRetryOutsideTry();

        new EhStructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Regions);
        Assert.Single(function.Descendants.OfType<TryFinally>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("try", output);
        Assert.Contains("finally", output);
    }

    [Fact]
    public void LeaveIntoSameTry_KeepsRegionFlat()
    {
        var function = LeaveIntoSameTry();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void LeaveFromFinally_KeepsRegionFlat()
    {
        var function = LeaveFromFinally();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void FilterRegion_KeepsRegionFlat()
    {
        var function = FilterRegion();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithLocalPredicate_InlinesPredicateLocal()
    {
        var function = FilterRegionWithLocalPredicate();

        new EhStructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Regions);
        var clause = Assert.Single(Assert.Single(function.Descendants.OfType<TryCatch>()).Clauses);
        Assert.NotNull(clause.Filter);
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("catch (Exception", output);
        Assert.Contains("when (true)", output);
        Assert.DoesNotContain("bool V_0", output);
    }

    [Fact]
    public void FilterRegionWithIgnoredIsInst_KeepsRegionFlat()
    {
        var function = FilterRegionWithIgnoredIsInst();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithUnmodeledSideEffect_KeepsRegionFlat()
    {
        var function = FilterRegionWithUnmodeledSideEffect();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithObservedPredicateLocal_KeepsRegionFlat()
    {
        var function = FilterRegionWithObservedPredicateLocal();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithHandlerEntryVariable_ReusesFoldedVariable()
    {
        var function = FilterRegionWithHandlerEntryVariable();

        new EhStructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Regions);
        var clause = Assert.Single(Assert.Single(function.Descendants.OfType<TryCatch>()).Clauses);
        Assert.Equal(0, clause.VariableIndex);
        Assert.Contains(clause.Body.Descendants.OfType<LoadLocal>(), load => load.Index == 0);
    }

    [Fact]
    public void FilterRegionWithUntestedIsInst_UsesTestedCatchType()
    {
        var function = FilterRegionWithUntestedIsInst();

        new EhStructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Regions);
        var clause = Assert.Single(Assert.Single(function.Descendants.OfType<TryCatch>()).Clauses);
        Assert.Equal(ExceptionType, clause.ExceptionType);
    }

    [Fact]
    public void FilterRegionWithPredicateUsingUntestedIsInst_KeepsRegionFlat()
    {
        var function = FilterRegionWithPredicateUsingUntestedIsInst();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithSideEffectingPredicateTemp_KeepsRegionFlat()
    {
        var function = FilterRegionWithSideEffectingPredicateTemp();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithHandlerEntryAndRepeatedFilterLocal_KeepsRegionFlat()
    {
        var function = FilterRegionWithHandlerEntryAndRepeatedFilterLocal();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithFilterLocalUsedAfterHandler_KeepsRegionFlat()
    {
        var function = FilterRegionWithFilterLocalUsedAfterHandler();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithHandlerEntryAndConflictingFilterLocal_KeepsRegionFlat()
    {
        var function = FilterRegionWithHandlerEntryAndConflictingFilterLocal();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithIgnoredAliasIndexThenSideEffectStore_KeepsRegionFlat()
    {
        var function = FilterRegionWithIgnoredAliasIndexThenSideEffectStore();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithIllegalCatchType_KeepsRegionFlat()
    {
        var function = FilterRegionWithIllegalCatchType();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithMismatchedHandlerEntryVariableType_KeepsRegionFlat()
    {
        var function = FilterRegionWithMismatchedHandlerEntryVariableType();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterRegionWithNestedCatch_DoesNotRewriteInnerCaughtException()
    {
        var function = FilterRegionWithNestedCatch();

        new EhStructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Regions);
        var catches = function.Descendants.OfType<TryCatch>().ToList();
        Assert.Equal(2, catches.Count);
        var outerClause = Assert.Single(catches[0].Clauses);
        var innerClause = Assert.Single(catches[1].Clauses);
        Assert.NotNull(outerClause.VariableIndex);
        if (innerClause.VariableIndex is { } innerVariable)
            Assert.NotEqual(outerClause.VariableIndex, innerVariable);
        Assert.DoesNotContain(
            innerClause.Body.Descendants.OfType<LoadLocal>(),
            load => load.Index == outerClause.VariableIndex);
    }

    [Fact]
    public void FaultRegion_KeepsRegionFlat()
    {
        var function = FaultRegion();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryFinally>());
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void FilterlessCatchRegion_KeepsRegionFlat()
    {
        var function = FilterlessCatchRegion();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }
}
