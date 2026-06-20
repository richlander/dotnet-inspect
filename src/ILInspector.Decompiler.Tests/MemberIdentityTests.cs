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
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");
    static readonly TypeRef s_handler = TypeRef.CoreLib("System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler");
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

    [Fact]
    public void InterpolatedStringHandlerMembers_RequireExactBclInstanceSignatures()
    {
        Assert.True(MemberIdentity.IsDefaultInterpolatedStringHandlerConstructor(HandlerCtor(s_handler)));
        Assert.True(MemberIdentity.IsDefaultInterpolatedStringHandlerAppendLiteral(AppendLiteral(s_handler)));
        Assert.True(MemberIdentity.IsDefaultInterpolatedStringHandlerAppendFormatted(AppendFormatted(s_handler, s_int)));
        Assert.True(MemberIdentity.IsDefaultInterpolatedStringHandlerToStringAndClear(ToStringAndClear(s_handler)));

        var userHandler = TypeRef.Definition(
            "UserAssembly",
            "System.Runtime.CompilerServices",
            "DefaultInterpolatedStringHandler");
        Assert.False(MemberIdentity.IsDefaultInterpolatedStringHandlerConstructor(HandlerCtor(userHandler)));
        Assert.False(MemberIdentity.IsDefaultInterpolatedStringHandlerAppendLiteral(AppendLiteral(userHandler)));
        Assert.False(MemberIdentity.IsDefaultInterpolatedStringHandlerAppendFormatted(AppendFormatted(userHandler, s_int)));
        Assert.False(MemberIdentity.IsDefaultInterpolatedStringHandlerToStringAndClear(ToStringAndClear(userHandler)));

        Assert.False(MemberIdentity.IsDefaultInterpolatedStringHandlerAppendLiteral(new Call(
            AppendLiteralMethod(s_handler) with { ParameterTypes = [s_object] },
            isVirtual: false,
            [new LoadLocalAddress(0, s_handler), new LoadArgument(0, "literal", s_string)])));

        Assert.False(MemberIdentity.IsDefaultInterpolatedStringHandlerToStringAndClear(new Call(
            ToStringAndClearMethod(s_handler) with { ReturnType = s_object },
            isVirtual: false,
            [new LoadLocalAddress(0, s_handler)])));
    }

    [Fact]
    public void IsIDisposableDispose_RequiresExactBclVirtualSignature()
    {
        var disposable = TypeRef.CoreLib("System", "IDisposable");
        Assert.True(MemberIdentity.IsIDisposableDispose(Dispose(disposable)));
        Assert.True(MemberIdentity.IsIDisposableDispose(Dispose(TypeRef.Definition(
            "System.Runtime",
            "System",
            "IDisposable"))));

        var userDisposable = TypeRef.Definition("UserAssembly", "System", "IDisposable");
        Assert.False(MemberIdentity.IsIDisposableDispose(Dispose(userDisposable)));

        Assert.False(MemberIdentity.IsIDisposableDispose(new Call(
            DisposeMethod(disposable) with { ReturnType = s_object },
            isVirtual: true,
            [new LoadLocal(0, disposable)])));

        Assert.False(MemberIdentity.IsIDisposableDispose(new Call(
            DisposeMethod(disposable) with { ParameterTypes = [s_object] },
            isVirtual: true,
            [new LoadLocal(0, disposable)])));

        Assert.False(MemberIdentity.IsIDisposableDispose(new Call(
            DisposeMethod(disposable),
            isVirtual: false,
            [new LoadLocal(0, disposable)])));
    }

    [Fact]
    public void IsValueTupleType_RequiresExactBclGenericDefinitionAndMatchingArity()
    {
        var tuple2 = ValueTupleType(TypeRef.CoreLib("System", "ValueTuple`2"), s_int, s_string);
        Assert.True(MemberIdentity.IsValueTupleType(tuple2, out var arity2));
        Assert.Equal(2, arity2);
        Assert.True(MemberIdentity.IsSupportedValueTupleType(tuple2, out _));

        Assert.True(MemberIdentity.IsValueTupleType(
            ValueTupleType(TypeRef.Definition("System.Runtime", "System", "ValueTuple`2"), s_int, s_string),
            out _));

        Assert.False(MemberIdentity.IsValueTupleType(
            ValueTupleType(TypeRef.Definition("UserAssembly", "System", "ValueTuple`2"), s_int, s_string),
            out _));

        Assert.False(MemberIdentity.IsValueTupleType(
            ValueTupleType(TypeRef.CoreLib("System", "ValueTuple`2"), s_int),
            out _));

        var tuple1 = ValueTupleType(TypeRef.CoreLib("System", "ValueTuple`1"), s_int);
        Assert.True(MemberIdentity.IsValueTupleType(tuple1, out var arity1));
        Assert.Equal(1, arity1);
        Assert.False(MemberIdentity.IsSupportedValueTupleType(tuple1, out _));

        var tuple8 = ValueTupleType(TypeRef.CoreLib("System", "ValueTuple`8"),
            s_int, s_int, s_int, s_int, s_int, s_int, s_int, s_int);
        Assert.True(MemberIdentity.IsValueTupleType(tuple8, out var arity8));
        Assert.Equal(8, arity8);
        Assert.False(MemberIdentity.IsSupportedValueTupleType(tuple8, out _));
    }

    [Fact]
    public void IsValueTupleConstructor_RequiresExactBclSupportedArityAndSignature()
    {
        var tuple2 = ValueTupleType(TypeRef.CoreLib("System", "ValueTuple`2"), s_int, s_string);
        Assert.True(MemberIdentity.IsValueTupleConstructor(ValueTupleNew(tuple2), out var arity));
        Assert.Equal(2, arity);

        Assert.False(MemberIdentity.IsValueTupleConstructor(ValueTupleNew(
            ValueTupleType(TypeRef.Definition("UserAssembly", "System", "ValueTuple`2"), s_int, s_string)), out _));

        Assert.False(MemberIdentity.IsValueTupleConstructor(ValueTupleNew(
            ValueTupleType(TypeRef.CoreLib("System", "ValueTuple`1"), s_int)), out _));

        var tuple8 = ValueTupleType(TypeRef.CoreLib("System", "ValueTuple`8"),
            s_int, s_int, s_int, s_int, s_int, s_int, s_int, s_int);
        Assert.False(MemberIdentity.IsValueTupleConstructor(ValueTupleNew(tuple8), out _));

        Assert.False(MemberIdentity.IsValueTupleConstructor(ValueTupleNew(tuple2, [s_int, s_int]), out _));
        Assert.False(MemberIdentity.IsValueTupleConstructor(ValueTupleNew(tuple2, argumentCount: 1), out _));
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

    static MethodRef HandlerCtorMethod(TypeRef declaringType)
        => new(declaringType, ".ctor", s_void, [s_int, s_int], HasThis: true);

    static NewObject HandlerCtor(TypeRef declaringType)
        => new(HandlerCtorMethod(declaringType), [new Constant(0, s_int), new Constant(0, s_int)]);

    static MethodRef AppendLiteralMethod(TypeRef declaringType)
        => new(declaringType, "AppendLiteral", s_void, [s_string], HasThis: true);

    static Call AppendLiteral(TypeRef declaringType)
        => new(
            AppendLiteralMethod(declaringType),
            isVirtual: false,
            [new LoadLocalAddress(0, declaringType), new LoadArgument(0, "literal", s_string)]);

    static MethodRef AppendFormattedMethod(TypeRef declaringType, TypeRef valueType)
        => new(declaringType, "AppendFormatted", s_void, [valueType], HasThis: true)
        {
            TypeArguments = [valueType],
        };

    static Call AppendFormatted(TypeRef declaringType, TypeRef valueType)
        => new(
            AppendFormattedMethod(declaringType, valueType),
            isVirtual: false,
            [new LoadLocalAddress(0, declaringType), new LoadArgument(0, "value", valueType)]);

    static MethodRef ToStringAndClearMethod(TypeRef declaringType)
        => new(declaringType, "ToStringAndClear", s_string, [], HasThis: true);

    static Call ToStringAndClear(TypeRef declaringType)
        => new(ToStringAndClearMethod(declaringType), isVirtual: false, [new LoadLocalAddress(0, declaringType)]);

    static MethodRef DisposeMethod(TypeRef declaringType)
        => new(declaringType, "Dispose", s_void, [], HasThis: true);

    static Call Dispose(TypeRef declaringType)
        => new(DisposeMethod(declaringType), isVirtual: true, [new LoadLocal(0, declaringType)]);

    static TypeRef ValueTupleType(TypeRef definition, params TypeRef[] arguments)
        => TypeRef.GenericInstance(definition, [.. arguments]);

    static NewObject ValueTupleNew(TypeRef tupleType, TypeRef[]? parameterTypes = null, int? argumentCount = null)
    {
        parameterTypes ??= [.. tupleType.TypeArguments];
        int count = argumentCount ?? parameterTypes.Length;
        var arguments = Enumerable.Range(0, count)
            .Select(index => (IrExpression)new Constant(index, parameterTypes[Math.Min(index, parameterTypes.Length - 1)]))
            .ToArray();
        return new NewObject(
            new MethodRef(tupleType, ".ctor", s_void, [.. parameterTypes], HasThis: false),
            arguments);
    }
}
