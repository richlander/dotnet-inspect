using DotnetInspector.Commands;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Formats implementer search results for display.
/// </summary>
public static class ImplementsOutputFormatter
{
    public static string FormatResults(string targetType, List<ImplementerResult> results)
    {
        var writer = new MarkoutWriter();

        writer.WriteHeading(1, $"Types Implementing {targetType}");

        if (results.Count == 0)
        {
            writer.WriteParagraph("No implementing types found.");
            return writer.ToString();
        }

        writer.WriteField("Matches", results.Count);

        // Group by source
        var bySource = results.GroupBy(r => r.Source).ToList();

        foreach (var sourceGroup in bySource)
        {
            var source = sourceGroup.Key;
            var version = sourceGroup.First().SourceVersion;
            var sourceDisplay = version != null ? $"{source}@{version}" : source;

            writer.WriteHeading(2, sourceDisplay ?? "Unknown");

            var headers = new[] { "Type", "Kind", "Relationship", "Library" };
            var rows = sourceGroup.OrderBy(r => r.TypeName)
                .Select(impl => new[] { impl.TypeName, impl.Kind, impl.Relationship, impl.Assembly ?? "" });
            writer.WriteTable(headers, rows);
        }

        return writer.ToString();
    }
}
