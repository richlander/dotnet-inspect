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

        string displayName = FormatGenericTypeName(
            definitionName.Segments,
            type.TypeParameters,
            type.IntroducedTypeParameterCounts);
        return definitionName.Namespace.Length == 0
            ? displayName
            : $"{definitionName.Namespace}.{displayName}";
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
        IReadOnlyList<TypeParameter>? typeParameters,
        IReadOnlyList<int>? introducedTypeParameterCounts)
    {
        if (typeParameters is null
            || introducedTypeParameterCounts is null
            || introducedTypeParameterCounts.Count
                != metadataNameSegments.Count
            || introducedTypeParameterCounts.Any(
                static count => count < 0)
            || introducedTypeParameterCounts.Sum(
                static count => (long)count)
                != typeParameters.Count)
        {
            return typeParameters is { Count: > 0 }
                ? TypeResolver.ApplyGenericArguments(
                    metadataNameSegments,
                    typeParameters.Select(
                        parameter => parameter.Name).ToArray())
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
                    typeParameters
                        .Skip(parameterIndex)
                        .Take(introducedCount)
                        .Select(parameter => parameter.Name))}>";
                parameterIndex += introducedCount;
            }
            parts[segmentIndex] = display;
        }
        return string.Join('.', parts);
    }
}
