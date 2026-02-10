using DotnetInspector.Commands;
using DotnetInspector.Options;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Formats extension method search results for display.
/// </summary>
public static class ExtensionsOutputFormatter
{
    public static string FormatResults(string targetType, List<ExtensionMethodResult> results,
        Verbosity verbosity = Verbosity.Normal)
    {
        var writer = new MarkoutWriter();

        writer.WriteHeading(1, $"Extension Methods for {targetType}");

        // Group by source, then by reachable path
        var directExtensions = results.Where(r => r.ReachablePath == null).ToList();
        var reachableExtensions = results.Where(r => r.ReachablePath != null)
            .GroupBy(r => r.ReachablePath)
            .ToList();

        if (verbosity == Verbosity.Quiet)
        {
            WriteCountsTable(writer, targetType, directExtensions, reachableExtensions);
            return writer.ToString();
        }

        // Direct extensions (always shown)
        writer.WriteHeading(2, $"{targetType} Extensions ({directExtensions.Count})");
        if (directExtensions.Count > 0)
            WriteExtensionTable(writer, directExtensions);
        else
            writer.WriteParagraph("None found.");

        // Reachable extensions
        foreach (var group in reachableExtensions)
        {
            var reachableType = group.First().ReachableFromType;
            writer.WriteHeading(2, $"{reachableType} Extensions ({group.Count()}; Via {group.Key})");
            WriteExtensionTable(writer, group.ToList());
        }

        return writer.ToString();
    }

    private static void WriteCountsTable(MarkoutWriter writer,
        string targetType,
        List<ExtensionMethodResult> directExtensions,
        List<IGrouping<string?, ExtensionMethodResult>> reachableExtensions)
    {
        var headers = new[] { "Type", "Extensions", "Via" };
        List<string[]> rows = [];

        rows.Add([targetType, directExtensions.Count.ToString(), ""]);

        foreach (var group in reachableExtensions)
        {
            var reachableType = group.First().ReachableFromType ?? "";
            rows.Add([reachableType, group.Count().ToString(), group.Key ?? ""]);
        }

        writer.WriteTable(headers, rows);
    }

    private static void WriteExtensionTable(MarkoutWriter writer, List<ExtensionMethodResult> results)
    {
        var ordered = results.OrderBy(r => r.ExtensionClass).ThenBy(r => r.MethodName);

        var headers = new[] { "Name", "Kind", "Class", "Library", "Source" };
        var rows = ordered.Select(ext =>
        {
            var sourceDisplay = ext.SourceVersion != null
                ? $"{ext.Source}@{ext.SourceVersion}"
                : ext.Source;
            var name = ext.Overloads > 1
                ? $"{ext.MethodName} ({ext.Overloads} overloads)"
                : ext.MethodName;
            return new[] { name, ext.Kind, ext.ExtensionClass ?? "", ext.Assembly ?? "", sourceDisplay ?? "" };
        });
        writer.WriteTable(headers, rows);
    }
}
