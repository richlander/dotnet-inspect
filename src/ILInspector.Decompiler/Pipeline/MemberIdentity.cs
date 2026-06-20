namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Thin, SRM-native identity facts over the decompiler IR. These helpers
/// centralize exact BCL member/type checks so raising passes do not silently
/// drift back to namespace/name-only matching.
/// </summary>
public static class MemberIdentity
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef s_range = TypeRef.CoreLib("System", "Range");
    static readonly TypeRef s_runtimeFieldHandle = TypeRef.CoreLib("System", "RuntimeFieldHandle");
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_refBool = TypeRef.ByRef(TypeRef.CoreLib("System", "Boolean"));

    public static bool IsCoreLibraryType(TypeRef? type, string ns, string name)
        => NamedDefinition(type) is
        {
            Kind: TypeRefKind.Definition,
            Assembly: TypeRef.CoreLibrary,
            Namespace: var typeNamespace,
            Name: var typeName,
        }
        && typeNamespace == ns
        && typeName == name;

    public static bool IsStaticCoreLibraryMethod(MethodRef method, string typeNamespace, string typeName, string methodName)
        => !method.HasThis
            && method.Name == methodName
            && IsCoreLibraryType(method.DeclaringType, typeNamespace, typeName);

    public static bool IsMonitorEnter(Call call)
        => IsMonitorMethod(call, "Enter")
            && call.Arguments.Count == 2
            && call.Callee.ParameterTypes is [var obj, var taken]
            && obj.Equals(s_object)
            && taken.Equals(s_refBool);

    public static bool IsMonitorExit(Call call)
        => IsMonitorMethod(call, "Exit")
            && call.Arguments.Count == 1
            && call.Callee.ParameterTypes is [var obj]
            && obj.Equals(s_object);

    public static bool IsRuntimeHelpersGetSubArray(Call call)
    {
        if (call.IsVirtual
            || !IsStaticCoreLibraryMethod(
                call.Callee,
                "System.Runtime.CompilerServices",
                "RuntimeHelpers",
                "GetSubArray")
            || call.Arguments.Count != 2
            || call.Callee.ParameterTypes is not [var arrayParameter, var rangeParameter])
        {
            return false;
        }

        return call.Callee.ReturnType is { Kind: TypeRefKind.SzArray } arrayReturn
            && arrayParameter.Equals(arrayReturn)
            && rangeParameter.Equals(s_range);
    }

    public static bool IsAsyncHelpersAwait(Call call)
        => !call.IsVirtual
            && IsStaticCoreLibraryMethod(
                call.Callee,
                "System.Runtime.CompilerServices",
                "AsyncHelpers",
                "Await")
            && call.Callee.ParameterTypes.Length == 1
            && call.Arguments.Count == 1;

    public static bool IsDefaultInterpolatedStringHandlerConstructor(NewObject newObject)
        => newObject.Constructor is
        {
            Name: ".ctor",
            HasThis: true,
            ParameterTypes: [var literalLength, var formattedCount],
        }
        && IsDefaultInterpolatedStringHandlerType(newObject.Constructor.DeclaringType)
        && literalLength.Equals(s_int)
        && formattedCount.Equals(s_int)
        && newObject.Arguments.Count == 2;

    public static bool IsDefaultInterpolatedStringHandlerAppendLiteral(Call call)
        => IsDefaultInterpolatedStringHandlerInstanceMethod(call, "AppendLiteral")
            && call.Callee.ReturnType.Equals(s_void)
            && call.Callee.ParameterTypes is [var literal]
            && literal.Equals(s_string)
            && call.Arguments.Count == 2;

    public static bool IsDefaultInterpolatedStringHandlerAppendFormatted(Call call)
        => IsDefaultInterpolatedStringHandlerInstanceMethod(call, "AppendFormatted")
            && call.Callee.ReturnType.Equals(s_void)
            && call.Callee.ParameterTypes.Length == 1
            && call.Arguments.Count == 2;

    public static bool IsDefaultInterpolatedStringHandlerToStringAndClear(Call call)
        => IsDefaultInterpolatedStringHandlerInstanceMethod(call, "ToStringAndClear")
            && call.Callee.ReturnType.Equals(s_string)
            && call.Callee.ParameterTypes.IsEmpty
            && call.Arguments.Count == 1;

    public static bool IsRuntimeHelpersCreateSpan(Call call)
    {
        if (call.IsVirtual
            || !IsStaticCoreLibraryMethod(
                call.Callee,
                "System.Runtime.CompilerServices",
                "RuntimeHelpers",
                "CreateSpan")
            || call.Arguments.Count != 1
            || call.Callee.TypeArguments is not [var element]
            || call.Callee.ParameterTypes is not [var parameter]
            || !parameter.Equals(s_runtimeFieldHandle))
        {
            return false;
        }

        return call.Callee.ReturnType is
        {
            Kind: TypeRefKind.GenericInstance,
            ElementType: { } definition,
            TypeArguments: [var returnedElement],
        }
        && IsCoreLibraryType(definition, "System", "ReadOnlySpan`1")
        && returnedElement.Equals(element);
    }

    static TypeRef? NamedDefinition(TypeRef? type)
        => type is { Kind: TypeRefKind.GenericInstance } ? type.ElementType : type;

    static bool IsMonitorMethod(Call call, string methodName)
        => !call.IsVirtual
            && call.Callee is
            {
                HasThis: false,
                Name: var name,
                ReturnType: var returnType,
                TypeArguments.IsEmpty: true,
            }
            && name == methodName
            && returnType.Equals(s_void)
            && IsCoreLibraryOrFacadeType(call.Callee.DeclaringType, "System.Threading", "Monitor", "System.Threading");

    static bool IsCoreLibraryOrFacadeType(TypeRef? type, string ns, string name, string facadeAssembly)
        => NamedDefinition(type) is
        {
            Kind: TypeRefKind.Definition,
            Assembly: var assembly,
            Namespace: var typeNamespace,
            Name: var typeName,
        }
        && (assembly == TypeRef.CoreLibrary || assembly == facadeAssembly)
        && typeNamespace == ns
        && typeName == name;

    static bool IsDefaultInterpolatedStringHandlerInstanceMethod(Call call, string methodName)
        => !call.IsVirtual
            && call.Callee.Name == methodName
            && call.Callee.HasThis
            && IsDefaultInterpolatedStringHandlerType(call.Callee.DeclaringType);

    static bool IsDefaultInterpolatedStringHandlerType(TypeRef type)
        => IsCoreLibraryOrFacadeType(
            type,
            "System.Runtime.CompilerServices",
            "DefaultInterpolatedStringHandler",
            "System.Runtime");
}
