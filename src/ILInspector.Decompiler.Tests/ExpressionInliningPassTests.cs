using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ExpressionInliningPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef ExceptionType = TypeRef.CoreLib("System", "Exception");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Action = TypeRef.CoreLib("System", "Action");

    static string PrintRaised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    // The collapsed cache leaves the chain spilled across reused stack slots
    // (`S_0 = xs; S_1 = x => ...; S_0 = Where(S_0, S_1); ...`). Live-range
    // inlining folds those temps into the call arguments, leaving one statement.
    [Fact]
    public void CachedDelegateArgument_InlinesToSingleCall()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateArgument));

        Assert.Equal(1, output.Count(c => c == ';'));
        Assert.StartsWith("return ", output);
        Assert.Contains("Where", output);
        Assert.Contains("x => x > 0", output);
    }

    [Fact]
    public void CachedDelegateChain_InlinesToSingleNestedExpression()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateChain));

        Assert.Equal(1, output.Count(c => c == ';'));
        Assert.StartsWith("return ", output);
        // The first call's result feeds the second as its receiver argument.
        int where = output.IndexOf("Where", StringComparison.Ordinal);
        int select = output.IndexOf("Select", StringComparison.Ordinal);
        Assert.True(where >= 0 && select >= 0 && select < where,
            $"expected Select(Where(...), ...) nesting, got: {output}");
        Assert.Contains("x => x > 0", output);
        Assert.Contains("x => x * 2", output);
    }

    [Fact]
    public void StoreBeforeTry_DoesNotInlineIntoCatchFilter()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        var filter = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadLocal(0, Int32),
            new Constant(0, Int32));
        block.Add(new StoreLocal(0, Int32, new LoadLocal(1, Int32)));
        var tryBody = new BlockContainer();
        tryBody.Add(new Block(1));
        var catchBody = new BlockContainer();
        catchBody.Add(new Block(2));
        block.Add(new TryCatch(
            tryBody,
            [new CatchClause(ExceptionType, catchBody, filter)]));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32, Int32],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Contains(block.Children.OfType<StoreLocal>(), store => store.Index == 0);
        var clause = Assert.Single(function.Descendants.OfType<CatchClause>());
        Assert.NotNull(clause.Filter);
        Assert.Contains(clause.Filter.Descendants.OfType<LoadLocal>(), load => load.Index == 0);
        Assert.DoesNotContain(clause.Filter.Descendants.OfType<LoadLocal>(), load => load.Index == 1);
    }

    [Fact]
    public void StoreBeforeTry_FilterReadBlocksInliningIntoOtherUse()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        var filter = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadLocal(0, Int32),
            new Constant(0, Int32));
        block.Add(new StoreLocal(0, Int32, new LoadLocal(1, Int32)));
        block.Add(new ExpressionStatement(new LoadLocal(0, Int32)));
        var tryBody = new BlockContainer();
        tryBody.Add(new Block(1));
        var catchBody = new BlockContainer();
        catchBody.Add(new Block(2));
        block.Add(new TryCatch(
            tryBody,
            [new CatchClause(ExceptionType, catchBody, filter)]));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32, Int32],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Contains(block.Children.OfType<StoreLocal>(), store => store.Index == 0);
        Assert.Contains(block.Children.OfType<ExpressionStatement>(), statement =>
            statement.Expression is LoadLocal { Index: 0 });
    }

    [Fact]
    public void DelegateCreationTargetFieldWrite_BlocksLiveRangeInlining()
    {
        var receiverField = new FieldRef(Holder, "Receiver", Object);
        var target = new MethodRef(Holder, "M", Void, [], HasThis: true);
        var use = new MethodRef(Holder, "Use", Void, [Action], HasThis: false);

        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new DelegateCreation(
            Action,
            target,
            isVirtual: false,
            new LoadField(receiverField, instance: null))));
        block.Add(new StoreField(receiverField, instance: null, new LoadArgument(0, "newReceiver", Object)));
        block.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(0, Action)])));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [new Parameter("newReceiver", Object)], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Contains(block.Children.OfType<StoreStackSlot>(), store => store.Slot == 0);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 0);
        function.CheckInvariant();
    }

    [Fact]
    public void StaticFieldDelegateCreation_EffectBetweenStoreAndUse_BlocksLiveRangeInlining()
    {
        var receiverField = new FieldRef(Holder, "Receiver", Object);
        var target = new MethodRef(Holder, "M", Void, [], HasThis: true);
        var sideEffect = new MethodRef(Holder, "SideEffect", Void, [], HasThis: false);
        var use = new MethodRef(Holder, "Use", Void, [Action], HasThis: false);

        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new DelegateCreation(
            Action,
            target,
            isVirtual: false,
            new LoadField(receiverField, instance: null))));
        block.Add(new ExpressionStatement(new Call(sideEffect, isVirtual: false, [])));
        block.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(0, Action)])));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Contains(block.Children.OfType<StoreStackSlot>(), store => store.Slot == 0);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 0);
        function.CheckInvariant();
    }

    [Fact]
    public void StaticFieldDelegateCreation_AdjacentUse_StillInlines()
    {
        var receiverField = new FieldRef(Holder, "Receiver", Object);
        var target = new MethodRef(Holder, "M", Void, [], HasThis: true);
        var use = new MethodRef(Holder, "Use", Void, [Action], HasThis: false);

        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new DelegateCreation(
            Action,
            target,
            isVirtual: false,
            new LoadField(receiverField, instance: null))));
        block.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(0, Action)])));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Single(function.Descendants.OfType<DelegateCreation>());
        function.CheckInvariant();
    }
}
