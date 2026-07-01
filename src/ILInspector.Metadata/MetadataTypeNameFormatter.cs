namespace ILInspector.Metadata;

/// <summary>
/// Metadata-safe type name display helpers: generic arity expansion, namespace
/// composition, and API-model full names. This deliberately stops short of C#
/// declaration printing; source-shaped signatures belong to the decompiler.
/// </summary>
public static class MetadataTypeNameFormatter
{
    public static string FormatFullName(ApiType type)
        => FormatFullName(type.Namespace, type.Name, type.TypeParameters);

    public static string FormatFullName(string? ns, string name, IReadOnlyList<TypeParameter>? typeParameters = null)
    {
        var displayName = FormatGenericTypeName(name, typeParameters);
        return string.IsNullOrEmpty(ns) ? displayName : $"{ns}.{displayName}";
    }

    public static string FormatGenericTypeName(string name, IReadOnlyList<TypeParameter>? typeParameters = null)
    {
        if (!name.Contains('`', StringComparison.Ordinal))
            return name;

        return typeParameters is { Count: > 0 }
            ? TypeResolver.ApplyGenericArguments(name, typeParameters.Select(tp => tp.Name).ToArray())
            : TypeResolver.FormatDisplayName(name);
    }
}
