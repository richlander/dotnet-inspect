using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using DotnetInspector.Decompiler;

namespace DotnetInspector.Decompiler.Tests;

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

        helpers.CreateType();

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

        sample.CreateType();

        var stream = new MemoryStream();
        ab.Save(stream);
        stream.Position = 0;
        return stream;
    }
}
