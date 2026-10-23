using System.Globalization;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Views;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class DiffAnalysisOutput
{
    internal static readonly InspectionEnvelopeJsonContract<DiffAnalysisDocument>
        JsonContract = new(
            "diff-analysis",
            2,
            DiffAnalysisInspectionJsonContext.Default.DiffAnalysisDocument);

    internal static readonly InspectionEnvelopeJsonContract<DiffAnalysisSummaryTable>
        SummaryTableJsonContract = new(
            "diff-analysis-summary-table", 1,
            DiffAnalysisInspectionJsonContext.Default.DiffAnalysisSummaryTable);

    internal static readonly InspectionEnvelopeJsonContract<DiffAnalysisTransitionsTable>
        TransitionsTableJsonContract = new(
            "diff-analysis-transitions-table", 1,
            DiffAnalysisInspectionJsonContext.Default.DiffAnalysisTransitionsTable);

    internal static void WriteSummaryRows(
        DiffAnalysisSummaryTable table,
        DiffOptions options)
        => WriteRows(
            ["Analysis", "Outcome", "Added", "Removed", "Changed", "Present", "Detail"],
            ["analysis", "outcome", "added", "removed", "changed", "present", "detail"],
            table.Rows.Select(row => new[]
            {
                row.Analysis, row.Outcome.ToString(),
                row.Added.ToString(CultureInfo.InvariantCulture),
                row.Removed.ToString(CultureInfo.InvariantCulture),
                row.Changed.ToString(CultureInfo.InvariantCulture),
                row.Present.ToString(CultureInfo.InvariantCulture),
                row.Detail ?? "",
            }).ToArray(),
            options);

    internal static void WriteTransitionRows(
        DiffAnalysisTransitionsTable table,
        DiffOptions options)
        => WriteRows(
            ["Transition", "Finding", "Target", "From", "To", "Old", "New", "Detail", "Old Inspection", "New Inspection"],
            ["transition", "finding", "target", "from", "to", "old", "new", "detail", "old_inspection", "new_inspection"],
            table.Rows.Select(row => new[]
            {
                row.Transition, row.Finding, row.Target, row.From, row.To,
                row.Old, row.New, row.Detail ?? "",
                row.OldInspection ?? "", row.NewInspection ?? "",
            }).ToArray(),
            options);

    private static void WriteRows(
        string[] headers,
        string[] keys,
        string[][] rows,
        DiffOptions options)
        => OutputFormatter.WriteProjectedTable(
            Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
            options.Columns, options.Fields,
            (output, formatter, writerOptions) =>
            {
                var writer = new MarkoutWriter(output, formatter, writerOptions);
                writer.WriteTable(headers, keys, rows.Select(row =>
                    row.Select(value => DiffViewText.Field(value).ToString()).ToArray()).ToArray());
                writer.Flush();
            },
            options.Rows);

    internal static bool Write(
        InspectionEnvelope<DiffAnalysisDocument> inspection,
        DiffOptions options)
        => InspectionEnvelopeOutput.TryWrite(
            inspection,
            JsonContract,
            options.EnvelopeOutput,
            options.CompactJson);
}
