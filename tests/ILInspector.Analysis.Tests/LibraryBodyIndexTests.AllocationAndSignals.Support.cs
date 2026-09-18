using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    static IEnumerable<string> ArrayShapes(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape is "small-array" or "stackalloc-candidate")
            .Select(o => o.Shape);

    static AllocationOccurrence SingleAllocationOccurrence(LibraryBodyIndex index, string methodName, AllocationKind kind)
        => SingleAllocationOccurrence(index, nameof(OptimizationOpportunityFixtures), methodName, kind);

    static AllocationOccurrence SingleAllocationOccurrence(LibraryBodyIndex index, string typeName, string methodName, AllocationKind kind)
    {
        var method = Assert.Single(index.Methods.Where(m =>
            m.DeclaringType.Name == typeName
            && m.Name == methodName));
        Assert.True(index.GetAllocationOccurrences().TryGetValue(method.MetadataToken, out var occurrences));
        return Assert.Single(occurrences.Where(occurrence =>
            occurrence.Kind == kind
            && occurrence.CountsAsHeapAllocation));
    }

    static (string Path, string Directory) BuildSlotReuseArrayFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnet-inspect-slot-reuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "SlotReuseArrayFixture.dll");

        var assemblyName = new AssemblyName("SlotReuseArrayFixture");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("SlotReuseArrayFixture", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = type.DefineMethod(
            "LocalThenEscapingSlotReuse",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int[]),
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.DeclareLocal(typeof(int[]));

        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Newarr, typeof(int));
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stelem_I4);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Newarr, typeof(int));
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ret);

        type.CreateType();
        assembly.Save(path);
        return (path, directory);
    }

    static (string Path, string Directory) BuildLongAddressLoadArrayFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnet-inspect-long-address-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "LongAddressLoadArrayFixture.dll");

        var assemblyName = new AssemblyName("LongAddressLoadArrayFixture");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("LongAddressLoadArrayFixture", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);

        DefineLdlocaMethod(type);
        DefineLdargaMethod(type);

        type.CreateType();
        assembly.Save(path);
        return (path, directory);

        static void DefineLdlocaMethod(TypeBuilder type)
        {
            var method = type.DefineMethod(
                "ArrayReturnedAfterLongLdloca",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int[]),
                Type.EmptyTypes);
            var il = method.GetILGenerator();
            var array = il.DeclareLocal(typeof(int[]));
            var marker = il.DeclareLocal(typeof(int));

            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Newarr, typeof(int));
            il.Emit(OpCodes.Stloc, array);
            il.Emit(OpCodes.Ldloc, array);
            il.Emit(OpCodes.Ldloca, marker);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }

        static void DefineLdargaMethod(TypeBuilder type)
        {
            var method = type.DefineMethod(
                "ArrayReturnedAfterLongLdarga",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int[]),
                [typeof(int)]);
            var il = method.GetILGenerator();
            var array = il.DeclareLocal(typeof(int[]));

            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Newarr, typeof(int));
            il.Emit(OpCodes.Stloc, array);
            il.Emit(OpCodes.Ldloc, array);
            il.Emit(OpCodes.Ldarga, (short)0);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }
    }

    static (string Path, string Directory) BuildPathContextFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnet-inspect-path-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "PathContextFixture.dll");

        var assemblyName = new AssemblyName("PathContextFixture");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("PathContextFixture", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var ctor = typeof(PlainObject).GetConstructor([typeof(int)])!;

        DefineBranchAllocation(type, ctor);
        DefineSwitchAllocation(type, ctor);
        DefineAfterIfJoinAllocation(type, ctor);
        DefineReturnOrInfiniteLoopAllocation(type, ctor);
        DefineInternalReturnLoopAllocation(type, ctor);

        type.CreateType();
        assembly.Save(path);
        return (path, directory);

        static void EmitNewPlainObject(ILGenerator il, ConstructorInfo ctor, int value)
        {
            il.Emit(OpCodes.Ldc_I4, value);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);
        }

        static void DefineBranchAllocation(TypeBuilder type, ConstructorInfo ctor)
        {
            var method = type.DefineMethod(
                "BranchAllocation",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(bool)]);
            var il = method.GetILGenerator();
            var elseLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse_S, elseLabel);
            EmitNewPlainObject(il, ctor, 1);
            il.MarkLabel(elseLabel);
            EmitNewPlainObject(il, ctor, 2);
        }

        static void DefineSwitchAllocation(TypeBuilder type, ConstructorInfo ctor)
        {
            var method = type.DefineMethod(
                "SwitchAllocation",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(int)]);
            var il = method.GetILGenerator();
            var case0 = il.DefineLabel();
            var case1 = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Switch, [case0, case1]);
            EmitNewPlainObject(il, ctor, 3);
            il.MarkLabel(case0);
            EmitNewPlainObject(il, ctor, 1);
            il.MarkLabel(case1);
            EmitNewPlainObject(il, ctor, 2);
        }

        static void DefineAfterIfJoinAllocation(TypeBuilder type, ConstructorInfo ctor)
        {
            var method = type.DefineMethod(
                "AfterIfJoinAllocation",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(bool)]);
            var il = method.GetILGenerator();
            var join = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse_S, join);
            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Br_S, join);
            il.MarkLabel(join);
            EmitNewPlainObject(il, ctor, 1);
        }

        static void DefineReturnOrInfiniteLoopAllocation(TypeBuilder type, ConstructorInfo ctor)
        {
            var method = type.DefineMethod(
                "ReturnOrInfiniteLoopAllocation",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(bool)]);
            var il = method.GetILGenerator();
            var loop = il.DefineLabel();
            var value = il.DeclareLocal(typeof(object));
            il.Emit(OpCodes.Ldc_I4, 1);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue_S, loop);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(loop);
            il.Emit(OpCodes.Br_S, loop);
        }

        static void DefineInternalReturnLoopAllocation(TypeBuilder type, ConstructorInfo ctor)
        {
            var method = type.DefineMethod(
                "InternalReturnLoopAllocation",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(bool)]);
            var il = method.GetILGenerator();
            var header = il.DefineLabel();
            var ret = il.DefineLabel();
            var value = il.DeclareLocal(typeof(object));
            il.Emit(OpCodes.Ldc_I4, 1);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stloc, value);
            il.MarkLabel(header);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue_S, ret);
            il.Emit(OpCodes.Br_S, header);
            il.MarkLabel(ret);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ret);
        }
    }

    static (string Path, string Directory) BuildSpanToArrayLocalFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnet-inspect-span-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "SpanToArrayLocalFixture.dll");

        var assemblyName = new AssemblyName("SpanToArrayLocalFixture");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("SpanToArrayLocalFixture", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var spanType = typeof(ReadOnlySpan<int>);
        var toArray = spanType.GetMethod(nameof(ReadOnlySpan<int>.ToArray), Type.EmptyTypes)!;

        DefineLocalRead(type, spanType, toArray);
        DefineLocalReturn(type, spanType, toArray);

        type.CreateType();
        assembly.Save(path);
        return (path, directory);

        static void DefineLocalRead(TypeBuilder type, Type spanType, MethodInfo toArray)
        {
            var method = type.DefineMethod(
                "LocalRead",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                [spanType]);
            var il = method.GetILGenerator();
            il.DeclareLocal(typeof(int[]));
            il.Emit(OpCodes.Ldarga_S, (byte)0);
            il.Emit(OpCodes.Call, toArray);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ret);
        }

        static void DefineLocalReturn(TypeBuilder type, Type spanType, MethodInfo toArray)
        {
            var method = type.DefineMethod(
                "LocalReturn",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int[]),
                [spanType]);
            var il = method.GetILGenerator();
            il.DeclareLocal(typeof(int[]));
            il.Emit(OpCodes.Ldarga_S, (byte)0);
            il.Emit(OpCodes.Call, toArray);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ret);
        }
    }

    static IEnumerable<string> DelegateShapes(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape is "delegate-allocation" or "capturing-delegate" or "instance-method-group-delegate" or "cache-lookup-factory-delegate")
            .Select(o => o.Shape);

    static System.Collections.Generic.List<OptimizationOpportunity> BoxRows(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape == "box-value-type")
            .ToList();

    static System.Collections.Generic.List<OptimizationOpportunity> GenericObjectBoxRows(
        LibraryBodyIndex index,
        string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName
                && o.Shape == "generic-parameter-object-box")
            .ToList();

    static System.Collections.Generic.List<OptimizationOpportunity> HotspotRows(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape == "allocation-hotspot")
            .ToList();

    static System.Collections.Generic.List<OptimizationOpportunity> StringBuildRows(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape == "string-build-in-loop")
            .ToList();

    static System.Collections.Generic.List<OptimizationOpportunity> EnumeratorRows(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape == "enumerator-allocation")
            .ToList();

    static int AllocationsOf(LibraryBodyIndex index, string methodName)
    {
        int token = index.Methods.First(method => method.Name == methodName).MetadataToken;
        return index.GetMethodSignals().GetValueOrDefault(token, MethodSignals.None).Allocations;
    }

    static DirectCall EnumerableToArrayCall(string calleeAssembly, int callerToken)
    {
        var enumerable = TypeRef.Definition(calleeAssembly, "System.Linq", "Enumerable");
        var callee = new MemberRef(enumerable, "ToArray", [], TypeRef.Unsupported("ret"), MemberKind.Method);
        return FrameworkCall(callee, callerToken);
    }

    static DirectCall ExpressionConstantCall(string calleeAssembly, int callerToken)
    {
        var expression = TypeRef.Definition(calleeAssembly, "System.Linq.Expressions", "Expression");
        var callee = new MemberRef(expression, "Constant", [], TypeRef.Unsupported("ret"), MemberKind.Method);
        return FrameworkCall(callee, callerToken);
    }

    static DirectCall FrameworkCall(MemberRef callee, int callerToken)
    {
        var callerType = TypeRef.Definition("Caller.Assembly", "Caller.Ns", "CallerType");
        var caller = new MethodIdentity(
            "Caller.Assembly",
            Guid.Empty,
            callerType,
            "Caller",
            [],
            TypeRef.CoreLib("System", "Void"),
            callerToken,
            IsStatic: true);
        return new DirectCall(caller, callee, ILOffset: 0, OperandToken: 0, CalleeDefinitionToken: 0, CallKind.Call);
    }

}
