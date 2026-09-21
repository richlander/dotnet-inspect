using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Views;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class DetailedDiscoverOutput
{
    public static int Validate(DiscoveryOutputRequest request)
    {
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

        return 0;
    }

    public static int Write(
        DiscoveryDocumentFactory.Projection projection,
        DiscoveryOutputRequest request)
    {
        List<DetailedDiscoveryRow> rows = ResolveRows(projection);

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

    private static List<DetailedDiscoveryRow> ResolveRows(
        DiscoveryDocumentFactory.Projection projection)
    {
        DiscoveryDocument document = projection.Document;
        IReadOnlyDictionary<DiscoveryResourceIdentity, ResourcePath> paths =
            projection.ResourcePaths.ToDictionary(
                static registration => registration.Identity,
                static registration => registration.Path);
        if (document.Selection.IsCatalog)
        {
            return
            [
                .. document.Selection.Rows.Select(identity =>
                    CreateRow(
                        document.GetResource(identity),
                        paths[identity])),
            ];
        }

        DiscoveryResourceIdentity addressed =
            document.Selection.AddressedResources.Single();
        DiscoveryResource resource = document.GetResource(addressed);
        if (addressed.Kind == DiscoveryResourceKind.Category)
        {
            return
            [
                CreateRow(resource, paths[resource.Identity]),
                .. document.Selection.Rows.Select(identity =>
                    CreateRow(
                        document.GetResource(identity),
                        paths[identity])),
            ];
        }

        return [CreateRow(resource, paths[resource.Identity])];
    }

    private static DetailedDiscoveryRow CreateRow(
        DiscoveryResource resource,
        ResourcePath path) =>
        new(
            resource.Identity.Name,
            resource.Identity.Kind == DiscoveryResourceKind.Item
                ? resource.Identity.ItemKind!
                : resource.Identity.Kind.ToString().ToLowerInvariant(),
            path.Value,
            [
                .. resource.OutputModes.Select(
                    OutputCapabilityCatalog.CliOption),
            ]);
}

[JsonSerializable(typeof(List<DetailedDiscoveryRow>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class DetailedDiscoveryJsonContext
    : JsonSerializerContext
{
}
