using System.Collections.Immutable;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// A <c>pinned T&amp;</c> local has no faithful C# spelling on its own — left
/// flat it renders as the IL-only <c>pinned ref T name;</c> declaration, which is
/// not legal C# (CS1585/CS1002, the dominant malformed-Full family the validity
/// check surfaces over CoreLib's <c>[LibraryImport]</c> marshalling stubs).
/// <see cref="FixedStatementPass"/> raises the shapes it can prove into a real
/// <c>fixed</c> statement; the ones it cannot prove are deliberately left flat,
/// and those must degrade honestly to <see cref="DecompilationFidelity.Partial"/>
/// rather than claim <see cref="DecompilationFidelity.Full"/>. These tests pin the
/// fidelity rule directly so they stay meaningful regardless of which concrete
/// shapes the pass happens to raise.
/// </summary>
public class PinnedLocalFidelityTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef PinnedRefInt = TypeRef.Pinned(TypeRef.ByRef(Int32));

    static IrFunction Function(ImmutableArray<TypeRef> locals, BlockContainer body)
    {
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("System", "Object"), signature, locals, body);
    }

    static BlockContainer Container(params IrNode[] statements)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        foreach (var statement in statements)
            block.Add(statement);
        return container;
    }

    [Fact]
    public void UnraisedPinnedLocal_StillReferenced_DegradesToPartial()
    {
        // A pinned slot referenced by a store/load with no owning `fixed` is the
        // flat, un-raised shape: it would render `pinned ref int V_0;`, invalid C#.
        var body = Container(
            new StoreLocal(0, Int32, new Constant(0, Int32)),
            new ExpressionStatement(new LoadLocal(0, Int32)));

        var function = Function([PinnedRefInt], body);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void PinnedLocal_OwnedByFixedStatement_StaysFull()
    {
        // The raised shape: the pinned slot IS the `fixed` pointer variable, so a
        // body load of it is spellable. It must not trip the honest-degradation
        // rule even though the slot is still referenced and still typed `pinned`.
        var fixedBody = Container(new ExpressionStatement(new LoadLocal(0, Int32)));
        var body = Container(new Fixed(Int32, localIndex: 0, new Constant(0, Int32), fixedBody));

        var function = Function([PinnedRefInt], body);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void PinnedLocal_Unreferenced_StaysFull()
    {
        // The folded raise leaves the pinned slot unreferenced (the derived
        // pointer became the `fixed` variable). An unreferenced pinned slot is
        // never declared, so it has nothing to render and must not degrade.
        var body = Container(new ExpressionStatement(new LoadLocal(1, Int32)));

        var function = Function([PinnedRefInt, Int32], body);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }
}
