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

            // Get interfaces
            var interfaces = typeDef.GetInterfaceImplementations();
            if (interfaces.Count > 0)
            {
                apiType.Interfaces = [];
                foreach (var ifaceHandle in interfaces)
                {
                    var iface = reader.GetInterfaceImplementation(ifaceHandle);
                    string? ifaceName = GetTypeName(reader, iface.Interface);
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

                var member = new ApiMember
                {
                    Name = methodName,
                    Kind = methodName == ".ctor" ? "constructor" : "method",
                    IsStatic = (method.Attributes & MethodAttributes.Static) != 0,
                    IsVirtual = (method.Attributes & MethodAttributes.Virtual) != 0,
                    IsAbstract = (method.Attributes & MethodAttributes.Abstract) != 0,
                    Signature = GetMethodSignature(reader, typeDef, method)
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
                    Signature = GetPropertySignature(reader, typeDef, prop)
                };

                apiType.Members.Add(member);
                surface.PublicPropertyCount++;
            }

            // Fields (only public non-backing fields)
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

        return surface;
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

    private static string? GetTypeName(MetadataReader reader, EntityHandle handle)
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
            return "(generic)";
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

    private static string GetPropertySignature(MetadataReader reader, TypeDefinition typeDef, PropertyDefinition prop)
    {
        string name = reader.GetString(prop.Name);
        var context = GenericContext.ForType(reader, typeDef);
        var signature = prop.DecodeSignature(new SignatureTypeProvider(), context);
        return $"{signature.ReturnType} {name}";
    }
}
