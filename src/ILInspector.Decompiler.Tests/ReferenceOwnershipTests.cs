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
