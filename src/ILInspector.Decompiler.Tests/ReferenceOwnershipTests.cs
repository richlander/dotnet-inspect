using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ReferenceOwnershipTests
{
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void IsInside_MatchesAncestorsOnly()
    {
        var root = new Block();
        var store = new StoreLocal(0, Int, new Constant(1, Int));
        var sibling = new Return(new Constant(0, Int));
        root.Add(store);
        root.Add(sibling);

        Assert.True(ReferenceOwnership.IsInside(store.Value, store));
        Assert.True(ReferenceOwnership.IsInside(store.Value, root));
        Assert.False(ReferenceOwnership.IsInside(store.Value, sibling));
    }

    [Fact]
    public void LocalReferencesOnlyWithin_AllowsStoreLoadAndAddressUnderAllowedRoots()
    {
        var allowed = new Block();
        var store = new StoreLocal(0, Int, new Constant(1, Int));
        var load = new ExpressionStatement(new LoadLocal(0, Int));
        var address = new ExpressionStatement(new LoadLocalAddress(0, Int));
        allowed.Add(store);
        allowed.Add(load);
        allowed.Add(address);

        var function = Function(allowed);

        Assert.True(ReferenceOwnership.LocalReferencesOnlyWithin(function, 0, [allowed]));
    }

    [Fact]
    public void LocalReferencesOnlyWithin_RejectsExternalLoad()
    {
        var allowed = new Block();
        allowed.Add(new StoreLocal(0, Int, new Constant(1, Int)));
        var external = new Block();
        external.Add(new Return(new LoadLocal(0, Int)));

        var function = Function(allowed, external);

        Assert.False(ReferenceOwnership.LocalReferencesOnlyWithin(function, 0, [allowed]));
    }

    [Fact]
    public void StackSlotReferencesOnlyWithin_CoversLoadAndStore()
    {
        var allowed = StackSlotBlock();

        var function = Function(allowed);

        Assert.True(ReferenceOwnership.StackSlotReferencesOnlyWithin(function, 256, [allowed]));

        allowed = StackSlotBlock();
        var external = new Block();
        external.Add(new Return(new LoadStackSlot(256, Int)));
        function = Function(allowed, external);

        Assert.False(ReferenceOwnership.StackSlotReferencesOnlyWithin(function, 256, [external]));
    }

    [Fact]
    public void SubtreeReferenceAtoms_IncludeRootAndDescendants()
    {
        var store = new StoreLocal(0, Int, new Constant(1, Int));
        var returnLoad = new Return(new LoadLocal(0, Int));
        var address = new ExpressionStatement(new LoadLocalAddress(0, Int));

        Assert.True(ReferenceOwnership.SubtreeReferencesLocal(store, 0));
        Assert.True(ReferenceOwnership.SubtreeReferencesLocal(returnLoad, 0));
        Assert.True(ReferenceOwnership.SubtreeReferencesLocal(address, 0));
        Assert.True(ReferenceOwnership.SubtreeStoresLocal(store, 0));
        Assert.False(ReferenceOwnership.SubtreeStoresLocal(returnLoad, 0));
    }

    // A local can be bound or written through a designation that carries the
    // index directly (a null-coalescing target, a pattern binding, a foreach
    // header) rather than an explicit Load/Store. ReferencesLocal only knows the
    // Load/Store atoms, so a confinement proof built on it alone is blind to
    // these; BindsLocal / ReferencesOrBindsLocal close that gap.
    [Fact]
    public void BindsLocal_CountsIndexBearingDesignationsMissedByReferencesLocal()
    {
        var generic = TypeRef.GenericParameter(0, "T");
        var objectType = TypeRef.CoreLib("System", "Object");
        var nullCoalescing = new NullCoalescingAssignment(0, Int, new Constant(1, Int));
        var pattern = new IsPattern(new LoadArgument(0, "x", objectType), generic, 0);

        Assert.False(ReferenceOwnership.ReferencesLocal(nullCoalescing, 0));
        Assert.False(ReferenceOwnership.ReferencesLocal(pattern, 0));

        Assert.True(ReferenceOwnership.BindsLocal(nullCoalescing, 0));
        Assert.True(ReferenceOwnership.BindsLocal(pattern, 0));
        Assert.True(ReferenceOwnership.ReferencesOrBindsLocal(nullCoalescing, 0));
        Assert.True(ReferenceOwnership.ReferencesOrBindsLocal(pattern, 0));

        Assert.False(ReferenceOwnership.BindsLocal(nullCoalescing, 1));
        Assert.False(ReferenceOwnership.BindsLocal(pattern, 1));
    }

    // The legacy Load/Store-only confinement check passes even when an external
    // binding writes the local through an index-bearing designation, because it
    // never sees that node kind. The completeness-aware variant rejects it, so a
    // rewrite that narrows the local's scope cannot leave the external binding
    // referencing an out-of-scope local.
    [Fact]
    public void LocalReferencedOrBoundOnlyWithin_RejectsExternalBindingLoadStoreMisses()
    {
        var allowed = new Block();
        allowed.Add(new StoreLocal(0, Int, new Constant(1, Int)));
        var external = new Block();
        external.Add(new NullCoalescingAssignment(0, Int, new Constant(2, Int)));

        var function = Function(allowed, external);

        Assert.True(ReferenceOwnership.LocalReferencesOnlyWithin(function, 0, [allowed]));
        Assert.False(ReferenceOwnership.LocalReferencedOrBoundOnlyWithin(function, 0, [allowed]));
    }

    static IrFunction Function(params Block[] blocks)
    {
        var container = new BlockContainer();
        foreach (var block in blocks)
            container.Add(block);

        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Tests", "Owner"), signature, [Int], container);
    }

    static Block StackSlotBlock()
    {
        var block = new Block();
        block.Add(new StoreStackSlot(256, new Constant(1, Int)));
        block.Add(new Return(new LoadStackSlot(256, Int)));
        return block;
    }
}
