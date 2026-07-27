using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Negative controls proving <see cref="IrNode.CheckInvariant"/> actually
/// detects a corrupt parent/child link. Without these the check could silently
/// become a no-op (its whole failure mode under #3241) and every other test
/// would still pass. Corruption is forced through the private backing fields the
/// normal API never lets a caller reach.
/// </summary>
public sealed class IrInvariantCheckTests
{
    static readonly FieldInfo ParentField = typeof(IrNode)
        .GetField("<Parent>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

    static readonly FieldInfo ChildIndexField = typeof(IrNode)
        .GetField("<ChildIndex>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

    static Block ParentWithTwoChildren(out Block first, out Block second)
    {
        first = new Block(0x10);
        second = new Block(0x20);
        var parent = new Block();
        parent.Add(first);
        parent.Add(second);
        return parent;
    }

    [Fact]
    public void CheckInvariant_PassesForWellFormedTree()
    {
        var parent = ParentWithTwoChildren(out _, out _);

        parent.CheckInvariant();
    }

    [Fact]
    public void CheckInvariant_ThrowsWhenChildParentPointerIsWrong()
    {
        var parent = ParentWithTwoChildren(out _, out var second);
        ParentField.SetValue(second, null);

        var ex = Assert.Throws<InvalidOperationException>(parent.CheckInvariant);
        Assert.Contains("wrong parent", ex.Message);
    }

    [Fact]
    public void CheckInvariant_ThrowsWhenChildSlotIsWrong()
    {
        var parent = ParentWithTwoChildren(out _, out var second);
        ChildIndexField.SetValue(second, 99);

        var ex = Assert.Throws<InvalidOperationException>(parent.CheckInvariant);
        Assert.Contains("slot", ex.Message);
    }

    [Fact]
    public void CheckInvariant_DetectsCorruptionInADeepChild()
    {
        var leaf = new Block(0x30);
        var mid = new Block(0x20);
        mid.Add(leaf);
        var root = new Block();
        root.Add(mid);
        ChildIndexField.SetValue(leaf, 7);

        Assert.Throws<InvalidOperationException>(root.CheckInvariant);
    }

    // ---- Semantic invariant: local-slot range (CheckInvariant(includeSemantics: true)) ----
    // These pass includeSemantics:true DIRECTLY, never reading the global
    // IrInvariants.CheckSemantics level, so they stay hermetic under xUnit's
    // parallel collections — raising that level process-wide would false-positive
    // the 5 minimal-fixture pass tests that reference slots without populating
    // Locals (#3302), which is why it has no setter and moves only via
    // DOTNET_INSPECT_IR_INVARIANTS=full.

    [Fact]
    public void SemanticCheck_PassesWhenLocalSlotIsInRange()
    {
        var function = FunctionStoringLocal([IntType], slot: 0);

        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void SemanticCheck_ThrowsWhenLocalSlotIsOutOfRange()
    {
        var function = FunctionStoringLocal([IntType], slot: 5);

        var ex = Assert.Throws<InvalidOperationException>(
            () => function.CheckInvariant(includeSemantics: true));
        Assert.Contains("local slot 5", ex.Message);
    }

    [Fact]
    public void StructuralCheck_IgnoresLocalSlotRange_ButSemanticCheckCatchesIt()
    {
        // A minimal fixture: a store into slot 0 while the function declares zero
        // locals — exactly the shape hand-built pass-test fixtures produce.
        var function = FunctionStoringLocal([], slot: 0);

        // Structural mode (the suite-wide default) must not trip on it...
        function.CheckInvariant();

        // ...but the semantic mode, meant for real importer output, does.
        Assert.Throws<InvalidOperationException>(
            () => function.CheckInvariant(includeSemantics: true));
    }

    [Fact]
    public void SemanticCheck_ScopesLocalSlotToTheNearestLambda_NotTheOuterFunction()
    {
        // Outer function declares zero locals; the returned lambda declares one and
        // stores into its slot 0. If the check wrongly used the outer scope (0),
        // this would throw. It must scope to the lambda.
        var valid = FunctionReturningLambdaThatStores(lambdaLocals: 1, lambdaSlot: 0);
        valid.CheckInvariant(includeSemantics: true);

        // A lambda-local reference past the lambda's own table still trips.
        var invalid = FunctionReturningLambdaThatStores(lambdaLocals: 1, lambdaSlot: 3);
        Assert.Throws<InvalidOperationException>(
            () => invalid.CheckInvariant(includeSemantics: true));
    }

    [Fact]
    public void SemanticCheck_EmptyLocalsLambda_SharesTheOuterScope()
    {
        // Regression for the shared-scope representation (PR #3261 adversarial
        // review, GPT): a lambda/local-function with an EMPTY local table shares
        // its host's scope and references the outer function's locals by their
        // outer index — exactly how LambdaRaisingPass substitutes captured locals
        // and how the C# printer scopes it (NeedsNestedLambdaScope). Treating the
        // empty-Locals lambda as its own zero-slot scope would reject the valid
        // outer reference. Outer declares one local; the empty lambda reads slot 0.
        var valid = FunctionWithLocalsReturningEmptyLambdaThatStores(outerLocals: 1, lambdaSlot: 0);
        valid.CheckInvariant(includeSemantics: true);

        // The shared scope is still bounded by the OUTER table: a slot past the
        // outer locals is a real dangling reference and must trip.
        var invalid = FunctionWithLocalsReturningEmptyLambdaThatStores(outerLocals: 1, lambdaSlot: 5);
        Assert.Throws<InvalidOperationException>(
            () => invalid.CheckInvariant(includeSemantics: true));
    }

    [Fact]
    public void SemanticCheck_StaticLocalFunction_OpensItsOwnZeroSlotScope()
    {
        // Second adversarial finding (GPT, PR #3261): a STATIC local function
        // cannot capture, so its empty local table is a genuine zero-slot scope —
        // not a shared one. A store into slot 0 there is dangling and must trip,
        // even though the outer function has a local at slot 0.
        var staticFn = FunctionWithLocalContainingLocalFunction(isStatic: true, storeSlot: 0);
        Assert.Throws<InvalidOperationException>(
            () => staticFn.CheckInvariant(includeSemantics: true));

        // A NON-static (capturing) empty-Locals local function shares the outer
        // scope, so the same store into outer slot 0 is valid.
        var capturingFn = FunctionWithLocalContainingLocalFunction(isStatic: false, storeSlot: 0);
        capturingFn.CheckInvariant(includeSemantics: true);
    }

    static readonly TypeRef IntType = TypeRef.CoreLib("System", "Int32");

    static IrFunction FunctionStoringLocal(ImmutableArray<TypeRef> locals, int slot)
    {
        var block = new Block(0);
        block.Add(new StoreLocal(slot, IntType, new Constant(0, IntType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(IntType, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, locals, container);
    }

    static IrFunction FunctionReturningLambdaThatStores(int lambdaLocals, int lambdaSlot)
    {
        var actionType = TypeRef.CoreLib("System", "Action");

        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new StoreLocal(lambdaSlot, IntType, new Constant(0, IntType)));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var locals = Enumerable.Repeat(IntType, lambdaLocals).ToImmutableArray();
        var names = Enumerable.Repeat((string?)null, lambdaLocals).ToImmutableArray();
        var lambda = new Lambda(
            actionType, [], locals, names,
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, lambdaBody);

        var outerBlock = new Block(0);
        outerBlock.Add(new Return(lambda));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var signature = new MethodSignature(actionType, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], outerBody);
    }

    static IrFunction FunctionWithLocalsReturningEmptyLambdaThatStores(int outerLocals, int lambdaSlot)
    {
        var actionType = TypeRef.CoreLib("System", "Action");

        // The lambda declares NO locals of its own; its store targets a slot in the
        // outer function's local table (shared scope).
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new StoreLocal(lambdaSlot, IntType, new Constant(0, IntType)));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            actionType, [], [], [],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, lambdaBody);

        var outerBlock = new Block(0);
        outerBlock.Add(new Return(lambda));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var outerLocalTable = Enumerable.Repeat(IntType, outerLocals).ToImmutableArray();
        var signature = new MethodSignature(actionType, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, outerLocalTable, outerBody);
    }

    static IrFunction FunctionWithLocalContainingLocalFunction(bool isStatic, int storeSlot)
    {
        // An empty-Locals local function whose body stores into `storeSlot`.
        var fnBlock = new Block(0);
        fnBlock.Add(new StoreLocal(storeSlot, IntType, new Constant(0, IntType)));
        var fnBody = new BlockContainer();
        fnBody.Add(fnBlock);
        var localFunction = new LocalFunctionStatement(
            "Local", TypeRef.CoreLib("System", "Void"), [], isStatic, [], [],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, fnBody);

        var outerBlock = new Block(0);
        outerBlock.Add(localFunction);
        outerBlock.Add(new Return(null));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [IntType], outerBody);
    }

    /// <summary>
    /// The end-to-end teeth test: it does NOT set <see cref="IrInvariants.Enabled"/>
    /// itself. It relies on the flag being armed <em>by default</em> (#3267) and on
    /// the pipeline runner honoring it after every pass. A corrupting pass breaks
    /// the tree; running it through <see cref="IrPasses.Run"/> must throw. This is
    /// the only test that fails if the default is flipped back to off, the per-pass
    /// gate is deleted, or the runner stops calling CheckInvariant — the direct-call
    /// tests above would all still pass.
    /// </summary>
    [Fact]
    public void PipelineRunner_ThrowsWhenAPassCorruptsTheTree()
    {
        Assert.True(IrInvariants.Enabled,
            "IR invariants should be armed by default for any host that does not explicitly opt out.");

        var (function, block) = MinimalFunction();
        var passes = ImmutableArray.Create<IIrPass>(new SlotCorruptingPass(block));

        Assert.Throws<InvalidOperationException>(() => IrPasses.Run(function, passes));
    }

    static (IrFunction Function, Block Block) MinimalFunction()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(new Constant(0, intType)));
        container.Add(block);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);
        return (function, block);
    }

    sealed class SlotCorruptingPass(Block target) : IIrPass
    {
        public string Name => "SlotCorrupting(test)";

        public void Run(IrFunction function, PassContext context) =>
            ChildIndexField.SetValue(target, 99);
    }
}
