using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Metadata;

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

            // Get type's generic context for resolving interface type parameters
            var typeContext = Metadata.GenericContext.ForType(reader, typeDef);

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
                if (!includeAll && AttributeReader.HasHiddenAttribute(reader, method.GetCustomAttributes()))
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
                if (!includeAll && AttributeReader.HasHiddenAttribute(reader, prop.GetCustomAttributes()))
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
                if (!includeAll && AttributeReader.HasHiddenAttribute(reader, field.GetCustomAttributes()))
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
    /// Gets the first parameter type for extension methods.
    /// </summary>
    private static string? GetFirstParameterType(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
    {
        var context = GenericContext.ForMethod(reader, typeDef, method);
        var signature = method.DecodeSignature(new SignatureTypeProvider(), context);
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
