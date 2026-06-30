using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Output;

public enum ShapeProjectionKind
{
    Value,
    Urls,
    Paths
}

public sealed record ShapeProjectionRow(
    int Row,
    string Section,
    string Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path = null);

public sealed record ShapeProjectionOptions(
    ShapeProjectionKind Kind,
    int? Row,
    bool JsonOutput,
    bool Jsonl,
    bool JsonArray);

public static class ShapeProjectionOutput
{
    public static int ActiveShapeCount(bool value, bool urls, bool paths)
        => (value ? 1 : 0) + (urls ? 1 : 0) + (paths ? 1 : 0);

    public static ShapeProjectionKind GetKind(bool value, bool urls, bool paths)
        => value ? ShapeProjectionKind.Value
            : urls ? ShapeProjectionKind.Urls
            : ShapeProjectionKind.Paths;

    public static bool ValidateSingleSection(HashSet<string>? includeSections, string optionName)
    {
        if (includeSections is { Count: 1 })
            return true;

        Console.Error.WriteLine($"Error: {optionName} requires -S/--select to match exactly one section.");
        return false;
    }

    public static int Write(IReadOnlyList<ShapeProjectionRow> rows, ShapeProjectionOptions options)
    {
        if (rows.Count == 0)
        {
            Console.Error.WriteLine($"Error: selected section has no {ProjectionName(options.Kind)} values.");
            return 1;
        }

        IReadOnlyList<ShapeProjectionRow> selected = rows;
        if (options.Row is { } row)
        {
            if (row < 1 || row > rows.Count)
            {
                Console.Error.WriteLine($"Error: row {row} is out of range. Use --row 1 through {rows.Count}.");
                return 1;
            }

            selected = [rows[row - 1]];
        }

        if (options.Kind == ShapeProjectionKind.Value && selected.Count != 1)
        {
            Console.Error.WriteLine($"Error: --value found {selected.Count} rows; use --row N or select a single-row section.");
            return 1;
        }

        if (options.Jsonl)
        {
            foreach (var item in selected)
                Console.WriteLine(JsonSerializer.Serialize(item, ShapeProjectionJsonContext.Default.ShapeProjectionRow));
            return 0;
        }

        if (options.JsonArray)
        {
            Console.Write(JsonSerializer.Serialize(selected.ToArray(), ShapeProjectionJsonContext.Default.ShapeProjectionRowArray));
            return 0;
        }

        if (options.JsonOutput)
        {
            var json = selected.Count == 1
                ? JsonSerializer.Serialize(selected[0], ShapeProjectionJsonContext.Default.ShapeProjectionRow)
                : JsonSerializer.Serialize(selected.ToArray(), ShapeProjectionJsonContext.Default.ShapeProjectionRowArray);
            Console.Write(json);
            return 0;
        }

        foreach (var item in selected)
            Console.WriteLine(item.Value);
        return 0;
    }

    private static string ProjectionName(ShapeProjectionKind kind) => kind switch
    {
        ShapeProjectionKind.Urls => "URL",
        ShapeProjectionKind.Paths => "path",
        _ => "scalar"
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ShapeProjectionRow))]
[JsonSerializable(typeof(ShapeProjectionRow[]))]
internal partial class ShapeProjectionJsonContext : JsonSerializerContext
{
}
