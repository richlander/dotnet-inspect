using System.Reflection.Metadata;

namespace DotnetInspector.Metadata;

/// <summary>
/// Resolves type names from metadata handles.
/// </summary>
public static class TypeResolver
{
    /// <summary>
    /// Gets the fully qualified type name from an entity handle.
    /// Handles TypeReference, TypeDefinition, and TypeSpecification.
    /// </summary>
    public static string? GetTypeName(MetadataReader reader, EntityHandle handle, GenericContext? context = null)
    {
        if (handle.IsNil)
            return null;

        return handle.Kind switch
        {
            HandleKind.TypeReference => GetTypeNameFromReference(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeDefinition => GetTypeNameFromDefinition(reader, (TypeDefinitionHandle)handle),
            HandleKind.TypeSpecification => GetTypeNameFromSpecification(reader, (TypeSpecificationHandle)handle, context),
            _ => null
        };
    }

    /// <summary>
    /// Gets the type name from a TypeReference handle.
    /// </summary>
    public static string GetTypeNameFromReference(MetadataReader reader, TypeReferenceHandle handle)
    {
        var typeRef = reader.GetTypeReference(handle);
        string ns = reader.GetString(typeRef.Namespace);
        string name = reader.GetString(typeRef.Name);
        return GetFullName(ns, name);
    }

    /// <summary>
    /// Gets the type name from a TypeDefinition handle.
    /// </summary>
    public static string GetTypeNameFromDefinition(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var typeDef = reader.GetTypeDefinition(handle);
        string ns = reader.GetString(typeDef.Namespace);
        string name = reader.GetString(typeDef.Name);
        return GetFullName(ns, name);
    }

    /// <summary>
    /// Gets the type name from a TypeSpecification handle (generic instantiations).
    /// </summary>
    public static string GetTypeNameFromSpecification(MetadataReader reader, TypeSpecificationHandle handle, GenericContext? context = null)
    {
        var typeSpec = reader.GetTypeSpecification(handle);
        return typeSpec.DecodeSignature(SignatureDecoder.Instance, context);
    }

    /// <summary>
    /// Gets the full name of a type definition (Namespace.Name).
    /// </summary>
    public static string GetFullName(MetadataReader reader, TypeDefinition typeDef)
    {
        string ns = reader.GetString(typeDef.Namespace);
        string name = reader.GetString(typeDef.Name);
        return GetFullName(ns, name);
    }

    /// <summary>
    /// Gets the full name of a type (Namespace.Name) from an ApiType-like structure.
    /// </summary>
    public static string GetFullName(string? ns, string name)
    {
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }
}
