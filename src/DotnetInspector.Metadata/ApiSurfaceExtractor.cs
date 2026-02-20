using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

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

            string typeName = reader.GetString(typeDef.Name);

            // Skip compiler-generated types
            if (typeName.StartsWith("<") || typeName.StartsWith("__"))
                continue;

            // Skip EditorBrowsable(Never) and Obsolete types unless --all
            if (!includeAll && AttributeReader.HasHiddenAttribute(reader, typeDef.GetCustomAttributes()))
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

            // Get public members
            if (!typesOnly)
            {
            apiType.Members = [];

            // Methods
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                    continue;

                string methodName = reader.GetString(method.Name);

                // Skip property accessors and event accessors
                if (methodName.StartsWith("get_") || methodName.StartsWith("set_") ||
                    methodName.StartsWith("add_") || methodName.StartsWith("remove_"))
                    continue;

                // Skip EditorBrowsable(Never) and Obsolete methods unless --all
                if (!includeAll && AttributeReader.HasHiddenAttribute(reader, method.GetCustomAttributes()))
                    continue;

                var signature = GetMethodSignature(reader, typeDef, method, typeNullableContext);
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

                // Check for extension method
                if (isExtensionClass && member.IsStatic && AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes()))
                {
                    member.IsExtension = true;
                    member.ExtendedType = GetFirstParameterType(reader, typeDef, method);
                }

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
                    isPublicProp = (getter.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
                }
                if (!isPublicProp && !accessors.Setter.IsNil)
                {
                    var setter = reader.GetMethodDefinition(accessors.Setter);
                    isPublicProp = (setter.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
                }

                if (!isPublicProp)
                    continue;

                // Skip EditorBrowsable(Never) and Obsolete properties unless --all
                if (!includeAll && AttributeReader.HasHiddenAttribute(reader, prop.GetCustomAttributes()))
                    continue;

                var member = new ApiMember
                {
                    Name = reader.GetString(prop.Name),
                    Kind = "property",
                    Signature = GetPropertySignature(reader, typeDef, prop, accessors, typeNullableContext)
                };

                apiType.Members.Add(member);
                surface.PublicPropertyCount++;
            }

            // Fields (only public non-backing fields)
            bool isEnum = apiType.Kind == "enum";
            foreach (var fieldHandle in typeDef.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public)
                    continue;

                string fieldName = reader.GetString(field.Name);
                if (fieldName.StartsWith("<"))
                    continue; // Skip backing fields

                // Skip EditorBrowsable(Never) and Obsolete fields unless --all
                if (!includeAll && AttributeReader.HasHiddenAttribute(reader, field.GetCustomAttributes()))
                    continue;

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
                if ((adder.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                    continue;

                // Skip EditorBrowsable(Never) and Obsolete events unless --all
                if (!includeAll && AttributeReader.HasHiddenAttribute(reader, evt.GetCustomAttributes()))
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
            } // end if (!typesOnly)

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
            var (paramName, isParams, hasDefault, defaultValue) = GetParameterInfo(reader, paramHandles, i + 1);
            paramName ??= $"arg{i}";

            var paramStr = isParams ? $"params {type} {paramName}" : $"{type} {paramName}";

            if (hasDefault)
            {
                paramStr += $" = {FormatDefaultValue(defaultValue, type)}";
            }

            parameters.Add(paramStr);
        }

        string paramStr2 = string.Join(", ", parameters);
        return $"{treeSignature.ReturnType.Render()} {name}({paramStr2})";
    }

    private static (string? name, bool isParams, bool hasDefault, object? defaultValue) GetParameterInfo(
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

                return (name, isParams, hasDefault, defaultValue);
            }
        }
        return (null, false, false, null);
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

    private static string FormatDefaultValue(object? value, string typeName)
    {
        if (value == null)
            return "null";

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

    private static string GetPropertySignature(MetadataReader reader, TypeDefinition typeDef, PropertyDefinition prop, PropertyAccessors accessors, byte typeNullableContext)
    {
        string name = reader.GetString(prop.Name);
        var context = GenericContext.ForType(reader, typeDef);
        var treeSignature = prop.DecodeSignature(TypeNodeProvider.Instance, context);

        // Apply nullability to the property type
        var propBytes = NullabilityReader.GetNullableBytes(reader, prop.GetCustomAttributes());
        int pos = 0;
        treeSignature.ReturnType.ApplyNullability(propBytes, ref pos, typeNullableContext);

        // Determine accessor visibility
        bool hasPublicGetter = false;
        bool hasPublicSetter = false;
        bool hasPrivateSetter = false;

        if (!accessors.Getter.IsNil)
        {
            var getter = reader.GetMethodDefinition(accessors.Getter);
            hasPublicGetter = (getter.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
        }

        if (!accessors.Setter.IsNil)
        {
            var setter = reader.GetMethodDefinition(accessors.Setter);
            hasPublicSetter = (setter.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
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

        return $"{treeSignature.ReturnType.Render()} {name} {accessorStr}";
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
