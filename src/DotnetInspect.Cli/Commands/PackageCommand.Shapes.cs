using System.Globalization;
using System.Text.Json;

using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The package command's adoption of <c>docs/design/section-shapes.md</c>: a
/// lone explicitly selected section renders in its declared shape's native
/// format, a scalar section rejects row terminals, and the package file
/// family lowers to one homogeneous Path/Size listing.
/// </summary>
public partial class PackageCommand
{
    /// <summary>
    /// Applies the native format of a lone selected section when the caller
    /// named no format: a Table streams TSV rows, a Hierarchy renders its
    /// tree, and a Text prints its payload. Explicit intent — a format flag or
    /// environment default, <c>--print</c>, <c>--raw</c>, <c>--tree</c>,
    /// <c>--count</c>, a projection, a shape flag, discovery, or a lens —
    /// leaves the options unchanged.
    /// </summary>
    private static InspectionOptions ApplyNativeShapeFormat(
        InspectionOptions options,
        PackageSectionCatalog catalog)
    {
        bool hasExplicitOutputIntent =
            options.FormatExplicitlySet
            || options.Print
            || options.Raw
            || options.JsonArray
            || options.Tree
            || options.Count
            || options.Value
            || options.Urls
            || options.Paths
            || options.Roots
            || options.ShowContent
            || options.ListVersions
            || options.Discover is not null
            || options.Schema
            || options.Fields is { Length: > 0 }
            || options.Columns is { Length: > 0 };
        SectionNativeOutput? nativeOutput =
            SectionShapeOutputPolicy.ResolveNativeOutput(
                options.SelectExplicitlySet,
                options.IncludeSections,
                catalog.Sections.SectionShapes,
                hasExplicitOutputIntent);

        return nativeOutput switch
        {
            SectionNativeOutput.TabularRows => options with
            {
                Format = OutputFormat.Tsv,
                Tabular = true,
                Tsv = true,
                Jsonl = false,
            },
            SectionNativeOutput.HierarchyTree => options with { Tree = true },
            SectionNativeOutput.TextPayload => options with { Print = true },
            _ => options,
        };
    }

    /// <summary>
    /// A lone scalar section (a field set or a single Text payload) has no rows
    /// under <c>docs/design/section-cardinality.md</c>, so <c>--count</c> and
    /// the <c>--rows</c> row window are rejected before acquisition. A bare
    /// <c>-n</c> is not a row terminal here: without a semantic row population
    /// it is the rendered-line window that <c>docs/design/cli-row-selection.md</c>
    /// assigns to unadopted modes, exactly as for <c>Library Info</c>. Count
    /// maps over several sections keep their existing per-section meaning.
    /// </summary>
    private static string? ValidatePackageScalarTerminals(
        InspectionOptions options)
    {
        SectionTerminalCapability? terminal =
            options.Count
                ? SectionTerminalCapability.Count
                : options.Rows is not null
                    ? SectionTerminalCapability.Rows
                    : null;
        return SectionShapeOutputPolicy.ValidateScalarTerminal(
            options.IncludeSections,
            PackageSectionCardinality.Declarations,
            terminal,
            discovery: options.Discover is not null);
    }

    /// <summary>
    /// True when the selection is more than one section and every member is a
    /// package file family section, which share the Path/Size row schema.
    /// </summary>
    private static bool IsPackageFileFamilySelection(
        IReadOnlyCollection<string>? sections) =>
        sections is { Count: > 1 }
        && sections.All(PackageFileFamily.IsFamilySection);

    /// <summary>
    /// Names a section with its declared shape for a diagnostic, as
    /// <c>docs/design/section-shapes.md</c> requires of an inadmissible
    /// shape/format pair ("Target Frameworks (Table)").
    /// </summary>
    private static string DescribeSectionShape(string section)
    {
        PackageSectionCatalog catalog = PackageSectionDescriptors.CreateCatalog();
        return SectionShapeOutputPolicy.DescribeSection(
            section,
            catalog.Sections.SectionShapes);
    }

    private static string FormatsForSection(string section) =>
        SectionShapeOutputPolicy.DescribeFormats(
            section,
            PackageOutputCapabilities.Catalog);

    /// <summary>
    /// Explicit <c>--tree</c> on a lone section that is not a Hierarchy fails
    /// before acquisition, naming the section's shape, the formats that shape
    /// supports, and the Hierarchy sections a tree can render.
    /// </summary>
    private static string DescribeTreeRejection(IReadOnlySet<string> sections)
    {
        if (sections.Count == 1)
        {
            string section = sections.Single();
            string prefix = section.Equals(PackageSections.Dependencies, StringComparison.OrdinalIgnoreCase)
                ? "Dependencies is direct evidence and cannot be rendered as a hierarchy. "
                : "";
            return prefix
                + $"--tree renders a Hierarchy; {DescribeSectionShape(section)} supports "
                + $"{FormatsForSection(section)}. Hierarchy sections: "
                + $"'{PackageSections.Files}', '{PackageSections.DependencyHierarchy}'.";
        }

        return "--tree renders exactly one Hierarchy section; the selection is "
            + string.Join(", ", sections.Select(DescribeSectionShape)) + ". "
            + $"Select '{PackageSections.Files}' or '{PackageSections.DependencyHierarchy}' alone, "
            + "omit the section for the Package children tree, or use --markdown/--json "
            + "for the composition.";
    }

    /// <summary>
    /// Explicit <c>--table</c>, <c>--tsv</c>, or <c>--jsonl</c> over several
    /// sections is admitted only for one homogeneous row family; otherwise the
    /// rejection names each section's shape so the caller can pick one Table
    /// or a composition format.
    /// </summary>
    private static bool ValidatePackageTabularSelection(
        InspectionOptions options,
        IReadOnlyCollection<string>? sections)
    {
        if (!options.TabularExplicitlySet || sections is not { Count: > 1 })
            return true;
        if (PackageOutputCapabilities.Catalog.Supports(DiscoveryOutputMode.Table, sections))
            return true;

        // Table sections share a shape but not a row schema, so the obstacle
        // is the missing homogeneous family, not a shape mismatch.
        CommandError.Write(
            $"Selection matches {sections.Count} sections without a shared row schema: "
                + string.Join(", ", sections.Select(DescribeSectionShape)) + ".");
        CommandError.WriteBlankLine();
        CommandError.WriteLine(
            "--table, --tsv, and --jsonl display one Table or one homogeneous row family at a time.");
        CommandError.WriteLine(
            "Use -S with a single section, -S @Files for the package file family, or --markdown/--json for multi-section output.");
        return false;
    }

    private static bool IsSingleFilesSelection(InspectionOptions options) =>
        options.IncludeSections is { Count: 1 } sections
        && sections.Single().Equals(
            PackageSections.Files,
            StringComparison.OrdinalIgnoreCase);

    private const string PackageFileFamilyListing = "Package file family";

    private static readonly string[] PackageFileFamilyColumns =
        ["Path", "Size", "path", "size"];

    /// <summary>
    /// A family listing is one Table, so a column or field projection names
    /// its Path/Size columns; an unknown name is diagnosed before any output.
    /// </summary>
    private static bool ValidatePackageFileFamilyProjection(InspectionOptions options)
    {
        if (options.Fields is not { Length: > 0 } && options.Columns is not { Length: > 0 })
            return true;
        var schema = new DocumentSchema();
        schema.Add(PackageFileFamilyListing, "column", PackageFileFamilyColumns);
        return ProjectionDiagnostics.ValidateProjection(
            schema,
            PackageFileFamilyListing,
            fields: null,
            [.. options.Fields ?? [], .. options.Columns ?? []]);
    }

    /// <summary>
    /// Streams the package file family as one Path/Size listing: each member
    /// section's rows in family order, one row per distinct path. Text
    /// members contribute their fact row; Table members their rows.
    /// <c>--rows</c> windows the rows; a bare <c>-n</c> remains the rendered-line
    /// window it is for every other lone package Table.
    /// </summary>
    private static void WritePackageFileFamilyTable(
        TextWriter output,
        InspectionResult result,
        InspectionOptions options)
    {
        var text = new PackageInspectionText(result);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<PackageFileText>();
        foreach (string section in PackageFileFamily.SectionNames)
        {
            if (!options.IncludeSections!.Contains(section))
                continue;
            foreach (PackageFileText file in GetPackageFileTextRows(result, text, section))
            {
                if (seen.Add(file.Path.ToString()))
                    rows.Add(file);
            }
        }

        PackageFileText[] windowed = [.. RowWindow.Apply(options.Rows, rows)];
        string[][] cells =
        [
            .. windowed.Select(file => new[]
            {
                file.Path.ToString(),
                file.Size.ToString(CultureInfo.InvariantCulture),
            }),
        ];
        // The listing has columns only, so a --fields spelling projects the
        // same Path/Size columns; one projected writer serves table, TSV, and
        // JSONL so every format honors the projection.
        string[] projection = [.. options.Fields ?? [], .. options.Columns ?? []];
        OutputFormatter.WriteProjectedTable(
            output,
            showHeader: !options.NoHeader,
            tsv: options.Tsv,
            jsonl: options.Jsonl,
            columns: projection.Length > 0 ? projection : null,
            fields: null,
            (writer, formatter, writerOptions) =>
            {
                writerOptions.JsonTypedValues = true;
                var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
                markoutWriter.WriteTable(
                    ["Path", "Size"],
                    ["path", "size"],
                    cells);
                markoutWriter.Flush();
            });
    }

    /// <summary>
    /// Renders the whole-package file inventory as its native tree: the
    /// subject identity and issued properties on the title line, then the
    /// selected file rows grouped under their directories. Directory nodes are
    /// context, so <c>-n</c> and <c>--rows</c> select files before the tree is
    /// lowered through the shared hierarchy sink.
    /// </summary>
    private static void WritePackageFilesTree(
        InspectionResult result,
        InspectionOptions options)
    {
        var text = new PackageInspectionText(result);
        List<PackageFileText> files =
            GetPackageFileTextRows(result, text, PackageSections.Files);
        DotnetInspector.Queries.PackageFileInventoryEntry[] selected =
        [
            .. RowWindow.Apply(options.Rows, files)
                .Select(static file => new DotnetInspector.Queries.PackageFileInventoryEntry(
                    file.Path.ToString(), file.Size)),
        ];

        var properties = new List<ResultProperty>(2);
        if (!string.IsNullOrWhiteSpace(result.Source))
            properties.Add(new ResultProperty("source", result.Source));
        string? target = options.PackageFilePredicates
            .FirstOrDefault(term => term.Key.Equals("Target", StringComparison.OrdinalIgnoreCase)
                && term.Operator == QuerySpace.PortableQueryOperator.Equal)?.Value
            ?? (options.Tfm?.Equals("all", StringComparison.OrdinalIgnoreCase) == true ? null : options.Tfm);
        if (!string.IsNullOrWhiteSpace(target))
            properties.Add(new ResultProperty("target", target));
        string title = ResultTitle.Compose(
            $"{result.PackageName} {result.Version}".Trim(),
            properties);

        OutputDestination.Write(
            options.OutputPath,
            null,
            output => DotnetInspector.Presentation.PackageFileHierarchyPresentation.WriteTree(
                selected,
                title,
                output));
    }
}
