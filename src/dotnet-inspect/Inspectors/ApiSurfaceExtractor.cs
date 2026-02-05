using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Extracts public API surface from assemblies.
/// </summary>
public static class ApiSurfaceExtractor
{
    public static ApiSurface Extract(PEReader peReader, bool includeAll = false)
    {
        var surface = new ApiSurface();
        var reader = peReader.GetMetadataReader();

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            var attributes = typeDef.Attributes;

            // Only include public types
            bool isPublic = (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public ||
                            (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic;

            if (!isPublic)
                continue;

            string typeName = reader.GetString(typeDef.Name);

            // Skip compiler-generated types
            if (typeName.StartsWith("<") || typeName.StartsWith("__"))
                continue;

            // Skip EditorBrowsable(Never) and Obsolete types unless --all
            if (!includeAll && HasHiddenAttribute(reader, typeDef.GetCustomAttributes()))
                continue;

            var apiType = new ApiType
            {
                Namespace = reader.GetString(typeDef.Namespace),
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
                string? baseTypeName = GetTypeName(reader, typeDef.BaseType);
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
                        string? constraintTypeName = GetTypeName(reader, constraint.Type, typeContext);
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
                    string? ifaceName = GetTypeName(reader, iface.Interface, typeContext);
                    if (ifaceName != null)
                        apiType.Interfaces.Add(ifaceName);
                }
            }

            // Get public members
            apiType.Members = [];

            // Methods
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.Public) == 0)
                    continue;

                string methodName = reader.GetString(method.Name);

                // Skip property accessors and event accessors
                if (methodName.StartsWith("get_") || methodName.StartsWith("set_") ||
                    methodName.StartsWith("add_") || methodName.StartsWith("remove_"))
                    continue;

                // Skip EditorBrowsable(Never) and Obsolete methods unless --all
                if (!includeAll && HasHiddenAttribute(reader, method.GetCustomAttributes()))
                    continue;

                var signature = GetMethodSignature(reader, typeDef, method);
                var member = new ApiMember
                {
                    Name = methodName,
                    Kind = methodName == ".ctor" ? "constructor" : "method",
                    IsStatic = (method.Attributes & MethodAttributes.Static) != 0,
                    IsVirtual = (method.Attributes & MethodAttributes.Virtual) != 0,
                    IsAbstract = (method.Attributes & MethodAttributes.Abstract) != 0,
                    Signature = signature,
                    IsUnsafe = HasUnsafeSignature(signature)
                };

                apiType.Members.Add(member);
                surface.PublicMethodCount++;
            }

            // Properties
            foreach (var propHandle in typeDef.GetProperties())
            {
                var prop = reader.GetPropertyDefinition(propHandle);
                var accessors = prop.GetAccessors();

                // Check if any accessor is public
                bool isPublicProp = false;
                if (!accessors.Getter.IsNil)
                {
                    var getter = reader.GetMethodDefinition(accessors.Getter);
                    isPublicProp = (getter.Attributes & MethodAttributes.Public) != 0;
                }
                if (!isPublicProp && !accessors.Setter.IsNil)
                {
                    var setter = reader.GetMethodDefinition(accessors.Setter);
                    isPublicProp = (setter.Attributes & MethodAttributes.Public) != 0;
                }

                if (!isPublicProp)
                    continue;

                // Skip EditorBrowsable(Never) and Obsolete properties unless --all
                if (!includeAll && HasHiddenAttribute(reader, prop.GetCustomAttributes()))
                    continue;

                var member = new ApiMember
                {
                    Name = reader.GetString(prop.Name),
                    Kind = "property",
                    Signature = GetPropertySignature(reader, typeDef, prop, accessors)
                };

                apiType.Members.Add(member);
                surface.PublicPropertyCount++;
            }

            // Fields (only public non-backing fields)
            bool isEnum = apiType.Kind == "enum";
            foreach (var fieldHandle in typeDef.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.Public) == 0)
                    continue;

                string fieldName = reader.GetString(field.Name);
                if (fieldName.StartsWith("<"))
                    continue; // Skip backing fields

                // Skip EditorBrowsable(Never) and Obsolete fields unless --all
                if (!includeAll && HasHiddenAttribute(reader, field.GetCustomAttributes()))
                    continue;

                var member = new ApiMember
                {
                    Name = fieldName,
                    Kind = "field",
                    IsStatic = (field.Attributes & FieldAttributes.Static) != 0
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

                // Check if adder is public
                if (accessors.Adder.IsNil)
                    continue;

                var adder = reader.GetMethodDefinition(accessors.Adder);
                if ((adder.Attributes & MethodAttributes.Public) == 0)
                    continue;

                // Skip EditorBrowsable(Never) and Obsolete events unless --all
                if (!includeAll && HasHiddenAttribute(reader, evt.GetCustomAttributes()))
                    continue;

                var member = new ApiMember
                {
                    Name = reader.GetString(evt.Name),
                    Kind = "event",
                    IsStatic = (adder.Attributes & MethodAttributes.Static) != 0
                };

                apiType.Members.Add(member);
                surface.PublicEventCount++;
            }

            surface.Types.Add(apiType);
            surface.PublicTypeCount++;
        }

        // Extract type forwarders (ExportedTypes that are forwarded to other assemblies)
        foreach (var exportedTypeHandle in reader.ExportedTypes)
        {
            var exportedType = reader.GetExportedType(exportedTypeHandle);
            
            // Type forwarders have IsForwarder flag set
            if (!exportedType.IsForwarder)
                continue;

            var typeName = reader.GetString(exportedType.Name);
            var ns = reader.GetString(exportedType.Namespace);
            var fullName = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}";

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

    /// <summary>
    /// Populates DerivedTypes for a specific type by scanning all types in the surface.
    /// </summary>
    public static void PopulateDerivedTypes(ApiSurface surface, ApiType targetType)
    {
        var fullName = string.IsNullOrEmpty(targetType.Namespace) 
            ? targetType.Name 
            : $"{targetType.Namespace}.{targetType.Name}";

        var derivedTypes = new List<string>();

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

    /// <summary>
    /// Checks if the member has EditorBrowsable(Never) or Obsolete attribute.
    /// </summary>
    private static bool HasHiddenAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);

            if (attrTypeName == "System.ComponentModel.EditorBrowsableAttribute")
            {
                // Check if the value is EditorBrowsableState.Never (value = 1)
                var value = reader.GetBlobBytes(attr.Value);
                // Attribute blob format: 2-byte prolog (0x0001), then the enum value as int32
                if (value.Length >= 6)
                {
                    int enumValue = value[2] | (value[3] << 8) | (value[4] << 16) | (value[5] << 24);
                    if (enumValue == 1) // EditorBrowsableState.Never
                        return true;
                }
            }
            else if (attrTypeName == "System.ObsoleteAttribute")
            {
                return true;
            }
        }
        return false;
    }

    private static string? GetAttributeTypeName(MetadataReader reader, EntityHandle constructorHandle)
    {
        if (constructorHandle.Kind == HandleKind.MemberReference)
        {
            var memberRef = reader.GetMemberReference((MemberReferenceHandle)constructorHandle);
            return GetTypeName(reader, memberRef.Parent);
        }
        else if (constructorHandle.Kind == HandleKind.MethodDefinition)
        {
            var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)constructorHandle);
            var typeDef = reader.GetTypeDefinition(methodDef.GetDeclaringType());
            string ns = reader.GetString(typeDef.Namespace);
            string name = reader.GetString(typeDef.Name);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }
        return null;
    }

    private static string? GetTypeName(MetadataReader reader, EntityHandle handle, GenericContext? context = null)
    {
        if (handle.Kind == HandleKind.TypeReference)
        {
            var typeRef = reader.GetTypeReference((TypeReferenceHandle)handle);
            string ns = reader.GetString(typeRef.Namespace);
            string name = reader.GetString(typeRef.Name);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }
        else if (handle.Kind == HandleKind.TypeDefinition)
        {
            var typeDef = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            string ns = reader.GetString(typeDef.Namespace);
            string name = reader.GetString(typeDef.Name);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }
        else if (handle.Kind == HandleKind.TypeSpecification)
        {
            // Decode generic type specifications (e.g., IList<T>, IEnumerable<T>)
            var typeSpec = reader.GetTypeSpecification((TypeSpecificationHandle)handle);
            return typeSpec.DecodeSignature(new SignatureTypeProvider(), context);
        }
        return null;
    }

    private static string GetMethodSignature(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
    {
        string name = reader.GetString(method.Name);
        var context = GenericContext.ForMethod(reader, typeDef, method);
        var signature = method.DecodeSignature(new SignatureTypeProvider(), context);

        // Get parameter names from metadata
        var paramHandles = method.GetParameters().ToList();
        var paramTypes = signature.ParameterTypes;

        var parameters = new List<string>();
        for (int i = 0; i < paramTypes.Length; i++)
        {
            string type = paramTypes[i];
            // Parameter handles may include return parameter at SequenceNumber 0
            // Actual parameters have SequenceNumber 1, 2, 3...
            string paramName = GetParameterName(reader, paramHandles, i + 1) ?? $"arg{i}";
            parameters.Add($"{type} {paramName}");
        }

        string paramStr = string.Join(", ", parameters);
        return $"{signature.ReturnType} {name}({paramStr})";
    }

    private static string? GetParameterName(MetadataReader reader, List<ParameterHandle> handles, int sequenceNumber)
    {
        foreach (var handle in handles)
        {
            var param = reader.GetParameter(handle);
            if (param.SequenceNumber == sequenceNumber)
                return reader.GetString(param.Name);
        }
        return null;
    }

    private static string GetPropertySignature(MetadataReader reader, TypeDefinition typeDef, PropertyDefinition prop, PropertyAccessors accessors)
    {
        string name = reader.GetString(prop.Name);
        var context = GenericContext.ForType(reader, typeDef);
        var signature = prop.DecodeSignature(new SignatureTypeProvider(), context);

        // Determine accessor visibility
        bool hasPublicGetter = false;
        bool hasPublicSetter = false;
        bool hasPrivateSetter = false;

        if (!accessors.Getter.IsNil)
        {
            var getter = reader.GetMethodDefinition(accessors.Getter);
            hasPublicGetter = (getter.Attributes & MethodAttributes.Public) != 0;
        }

        if (!accessors.Setter.IsNil)
        {
            var setter = reader.GetMethodDefinition(accessors.Setter);
            hasPublicSetter = (setter.Attributes & MethodAttributes.Public) != 0;
            hasPrivateSetter = !hasPublicSetter;
        }

        // Build accessor string
        string accessorStr;
        if (hasPublicGetter && hasPublicSetter)
            accessorStr = "{ get; set; }";
        else if (hasPublicGetter && hasPrivateSetter)
            accessorStr = "{ get; private set; }";
        else if (hasPublicGetter)
            accessorStr = "{ get; }";
        else if (hasPublicSetter)
            accessorStr = "{ set; }";
        else
            accessorStr = "{ get; }"; // Fallback

        return $"{signature.ReturnType} {name} {accessorStr}";
    }

    /// <summary>
    /// Checks if a method signature contains unsafe constructs (pointers).
    /// </summary>
    private static bool HasUnsafeSignature(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        // Check for pointer types (e.g., int*, void*, byte*)
        // and function pointers (delegate*)
        return signature.Contains('*');
    }
}
