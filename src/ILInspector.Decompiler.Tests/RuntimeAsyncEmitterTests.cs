using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Verifies that the C# emitter renders .NET 11 "runtime async" (async v2) methods back
/// into <c>await</c> expressions. Runtime async lowers <c>await x</c> to a call to
/// <c>System.Runtime.CompilerServices.AsyncHelpers.Await(x)</c> rather than a state machine.
///
/// The decompiler-test assembly targets net10.0, so a real runtime-async method cannot be
/// produced by compiling C#. Instead these tests synthesize an assembly with
/// <see cref="System.Reflection.Emit.PersistedAssemblyBuilder"/> that contains an
/// <c>AsyncHelpers.Await</c> helper and methods that call it (carrying the
/// <c>MethodImplAttributes.Async</c> (0x2000) flag, matching real runtime-async metadata).
/// </summary>
public class RuntimeAsyncEmitterTests
{
    [Fact]
    public void VoidAwait_RendersAwaitStatement()
    {
        string code = EmitRuntimeAsync("AwaitVoid");

        Assert.Contains("await t", code);
        Assert.DoesNotContain("AsyncHelpers.Await", code);
        Assert.DoesNotContain("Await(", code);
    }

    [Fact]
    public void ValueAwait_RendersReturnAwait()
    {
        string code = EmitRuntimeAsync("AwaitValue");

        Assert.Contains("return await t", code);
        Assert.DoesNotContain("AsyncHelpers.Await", code);
    }

    [Fact]
    public void AwaitResultUsedAsReceiver_IsParenthesized()
    {
        string code = EmitRuntimeAsync("AwaitReceiver");

        // The await result is the receiver of a member access, so it must be parenthesized:
        // (await t).Length — never "await t.Length".
        Assert.Contains("(await t).Length", code);
        Assert.DoesNotContain("AsyncHelpers.Await", code);
    }

    [Fact]
    public void TaskReturningRuntimeAsync_DoesNotThrowOnStackSimulation()
    {
        // A runtime-async method declares Task/Task<T> in metadata, but its IL `ret` carries the
        // unwrapped value (or nothing for Task). The stack simulator must not treat the declared
        // Task as an on-stack return value, or it underflows. Regression guard for that crash.
        Assert.Null(Record.Exception(() => EmitRuntimeAsync("AwaitVoid")));
        Assert.Null(Record.Exception(() => EmitRuntimeAsync("AwaitValue")));
    }

    [Fact]
    public void CustomAwait_RendersAwaitedAwaitable()
    {
        string code = EmitRuntimeAsync("AwaitCustomValue");

        Assert.Contains("return await t", code);
        Assert.DoesNotContain("GetAwaiter", code);
        Assert.DoesNotContain("IsCompleted", code);
        Assert.DoesNotContain("AwaitAwaiter", code);
        Assert.DoesNotContain("GetResult", code);
    }

    [Fact]
    public void CustomAwait_BrfalseHelperPath_RendersAwaitedAwaitable()
    {
        string code = EmitRuntimeAsync("AwaitCustomValueBrfalse");

        Assert.Contains("return await t", code);
        Assert.DoesNotContain("GetAwaiter", code);
        Assert.DoesNotContain("AwaitAwaiter", code);
        Assert.DoesNotContain("GetResult", code);
    }

    [Fact]
    public void CustomAwait_InvertedGuard_DoesNotFoldToAwait()
    {
        string code = EmitRuntimeAsync("InvertedCustomAwaitGuard");

        Assert.DoesNotContain("return await t", code);
        Assert.Contains("AwaitAwaiter", code);
        Assert.Contains("GetResult", code);
    }

    [Fact]
    public void StructCustomAwait_DoesNotRenderRefAwaitOperand()
    {
        string code = EmitRuntimeAsync("AwaitCustomStructValue");

        Assert.Contains("return await t", code);
        Assert.DoesNotContain("await ref t", code);
        Assert.DoesNotContain("GetAwaiter", code);
        Assert.DoesNotContain("AwaitAwaiter", code);
        Assert.DoesNotContain("GetResult", code);
    }

    [Fact]
    public void StructArrayElementCustomAwait_DoesNotRenderRefAwaitOperand()
    {
        string code = EmitRuntimeAsync("AwaitCustomStructArrayElement");

        Assert.Contains("return await values[0]", code);
        Assert.DoesNotContain("await ref values[0]", code);
        Assert.DoesNotContain("GetAwaiter", code);
        Assert.DoesNotContain("AwaitAwaiter", code);
        Assert.DoesNotContain("GetResult", code);
    }

    static string EmitRuntimeAsync(string methodName)
    {
        using var stream = BuildRuntimeAsyncAssembly();
        using var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(peReader, "RuntimeAsyncSample", methodName);
        Assert.NotNull(context);
        return CSharpEmitter.Emit(context);
    }

    private static MemoryStream BuildRuntimeAsyncAssembly()
    {
        const MethodImplAttributes AsyncImplFlag = (MethodImplAttributes)0x2000;

        var ab = new PersistedAssemblyBuilder(
            new AssemblyName("RuntimeAsyncEmitterFixture"), typeof(object).Assembly);
        var module = ab.DefineDynamicModule("RuntimeAsyncEmitterFixture");

        // System.Runtime.CompilerServices.AsyncHelpers with the Await helpers the compiler targets.
        var helpers = module.DefineType(
            "System.Runtime.CompilerServices.AsyncHelpers",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        var awaitVoid = helpers.DefineMethod(
            "Await", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), [typeof(Task)]);
        var awaitVoidIl = awaitVoid.GetILGenerator();
        awaitVoidIl.Emit(OpCodes.Ret);

        var awaitGeneric = helpers.DefineMethod(
            "Await", MethodAttributes.Public | MethodAttributes.Static);
        var tp = awaitGeneric.DefineGenericParameters("T")[0];
        awaitGeneric.SetReturnType(tp);
        awaitGeneric.SetParameters(typeof(Task<>).MakeGenericType(tp));
        var awaitGenericIl = awaitGeneric.GetILGenerator();
        var defaultLocal = awaitGenericIl.DeclareLocal(tp);
        awaitGenericIl.Emit(OpCodes.Ldloca_S, defaultLocal);
        awaitGenericIl.Emit(OpCodes.Initobj, tp);
        awaitGenericIl.Emit(OpCodes.Ldloc, defaultLocal);
        awaitGenericIl.Emit(OpCodes.Ret);

        var awaitAwaiterGeneric = helpers.DefineMethod(
            "AwaitAwaiter", MethodAttributes.Public | MethodAttributes.Static);
        var awaiterTp = awaitAwaiterGeneric.DefineGenericParameters("TAwaiter")[0];
        awaitAwaiterGeneric.SetReturnType(typeof(void));
        awaitAwaiterGeneric.SetParameters(awaiterTp);
        var awaitAwaiterIl = awaitAwaiterGeneric.GetILGenerator();
        awaitAwaiterIl.Emit(OpCodes.Ret);

        helpers.CreateType();

        var awaiterType = module.DefineType("CustomAwaiter", TypeAttributes.Public | TypeAttributes.Class);
        var awaiterCtor = awaiterType.DefineDefaultConstructor(MethodAttributes.Public);
        var isCompleted = awaiterType.DefineMethod(
            "get_IsCompleted",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(bool),
            Type.EmptyTypes);
        var isCompletedIl = isCompleted.GetILGenerator();
        isCompletedIl.Emit(OpCodes.Ldc_I4_0);
        isCompletedIl.Emit(OpCodes.Ret);

        var getResult = awaiterType.DefineMethod(
            "GetResult",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes);
        var getResultIl = getResult.GetILGenerator();
        getResultIl.Emit(OpCodes.Ldc_I4, 42);
        getResultIl.Emit(OpCodes.Ret);
        var customAwaiter = awaiterType.CreateType();

        var awaitableType = module.DefineType("CustomAwaitable", TypeAttributes.Public | TypeAttributes.Class);
        awaitableType.DefineDefaultConstructor(MethodAttributes.Public);
        var getAwaiter = awaitableType.DefineMethod(
            "GetAwaiter",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            customAwaiter,
            Type.EmptyTypes);
        var getAwaiterIl = getAwaiter.GetILGenerator();
        getAwaiterIl.Emit(OpCodes.Newobj, awaiterCtor);
        getAwaiterIl.Emit(OpCodes.Ret);
        var customAwaitable = awaitableType.CreateType();

        var structAwaitableType = module.DefineType(
            "CustomStructAwaitable",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
            typeof(ValueType));
        var structGetAwaiter = structAwaitableType.DefineMethod(
            "GetAwaiter",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            customAwaiter,
            Type.EmptyTypes);
        var structGetAwaiterIl = structGetAwaiter.GetILGenerator();
        structGetAwaiterIl.Emit(OpCodes.Newobj, awaiterCtor);
        structGetAwaiterIl.Emit(OpCodes.Ret);
        var customStructAwaitable = structAwaitableType.CreateType();

        var sample = module.DefineType("RuntimeAsyncSample", TypeAttributes.Public | TypeAttributes.Class);

        // static Task AwaitVoid(Task t) => await t; (IL `ret` carries nothing; metadata says Task)
        var awaitVoidCaller = sample.DefineMethod(
            "AwaitVoid", MethodAttributes.Public | MethodAttributes.Static,
            typeof(Task), [typeof(Task)]);
        awaitVoidCaller.DefineParameter(1, ParameterAttributes.None, "t");
        var voidIl = awaitVoidCaller.GetILGenerator();
        voidIl.Emit(OpCodes.Ldarg_0);
        voidIl.Emit(OpCodes.Call, awaitVoid);
        voidIl.Emit(OpCodes.Ret);
        awaitVoidCaller.SetImplementationFlags(AsyncImplFlag);

        // static Task<int> AwaitValue(Task<int> t) => return await t; (IL `ret` carries the int)
        var awaitInt = awaitGeneric.MakeGenericMethod(typeof(int));
        var awaitValueCaller = sample.DefineMethod(
            "AwaitValue", MethodAttributes.Public | MethodAttributes.Static,
            typeof(Task<int>), [typeof(Task<int>)]);
        awaitValueCaller.DefineParameter(1, ParameterAttributes.None, "t");
        var valueIl = awaitValueCaller.GetILGenerator();
        valueIl.Emit(OpCodes.Ldarg_0);
        valueIl.Emit(OpCodes.Call, awaitInt);
        valueIl.Emit(OpCodes.Ret);
        awaitValueCaller.SetImplementationFlags(AsyncImplFlag);

        // static Task<int> AwaitReceiver(Task<string> t) => return (await t).Length;
        var awaitString = awaitGeneric.MakeGenericMethod(typeof(string));
        var awaitReceiverCaller = sample.DefineMethod(
            "AwaitReceiver", MethodAttributes.Public | MethodAttributes.Static,
            typeof(Task<int>), [typeof(Task<string>)]);
        awaitReceiverCaller.DefineParameter(1, ParameterAttributes.None, "t");
        var receiverIl = awaitReceiverCaller.GetILGenerator();
        receiverIl.Emit(OpCodes.Ldarg_0);
        receiverIl.Emit(OpCodes.Call, awaitString);
        receiverIl.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Length")!);
        receiverIl.Emit(OpCodes.Ret);
        awaitReceiverCaller.SetImplementationFlags(AsyncImplFlag);

        var awaitCustomAwaiter = awaitAwaiterGeneric.MakeGenericMethod(customAwaiter);
        var awaitCustomCaller = sample.DefineMethod(
            "AwaitCustomValue", MethodAttributes.Public | MethodAttributes.Static,
            typeof(Task<int>), [customAwaitable]);
        awaitCustomCaller.DefineParameter(1, ParameterAttributes.None, "t");
        var customIl = awaitCustomCaller.GetILGenerator();
        var awaiterLocal = customIl.DeclareLocal(customAwaiter);
        var afterAwait = customIl.DefineLabel();
        customIl.Emit(OpCodes.Ldarg_0);
        customIl.Emit(OpCodes.Callvirt, getAwaiter);
        customIl.Emit(OpCodes.Stloc, awaiterLocal);
        customIl.Emit(OpCodes.Ldloc, awaiterLocal);
        customIl.Emit(OpCodes.Callvirt, isCompleted);
        customIl.Emit(OpCodes.Brtrue_S, afterAwait);
        customIl.Emit(OpCodes.Ldloc, awaiterLocal);
        customIl.Emit(OpCodes.Call, awaitCustomAwaiter);
        customIl.MarkLabel(afterAwait);
        customIl.Emit(OpCodes.Ldloc, awaiterLocal);
        customIl.Emit(OpCodes.Callvirt, getResult);
        customIl.Emit(OpCodes.Ret);
        awaitCustomCaller.SetImplementationFlags(AsyncImplFlag);

        var awaitCustomBrfalseCaller = sample.DefineMethod(
            "AwaitCustomValueBrfalse", MethodAttributes.Public | MethodAttributes.Static,
            typeof(Task<int>), [customAwaitable]);
        awaitCustomBrfalseCaller.DefineParameter(1, ParameterAttributes.None, "t");
        var customBrfalseIl = awaitCustomBrfalseCaller.GetILGenerator();
        var brfalseAwaiterLocal = customBrfalseIl.DeclareLocal(customAwaiter);
        var brfalseHelper = customBrfalseIl.DefineLabel();
        var afterBrfalseAwait = customBrfalseIl.DefineLabel();
        customBrfalseIl.Emit(OpCodes.Ldarg_0);
        customBrfalseIl.Emit(OpCodes.Callvirt, getAwaiter);
        customBrfalseIl.Emit(OpCodes.Stloc, brfalseAwaiterLocal);
        customBrfalseIl.Emit(OpCodes.Ldloc, brfalseAwaiterLocal);
        customBrfalseIl.Emit(OpCodes.Callvirt, isCompleted);
        customBrfalseIl.Emit(OpCodes.Brfalse_S, brfalseHelper);
        customBrfalseIl.Emit(OpCodes.Br_S, afterBrfalseAwait);
        customBrfalseIl.MarkLabel(brfalseHelper);
        customBrfalseIl.Emit(OpCodes.Ldloc, brfalseAwaiterLocal);
        customBrfalseIl.Emit(OpCodes.Call, awaitCustomAwaiter);
        customBrfalseIl.MarkLabel(afterBrfalseAwait);
        customBrfalseIl.Emit(OpCodes.Ldloc, brfalseAwaiterLocal);
        customBrfalseIl.Emit(OpCodes.Callvirt, getResult);
        customBrfalseIl.Emit(OpCodes.Ret);
        awaitCustomBrfalseCaller.SetImplementationFlags(AsyncImplFlag);

        var invertedGuardCaller = sample.DefineMethod(
            "InvertedCustomAwaitGuard", MethodAttributes.Public | MethodAttributes.Static,
            typeof(Task<int>), [customAwaitable]);
        invertedGuardCaller.DefineParameter(1, ParameterAttributes.None, "t");
        var invertedIl = invertedGuardCaller.GetILGenerator();
        var invertedAwaiterLocal = invertedIl.DeclareLocal(customAwaiter);
        var invertedHelper = invertedIl.DefineLabel();
        var afterInverted = invertedIl.DefineLabel();
        invertedIl.Emit(OpCodes.Ldarg_0);
        invertedIl.Emit(OpCodes.Callvirt, getAwaiter);
        invertedIl.Emit(OpCodes.Stloc, invertedAwaiterLocal);
        invertedIl.Emit(OpCodes.Ldloc, invertedAwaiterLocal);
        invertedIl.Emit(OpCodes.Callvirt, isCompleted);
        invertedIl.Emit(OpCodes.Brtrue_S, invertedHelper);
        invertedIl.Emit(OpCodes.Br_S, afterInverted);
        invertedIl.MarkLabel(invertedHelper);
        invertedIl.Emit(OpCodes.Ldloc, invertedAwaiterLocal);
        invertedIl.Emit(OpCodes.Call, awaitCustomAwaiter);
        invertedIl.MarkLabel(afterInverted);
        invertedIl.Emit(OpCodes.Ldloc, invertedAwaiterLocal);
        invertedIl.Emit(OpCodes.Callvirt, getResult);
        invertedIl.Emit(OpCodes.Ret);
        invertedGuardCaller.SetImplementationFlags(AsyncImplFlag);

        var awaitCustomStructCaller = sample.DefineMethod(
            "AwaitCustomStructValue", MethodAttributes.Public | MethodAttributes.Static,
            typeof(Task<int>), [customStructAwaitable]);
        awaitCustomStructCaller.DefineParameter(1, ParameterAttributes.None, "t");
        var customStructIl = awaitCustomStructCaller.GetILGenerator();
        var structAwaiterLocal = customStructIl.DeclareLocal(customAwaiter);
        var afterStructAwait = customStructIl.DefineLabel();
        customStructIl.Emit(OpCodes.Ldarga_S, (byte)0);
        customStructIl.Emit(OpCodes.Call, structGetAwaiter);
        customStructIl.Emit(OpCodes.Stloc, structAwaiterLocal);
        customStructIl.Emit(OpCodes.Ldloc, structAwaiterLocal);
        customStructIl.Emit(OpCodes.Callvirt, isCompleted);
        customStructIl.Emit(OpCodes.Brtrue_S, afterStructAwait);
        customStructIl.Emit(OpCodes.Ldloc, structAwaiterLocal);
        customStructIl.Emit(OpCodes.Call, awaitCustomAwaiter);
        customStructIl.MarkLabel(afterStructAwait);
        customStructIl.Emit(OpCodes.Ldloc, structAwaiterLocal);
        customStructIl.Emit(OpCodes.Callvirt, getResult);
        customStructIl.Emit(OpCodes.Ret);
        awaitCustomStructCaller.SetImplementationFlags(AsyncImplFlag);

        var awaitCustomStructArrayCaller = sample.DefineMethod(
            "AwaitCustomStructArrayElement", MethodAttributes.Public | MethodAttributes.Static,
            typeof(Task<int>), [customStructAwaitable.MakeArrayType()]);
        awaitCustomStructArrayCaller.DefineParameter(1, ParameterAttributes.None, "values");
        var customStructArrayIl = awaitCustomStructArrayCaller.GetILGenerator();
        var structArrayAwaiterLocal = customStructArrayIl.DeclareLocal(customAwaiter);
        var afterStructArrayAwait = customStructArrayIl.DefineLabel();
        customStructArrayIl.Emit(OpCodes.Ldarg_0);
        customStructArrayIl.Emit(OpCodes.Ldc_I4_0);
        customStructArrayIl.Emit(OpCodes.Ldelema, customStructAwaitable);
        customStructArrayIl.Emit(OpCodes.Call, structGetAwaiter);
        customStructArrayIl.Emit(OpCodes.Stloc, structArrayAwaiterLocal);
        customStructArrayIl.Emit(OpCodes.Ldloc, structArrayAwaiterLocal);
        customStructArrayIl.Emit(OpCodes.Callvirt, isCompleted);
        customStructArrayIl.Emit(OpCodes.Brtrue_S, afterStructArrayAwait);
        customStructArrayIl.Emit(OpCodes.Ldloc, structArrayAwaiterLocal);
        customStructArrayIl.Emit(OpCodes.Call, awaitCustomAwaiter);
        customStructArrayIl.MarkLabel(afterStructArrayAwait);
        customStructArrayIl.Emit(OpCodes.Ldloc, structArrayAwaiterLocal);
        customStructArrayIl.Emit(OpCodes.Callvirt, getResult);
        customStructArrayIl.Emit(OpCodes.Ret);
        awaitCustomStructArrayCaller.SetImplementationFlags(AsyncImplFlag);

        sample.CreateType();

        var stream = new MemoryStream();
        ab.Save(stream);
        stream.Position = 0;
        return stream;
    }
}
