using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class MemberIdentityTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef s_range = TypeRef.CoreLib("System", "Range");
    static readonly TypeRef s_runtimeFieldHandle = TypeRef.CoreLib("System", "RuntimeFieldHandle");
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_refBool = TypeRef.ByRef(TypeRef.CoreLib("System", "Boolean"));
    static readonly TypeRef s_intArray = TypeRef.SzArray(s_int);
    static readonly TypeRef s_readOnlySpanInt = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [s_int]);

    [Fact]
    public void IsCoreLibraryType_RequiresCoreLibraryIdentity()
    {
        Assert.True(MemberIdentity.IsCoreLibraryType(TypeRef.CoreLib("System", "Range"), "System", "Range"));
        Assert.False(MemberIdentity.IsCoreLibraryType(
            TypeRef.Definition("UserAssembly", "System", "Range"),
            "System",
            "Range"));
    }

    [Fact]
    public void IsCoreLibraryType_UnwrapsGenericInstanceDefinition()
    {
        var list = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "List`1"),
            [s_int]);

        Assert.True(MemberIdentity.IsCoreLibraryType(list, "System.Collections.Generic", "List`1"));
    }

    [Fact]
    public void IsRuntimeHelpersGetSubArray_RequiresExactBclStaticSignature()
    {
        Assert.True(MemberIdentity.IsRuntimeHelpersGetSubArray(GetSubArray(TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "RuntimeHelpers"))));

        Assert.False(MemberIdentity.IsRuntimeHelpersGetSubArray(GetSubArray(TypeRef.Definition(
            "UserAssembly",
            "System.Runtime.CompilerServices",
            "RuntimeHelpers"))));

        Assert.False(MemberIdentity.IsRuntimeHelpersGetSubArray(new Call(
            GetSubArrayMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "RuntimeHelpers")) with
            {
                ParameterTypes = [s_intArray, s_object],
            },
            isVirtual: false,
            [new LoadArgument(0, "a", s_intArray), new LoadArgument(1, "range", s_range)])));

        Assert.False(MemberIdentity.IsRuntimeHelpersGetSubArray(new Call(
            GetSubArrayMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "RuntimeHelpers")),
            isVirtual: false,
            [new LoadArgument(0, "a", s_intArray)])));
    }

    [Fact]
    public void IsAsyncHelpersAwait_RequiresExactBclStaticSingleArgument()
    {
        Assert.True(MemberIdentity.IsAsyncHelpersAwait(Await(TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "AsyncHelpers"))));

        Assert.False(MemberIdentity.IsAsyncHelpersAwait(Await(TypeRef.Definition(
            "UserAssembly",
            "System.Runtime.CompilerServices",
            "AsyncHelpers"))));

        Assert.False(MemberIdentity.IsAsyncHelpersAwait(new Call(
            AwaitMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "AsyncHelpers")),
            isVirtual: true,
            [new LoadArgument(0, "x", s_int)])));

        Assert.False(MemberIdentity.IsAsyncHelpersAwait(new Call(
            AwaitMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "AsyncHelpers")),
            isVirtual: false,
            [new LoadArgument(0, "x", s_int), new LoadArgument(1, "y", s_int)])));

        Assert.False(MemberIdentity.IsAsyncHelpersAwait(new Call(
            AwaitMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "AsyncHelpers")) with
            {
                ParameterTypes = [s_int, s_int],
            },
            isVirtual: false,
            [new LoadArgument(0, "x", s_int)])));
    }

    [Fact]
    public void IsRuntimeHelpersCreateSpan_RequiresExactBclStaticSignature()
    {
        Assert.True(MemberIdentity.IsRuntimeHelpersCreateSpan(CreateSpan(TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "RuntimeHelpers"))));

        Assert.False(MemberIdentity.IsRuntimeHelpersCreateSpan(CreateSpan(TypeRef.Definition(
            "UserAssembly",
            "System.Runtime.CompilerServices",
            "RuntimeHelpers"))));

        Assert.False(MemberIdentity.IsRuntimeHelpersCreateSpan(new Call(
            CreateSpanMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "RuntimeHelpers")) with
            {
                ParameterTypes = [s_object],
            },
            isVirtual: false,
            [new LoadArgument(0, "field", s_runtimeFieldHandle)])));

        Assert.False(MemberIdentity.IsRuntimeHelpersCreateSpan(new Call(
            CreateSpanMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "RuntimeHelpers")) with
            {
                ReturnType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Span`1"), [s_int]),
            },
            isVirtual: false,
            [new LoadArgument(0, "field", s_runtimeFieldHandle)])));

        Assert.False(MemberIdentity.IsRuntimeHelpersCreateSpan(new Call(
            CreateSpanMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "RuntimeHelpers")),
            isVirtual: false,
            [])));
    }

    [Fact]
    public void IsMonitorEnterExit_RequireExactBclStaticSignature()
    {
        var monitor = TypeRef.CoreLib("System.Threading", "Monitor");
        Assert.True(MemberIdentity.IsMonitorEnter(MonitorEnter(monitor)));
        Assert.True(MemberIdentity.IsMonitorExit(MonitorExit(monitor)));
        Assert.True(MemberIdentity.IsMonitorEnter(MonitorEnter(TypeRef.Definition(
            "System.Threading",
            "System.Threading",
            "Monitor"))));

        var userMonitor = TypeRef.Definition("UserAssembly", "System.Threading", "Monitor");
        Assert.False(MemberIdentity.IsMonitorEnter(MonitorEnter(userMonitor)));
        Assert.False(MemberIdentity.IsMonitorExit(MonitorExit(userMonitor)));

        Assert.False(MemberIdentity.IsMonitorEnter(new Call(
            MonitorEnterMethod(monitor) with { ReturnType = s_object },
            isVirtual: false,
            [new LoadArgument(0, "obj", s_object), new LoadArgumentAddress(1, "taken", TypeRef.CoreLib("System", "Boolean"))])));

        Assert.False(MemberIdentity.IsMonitorEnter(new Call(
            MonitorEnterMethod(monitor) with { ParameterTypes = [s_object, TypeRef.CoreLib("System", "Boolean")] },
            isVirtual: false,
            [new LoadArgument(0, "obj", s_object), new LoadArgumentAddress(1, "taken", TypeRef.CoreLib("System", "Boolean"))])));

        Assert.False(MemberIdentity.IsMonitorExit(new Call(
            MonitorExitMethod(monitor),
            isVirtual: true,
            [new LoadArgument(0, "obj", s_object)])));
    }

    static MethodRef GetSubArrayMethod(TypeRef declaringType)
        => new(declaringType, "GetSubArray", s_intArray, [s_intArray, s_range], HasThis: false);

    static Call GetSubArray(TypeRef declaringType)
        => new(
            GetSubArrayMethod(declaringType),
            isVirtual: false,
            [new LoadArgument(0, "a", s_intArray), new LoadArgument(1, "range", s_range)]);

    static MethodRef AwaitMethod(TypeRef declaringType)
        => new(declaringType, "Await", s_int, [s_int], HasThis: false);

    static Call Await(TypeRef declaringType)
        => new(AwaitMethod(declaringType), isVirtual: false, [new LoadArgument(0, "x", s_int)]);

    static MethodRef CreateSpanMethod(TypeRef declaringType)
        => new(declaringType, "CreateSpan", s_readOnlySpanInt, [s_runtimeFieldHandle], HasThis: false)
        {
            TypeArguments = [s_int],
        };

    static Call CreateSpan(TypeRef declaringType)
        => new(
            CreateSpanMethod(declaringType),
            isVirtual: false,
            [new LoadArgument(0, "field", s_runtimeFieldHandle)]);

    static MethodRef MonitorEnterMethod(TypeRef declaringType)
        => new(declaringType, "Enter", s_void, [s_object, s_refBool], HasThis: false);

    static Call MonitorEnter(TypeRef declaringType)
        => new(
            MonitorEnterMethod(declaringType),
            isVirtual: false,
            [new LoadArgument(0, "obj", s_object), new LoadArgumentAddress(1, "taken", TypeRef.CoreLib("System", "Boolean"))]);

    static MethodRef MonitorExitMethod(TypeRef declaringType)
        => new(declaringType, "Exit", s_void, [s_object], HasThis: false);

    static Call MonitorExit(TypeRef declaringType)
        => new(MonitorExitMethod(declaringType), isVirtual: false, [new LoadArgument(0, "obj", s_object)]);
}
