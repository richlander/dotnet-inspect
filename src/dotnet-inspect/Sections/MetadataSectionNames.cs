using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata.Ecma335;
using DotnetInspector.MetadataRendering;
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
    /// The coordinate-scoped section: the single heap value <c>--heap</c> names.
    ///
    /// Like the IL-offset sections, this one exists only when its coordinate does. Without
    /// <c>--heap</c> there is no value to show, so the section is inapplicable and <c>-D</c> does
    /// not list it; a section listed with nothing to render would advertise a view the command
    /// cannot produce.
    /// </summary>
    public const string Heap = Prefix + "Heap";

    /// <summary>
    /// One section name per heap, spelled with the ECMA-335 stream name
    /// (<c>Metadata: #Strings</c>), so the section a user selects and the coordinate they pass to
    /// <c>--heap</c> name the heap the same way.
    /// </summary>
    public static ImmutableArray<string> Heaps { get; } =
        [.. MetadataHeapCoordinate.Heaps.Select(static heap => Prefix + MetadataHeapCoordinate.StreamName(heap))];

    static readonly ImmutableDictionary<string, HeapKind> HeapsByName =
        MetadataHeapCoordinate.Heaps.ToImmutableDictionary(
            static heap => Prefix + MetadataHeapCoordinate.StreamName(heap),
            static heap => heap,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The section name that lists <paramref name="heap"/>'s entries.</summary>
    public static string ForHeap(HeapKind heap) => Prefix + MetadataHeapCoordinate.StreamName(heap);

    /// <summary>
    /// Resolves a section name to the heap it lists. Returns <see langword="false"/> for
    /// <see cref="Heap"/> — the coordinate section is one value, not a heap listing — and for
    /// every non-heap section.
    /// </summary>
    public static bool TryGetHeap(string section, out HeapKind heap)
        => HeapsByName.TryGetValue(section, out heap);

    /// <summary>
    /// The heaps selected by <paramref name="sections"/>, in stream-name order. Sections that are
    /// not heap listings are skipped.
    /// </summary>
    public static ImmutableArray<HeapKind> HeapsFor(IEnumerable<string> sections)
    {
        var selected = sections
            .Where(static s => HeapsByName.ContainsKey(s))
            .Select(static s => HeapsByName[s])
            .ToHashSet();

        return [.. MetadataHeapCoordinate.Heaps.Where(selected.Contains)];
    }

    /// <summary>
    /// One section name per projected table, in ECMA-335 table order — the same order
    /// <see cref="MetadataTableProjector.ProjectedTables"/> declares, so rendered sections follow
    /// table order rather than an independent list that could drift out of it.
    /// </summary>
    public static ImmutableArray<string> Tables { get; } =
        [.. MetadataTableProjector.ProjectedTables.Select(static index => Prefix + index)];

    /// <summary>
    /// Every section in the <c>@Metadata</c> category: the image overview first, then the heap
    /// coordinate section, the per-heap listings, and the tables.
    /// This is the category membership list and the registration list, so a table can never be
    /// registered without being reachable through the category door.
    /// </summary>
    public static ImmutableArray<string> All { get; } = [Image, Heap, .. Heaps, .. Tables];

    static readonly ImmutableDictionary<string, TableIndex> ByName =
        MetadataTableProjector.ProjectedTables.ToImmutableDictionary(
            static index => Prefix + index,
            static index => index,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The section name that renders <paramref name="table"/>.</summary>
    public static string ForTable(TableIndex table) => Prefix + table;

    /// <summary>
    /// The hex spelling of every projected table, derived from the same
    /// <see cref="MetadataTableProjector.ProjectedTables"/> array the canonical names come from.
    /// Deriving rather than restating is what keeps an <em>unprojected</em> index from becoming
    /// selectable by its hex spelling: a table absent from that array has no entry here either.
    /// </summary>
    static readonly ImmutableDictionary<TableIndex, string> HexAliases =
        MetadataTableProjector.ProjectedTables.ToImmutableDictionary(
            static index => index,
            static index => ForTable(index));

    /// <summary>
    /// Rewrites a hex table spelling to its canonical section name: <c>Metadata: 0x02</c> becomes
    /// <c>Metadata: TypeDef</c>. Anything that is not a hex spelling passes through unchanged, so
    /// this is safe to run over every selector.
    ///
    /// Rewriting the *input* — rather than registering a second section — is what keeps the two
    /// spellings one section. A hex alias registered as its own section would render its own
    /// heading, order independently, and count separately, which is exactly the "two
    /// different-looking sections" outcome this alias exists to avoid. For the same reason
    /// <see cref="Tables"/> and the catalog are untouched: the hex form is an input alias, not a
    /// second catalog entry.
    ///
    /// Hex is required to carry its <c>0x</c>, matching the <c>--heap</c> address rule: a bare
    /// <c>02</c> is not a table index here, because a suffix without the prefix is a table
    /// <em>name</em> and inferring a radix would let one spelling mean two things.
    /// </summary>
    public static bool TryResolveTableAlias(string section, out string canonical, out string? error)
    {
        ArgumentNullException.ThrowIfNull(section);

        canonical = section;
        error = null;

        if (!section.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        string suffix = section[Prefix.Length..].Trim();
        if (!suffix.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return true;

        string digits = suffix[2..];

        // Width is checked textually, not just numerically. A table index is one byte, so it is at
        // most two hex digits; `byte.TryParse` alone enforces the numeric range but not the width,
        // which accepts an eight-digit metadata token whose value happens to fit — `0x00000001` is
        // a Module *row* token and would otherwise resolve as table 0x01, TypeRef. Adversarial
        // review of #3510 found this: the original close-negative case, 0x02000015, was rejected
        // only because it overflowed, not because it was recognized as a token.
        if (digits.Length is 1 or 2
            && byte.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte index)
            && HexAliases.TryGetValue((TableIndex)index, out string? resolved))
        {
            canonical = resolved;
            return true;
        }

        error = $"\"{section}\" is not a projected metadata table. Projected tables: {ProjectedTableList}.";
        return false;
    }

    /// <summary>
    /// Every projected table as <c>0xNN Name</c>, for the rejection diagnostic. A caller who
    /// pasted a hex index is thinking in hex, so the remedy answers in both spellings at once.
    /// </summary>
    static string ProjectedTableList { get; } = string.Join(
        ", ",
        MetadataTableProjector.ProjectedTables.Select(
            static index => $"0x{(int)index:X2} {index}"));

    /// <summary>
    /// Resolves a section name to the table it renders. Returns <see langword="false"/> for
    /// <see cref="Image"/> and for any non-metadata section, so a caller cannot mistake the
    /// image overview for a table.
    ///
    /// This deliberately does <em>not</em> accept the hex spelling. Hex is resolved once, at the
    /// input boundary, by <see cref="TryResolveTableAlias"/>; by the time any lookup here runs the
    /// name is already canonical. Teaching this method hex as well would put alias resolution in
    /// two places and would make <see cref="IsMetadataSection"/> claim a name that selection
    /// resolution — which matches against the canonical catalog — would still reject.
    /// </summary>
    public static bool TryGetTable(string section, out TableIndex table)
        => ByName.TryGetValue(section, out table);

    /// <summary>
    /// True when <paramref name="section"/> is any section of this lens, including
    /// <see cref="Image"/>. Used to route a selection to the metadata render path.
    /// </summary>
    public static bool IsMetadataSection(string section)
        => string.Equals(section, Image, StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, Heap, StringComparison.OrdinalIgnoreCase)
            || HeapsByName.ContainsKey(section)
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
            : string.Equals(section, Heap, StringComparison.OrdinalIgnoreCase)
                ? [.. MetadataProjectionRenderer.HeapValueColumns]
                : TryGetHeap(section, out _)
                    ? [.. MetadataProjectionRenderer.HeapEntryColumns]
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
        schema.Add(Heap, "column", [.. ColumnsFor(Heap)]);
        foreach (var heap in MetadataHeapCoordinate.Heaps)
        {
            var heapSection = ForHeap(heap);
            schema.Add(heapSection, "column", [.. ColumnsFor(heapSection)]);
        }

        foreach (var table in MetadataTableProjector.ProjectedTables)
        {
            var name = ForTable(table);
            schema.Add(name, "column", [.. ColumnsFor(name)]);
        }

        return schema;
    }
}
