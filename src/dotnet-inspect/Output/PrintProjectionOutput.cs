using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Output;

public sealed record PrintableDocument(
    int Row,
    string Section,
    string Label,
    string? Path,
    string? Url,
    string Content);

public sealed record PrintProjectionOptions(
    RowSelector? Row,
    bool JsonOutput,
    bool Jsonl,
    bool JsonArray,
    bool Bare,
    string? OutputPath);

public static class PrintProjectionOutput
{
    public static int Write(IReadOnlyList<PrintableDocument> documents, PrintProjectionOptions options)
    {
        ProjectionAudit.MarkHonored(ProjectionAudit.Print);

        if (documents.Count == 0)
        {
            Console.Error.WriteLine("Error: selected section has no printable rows.");
            return 1;
        }

        PrintableDocument selected;
        if (options.Row is { } selector)
        {
            var row = selector.Resolve(documents.Count);
            if (row < 1 || row > documents.Count)
            {
                Console.Error.WriteLine($"Error: printable row {row} is out of range. Use 1 through {documents.Count}, first, or last.");
                return 1;
            }

            selected = documents[row - 1];
        }
        else
        {
            if (documents.Count != 1)
            {
                Console.Error.WriteLine($"Error: selected section has {documents.Count} printable rows; use --row N|first|last to choose one row.");
                return 1;
            }

            selected = documents[0];
        }

        if (options.Jsonl)
        {
            WriteOutput(
                JsonSerializer.Serialize(selected, PrintProjectionJsonContext.Default.PrintableDocument) + Environment.NewLine,
                options.OutputPath);
            return 0;
        }

        if (options.JsonArray)
        {
            WriteOutput(JsonSerializer.Serialize(new[] { selected }, PrintProjectionJsonContext.Default.PrintableDocumentArray), options.OutputPath);
            return 0;
        }

        if (options.JsonOutput)
        {
            WriteOutput(JsonSerializer.Serialize(selected, PrintProjectionJsonContext.Default.PrintableDocument), options.OutputPath);
            return 0;
        }

        WriteOutput(selected.Content, options.OutputPath);
        return 0;
    }

    private static void WriteOutput(string output, string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
            File.WriteAllText(outputPath, output);
        else
            Console.Write(output);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PrintableDocument))]
[JsonSerializable(typeof(PrintableDocument[]))]
internal partial class PrintProjectionJsonContext : JsonSerializerContext
{
}
