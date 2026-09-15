using System.Globalization;
using DotnetInspect.Cli.Models;

namespace DotnetInspect.Cli.Output;

internal static class ContainmentDiagnosticOutput
{
    public static void Write(ContainmentSelectedText? selected)
    {
        if (selected?.Diagnostic is not { } diagnostic)
            return;

        var details = diagnostic.Ranges
            .Select(FormatRange)
            .ToList();
        int omitted = diagnostic.TotalRangeCount - diagnostic.Ranges.Count;
        if (omitted > 0)
        {
            details.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{omitted} additional {(omitted == 1 ? "range" : "ranges")} omitted."));
        }

        CommandError.WriteWarning(
            $"Skill document '{diagnostic.Source}' was omitted because "
            + $"{diagnostic.TotalRangeCount} text "
            + $"{(diagnostic.TotalRangeCount == 1 ? "range requires" : "ranges require")} containment.",
            [.. details]);
    }

    private static string FormatRange(ContainmentConcernRange range)
    {
        string location = range.StartLine == range.EndLine
            ? range.StartColumn == range.EndColumn
                ? $"line {range.StartLine}, column {range.StartColumn}"
                : $"line {range.StartLine}, columns {range.StartColumn}-{range.EndColumn}"
            : $"line {range.StartLine}, column {range.StartColumn} through "
                + $"line {range.EndLine}, column {range.EndColumn}";
        string scalar = string.Create(
            CultureInfo.InvariantCulture,
            $"U+{range.Scalar:X4} ({range.Category})");
        return range.ScalarCount == 1
            ? $"{location}: {scalar}"
            : $"{location}: {range.ScalarCount} x {scalar}";
    }
}
