using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Sections;

/// <summary>
/// The section names of the <c>@Metadata</c> lens: one section per ECMA-335 table the
/// projection models, plus a single <c>Metadata: Image</c> section for the image-level facts
/// (stream sizes, table bitmask, metadata version) that are not expressible as table rows.
///
/// The table names are *derived* from <see cref="MetadataTableProjector.ProjectedTables"/>
/// rather than restated, so a table added to or removed from the projector moves its section
/// with it and neither a stale nor a missing entry is possible. That derivation is the whole
/// point of this type: the section list, the category membership, and the render-time table
/// lookup all read the same array.
/// </summary>
public static class MetadataSectionNames
{
    /// <summary>
    /// The prefix every metadata section name carries. Matches the existing
    /// <c>"Performance: "</c> convention, so <c>-S "Metadata: TypeRef"</c> needs no parser change.
    /// </summary>
    public const string Prefix = "Metadata: ";

    /// <summary>
    /// Image-level metadata facts: metadata version, heap sizes, and which tables are present.
    /// These are not rows, so they cannot live in a per-table section.
    /// </summary>
    public const string Image = Prefix + "Image";

    /// <summary>
    /// One section name per projected table, in ECMA-335 table order — the same order
    /// <see cref="MetadataTableProjector.ProjectedTables"/> declares, so rendered sections follow
    /// table order rather than an independent list that could drift out of it.
    /// </summary>
    public static ImmutableArray<string> Tables { get; } =
        [.. MetadataTableProjector.ProjectedTables.Select(static index => Prefix + index)];

    /// <summary>
    /// Every section in the <c>@Metadata</c> category: the image overview first, then the tables.
    /// This is the category membership list and the registration list, so a table can never be
    /// registered without being reachable through the category door.
    /// </summary>
    public static ImmutableArray<string> All { get; } = [Image, .. Tables];

    static readonly ImmutableDictionary<string, TableIndex> ByName =
        MetadataTableProjector.ProjectedTables.ToImmutableDictionary(
            static index => Prefix + index,
            static index => index,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The section name that renders <paramref name="table"/>.</summary>
    public static string ForTable(TableIndex table) => Prefix + table;

    /// <summary>
    /// Resolves a section name to the table it renders. Returns <see langword="false"/> for
    /// <see cref="Image"/> and for any non-metadata section, so a caller cannot mistake the
    /// image overview for a table.
    /// </summary>
    public static bool TryGetTable(string section, out TableIndex table)
        => ByName.TryGetValue(section, out table);

    /// <summary>
    /// True when <paramref name="section"/> is any section of this lens, including
    /// <see cref="Image"/>. Used to route a selection to the metadata render path.
    /// </summary>
    public static bool IsMetadataSection(string section)
        => string.Equals(section, Image, StringComparison.OrdinalIgnoreCase)
            || ByName.ContainsKey(section);

    /// <summary>
    /// The tables selected by <paramref name="sections"/>, in table order. Sections that are not
    /// table sections (including <see cref="Image"/>) are skipped.
    /// </summary>
    public static ImmutableArray<TableIndex> TablesFor(IEnumerable<string> sections)
    {
        var selected = sections
            .Where(static s => ByName.ContainsKey(s))
            .Select(static s => ByName[s])
            .ToHashSet();

        return [.. MetadataTableProjector.ProjectedTables.Where(selected.Contains)];
    }

    /// <summary>
    /// The column names <paramref name="section"/> renders, or an empty array when the section is
    /// not a metadata table section. Read from
    /// <see cref="MetadataTableProjector.ColumnsFor(TableIndex)"/> so the schema a caller
    /// discovers and projects against is the one the renderer actually emits.
    /// </summary>
    public static ImmutableArray<string> ColumnsFor(string section)
        => TryGetTable(section, out var table)
            ? [RowIdColumn, .. MetadataTableProjector.ColumnsFor(table).Select(static c => c.Name)]
            : [];

    /// <summary>
    /// The name of the leading row-id column every table section carries. It is not an ECMA-335
    /// column — it is the row's own index — so the projector does not declare it, but it is a real
    /// rendered column and must appear in the schema like any other.
    /// </summary>
    public const string RowIdColumn = "Rid";

    /// <summary>
    /// Registers this lens's sections and their columns into <paramref name="schema"/>.
    ///
    /// Metadata sections are not attributed view properties, so they are absent from the Markout
    /// schema; without this, <c>-D "Metadata: TypeRef"</c> lists nothing and <c>--columns</c>
    /// rejects every name. Registering here keeps one source of column names for discovery,
    /// projection validation, and rendering.
    /// </summary>
    public static DocumentSchema AugmentSchema(DocumentSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        schema.Add(Image, "column", "Property", "Value");
        foreach (var table in MetadataTableProjector.ProjectedTables)
        {
            var name = ForTable(table);
            schema.Add(name, "column", [.. ColumnsFor(name)]);
        }

        return schema;
    }
}
