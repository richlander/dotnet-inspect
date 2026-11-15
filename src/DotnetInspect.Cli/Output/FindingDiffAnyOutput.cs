using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Views;
using DotnetInspector.Diff;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// Writes the Changed Types and Changed Members sections: a Finding Diff Any
/// route's Table as rows, its Document as JSON, or a Markdown view of the
/// Document.
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

    internal static readonly InspectionEnvelopeJsonContract<FindingDiffAnyTypeDocument>
        TypeDocumentJsonContract = new(
            "finding-diff-any-members",
            1,
            FindingDiffAnyTypeJsonContext.Default.FindingDiffAnyTypeDocument);

    internal static void WriteRows(
        FindingDiffAnyTypeTable table,
        DiffOptions options)
        => OutputFormatter.WriteProjectedTable(
            Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
            options.Columns, options.Fields,
            (output, formatter, writerOptions) =>
            {
                var writer = new MarkoutWriter(output, formatter, writerOptions);
                writer.WriteTable(
                    ["Owner", "Member", "API", "Body", "Selector"],
                    ["owner", "member", "api", "body", "selector"],
                    [
                        .. table.Rows.Select(static row => new[]
                        {
                            row.Owner.ToString(),
                            DiffViewText.Field(row.Member ?? "").ToString(),
                            row.Api.ToString(),
                            row.Body.ToString(),
                            DiffViewText.Field(row.Selector ?? "").ToString(),
                        }),
                    ]);
                writer.Flush();
            },
            options.Rows);

    internal static bool WriteJson(
        InspectionEnvelope<FindingDiffAnyTypeDocument> inspection,
        DiffOptions options)
        => InspectionEnvelopeOutput.TryWrite(
            inspection,
            TypeDocumentJsonContract,
            options.EnvelopeOutput,
            options.CompactJson);

    internal static string RenderMarkdown(
        FindingDiffAnyTypeDocument document,
        DiffOptions options)
    {
        FindingDiffAnyTypeComparison comparison = document.Comparison;
        FindingDiffAnyTypeCounts counts = document.Counts!;
        int compared = document.Receipt!.ComparedMembers;
        var view = new ChangedMembersView(
            DiffViewText.Field($"Changed Members: {document.FullName ?? comparison.Type}"),
            DiffViewText.Field($"{comparison.Name} {comparison.BeforeVersion} -> {comparison.AfterVersion}"))
        {
            Api = counts.Api.ToString(),
            Body = counts.Body.ToString(),
            Compared = compared,
            ApiChanged = counts.ApiChanged,
            ApiIndeterminate = counts.ApiIndeterminate,
            BodyChanged = counts.BodyChanged,
            BodyIndeterminate = counts.BodyIndeterminate,
            Status = new Callout(CalloutSeverity.Note, Status(document.Rows, compared)),
            Rows = document.Rows.Length == 0
                ? null
                : [
                    .. document.Rows.Select(static row => new ChangedMembersRow(
                        row.Owner.ToString(),
                        row.Member ?? "",
                        row.Api.ToString(),
                        row.Body.ToString(),
                        row.Selector ?? "")),
                ],
        };
        var writer = new MarkoutWriter(
            new MarkdownFormatter(),
            OutputFormatter.CreateWindowedOptions(options.Rows));
        DiffViewContext.Default.Serialize(view, writer);
        return writer.Complete().TrimEnd();
    }

    static string Status(
        System.Collections.Immutable.ImmutableArray<FindingDiffAnyTypeRow> rows,
        int compared)
    {
        int members = rows.Count(static row => row.Owner == FindingDiffAnyTypeOwner.Member);
        bool residual = members != rows.Length;
        if (rows.Length == 0)
            return $"No compared Member differs, nor does the Type's own declaration; {compared} compared.";
        bool undecided = rows.Any(static row =>
            row.Api == FindingDiffAnyState.Indeterminate || row.Body == FindingDiffAnyState.Indeterminate);
        string verb = undecided
            ? members == 1 ? "differs or is undecided" : "differ or are undecided"
            : members == 1 ? "differs" : "differ";
        string memberText = $"{members} of {compared} compared Members {verb}";
        return residual
            ? $"{memberText}; the Type's own declaration or presence {(undecided ? "differs or is undecided" : "differs")}."
            : $"{memberText}.";
    }
}
