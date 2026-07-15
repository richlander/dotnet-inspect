using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace ILInspector.Metadata;

public sealed record MetadataMethodDeclaration(
    string MetadataName,
    string CSharpName,
    string Accessibility,
    bool IsPublicOrProtected,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsSealed,
    ApiSignature Signature,
    IReadOnlyList<string> Attributes)
{
    public SignatureDecodeStatus? SignatureDecodeStatus { get; init; }
}

public sealed record MetadataPropertyDeclaration(
    string MetadataName,
    string CSharpName,
    string Accessibility,
    bool IsPublicOrProtected,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsSealed,
    ApiSignature Signature,
    IReadOnlyList<string> Attributes,
    MethodDefinitionHandle Getter,
    MethodDefinitionHandle Setter)
{
    public SignatureDecodeStatus? SignatureDecodeStatus { get; init; }
}

public sealed record MetadataFieldDeclaration(
    string MetadataName,
    string CSharpName,
    string Accessibility,
    bool IsStatic,
    bool IsReadOnly,
    bool IsConst,
    string? ReturnType,
    IReadOnlyList<string> Attributes)
{
    public SignatureDecodeStatus? SignatureDecodeStatus { get; init; }
}

/// <summary>
/// Handle-based declaration questions over SRM metadata. This layer is safe for
/// NativeAOT and compile-back source composition: it does not load inspected
/// assemblies and does not depend on Roslyn or the decompiler.
/// </summary>
public static class MetadataDeclarationQuery
{
    const string DegradedType = "object";
    static readonly MethodSignature<string> DegradedMethodSignature =
        new(default, DegradedType, requiredParameterCount: 0, genericParameterCount: 0, []);

    public static ApiType GetTypeSurface(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        bool includeNonPublicMembers = false)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var attributes = typeDef.Attributes;
        var (ns, name) = GetApiTypeNameParts(reader, typeDef);
        var type = new ApiType
        {
            Namespace = ns,
            Name = name,
            Accessibility = TypeAccessibility(typeDef),
            IsSealed = (attributes & TypeAttributes.Sealed) != 0,
            IsAbstract = (attributes & TypeAttributes.Abstract) != 0,
            Attributes = AttributeReader.RenderAttributes(reader, typeDef.GetCustomAttributes(), qualifyNames: true),
            TypeParameters = TypeParameters(reader, typeDef.GetGenericParameters(), GenericContext.ForType(reader, typeDef)).ToList(),
        };

        if ((attributes & TypeAttributes.Interface) != 0)
        {
            type.Kind = "interface";
        }
        else if (!typeDef.BaseType.IsNil)
        {
            var baseTypeName = TypeResolver.GetTypeName(reader, typeDef.BaseType);
            type.BaseType = baseTypeName;
            type.Kind = baseTypeName switch
            {
                "System.Enum" => "enum",
                "System.ValueType" => "struct",
                "System.Delegate" or "System.MulticastDelegate" => "delegate",
                _ => "class",
            };
        }
        else
        {
            type.Kind = "class";
        }

        type.IsStatic = type.IsSealed && type.IsAbstract;
        var accessorMethods = new HashSet<MethodDefinitionHandle>();
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var declaration = GetProperty(reader, typeDef, property);
            if (!includeNonPublicMembers && declaration.Accessibility != "public")
                continue;

            if (!declaration.Getter.IsNil)
                accessorMethods.Add(declaration.Getter);
            if (!declaration.Setter.IsNil)
                accessorMethods.Add(declaration.Setter);

            type.Members.Add(new ApiMember
            {
                Name = declaration.MetadataName,
                Kind = "property",
                SignatureModel = declaration.Signature,
                Signature = PropertySignatureText(declaration),
                SignatureDecodeStatus = declaration.SignatureDecodeStatus,
                IsStatic = declaration.IsStatic,
                IsAbstract = declaration.IsAbstract,
                IsVirtual = declaration.IsVirtual,
                IsOverride = declaration.IsOverride,
                IsSealed = declaration.IsSealed,
                Accessibility = NonPublicAccessibility(declaration.Accessibility),
                Attributes = declaration.Attributes.ToList(),
                GetterToken = declaration.Getter.IsNil ? null : MetadataTokens.GetToken(declaration.Getter),
                SetterToken = declaration.Setter.IsNil ? null : MetadataTokens.GetToken(declaration.Setter),
            });
        }

        foreach (var methodHandle in typeDef.GetMethods())
        {
            if (accessorMethods.Contains(methodHandle))
                continue;

            var method = reader.GetMethodDefinition(methodHandle);
            var methodName = reader.GetString(method.Name);
            if (methodName.StartsWith('<'))
                continue;

            var declaration = GetMethod(reader, typeDef, method);
            if (!includeNonPublicMembers && declaration.Accessibility != "public")
                continue;

            type.Members.Add(new ApiMember
            {
                Name = declaration.MetadataName,
                Kind = declaration.MetadataName == ".ctor" ? "constructor" : "method",
                SignatureModel = declaration.Signature,
                Signature = MethodSignatureText(declaration),
                SignatureDecodeStatus = declaration.SignatureDecodeStatus,
                MetadataToken = MetadataTokens.GetToken(methodHandle),
                IsStatic = declaration.IsStatic,
                IsAbstract = declaration.IsAbstract,
                IsVirtual = declaration.IsVirtual,
                IsOverride = declaration.IsOverride,
                IsSealed = declaration.IsSealed,
                Accessibility = NonPublicAccessibility(declaration.Accessibility),
                Attributes = declaration.Attributes.ToList(),
            });
        }

        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            var fieldName = reader.GetString(field.Name);
            if (fieldName.StartsWith("<", StringComparison.Ordinal))
                continue;

            var declaration = GetField(reader, typeDef, field);
            if (!includeNonPublicMembers && declaration.Accessibility != "public")
                continue;

            type.Members.Add(new ApiMember
            {
                Name = declaration.MetadataName,
                Kind = "field",
                ReturnType = declaration.ReturnType,
                SignatureModel = declaration.ReturnType is null
                    ? null
                    : new ApiSignature { ReturnType = declaration.ReturnType, MemberName = declaration.CSharpName },
                SignatureDecodeStatus = declaration.SignatureDecodeStatus,
                IsStatic = declaration.IsStatic,
                IsReadOnly = declaration.IsReadOnly,
                IsConst = declaration.IsConst,
                Accessibility = NonPublicAccessibility(declaration.Accessibility),
                Attributes = declaration.Attributes.ToList(),
            });
        }

        return type;
    }

    public static MetadataMethodDeclaration GetMethod(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var result = GuardedSignatureText.MethodText(
            reader,
            method,
            GenericContext.ForMethod(reader, typeDef, method));
        var signature = ProjectDecode(result, DegradedMethodSignature, out var status);
        return GetMethod(reader, typeDef, method, signature) with
        {
            SignatureDecodeStatus = status,
        };
    }

    public static string GetMethodReturnType(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var signature = GuardedSignatureText.MethodText(
            reader,
            method,
            GenericContext.ForMethod(reader, typeDef, method))
            .GetValueOrThrow();
        return FormatMethodReturnType(reader, signature.ReturnType, method.GetParameters());
    }

    public static MetadataMethodDeclaration GetMethod(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        MethodSignature<string> signature)
    {
        var name = reader.GetString(method.Name);
        var attributes = method.Attributes;
        var access = attributes & MethodAttributes.MemberAccessMask;
        var isPublicOrProtected = IsPublicOrProtected(access);
        var isVirtual = (attributes & MethodAttributes.Virtual) != 0;
        var isNewSlot = (attributes & MethodAttributes.NewSlot) != 0;
        var isOverride = isVirtual && !isNewSlot;
        var typeParameters = MethodTypeParameters(reader, typeDef, method);
        var csharpName = EscapeIdentifier(name);
        var methodName = typeParameters.Count == 0
            ? csharpName
            : $"{csharpName}<{string.Join(", ", typeParameters.Select(parameter => parameter.Name))}>";

        return new MetadataMethodDeclaration(
            name,
            csharpName,
            AccessibilityKeyword(access),
            isPublicOrProtected,
            (attributes & MethodAttributes.Static) != 0,
            isPublicOrProtected && (attributes & MethodAttributes.Abstract) != 0,
            isPublicOrProtected
                && isVirtual
                && (attributes & MethodAttributes.Abstract) == 0
                && (attributes & MethodAttributes.Final) == 0
                && isNewSlot,
            isOverride,
            isOverride && (attributes & MethodAttributes.Final) != 0,
            new ApiSignature
            {
                ReturnType = FormatMethodReturnType(reader, signature.ReturnType, method.GetParameters()),
                ReturnAttributes = ReturnAttributes(reader, method.GetParameters()).ToList(),
                MemberName = methodName,
                TypeParameters = typeParameters.ToList(),
                Parameters = MethodParameters(reader, method, signature).ToList(),
            },
            RenderMemberAttributes(reader, method.GetCustomAttributes()));
    }

    public static MetadataPropertyDeclaration GetProperty(
        MetadataReader reader,
        TypeDefinition typeDef,
        PropertyDefinition property)
    {
        var result = GuardedSignatureText.PropertyText(
            reader,
            property,
            GenericContext.ForType(reader, typeDef));
        var signature = ProjectDecode(result, DegradedMethodSignature, out var status);
        return GetProperty(reader, typeDef, property, signature) with
        {
            SignatureDecodeStatus = status,
        };
    }

    static MetadataPropertyDeclaration GetProperty(
        MetadataReader reader,
        TypeDefinition typeDef,
        PropertyDefinition property,
        MethodSignature<string> signature)
    {
        var accessors = property.GetAccessors();
        var getter = accessors.Getter.IsNil ? default : reader.GetMethodDefinition(accessors.Getter);
        var setter = accessors.Setter.IsNil ? default : reader.GetMethodDefinition(accessors.Setter);

        var bestAccess = BestAccessorAccess(getter, setter, accessors);
        var primaryAccessor = !accessors.Getter.IsNil ? getter : setter;
        var accessorAttributes = !accessors.Getter.IsNil || !accessors.Setter.IsNil
            ? primaryAccessor.Attributes
            : default;
        var isVirtual = (accessorAttributes & MethodAttributes.Virtual) != 0;
        var isNewSlot = (accessorAttributes & MethodAttributes.NewSlot) != 0;
        var isOverride = isVirtual && !isNewSlot;
        var isPublicOrProtected = IsPublicOrProtected(bestAccess);

        var accessorParameters = !accessors.Getter.IsNil
            ? getter.GetParameters()
            : !accessors.Setter.IsNil
                ? setter.GetParameters()
                : default;

        var accessorModels = new List<ApiAccessor>();
        if (!accessors.Getter.IsNil)
        {
            var getterAccess = getter.Attributes & MethodAttributes.MemberAccessMask;
            accessorModels.Add(new ApiAccessor
            {
                Kind = "get",
                Accessibility = AccessorAccessibility(getterAccess, bestAccess),
                ReturnAttributes = ReturnAttributes(reader, getter.GetParameters()).ToList(),
            });
        }

        if (!accessors.Setter.IsNil)
        {
            accessorModels.Add(new ApiAccessor
            {
                Kind = "set",
                Accessibility = AccessorAccessibility(setter.Attributes & MethodAttributes.MemberAccessMask, bestAccess),
            });
        }

        var parameters = PropertyParameters(reader, accessorParameters, signature).ToList();
        var name = reader.GetString(property.Name);
        var csharpName = EscapeIdentifier(name);
        return new MetadataPropertyDeclaration(
            name,
            csharpName,
            AccessibilityKeyword(bestAccess),
            isPublicOrProtected,
            !accessors.Getter.IsNil || !accessors.Setter.IsNil
                ? (accessorAttributes & MethodAttributes.Static) != 0
                : false,
            isPublicOrProtected && (accessorAttributes & MethodAttributes.Abstract) != 0,
            isPublicOrProtected
                && isVirtual
                && (accessorAttributes & MethodAttributes.Abstract) == 0
                && (accessorAttributes & MethodAttributes.Final) == 0
                && isNewSlot,
            isOverride,
            isOverride && (accessorAttributes & MethodAttributes.Final) != 0,
            new ApiSignature
            {
                ReturnType = signature.ReturnType,
                ReturnAttributes = !accessors.Getter.IsNil
                    ? ReturnAttributes(reader, getter.GetParameters()).ToList()
                    : [],
                MemberName = parameters.Count == 0 ? csharpName : "this[]",
                Parameters = parameters,
                Accessors = accessorModels,
            },
            RenderMemberAttributes(reader, property.GetCustomAttributes()),
            accessors.Getter,
            accessors.Setter);
    }

    public static MetadataFieldDeclaration GetField(
        MetadataReader reader,
        TypeDefinition typeDef,
        FieldDefinition field)
    {
        var result = GuardedSignatureText.FieldText(
            reader,
            field,
            GenericContext.ForType(reader, typeDef));
        var fieldType = ProjectDecode(result, DegradedType, out var status);
        return GetField(reader, typeDef, field, fieldType) with
        {
            SignatureDecodeStatus = status,
        };
    }

    static MetadataFieldDeclaration GetField(
        MetadataReader reader,
        TypeDefinition typeDef,
        FieldDefinition field,
        string fieldType)
    {
        var attributes = field.Attributes;
        var access = attributes & FieldAttributes.FieldAccessMask;
        return new MetadataFieldDeclaration(
            reader.GetString(field.Name),
            EscapeIdentifier(reader.GetString(field.Name)),
            AccessibilityKeyword(access),
            (attributes & FieldAttributes.Static) != 0,
            (attributes & FieldAttributes.InitOnly) != 0,
            (attributes & FieldAttributes.Literal) != 0,
            fieldType,
            RenderMemberAttributes(reader, field.GetCustomAttributes()));
    }

    /// <summary>
    /// Whether <paramref name="field"/> is declared <c>volatile</c> — its signature carries
    /// <c>modreq(System.Runtime.CompilerServices.IsVolatile)</c>. Reads the same custom-modifier
    /// evidence (<see cref="TypeNode.HasRequiredModifier"/>) that the API surface uses for
    /// <c>in</c>/<c>ref readonly</c>, so callers do not re-decode the signature to spot the modifier.
    /// </summary>
    public static bool IsVolatileField(MetadataReader reader, FieldDefinition field, GenericContext context)
        => GuardedProviderDecode.Field(reader, field, TypeNodeProvider.Instance, context, (TypeNode)new NamedTypeNode("object", isReferenceType: true))
            .HasRequiredModifier("System.Runtime.CompilerServices", "IsVolatile");

    /// <summary>
    /// The C# generic-constraint clause body for each in-scope generic parameter of
    /// <paramref name="method"/> — its own parameters and its declaring type's —
    /// keyed by parameter name (for example <c>"TOther"</c> to
    /// <c>"INumberBase&lt;TOther&gt;"</c>). Each body already honors the C# ordering
    /// and redundancy rules (<c>class</c>/<c>struct</c>, then base/interface
    /// constraints, then <c>new()</c>; <c>struct</c> implies <c>new()</c> and drops
    /// the <c>ValueType</c> base). Type parameters win over method parameters on a
    /// name clash, since both share one scope inside a method shell. A parameter with
    /// no constraints is omitted. This is the product-owned source of constraint
    /// declaration facts, so consumers do not re-derive the C# rules from metadata.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetGenericConstraintClauses(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var clauses = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in TypeParameters(reader, typeDef.GetGenericParameters(), GenericContext.ForType(reader, typeDef)))
            if (SpellableConstraintClause(parameter) is { } clause)
                clauses[parameter.Name] = clause;
        foreach (var parameter in MethodTypeParameters(reader, typeDef, method))
            if (SpellableConstraintClause(parameter) is { } clause)
                clauses.TryAdd(parameter.Name, clause);
        return clauses;
    }

    /// <summary>
    /// The spellable <c>where</c> clause body for a parameter, or null when nothing
    /// remains to spell. C# forbids an explicit <c>System.Object</c> constraint
    /// (<c>CS0702: Constraint cannot be special class 'object'</c>) — and it is
    /// semantically vacuous — so it is dropped. Non-C# compilers can emit it even
    /// though Roslyn does not.
    /// </summary>
    internal static string? SpellableConstraintClause(TypeParameter parameter)
    {
        var spellable = parameter.Constraints
            .Where(constraint => constraint is not ("System.Object" or "Object" or "object"))
            .ToList();
        return spellable.Count > 0 ? string.Join(", ", spellable) : null;
    }

    public static string SelfTypeSignature(MetadataReader reader, TypeDefinition typeDef)
    {
        var metadataFullName = TypeResolver.GetFullName(reader, typeDef);
        var directTypeParameters = TypeParameterNames(reader, typeDef);
        var typeParameters = directTypeParameters.Count >= GenericArity(metadataFullName)
            ? directTypeParameters
            : TypeAndDeclaringTypeParameters(reader, typeDef);
        return TypeResolver.ApplyGenericArguments(metadataFullName, typeParameters);
    }

    public static IReadOnlyList<string> RenderMemberAttributes(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes)
        => AttributeReader.RenderAttributes(reader, attributes, qualifyNames: true);

    public static bool IsPublicOrProtected(MethodDefinition method)
        => IsPublicOrProtected(method.Attributes & MethodAttributes.MemberAccessMask);

    public static bool IsAbstractMethod(MethodDefinition method)
        => IsPublicOrProtected(method)
            && (method.Attributes & MethodAttributes.Abstract) != 0;

    public static bool IsVirtualMethod(MethodDefinition method)
        => IsPublicOrProtected(method)
            && (method.Attributes & MethodAttributes.Virtual) != 0
            && (method.Attributes & MethodAttributes.Abstract) == 0
            && (method.Attributes & MethodAttributes.Final) == 0
            && (method.Attributes & MethodAttributes.NewSlot) != 0;

    public static string AccessibilityKeyword(MethodDefinition method)
        => AccessibilityKeyword(method.Attributes & MethodAttributes.MemberAccessMask);

    internal static string? TypeAccessibility(TypeDefinition type)
        => (type.Attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.NotPublic => "internal",
            TypeAttributes.NestedPrivate => "private",
            TypeAttributes.NestedFamily => "protected",
            TypeAttributes.NestedAssembly => "internal",
            TypeAttributes.NestedFamANDAssem => "private protected",
            TypeAttributes.NestedFamORAssem => "protected internal",
            _ => null,
        };

    static IReadOnlyList<ApiParameter> MethodParameters(
        MetadataReader reader,
        MethodDefinition method,
        MethodSignature<string> signature)
        => Parameters(reader, method.GetParameters(), signature.ParameterTypes);

    static IReadOnlyList<ApiParameter> PropertyParameters(
        MetadataReader reader,
        ParameterHandleCollection accessorParameters,
        MethodSignature<string> signature)
        => Parameters(reader, accessorParameters, signature.ParameterTypes);

    static IReadOnlyList<ApiParameter> Parameters(
        MetadataReader reader,
        ParameterHandleCollection parameterHandles,
        IReadOnlyList<string> parameterTypes)
    {
        var parameters = new List<ApiParameter>();
        for (var index = 0; index < parameterTypes.Count; index++)
        {
            var parameterInfo = GetParameterInfo(reader, parameterHandles, index + 1);
            var name = parameterInfo.Name is { Length: > 0 } parameterName
                ? parameterName
                : $"arg{index}";
            var type = parameterTypes[index];
            string? modifier = null;
            if (type.StartsWith("ref ", StringComparison.Ordinal))
            {
                type = type["ref ".Length..];
                modifier = parameterInfo.RefKind ?? "ref";
            }

            if (parameterInfo.IsParams)
                modifier = "params";

            var attributes = parameterInfo.Attributes.ToList();
            string? defaultValueText = null;
            var hasDefault = false;
            if (TryFormatAttributedParameterDefault(
                    reader,
                    parameterInfo.CustomAttributes,
                    out var attributedDefaultValue,
                    out var attributedDefaultAttributes))
            {
                hasDefault = true;
                foreach (var attribute in attributedDefaultAttributes)
                {
                    if (!attributes.Contains(attribute, StringComparer.Ordinal))
                        attributes.Add(attribute);
                }

                if (attributedDefaultValue.Length == 0)
                {
                    if (!attributes.Contains("System.Runtime.InteropServices.Optional", StringComparer.Ordinal))
                        attributes.Add("System.Runtime.InteropServices.Optional");
                }
                else
                {
                    defaultValueText = attributedDefaultValue;
                }
            }
            else if (parameterInfo.DefaultParameter is { } defaultParameter)
            {
                hasDefault = true;
                if (TryFormatParameterDefault(reader, defaultParameter, type, out var formattedDefault))
                {
                    defaultValueText = formattedDefault;
                }
                else if (!attributes.Contains("System.Runtime.InteropServices.Optional", StringComparer.Ordinal))
                {
                    attributes.Add("System.Runtime.InteropServices.Optional");
                }
            }

            parameters.Add(new ApiParameter
            {
                Attributes = attributes,
                Name = name,
                Type = type,
                Modifier = modifier,
                HasDefault = hasDefault,
                DefaultValueText = hasDefault ? defaultValueText : null,
            });
        }

        return parameters;
    }

    sealed record ParameterInfo(
        string? Name,
        bool IsParams,
        string? RefKind,
        IReadOnlyList<string> Attributes,
        Parameter? DefaultParameter,
        CustomAttributeHandleCollection CustomAttributes);

    static ParameterInfo GetParameterInfo(MetadataReader reader, ParameterHandleCollection handles, int sequenceNumber)
    {
        foreach (var handle in handles)
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber != sequenceNumber)
                continue;

            var attributes = parameter.GetCustomAttributes();
            var isParams = AttributeReader.HasAttribute(reader, attributes, "System.ParamArrayAttribute")
                || AttributeReader.HasAttribute(reader, attributes, KnownAttributeNames.ParamCollectionAttribute);
            var isOut = (parameter.Attributes & ParameterAttributes.Out) != 0;
            var isIn = (parameter.Attributes & ParameterAttributes.In) != 0;
            var refKind = isOut && !isIn
                ? "out"
                : isIn && !isOut
                    ? "in"
                    : null;
            var defaultParameter = (parameter.Attributes & ParameterAttributes.HasDefault) != 0
                ? parameter
                : (Parameter?)null;
            return new ParameterInfo(
                reader.GetString(parameter.Name),
                isParams,
                refKind,
                AttributeReader.RenderParameterAttributes(reader, handle),
                defaultParameter,
                attributes);
        }

        return new ParameterInfo(null, false, null, [], null, default);
    }

    static IReadOnlyList<string> ReturnAttributes(MetadataReader reader, ParameterHandleCollection handles)
    {
        foreach (var handle in handles)
        {
            if (reader.GetParameter(handle).SequenceNumber == 0)
                return AttributeReader.RenderParameterAttributes(reader, handle);
        }

        return [];
    }

    static string FormatMethodReturnType(MetadataReader reader, string returnType, ParameterHandleCollection handles)
        => returnType.StartsWith("ref ", StringComparison.Ordinal)
            && ReturnIsReadOnlyRef(reader, handles)
            ? $"ref readonly {returnType["ref ".Length..]}"
            : returnType;

    static bool ReturnIsReadOnlyRef(MetadataReader reader, ParameterHandleCollection handles)
    {
        foreach (var handle in handles)
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber == 0
                && (AttributeReader.HasAttribute(reader, parameter.GetCustomAttributes(), KnownAttributeNames.IsReadOnlyAttribute)
                    || AttributeReader.HasAttribute(reader, parameter.GetCustomAttributes(), KnownAttributeNames.RequiresLocationAttribute)))
            {
                return true;
            }
        }
        return false;
    }

    static IReadOnlyList<TypeParameter> MethodTypeParameters(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var context = GenericContext.ForMethod(reader, typeDef, method);
        return TypeParameters(reader, method.GetGenericParameters(), context);
    }

    static IReadOnlyList<TypeParameter> TypeParameters(
        MetadataReader reader,
        GenericParameterHandleCollection handles,
        GenericContext context)
    {
        var parameters = new List<TypeParameter>();
        foreach (var handle in handles)
        {
            var parameter = reader.GetGenericParameter(handle);
            var constraints = new List<string>();
            var attributes = parameter.Attributes;
            var isStruct = (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
            if (GenericConstraintKeywords.PrimaryKeyword(attributes, nullableFlag: 0, isUnmanaged: false) is { } primaryKeyword)
                constraints.Add(primaryKeyword);

            foreach (var constraintHandle in parameter.GetConstraints())
            {
                var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                if (ConstraintTypeName(reader, constraint.Type, context) is { Length: > 0 } constraintName)
                {
                    if (isStruct && constraintName is "System.ValueType" or "ValueType")
                        continue;
                    constraints.Add(constraintName);
                }
            }

            if (GenericConstraintKeywords.NewConstraintKeyword(attributes) is { } newConstraint)
                constraints.Add(newConstraint);

            parameters.Add(new TypeParameter
            {
                Name = reader.GetString(parameter.Name),
                Constraints = constraints,
            });
        }

        return parameters;
    }

    static string? ConstraintTypeName(MetadataReader reader, EntityHandle handle, GenericContext context)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition => TypeResolver.GetFullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
            HandleKind.TypeReference => TypeResolver.GetTypeNameFromReference(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => GuardedSignatureText.TypeSpecText(
                reader,
                (TypeSpecificationHandle)handle,
                context).TryGetValue(out var typeName)
                    ? typeName
                    : null,
            _ => null,
        };

    static bool IsPublicOrProtected(MethodAttributes access)
        => access is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    static MethodAttributes BestAccessorAccess(
        MethodDefinition getter,
        MethodDefinition setter,
        PropertyAccessors accessors)
    {
        MethodAttributes best = 0;
        if (!accessors.Getter.IsNil)
            best = getter.Attributes & MethodAttributes.MemberAccessMask;
        if (!accessors.Setter.IsNil)
        {
            var setterAccess = setter.Attributes & MethodAttributes.MemberAccessMask;
            if ((int)setterAccess > (int)best)
                best = setterAccess;
        }

        return best;
    }

    static string? AccessorAccessibility(MethodAttributes access, MethodAttributes bestAccess)
        => access == bestAccess ? null : NonPublicAccessibility(access);

    static string AccessibilityKeyword(MethodAttributes access)
        => NonPublicAccessibility(access) ?? "public";

    static string AccessibilityKeyword(FieldAttributes access)
        => NonPublicAccessibility(access) ?? "public";

    static string? NonPublicAccessibility(MethodAttributes access) => access switch
    {
        MethodAttributes.PrivateScope => "private",
        MethodAttributes.Private => "private",
        MethodAttributes.FamANDAssem => "private protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected internal",
        _ => null,
    };

    static string? NonPublicAccessibility(FieldAttributes access) => access switch
    {
        FieldAttributes.PrivateScope => "private",
        FieldAttributes.Private => "private",
        FieldAttributes.FamANDAssem => "private protected",
        FieldAttributes.Assembly => "internal",
        FieldAttributes.Family => "protected",
        FieldAttributes.FamORAssem => "protected internal",
        _ => null,
    };

    static string? NonPublicAccessibility(string accessibility)
        => accessibility == "public" ? null : accessibility;

    static T ProjectDecode<T>(
        SignatureDecodeResult<T> result,
        T degradedValue,
        out SignatureDecodeStatus? status)
        where T : notnull
    {
        if (result.TryGetValue(out var value))
        {
            status = null;
            return value;
        }

        status = SignatureDecodeStatus.Degraded;
        return degradedValue;
    }

    static (string? Namespace, string Name) GetApiTypeNameParts(MetadataReader reader, TypeDefinition typeDef)
    {
        var fullName = TypeResolver.GetFullName(reader, typeDef);
        var rootNamespace = GetRootNamespace(reader, typeDef);
        if (rootNamespace.Length == 0)
            return (null, fullName);

        var prefix = rootNamespace + ".";
        return fullName.StartsWith(prefix, StringComparison.Ordinal)
            ? (rootNamespace, fullName[prefix.Length..])
            : (rootNamespace, fullName);
    }

    static string GetRootNamespace(MetadataReader reader, TypeDefinition typeDef)
    {
        var declaringType = typeDef.GetDeclaringType();
        return declaringType.IsNil
            ? reader.GetString(typeDef.Namespace)
            : GetRootNamespace(reader, reader.GetTypeDefinition(declaringType));
    }

    static string MethodSignatureText(MetadataMethodDeclaration declaration)
    {
        var parameters = $"({string.Join(", ", declaration.Signature.Parameters.Select(ParameterDeclaration))})";
        var returnType = declaration.Signature.ReturnType ?? "void";
        return $"{returnType} {declaration.Signature.MemberName ?? declaration.MetadataName}{parameters}";
    }

    static string PropertySignatureText(MetadataPropertyDeclaration declaration)
    {
        var returnType = declaration.Signature.ReturnType ?? "void";
        var accessors = declaration.Signature.Accessors.Count == 0
            ? "{ get; }"
            : "{ " + string.Join(" ", declaration.Signature.Accessors.Select(AccessorText)) + " }";
        return declaration.Signature.Parameters.Count == 0
            ? $"{returnType} {declaration.CSharpName} {accessors}"
            : $"{returnType} this[{string.Join(", ", declaration.Signature.Parameters.Select(ParameterDeclaration))}] {accessors}";
    }

    static string ParameterDeclaration(ApiParameter parameter)
    {
        var attributes = parameter.Attributes.Count == 0
            ? ""
            : $"[{string.Join(", ", parameter.Attributes)}] ";
        string type = EscapeCompatibilityTypeKeywords(parameter.Type);
        string typeWithModifier = string.IsNullOrEmpty(parameter.Modifier)
            ? type
            : $"{parameter.Modifier} {type}";
        var head = string.IsNullOrWhiteSpace(parameter.Name)
            ? typeWithModifier
            : $"{typeWithModifier} {EscapeIdentifier(parameter.Name)}";
        var declaration = parameter.HasDefault && parameter.DefaultValueText is { Length: > 0 }
            ? $"{head} = {parameter.DefaultValueText}"
            : head;
        return EscapeCompatibilityQualifiedKeywordSegments(attributes + declaration);
    }

    internal static string EscapeCompatibilityTypeKeywords(string type)
    {
        var builder = new StringBuilder(type.Length);
        for (int index = 0; index < type.Length;)
        {
            if (!(char.IsLetter(type[index]) || type[index] == '_'))
            {
                builder.Append(type[index++]);
                continue;
            }

            int end = index + 1;
            while (end < type.Length
                   && (char.IsLetterOrDigit(type[end]) || type[end] == '_'))
            {
                end++;
            }

            string identifier = type[index..end];
            bool isTypeSyntaxKeyword = IsTypeSyntaxKeyword(type, identifier, index, end);
            if ((index == 0 || type[index - 1] != '@')
                && EscapeIdentifier(identifier) != identifier
                && !isTypeSyntaxKeyword)
            {
                builder.Append('@');
            }
            builder.Append(identifier);
            index = end;
        }
        return builder.ToString();
    }

    static bool IsTypeSyntaxKeyword(string type, string identifier, int start, int end)
    {
        if (identifier is "bool" or "byte" or "sbyte" or "char" or "decimal" or "double"
            or "float" or "int" or "uint" or "nint" or "nuint" or "long" or "ulong"
            or "object" or "short" or "ushort" or "string" or "void")
        {
            return end == type.Length || type[end] != '.';
        }

        if (identifier == "delegate")
            return end < type.Length && type[end] == '*';

        if (type.StartsWith("delegate*", StringComparison.Ordinal)
            && identifier is "ref" or "in" or "out" or "readonly" or "unmanaged")
        {
            return true;
        }

        return start == 0
            && end < type.Length
            && char.IsWhiteSpace(type[end])
            && identifier is "ref" or "in" or "out" or "params" or "readonly" or "scoped";
    }

    // ApiMember.Signature is a legacy compatibility string. Keep its qualified
    // keyword escaping local rather than restoring declaration ownership to Metadata.
    static string EscapeCompatibilityQualifiedKeywordSegments(string signature)
    {
        var builder = new StringBuilder(signature.Length);
        bool inString = false;
        bool inChar = false;
        bool escapedCharacter = false;
        for (int index = 0; index < signature.Length; index++)
        {
            char character = signature[index];
            builder.Append(character);
            if (inString || inChar)
            {
                if (escapedCharacter)
                {
                    escapedCharacter = false;
                    continue;
                }
                if (character == '\\')
                {
                    escapedCharacter = true;
                    continue;
                }
                if (inString && character == '"')
                    inString = false;
                else if (inChar && character == '\'')
                    inChar = false;
                continue;
            }
            if (character == '"')
            {
                inString = true;
                continue;
            }
            if (character == '\'')
            {
                inChar = true;
                continue;
            }
            if (character != '.'
                || index + 1 >= signature.Length
                || signature[index + 1] == '@'
                || !(char.IsLetter(signature[index + 1]) || signature[index + 1] == '_'))
            {
                continue;
            }

            int start = index + 1;
            int end = start + 1;
            while (end < signature.Length
                   && (char.IsLetterOrDigit(signature[end]) || signature[end] == '_'))
            {
                end++;
            }

            string segment = signature[start..end];
            string escaped = EscapeIdentifier(segment);
            if (escaped != segment)
            {
                builder.Append(escaped);
                index = end - 1;
            }
        }
        return builder.ToString();
    }

    static string AccessorText(ApiAccessor accessor)
        => accessor.Accessibility is { Length: > 0 }
            ? $"{accessor.Accessibility} {accessor.Kind};"
            : $"{accessor.Kind};";

    static bool TryFormatAttributedParameterDefault(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out string defaultValueText,
        out IReadOnlyList<string> defaultAttributes)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            var attributeTypeName = AttributeReader.GetAttributeTypeName(reader, attribute.Constructor);
            if (attributeTypeName == KnownAttributeNames.DecimalConstantAttribute
                && TryReadDecimalConstantAttribute(reader, attribute, out var decimalValue))
            {
                defaultValueText = FormatDecimalDefault(decimalValue);
                defaultAttributes = [];
                return true;
            }

            if (attributeTypeName == KnownAttributeNames.DateTimeConstantAttribute
                && TryReadDateTimeConstantAttribute(reader, attribute, out var ticks))
            {
                defaultValueText = "";
                defaultAttributes =
                [
                    "System.Runtime.InteropServices.Optional",
                    $"System.Runtime.CompilerServices.DateTimeConstant({FormatInt64Default(ticks)})",
                ];
                return true;
            }
        }

        defaultValueText = "";
        defaultAttributes = [];
        return false;
    }

    static bool TryReadDateTimeConstantAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out long ticks)
    {
        if (AttributeDecoder.TryDecode(reader, attribute) is { FixedArguments.Length: 1 } decoded
            && decoded.FixedArguments[0].Value is long value)
        {
            ticks = value;
            return true;
        }

        ticks = 0;
        return false;
    }

    static bool TryReadDecimalConstantAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out decimal value)
    {
        if (AttributeDecoder.TryDecode(reader, attribute) is not { } decoded
            || decoded.FixedArguments.Length != 5
            || decoded.FixedArguments[0].Value is not byte scale
            || decoded.FixedArguments[1].Value is not byte sign
            || !TryGetUInt32(decoded.FixedArguments[2].Value, out var hi)
            || !TryGetUInt32(decoded.FixedArguments[3].Value, out var mid)
            || !TryGetUInt32(decoded.FixedArguments[4].Value, out var low)
            || scale > 28
            || sign > 1)
        {
            value = default;
            return false;
        }

        value = new decimal(
            unchecked((int)low),
            unchecked((int)mid),
            unchecked((int)hi),
            sign != 0,
            scale);
        return true;
    }

    static bool TryGetUInt32(object? value, out uint result)
    {
        switch (value)
        {
            case uint unsigned:
                result = unsigned;
                return true;
            case int signed:
                result = unchecked((uint)signed);
                return true;
            default:
                result = 0;
                return false;
        }
    }

    static string FormatDecimalDefault(decimal value)
        => value.ToString("G29", CultureInfo.InvariantCulture) + "m";

    static string FormatInt64Default(long value)
    {
        long minValue = long.MaxValue;
        minValue = -minValue - 1;
        return value == minValue
            ? "long.MinValue"
            : value.ToString(CultureInfo.InvariantCulture) + "L";
    }

    static bool TryFormatParameterDefault(
        MetadataReader reader,
        Parameter parameter,
        string parameterType,
        out string defaultValueText)
    {
        defaultValueText = "";
        if ((parameter.Attributes & ParameterAttributes.HasDefault) == 0)
            return false;

        var constantHandle = parameter.GetDefaultValue();
        if (constantHandle.IsNil)
            return false;

        var constant = reader.GetConstant(constantHandle);
        var blob = reader.GetBlobReader(constant.Value);
        defaultValueText = constant.TypeCode switch
        {
            ConstantTypeCode.Boolean when IsDefaultType(parameterType, "bool") => blob.ReadBoolean() ? "true" : "false",
            ConstantTypeCode.Char when IsDefaultType(parameterType, "char") => $"'{EscapeCharLiteral(blob.ReadChar())}'",
            ConstantTypeCode.SByte when IsDefaultType(parameterType, "sbyte") => blob.ReadSByte().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Byte when IsDefaultType(parameterType, "byte") => blob.ReadByte().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Int16 when IsDefaultType(parameterType, "short") => blob.ReadInt16().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.UInt16 when IsDefaultType(parameterType, "ushort") => blob.ReadUInt16().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Int32 when IsDefaultType(parameterType, "int") => blob.ReadInt32().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Int32 when IsLikelyEnumDefaultType(parameterType) => FormatEnumParameterDefault(reader, blob.ReadInt32(), parameterType),
            ConstantTypeCode.UInt32 when IsDefaultType(parameterType, "uint") => blob.ReadUInt32().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.UInt32 when IsLikelyEnumDefaultType(parameterType) => FormatEnumParameterDefault(reader, blob.ReadUInt32(), parameterType),
            ConstantTypeCode.Int64 when IsDefaultType(parameterType, "long") => blob.ReadInt64().ToString(CultureInfo.InvariantCulture) + "L",
            ConstantTypeCode.Int64 when IsLikelyEnumDefaultType(parameterType) => FormatEnumParameterDefault(reader, blob.ReadInt64(), parameterType),
            ConstantTypeCode.UInt64 when IsDefaultType(parameterType, "ulong") => blob.ReadUInt64().ToString(CultureInfo.InvariantCulture) + "UL",
            ConstantTypeCode.UInt64 when IsLikelyEnumDefaultType(parameterType) => FormatEnumParameterDefault(reader, blob.ReadUInt64(), parameterType),
            ConstantTypeCode.Single when IsDefaultType(parameterType, "float") => FormatParameterSingleDefault(blob.ReadSingle()),
            ConstantTypeCode.Double when IsDefaultType(parameterType, "double") => FormatParameterDoubleDefault(blob.ReadDouble()),
            ConstantTypeCode.String when IsDefaultType(parameterType, "string") => StringLiteral(blob.ReadUTF16(blob.Length)),
            ConstantTypeCode.NullReference when AcceptsNullParameterDefault(parameterType) => "null",
            _ => "",
        };
        return defaultValueText.Length != 0;
    }

    static bool IsDefaultType(string parameterType, string expected)
        => string.Equals(parameterType, expected, StringComparison.Ordinal);

    static bool AcceptsNullParameterDefault(string parameterType)
        => parameterType is not ("bool" or "byte" or "sbyte" or "char" or "decimal" or "double"
            or "float" or "int" or "uint" or "nint" or "nuint" or "long" or "ulong"
            or "short" or "ushort" or "System.DateTime");

    static bool IsLikelyEnumDefaultType(string parameterType)
        => parameterType is not ("bool" or "byte" or "sbyte" or "char" or "decimal" or "double"
            or "float" or "int" or "uint" or "nint" or "nuint" or "long" or "ulong"
            or "short" or "ushort" or "string" or "object" or "System.Boolean" or "System.Byte"
            or "System.SByte" or "System.Char" or "System.Decimal" or "System.Double" or "System.Single"
            or "System.Int32" or "System.UInt32" or "System.IntPtr" or "System.UIntPtr" or "System.Int64"
            or "System.UInt64" or "System.Int16" or "System.UInt16" or "System.String" or "System.Object"
            or "System.DateTime");

    static string FormatEnumParameterDefault(MetadataReader reader, object value, string parameterType)
    {
        if (!TryConvertEnumConstant(value, out var defaultValue))
            return "";

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (TypeResolver.GetTypeName(reader, typeDef.BaseType) != "System.Enum"
                || !string.Equals(TypeResolver.GetFullName(reader, typeDef), parameterType, StringComparison.Ordinal))
            {
                continue;
            }

            string escapedType = EscapeCompatibilityTypeKeywords(parameterType);
            return $"({escapedType}){defaultValue.ToString(CultureInfo.InvariantCulture)}";
        }

        return "";
    }

    static bool TryConvertEnumConstant(object value, out decimal converted)
    {
        switch (value)
        {
            case sbyte v:
                converted = v;
                return true;
            case byte v:
                converted = v;
                return true;
            case short v:
                converted = v;
                return true;
            case ushort v:
                converted = v;
                return true;
            case int v:
                converted = v;
                return true;
            case uint v:
                converted = v;
                return true;
            case long v:
                converted = v;
                return true;
            case ulong v:
                converted = v;
                return true;
            default:
                converted = 0;
                return false;
        }
    }

    static string FormatParameterSingleDefault(float value)
    {
        if (float.IsNaN(value))
            return "float.NaN";
        if (float.IsPositiveInfinity(value))
            return "float.PositiveInfinity";
        if (float.IsNegativeInfinity(value))
            return "float.NegativeInfinity";
        return value.ToString("R", CultureInfo.InvariantCulture) + "f";
    }

    static string FormatParameterDoubleDefault(double value)
    {
        if (double.IsNaN(value))
            return "double.NaN";
        if (double.IsPositiveInfinity(value))
            return "double.PositiveInfinity";
        if (double.IsNegativeInfinity(value))
            return "double.NegativeInfinity";
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    static string StringLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char ch in value)
            sb.Append(ch == '"' ? "\\\"" : EscapeCharLiteral(ch));
        sb.Append('"');
        return sb.ToString();
    }

    static string EscapeCharLiteral(char ch) => ch switch
    {
        '\\' => "\\\\",
        '\'' => "\\'",
        '\0' => "\\0",
        '\a' => "\\a",
        '\b' => "\\b",
        '\f' => "\\f",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\v' => "\\v",
        _ when char.IsControl(ch) => $"\\u{(int)ch:x4}",
        _ => ch.ToString(),
    };

    static IReadOnlyList<string> TypeAndDeclaringTypeParameters(MetadataReader reader, TypeDefinition typeDef)
    {
        var parameters = new List<string>();
        var declaringType = typeDef.GetDeclaringType();
        if (!declaringType.IsNil)
            parameters.AddRange(TypeAndDeclaringTypeParameters(reader, reader.GetTypeDefinition(declaringType)));
        parameters.AddRange(TypeParameterNames(reader, typeDef));
        return parameters;
    }

    static IReadOnlyList<string> TypeParameterNames(MetadataReader reader, TypeDefinition typeDef)
        => typeDef.GetGenericParameters()
            .Select(handle => reader.GetString(reader.GetGenericParameter(handle).Name))
            .ToArray();

    static int GenericArity(string metadataFullName)
    {
        var arity = 0;
        for (var i = 0; i < metadataFullName.Length; i++)
        {
            if (metadataFullName[i] != '`')
                continue;
            var start = i + 1;
            var end = start;
            while (end < metadataFullName.Length && char.IsDigit(metadataFullName[end]))
                end++;
            if (end > start && int.TryParse(metadataFullName.AsSpan(start, end - start), out var value))
            {
                arity += value;
                i = end - 1;
            }
        }

        return arity;
    }

    static string EscapeIdentifier(string name)
        => ReservedKeywords.Contains(name) ? "@" + name : name;

    // Keep synchronized with CSharpDeclarationWriter's owning spelling policy.
    static readonly HashSet<string> ReservedKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while", "await", "record", "required", "init", "file", "scoped",
    };
}
