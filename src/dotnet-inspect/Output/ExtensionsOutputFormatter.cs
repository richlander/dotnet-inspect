using DotnetInspector.Commands;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Formats extension method search results for display.
/// </summary>
public static class ExtensionsOutputFormatter
{
    public static string FormatResults(string targetType, List<ExtensionMethodResult> results)
    {
        var writer = new MarkoutWriter();

        writer.WriteHeading(1, $"Extension Methods for {targetType}");

        // Group by source, then by reachable path
        var directExtensions = results.Where(r => r.ReachablePath == null).ToList();
        var reachableExtensions = results.Where(r => r.ReachablePath != null)
            .GroupBy(r => r.ReachablePath)
            .ToList();

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

    private static void WriteExtensionTable(MarkoutWriter writer, List<ExtensionMethodResult> results)
    {
        var byClass = results.GroupBy(r => r.ExtensionClass).ToList();

        var headers = new[] { "Name", "Kind", "Class", "Assembly", "Source" };
        var rows = byClass.SelectMany(classGroup => classGroup.Select(ext =>
        {
            var sourceDisplay = ext.SourceVersion != null
                ? $"{ext.Source}@{ext.SourceVersion}"
                : ext.Source;
            return new[] { ext.MethodName, ext.Kind, ext.ExtensionClass ?? "", ext.Assembly ?? "", sourceDisplay ?? "" };
        }));
        writer.WriteTable(headers, rows);
    }
}
