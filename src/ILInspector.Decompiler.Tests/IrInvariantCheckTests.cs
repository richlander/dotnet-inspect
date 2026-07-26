using System.Collections.Immutable;
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

    /// <summary>
    /// The end-to-end teeth test: it does NOT set <see cref="IrInvariants.Enabled"/>
    /// itself. It relies on the test host's module initializer having turned the
    /// flag on, and on the pipeline runner honoring it after every pass. A
    /// corrupting pass breaks the tree; running it through <see cref="IrPasses.Run"/>
    /// must throw. This is the only test that fails if the module initializer is
    /// removed, the per-pass gate is deleted, or the runner stops calling
    /// CheckInvariant — the direct-call tests above would all still pass.
    /// </summary>
    [Fact]
    public void PipelineRunner_UnderTestHost_ThrowsWhenAPassCorruptsTheTree()
    {
        Assert.True(IrInvariants.Enabled,
            "Test host module initializer should have enabled IR invariants for the suite.");

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
