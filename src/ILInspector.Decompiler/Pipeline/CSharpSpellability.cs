namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Final-output C# spelling checks for metadata names the printer would emit
/// bare. These are honest-degradation predicates, not rewrite gates: when a
/// compiler-generated or otherwise unspeakable metadata name survives raising,
/// the method is no longer Full-fidelity C#.
/// </summary>
internal static class CSharpSpellability
{
    internal readonly record struct NameIssue(string Discriminator, string Reason);

    public static bool HasUnrepresentableMetadataName(IrNode node)
        => InspectUnrepresentableMetadataName(node) is not null;

    internal static NameIssue? InspectUnrepresentableMetadataName(IrNode node)
    {
        foreach (var type in RenderedTypes(node))
        {
            if (TypeIssue(type) is { } issue)
                return issue;
        }

        if (node is IrExpression { ResultType: { } resultType }
            && TypeIssue(resultType) is { } expressionTypeIssue)
        {
            return expressionTypeIssue;
        }

        return node switch
        {
            Call call => MethodIssue(call.Callee),
            NewObject newObject => ConstructorIssue(newObject.Constructor),
            AddressOfMethod address => MethodIssue(address.Method),
            DelegateCreation creation => MethodIssue(creation.Method),
            LoadField load => FieldIssue(load.Field),
            StoreField store => FieldIssue(store.Field),
            LoadFieldAddress address => FieldIssue(address.Field),
            FixedBufferElementAddress address => FieldIssue(address.BufferField),
            LoadProperty load => PropertyIssue(load.PropertyName),
            StoreProperty store => PropertyIssue(store.PropertyName),
            NullCoalescingFieldAssignment assignment => FieldIssue(assignment.Field),
            NullCoalescingFieldAssignmentExpression assignment => FieldIssue(assignment.Field),
            NullCoalescingPropertyAssignment assignment => PropertyIssue(assignment.PropertyName),
            AnonymousObject anonymous => InitializerMembersIssue(anonymous.PropertyNames),
            ObjectInitializerExpression initializer => InitializerMembersIssue(initializer.Members),
            WithExpression withExpression => InitializerMembersIssue(withExpression.Members),
            InitializerBlock block => InitializerMembersIssue(block.Members),
            DeconstructionTarget target => DeconstructionTargetIssue(target),
            RecursivePropertyDeclarationPattern pattern => PropertyIssue(pattern.PropertyName),
            EventSubscription subscription => PropertyIssue(subscription.EventName),
            LocalFunctionStatement statement => LocalFunctionIssue(statement.Name),
            LocalFunctionInvocation invocation => LocalFunctionIssue(invocation.Name),
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

    static NameIssue? ConstructorIssue(MethodRef constructor)
    {
        foreach (var argument in constructor.TypeArguments)
            if (TypeIssue(argument) is { } issue)
                return issue;
        return null;
    }

    static NameIssue? MethodIssue(MethodRef method)
    {
        foreach (var argument in method.TypeArguments)
            if (TypeIssue(argument) is { } issue)
                return issue;

        if (method.Name is ".ctor")
            return null;

        if (IsUnverifiedAccessorLikeMethod(method))
        {
            return Issue(
                DecompilerFidelityDiscriminators.AccessorMetadataUnavailable,
                $"method '{method.Name}' looks like a property/event accessor but accessor metadata was unavailable; explicit accessor calls have no C# spelling");
        }

        string name = CSharpNaming.MethodName(method.Name);
        return CSharpNaming.IsEscapableIdentifier(name)
            ? null
            : Issue(
                MethodNameDiscriminator(method.Name),
                $"method name '{method.Name}' has no C# spelling");
    }

    static NameIssue? FieldIssue(FieldRef field)
    {
        string name = field.BackingPropertyName ?? field.Name;
        if (CSharpNaming.IsUsableIdentifier(name))
            return null;
        if (CSharpNaming.IsEscapableIdentifier(name))
        {
            return Issue(
                DecompilerFidelityDiscriminators.EscapableFieldName,
                $"field name '{field.Name}' requires C# @ escaping");
        }
        return Issue(
            GeneratedCodeIdentity.IsGeneratedFieldName(name)
                ? DecompilerFidelityDiscriminators.GeneratedFieldName
                : DecompilerFidelityDiscriminators.UnspellableFieldName,
            $"field name '{field.Name}' has no C# spelling");
    }

    static NameIssue? PropertyIssue(string name)
    {
        if (CSharpNaming.IsUsableIdentifier(name))
            return null;
        if (CSharpNaming.IsEscapableIdentifier(name))
        {
            return Issue(
                DecompilerFidelityDiscriminators.EscapablePropertyName,
                $"property name '{name}' requires C# @ escaping");
        }
        return Issue(
            IsGeneratedNameShape(name)
                ? DecompilerFidelityDiscriminators.GeneratedPropertyName
                : DecompilerFidelityDiscriminators.UnspellablePropertyName,
            $"property name '{name}' has no C# spelling");
    }

    static bool IsUnverifiedAccessorLikeMethod(MethodRef method)
        => method.AccessorKind == AccessorKind.Unknown
            && method.IsSpecialName
            && !method.IsSpecialNameInferred
            && (method.Name.StartsWith("get_", StringComparison.Ordinal)
                || method.Name.StartsWith("set_", StringComparison.Ordinal)
                || method.Name.StartsWith("add_", StringComparison.Ordinal)
                || method.Name.StartsWith("remove_", StringComparison.Ordinal));

    // A local function name is emitted through CSharpNaming.EscapeIdentifier, so a
    // reserved keyword (e.g. return -> @return) is spellable; only a name with
    // invalid identifier characters (e.g. a hand-written `bad-name`) has no C#
    // spelling. Use the keyword-tolerant predicate to avoid degrading the
    // keyword-escaped local functions #1465 made Full.
    static NameIssue? LocalFunctionIssue(string name)
        => CSharpNaming.IsEscapableIdentifier(name)
            ? null
            : Issue(
                DecompilerFidelityDiscriminators.UnspellableLocalFunctionName,
                $"local function name '{name}' has no C# spelling");

    static NameIssue? InitializerMembersIssue(IEnumerable<string?> members)
    {
        foreach (var member in members)
        {
            if (member is null)
                continue;
            if (!CSharpNaming.IsUsableIdentifier(member))
            {
                if (CSharpNaming.IsEscapableIdentifier(member))
                {
                    return Issue(
                        DecompilerFidelityDiscriminators.EscapableInitializerMemberName,
                        $"initializer member name '{member}' requires C# @ escaping");
                }
                return Issue(
                    IsGeneratedNameShape(member)
                        ? DecompilerFidelityDiscriminators.GeneratedInitializerMemberName
                        : DecompilerFidelityDiscriminators.UnspellableInitializerMemberName,
                    $"initializer member name '{member}' has no C# spelling");
            }
        }
        return null;
    }

    static NameIssue? DeconstructionTargetIssue(DeconstructionTarget target)
        => target.Kind switch
        {
            DeconstructionTargetKind.Field when target.Field is { } field => FieldIssue(field),
            DeconstructionTargetKind.Property => PropertyIssue(target.PropertyName),
            _ => null,
        };

    static NameIssue? TypeIssue(TypeRef type)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                if (IsCoreLibPrimitive(type))
                    return null;
                // Validate every nested segment, not just the leaf: a foreign
                // nested type is spelled through its declaring chain
                // (`Outer.Inner`), so an unspellable outer segment also has no
                // valid C# spelling even when the innermost name is fine. Keyword
                // segments are spellable via @ escaping, so use the keyword-tolerant
                // identifier predicate.
                foreach (var segment in type.Name.Split('+'))
                {
                    string simpleSegment = StripArity(segment);
                    if (!CSharpNaming.IsEscapableIdentifier(simpleSegment))
                    {
                        return Issue(
                            TypeNameDiscriminator(simpleSegment),
                            $"type name '{simpleSegment}' has no C# spelling");
                    }
                }
                return null;

            case TypeRefKind.GenericInstance:
                if (type.ElementType is { } definition && TypeIssue(definition) is { } definitionIssue)
                    return definitionIssue;
                foreach (var argument in type.TypeArguments)
                    if (TypeIssue(argument) is { } argumentIssue)
                        return argumentIssue;
                return null;

            case TypeRefKind.SzArray:
            case TypeRefKind.Array:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                return type.ElementType is { } element ? TypeIssue(element) : null;

            case TypeRefKind.GenericParameter:
            case TypeRefKind.MethodGenericParameter:
                return type.GenericParameterName.Length == 0 || CSharpNaming.IsEscapableIdentifier(type.GenericParameterName)
                    ? null
                    : Issue(
                        IsGeneratedNameShape(type.GenericParameterName)
                            ? DecompilerFidelityDiscriminators.GeneratedGenericParameterName
                            : DecompilerFidelityDiscriminators.UnspellableGenericParameterName,
                        $"generic parameter name '{type.GenericParameterName}' has no C# spelling");

            case TypeRefKind.FunctionPointer:
                if (type.ElementType is { } returnType && TypeIssue(returnType) is { } returnIssue)
                    return returnIssue;
                foreach (var parameter in type.TypeArguments)
                    if (TypeIssue(parameter) is { } parameterIssue)
                        return parameterIssue;
                return null;

            default:
                return null;
        }
    }

    static string MethodNameDiscriminator(string name)
    {
        if (GeneratedCodeIdentity.IsSynthesizedLambdaMethodName(name))
            return DecompilerFidelityDiscriminators.LambdaMethodName;
        if (GeneratedCodeIdentity.IsSynthesizedLocalFunctionName(name))
            return DecompilerFidelityDiscriminators.LocalFunctionMethodName;
        return IsGeneratedNameShape(name)
            ? DecompilerFidelityDiscriminators.GeneratedMethodName
            : DecompilerFidelityDiscriminators.UnspellableMethodName;
    }

    static string TypeNameDiscriminator(string name)
    {
        if (name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
            return DecompilerFidelityDiscriminators.DisplayClassTypeName;
        if (name == "<>c")
            return DecompilerFidelityDiscriminators.LambdaHolderTypeName;
        if (name.StartsWith("<", StringComparison.Ordinal)
            && name.Contains(">d__", StringComparison.Ordinal))
        {
            return DecompilerFidelityDiscriminators.StateMachineTypeName;
        }
        return IsGeneratedNameShape(name)
            ? DecompilerFidelityDiscriminators.GeneratedTypeName
            : DecompilerFidelityDiscriminators.UnspellableTypeName;
    }

    static bool IsGeneratedNameShape(string name)
        => name.StartsWith("<", StringComparison.Ordinal);

    static NameIssue Issue(string discriminator, string reason)
        => new(discriminator, reason);

    static bool IsCoreLibPrimitive(TypeRef type)
        => type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == "System"
            && s_coreLibPrimitiveNames.Contains(type.Name);

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
