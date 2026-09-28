using System.Collections.Immutable;
using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class QueryDiscoverOutput
{
    private static readonly string[] CatalogColumns = ["Section", "Operators", "Facets"];
    internal const string NoOperators = "This section has no CLI query facets or ranking operators.";

    internal static int Execute(
        ParseResult result,
        SharedOptions options,
        SectionQueryCatalog catalog,
        string[] query,
        bool discoverSchema,
        string? commandName = null)
    {
        bool bare = query.Length == 0;
        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            query, catalog.KnownSections, categories: catalog.Categories);
        if (SelectOutput.WriteUnresolved(selection))
            return 1;

        SectionQueryDescriptor[] selected = bare
            ? [.. catalog.Queries]
            : [.. catalog.KnownSections
                .Where(name => selection.Sections!.Contains(name))
                .Select(name => catalog.Queries.FirstOrDefault(item => item.Section == name)
                    ?? new SectionQueryDescriptor(name, NoOperators, []))];
        bool includeExecutionClass =
            !bare
            && selected.SelectMany(section => section.Keys)
                .Any(key => key.ExecutionClass is not null);
        bool includeResourcePath =
            !bare
            && selected.SelectMany(section => section.Keys)
                .Any(key => key.ResourcePath is not null);
        string[] facetColumns =
            FacetColumns(
                includeExecutionClass,
                includeResourcePath);
        RowSelectionIntent<string>? semanticRowSelection = null;
        string? semanticSelectionName = commandName switch
        {
            "find" => "Find",
            "package query" => "Package Query",
            _ => null,
        };
        if (semanticSelectionName is not null
            && !CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                result,
                semanticSelectionName,
                out semanticRowSelection,
                out string? rowSelectionError))
        {
            CommandError.Write(rowSelectionError!);
            return 1;
        }
        OutputFormat format = options.ResolveFormat(result);
        IProjectionOptions projection = ProjectionAudit.Requested(result, options);
        if (discoverSchema)
        {
            if (selected.Length != 1)
            {
                CommandError.Write("Query companion schema discovery requires one section; use -D \"Query: <section>\".");
                return 1;
            }
            var companionSchema = new DocumentSchema();
            foreach (SectionQueryDescriptor section in selected)
                companionSchema.Add(section.QuerySection, "column", facetColumns);
            return DiscoverOutput.Execute(
                [.. selected.Select(section => section.QuerySection)],
                companionSchema,
                DiscoveryOutputRequest.Create(
                    format,
                    tableExplicitlySet:
                        options.IsTableExplicitlySet(result),
                    noHeader:
                        result.GetValue(options.NoHeaders),
                    projection: projection),
                semanticRowSelection: semanticRowSelection,
                semanticSelectionName:
                    semanticSelectionName ?? "Discovery");
        }

        string[] headers = bare ? CatalogColumns : facetColumns;
        if (!LensProjection.TryResolveColumns(
                projection, "-Q/--query-help", headers, out string[] resolvedColumns))
            return 1;
        string[]? projectedColumns = projection.Fields is { Length: > 0 }
            || projection.Columns is { Length: > 0 } ? resolvedColumns : null;

        RowWindow? window =
            semanticRowSelection is null
                ? options.ParseRows(result)
                : null;
        if (bare)
        {
            if (!TryApplyRows(
                    selected,
                    window,
                    semanticRowSelection,
                    semanticSelectionName,
                    "query section",
                    out IReadOnlyList<SectionQueryDescriptor> selectedRows))
            {
                return 1;
            }
            selected = [.. selectedRows];
        }
        else
        {
            var selectedSections =
                new List<SectionQueryDescriptor>(
                    selected.Length);
            foreach (SectionQueryDescriptor section in selected)
            {
                if (!TryApplyRows(
                        section.Keys,
                        window,
                        semanticRowSelection,
                        semanticSelectionName,
                        "query facet",
                        out IReadOnlyList<SectionQueryKey> selectedKeys))
                {
                    return 1;
                }
                selectedSections.Add(
                    section with
                    {
                        Keys = [.. selectedKeys],
                    });
            }
            selected = [.. selectedSections];
        }

        if (projection.Count)
        {
            if (bare || selected.Length == 1)
                CountOutput.WriteCount(bare ? selected.Length : selected[0].Keys.Length);
            else
            {
                var counts = new CountProjection();
                foreach (SectionQueryDescriptor section in selected)
                    counts.SetRows(section.QuerySection, section.Keys.Length);
                CountOutput.Write(counts, [.. selected.Select(section => section.QuerySection)], format,
                    result.GetValue(options.NoHeaders));
            }
            return 0;
        }

        bool tabular = format is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl;
        if (tabular && !bare && selected.Length != 1)
        {
            CommandError.Write("Tabular query discovery requires one section; use -Q <section> or --json.");
            return 1;
        }

        string? message = selected.Length == 0
            ? "No CLI query facets or ranking operators are available in this scope."
            : null;
        if (format == OutputFormat.Json)
        {
            if (projectedColumns is not null)
            {
                OutputFormatter.WriteProjectedJson(
                    Console.Out, projectedColumns, null,
                    (output, formatter, writerOptions) =>
                        Write(
                            new MarkoutWriter(output, formatter, writerOptions),
                            selected,
                            bare,
                            includeExecutionClass,
                            includeResourcePath,
                            true,
                            message));
            }
            else
            {
                var document = new QueryDiscoveryDocument(
                    commandName ?? result.CommandResult.Command.Name,
                    message,
                    [.. selected.Select(section => new QueryDiscoverySection(
                        section.Section,
                        section.QuerySection,
                        section.Summary,
                        section.Keys.Length,
                        Operators(section),
                        bare ? null : section.Keys))]);
                Console.WriteLine(JsonSerializer.Serialize(
                    document, QueryDiscoveryJsonContext.Default.QueryDiscoveryDocument));
            }
            return 0;
        }

        if (tabular)
        {
            if (message is not null)
                CommandError.WriteNote(message);
            else if (!bare && selected[0].Keys.IsEmpty)
                CommandError.WriteNote(NoOperators);
            OutputFormatter.WriteProjectedTable(
                Console.Out, !result.GetValue(options.NoHeaders),
                format == OutputFormat.Tsv, format == OutputFormat.Jsonl,
                DisplayColumns(), null,
                (output, formatter, writerOptions) =>
                    Write(
                        new MarkoutWriter(output, formatter, writerOptions),
                        selected,
                        bare,
                        includeExecutionClass,
                        includeResourcePath,
                        false,
                        null));
            return 0;
        }

        var writer = new MarkoutWriter(
            Console.Out,
            format == OutputFormat.PlainText ? new PlainTextFormatter() : new MarkdownFormatter(),
            OutputFormatter.CreateProjectedWriterOptions(DisplayColumns(), null));
        Write(
            writer,
            selected,
            bare,
            includeExecutionClass,
            includeResourcePath,
            true,
            message);
        return 0;

        string[]? DisplayColumns() => projectedColumns
            ?? (!bare
                && options.ParseVerbosity(result) < Verbosity.Detailed
                    ? includeExecutionClass
                        ? includeResourcePath
                            ? ["Facet", "Execution Class", "Operators", "Comparisons", "Values", "Resource"]
                            : ["Facet", "Execution Class", "Operators", "Comparisons", "Values"]
                        : includeResourcePath
                            ? ["Facet", "Operators", "Comparisons", "Values", "Resource"]
                            : ["Facet", "Operators", "Comparisons", "Values"]
                    : null);
    }

    private static bool TryApplyRows<T>(
        IReadOnlyList<T> rows,
        RowWindow? legacyWindow,
        RowSelectionIntent<string>? semanticRowSelection,
        string? semanticSelectionName,
        string rowKind,
        out IReadOnlyList<T> selectedRows)
        => CliSemanticRowSelection.TrySelectOrApplyLegacy(
            semanticRowSelection,
            legacyWindow,
            rows,
            rowKind,
            failure =>
                $"{semanticSelectionName ?? "Query discovery"} row selection stage "
                + $"{failure.Failure.StageNumber} requires {rowKind} row "
                + $"{failure.Failure.RequiredPosition}, but only "
                + $"{failure.Failure.AvailableCount} {rowKind} rows are available.",
            out selectedRows);

    private static ImmutableArray<string> Operators(SectionQueryDescriptor section)
        => [.. section.Keys.SelectMany(key => key.Operators).Distinct(StringComparer.Ordinal)];

    private static void Write(
        MarkoutWriter writer,
        IReadOnlyList<SectionQueryDescriptor> sections,
        bool bare,
        bool includeExecutionClass,
        bool includeResourcePath,
        bool headings,
        string? message)
    {
        if (message is not null)
            writer.WriteParagraph(message);
        if (bare)
        {
            if (headings)
                writer.WriteHeading(2, "Query-capable sections");
            writer.WriteTable(CatalogColumns, ["section", "operators", "facets"],
                [.. sections.Select(section => new[]
                {
                    section.Section,
                    MarkoutInline.Code(string.Join(", ", Operators(section))),
                    section.Keys.Length.ToString(CultureInfo.InvariantCulture),
                })]);
        }
        else
        {
            string[] columns =
                FacetColumns(
                    includeExecutionClass,
                    includeResourcePath);
            string[] fields =
                FacetFields(
                    includeExecutionClass,
                    includeResourcePath);
            foreach (SectionQueryDescriptor section in sections)
            {
                if (headings)
                {
                    writer.WriteHeading(2, section.QuerySection);
                    writer.WriteParagraph(section.Summary);
                }
                writer.WriteTable(
                    columns,
                    fields,
                    [.. section.Keys.Select(key =>
                        FacetRow(
                            key,
                            includeExecutionClass,
                            includeResourcePath))]);
            }
        }
        writer.Flush();
    }

    private static string[] FacetColumns(
        bool includeExecutionClass,
        bool includeResourcePath) =>
        (includeExecutionClass, includeResourcePath) switch
        {
            (true, true) =>
                ["Facet", "Execution Class", "Operators", "Comparisons", "Values", "Example", "Resource"],
            (true, false) =>
                ["Facet", "Execution Class", "Operators", "Comparisons", "Values", "Example"],
            (false, true) =>
                ["Facet", "Operators", "Comparisons", "Values", "Example", "Resource"],
            (false, false) =>
                ["Facet", "Operators", "Comparisons", "Values", "Example"],
        };

    private static string[] FacetFields(
        bool includeExecutionClass,
        bool includeResourcePath) =>
        (includeExecutionClass, includeResourcePath) switch
        {
            (true, true) =>
                ["facet", "execution_class", "operators", "comparisons", "values", "example", "resource"],
            (true, false) =>
                ["facet", "execution_class", "operators", "comparisons", "values", "example"],
            (false, true) =>
                ["facet", "operators", "comparisons", "values", "example", "resource"],
            (false, false) =>
                ["facet", "operators", "comparisons", "values", "example"],
        };

    private static string[] FacetRow(
        SectionQueryKey key,
        bool includeExecutionClass,
        bool includeResourcePath)
    {
        string values = key.Values.IsEmpty
            ? key.ValueKind
            : key.Name == BodyKindQueryOptions.QueryKey.Name
                && key.ValueKind == BodyKindQueryOptions.QueryKey.ValueKind
                ? "C# Body Kinds: "
                    + MarkoutInline.Code("vocabulary -S \"C# Body Kinds\"")
                : string.Join(", ", key.Values);
        string[] remainder =
        [
            MarkoutInline.Code(string.Join(", ", key.Operators)),
            key.Comparisons.IsEmpty
                ? ""
                : MarkoutInline.Code(string.Join(", ", key.Comparisons)),
            values,
            MarkoutInline.Code(key.Example),
        ];
        string[] row = includeExecutionClass
            ? [key.Name, key.ExecutionClass ?? "", .. remainder]
            : [key.Name, .. remainder];
        return includeResourcePath
            ? [.. row, key.ResourcePath ?? ""]
            : row;
    }
}

internal sealed record QueryDiscoveryDocument(
    string Command,
    string? Message,
    ImmutableArray<QueryDiscoverySection> Sections);

internal sealed record QueryDiscoverySection(
    string Section,
    string QuerySection,
    string Summary,
    [property: JsonPropertyName("facet_count")]
    int KeyCount,
    ImmutableArray<string> Operators,
    [property: JsonPropertyName("facets")]
    ImmutableArray<SectionQueryKey>? Keys);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(QueryDiscoveryDocument))]
internal partial class QueryDiscoveryJsonContext : JsonSerializerContext;
