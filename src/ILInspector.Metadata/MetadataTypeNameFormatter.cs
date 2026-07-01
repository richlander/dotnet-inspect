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

        var typeParameterIndex = 0;
        var segments = name.Split('.');
        for (var i = 0; i < segments.Length; i++)
            segments[i] = FormatGenericTypeNameSegment(segments[i], typeParameters, ref typeParameterIndex);

        return string.Join(".", segments);
    }

    static string FormatGenericTypeNameSegment(
        string name,
        IReadOnlyList<TypeParameter>? typeParameters,
        ref int typeParameterIndex)
    {
        int backtickIndex = name.IndexOf('`');
        if (backtickIndex < 0)
            return name;

        var baseName = name[..backtickIndex];
        if (!int.TryParse(name[(backtickIndex + 1)..], out int arity) || arity <= 0)
            return name;

        if (typeParameters is { Count: > 0 } && typeParameterIndex + arity <= typeParameters.Count)
        {
            var names = typeParameters
                .Skip(typeParameterIndex)
                .Take(arity)
                .Select(tp => tp.Name);
            typeParameterIndex += arity;
            return $"{baseName}<{string.Join(", ", names)}>";
        }

        var fallbackNames = arity == 1
            ? "T"
            : string.Join(", ", Enumerable.Range(1, arity).Select(i => $"T{i}"));
        return $"{baseName}<{fallbackNames}>";
    }
}
