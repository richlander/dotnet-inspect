using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class MemberIdentityFactsTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef s_range = TypeRef.CoreLib("System", "Range");
    static readonly TypeRef s_intArray = TypeRef.SzArray(s_int);

    [Fact]
    public void IsCoreLibraryType_RequiresCoreLibraryIdentity()
    {
        Assert.True(MemberIdentityFacts.IsCoreLibraryType(TypeRef.CoreLib("System", "Range"), "System", "Range"));
        Assert.False(MemberIdentityFacts.IsCoreLibraryType(
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

        Assert.True(MemberIdentityFacts.IsCoreLibraryType(list, "System.Collections.Generic", "List`1"));
    }

    [Fact]
    public void IsRuntimeHelpersGetSubArray_RequiresExactBclStaticSignature()
    {
        Assert.True(MemberIdentityFacts.IsRuntimeHelpersGetSubArray(GetSubArray(TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "RuntimeHelpers"))));

        Assert.False(MemberIdentityFacts.IsRuntimeHelpersGetSubArray(GetSubArray(TypeRef.Definition(
            "UserAssembly",
            "System.Runtime.CompilerServices",
            "RuntimeHelpers"))));

        Assert.False(MemberIdentityFacts.IsRuntimeHelpersGetSubArray(new Call(
            GetSubArrayMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "RuntimeHelpers")) with
            {
                ParameterTypes = [s_intArray, s_object],
            },
            isVirtual: false,
            [new LoadArgument(0, "a", s_intArray), new LoadArgument(1, "range", s_range)])));

        Assert.False(MemberIdentityFacts.IsRuntimeHelpersGetSubArray(new Call(
            GetSubArrayMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "RuntimeHelpers")),
            isVirtual: false,
            [new LoadArgument(0, "a", s_intArray)])));
    }

    [Fact]
    public void IsAsyncHelpersAwait_RequiresExactBclStaticSingleArgument()
    {
        Assert.True(MemberIdentityFacts.IsAsyncHelpersAwait(Await(TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "AsyncHelpers"))));

        Assert.False(MemberIdentityFacts.IsAsyncHelpersAwait(Await(TypeRef.Definition(
            "UserAssembly",
            "System.Runtime.CompilerServices",
            "AsyncHelpers"))));

        Assert.False(MemberIdentityFacts.IsAsyncHelpersAwait(new Call(
            AwaitMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "AsyncHelpers")),
            isVirtual: true,
            [new LoadArgument(0, "x", s_int)])));

        Assert.False(MemberIdentityFacts.IsAsyncHelpersAwait(new Call(
            AwaitMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "AsyncHelpers")),
            isVirtual: false,
            [new LoadArgument(0, "x", s_int), new LoadArgument(1, "y", s_int)])));

        Assert.False(MemberIdentityFacts.IsAsyncHelpersAwait(new Call(
            AwaitMethod(TypeRef.CoreLib("System.Runtime.CompilerServices", "AsyncHelpers")) with
            {
                ParameterTypes = [s_int, s_int],
            },
            isVirtual: false,
            [new LoadArgument(0, "x", s_int)])));
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
}
