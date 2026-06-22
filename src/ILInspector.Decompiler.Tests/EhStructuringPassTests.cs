using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// <see cref="EhStructuringPass"/> is transactional: a function either raises
/// completely into try/catch/finally or keeps the always-correct flat form with
/// <see cref="IrFunction.Regions"/> intact. These tests pin the legality-preserving
/// bails — unsupported filters, fault, and filterless (null catch type) handlers
/// stay flat rather than emit a partial or illegal structuring — which is why
/// those methods surface as the "eh-entangled" shape instead of a structured
/// (but wrong) shell.
/// </summary>
public class EhStructuringPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Exception = TypeRef.CoreLib("System", "Exception");
    static readonly TypeRef ExceptionHandling = TypeRef.CoreLib("System", "ExceptionHandling");
    static readonly TypeRef FormatException = TypeRef.CoreLib("System", "FormatException");
    static readonly TypeRef IOException = TypeRef.CoreLib("System.IO", "IOException");

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

    static IrFunction IOExceptionFilterRegion()
    {
        var body = new BlockContainer();
        var getInnerException = new MethodRef(Exception, "get_InnerException", Exception, [], HasThis: true);

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0060));
        body.Add(tryBlock);

        var typeTest = new Block(0x0020);
        typeTest.Add(new StoreStackSlot(256, new IsInstance(Exception, new CaughtException(null))));
        typeTest.Add(new StoreStackSlot(0, new LoadStackSlot(256, Exception)));
        typeTest.Add(new ConditionalBranch(new LoadStackSlot(256, Exception), 0x0030));
        body.Add(typeTest);

        var falseArm = new Block(0x0028);
        falseArm.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseArm.Add(new Branch(0x0048));
        body.Add(falseArm);

        var typedException = new Block(0x0030);
        typedException.Add(new StoreLocal(0, Exception, new LoadStackSlot(0, Exception)));
        typedException.Add(new ConditionalBranch(new IsInstance(IOException, new LoadLocal(0, Exception)), 0x0040));
        body.Add(typedException);

        var innerExceptionTest = new Block(0x0038);
        innerExceptionTest.Add(new StoreStackSlot(
            2,
            new Comparison(
                ComparisonKind.GreaterThan,
                isUnsigned: true,
                new IsInstance(IOException, new Call(getInnerException, isVirtual: true, [new LoadLocal(0, Exception)])),
                new Constant(null, Exception))));
        innerExceptionTest.Add(new Branch(0x0044));
        body.Add(innerExceptionTest);

        var directMatch = new Block(0x0040);
        directMatch.Add(new StoreStackSlot(2, new Constant(1, Int32)));
        body.Add(directMatch);

        var join = new Block(0x0044);
        join.Add(new StoreStackSlot(
            1,
            new Comparison(
                ComparisonKind.GreaterThan,
                isUnsigned: true,
                new LoadStackSlot(2, Int32),
                new Constant(0, Int32))));
        body.Add(join);

        var endFilter = new Block(0x0048);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Int32)));
        body.Add(endFilter);

        var handler = new Block(0x0050);
        handler.Add(new ExpressionStatement(new CaughtException(null)));
        handler.Add(new Leave(0x0060));
        body.Add(handler);

        var tail = new Block(0x0060);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Exception],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0050,
                    HandlerLength: 0x0010,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction SimpleCatchAllFilterRegion()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0040));
        body.Add(tryBlock);

        var filterBlock = new Block(0x0020);
        filterBlock.Add(new ExpressionStatement(new CaughtException(null)));
        filterBlock.Add(new EndFilter(new LoadArgument(0, "handle", TypeRef.CoreLib("System", "Boolean"))));
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
            new MethodSignature(Void, [new Parameter("handle", TypeRef.CoreLib("System", "Boolean"))], HasThis: false, GenericParameterCount: 0),
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

    static IrFunction ExceptionCaptureFilterRegion()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0060));
        body.Add(tryBlock);

        var typeTest = new Block(0x0020);
        typeTest.Add(new StoreStackSlot(256, new IsInstance(Exception, new CaughtException(null))));
        typeTest.Add(new StoreStackSlot(0, new LoadStackSlot(256, Exception)));
        typeTest.Add(new ConditionalBranch(new LoadStackSlot(256, Exception), 0x0030));
        body.Add(typeTest);

        var falseArm = new Block(0x0028);
        falseArm.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseArm.Add(new Branch(0x0040));
        body.Add(falseArm);

        var typedException = new Block(0x0030);
        typedException.Add(new StoreLocal(0, Exception, new LoadStackSlot(0, Exception)));
        typedException.Add(new StoreStackSlot(
            1,
            new Comparison(
                ComparisonKind.GreaterThan,
                isUnsigned: true,
                new LoadArgument(0, "captureException", Boolean),
                new Constant(false, Boolean))));
        body.Add(typedException);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Boolean)));
        body.Add(endFilter);

        var handler = new Block(0x0050);
        handler.Add(new ExpressionStatement(new CaughtException(null)));
        handler.Add(new StoreLocal(1, Exception, new LoadLocal(0, Exception)));
        handler.Add(new Leave(0x0060));
        body.Add(handler);

        var tail = new Block(0x0060);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [new Parameter("captureException", Boolean)], HasThis: false, GenericParameterCount: 0),
            [Exception, Exception],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0050,
                    HandlerLength: 0x0010,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction GlobalExceptionHandlerFilterRegion()
    {
        var body = new BlockContainer();
        var isHandledByGlobalHandler = new MethodRef(
            ExceptionHandling,
            "IsHandledByGlobalHandler",
            Boolean,
            [Exception],
            HasThis: false);

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0060));
        body.Add(tryBlock);

        var typeTest = new Block(0x0020);
        typeTest.Add(new StoreStackSlot(256, new IsInstance(Exception, new CaughtException(null))));
        typeTest.Add(new StoreStackSlot(0, new LoadStackSlot(256, Exception)));
        typeTest.Add(new ConditionalBranch(new LoadStackSlot(256, Exception), 0x0030));
        body.Add(typeTest);

        var falseArm = new Block(0x0028);
        falseArm.Add(new StoreStackSlot(1, new Constant(0, Int32)));
        falseArm.Add(new Branch(0x0040));
        body.Add(falseArm);

        var typedException = new Block(0x0030);
        typedException.Add(new StoreStackSlot(
            1,
            new Comparison(
                ComparisonKind.GreaterThan,
                isUnsigned: true,
                new Call(isHandledByGlobalHandler, isVirtual: false, [new LoadStackSlot(0, Exception)]),
                new Constant(false, Boolean))));
        body.Add(typedException);

        var endFilter = new Block(0x0040);
        endFilter.Add(new EndFilter(new LoadStackSlot(1, Boolean)));
        body.Add(endFilter);

        var handler = new Block(0x0050);
        handler.Add(new ExpressionStatement(new CaughtException(null)));
        handler.Add(new Leave(0x0060));
        body.Add(handler);

        var tail = new Block(0x0060);
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
                    HandlerOffset: 0x0050,
                    HandlerLength: 0x0010,
                    FilterOffset: 0x0020,
                    CatchType: null),
            ],
        };
    }

    static IrFunction CatchThenFilterRegion()
    {
        var body = new BlockContainer();

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new Leave(0x0050));
        body.Add(tryBlock);

        var catchBlock = new Block(0x0020);
        catchBlock.Add(new ExpressionStatement(new CaughtException(FormatException)));
        catchBlock.Add(new Leave(0x0050));
        body.Add(catchBlock);

        var filterBlock = new Block(0x0030);
        filterBlock.Add(new ExpressionStatement(new CaughtException(null)));
        filterBlock.Add(new EndFilter(new LoadArgument(0, "handle", TypeRef.CoreLib("System", "Boolean"))));
        body.Add(filterBlock);

        var handlerBlock = new Block(0x0040);
        handlerBlock.Add(new ExpressionStatement(new CaughtException(null)));
        handlerBlock.Add(new Leave(0x0050));
        body.Add(handlerBlock);

        var tail = new Block(0x0050);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [new Parameter("handle", TypeRef.CoreLib("System", "Boolean"))], HasThis: false, GenericParameterCount: 0),
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
                    CatchType: FormatException),
                new HandlerRegion(
                    HandlerKind.Filter,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0040,
                    HandlerLength: 0x0010,
                    FilterOffset: 0x0030,
                    CatchType: null),
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
    public void IOExceptionFilterRegion_RaisesToCatchWhen()
    {
        var function = IOExceptionFilterRegion();

        new EhStructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Regions);
        var clause = Assert.Single(Assert.Single(function.Descendants.OfType<TryCatch>()).Clauses);
        Assert.NotNull(clause.Filter);
        Assert.NotNull(clause.VariableIndex);

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("catch (Exception V_0) when", output);
        Assert.Contains("V_0 is IOException", output);
        Assert.Contains("||", output);
        Assert.True(output.Split("IOException").Length >= 3, output);
    }

    [Fact]
    public void SimpleCatchAllFilterRegion_RaisesToCatchWhen()
    {
        var function = SimpleCatchAllFilterRegion();

        new EhStructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Regions);
        var clause = Assert.Single(Assert.Single(function.Descendants.OfType<TryCatch>()).Clauses);
        Assert.NotNull(clause.Filter);
        Assert.Null(clause.VariableIndex);

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("catch when (handle)", output);
    }

    [Fact]
    public void ExceptionCaptureFilterRegion_RaisesToTypedCatchWhen()
    {
        var function = ExceptionCaptureFilterRegion();

        new EhStructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Regions);
        var clause = Assert.Single(Assert.Single(function.Descendants.OfType<TryCatch>()).Clauses);
        Assert.NotNull(clause.Filter);
        Assert.Equal(0, clause.VariableIndex);

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("catch (Exception V_0) when (captureException)", output);
    }

    [Fact]
    public void GlobalExceptionHandlerFilterRegion_RaisesToTypedCatchWhen()
    {
        var function = GlobalExceptionHandlerFilterRegion();

        new EhStructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Regions);
        var clause = Assert.Single(Assert.Single(function.Descendants.OfType<TryCatch>()).Clauses);
        Assert.NotNull(clause.Filter);
        Assert.Equal(0, clause.VariableIndex);

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("catch (Exception V_0) when (ExceptionHandling.IsHandledByGlobalHandler(V_0))", output);
    }

    [Fact]
    public void CatchThenFilterRegion_RaisesBothClauses()
    {
        var function = CatchThenFilterRegion();

        new EhStructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Regions);
        var clauses = Assert.Single(function.Descendants.OfType<TryCatch>()).Clauses;
        Assert.Equal(2, clauses.Count);
        Assert.Null(clauses[0].Filter);
        Assert.NotNull(clauses[1].Filter);

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("catch (FormatException)", output);
        Assert.Contains("catch when (handle)", output);
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
