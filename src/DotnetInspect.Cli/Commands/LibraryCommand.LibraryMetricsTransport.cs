using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;

using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

using ILInspector.Analysis;
using ILInspector.Research;

namespace DotnetInspect.Cli.Commands;

public partial class LibraryCommand
{
    private static readonly InspectionEnvelopeJsonContract<
        LibraryStructuralReportDocument> s_libraryMetricsJson =
        new(
            "library-metrics",
            1,
            LibraryMetricsInspectionJson.Write);

    internal static bool IsExactLibraryMetricsSelection(
        LibraryOptions options)
    {
        if (options.SelectDefault)
            return false;

        if (options.IncludeSections is { } included)
        {
            return included.Count == 1
                && included.Contains(SectionNames.LibraryMetrics)
                && options.ExactIncludeSections is { Count: 1 } exact
                && exact.Contains(SectionNames.LibraryMetrics);
        }

        string[] selectors =
        [
            .. (options.Select ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        return selectors is [var selector]
            && selector.Equals(
                SectionNames.LibraryMetrics,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static bool RequestsLibraryMetricsTransport(
        LibraryOptions options) =>
        IsExactLibraryMetricsSelection(options)
        && (options.EnvelopeOutput || options.JsonOutput);

    internal static bool ValidateLibraryMetricsTransport(
        LibraryOptions options)
    {
        if (!RequestsLibraryMetricsTransport(options))
            return true;

        if (options.Count
            || options.Rows is not null
            || options.Fields is { Length: > 0 }
            || options.Columns is { Length: > 0 }
            || options.Print
            || options.PrintRow is not null
            || options.ProjectionRow is not null
            || options.Value
            || options.Urls
            || options.Paths
            || options.JsonArray
            || options.Tree
            || options.NoHeader
            || options.Discover is not null
            || options.Schema
            || options.Effective
            || options.ExtractResources is not null
            || options.IncludeReferences
            || options.ReferenceHierarchyDepth is not null)
        {
            CommandError.Write(
                "Complete Library Metrics JSON does not support row, field, "
                    + "column, count, discovery, or presentation projections.");
            return false;
        }

        if (options.EnvelopeOutput
            && options.FormatFlagExplicitlySet)
        {
            CommandError.Write(
                "Library Metrics --envelope cannot be combined with another "
                    + "output format.");
            return false;
        }

        if (string.Equals(
                options.Tfm,
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            CommandError.Write(
                "Complete Library Metrics JSON requires one target framework; "
                    + "--tfm all is not supported.");
            return false;
        }

        return true;
    }

    private static int WriteLibraryMetricsTransport(
        LibraryInspection inspection,
        LibraryOptions options)
    {
        switch (inspection.LibraryMetricsQueryResult)
        {
            case LibraryMetricsResult.Available available:
                InspectionEnvelope<LibraryStructuralReportDocument> envelope =
                    LibraryMetricsInspection.Execute(
                        available.Document);
                return InspectionEnvelopeOutput.TryWrite(
                    envelope,
                    s_libraryMetricsJson,
                    options.EnvelopeOutput,
                    options.CompactJson,
                    options.OutputPath)
                        ? 0
                        : 1;

            case LibraryMetricsResult.Unavailable unavailable:
                CommandError.Write(
                    "Library Metrics is unavailable: "
                        + unavailable.Outcome.Message);
                WriteAnalysisDiagnostics(
                    unavailable.Outcome.Receipt.Diagnostics);
                WriteAnalysisDiagnostics(
                    unavailable.Outcome.Coverage.Diagnostics);
                return 1;

            case LibraryMetricsResult.NoMetadata:
                CommandError.Write(
                    "Library Metrics is unavailable because the selected "
                        + "Library contains no managed metadata.");
                return 1;

            case LibraryMetricsResult.Failed failed:
                CommandError.Write(failed.Error);
                return 1;

            case null:
                CommandError.Write(
                    "Library Metrics produced no terminal result.");
                return 1;

            default:
                throw new InvalidOperationException(
                    "Unknown Library Metrics result.");
        }
    }

    private static void WriteAnalysisDiagnostics(
        IEnumerable<AnalysisDiagnostic> diagnostics)
    {
        foreach (AnalysisDiagnostic diagnostic in diagnostics)
            CommandError.WriteWarning(diagnostic.Message);
    }
}
