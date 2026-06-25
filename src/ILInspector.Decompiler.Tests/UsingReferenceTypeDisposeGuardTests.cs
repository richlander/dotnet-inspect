using System.Collections.Generic;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Adversarial guard for UsingStatementPass's unguarded (value-type) dispose branch
// (issue #1407). csc only omits the null guard for value-type / constrained dispose;
// a reference-type `using` always emits the null-guarded `finally { if (V) V.Dispose(); }`.
// Raising an *unguarded* reference-type `finally { V.Dispose(); }` to `using` injects a
// null guard the source never had (turning a possible NullReferenceException into a
// no-op), so a known reference-type resource must stay a try/finally. csc never emits
// this shape — it is a synthetic-IR near-miss — paired with value-type and null-guarded
// positive canaries that must still raise.
public class UsingReferenceTypeDisposeGuardTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef IDisposable = TypeRef.CoreLib("System", "IDisposable");
    static readonly TypeRef RefRes = TypeRef.Definition("UserAssembly", "Samples", "RefResource");
    static readonly TypeRef ValRes = TypeRef.Definition("UserAssembly", "Samples", "ValResource", ValueTypeHint.ValueType);

    static IrFunction Build(TypeRef resType, bool guarded, bool patternDispose, TypeShape shape)
    {
        // The dispose call: pattern dispose is a non-virtual Dispose() on the resource
        // type called through its address; interface dispose is virtual IDisposable.Dispose().
        Call Dispose() => patternDispose
            ? new Call(new MethodRef(resType, "Dispose", Void, [], HasThis: true), isVirtual: false,
                [new LoadLocalAddress(0, resType)])
            : new Call(new MethodRef(IDisposable, "Dispose", Void, [], HasThis: true), isVirtual: true,
                [new LoadLocal(0, resType)]);

        var finallyBlock = new Block(0);
        if (guarded)
        {
            var then = new Block(0);
            then.Add(new ExpressionStatement(Dispose()));
            finallyBlock.Add(new IfStatement(new LoadLocal(0, resType), then, null));
        }
        else
        {
            finallyBlock.Add(new ExpressionStatement(Dispose()));
        }
        var finallyBody = new BlockContainer();
        finallyBody.Add(finallyBlock);

        var tryBody = new BlockContainer();
        tryBody.Add(new Block(0));

        var entry = new Block(0);
        entry.Add(new StoreLocal(0, resType, new Constant(null, resType)));
        entry.Add(new TryFinally(tryBody, finallyBody));
        var body = new BlockContainer();
        body.Add(entry);

        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [resType], body);
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [resType] = shape };

        new UsingStatementPass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    [Fact]
    public void UnguardedReferenceTypeDispose_StaysTryFinally()
    {
        var function = Build(RefRes, guarded: false, patternDispose: false, TypeShape.Reference);
        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void UnguardedValueTypeDispose_RaisesToUsing()
    {
        var function = Build(ValRes, guarded: false, patternDispose: true, TypeShape.ValueType);
        Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Empty(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void NullGuardedReferenceTypeDispose_RaisesToUsing()
    {
        // The normal reference-type using shape (null-guarded) is unaffected.
        var function = Build(RefRes, guarded: true, patternDispose: false, TypeShape.Reference);
        Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Empty(function.Descendants.OfType<TryFinally>());
    }
}
