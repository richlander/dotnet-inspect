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

/// <summary>
/// A printable row before its payload is acquired. Cardinality and <c>--row</c> are decided from
/// row identity alone, so a caller whose payload costs something can resolve the selection first
/// and read only the document it is about to emit.
/// </summary>
public sealed record PrintableRow(
    int Row,
    string Section,
    string Label,
    string? Path,
    string? Url);

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
        // Callers whose payloads are already in hand keep passing documents. Identity is by
        // reference so two rows with equal fields still resolve to their own content.
        var content = new Dictionary<PrintableRow, string>(ReferenceEqualityComparer.Instance);
        var rows = new List<PrintableRow>(documents.Count);
        foreach (var document in documents)
        {
            var row = new PrintableRow(document.Row, document.Section, document.Label, document.Path, document.Url);
            content[row] = document.Content;
            rows.Add(row);
        }

        return Write(rows, row => content[row], options);
    }

    /// <summary>
    /// Answers a print request over <paramref name="rows"/>, calling <paramref name="readContent"/>
    /// exactly once, for the single row that is emitted, and not at all when the request is
    /// refused. Only one document is ever written -- the JSON shapes serialize the selected row
    /// too -- so acquiring the others would be work the request never authorized.
    /// </summary>
    public static int Write(
        IReadOnlyList<PrintableRow> rows,
        Func<PrintableRow, string> readContent,
        PrintProjectionOptions options)
    {
        ProjectionAudit.MarkHonored(ProjectionAudit.Print);

        if (rows.Count == 0)
        {
            Console.Error.WriteLine("Error: selected section has no printable rows.");
            return 1;
        }

        PrintableRow selectedRow;
        if (options.Row is { } selector)
        {
            // Same rule as the shape projections: the ordinal names the row the
            // reader saw, and every row already carries that number.
            var rowNumbers = rows.Select(row => row.Row).ToList();
            var row = selector.Resolve(rowNumbers);
            var position = RowNumbering.IndexOf(rowNumbers, row);
            if (position < 0)
            {
                Console.Error.WriteLine(
                    $"Error: row {row} is not in this section. Use --row {RowNumbering.Describe(rowNumbers)}, first, or last.");
                return 1;
            }

            selectedRow = rows[position];
        }
        else
        {
            if (rows.Count != 1)
            {
                Console.Error.WriteLine($"Error: selected section has {rows.Count} printable rows; use --row N|first|last to choose one row.");
                return 1;
            }

            selectedRow = rows[0];
        }

        var selected = new PrintableDocument(
            selectedRow.Row,
            selectedRow.Section,
            selectedRow.Label,
            selectedRow.Path,
            selectedRow.Url,
            readContent(selectedRow));

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
