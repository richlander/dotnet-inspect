using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class DetailedDiscoverOutput
{
    public static int Execute(
        string[]? discover,
        DocumentSchema schema,
        IReadOnlyList<string> knownSections,
        IReadOnlyDictionary<string, string[]> categories,
        IReadOnlySet<string> catalogHiddenSections,
        IReadOnlySet<string>? listedCategoryDoors,
        OutputCapabilityCatalog capabilities,
        DiscoveryOutputRequest request)
    {
        if (discover is { Length: > 1 })
        {
            CommandError.Write(
                "--details supports bare -D or one exact category or section "
                + "selector in this Library adoption.");
            return 1;
        }

        if (request.Tree
            || request.Format == OutputFormat.Mermaid)
        {
            CommandError.Write(
                "--details discovery supports --markdown, --plaintext, --json, "
                + "--table, --tsv, or --jsonl. Tree and Mermaid are reported "
                + "capabilities, not discovery renderers.");
            return 1;
        }

        if (request.Print
            || request.Value
            || request.Urls
            || request.Paths
            || request.Fields is { Length: > 0 }
            || request.Columns is { Length: > 0 })
        {
            CommandError.Write(
                "--details cannot be combined with print, shape, field, or "
                + "column projections.");
            return 1;
        }

        List<DetailedDiscoveryRow>? rows =
            ResolveRows(
                discover,
                schema,
                knownSections,
                categories,
                catalogHiddenSections,
                listedCategoryDoors,
                capabilities);
        if (rows is null)
            return 1;

        rows = [.. RowWindow.Apply(request.Rows, rows)];
        if (request.Count)
        {
            CountOutput.WriteCount(
                rows.Count,
                request.OutputPath,
                request.Rows);
            return 0;
        }

        var view = new DetailedDiscoveryView
        {
            Items = rows,
        };
        var context = new DiscoveryContext();
        OutputDestination.Write(
            request.OutputPath,
            request.Rows,
            output =>
            {
                if (request.Format == OutputFormat.Json)
                {
                    output.WriteLine(
                        JsonSerializer.Serialize(
                            rows,
                            DetailedDiscoveryJsonContext
                                .Default
                                .ListDetailedDiscoveryRow));
                }
                else if (request.Format == OutputFormat.Markdown)
                {
                    context.Serialize(
                        view,
                        output,
                        new MarkdownFormatter());
                }
                else if (request.Format == OutputFormat.PlainText)
                {
                    context.Serialize(
                        view,
                        output,
                        new PlainTextFormatter());
                }
                else
                {
                    bool tsv = request.Format == OutputFormat.Tsv;
                    bool jsonl = request.Format == OutputFormat.Jsonl;
                    OutputFormatter.WriteTable(
                        output,
                        showHeader: tsv && !request.NoHeader,
                        (writer, formatter) => context.Serialize(
                            view,
                            writer,
                            formatter,
                            OutputFormatter.CreateTableWriterOptions(
                                tsv,
                                jsonl)));
                }
            });
        return 0;
    }

    private static List<DetailedDiscoveryRow>? ResolveRows(
        string[]? discover,
        DocumentSchema schema,
        IReadOnlyList<string> knownSections,
        IReadOnlyDictionary<string, string[]> categories,
        IReadOnlySet<string> catalogHiddenSections,
        IReadOnlySet<string>? listedCategoryDoors,
        OutputCapabilityCatalog capabilities)
    {
        if (discover is null or { Length: 0 })
        {
            return
            [
                .. DiscoverOutput.GetTopLevelRows(
                        schema,
                        categories,
                        catalogHiddenSections,
                        listedCategoryDoors)
                    .Select(row => CreateRow(
                        row.Name,
                        row.Kind,
                        categories.TryGetValue(
                            row.Name,
                            out string[]? members)
                            ? capabilities.FormatsForSelection(
                                KnownMembers(members, knownSections))
                            : capabilities.FormatsForSection(row.Name))),
            ];
        }

        string selector = discover[0];
        if (SelectResolver.TryResolveCategory(
                selector,
                categories,
                knownSections,
                out string category,
                out string[] members))
        {
            string[] knownMembers =
                KnownMembers(members, knownSections);
            return
            [
                CreateRow(
                    category,
                    "category",
                    capabilities.FormatsForSelection(knownMembers)),
                .. knownMembers.Select(member => CreateRow(
                    member,
                    "section",
                    capabilities.FormatsForSection(member))),
            ];
        }

        var (matches, miss, isExact) =
            SelectResolver.ResolveSingleWithProvenance(
                selector,
                knownSections);
        if (miss is not null)
        {
            SelectOutput.WriteUnresolved(
                new SelectResult(
                    null,
                    [miss]));
            return null;
        }

        if (!isExact || matches.Count != 1)
        {
            CommandError.Write(
                "--details requires an exact category or section selector; "
                + "glob expansion is not supported.");
            return null;
        }

        string section = matches[0];
        return
        [
            CreateRow(
                section,
                "section",
                capabilities.FormatsForSection(section)),
        ];
    }

    private static string[] KnownMembers(
        IEnumerable<string> members,
        IReadOnlyList<string> knownSections) =>
        [
            .. members
                .Where(member => knownSections.Contains(
                    member,
                    StringComparer.OrdinalIgnoreCase))
                .OrderBy(
                    member => member,
                    StringComparer.OrdinalIgnoreCase),
        ];

    private static DetailedDiscoveryRow CreateRow(
        string name,
        string kind,
        IEnumerable<OutputMode> formats) =>
        new(
            name,
            kind,
            [.. formats.Select(OutputCapabilityCatalog.CliOption)]);
}

[JsonSerializable(typeof(List<DetailedDiscoveryRow>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class DetailedDiscoveryJsonContext
    : JsonSerializerContext
{
}
