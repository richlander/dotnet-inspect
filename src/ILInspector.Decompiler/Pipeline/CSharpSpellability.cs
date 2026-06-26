namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Final-output C# spelling checks for metadata names the printer would emit
/// bare. These are honest-degradation predicates, not rewrite gates: when a
/// compiler-generated or otherwise unspeakable metadata name survives raising,
/// the method is no longer Full-fidelity C#.
/// </summary>
internal static class CSharpSpellability
{
    public static bool HasUnrepresentableMetadataName(IrNode node)
        => UnrepresentableMetadataNameReason(node) is not null;

    public static string? UnrepresentableMetadataNameReason(IrNode node)
    {
        foreach (var type in RenderedTypes(node))
        {
            if (TypeReason(type) is { } reason)
                return reason;
        }

        if (node is IrExpression { ResultType: { } resultType }
            && TypeReason(resultType) is { } expressionTypeReason)
        {
            return expressionTypeReason;
        }

        return node switch
        {
            Call call => MethodReason(call.Callee),
            NewObject newObject => ConstructorReason(newObject.Constructor),
            AddressOfMethod address => MethodReason(address.Method),
            DelegateCreation creation => MethodReason(creation.Method),
            LoadField load => FieldReason(load.Field),
            StoreField store => FieldReason(store.Field),
            LoadFieldAddress address => FieldReason(address.Field),
            LoadProperty load => PropertyReason(load.PropertyName),
            StoreProperty store => PropertyReason(store.PropertyName),
            NullCoalescingFieldAssignment assignment => FieldReason(assignment.Field),
            NullCoalescingPropertyAssignment assignment => PropertyReason(assignment.PropertyName),
            AnonymousObject anonymous => InitializerMembersReason(anonymous.PropertyNames),
            ObjectInitializerExpression initializer => InitializerMembersReason(initializer.Members),
            InitializerBlock block => InitializerMembersReason(block.Members),
            DeconstructionTarget target => DeconstructionTargetReason(target),
            RecursivePropertyDeclarationPattern pattern => PropertyReason(pattern.PropertyName),
            EventSubscription subscription => PropertyReason(subscription.EventName),
            _ => null,
        };
    }

    static IEnumerable<TypeRef> RenderedTypes(IrNode node)
        => node is IrFunction function
            ? FunctionRenderedTypes(function)
            : node.DirectTypes;

    static IEnumerable<TypeRef> FunctionRenderedTypes(IrFunction function)
    {
        yield return function.Signature.ReturnType;
        foreach (var parameter in function.Signature.Parameters)
            yield return parameter.Type;
        foreach (var local in function.Locals)
            yield return local;
        foreach (var region in function.Regions)
            if (region.CatchType is { } catchType)
                yield return catchType;
    }

    static string? ConstructorReason(MethodRef constructor)
    {
        foreach (var argument in constructor.TypeArguments)
            if (TypeReason(argument) is { } reason)
                return reason;
        return null;
    }

    static string? MethodReason(MethodRef method)
    {
        foreach (var argument in method.TypeArguments)
            if (TypeReason(argument) is { } reason)
                return reason;

        if (method.Name is ".ctor")
            return null;

        string name = CSharpNaming.MethodName(method.Name);
        return CSharpNaming.IsUsableIdentifier(name)
            ? null
            : $"method name '{method.Name}' has no C# spelling";
    }

    static string? FieldReason(FieldRef field)
    {
        string name = field.BackingPropertyName ?? field.Name;
        return CSharpNaming.IsUsableIdentifier(name)
            ? null
            : $"field name '{field.Name}' has no C# spelling";
    }

    static string? PropertyReason(string name)
        => CSharpNaming.IsUsableIdentifier(name)
            ? null
            : $"property name '{name}' has no C# spelling";

    static string? InitializerMembersReason(IEnumerable<string?> members)
    {
        foreach (var member in members)
        {
            if (member is null)
                continue;
            if (!CSharpNaming.IsUsableIdentifier(member))
                return $"initializer member name '{member}' has no C# spelling";
        }
        return null;
    }

    static string? DeconstructionTargetReason(DeconstructionTarget target)
        => target.Kind switch
        {
            DeconstructionTargetKind.Field when target.Field is { } field => FieldReason(field),
            DeconstructionTargetKind.Property => PropertyReason(target.PropertyName),
            _ => null,
        };

    static string? TypeReason(TypeRef type)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                if (IsCoreLibPrimitive(type))
                    return null;
                string simpleName = StripArity(InnermostName(type.Name));
                return CSharpNaming.IsUsableIdentifier(simpleName)
                    ? null
                    : $"type name '{simpleName}' has no C# spelling";

            case TypeRefKind.GenericInstance:
                if (type.ElementType is { } definition && TypeReason(definition) is { } definitionReason)
                    return definitionReason;
                foreach (var argument in type.TypeArguments)
                    if (TypeReason(argument) is { } argumentReason)
                        return argumentReason;
                return null;

            case TypeRefKind.SzArray:
            case TypeRefKind.Array:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                return type.ElementType is { } element ? TypeReason(element) : null;

            case TypeRefKind.GenericParameter:
            case TypeRefKind.MethodGenericParameter:
                return type.GenericParameterName.Length == 0 || CSharpNaming.IsUsableIdentifier(type.GenericParameterName)
                    ? null
                    : $"generic parameter name '{type.GenericParameterName}' has no C# spelling";

            case TypeRefKind.FunctionPointer:
                if (type.ElementType is { } returnType && TypeReason(returnType) is { } returnReason)
                    return returnReason;
                foreach (var parameter in type.TypeArguments)
                    if (TypeReason(parameter) is { } parameterReason)
                        return parameterReason;
                return null;

            default:
                return null;
        }
    }

    static bool IsCoreLibPrimitive(TypeRef type)
        => type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == "System"
            && s_coreLibPrimitiveNames.Contains(type.Name);

    static string InnermostName(string metadataName)
    {
        int nested = metadataName.LastIndexOf('+');
        return nested < 0 ? metadataName : metadataName[(nested + 1)..];
    }

    static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    static readonly HashSet<string> s_coreLibPrimitiveNames = new(StringComparer.Ordinal)
    {
        "Boolean", "Byte", "SByte", "Char", "Int16", "UInt16", "Int32", "UInt32",
        "Int64", "UInt64", "Single", "Double", "Decimal", "IntPtr", "UIntPtr",
        "String", "Object", "Void",
    };
}
