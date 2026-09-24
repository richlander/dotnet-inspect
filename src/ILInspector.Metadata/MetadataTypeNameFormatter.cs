namespace ILInspector.Metadata;

/// <summary>
/// Metadata-safe type name display helpers: generic arity expansion, namespace
/// composition, and API-model full names. This deliberately stops short of C#
/// declaration printing; source-shaped signatures belong to the decompiler.
/// </summary>
public static class MetadataTypeNameFormatter
{
    public static string FormatFullName(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.DefinitionName is not { } definitionName)
            return FormatFullName(type.Namespace, type.Name, type.TypeParameters);

        return FormatFullName(
            definitionName,
            type.TypeParameters?.Select(
                static parameter => parameter.Name).ToArray(),
            type.IntroducedTypeParameterCounts);
    }

    public static string FormatFullName(
        MetadataTypeDefinitionName name,
        IReadOnlyList<string>? typeParameterNames = null,
        IReadOnlyList<int>? introducedTypeParameterCounts = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        string displayName = FormatGenericTypeName(
            name.Segments,
            typeParameterNames,
            introducedTypeParameterCounts);
        return name.Namespace.Length == 0
            ? displayName
            : $"{name.Namespace}.{displayName}";
    }

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

    static string FormatGenericTypeName(
        IReadOnlyList<string> metadataNameSegments,
        IReadOnlyList<string>? typeParameterNames,
        IReadOnlyList<int>? introducedTypeParameterCounts)
    {
        if (typeParameterNames is null
            || introducedTypeParameterCounts is null
            || introducedTypeParameterCounts.Count
                != metadataNameSegments.Count
            || introducedTypeParameterCounts.Any(
                static count => count < 0)
            || introducedTypeParameterCounts.Sum(
                static count => (long)count)
                != typeParameterNames.Count)
        {
            return typeParameterNames is { Count: > 0 }
                ? TypeResolver.ApplyGenericArguments(
                    metadataNameSegments,
                    [.. typeParameterNames])
                : TypeResolver.FormatDisplayName(
                    metadataNameSegments);
        }

        int parameterIndex = 0;
        var parts = new string[metadataNameSegments.Count];
        for (int segmentIndex = 0;
            segmentIndex < metadataNameSegments.Count;
            segmentIndex++)
        {
            string segment = metadataNameSegments[segmentIndex];
            int introducedCount =
                introducedTypeParameterCounts[segmentIndex];
            string display = MetadataNameArity.OfSegment(segment)
                    == introducedCount
                ? MetadataNameArity.StripFromSegment(segment)
                : segment;
            if (introducedCount > 0)
            {
                display += $"<{string.Join(
                    ", ",
                    typeParameterNames
                        .Skip(parameterIndex)
                        .Take(introducedCount))}>";
                parameterIndex += introducedCount;
            }
            parts[segmentIndex] = display;
        }
        return string.Join('.', parts);
    }
}
