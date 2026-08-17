using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Adversarial guards for StackSlotLiveRangePass. Its established non-EH path
// requires block-local loads; structured EH uses a stronger proof because later
// rewrites can reshape its regions: every reference to the reused slot must
// belong to a top-level statement in one block. Synthetic-IR near misses pair
// with positive canaries for both paths.
public class StackSlotLiveRangeCrossBlockTests
{
    static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "T");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Exception = TypeRef.CoreLib("System", "Exception");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");

    const int Slot = 5;

    // block0: S5 = int (range A, used); S5 = string (range B, used). When
    // crossBlock, a successor block also reads S5 (range B live-out).
    static IrFunction Build(bool crossBlock)
    {
        var container = new BlockContainer();

        var b0 = ReusedSlotBlock();
        container.Add(b0);

        if (crossBlock)
        {
            var b1 = new Block(100);
            b1.Add(new ExpressionStatement(new LoadStackSlot(Slot, String)));    // live-out range-B read
            b1.Add(new Return(null));
            container.Add(b1);
        }
        else
        {
            b0.Add(new Return(null));
        }

        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Owner, signature, [Int32, String], container);
        new StackSlotLiveRangePass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    static Block ReusedSlotBlock()
    {
        var block = new Block(0);
        block.Add(new StoreStackSlot(Slot, new Constant(1, Int32)));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(Slot, Int32)));
        block.Add(new StoreStackSlot(Slot, new Constant("x", String)));
        block.Add(new StoreLocal(1, String, new LoadStackSlot(Slot, String)));
        return block;
    }

    static IrFunction BuildStructuredEh(bool nestedLoop, bool handlerLoad, bool filterLoad = false)
    {
        var tryBody = new BlockContainer();
        Block tryBlock;
        if (nestedLoop)
        {
            var loopBody = ReusedSlotBlock();
            loopBody.Add(new Return(null));
            tryBlock = new Block(0);
            tryBlock.Add(new WhileLoop(new Constant(true, Boolean), loopBody));
        }
        else
        {
            tryBlock = ReusedSlotBlock();
        }
        tryBlock.Add(new Return(null));
        tryBody.Add(tryBlock);

        var catchBody = new BlockContainer();
        var catchBlock = new Block(100);
        if (handlerLoad)
            catchBlock.Add(new ExpressionStatement(new LoadStackSlot(Slot, String)));
        catchBlock.Add(new Return(null));
        catchBody.Add(catchBlock);

        var body = new BlockContainer();
        var bodyBlock = new Block(0);
        IrExpression? filter = filterLoad ? new LoadStackSlot(Slot, Boolean) : null;
        bodyBlock.Add(new TryCatch(tryBody, [new CatchClause(Exception, catchBody, filter)]));
        bodyBlock.Add(new Return(null));
        body.Add(bodyBlock);

        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Owner, signature, [Int32, String], body);
        new StackSlotLiveRangePass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    static IrFunction BuildHandlerCandidate(bool inFinally)
    {
        var tryBody = new BlockContainer();
        var tryBlock = new Block(0);
        tryBlock.Add(new Return(null));
        tryBody.Add(tryBlock);

        var handlerBody = new BlockContainer();
        var handlerBlock = ReusedSlotBlock();
        handlerBlock.Add(new Return(null));
        handlerBody.Add(handlerBlock);

        IrNode eh = inFinally
            ? new TryFinally(tryBody, handlerBody)
            : new TryCatch(tryBody, [new CatchClause(Exception, handlerBody)]);

        var body = new BlockContainer();
        var bodyBlock = new Block(200);
        bodyBlock.Add(eh);
        bodyBlock.Add(new Return(null));
        body.Add(bodyBlock);

        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Owner, signature, [Int32, String], body);
        new StackSlotLiveRangePass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    public enum NestedTryOwner
    {
        Catch,
        Finally,
        Loop,
        Try,
    }

    static IrFunction BuildNestedTryCandidate(NestedTryOwner owner)
    {
        var candidateTryBody = new BlockContainer();
        var candidateBlock = ReusedSlotBlock();
        candidateBlock.Add(new Return(null));
        candidateTryBody.Add(candidateBlock);

        var candidateCatchBody = new BlockContainer();
        var candidateCatchBlock = new Block(100);
        candidateCatchBlock.Add(new Return(null));
        candidateCatchBody.Add(candidateCatchBlock);
        var candidateTry = new TryCatch(candidateTryBody, [new CatchClause(Exception, candidateCatchBody)]);

        var rootBlock = new Block(400);
        switch (owner)
        {
            case NestedTryOwner.Catch:
            {
                var outerTryBody = ReturnContainer(200);
                var outerCatchBody = new BlockContainer();
                var outerCatchBlock = new Block(300);
                outerCatchBlock.Add(candidateTry);
                outerCatchBlock.Add(new Return(null));
                outerCatchBody.Add(outerCatchBlock);
                rootBlock.Add(new TryCatch(outerTryBody, [new CatchClause(Exception, outerCatchBody)]));
                break;
            }
            case NestedTryOwner.Finally:
            {
                var finallyBody = new BlockContainer();
                var finallyBlock = new Block(300);
                finallyBlock.Add(candidateTry);
                finallyBlock.Add(new Return(null));
                finallyBody.Add(finallyBlock);
                rootBlock.Add(new TryFinally(ReturnContainer(200), finallyBody));
                break;
            }
            case NestedTryOwner.Loop:
            {
                var loopBody = new Block(300);
                loopBody.Add(candidateTry);
                loopBody.Add(new Return(null));
                rootBlock.Add(new WhileLoop(new Constant(true, Boolean), loopBody));
                break;
            }
            case NestedTryOwner.Try:
            {
                var outerTryBody = new BlockContainer();
                var outerTryBlock = new Block(300);
                outerTryBlock.Add(candidateTry);
                outerTryBlock.Add(new Return(null));
                outerTryBody.Add(outerTryBlock);
                rootBlock.Add(new TryCatch(outerTryBody, [new CatchClause(Exception, ReturnContainer(200))]));
                break;
            }
        }
        rootBlock.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(rootBlock);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Owner, signature, [Int32, String], body);
        new StackSlotLiveRangePass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    static BlockContainer ReturnContainer(int offset)
    {
        var container = new BlockContainer();
        var block = new Block(offset);
        block.Add(new Return(null));
        container.Add(block);
        return container;
    }

    static IrFunction BuildReadBeforeWrite()
    {
        var rebuilt = BuildStructuredEhWithReadBeforeWrite();
        new StackSlotLiveRangePass().Run(rebuilt, PassContext.None);
        rebuilt.CheckInvariant();
        return rebuilt;
    }

    static IrFunction BuildStructuredEhWithReadBeforeWrite()
    {
        var tryBody = new BlockContainer();
        var tryBlock = ReusedSlotBlock();
        tryBlock.Add(new StoreStackSlot(Slot, new LoadStackSlot(Slot, String)));
        tryBlock.Add(new StoreLocal(1, String, new LoadStackSlot(Slot, String)));
        tryBlock.Add(new Return(null));
        tryBody.Add(tryBlock);

        var catchBody = new BlockContainer();
        var catchBlock = new Block(100);
        catchBlock.Add(new Return(null));
        catchBody.Add(catchBlock);

        var body = new BlockContainer();
        var bodyBlock = new Block(200);
        bodyBlock.Add(new TryCatch(tryBody, [new CatchClause(Exception, catchBody)]));
        bodyBlock.Add(new Return(null));
        body.Add(bodyBlock);

        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Owner, signature, [Int32, String], body);
    }

    static bool Split(IrFunction function)
        => function.Descendants.OfType<StoreStackSlot>().Any(s => s.Slot >= StoreStackSlot.DupSlotBase)
            || function.Descendants.OfType<LoadStackSlot>().Any(l => l.Slot >= StoreStackSlot.DupSlotBase);

    static IrFunction Run(params Block[] blocks)
    {
        var body = new BlockContainer();
        foreach (var block in blocks)
            body.Add(block);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Owner, signature, [], body);
        new StackSlotLiveRangePass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    static StoreStackSlot Store(int value)
        => new(Slot, new Constant(value, Int32));

    static StoreStackSlot Store(string value)
        => new(Slot, new Constant(value, String));

    static ExpressionStatement Load(TypeRef type)
        => new(new LoadStackSlot(Slot, type));

    [Fact]
    public void BlockLocalRange_Splits()
    {
        Assert.True(Split(Build(crossBlock: false)));
    }

    [Fact]
    public void CrossBlockRange_SplitsAllReachedLoads()
    {
        var function = Build(crossBlock: true);
        Assert.True(Split(function));
        Assert.Single(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == Slot);
        var rewrittenLoads = function.Descendants.OfType<LoadStackSlot>().Where(load => load.Slot != Slot).ToList();
        Assert.Equal(2, rewrittenLoads.Count);
        Assert.Single(rewrittenLoads.Select(load => load.Slot).Distinct());
    }

    [Fact]
    public void SequentialCrossBlockRanges_SplitOnlyTheReachedDefinitionAndLoad()
    {
        var firstStore = Store(1);
        var firstLoad = Load(Int32);
        var secondStore = Store("x");
        var secondLoad = Load(String);
        var function = Run(
            BlockOf(0, firstStore),
            BlockOf(10, firstLoad),
            BlockOf(20, secondStore),
            BlockOf(30, secondLoad, new Return(null)));

        Assert.Equal(Slot, firstStore.Slot);
        Assert.Equal(Slot, Assert.IsType<LoadStackSlot>(firstLoad.Expression).Slot);
        var rewrittenStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot != Slot);
        var rewrittenLoad = Assert.Single(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot != Slot);
        Assert.Equal(rewrittenStore.Slot, rewrittenLoad.Slot);
        Assert.Equal(String, rewrittenStore.Value.ResultType);
        Assert.Equal(String, rewrittenLoad.Type);
    }

    [Fact]
    public void SingleCrossBlockDefinition_StaysUnsplit()
    {
        var function = Run(
            BlockOf(0, Store(1)),
            BlockOf(10, Load(Int32), new Return(null)));

        Assert.False(Split(function));
    }

    [Fact]
    public void SameTypedSequentialCrossBlockRanges_StayUnsplit()
    {
        var function = Run(
            BlockOf(0, Store(1), Load(Int32)),
            BlockOf(10, Store(2)),
            BlockOf(20, Load(Int32), new Return(null)));

        Assert.False(Split(function));
    }

    [Fact]
    public void CompetingDiamondDefinitions_StayUnsplit()
    {
        var entry = BlockOf(
            0,
            Store(1),
            new ConditionalBranch(new LoadArgument(0, "condition", Boolean), 20));
        var unchanged = BlockOf(10, new Branch(30));
        var redefined = BlockOf(20, Store("x"), new Branch(30));
        var join = BlockOf(30, Load(Object), new Return(null));

        Assert.False(Split(Run(entry, unchanged, redefined, join)));
    }

    [Fact]
    public void NonFinalConditionalBranch_DroppedEdge_StaysUnsplit()
    {
        var define = BlockOf(0, Store(1), new Branch(10));
        var redefine = BlockOf(10, Store("x"), new Branch(20));
        var use = BlockOf(
            20,
            Load(String),
            new ConditionalBranch(new LoadArgument(0, "condition", Boolean), 30),
            new Return(null));
        var laterUse = BlockOf(30, Load(String), new Return(null));

        Assert.False(Split(Run(define, redefine, use, laterUse)));
    }

    [Fact]
    public void NonFinalConditionalBranch_HiddenDiamondJoin_StaysUnsplit()
    {
        var entry = BlockOf(
            0,
            Store(1),
            new ConditionalBranch(new LoadArgument(0, "condition", Boolean), 20),
            new Branch(10));
        var redefined = BlockOf(10, Store("x"), new Branch(20));
        var join = BlockOf(20, Load(Object), new Return(null));

        Assert.False(Split(Run(entry, redefined, join)));
    }

    [Fact]
    public void LoopRedefinitionWithUniquePostStoreLoad_Splits()
    {
        var entry = BlockOf(0, Store(1), Load(Int32), new Branch(10));
        var redefine = BlockOf(10, Store("x"));
        var useAndLatch = BlockOf(
            20,
            Load(String),
            new ConditionalBranch(new LoadArgument(0, "again", Boolean), 10));
        var exit = BlockOf(30, new Return(null));

        var function = Run(entry, redefine, useAndLatch, exit);

        var rewrittenStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot != Slot);
        var rewrittenLoad = Assert.Single(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot != Slot);
        Assert.Equal(rewrittenStore.Slot, rewrittenLoad.Slot);
    }

    [Fact]
    public void LoopLoadReachedByInitialAndBackEdgeDefinitions_StaysUnsplit()
    {
        var entry = BlockOf(0, Store(1), new Branch(10));
        var loop = BlockOf(
            10,
            Load(Object),
            Store("x"),
            new ConditionalBranch(new LoadArgument(0, "again", Boolean), 10));
        var exit = BlockOf(20, new Return(null));

        Assert.False(Split(Run(entry, loop, exit)));
    }

    [Fact]
    public void StructuredLoopCarriedBlockLocalRange_StaysUnsplit()
    {
        var loopBody = BlockOf(
            10,
            Load(Int32),
            Store("loop"),
            Load(String));
        var entry = BlockOf(
            0,
            Store(1),
            new WhileLoop(new LoadArgument(0, "again", Boolean), loopBody),
            new Return(null));

        Assert.False(Split(Run(entry)));
    }

    [Fact]
    public void LoopCarriedCandidateRhsLoad_StaysUnsplit()
    {
        var loopInput = new LoadStackSlot(Slot, Object);
        var loopPostStoreLoad = Load(String);
        var conditionalStore = BlockOf(20, Store(1));
        var loopBody = BlockOf(
            10,
            new IfStatement(new LoadArgument(0, "first", Boolean), conditionalStore, null),
            new StoreStackSlot(Slot, new Coerce(String, loopInput)),
            loopPostStoreLoad);
        var entry = BlockOf(
            0,
            Store("seed"),
            new WhileLoop(new LoadArgument(1, "again", Boolean), loopBody),
            new Return(null));

        Assert.False(Split(Run(entry)));
        Assert.Equal(Slot, loopInput.Slot);
        Assert.Equal(Slot, Assert.Single(loopBody.Children.OfType<StoreStackSlot>()).Slot);
        Assert.Equal(Slot, Assert.IsType<LoadStackSlot>(loopPostStoreLoad.Expression).Slot);
    }

    [Fact]
    public void RawLoopCarriedCandidateRhsLoad_StaysUnsplit()
    {
        var loopInput = new LoadStackSlot(Slot, Object);
        var loopPostStoreLoad = Load(String);
        var conditionalStore = BlockOf(20, Store(1));
        var entry = BlockOf(0, Store("seed"), new Branch(10));
        var loop = BlockOf(
            10,
            new IfStatement(new LoadArgument(0, "first", Boolean), conditionalStore, null),
            new StoreStackSlot(Slot, new Coerce(String, loopInput)),
            loopPostStoreLoad,
            new Branch(10));

        Assert.False(Split(Run(entry, loop)));
        Assert.Equal(Slot, loopInput.Slot);
        Assert.Equal(Slot, Assert.Single(loop.Children.OfType<StoreStackSlot>()).Slot);
        Assert.Equal(Slot, Assert.IsType<LoadStackSlot>(loopPostStoreLoad.Expression).Slot);
    }

    [Fact]
    public void StraightLineCandidateRhsLoad_AllowsSplit()
    {
        var input = new LoadStackSlot(Slot, Object);
        var postStoreLoad = Load(String);
        var block = BlockOf(
            0,
            Store(1),
            new StoreStackSlot(Slot, new Coerce(String, input)),
            postStoreLoad,
            new Return(null));

        var function = Run(block);

        Assert.Equal(Slot, input.Slot);
        var rewrittenStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot != Slot);
        var rewrittenLoad = Assert.Single(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot != Slot);
        Assert.Equal(rewrittenStore.Slot, rewrittenLoad.Slot);
    }

    [Fact]
    public void NestedBlockInRawLoopCarriedRange_StaysUnsplit()
    {
        var nested = BlockOf(
            10,
            Load(Int32),
            Store("loop"),
            Load(String));
        var entry = BlockOf(
            0,
            Store(1),
            new IfStatement(new LoadArgument(0, "take", Boolean), nested, null),
            new Branch(0));

        Assert.False(Split(Run(entry)));
    }

    [Fact]
    public void LaterNestedStatementLoadBeforeStore_StaysUnsplit()
    {
        var nestedLoad = Load(Int32);
        var nested = BlockOf(100, nestedLoad, Store("later"));
        var entry = BlockOf(
            0,
            Store("first"),
            Store(29),
            Load(Int32),
            new IfStatement(new LoadArgument(0, "condition", Boolean), nested, null),
            new Return(null));

        Assert.False(Split(Run(entry)));
        Assert.Equal(Slot, Assert.IsType<LoadStackSlot>(nestedLoad.Expression).Slot);
    }

    [Fact]
    public void LaterNestedStatementStoreWithoutLoad_AllowsSplit()
    {
        var directLoad = Load(Int32);
        var nestedStore = Store("later");
        var nested = BlockOf(100, nestedStore);
        var entry = BlockOf(
            0,
            Store("first"),
            Store(29),
            directLoad,
            new IfStatement(new LoadArgument(0, "condition", Boolean), nested, null),
            new Return(null));

        var function = Run(entry);

        var rewrittenStore = Assert.Single(
            entry.Children.OfType<StoreStackSlot>(),
            store => store.Slot != Slot);
        Assert.Equal(rewrittenStore.Slot, Assert.IsType<LoadStackSlot>(directLoad.Expression).Slot);
        Assert.Equal(Slot, nestedStore.Slot);
        Assert.Single(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot != Slot);
    }

    [Fact]
    public void CrossBlockSplit_DoesNotEnableLoopCarriedBlockLocalSplit()
    {
        var loopHeadLoad = Load(Int32);
        var loopPostStoreLoad = Load(String);
        var finalLoad = Load(String);
        var loop = BlockOf(
            10,
            loopHeadLoad,
            Store("loop"),
            loopPostStoreLoad,
            new ConditionalBranch(new LoadArgument(1, "again", Boolean), 10));
        var function = Run(
            BlockOf(
                0,
                Store(1),
                new ConditionalBranch(new LoadArgument(0, "skipLoop", Boolean), 20)),
            loop,
            BlockOf(20, Store(2), Store("final")),
            BlockOf(30, finalLoad, new Return(null)));

        Assert.Equal(Slot, Assert.IsType<LoadStackSlot>(loopHeadLoad.Expression).Slot);
        Assert.Equal(Slot, Assert.Single(loop.Children.OfType<StoreStackSlot>()).Slot);
        Assert.Equal(Slot, Assert.IsType<LoadStackSlot>(loopPostStoreLoad.Expression).Slot);
        int finalLoadSlot = Assert.IsType<LoadStackSlot>(finalLoad.Expression).Slot;
        Assert.NotEqual(Slot, finalLoadSlot);
        Assert.Single(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == finalLoadSlot);
    }

    [Fact]
    public void UnreachableCompetingDefinition_DoesNotPolluteReachableJoin()
    {
        var entry = BlockOf(0, Store(1), new Branch(20));
        var unreachable = BlockOf(10, Store("unreachable"), new Branch(30));
        var redefine = BlockOf(20, Store("x"), new Branch(30));
        var join = BlockOf(30, Load(String), new Return(null));

        var function = Run(entry, unreachable, redefine, join);

        Assert.Single(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot != Slot);
        Assert.Single(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot != Slot);
    }

    [Fact]
    public void UseBeforeDefinition_StaysUnsplit()
    {
        var entry = BlockOf(0, new ConditionalBranch(new LoadArgument(0, "skip", Boolean), 20));
        var define = BlockOf(10, Store(1), new Branch(20));
        var useAndRedefine = BlockOf(20, Load(Int32), Store("x"));
        var finalUse = BlockOf(30, Load(String), new Return(null));

        Assert.False(Split(Run(entry, define, useAndRedefine, finalUse)));
    }

    [Fact]
    public void SameStoreReadBeforeWrite_StaysUnsplit()
    {
        var entry = BlockOf(0, Store(1), Load(Int32));
        var redefine = BlockOf(
            10,
            new StoreStackSlot(Slot, new LoadStackSlot(Slot, String)));
        var finalUse = BlockOf(20, Load(String), new Return(null));

        Assert.False(Split(Run(entry, redefine, finalUse)));
    }

    [Fact]
    public void ExternalCfgTarget_StaysUnsplit()
    {
        var entry = BlockOf(0, Store(1));
        var redefine = BlockOf(10, Store("x"));
        var use = BlockOf(20, Load(String), new Branch(0xFF));

        Assert.False(Split(Run(entry, redefine, use)));
    }

    [Fact]
    public void DuplicateBlockOffsets_StayUnsplit()
    {
        var entry = BlockOf(0, Store(1), new Branch(10));
        var firstAtOffset = BlockOf(10, Store("first"), new Branch(30));
        var secondAtOffset = BlockOf(10, new Branch(20));
        var redefine = BlockOf(20, Store("second"), new Branch(30));
        var use = BlockOf(30, Load(String), new Return(null));

        Assert.False(Split(Run(entry, firstAtOffset, secondAtOffset, redefine, use)));
    }

    [Fact]
    public void EhLeaveCfgEdge_StaysUnsplit()
    {
        var entry = BlockOf(0, Store(1));
        var redefine = BlockOf(10, Store("x"));
        var use = BlockOf(20, Load(String), new Leave(30));

        Assert.False(Split(Run(entry, redefine, use)));
    }

    [Fact]
    public void NestedControlFlowReference_StaysUnsplit()
    {
        var nestedBody = new Block(100);
        nestedBody.Add(Load(Int32));
        var entry = BlockOf(0, Store(1), new WhileLoop(new LoadArgument(0, "condition", Boolean), nestedBody));
        var redefine = BlockOf(10, Store("x"));
        var use = BlockOf(20, Load(String), new Return(null));

        Assert.False(Split(Run(entry, redefine, use)));
    }

    [Fact]
    public void LocalFunctionRoot_DoesNotExposeNestedLoadsToCrossBlockRewrite()
    {
        var localLoad = Load(Object);
        var localBlock = BlockOf(100, Store(1), localLoad, Store("local"), new Return(null));
        var localBody = new BlockContainer();
        localBody.Add(localBlock);
        var localFunction = new LocalFunctionStatement(
            "Local",
            TypeRef.CoreLib("System", "Void"),
            [],
            isStatic: true,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localBody);
        var hostLoad = Load(String);

        var function = Run(
            BlockOf(0, Store(1)),
            BlockOf(10, Store("host")),
            BlockOf(20, hostLoad, localFunction, new Return(null)));

        Assert.NotEqual(Slot, Assert.IsType<LoadStackSlot>(hostLoad.Expression).Slot);
        Assert.Equal(Slot, Assert.IsType<LoadStackSlot>(localLoad.Expression).Slot);
    }

    [Fact]
    public void CrossBlockSplit_DoesNotEnableNestedFunctionLoopCarriedSplit()
    {
        var localPriorLoad = Load(Int32);
        var localPostStoreLoad = Load(String);
        var localEntry = BlockOf(100, Store(1), new Branch(110));
        var localLoop = BlockOf(
            110,
            localPriorLoad,
            Store("local"),
            localPostStoreLoad,
            new Branch(110));
        var localBody = new BlockContainer();
        localBody.Add(localEntry);
        localBody.Add(localLoop);
        var localFunction = new LocalFunctionStatement(
            "Local",
            TypeRef.CoreLib("System", "Void"),
            [],
            isStatic: true,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localBody);
        var hostLoad = Load(String);

        var function = Run(
            BlockOf(0, Store(1)),
            BlockOf(10, Store("host")),
            BlockOf(20, hostLoad, localFunction, new Return(null)));

        Assert.NotEqual(Slot, Assert.IsType<LoadStackSlot>(hostLoad.Expression).Slot);
        Assert.Equal(Slot, Assert.IsType<LoadStackSlot>(localPriorLoad.Expression).Slot);
        Assert.Equal(Slot, Assert.Single(localLoop.Children.OfType<StoreStackSlot>()).Slot);
        Assert.Equal(Slot, Assert.IsType<LoadStackSlot>(localPostStoreLoad.Expression).Slot);
        Assert.Single(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot != Slot);
    }

    [Fact]
    public void NestedBranchThatBypassesDefinition_StaysUnsplit()
    {
        var bypass = new Block(100);
        bypass.Add(new Branch(20));
        var entry = BlockOf(
            0,
            Store(1),
            new IfStatement(new LoadArgument(0, "bypass", Boolean), bypass, null));
        var redefine = BlockOf(10, Store("x"));
        var use = BlockOf(20, Load(String), new Return(null));

        Assert.False(Split(Run(entry, redefine, use)));
    }

    [Fact]
    public void TryBodyStraightLineRange_Splits()
    {
        Assert.True(Split(BuildStructuredEh(nestedLoop: false, handlerLoad: false)));
    }

    [Fact]
    public void CoreLibTryBodyStraightLineRange_FullPipelineRemovesSlot()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Exception", "CreateTypeInitializationException");
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain("S_0", result.Output);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
    }

    [Theory]
    [InlineData("System.Text.StringBuilder", "AppendFormat", 9)]
    [InlineData("System.Text.ValueStringBuilder", "AppendFormatHelper", 0)]
    public void CoreLibSequentialCrossBlockRange_FullPipelineRemovesSlots(
        string typeName,
        string methodName,
        int overloadIndex)
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, typeName, methodName, overloadIndex);
        Assert.NotNull(function);
        Assert.True(function!.Descendants.OfType<StoreStackSlot>().Count(store => store.Slot == 0) >= 2);
        Assert.True(function.Descendants.OfType<LoadStackSlot>().Count(load => load.Slot == 0) >= 2);

        var result = CSharpPrinter.PrintRaised(function);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
    }

    [Fact]
    public void NestedLoopRange_StaysUnsplit()
    {
        Assert.False(Split(BuildStructuredEh(nestedLoop: true, handlerLoad: false)));
    }

    [Fact]
    public void HandlerRead_StaysUnsplit()
    {
        Assert.False(Split(BuildStructuredEh(nestedLoop: false, handlerLoad: true)));
    }

    [Fact]
    public void CatchBodyStraightLineRange_StaysUnsplit()
    {
        Assert.False(Split(BuildHandlerCandidate(inFinally: false)));
    }

    [Fact]
    public void FinallyBodyStraightLineRange_StaysUnsplit()
    {
        Assert.False(Split(BuildHandlerCandidate(inFinally: true)));
    }

    [Theory]
    [InlineData(NestedTryOwner.Catch)]
    [InlineData(NestedTryOwner.Finally)]
    [InlineData(NestedTryOwner.Loop)]
    [InlineData(NestedTryOwner.Try)]
    public void NestedTryBodyRange_StaysUnsplit(NestedTryOwner owner)
    {
        Assert.False(Split(BuildNestedTryCandidate(owner)));
    }

    [Fact]
    public void FilterRead_StaysUnsplit()
    {
        Assert.False(Split(BuildStructuredEh(nestedLoop: false, handlerLoad: false, filterLoad: true)));
    }

    [Fact]
    public void ReadBeforeWriteRange_StaysUnsplit()
    {
        Assert.False(Split(BuildReadBeforeWrite()));
    }

    static Block BlockOf(int offset, params IrNode[] statements)
    {
        var block = new Block(offset);
        foreach (var statement in statements)
            block.Add(statement);
        return block;
    }
}
