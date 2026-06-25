namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Thin, SRM-native identity facts over the decompiler IR. These helpers
/// centralize exact BCL member/type checks so raising passes do not silently
/// drift back to namespace/name-only matching.
/// </summary>
public static class MemberIdentity
{
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_char = TypeRef.CoreLib("System", "Char");
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_exception = TypeRef.CoreLib("System", "Exception");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef s_range = TypeRef.CoreLib("System", "Range");
    static readonly TypeRef s_runtimeFieldHandle = TypeRef.CoreLib("System", "RuntimeFieldHandle");
    static readonly TypeRef s_runtimeTypeHandle = TypeRef.CoreLib("System", "RuntimeTypeHandle");
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");
    static readonly TypeRef s_type = TypeRef.CoreLib("System", "Type");
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

    /// <summary>
    /// Exact identity for the core delegate families the compiler commonly emits
    /// without requiring cross-assembly metadata to be loaded. Other delegate
    /// types still rely on the importer/resolver's MulticastDelegate base fact.
    /// </summary>
    public static bool IsKnownCoreLibraryDelegateType(TypeRef? type)
    {
        if (NamedDefinition(type) is not
            {
                Kind: TypeRefKind.Definition,
                Assembly: TypeRef.CoreLibrary,
                Namespace: "System",
                Name: var name,
            })
        {
            return false;
        }

        return name == "Action"
            || name.StartsWith("Action`", StringComparison.Ordinal)
            || name.StartsWith("Func`", StringComparison.Ordinal);
    }

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

    public static bool IsTypeGetTypeFromHandle(Call call)
        => !call.IsVirtual
            && IsStaticCoreLibraryMethod(call.Callee, "System", "Type", "GetTypeFromHandle")
            && call.Arguments.Count == 1
            && call.Callee.ParameterTypes is [var handle]
            && handle.Equals(s_runtimeTypeHandle)
            && call.Callee.ReturnType.Equals(s_type);

    public static bool IsStringSubstring(Call call)
        => call.IsVirtual
            && call.Callee is
            {
                HasThis: true,
                Name: "Substring",
                TypeArguments.IsEmpty: true,
                ReturnType: var returnType,
            }
            && returnType.Equals(s_string)
            && IsCoreLibraryType(call.Callee.DeclaringType, "System", "String")
            && call.Callee.ParameterTypes.All(p => p.Equals(s_int))
            && call.Callee.ParameterTypes.Length is 1 or 2
            && call.Arguments.Count == call.Callee.ParameterTypes.Length + 1;

    public static bool IsSpanSlice(Call call)
    {
        if (call.IsVirtual
            || call.Callee is not
            {
                HasThis: true,
                Name: "Slice",
                TypeArguments.IsEmpty: true,
                DeclaringType: var declaringType,
                ReturnType: var returnType,
            }
            || !returnType.Equals(declaringType)
            || !IsSpanLikeType(declaringType)
            || call.Callee.ParameterTypes.Length is not (1 or 2)
            || call.Arguments.Count != call.Callee.ParameterTypes.Length + 1)
        {
            return false;
        }

        return call.Callee.ParameterTypes.All(p => p.Equals(s_int));
    }

    public static bool IsSpanIndexerGetter(LoadProperty property)
        => property is
        {
            HasInstance: true,
            IndexArguments.Count: 1,
            Accessor:
            {
                HasThis: true,
                Name: "get_Item",
                TypeArguments.IsEmpty: true,
                DeclaringType: var declaringType,
                ParameterTypes: [var index],
                ReturnType: { Kind: TypeRefKind.ByRef, ElementType: { } element },
            },
        }
        && IsSpanLikeType(declaringType)
        && index.Equals(s_int)
        && declaringType.TypeArguments is [var spanElement]
        && element.Equals(spanElement);

    public static bool IsSpanLengthGetter(LoadProperty property)
        => property is
        {
            HasInstance: true,
            PropertyName: "Length",
            Accessor:
            {
                HasThis: true,
                Name: "get_Length",
                TypeArguments.IsEmpty: true,
                DeclaringType: var declaringType,
                ParameterTypes.IsEmpty: true,
                ReturnType: var returnType,
            },
            IndexArguments.Count: 0,
        }
        && IsSpanLikeType(declaringType)
        && returnType.Equals(s_int);

    public static bool IsReadOnlySpanArrayConstructor(NewObject newObject, out TypeRef element)
    {
        element = TypeRef.Unsupported("unmatched ReadOnlySpan constructor");
        if (newObject.Constructor is not
            {
                Name: ".ctor",
                HasThis: true,
                TypeArguments.IsEmpty: true,
                DeclaringType: var declaringType,
                ParameterTypes: [var parameter],
            }
            || newObject.Arguments.Count != 1
            || !IsCoreLibraryType(declaringType, "System", "ReadOnlySpan`1")
            || declaringType.TypeArguments is not [var spanElement]
            || parameter is not { Kind: TypeRefKind.SzArray, ElementType: { } arrayElement }
            || !arrayElement.Equals(spanElement))
        {
            return false;
        }

        element = spanElement;
        return true;
    }

    public static bool IsSpanArrayConstructor(NewObject newObject, TypeRef element)
        => newObject.Constructor is
        {
            Name: ".ctor",
            HasThis: true,
            TypeArguments.IsEmpty: true,
            DeclaringType: var declaringType,
            ParameterTypes: [var parameter],
        }
        && newObject.Arguments.Count == 1
        && IsCoreLibraryType(declaringType, "System", "Span`1")
        && declaringType.TypeArguments is [var spanElement]
        && spanElement.Equals(element)
        && parameter is { Kind: TypeRefKind.SzArray, ElementType: { } arrayElement }
        && arrayElement.Equals(element);

    public static bool IsReadOnlySpanCopyTo(Call call, TypeRef element)
        => !call.IsVirtual
            && call.Callee is
            {
                HasThis: true,
                Name: "CopyTo",
                TypeArguments.IsEmpty: true,
                DeclaringType: var declaringType,
                ParameterTypes: [var destination],
                ReturnType: var returnType,
            }
            && call.Arguments.Count == 2
            && returnType.Equals(s_void)
            && IsCoreLibraryType(declaringType, "System", "ReadOnlySpan`1")
            && declaringType.TypeArguments is [var sourceElement]
            && sourceElement.Equals(element)
            && destination is
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType: { } destinationDefinition,
                TypeArguments: [var destinationElement],
            }
            && IsCoreLibraryType(destinationDefinition, "System", "Span`1")
            && destinationElement.Equals(element);

    public static bool IsAsyncHelpersAwait(Call call)
        => !call.IsVirtual
            && IsStaticCoreLibraryMethod(
                call.Callee,
                "System.Runtime.CompilerServices",
                "AsyncHelpers",
                "Await")
            && call.Callee.ParameterTypes.Length == 1
            && call.Arguments.Count == 1;

    public static bool IsFileStreamHelpersIsIoRelatedException(Call call)
        => !call.IsVirtual
            && IsStaticCoreLibraryMethod(
                call.Callee,
                "System.IO.Strategies",
                "FileStreamHelpers",
                "IsIoRelatedException")
            && call.Callee.ReturnType.Equals(s_bool)
            && call.Callee.ParameterTypes is [var exception]
            && exception.Equals(s_exception)
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
    {
        if (!IsDefaultInterpolatedStringHandlerInstanceMethod(call, "AppendFormatted")
            || !call.Callee.ReturnType.Equals(s_void)
            || call.Arguments.Count != call.Callee.ParameterTypes.Length + 1)
        {
            return false;
        }

        // value; value + alignment (int) | format (string); value + alignment + format.
        // The alignment/format arguments are compile-time constants in every C#
        // interpolation, so a non-constant in those slots is not this lowering. A
        // format that contains a brace cannot round-trip through `{value:format}`
        // syntax, so leave those (hand-written handler) calls lowered.
        return call.Callee.ParameterTypes switch
        {
            [_] => true,
            [_, var second] when second.Equals(s_int) => call.Arguments[2] is Constant { Value: int },
            [_, var second] when second.Equals(s_string) => call.Arguments[2] is Constant { Value: string format } && IsBraceFreeFormat(format),
            [_, var alignment, var format] => alignment.Equals(s_int) && format.Equals(s_string)
                && call.Arguments[2] is Constant { Value: int } && call.Arguments[3] is Constant { Value: string formatText } && IsBraceFreeFormat(formatText),
            _ => false,
        };
    }

    static bool IsBraceFreeFormat(string format)
        => !format.Contains('{') && !format.Contains('}');

    /// <summary>
    /// The alignment/format clause carried by an <c>AppendFormatted</c> call's
    /// extra constant arguments, or <see langword="null"/> for the value-only
    /// overload. The caller must have already matched
    /// <see cref="IsDefaultInterpolatedStringHandlerAppendFormatted"/>.
    /// </summary>
    public static InterpolationFormat? GetAppendFormattedFormat(Call call)
        => call.Callee.ParameterTypes switch
        {
            [_, var second] when second.Equals(s_int)
                => new InterpolationFormat((int)((Constant)call.Arguments[2]).Value!, HasAlignment: true, FormatString: null),
            [_, var second] when second.Equals(s_string)
                => new InterpolationFormat(0, HasAlignment: false, (string)((Constant)call.Arguments[2]).Value!),
            [_, _, _]
                => new InterpolationFormat((int)((Constant)call.Arguments[2]).Value!, HasAlignment: true, (string)((Constant)call.Arguments[3]).Value!),
            _ => null,
        };

    public static bool IsDefaultInterpolatedStringHandlerToStringAndClear(Call call)
        => IsDefaultInterpolatedStringHandlerInstanceMethod(call, "ToStringAndClear")
            && call.Callee.ReturnType.Equals(s_string)
            && call.Callee.ParameterTypes.IsEmpty
            && call.Arguments.Count == 1;

    public static bool IsIDisposableDispose(Call call)
        => call.IsVirtual
            && call.Callee is
            {
                HasThis: true,
                Name: "Dispose",
                TypeArguments.IsEmpty: true,
                ParameterTypes.IsEmpty: true,
                ReturnType: var returnType,
            }
            && returnType.Equals(s_void)
            && IsCoreLibraryOrFacadeType(call.Callee.DeclaringType, "System", "IDisposable", "System.Runtime");

    public static bool IsStringEquality(Call call)
        => !call.IsVirtual
            && call.Callee is
            {
                HasThis: false,
                Name: "op_Equality",
                TypeArguments.IsEmpty: true,
                ParameterTypes: [var left, var right],
                ReturnType: var returnType,
            }
            && returnType.Equals(s_bool)
            && left.Equals(s_string)
            && right.Equals(s_string)
            && call.Arguments.Count == 2
            && IsCoreLibraryType(call.Callee.DeclaringType, "System", "String");

    public static bool IsStringLengthGetter(LoadProperty property)
        => property is
        {
            HasInstance: true,
            PropertyName: "Length",
            Accessor:
            {
                HasThis: true,
                Name: "get_Length",
                TypeArguments.IsEmpty: true,
                ParameterTypes.IsEmpty: true,
                ReturnType: var returnType,
            },
            IndexArguments.Count: 0,
        }
        && returnType.Equals(s_int)
        && IsCoreLibraryType(property.Accessor.DeclaringType, "System", "String");

    public static bool IsStringCharsGetter(LoadProperty property)
        => property is
        {
            HasInstance: true,
            PropertyName: "Chars",
            Accessor:
            {
                HasThis: true,
                Name: "get_Chars",
                TypeArguments.IsEmpty: true,
                ParameterTypes: [var index],
                ReturnType: var returnType,
            },
            IndexArguments.Count: 1,
        }
        && returnType.Equals(s_char)
        && index.Equals(s_int)
        && IsCoreLibraryType(property.Accessor.DeclaringType, "System", "String");

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

    /// <summary><c>RuntimeHelpers.InitializeArray(Array, RuntimeFieldHandle)</c> — the
    /// bulk array-initializer lowering that copies a <c>&lt;PrivateImplementationDetails&gt;</c>
    /// RVA blob into a freshly-allocated array.</summary>
    public static bool IsRuntimeHelpersInitializeArray(Call call)
        => !call.IsVirtual
            && IsStaticCoreLibraryMethod(
                call.Callee,
                "System.Runtime.CompilerServices",
                "RuntimeHelpers",
                "InitializeArray")
            && call.Arguments.Count == 2
            && call.Callee.ParameterTypes is [_, var handle]
            && handle.Equals(s_runtimeFieldHandle);

    public static bool IsCollectionsMarshalSetCount(Call call, out TypeRef element)
    {
        element = null!;
        if (call.IsVirtual
            || call.Callee is not
            {
                HasThis: false,
                Name: "SetCount",
                TypeArguments: [var typeArgument],
                ReturnType: var returnType,
                ParameterTypes: [var list, var count],
            }
            || !returnType.Equals(s_void)
            || !count.Equals(s_int)
            || call.Arguments.Count != 2
            || !IsCollectionsMarshalType(call.Callee.DeclaringType)
            || !IsGenericListType(list, out element)
            || !element.Equals(typeArgument))
        {
            return false;
        }

        return true;
    }

    public static bool IsCollectionsMarshalAsSpan(Call call, out TypeRef element)
    {
        element = null!;
        if (call.IsVirtual
            || call.Callee is not
            {
                HasThis: false,
                Name: "AsSpan",
                TypeArguments: [var typeArgument],
                ReturnType: var returnType,
                ParameterTypes: [var list],
            }
            || call.Arguments.Count != 1
            || !IsCollectionsMarshalType(call.Callee.DeclaringType)
            || !IsGenericListType(list, out element)
            || !element.Equals(typeArgument)
            || returnType is not { Kind: TypeRefKind.GenericInstance, ElementType: { } spanDefinition, TypeArguments: [var spanElement] }
            || !IsCoreLibraryType(spanDefinition, "System", "Span`1")
            || !spanElement.Equals(element))
        {
            return false;
        }

        return true;
    }

    public static bool IsGenericListType(TypeRef? type, out TypeRef element)
    {
        element = null!;
        if (type is not { Kind: TypeRefKind.GenericInstance, ElementType: { } definition, TypeArguments: [var argument] }
            || !IsCoreLibraryOrFacadeType(definition, "System.Collections.Generic", "List`1", "System.Collections"))
        {
            return false;
        }

        element = argument;
        return true;
    }

    public static bool IsValueTupleType(TypeRef? type, out int arity)
    {
        arity = 0;
        if (type is not { Kind: TypeRefKind.GenericInstance, ElementType: { } definition })
            return false;
        if (definition is not
            {
                Kind: TypeRefKind.Definition,
                Assembly: TypeRef.CoreLibrary or "System.Runtime",
                Namespace: "System",
                Name: var name,
            } || !TryValueTupleArity(name, out arity))
        {
            return false;
        }

        return arity == type.TypeArguments.Length;
    }

    public static bool IsSupportedValueTupleType(TypeRef? type, out int arity)
        => IsValueTupleType(type, out arity) && arity is >= 2 and <= 7;

    public static bool IsValueTupleConstructor(NewObject newObject, out int arity)
        => IsValueTupleConstructorOfArity(newObject, out arity) && arity is >= 2 and <= 7;

    /// <summary>
    /// A direct <c>System.ValueTuple&lt;...&gt;</c> constructor of any arity 1-8,
    /// with the constructor's parameter types matching its type arguments. Arity 8
    /// is the nested <c>TRest</c> head and arity 1 the innermost rest of an
    /// eight-or-more-element tuple literal; <see cref="IsValueTupleConstructor"/> is
    /// the arity 2-7 subset that is itself a complete C# tuple.
    /// </summary>
    public static bool IsValueTupleConstructorOfArity(NewObject newObject, out int arity)
    {
        arity = 0;
        var ctor = newObject.Constructor;
        if (ctor.Name != ".ctor"
            || !IsValueTupleType(ctor.DeclaringType, out arity)
            || ctor.ParameterTypes.Length != arity
            || newObject.Arguments.Count != arity)
        {
            return false;
        }

        for (int i = 0; i < arity; i++)
            if (!ctor.ParameterTypes[i].Equals(ctor.DeclaringType.TypeArguments[i]))
                return false;

        return true;
    }

    static TypeRef? NamedDefinition(TypeRef? type)
        => type is { Kind: TypeRefKind.GenericInstance } ? type.ElementType : type;

    static bool IsSpanLikeType(TypeRef type)
        => IsCoreLibraryType(type, "System", "Span`1")
            || IsCoreLibraryType(type, "System", "ReadOnlySpan`1");

    static bool IsCollectionsMarshalType(TypeRef type)
        => IsCoreLibraryOrFacadeType(type, "System.Runtime.InteropServices", "CollectionsMarshal", "System.Runtime.InteropServices");

    static bool TryValueTupleArity(string name, out int arity)
    {
        const string prefix = "ValueTuple`";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(name[prefix.Length..], out arity))
        {
            arity = 0;
            return false;
        }
        return arity > 0;
    }

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
