using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using DotnetInspector.MetadataRendering;
using DotnetInspector.Models;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Output;

/// <summary>
/// Renders the <c>@Metadata</c> lens's sections into the library command's output.
///
/// Metadata tables have per-table column shapes, so they cannot be attributed properties on
/// <c>LibraryInspectionView</c> the way every other library section is — Markout's attributed
/// model binds a static row type per section. They are rendered imperatively instead, through
/// <see cref="MetadataProjectionRenderer"/>, and composed into the same document. Composing rather
/// than writing straight to the console is what keeps the rest of the output contract working
/// unchanged: section ordering, <c>--rows</c> windowing, and <c>--count</c> all operate on the
/// rendered Markdown, so they apply to metadata sections without knowing anything about them.
/// </summary>
internal static class MetadataLensRenderer
{
    /// <summary>
    /// The per-table row ceiling the lens projects with.
    ///
    /// Deliberately far above <see cref="MetadataProjectionOptions.DefaultMaxRowsPerTable"/>: row
    /// *selection* is <c>--rows</c>'s job and it windows the rendered table, so a projection
    /// ceiling below the table's size would silently put rows beyond it out of reach of any
    /// <c>--rows</c> range. The ceiling remains non-infinite so a corrupt or hostile image cannot
    /// drive unbounded allocation, and crossing it is reported as a caveat rather than passed off
    /// as a complete table.
    /// </summary>
    internal const int MaxRowsPerTable = 1_000_000;

    /// <summary>
    /// Maps the command's tabular flags onto the projection renderer's format. JSONL wins over TSV
    /// because it is the more specific request; with neither set the caller is on the pretty-table
    /// path, whose rows the Markdown pipe form already carries.
    /// </summary>
    internal static MetadataTableFormat FormatFor(bool tsv, bool jsonl)
        => jsonl ? MetadataTableFormat.Jsonl
            : tsv ? MetadataTableFormat.Tsv
            : MetadataTableFormat.Markdown;

    /// <summary>
    /// True when any section in <paramref name="sections"/> belongs to this lens. A null set means
    /// "no section filter", which for an explicit-only lens means nothing of it was selected.
    /// </summary>
    internal static bool IsSelected(IReadOnlyCollection<string>? sections)
        => sections is not null && sections.Any(MetadataSectionNames.IsMetadataSection);

    /// <summary>
    /// Renders the selected metadata sections as Markdown H2 sections, or returns
    /// <see langword="null"/> when none is selected.
    ///
    /// Each heading is exactly the registered section name, because the section orderer, the
    /// section filter, and <c>--count</c> all key off the heading text.
    ///
    /// Caveats are written inline, as paragraphs under the section they qualify. That matches the
    /// existing discipline: Markdown is the human view and a caveat separated from its table is a
    /// caveat a reader can miss, whereas the machine formats route caveats to stderr to keep the
    /// stream pure. Inline paragraphs are not table rows, so they do not perturb <c>--count</c> or
    /// <c>--rows</c>.
    /// </summary>
    internal static string? RenderMarkdown(
        LibraryInspection inspection,
        IReadOnlyCollection<string>? sections)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        if (!IsSelected(sections))
            return null;

        var selected = sections!;
        var output = new StringWriter();
        var caveats = new StringWriter();
        bool first = true;

        if (selected.Contains(MetadataSectionNames.Image, StringComparer.OrdinalIgnoreCase)
            && inspection.MetadataOverview is { } overview)
        {
            output.WriteLine($"## {MetadataSectionNames.Image}");
            output.WriteLine();
            MetadataProjectionRenderer.RenderImageFacts(overview, output);
            WriteCaveats(output, MetadataProjectionRenderer.Caveats(overview));
            first = false;
        }

        foreach (var table in ProjectSelected(inspection, selected, caveats))
        {
            if (!first)
                output.WriteLine();
            first = false;

            output.WriteLine($"## {MetadataSectionNames.ForTable(table.Index)}");
            output.WriteLine();
            MetadataProjectionRenderer.RenderRows(table.View, output);
            WriteCaveats(output, MetadataProjectionRenderer.Caveats(table.View));
        }

        // Projection-level failures are collected separately because they occur before any section
        // heading exists to hang them under; they still must be seen, so they trail the sections
        // rather than being dropped.
        var pending = caveats.ToString();
        if (pending.Length > 0)
        {
            output.WriteLine();
            output.Write(pending);
        }

        var text = output.ToString();
        return text.Length == 0 ? null : text.TrimEnd();
    }

    static void WriteCaveats(TextWriter output, IEnumerable<string> caveats)
    {
        foreach (string caveat in caveats)
        {
            output.WriteLine();
            output.WriteLine(caveat);
        }
    }

    /// <summary>
    /// Renders the selected metadata table sections as one self-identifying tabular stream
    /// (TSV/JSONL/pretty table), or returns <see langword="false"/> when none is selected.
    ///
    /// <c>Metadata: Image</c> is a Property/Value shape rather than table rows, so it is emitted
    /// through the projection renderer's own overview path, which carries a leading
    /// <c>Section</c> column for exactly this reason.
    /// </summary>
    internal static bool TryRenderTabular(
        LibraryInspection inspection,
        IReadOnlyCollection<string>? sections,
        MetadataTableFormat format,
        TextWriter output,
        TextWriter caveats)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(caveats);

        if (!IsSelected(sections))
            return false;

        var selected = sections!;

        if (selected.Contains(MetadataSectionNames.Image, StringComparer.OrdinalIgnoreCase)
            && inspection.MetadataOverview is { } overview)
        {
            MetadataProjectionRenderer.Render(overview, output, format);
            foreach (string caveat in MetadataProjectionRenderer.Caveats(overview))
                caveats.WriteLine(caveat);
        }

        var views = ProjectSelected(inspection, selected, caveats);
        if (views.Length > 0)
        {
            var projection = new MetadataTableProjection([.. views.Select(static v => v.View)]);
            MetadataProjectionRenderer.Render(projection, output, format);
            foreach (var view in views)
                foreach (string caveat in MetadataProjectionRenderer.Caveats(view.View))
                    caveats.WriteLine(caveat);
        }

        return true;
    }

    /// <summary>
    /// Projects exactly the tables the selection names — never the rest.
    ///
    /// This is the lens's expensive half, and restricting it to the selection is what makes
    /// <c>-S "Metadata: TypeRef"</c> cost one table rather than seventeen.
    /// </summary>
    static ImmutableArray<(TableIndex Index, MetadataTableView View)> ProjectSelected(
        LibraryInspection inspection,
        IReadOnlyCollection<string> sections,
        TextWriter caveats)
    {
        var tables = MetadataSectionNames.TablesFor(sections);
        if (tables.IsEmpty)
            return [];

        if (inspection.MetadataAssemblyPath is not { } path)
        {
            caveats.WriteLine(
                "Metadata tables were selected but the assembly path is unavailable, so no table could be projected.");
            return [];
        }

        MetadataTableProjection projection;
        try
        {
            // Projected through AssemblyInspectionSession rather than by opening a PEReader here:
            // the metadata layer owns reading the image, and the CLI is gated against referencing
            // raw readers (LayeringTests.Cli_DoesNotReferenceRawMetadataReaders).
            using var session = AssemblyInspectionSession.Open(path);
            projection = session.MetadataTables(new MetadataProjectionOptions
            {
                Tables = tables,
                MaxRowsPerTable = MaxRowsPerTable,
            });
        }
        catch (Exception ex)
        {
            // Surfaced rather than swallowed: an unreadable image must not render as a set of
            // legitimately empty tables.
            caveats.WriteLine($"Metadata tables could not be projected from {path}: {ex.Message}");
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<(TableIndex, MetadataTableView)>(projection.Tables.Length);
        foreach (var view in projection.Tables)
            builder.Add((view.Index, view));

        return builder.ToImmutable();
    }
}
