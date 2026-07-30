using DotnetInspector.MetadataRendering;
using DotnetInspector.Models;

namespace DotnetInspector.Sections;

/// <summary>
/// Registration of the <c>@Metadata</c> lens on the library pipeline: one section per projected
/// ECMA-335 table plus <c>Metadata: Image</c> for the image-level facts that are not rows.
///
/// The sections are generated from <see cref="MetadataSectionNames"/> rather than hand-declared as
/// one descriptor type each. A per-table descriptor type would restate the projector's table list
/// a second time, so a table added to the projector could silently gain no section; generating
/// them makes that impossible by construction.
/// </summary>
public static class MetadataSections
{
    /// <summary>
    /// Registers every metadata section and the <c>@Metadata</c> category door.
    ///
    /// Raw tables must never appear in a view the caller did not ask for. Two independent
    /// properties enforce that, and each was measured to be sufficient on its own — removing
    /// either alone leaves the sections suppressed, and only removing both makes them render:
    ///
    /// <list type="bullet">
    /// <item><see cref="SectionEntry{TModel}.ExplicitOnly"/>, so no verbosity ladder requests
    /// them.</item>
    /// <item><see cref="SectionCost.Unbounded"/>, which no verbosity auto-runs — not even
    /// <c>-v:d</c>.</item>
    /// </list>
    ///
    /// The gate is
    /// <c>CommandExecutionTests.MetadataLens_NoVerbosity_RendersAnyMetadataSection</c>, which
    /// walks the whole verbosity ladder plus <c>-S @All</c>. Because the two properties are
    /// redundant, that gate catches the loss of both but not of one; treat either as load-bearing.
    ///
    /// Members are also <see cref="SectionEntry{TModel}.ListedInCatalog"/> <c>= false</c> so
    /// seventeen mostly-empty table rows do not flood the top-level <c>-D</c> catalog; they stay
    /// visible under <c>-D @Metadata</c> and by exact name. That half is gated by
    /// <c>MetadataLens_BareDiscovery_ListsDoorWithoutMembers</c>.
    /// </summary>
    public static SectionPipeline<LibraryInspection> AddMetadataLens(
        this SectionPipeline<LibraryInspection> pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        pipeline.Add(new SectionEntry<LibraryInspection>
        {
            Name = MetadataSectionNames.Image,
            IsExpensive = false,
            ExplicitOnly = true,
            ListedInCatalog = false,
            // The image overview is a fixed set of facts, already gathered by the scanner, so it
            // is neither Verbose nor Unbounded like the table sections it introduces.
            SizeClass = SectionSizeClass.Fixed,
            Cost = SectionCost.NetworkFree,
            ScannerKey = LibrarySections.ScannerMetadata,
            HasExplicitApplicability = true,
            IsApplicable = HasMetadata,
            CanRender = HasMetadata,
        });

        // The coordinate-scoped section. Applicable exactly when --heap supplied a coordinate, so
        // it is listed by -D only then — the same discipline the IL-offset coordinate sections
        // follow, and for the same reason: a section with no coordinate has nothing to render, and
        // listing it would advertise a view the command cannot produce.
        pipeline.Add(new SectionEntry<LibraryInspection>
        {
            Name = MetadataSectionNames.Heap,
            IsExpensive = false,
            ExplicitOnly = true,
            ListedInCatalog = false,
            // One value at one address: a fixed shape, and cheap — the read is by address and
            // touches no table.
            SizeClass = SectionSizeClass.Fixed,
            Cost = SectionCost.NetworkFree,
            ScannerKey = LibrarySections.ScannerMetadata,
            HasExplicitApplicability = true,
            IsApplicable = static model => model.MetadataHeap is not null,
            CanRender = static model => model.MetadataHeap is not null,
        });

        foreach (var heap in MetadataHeapCoordinate.Heaps)
        {
            var kind = heap;

            pipeline.Add(new SectionEntry<LibraryInspection>
            {
                Name = MetadataSectionNames.ForHeap(kind),
                IsExpensive = false,
                ExplicitOnly = true,
                ListedInCatalog = false,
                SizeClass = SectionSizeClass.Verbose,
                // Listing a heap costs a projection of every table — an entry is referenced by the
                // image, not by a subset of it — and a string heap can hold tens of thousands of
                // entries. Unbounded is the honest classification.
                Cost = SectionCost.Unbounded,
                ScannerKey = LibrarySections.ScannerMetadata,
                // Effectiveness follows the heap's size from the cheap image scan. Probing by
                // rendering would project every table during discovery, paying the section's whole
                // cost just to decide whether to list it.
                ProbeEffectiveness = false,
                HasExplicitApplicability = true,
                IsApplicable = model => HasHeapBytes(model, kind),
                CanRender = model => HasHeapBytes(model, kind),
            });
        }

        foreach (var table in ILInspector.Metadata.MetadataTableProjector.ProjectedTables)        {
            // Captured per iteration so each entry's predicate closes over its own table rather
            // than the loop's final value.
            var index = table;

            pipeline.Add(new SectionEntry<LibraryInspection>
            {
                Name = MetadataSectionNames.ForTable(index),
                IsExpensive = false,
                ExplicitOnly = true,
                ListedInCatalog = false,
                SizeClass = SectionSizeClass.Verbose,
                // Unbounded is the honest classification: a table such as MethodDef grows without
                // a meaningful bound, so no verbosity may auto-run it. Combined with ExplicitOnly
                // this section is reachable only by exact name or the @Metadata door.
                Cost = SectionCost.Unbounded,
                ScannerKey = LibrarySections.ScannerMetadata,
                // Effectiveness is decided by the scanner's row count, which is exact: a table with
                // rows always renders rows. Probing by rendering would project the whole table
                // during discovery, paying the lens's expensive half just to list it.
                ProbeEffectiveness = false,
                HasExplicitApplicability = true,
                IsApplicable = model => HasRows(model, index),
                CanRender = model => HasRows(model, index),
            });
        }

        return pipeline.AddCategory(SectionCategoryNames.Metadata, [.. MetadataSectionNames.All]);
    }

    static bool HasMetadata(LibraryInspection model) => model.MetadataOverview is not null;

    /// <summary>
    /// True when <paramref name="heap"/> holds any bytes in this image. Heap sizes come from the
    /// cheap image scan, so this never lists entries.
    ///
    /// Size, not listability, is the applicability test — including for <c>#US</c>, which cannot
    /// be enumerated at all. An image that stores user strings has a <c>#US</c> heap worth naming,
    /// and its section says why its entries cannot be listed and how to read one by address. An
    /// image with no user strings has a zero-byte heap and no section, which is the honest answer
    /// in both directions.
    /// </summary>
    static bool HasHeapBytes(LibraryInspection model, ILInspector.Metadata.HeapKind heap)
    {
        if (model.MetadataOverview is not { } overview)
            return false;

        foreach (var summary in overview.Heaps)
            if (summary.Heap == heap)
                return summary.SizeInBytes > 0;

        return false;
    }

    /// <summary>
    /// True when <paramref name="table"/> has at least one row in this image. Row counts come from
    /// the cheap image scan, so this never projects rows.
    /// </summary>
    static bool HasRows(LibraryInspection model, System.Reflection.Metadata.Ecma335.TableIndex table)
    {
        if (model.MetadataOverview is not { } overview)
            return false;

        foreach (var summary in overview.Tables)
            if (summary.Index == table)
                return summary.RowCount > 0;

        return false;
    }
}
