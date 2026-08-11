using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Options;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

/// <summary>
/// Helpers for the --count output mode.
/// </summary>
public static class CountOutput
{
    public const string SingleSectionRequiredMessage = "--count requires -S/--select to match exactly one section.";
    public const string SectionRequiredMessage = "--count requires -S/--select to match at least one section.";

    public static bool ValidateSingleSection(HashSet<string>? includeSections)
    {
        if (includeSections is { Count: 1 })
            return true;

        CommandError.Write(SingleSectionRequiredMessage);
        return false;
    }

    /// <summary>
    /// Validates that <c>--count</c> has selected at least one section. Unlike
    /// <see cref="ValidateSingleSection"/> this permits multi-section selection (e.g. a
    /// category such as <c>@Performance</c>), which renders a per-section count map.
    /// </summary>
    /// <param name="fixedOverview">
    /// Whether bare <c>-S</c> selected the fixed overview. That route carries its selection as a
    /// flag rather than as <paramref name="includeSections"/>, so reading the include set alone
    /// reports a selection the user did make as no selection at all (#3547).
    /// </param>
    public static bool ValidateSectionsSelected(HashSet<string>? includeSections, bool fixedOverview)
    {
        if (includeSections is { Count: >= 1 } || fixedOverview)
            return true;

        CommandError.Write(SectionRequiredMessage);
        return false;
    }

    /// <summary>
    /// Validates that the selected presentation can represent a multi-section count map.
    /// Scalar counts are format-independent bare numbers, so they remain valid for every format
    /// and ignore a tree presentation flag.
    /// </summary>
    public static bool ValidateMapFormat(
        OutputFormat format,
        IReadOnlyList<string>? orderedSections,
        bool tree = false)
    {
        if (orderedSections is null)
            return true;

        if (tree)
        {
            CommandError.Write(
                "--count cannot render multiple sections as --tree; --tree requires exactly one selected shape.");
            return false;
        }

        if (format != OutputFormat.Mermaid)
            return true;

        CommandError.Write(
            "--count cannot render multiple sections as Mermaid. Use Markdown, JSON, TSV, JSONL, table, or plain-text output.");
        return false;
    }

    /// <summary>
    /// Writes a single count and records that the <c>--count</c> projection was honored.
    /// Count-emitting paths should route through this rather than writing to the console
    /// directly, so the payload-projection audit can tell a rendered count from a dropped one.
    /// </summary>
    public static void WriteCount(int count) => WriteCount(count, null);

    /// <summary>
    /// Writes a count to <paramref name="outputPath"/>, or to stdout when it is null. A count is
    /// still the command's payload, so --out has to apply to it as it does to a full render.
    /// </summary>
    public static void WriteCount(int count, string? outputPath)
    {
        // Invariant: a count is machine-readable output, so it must not pick up
        // culture-specific digits or grouping from the ambient locale.
        WriteCountResult(count.ToString(CultureInfo.InvariantCulture), outputPath);
    }

    /// <summary>
    /// Writes an already-rendered scalar or structured count to <paramref name="outputPath"/>,
    /// or to stdout when it is null.
    /// </summary>
    public static void WriteCountResult(string result, string? outputPath)
    {
        ProjectionAudit.MarkHonored(ProjectionAudit.Count);
        var text = result.TrimEnd('\r', '\n') + '\n';
        if (string.IsNullOrEmpty(outputPath))
            Console.Write(text);
        else
            File.WriteAllText(outputPath, text);
    }

    internal static string Render(
        CountProjection projection,
        IReadOnlyList<string>? orderedSections,
        OutputFormat format,
        bool noHeader = false)
        => orderedSections is null
            ? projection.Total.ToString(CultureInfo.InvariantCulture)
            : RenderSectionCounts(projection.SectionCounts, orderedSections, format, noHeader);

    internal static void Write(
        CountProjection projection,
        IReadOnlyList<string>? orderedSections,
        OutputFormat format,
        bool noHeader = false,
        string? outputPath = null)
        => WriteCountResult(Render(projection, orderedSections, format, noHeader), outputPath);

    internal static string RenderSectionCounts(
        IReadOnlyDictionary<string, int> counts,
        IReadOnlyList<string> orderedSections,
        OutputFormat format,
        bool noHeader = false)
    {
        var rows = orderedSections
            .Select(section => new SectionCount(section, counts.GetValueOrDefault(section)))
            .ToArray();

        if (format == OutputFormat.Json)
            return JsonSerializer.Serialize(rows, CountOutputJsonContext.Default.SectionCountArray);

        var output = new StringWriter { NewLine = "\n" };
        IMarkoutFormatter formatter = format switch
        {
            OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl
                => new TableFormatter(showHeader: !noHeader),
            OutputFormat.PlainText => new PlainTextFormatter(),
            OutputFormat.Markdown => new MarkdownFormatter(),
            OutputFormat.Mermaid => throw new InvalidOperationException(
                "Mermaid count maps must be rejected before rendering."),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported count-map format.")
        };
        var options = new MarkoutWriterOptions();
        options.JsonTypedValues = true;
        if (format == OutputFormat.Tsv)
            options.TableMode = MarkoutTableMode.Tsv;
        else if (format == OutputFormat.Jsonl)
            options.TableMode = MarkoutTableMode.Jsonl;
        var writer = new MarkoutWriter(output, formatter, options);
        writer.WriteTable(
            ["Section", "Count"],
            ["section", "count"],
            rows.Select(row => new[]
            {
                row.Section,
                row.Count.ToString(CultureInfo.InvariantCulture)
            }).ToArray());
        writer.Flush();
        return output.ToString().TrimEnd();
    }
}

internal sealed record SectionCount(string Section, int Count);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true)]
[JsonSerializable(typeof(SectionCount[]))]
internal partial class CountOutputJsonContext : JsonSerializerContext
{
}
