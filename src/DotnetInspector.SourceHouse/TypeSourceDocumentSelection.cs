using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse;

/// <summary>Selects the conventional default without reordering producer evidence.</summary>
public static class TypeSourceDocumentSelection
{
    public static SourceLinkResolver.TypeSourceDocument? SelectDefault(
        SourceLinkResolver.TypeSourceInfo mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        string primaryName = $"{mapping.Type.Segments[^1]}.cs";
        return mapping.Documents.FirstOrDefault(file => Path.GetFileName(file.FilePath)
                .Equals(primaryName, StringComparison.OrdinalIgnoreCase))
            ?? mapping.Documents.MinBy(file => Path.GetFileName(file.FilePath).Length);
    }
}
