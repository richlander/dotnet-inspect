using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MethodDefinitionHandle = System.Reflection.Metadata.MethodDefinitionHandle;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Soundness of the lock-sugar match, on shapes the C# compiler never emits
/// but hand-written or obfuscated IL could: a lockTaken local read after the
/// try/finally, a same-named Monitor from a non-BCL assembly, a wrong Enter
/// signature, extra trailing work in the finally, and an Exit whose object does
/// not match the Enter. Each must leave the construct flat. Built directly in the
/// post-structuring shape and run through LockSugarPass alone.
/// </summary>
public class LockSugarSoundnessTests
{
    static IrFunction BuildLock(string monitorAssembly, bool strayTakenRef, bool malformedEnterSignature = false, bool staticFieldLockObject = false, bool extraFinallyWork = false, bool mismatchedExitObject = false)
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var objType = TypeRef.CoreLib("System", "Object");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var monitor = monitorAssembly == TypeRef.CoreLibrary
            ? TypeRef.CoreLib("System.Threading", "Monitor")
            : TypeRef.Definition(monitorAssembly, "System.Threading", "Monitor");
        // A malformed Enter returns object instead of void — same name, type,
        // and argument node shapes, wrong signature.
        var enterReturn = malformedEnterSignature ? objType : voidType;
        var enterRef = new MethodRef(monitor, "Enter", enterReturn, [objType, TypeRef.ByRef(boolType)], HasThis: false);
        var exitRef = new MethodRef(monitor, "Exit", voidType, [objType], HasThis: false);
        // A second object local (index 2) so the finally can Exit a DIFFERENT
        // object than Enter locked — Enter(V_0)/Exit(V_2): not a real lock.
        System.Collections.Immutable.ImmutableArray<TypeRef> locals = mismatchedExitObject ? [objType, boolType, objType] : [objType, boolType];
        int exitObjectIndex = mismatchedExitObject ? 2 : 0;

        var tryBlock = new Block(0);
        tryBlock.Add(new ExpressionStatement(new Call(enterRef, false,
            [new LoadLocal(0, objType), new LoadLocalAddress(1, boolType)])));
        var tryBody = new BlockContainer();
        tryBody.Add(tryBlock);

        var thenBlock = new Block(0);
        thenBlock.Add(new ExpressionStatement(new Call(exitRef, false, [new LoadLocal(exitObjectIndex, objType)])));
        var finallyBlock = new Block(0);
        finallyBlock.Add(new IfStatement(new LoadLocal(1, boolType), thenBlock, null));
        if (extraFinallyWork)
            finallyBlock.Add(new ExpressionStatement(new LoadLocal(0, objType)));   // finally does more than guarded Exit
        var finallyBody = new BlockContainer();
        finallyBody.Add(finallyBlock);

        var entry = new Block(0);
        IrExpression lockObject = staticFieldLockObject
            ? new LoadField(new FieldRef(TypeRef.Definition("SyntheticAssembly", "Samples", "Owner"), "Gate", objType), instance: null)
            : new Constant(null, objType);
        entry.Add(new StoreLocal(0, objType, lockObject));
        entry.Add(new StoreLocal(1, boolType, new Constant(0, boolType)));
        entry.Add(new TryFinally(tryBody, finallyBody));
        if (strayTakenRef)
            entry.Add(new ExpressionStatement(new LoadLocal(1, boolType)));   // reads V_1 after the lock
        var body = new BlockContainer();
        body.Add(entry);

        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, locals, body);
    }

    [Fact]
    public void CleanShape_Raises()   // positive control: the synthetic shape is well-formed
    {
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false);
        new LockSugarPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Empty(function.Descendants.OfType<TryFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void StaticFieldReceiver_RaisesButKeepsObjectCopy()
    {
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false, staticFieldLockObject: true);
        new LockSugarPass().Run(function, PassContext.None);

        var lockNode = Assert.Single(function.Descendants.OfType<Pipeline.Lock>());
        var lockObject = Assert.IsType<LoadLocal>(lockNode.LockObject);
        Assert.Equal(0, lockObject.Index);
        Assert.Contains(function.Descendants.OfType<StoreLocal>(), store => store.Index == 0);
        Assert.Empty(function.Descendants.OfType<TryFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void LockTakenReadAfterTryFinally_StaysFlat()
    {
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: true);
        new LockSugarPass().Run(function, PassContext.None);

        // Detaching the stores would strand the later read of V_1.
        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void MonitorFromOtherAssembly_StaysFlat()
    {
        var function = BuildLock("SomeUserAssembly", strayTakenRef: false);
        new LockSugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void WrongMonitorSignature_StaysFlat()
    {
        // Right name, type, and argument shapes — but Enter returns object,
        // not void. The signature check rejects it.
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false, malformedEnterSignature: true);
        new LockSugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void ExtraFinallyWork_StaysFlat()
    {
        // The finally does more than the guarded Monitor.Exit. csc's lock never
        // emits trailing finally work, so the single-statement finally check
        // must reject it — raising would drop that extra statement.
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false, extraFinallyWork: true);
        new LockSugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void ExitObjectMismatch_StaysFlat()
    {
        // Enter locks V_0 but the finally Exits a different object (V_2). The two
        // are not a matched lock acquire/release, so the object-identity check
        // must reject it rather than invent lock (V_0).
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false, mismatchedExitObject: true);
        new LockSugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }
}
