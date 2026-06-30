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
    bool PrintAll,
    int? Row,
    bool JsonOutput,
    bool Jsonl,
    bool Bare,
    string? OutputPath);

public static class PrintProjectionOutput
{
    public static int Write(IReadOnlyList<PrintableDocument> documents, PrintProjectionOptions options)
    {
        if (documents.Count == 0)
        {
            Console.Error.WriteLine("Error: selected section has no printable rows.");
            return 1;
        }

        if (options.PrintAll && options.Row is not null)
        {
            Console.Error.WriteLine("Error: --print-all cannot be combined with --row.");
            return 1;
        }

        IReadOnlyList<PrintableDocument> selected;
        if (options.PrintAll)
        {
            if (options.Bare && !options.JsonOutput && !options.Jsonl)
            {
                Console.Error.WriteLine("Error: --bare cannot be combined with --print-all because multiple documents need separators.");
                return 1;
            }

            selected = documents;
        }
        else if (options.Row is { } row)
        {
            if (row < 1 || row > documents.Count)
            {
                Console.Error.WriteLine($"Error: printable row {row} is out of range. Use 1 through {documents.Count}.");
                return 1;
            }

            selected = [documents[row - 1]];
        }
        else
        {
            if (documents.Count != 1)
            {
                Console.Error.WriteLine($"Error: selected section has {documents.Count} printable rows; use --row N to choose one row or --print-all.");
                return 1;
            }

            selected = [documents[0]];
        }

        if (options.Jsonl)
        {
            var lines = selected
                .Select(item => JsonSerializer.Serialize(item, PrintProjectionJsonContext.Default.PrintableDocument));
            WriteOutput(string.Join(Environment.NewLine, lines) + Environment.NewLine, options.OutputPath);
            return 0;
        }

        if (options.JsonOutput)
        {
            var json = selected.Count == 1
                ? JsonSerializer.Serialize(selected[0], PrintProjectionJsonContext.Default.PrintableDocument)
                : JsonSerializer.Serialize(selected.ToArray(), PrintProjectionJsonContext.Default.PrintableDocumentArray);
            WriteOutput(json, options.OutputPath);
            return 0;
        }

        if (selected.Count == 1)
        {
            WriteOutput(selected[0].Content, options.OutputPath);
            return 0;
        }

        var rendered = string.Join(
            Environment.NewLine + Environment.NewLine,
            selected.Select(item => $"--- {item.Label} ---{Environment.NewLine}{item.Content.TrimEnd()}"));
        WriteOutput(rendered + Environment.NewLine, options.OutputPath);
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
