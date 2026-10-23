using System.Globalization;

using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Views;

using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

using Markout;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// Writes the Dependency Structure Table representation: one row per selected
/// namespace edge under one schema, with no Document summary
/// (docs/design/section-shapes.md#consumer-selection).
/// </summary>
internal static class LibraryDependencyStructureTableOutput
{
    internal static readonly string[] Headers =
    [
        "From",
        "To",
        "Calls",
        "Function References",
        "From Level",
        "From Cycle",
        "To Level",
        "To Cycle",
    ];

    internal static readonly string[] StableHeaders =
    [
        "from",
        "to",
        "calls",
        "function_references",
        "from_level",
        "from_cycle",
        "to_level",
        "to_cycle",
    ];

    internal static void Write(
        LibraryDependencyStructureQueryResult.Available available,
        LibraryOptions options)
    {
        InspectionEnvelope<LibraryDependencyStructureTable> table =
            LibraryDependencyStructureInspection.TableEnvelope(available);
        WriteDiagnostics(table.Diagnostics);
        string[][] rows = [.. Values(table.Content)];
        OutputDestination.Write(
            options.OutputPath,
            options.Rows,
            output => OutputFormatter.WriteProjectedTable(
                output,
                !options.NoHeader,
                options.Tsv,
                options.Jsonl,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    var markout = new MarkoutWriter(
                        writer,
                        formatter,
                        writerOptions);
                    markout.WriteTable(Headers, StableHeaders, rows);
                    markout.Flush();
                },
                options.Rows));
    }

    internal static IEnumerable<string[]> Values(
        LibraryDependencyStructureTable table) =>
        table.Rows.Select(static row => new[]
        {
            LibraryViewText.Contain(row.From),
            LibraryViewText.Contain(row.To),
            Number(row.Calls),
            Number(row.FunctionReferences),
            Number(row.FromLevel),
            row.FromCycle is { } fromCycle ? Number(fromCycle) : "",
            Number(row.ToLevel),
            row.ToCycle is { } toCycle ? Number(toCycle) : "",
        });

    internal static void WriteDiagnostics(
        IReadOnlyList<InspectionDiagnostic> diagnostics)
    {
        foreach (InspectionDiagnostic diagnostic in diagnostics)
        {
            string summary = diagnostic.Summary.ToString();
            switch (diagnostic.Severity)
            {
                case InspectionDiagnosticSeverity.Information:
                    CommandError.WriteNote(summary);
                    break;
                case InspectionDiagnosticSeverity.Warning:
                    CommandError.WriteWarning(summary);
                    break;
                default:
                    CommandError.Write(summary);
                    break;
            }
        }
    }

    private static string Number(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
