using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Composes Analysis type identities with API-surface declarations without
/// relying on rendered names.
/// </summary>
public static class AnalysisApiCorrespondence
{
    public static bool IsSameType(TypeRef typeRef, ApiType type)
    {
        ArgumentNullException.ThrowIfNull(typeRef);
        ArgumentNullException.ThrowIfNull(type);
        if (typeRef.Kind != TypeRefKind.Definition)
            return false;

        if (typeRef.Resolution?.Type is { } referenceName
            && type.DefinitionName is { } definitionName)
        {
            return referenceName == definitionName;
        }

        if (!string.Equals(
                typeRef.Namespace,
                type.Namespace ?? "",
                StringComparison.Ordinal))
        {
            return false;
        }

        return type.MetadataName is { } metadataName
            ? string.Equals(typeRef.Name, metadataName, StringComparison.Ordinal)
            : string.Equals(
                typeRef.Name.Replace('+', '.'),
                type.Name,
                StringComparison.Ordinal);
    }
}
