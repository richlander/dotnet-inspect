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
        if (!options.SelectExplicitlySet
            || options.IncludeSections is not { Count: 1 } sections
            || options.FormatExplicitlySet
            || options.Print
            || options.Raw
            || options.Tree
            || options.Count
            || options.Value
            || options.Urls
            || options.Paths
            || options.Roots
            || options.ShowContent
            || options.ListVersions
            || options.ListLayout
            || options.ListTfms
            || options.Discover is not null
            || options.Schema
            || options.Fields is { Length: > 0 }
            || options.Columns is { Length: > 0 })
        {
            return options;
        }

        string section = sections.Single();
        if (!catalog.Sections.SectionShapes.TryGetValue(
                section,
                out SectionShape shape))
        {
            return options;
        }

        return shape switch
        {
            SectionShape.Table => options with
            {
                Format = OutputFormat.Tsv,
                Tabular = true,
                Tsv = true,
                Jsonl = false,
            },
            SectionShape.Hierarchy => options with { Tree = true },
            SectionShape.Text => options with { Print = true },
            _ => options,
        };
    }

    /// <summary>
    /// A lone scalar section (a field set or a single Text payload) has no rows
    /// under <c>docs/design/section-cardinality.md</c>, so <c>--count</c>,
    /// <c>-n</c>, and <c>--rows</c> are rejected before acquisition. Count
    /// maps over several sections keep their existing per-section meaning.
    /// </summary>
    private static string? ValidatePackageScalarTerminals(
        InspectionOptions options)
    {
        bool rows = options.Rows is not null;
        if (!options.Count && !rows)
            return null;
        if (options.Discover is not null
            || options.IncludeSections is not { Count: 1 } sections)
        {
            return null;
        }

        string section = sections.Single();
        if (!PackageSectionCardinality.Declarations.TryGetValue(
                section,
                out SectionCardinalityDeclaration? declaration)
            || declaration.Kind != SectionCardinalityKind.Scalar)
        {
            return null;
        }

        string terminal = options.Count ? "--count" : "--rows";
        return $"Section '{section}' is scalar and does not support "
            + $"{terminal}. Select an inventory section.";
    }

    /// <summary>
    /// True when the selection is more than one section and every member is a
    /// package file family section, which share the Path/Size row schema.
    /// </summary>
    private static bool IsPackageFileFamilySelection(
        IReadOnlyCollection<string>? sections) =>
        sections is { Count: > 1 }
        && sections.All(PackageFileFamily.IsFamilySection);

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
        if (options.Jsonl)
        {
            foreach (PackageFileText file in windowed)
            {
                var row = new PackageFileJsonRow(file.Path, file.Size);
                output.WriteLine(JsonSerializer.Serialize(
                    row,
                    PackageFileJsonRowContext.Default.PackageFileJsonRow));
            }
            return;
        }

        string[][] cells =
        [
            .. windowed.Select(file => new[]
            {
                file.Path.ToString(),
                file.Size.ToString(CultureInfo.InvariantCulture),
            }),
        ];
        OutputFormatter.WriteTable(output, !options.NoHeader, (writer, formatter) =>
        {
            var writerOptions = OutputFormatter.CreateProjectedWriterOptions(
                options.Columns,
                options.Fields);
            OutputFormatter.ConfigureTableWriterOptions(
                writerOptions,
                options.Tsv,
                options.Jsonl);
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
    /// built, as the layout lens does.
    /// </summary>
    private static void WritePackageFilesTree(
        InspectionResult result,
        InspectionOptions options)
    {
        var text = new PackageInspectionText(result);
        List<PackageFileText> files =
            GetPackageFileTextRows(result, text, PackageSections.Files);
        string[] paths =
        [
            .. RowWindow.Apply(options.Rows, files)
                .Select(static file => file.Path.ToString()),
        ];

        var properties = new List<ResultProperty>(2);
        if (!string.IsNullOrWhiteSpace(result.Source))
            properties.Add(new ResultProperty("source", result.Source));
        if (!string.IsNullOrWhiteSpace(options.Tfm))
            properties.Add(new ResultProperty("target", options.Tfm));
        string title = ResultTitle.Compose(
            $"{result.PackageName} {result.Version}".Trim(),
            properties);

        OutputDestination.Write(
            options.OutputPath,
            null,
            output => PackageOutputFormatter.WriteFileTree(
                output,
                title,
                paths,
                collapseSingleChildDirectories: true));
    }
}
