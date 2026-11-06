using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Views;
using DotnetInspector.Diff;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// Writes the Changed Types section: the Finding Diff Any route's Table as
/// rows, its Document as JSON, or a Markdown view of the Document.
/// </summary>
internal static class FindingDiffAnyOutput
{
    internal static readonly InspectionEnvelopeJsonContract<FindingDiffAnyLibraryDocument>
        DocumentJsonContract = new(
            "finding-diff-any-library",
            1,
            FindingDiffAnyLibraryJsonContext.Default.FindingDiffAnyLibraryDocument);

    internal static void WriteRows(
        FindingDiffAnyLibraryTable table,
        DiffOptions options)
        => OutputFormatter.WriteProjectedTable(
            Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
            options.Columns, options.Fields,
            (output, formatter, writerOptions) =>
            {
                var writer = new MarkoutWriter(output, formatter, writerOptions);
                writer.WriteTable(
                    ["Type", "API", "Body", "Identifier"],
                    ["type", "api", "body", "identifier"],
                    [
                        .. table.Rows.Select(static row => new[]
                        {
                            DiffViewText.Field(row.Type).ToString(),
                            row.Api.ToString(),
                            row.Body.ToString(),
                            DiffViewText.Field(row.Identifier).ToString(),
                        }),
                    ]);
                writer.Flush();
            },
            options.Rows);

    internal static bool WriteJson(
        InspectionEnvelope<FindingDiffAnyLibraryDocument> inspection,
        DiffOptions options)
        => InspectionEnvelopeOutput.TryWrite(
            inspection,
            DocumentJsonContract,
            options.EnvelopeOutput,
            options.CompactJson);

    internal static string RenderMarkdown(
        FindingDiffAnyLibraryDocument document,
        DiffOptions options)
    {
        FindingDiffAnyLibraryComparison comparison = document.Comparison;
        FindingDiffAnyLibraryCounts counts = document.Counts!;
        int compared = document.Receipt!.ComparedTypes;
        var view = new ChangedTypesView(
            DiffViewText.Field($"Changed Types: {comparison.Name}"),
            DiffViewText.Field($"{comparison.BeforeVersion} -> {comparison.AfterVersion}"))
        {
            Axes = comparison.Axes == FindingDiffAnyAxes.ApiAndBody
                ? "API and Body"
                : "API",
            Compared = compared,
            ApiChanged = counts.ApiChanged,
            ApiIndeterminate = counts.ApiIndeterminate,
            BodyChanged = counts.BodyChanged,
            BodyIndeterminate = counts.BodyIndeterminate,
            Status = new Callout(CalloutSeverity.Note, Status(counts, compared)),
            Rows = document.Rows.Length == 0
                ? null
                : [
                    .. document.Rows.Select(static row => new ChangedTypesRow(
                        row.Type,
                        row.Api.ToString(),
                        row.Body.ToString(),
                        row.Identifier)),
                ],
        };
        var writer = new MarkoutWriter(
            new MarkdownFormatter(),
            OutputFormatter.CreateWindowedOptions(options.Rows));
        DiffViewContext.Default.Serialize(view, writer);
        return writer.Complete().TrimEnd();
    }

    static string Status(FindingDiffAnyLibraryCounts counts, int compared)
    {
        if (counts.ReportedTypes == 0)
            return $"No compared Type differs; {compared} compared.";
        bool undecided = counts.ApiIndeterminate + counts.BodyIndeterminate > 0;
        bool one = counts.ReportedTypes == 1;
        string verb = undecided
            ? one ? "differs or is undecided" : "differ or are undecided"
            : one ? "differs" : "differ";
        return $"{counts.ReportedTypes} of {compared} compared Types {verb}.";
    }
}
