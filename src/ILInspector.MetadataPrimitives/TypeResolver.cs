using System.Reflection.Metadata;

namespace ILInspector.Metadata;

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
        return GetFullName(reader.GetString(typeRef.Namespace), reader.GetString(typeRef.Name));
    }

    /// <summary>
    /// Gets the type name from a TypeDefinition handle.
    /// </summary>
    public static string GetTypeNameFromDefinition(MetadataReader reader, TypeDefinitionHandle handle)
        => GetFullName(reader, reader.GetTypeDefinition(handle));

    /// <summary>
    /// Gets the type name from a TypeSpecification handle (generic instantiations).
    /// </summary>
    public static string GetTypeNameFromSpecification(MetadataReader reader, TypeSpecificationHandle handle, GenericContext? context = null)
    {
        var typeSpec = reader.GetTypeSpecification(handle);
        return typeSpec.DecodeSignature(SignatureDecoder.Instance, context);
    }

    /// <summary>
    /// Gets the full name of a type definition (Namespace.Name), qualifying a
    /// nested type through its declaring type (Outer.Inner).
    /// </summary>
    public static string GetFullName(MetadataReader reader, TypeDefinition typeDef)
    {
        var declaringType = typeDef.GetDeclaringType();
        if (!declaringType.IsNil)
            return $"{GetFullName(reader, reader.GetTypeDefinition(declaringType))}.{reader.GetString(typeDef.Name)}";
        return GetFullName(reader.GetString(typeDef.Namespace), reader.GetString(typeDef.Name));
    }

    /// <summary>
    /// Gets the full name of a type (Namespace.Name) from an ApiType-like structure.
    /// </summary>
    public static string GetFullName(string? ns, string name)
    {
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    /// <summary>
    /// Formats raw metadata type names for display by replacing CLR generic arity suffixes
    /// with readable type parameter placeholders.
    /// </summary>
    public static string FormatDisplayName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName) || !typeName.Contains('`'))
            return typeName;

        var result = new System.Text.StringBuilder(typeName.Length + 8);
        for (var i = 0; i < typeName.Length; i++)
        {
            if (typeName[i] != '`')
            {
                result.Append(typeName[i]);
                continue;
            }

            var digitStart = i + 1;
            var digitEnd = digitStart;
            while (digitEnd < typeName.Length && char.IsDigit(typeName[digitEnd]))
                digitEnd++;

            if (digitEnd == digitStart || !int.TryParse(typeName.AsSpan(digitStart, digitEnd - digitStart), out var arity) || arity <= 0)
            {
                result.Append(typeName[i]);
                continue;
            }

            result.Append('<');
            for (var parameterIndex = 1; parameterIndex <= arity; parameterIndex++)
            {
                if (parameterIndex > 1)
                    result.Append(", ");
                result.Append(arity == 1 ? "T" : $"T{parameterIndex}");
            }
            result.Append('>');
            i = digitEnd - 1;
        }

        return result.ToString();
    }
}
