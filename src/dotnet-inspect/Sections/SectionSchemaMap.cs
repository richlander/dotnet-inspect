namespace DotnetInspector.Sections;

/// <summary>
/// Describes the items (fields or columns) within a section.
/// </summary>
public record SectionSchema(string[] ItemNames, string ItemKind);

/// <summary>
/// Maps section names to their item schemas.
/// Used by -D/--discover to show what fields or columns a section contains,
/// so the user knows which projection flag (--fields or --columns) to use.
/// </summary>
public sealed class SectionSchemaMap
{
    private readonly Dictionary<string, SectionSchema> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers items for a section. Kind is "field" or "column".
    /// </summary>
    public SectionSchemaMap Add(string sectionName, string itemKind, params string[] itemNames)
    {
        _entries[sectionName] = new SectionSchema(itemNames, itemKind);
        return this;
    }

    /// <summary>
    /// Returns the schema for a section, or null if not registered.
    /// </summary>
    public SectionSchema? GetSchema(string sectionName)
        => _entries.GetValueOrDefault(sectionName);

    /// <summary>
    /// Returns all registered section names.
    /// </summary>
    public IEnumerable<string> SectionNames => _entries.Keys;
}
