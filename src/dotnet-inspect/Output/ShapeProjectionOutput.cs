using ILInspector.CSharp;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Output;

public enum ShapeProjectionKind
{
    Value,
    Urls,
    Paths
}

/// <summary>
/// One projected scalar, URL, or path, as printed by <c>--value</c> and by the
/// structured projection modes.
/// </summary>
/// <remarks>
/// Every text field here is untrusted: fourteen call sites across
/// <c>ApiCommand</c>, <c>LibraryCommand</c>, <c>PackageCommand</c>, and
/// <c>ProjectCommand</c> project type names, member signatures, nuspec fields,
/// ZIP entry paths, and SourceLink URLs into these rows, and the projection
/// path does not go through the section views that contain them. Containing at
/// each producer would restate the rule fourteen times, so it lives on the
/// record instead: a new projection cannot reopen the hole (issue #3319).
/// Enforced by
/// <c>UntrustedProjectionContainmentTests.ShapeProjectionRow_WithHostileText_ContainsEveryUntrustedField</c>,
/// which sets every field hostile and fails if one renders raw.
///
/// <c>Row</c> and <c>Section</c> are tool-owned -- a row number and a section
/// name drawn from a fixed set -- so neither is contained. Every positional
/// property is redeclared so the reflected order stays the constructor's, which
/// is what the structured serializers emit.
/// </remarks>
public sealed record ShapeProjectionRow(
    int Row,
    string Section,
    string Value,
    string? Label = null,
    string? Url = null,
    string? Path = null)
{
    /// <inheritdoc cref="ShapeProjectionRow"/>
    public int Row { get; init; } = Row;

    /// <inheritdoc cref="ShapeProjectionRow"/>
    public string Section { get; init; } = Section;

    /// <inheritdoc cref="ShapeProjectionRow"/>
    public string Value { get; init; } = CSharpIdentifier.ContainRenderedText(Value);

    /// <inheritdoc cref="ShapeProjectionRow"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; init; } = Label is null ? null : CSharpIdentifier.ContainRenderedText(Label);

    /// <inheritdoc cref="ShapeProjectionRow"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; } = Url is null ? null : CSharpIdentifier.ContainRenderedText(Url);

    /// <inheritdoc cref="ShapeProjectionRow"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; } = Path is null ? null : CSharpIdentifier.ContainRenderedText(Path);
}

public sealed record ShapeProjectionOptions(
    ShapeProjectionKind Kind,
    RowSelector? Row,
    bool JsonOutput,
    bool Jsonl,
    bool JsonArray,
    string? OutputPath = null);

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

        CommandError.Write($"{optionName} requires -S/--select to match exactly one section.");
        return false;
    }

    public static int Write(IReadOnlyList<ShapeProjectionRow> rows, ShapeProjectionOptions options)
    {
        ProjectionAudit.MarkHonored(options.Kind switch
        {
            ShapeProjectionKind.Value => ProjectionAudit.Value,
            ShapeProjectionKind.Urls => ProjectionAudit.Urls,
            _ => ProjectionAudit.Paths
        });

        if (rows.Count == 0)
        {
            CommandError.Write($"selected section has no {ProjectionName(options.Kind)} values.");
            return 1;
        }

        IReadOnlyList<ShapeProjectionRow> selected = rows;
        if (options.Row is { } selector)
        {
            // Address the row by the number it was rendered with. Indexing the
            // list positionally would return a different row whenever the
            // projection skipped one, and would do so without complaint.
            var rowNumbers = rows.Select(row => row.Row).ToList();
            var row = selector.Resolve(rowNumbers);
            var position = RowNumbering.IndexOf(rowNumbers, row);
            if (position < 0)
            {
                CommandError.Write(
                    $"row {row} is not in this section. Use --row {RowNumbering.Describe(rowNumbers)}, first, or last.");
                return 1;
            }

            selected = [rows[position]];
        }

        if (options.Kind == ShapeProjectionKind.Value && selected.Count != 1)
        {
            CommandError.Write($"--value found {selected.Count} rows; use --row N|first|last or select a single-row section.");
            return 1;
        }

        if (options.Jsonl)
        {
            string output = string.Concat(selected.Select(item =>
                JsonSerializer.Serialize(
                    item,
                    ShapeProjectionJsonContext.Default.ShapeProjectionRow)
                + '\n'));
            WriteOutput(output, options.OutputPath);
            return 0;
        }

        if (options.JsonArray)
        {
            WriteOutput(
                JsonSerializer.Serialize(
                    selected.ToArray(),
                    ShapeProjectionJsonContext.Default.ShapeProjectionRowArray),
                options.OutputPath);
            return 0;
        }

        if (options.JsonOutput)
        {
            var json = selected.Count == 1
                ? JsonSerializer.Serialize(selected[0], ShapeProjectionJsonContext.Default.ShapeProjectionRow)
                : JsonSerializer.Serialize(selected.ToArray(), ShapeProjectionJsonContext.Default.ShapeProjectionRowArray);
            WriteOutput(json, options.OutputPath);
            return 0;
        }

        WriteOutput(
            string.Concat(selected.Select(item => item.Value + '\n')),
            options.OutputPath);
        return 0;
    }

    private static void WriteOutput(string output, string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
            OutputPathWriter.Write(outputPath, output);
        else
            Console.Write(output);
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
