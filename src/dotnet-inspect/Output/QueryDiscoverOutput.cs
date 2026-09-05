using System.Collections.Immutable;
using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspector.Output;

internal static class QueryDiscoverOutput
{
    private static readonly string[] CatalogColumns = ["Section", "Operators", "Facets"];
    private static readonly string[] FacetColumns = ["Facet", "Operators", "Comparisons", "Values", "Example"];
    internal const string NoOperators = "This section has no CLI query facets or ranking operators.";

    internal static int Execute(
        ParseResult result,
        SharedOptions options,
        SectionQueryCatalog catalog,
        string[] query,
        bool discoverSchema)
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
                companionSchema.Add(section.QuerySection, "column", FacetColumns);
            return DiscoverOutput.Execute(
                [.. selected.Select(section => section.QuerySection)],
                companionSchema,
                json: format == OutputFormat.Json,
                markdown: format == OutputFormat.Markdown,
                tsv: format == OutputFormat.Tsv,
                jsonl: format == OutputFormat.Jsonl,
                plainText: format == OutputFormat.PlainText,
                projection: projection);
        }

        string[] headers = bare ? CatalogColumns : FacetColumns;
        if (!LensProjection.TryResolveColumns(
                projection, "-Q/--query-help", headers, out string[] resolvedColumns))
            return 1;
        string[]? projectedColumns = projection.Fields is { Length: > 0 }
            || projection.Columns is { Length: > 0 } ? resolvedColumns : null;

        RowWindow? window = options.ParseRows(result);
        if (bare)
            selected = [.. RowWindow.Apply(window, selected)];
        else
            selected = [.. selected.Select(section => section with
            {
                Facets = [.. RowWindow.Apply(window, section.Facets)],
            })];

        if (projection.Count)
        {
            if (bare || selected.Length == 1)
                CountOutput.WriteCount(bare ? selected.Length : selected[0].Facets.Length);
            else
            {
                var counts = new CountProjection();
                foreach (SectionQueryDescriptor section in selected)
                    counts.SetRows(section.QuerySection, section.Facets.Length);
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
                        Write(new MarkoutWriter(output, formatter, writerOptions), selected, bare, true, message));
            }
            else
            {
                var document = new QueryDiscoveryDocument(
                    result.CommandResult.Command.Name,
                    message,
                    [.. selected.Select(section => new QueryDiscoverySection(
                        section.Section,
                        section.QuerySection,
                        section.Summary,
                        section.Facets.Length,
                        Operators(section),
                        bare ? null : section.Facets))]);
                Console.WriteLine(JsonSerializer.Serialize(
                    document, QueryDiscoveryJsonContext.Default.QueryDiscoveryDocument));
            }
            return 0;
        }

        if (tabular)
        {
            if (message is not null)
                CommandError.WriteNote(message);
            else if (!bare && selected[0].Facets.IsEmpty)
                CommandError.WriteNote(NoOperators);
            OutputFormatter.WriteProjectedTable(
                Console.Out, !result.GetValue(options.NoHeaders),
                format == OutputFormat.Tsv, format == OutputFormat.Jsonl,
                DisplayColumns(), null,
                (output, formatter, writerOptions) =>
                    Write(new MarkoutWriter(output, formatter, writerOptions), selected, bare, false, null));
            return 0;
        }

        var writer = new MarkoutWriter(
            Console.Out,
            format == OutputFormat.PlainText ? new PlainTextFormatter() : new MarkdownFormatter(),
            OutputFormatter.CreateProjectedWriterOptions(DisplayColumns(), null));
        Write(writer, selected, bare, true, message);
        return 0;

        string[]? DisplayColumns() => projectedColumns
            ?? (!bare
                && options.ParseVerbosity(result) < Verbosity.Detailed
                    ? ["Facet", "Operators", "Comparisons", "Values"]
                    : null);
    }

    private static ImmutableArray<string> Operators(SectionQueryDescriptor section)
        => [.. section.Facets.SelectMany(facet => facet.Operators).Distinct(StringComparer.Ordinal)];

    private static void Write(
        MarkoutWriter writer,
        IReadOnlyList<SectionQueryDescriptor> sections,
        bool bare,
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
                    string.Join(", ", Operators(section)),
                    section.Facets.Length.ToString(CultureInfo.InvariantCulture),
                })]);
        }
        else
        {
            foreach (SectionQueryDescriptor section in sections)
            {
                if (headings)
                {
                    writer.WriteHeading(2, section.QuerySection);
                    writer.WriteParagraph(section.Summary);
                }
                writer.WriteTable(FacetColumns, ["facet", "operators", "comparisons", "values", "example"],
                    [.. section.Facets.Select(facet => new[]
                    {
                        facet.Name,
                        string.Join(", ", facet.Operators),
                        string.Join(", ", facet.Comparisons),
                        facet.Values.IsEmpty
                            ? facet.ValueKind
                            : facet.Name == "Kind"
                                ? "C# Body Kinds: vocabulary -S \"C# Body Kinds\""
                                : string.Join(", ", facet.Values),
                        facet.Example,
                    })]);
            }
        }
        writer.Flush();
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
    int FacetCount,
    ImmutableArray<string> Operators,
    ImmutableArray<SectionQueryFacet>? Facets);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(QueryDiscoveryDocument))]
internal partial class QueryDiscoveryJsonContext : JsonSerializerContext;
