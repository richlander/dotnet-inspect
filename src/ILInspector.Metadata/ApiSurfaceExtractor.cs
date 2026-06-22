using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Extracts public API surface from assemblies.
/// </summary>
public static class ApiSurfaceExtractor
{
    public static ApiSurface Extract(PEReader peReader, bool includeAll = false, bool typesOnly = false)
    {
        var surface = new ApiSurface();
        var reader = peReader.GetMetadataReader();

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            var attributes = typeDef.Attributes;

            // Only include public types
            if (!typeDef.IsPublic)
                continue;

            string metadataName = reader.GetString(typeDef.Name);

            // Skip compiler-generated types
            if (TypeFilters.IsCompilerGenerated(metadataName))
                continue;

            // Skip EditorBrowsable(Never) and Obsolete types unless --all
            if (!includeAll && AttributeReader.HasHiddenAttribute(reader, typeDef.GetCustomAttributes()))
                continue;

            var (typeNamespace, typeName) = GetApiTypeNameParts(reader, typeDef);

            var apiType = new ApiType
            {
                Namespace = typeNamespace,
                Name = typeName,
                IsSealed = (attributes & TypeAttributes.Sealed) != 0,
                IsAbstract = (attributes & TypeAttributes.Abstract) != 0,
            };

            // Determine kind
            if ((attributes & TypeAttributes.Interface) != 0)
            {
                apiType.Kind = "interface";
            }
            else if (!typeDef.BaseType.IsNil)
            {
                string? baseTypeName = TypeResolver.GetTypeName(reader, typeDef.BaseType);
                apiType.BaseType = baseTypeName;

                apiType.Kind = baseTypeName switch
                {
                    "System.Enum" => "enum",
                    "System.ValueType" => "struct",
                    "System.Delegate" or "System.MulticastDelegate" => "delegate",
                    _ => "class"
                };
            }
            else
            {
                apiType.Kind = "class";
            }

            apiType.IsStatic = apiType.IsSealed && apiType.IsAbstract;

            // The ref struct / readonly struct modifiers. Their [IsByRefLike] /
            // [IsReadOnly] attributes are compiler-synthesized from syntax and so
            // suppressed from the attribute list (AttributeReader.IsReEmitted), so
            // the modifier is reconstructed here from the still-present attribute.
            if (apiType.Kind == "struct")
            {
                var typeAttributes = typeDef.GetCustomAttributes();
                apiType.IsByRefLike = AttributeReader.HasAttribute(reader, typeAttributes, KnownAttributeNames.IsByRefLikeAttribute);
                apiType.IsReadOnly = AttributeReader.HasAttribute(reader, typeAttributes, KnownAttributeNames.IsReadOnlyAttribute);
            }

            // Check if this is an extension class (static class with [Extension] attribute)
            bool isExtensionClass = apiType.IsStatic && AttributeReader.HasExtensionAttribute(reader, typeDef.GetCustomAttributes());

            // Nullability context for annotated signatures
            byte typeNullableContext = NullabilityReader.GetNullableContext(reader, typeDef.GetCustomAttributes());

            // Get type's generic context for resolving interface type parameters
            var typeContext = GenericContext.ForType(reader, typeDef);

            // Get generic type parameters with constraints
            var genericParams = typeDef.GetGenericParameters();
            if (genericParams.Count > 0)
            {
                apiType.TypeParameters = [];
                foreach (var paramHandle in genericParams)
                {
                    var param = reader.GetGenericParameter(paramHandle);
                    var typeParam = new TypeParameter
                    {
                        Name = reader.GetString(param.Name)
                    };

                    // Get variance (only applies to interfaces and delegates)
                    var attrs = param.Attributes;
                    if ((attrs & GenericParameterAttributes.Covariant) != 0)
                        typeParam.Variance = "out";
                    else if ((attrs & GenericParameterAttributes.Contravariant) != 0)
                        typeParam.Variance = "in";

                    // Get special constraints
                    if ((attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                        typeParam.Constraints.Add("class");
                    if ((attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                        typeParam.Constraints.Add("struct");
                    if ((attrs & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
                        (attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
                        // new() is implied by struct constraint, only show if not struct
                        typeParam.Constraints.Add("new()");
                    if ((attrs & GenericParameterAttributes.AllowByRefLike) != 0)
                        typeParam.Constraints.Add("allows ref struct");

                    // Get type constraints (interfaces and base class)
                    foreach (var constraintHandle in param.GetConstraints())
                    {
                        var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                        string? constraintTypeName = TypeResolver.GetTypeName(reader, constraint.Type, typeContext);
                        if (constraintTypeName != null)
                        {
                            // Skip System.ValueType (shown as 'struct' above) and System.Object
                            if (constraintTypeName != "System.ValueType" && constraintTypeName != "System.Object")
                                typeParam.Constraints.Add(constraintTypeName);
                        }
                    }

                    apiType.TypeParameters.Add(typeParam);
                }
            }

            // Get interfaces
            var interfaces = typeDef.GetInterfaceImplementations();
            if (interfaces.Count > 0)
            {
                apiType.Interfaces = [];
                foreach (var ifaceHandle in interfaces)
                {
                    var iface = reader.GetInterfaceImplementation(ifaceHandle);
                    string? ifaceName = TypeResolver.GetTypeName(reader, iface.Interface, typeContext);
                    if (ifaceName != null)
                        apiType.Interfaces.Add(ifaceName);
                }
            }

            // Get members (public only, or all when includeAll)
            if (!typesOnly)
            {
            apiType.Members = [];

            var explicitImplementationBodies = GetExplicitImplementationBodies(reader, typeDef);

            // Methods
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var methodAccess = method.Attributes & MethodAttributes.MemberAccessMask;
                var isExplicitInterfaceImplementation = explicitImplementationBodies.Contains(methodHandle);
                if (methodAccess != MethodAttributes.Public && !includeAll && !isExplicitInterfaceImplementation)
                    continue;

                string methodName = reader.GetString(method.Name);

                // Skip property accessors and event accessors
                if (methodName.StartsWith("get_") || methodName.StartsWith("set_") ||
                    methodName.StartsWith("add_") || methodName.StartsWith("remove_"))
                    continue;

                // Skip compiler-generated methods (lambdas, state machines, etc.)
                if (methodName.StartsWith("<"))
                    continue;

                // Skip EditorBrowsable(Never) methods unless --all; obsolete are surfaced with marker.
                if (!includeAll
                    && !isExplicitInterfaceImplementation
                    && AttributeReader.HasEditorBrowsableNeverAttribute(reader, method.GetCustomAttributes()))
                    continue;

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(reader, method.GetCustomAttributes(), out var obsoleteMessage);

                var signature = GetMethodSignature(reader, typeDef, method, typeNullableContext);
                var isOperator = IsOperatorMethodName(methodName);
                var methodAttributes = method.Attributes;
                var isVirtual = (methodAttributes & MethodAttributes.Virtual) != 0;
                var isNewSlot = (methodAttributes & MethodAttributes.NewSlot) != 0;
                var isOverride = isVirtual && !isNewSlot && !isExplicitInterfaceImplementation;
                var member = new ApiMember
                {
                    Name = methodName,
                    Kind = methodName switch
                    {
                        ".ctor" => "constructor",
                        _ when isOperator => "operator",
                        _ when isExplicitInterfaceImplementation => "explicit-interface-implementation",
                        _ => "method"
                    },
                    IsStatic = (methodAttributes & MethodAttributes.Static) != 0,
                    IsVirtual = isVirtual,
                    IsAbstract = (methodAttributes & MethodAttributes.Abstract) != 0,
                    IsOverride = isOverride,
                    IsSealed = isOverride && (methodAttributes & MethodAttributes.Final) != 0,
                    Signature = signature,
                    MetadataToken = MetadataTokens.GetToken(methodHandle),
                    IsUnsafe = HasUnsafeSignature(signature)
                        || AttributeReader.HasRequiresUnsafeAttribute(reader, method.GetCustomAttributes()),
                    Accessibility = isExplicitInterfaceImplementation && !isOperator ? null : GetAccessibility(methodAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage
                };

                // Check for extension method
                if (isExtensionClass && member.IsStatic && AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes()))
                {
                    member.IsExtension = true;
                    member.ExtendedType = GetFirstParameterType(reader, typeDef, method);
                    member.DeclaringType = apiType.FullName;
                }

                apiType.Members.Add(member);
                surface.PublicMethodCount++;
            }

            // Properties
            foreach (var propHandle in typeDef.GetProperties())
            {
                var prop = reader.GetPropertyDefinition(propHandle);
                var accessors = prop.GetAccessors();

                // Determine best accessor visibility
                MethodAttributes bestAccess = 0;
                bool isStaticProperty = false;
                bool isVirtualProperty = false;
                bool isAbstractProperty = false;
                bool isOverrideProperty = false;
                bool isSealedProperty = false;
                if (!accessors.Getter.IsNil)
                {
                    var getter = reader.GetMethodDefinition(accessors.Getter);
                    var getterAttributes = getter.Attributes;
                    bestAccess = getter.Attributes & MethodAttributes.MemberAccessMask;
                    isStaticProperty = (getterAttributes & MethodAttributes.Static) != 0;
                    isVirtualProperty = (getterAttributes & MethodAttributes.Virtual) != 0;
                    isAbstractProperty = (getterAttributes & MethodAttributes.Abstract) != 0;
                    isOverrideProperty = isVirtualProperty && (getterAttributes & MethodAttributes.NewSlot) == 0;
                    isSealedProperty = isOverrideProperty && (getterAttributes & MethodAttributes.Final) != 0;
                }
                if (!accessors.Setter.IsNil)
                {
                    var setter = reader.GetMethodDefinition(accessors.Setter);
                    var setterAttributes = setter.Attributes;
                    var setterAccess = setterAttributes & MethodAttributes.MemberAccessMask;
                    if (setterAccess > bestAccess)
                        bestAccess = setterAccess;
                    var setterVirtual = (setterAttributes & MethodAttributes.Virtual) != 0;
                    var setterOverride = setterVirtual && (setterAttributes & MethodAttributes.NewSlot) == 0;
                    isStaticProperty |= (setterAttributes & MethodAttributes.Static) != 0;
                    isVirtualProperty |= setterVirtual;
                    isAbstractProperty |= (setterAttributes & MethodAttributes.Abstract) != 0;
                    isOverrideProperty |= setterOverride;
                    isSealedProperty |= setterOverride && (setterAttributes & MethodAttributes.Final) != 0;
                }

                bool isPublicProp = bestAccess == MethodAttributes.Public;
                if (!isPublicProp && !includeAll)
                    continue;

                // Skip EditorBrowsable(Never) properties unless --all; obsolete are surfaced with marker.
                if (!includeAll && AttributeReader.HasEditorBrowsableNeverAttribute(reader, prop.GetCustomAttributes()))
                    continue;

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(reader, prop.GetCustomAttributes(), out var obsoleteMessage);

                var member = new ApiMember
                {
                    Name = reader.GetString(prop.Name),
                    Kind = "property",
                    Signature = GetPropertySignature(reader, typeDef, prop, accessors, typeNullableContext, includeAll),
                    IsStatic = isStaticProperty,
                    IsVirtual = isVirtualProperty,
                    IsAbstract = isAbstractProperty,
                    IsOverride = isOverrideProperty,
                    IsSealed = isSealedProperty,
                    Accessibility = GetAccessibility(bestAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage
                };

                apiType.Members.Add(member);
                surface.PublicPropertyCount++;
            }

            // Fields (non-backing fields; non-public included with --all)
            bool isEnum = apiType.Kind == "enum";
            foreach (var fieldHandle in typeDef.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                var fieldAccess = field.Attributes & FieldAttributes.FieldAccessMask;
                if (fieldAccess != FieldAttributes.Public && !includeAll)
                    continue;

                string fieldName = reader.GetString(field.Name);
                if (fieldName.StartsWith("<"))
                    continue; // Skip backing fields

                // Skip EditorBrowsable(Never) fields unless --all; obsolete are surfaced with marker.
                if (!includeAll && AttributeReader.HasEditorBrowsableNeverAttribute(reader, field.GetCustomAttributes()))
                    continue;

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(reader, field.GetCustomAttributes(), out var obsoleteMessage);

                // Decode field type
                string? fieldType = null;
                if (!isEnum)
                {
                    var context = GenericContext.ForType(reader, typeDef);
                    var fieldNode = field.DecodeSignature(TypeNodeProvider.Instance, context);
                    var fieldBytes = NullabilityReader.GetNullableBytes(reader, field.GetCustomAttributes());
                    int pos = 0;
                    fieldNode.ApplyNullability(fieldBytes, ref pos, typeNullableContext);
                    fieldType = fieldNode.Render();
                }

                var member = new ApiMember
                {
                    Name = fieldName,
                    Kind = "field",
                    ReturnType = fieldType,
                    IsStatic = (field.Attributes & FieldAttributes.Static) != 0,
                    IsReadOnly = (field.Attributes & FieldAttributes.InitOnly) != 0,
                    IsConst = (field.Attributes & FieldAttributes.Literal) != 0,
                    Accessibility = GetFieldAccessibility(fieldAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage
                };

                // Read enum constant value
                if (isEnum && (field.Attributes & FieldAttributes.Literal) != 0)
                {
                    var constantHandle = field.GetDefaultValue();
                    if (!constantHandle.IsNil)
                    {
                        var constant = reader.GetConstant(constantHandle);
                        var blob = reader.GetBlobReader(constant.Value);
                        member.EnumValue = constant.TypeCode switch
                        {
                            ConstantTypeCode.SByte => blob.ReadSByte(),
                            ConstantTypeCode.Byte => blob.ReadByte(),
                            ConstantTypeCode.Int16 => blob.ReadInt16(),
                            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
                            ConstantTypeCode.Int32 => blob.ReadInt32(),
                            ConstantTypeCode.UInt32 => blob.ReadUInt32(),
                            ConstantTypeCode.Int64 => blob.ReadInt64(),
                            ConstantTypeCode.UInt64 => (long)blob.ReadUInt64(),
                            _ => null
                        };
                    }
                }

                apiType.Members.Add(member);
                surface.PublicFieldCount++;
            }

            // Events
            foreach (var eventHandle in typeDef.GetEvents())
            {
                var evt = reader.GetEventDefinition(eventHandle);
                var accessors = evt.GetAccessors();

                // Check if adder exists
                if (accessors.Adder.IsNil)
                    continue;

                var adder = reader.GetMethodDefinition(accessors.Adder);
                var adderAccess = adder.Attributes & MethodAttributes.MemberAccessMask;
                if (adderAccess != MethodAttributes.Public && !includeAll)
                    continue;

                // Skip EditorBrowsable(Never) events unless --all; obsolete are surfaced with marker.
                if (!includeAll && AttributeReader.HasEditorBrowsableNeverAttribute(reader, evt.GetCustomAttributes()))
                    continue;

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(reader, evt.GetCustomAttributes(), out var obsoleteMessage);
                var eventType = TypeResolver.GetTypeName(reader, evt.Type, GenericContext.ForType(reader, typeDef)) ?? "";
                var eventNullableBytes = NullabilityReader.GetNullableBytes(reader, evt.GetCustomAttributes());
                eventNullableBytes ??= NullabilityReader.GetParameterNullableBytes(reader, adder.GetParameters(), 1);
                if (eventNullableBytes is { Length: > 0 } && eventNullableBytes[0] == 2 && !eventType.EndsWith("?", StringComparison.Ordinal))
                    eventType += "?";
                var adderAttributes = adder.Attributes;
                var isVirtualEvent = (adderAttributes & MethodAttributes.Virtual) != 0;
                var isOverrideEvent = isVirtualEvent && (adderAttributes & MethodAttributes.NewSlot) == 0;

                var member = new ApiMember
                {
                    Name = reader.GetString(evt.Name),
                    Kind = "event",
                    ReturnType = eventType,
                    Signature = $"{eventType} {reader.GetString(evt.Name)}",
                    IsStatic = (adderAttributes & MethodAttributes.Static) != 0,
                    IsVirtual = isVirtualEvent,
                    IsAbstract = (adderAttributes & MethodAttributes.Abstract) != 0,
                    IsOverride = isOverrideEvent,
                    IsSealed = isOverrideEvent && (adderAttributes & MethodAttributes.Final) != 0,
                    Accessibility = GetAccessibility(adderAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage
                };

                apiType.Members.Add(member);
                surface.PublicEventCount++;
            }
            } // end if (!typesOnly)

            surface.Types.Add(apiType);
            surface.PublicTypeCount++;
        }

        AttachLocalExtensionMethods(surface);

        // Extract type forwarders (ExportedTypes that are forwarded to other assemblies)
        foreach (var exportedTypeHandle in reader.ExportedTypes)
        {
            var exportedType = reader.GetExportedType(exportedTypeHandle);

            // Type forwarders have IsForwarder flag set
            if (!exportedType.IsForwarder)
                continue;

            var fullName = reader.GetFullTypeName(exportedType);

            // Get the target assembly
            string targetAssembly = "";
            if (exportedType.Implementation.Kind == HandleKind.AssemblyReference)
            {
                var assemblyRef = reader.GetAssemblyReference((AssemblyReferenceHandle)exportedType.Implementation);
                targetAssembly = reader.GetString(assemblyRef.Name);
            }

            surface.TypeForwarders.Add(new TypeForwarder
            {
                TypeName = fullName,
                TargetAssembly = targetAssembly
            });
        }

        return surface;
    }

    private static (string? Namespace, string Name) GetApiTypeNameParts(MetadataReader reader, TypeDefinition typeDef)
    {
        var fullName = reader.GetFullTypeName(typeDef);
        var rootNamespace = GetRootNamespace(reader, typeDef);
        if (rootNamespace.Length == 0)
            return (null, fullName);

        var prefix = rootNamespace + ".";
        return fullName.StartsWith(prefix, StringComparison.Ordinal)
            ? (rootNamespace, fullName[prefix.Length..])
            : (rootNamespace, fullName);
    }

    private static string GetRootNamespace(MetadataReader reader, TypeDefinition typeDef)
    {
        var declaringType = typeDef.GetDeclaringType();
        if (!declaringType.IsNil)
            return GetRootNamespace(reader, reader.GetTypeDefinition(declaringType));

        return reader.GetString(typeDef.Namespace);
    }

    private static HashSet<MethodDefinitionHandle> GetExplicitImplementationBodies(
        MetadataReader reader, TypeDefinition typeDef)
    {
        HashSet<MethodDefinitionHandle> handles = [];
        foreach (var implementationHandle in typeDef.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody.Kind == HandleKind.MethodDefinition)
                handles.Add((MethodDefinitionHandle)implementation.MethodBody);
        }

        return handles;
    }

    private static bool IsOperatorMethodName(string methodName) =>
        methodName.StartsWith("op_", StringComparison.Ordinal);

    private static void AttachLocalExtensionMethods(ApiSurface surface)
    {
        var targets = surface.Types
            .SelectMany(type => GetTypeMatchKeys(type).Select(key => (key, type)))
            .GroupBy(item => item.key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().type, StringComparer.OrdinalIgnoreCase);

        foreach (var declaringType in surface.Types)
        {
            foreach (var extension in declaringType.Members.Where(member => member.IsExtension))
            {
                var key = NormalizeTypeMatchKey(extension.ExtendedType);
                if (key == null || !targets.TryGetValue(key, out var targetType))
                    continue;
                if (ReferenceEquals(targetType, declaringType))
                    continue;
                if (targetType.Members.Any(member =>
                    member.Kind == "extension-method"
                    && string.Equals(member.DeclaringType, declaringType.FullName, StringComparison.Ordinal)
                    && string.Equals(member.Name, extension.Name, StringComparison.Ordinal)
                    && string.Equals(member.Signature, extension.Signature, StringComparison.Ordinal)))
                    continue;

                var declaringOverloadIndex = declaringType.Members
                    .Where(member => string.Equals(member.Name, extension.Name, StringComparison.Ordinal))
                    .ToList()
                    .IndexOf(extension) + 1;

                targetType.Members.Add(new ApiMember
                {
                    Name = extension.Name,
                    Kind = "extension-method",
                    ReturnType = extension.ReturnType,
                    Signature = extension.Signature,
                    MetadataToken = extension.MetadataToken,
                    IsStatic = extension.IsStatic,
                    IsVirtual = extension.IsVirtual,
                    IsAbstract = extension.IsAbstract,
                    IsOverride = extension.IsOverride,
                    IsSealed = extension.IsSealed,
                    IsUnsafe = extension.IsUnsafe,
                    IsExtension = true,
                    ExtendedType = extension.ExtendedType,
                    DeclaringType = declaringType.FullName,
                    DeclaringOverloadIndex = declaringOverloadIndex,
                    IsObsolete = extension.IsObsolete,
                    ObsoleteMessage = extension.ObsoleteMessage,
                    Documentation = extension.Documentation
                });
            }
        }
    }

    private static IEnumerable<string> GetTypeMatchKeys(ApiType type)
    {
        var fullNameKey = NormalizeTypeMatchKey(type.FullName);
        if (fullNameKey != null)
            yield return fullNameKey;
    }

    private static string? NormalizeTypeMatchKey(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var value = typeName.Trim();
        foreach (var prefix in (ReadOnlySpan<string>)["ref ", "in ", "out "])
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                value = value[prefix.Length..].TrimStart();
                break;
            }
        }

        if (value.EndsWith("?", StringComparison.Ordinal))
            value = value[..^1];

        value = PrimitiveTypeNames.ToClrFullName(value);

        var genericIndex = value.IndexOf('<');
        if (genericIndex > 0)
            value = value[..genericIndex];

        var arityIndex = value.IndexOf('`');
        if (arityIndex > 0)
            value = value[..arityIndex];

        return value;
    }

    /// <summary>
    /// Populates DerivedTypes for a specific type by scanning all types in the surface.
    /// </summary>
    public static void PopulateDerivedTypes(ApiSurface surface, ApiType targetType)
    {
        var fullName = string.IsNullOrEmpty(targetType.Namespace)
            ? targetType.Name
            : $"{targetType.Namespace}.{targetType.Name}";

        List<string> derivedTypes = [];

        foreach (var type in surface.Types)
        {
            if (type == targetType)
                continue;

            // Check if this type's base is our target
            if (type.BaseType == fullName)
            {
                var derivedFullName = string.IsNullOrEmpty(type.Namespace)
                    ? type.Name
                    : $"{type.Namespace}.{type.Name}";
                derivedTypes.Add(derivedFullName);
            }

            // Check if this type implements our target (if target is an interface)
            if (targetType.Kind == "interface" && type.Interfaces != null)
            {
                if (type.Interfaces.Contains(fullName))
                {
                    var derivedFullName = string.IsNullOrEmpty(type.Namespace)
                        ? type.Name
                        : $"{type.Namespace}.{type.Name}";
                    if (!derivedTypes.Contains(derivedFullName))
                        derivedTypes.Add(derivedFullName);
                }
            }
        }

        if (derivedTypes.Count > 0)
        {
            derivedTypes.Sort(StringComparer.Ordinal);
            targetType.DerivedTypes = derivedTypes;
        }
    }

    private static string GetMethodSignature(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method, byte typeNullableContext)
    {
        string name = reader.GetString(method.Name);
        var context = GenericContext.ForMethod(reader, typeDef, method);
        var treeSignature = method.DecodeSignature(TypeNodeProvider.Instance, context);

        // Determine the effective nullable default: method overrides type
        byte methodContext = NullabilityReader.GetNullableContext(reader, method.GetCustomAttributes());
        byte nullableDefault = methodContext != 0 ? methodContext : typeNullableContext;

        // Apply nullability to return type
        var paramHandles = method.GetParameters();
        var returnBytes = NullabilityReader.GetParameterNullableBytes(reader, paramHandles, 0);
        int pos = 0;
        treeSignature.ReturnType.ApplyNullability(returnBytes, ref pos, nullableDefault);

        // Build parameter list with nullability
        var paramTypes = treeSignature.ParameterTypes;

        List<string> parameters = [];
        for (int i = 0; i < paramTypes.Length; i++)
        {
            // Apply nullability to this parameter's type tree
            var paramBytes = NullabilityReader.GetParameterNullableBytes(reader, paramHandles, i + 1);
            pos = 0;
            paramTypes[i].ApplyNullability(paramBytes, ref pos, nullableDefault);
            string type = paramTypes[i].Render();

            // Parameter handles may include return parameter at SequenceNumber 0
            // Actual parameters have SequenceNumber 1, 2, 3...
            var (paramName, isParams, refKind, hasDefault, defaultValue) = GetParameterInfo(reader, paramHandles, i + 1);
            paramName ??= $"arg{i}";

            var isByRef = type.StartsWith("ref ", StringComparison.Ordinal);
            if (isByRef)
            {
                type = type["ref ".Length..];
                refKind ??= "ref";
            }
            else
            {
                refKind = null;
            }

            var modifier = isParams ? "params" : refKind;
            var paramStr = modifier is null ? $"{type} {paramName}" : $"{modifier} {type} {paramName}";

            if (hasDefault)
            {
                paramStr += $" = {FormatDefaultValue(defaultValue, AcceptsNullDefault(paramTypes[i]))}";
            }

            parameters.Add(paramStr);
        }

        string paramStr2 = string.Join(", ", parameters);
        var methodName = context.MethodParameters.Count > 0
            ? $"{name}<{string.Join(", ", context.MethodParameters)}>"
            : name;
        return $"{treeSignature.ReturnType.Render()} {methodName}({paramStr2})";
    }

    private static (string? name, bool isParams, string? refKind, bool hasDefault, object? defaultValue) GetParameterInfo(
        MetadataReader reader, ParameterHandleCollection handles, int sequenceNumber)
    {
        foreach (var handle in handles)
        {
            var param = reader.GetParameter(handle);
            if (param.SequenceNumber == sequenceNumber)
            {
                string name = reader.GetString(param.Name);
                bool isParams = AttributeReader.HasAttribute(reader, param.GetCustomAttributes(),
                    "System.ParamArrayAttribute");
                string? refKind = (param.Attributes & System.Reflection.ParameterAttributes.Out) != 0
                    ? "out"
                    : (param.Attributes & System.Reflection.ParameterAttributes.In) != 0
                        ? "in"
                        : null;

                bool hasDefault = (param.Attributes & System.Reflection.ParameterAttributes.HasDefault) != 0;
                object? defaultValue = null;

                if (hasDefault)
                {
                    var constantHandle = param.GetDefaultValue();
                    if (!constantHandle.IsNil)
                    {
                        var constant = reader.GetConstant(constantHandle);
                        defaultValue = ReadConstantValue(reader, constant);
                    }
                }

                return (name, isParams, refKind, hasDefault, defaultValue);
            }
        }

        return (null, false, null, false, null);
    }

    private static object? ReadConstantValue(MetadataReader reader, Constant constant)
    {
        var blob = reader.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Boolean => blob.ReadBoolean(),
            ConstantTypeCode.Char => blob.ReadChar(),
            ConstantTypeCode.SByte => blob.ReadSByte(),
            ConstantTypeCode.Byte => blob.ReadByte(),
            ConstantTypeCode.Int16 => blob.ReadInt16(),
            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
            ConstantTypeCode.Int32 => blob.ReadInt32(),
            ConstantTypeCode.UInt32 => blob.ReadUInt32(),
            ConstantTypeCode.Int64 => blob.ReadInt64(),
            ConstantTypeCode.UInt64 => blob.ReadUInt64(),
            ConstantTypeCode.Single => blob.ReadSingle(),
            ConstantTypeCode.Double => blob.ReadDouble(),
            ConstantTypeCode.String => blob.ReadUTF16(blob.Length),
            ConstantTypeCode.NullReference => null,
            _ => null
        };
    }

    // `null` is a legal default only for a reference type or a Nullable<T> (a
    // value type that nonetheless accepts the `null` literal). A non-nullable
    // value type must spell its null constant `default`.
    private static bool AcceptsNullDefault(TypeNode node)
        => node.IsReferenceType
            || node.Render().StartsWith("System.Nullable<", StringComparison.Ordinal);

    private static string FormatDefaultValue(object? value, bool acceptsNullDefault)
    {
        // A null constant is `default(T)` for a non-nullable value-type parameter
        // (the only legal spelling — `T x = null` is CS1750), and a genuine `null`
        // for reference types and Nullable<T> (both accept `null` as a literal
        // default). value-vs-reference comes from the signature's element type
        // (ELEMENT_TYPE_VALUETYPE), already on the decoded type node.
        if (value == null)
            return acceptsNullDefault ? "null" : "default";

        return value switch
        {
            bool b => b ? "true" : "false",
            string s => $"\"{s}\"",
            char c => $"'{c}'",
            float f => f.ToString("G") + "f",
            double d => d.ToString("G"),
            _ => value.ToString() ?? "default"
        };
    }

    private static string GetPropertySignature(MetadataReader reader, TypeDefinition typeDef, PropertyDefinition prop, PropertyAccessors accessors, byte typeNullableContext, bool includeAll = false)
    {
        string name = reader.GetString(prop.Name);
        var context = GenericContext.ForType(reader, typeDef);
        var treeSignature = prop.DecodeSignature(TypeNodeProvider.Instance, context);

        // Apply nullability to the property type
        var propBytes = NullabilityReader.GetNullableBytes(reader, prop.GetCustomAttributes());
        int pos = 0;
        treeSignature.ReturnType.ApplyNullability(propBytes, ref pos, typeNullableContext);

        // Determine accessor visibility
        MethodAttributes getterAccess = 0;
        MethodAttributes setterAccess = 0;
        bool hasGetter = !accessors.Getter.IsNil;
        bool hasSetter = !accessors.Setter.IsNil;

        if (hasGetter)
        {
            var getter = reader.GetMethodDefinition(accessors.Getter);
            getterAccess = getter.Attributes & MethodAttributes.MemberAccessMask;
        }

        if (hasSetter)
        {
            var setter = reader.GetMethodDefinition(accessors.Setter);
            setterAccess = setter.Attributes & MethodAttributes.MemberAccessMask;
        }

        bool hasPublicGetter = hasGetter && getterAccess == MethodAttributes.Public;
        bool hasPublicSetter = hasSetter && setterAccess == MethodAttributes.Public;

        // Build accessor string
        string accessorStr;
        if (includeAll)
        {
            // Show explicit access levels for non-public accessors
            var getStr = hasGetter ? FormatAccessor("get", getterAccess, Math.Max((int)getterAccess, (int)setterAccess)) : null;
            var setStr = hasSetter ? FormatAccessor("set", setterAccess, Math.Max((int)getterAccess, (int)setterAccess)) : null;
            accessorStr = (getStr, setStr) switch
            {
                (not null, not null) => $"{{ {getStr}; {setStr}; }}",
                (not null, null) => $"{{ {getStr}; }}",
                (null, not null) => $"{{ {setStr}; }}",
                _ => "{ get; }"
            };
        }
        else
        {
            if (hasPublicGetter && hasPublicSetter)
                accessorStr = "{ get; set; }";
            else if (hasPublicGetter && hasSetter)
                accessorStr = "{ get; private set; }";
            else if (hasPublicGetter)
                accessorStr = "{ get; }";
            else if (hasPublicSetter)
                accessorStr = "{ set; }";
            else
                accessorStr = "{ get; }"; // Fallback
        }

        var requiredPrefix = AttributeReader.HasRequiredMemberAttribute(reader, prop.GetCustomAttributes())
            ? "required "
            : "";

        var paramHandles = hasGetter
            ? reader.GetMethodDefinition(accessors.Getter).GetParameters()
            : hasSetter
                ? reader.GetMethodDefinition(accessors.Setter).GetParameters()
                : default;
        var paramTypes = treeSignature.ParameterTypes;
        List<string> indexerParameters = [];
        for (var i = 0; i < paramTypes.Length; i++)
        {
            var paramBytes = NullabilityReader.GetParameterNullableBytes(reader, paramHandles, i + 1);
            pos = 0;
            paramTypes[i].ApplyNullability(paramBytes, ref pos, typeNullableContext);
            var paramType = paramTypes[i].Render();
            var (paramName, isParams, refKind, hasDefault, defaultValue) = GetParameterInfo(reader, paramHandles, i + 1);
            paramName ??= $"arg{i}";

            var isByRef = paramType.StartsWith("ref ", StringComparison.Ordinal);
            if (isByRef)
            {
                paramType = paramType["ref ".Length..];
                refKind ??= "ref";
            }
            else
            {
                refKind = null;
            }

            var modifier = isParams ? "params" : refKind;
            var parameter = modifier is null ? $"{paramType} {paramName}" : $"{modifier} {paramType} {paramName}";
            if (hasDefault)
                parameter += $" = {FormatDefaultValue(defaultValue, AcceptsNullDefault(paramTypes[i]))}";
            indexerParameters.Add(parameter);
        }

        if (indexerParameters.Count > 0)
            return $"{requiredPrefix}{treeSignature.ReturnType.Render()} this[{string.Join(", ", indexerParameters)}] {accessorStr}";

        return $"{requiredPrefix}{treeSignature.ReturnType.Render()} {name} {accessorStr}";
    }

    /// <summary>
    /// Formats a property accessor with its access level prefix when it differs from the property's overall level.
    /// </summary>
    private static string FormatAccessor(string kind, MethodAttributes access, int bestAccess)
    {
        if ((int)access == bestAccess)
            return kind;
        var prefix = GetAccessibility(access);
        return prefix != null ? $"{prefix} {kind}" : kind;
    }

    /// <summary>
    /// Gets the first parameter type for extension methods.
    /// </summary>
    private static string? GetFirstParameterType(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
    {
        var context = GenericContext.ForMethod(reader, typeDef, method);
        var signature = method.DecodeSignature(SignatureDecoder.Instance, context);
        return signature.ParameterTypes.Length > 0 ? signature.ParameterTypes[0] : null;
    }

    /// <summary>
    /// Checks if a method signature contains unsafe constructs (pointers). This
    /// catches members whose signature renders a pointer; members declared
    /// <c>unsafe</c> with no pointer in the signature are detected separately via
    /// <see cref="AttributeReader.HasRequiresUnsafeAttribute"/>.
    /// </summary>
    private static bool HasUnsafeSignature(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        // Check for pointer types (e.g., int*, void*, byte*)
        // and function pointers (delegate*)
        return signature.Contains('*');
    }

    /// <summary>
    /// Maps MethodAttributes access level to C# keyword. Returns null for public.
    /// </summary>
    private static string? GetAccessibility(MethodAttributes access) => access switch
    {
        MethodAttributes.Private => "private",
        MethodAttributes.FamANDAssem => "private protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected internal",
        _ => null // Public
    };

    /// <summary>
    /// Maps FieldAttributes access level to C# keyword. Returns null for public.
    /// </summary>
    private static string? GetFieldAccessibility(FieldAttributes access) => access switch
    {
        FieldAttributes.Private => "private",
        FieldAttributes.FamANDAssem => "private protected",
        FieldAttributes.Assembly => "internal",
        FieldAttributes.Family => "protected",
        FieldAttributes.FamORAssem => "protected internal",
        _ => null // Public
    };
}
