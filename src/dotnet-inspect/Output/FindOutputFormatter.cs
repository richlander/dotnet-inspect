using DotnetInspector.Commands;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Formats find command results for display.
/// </summary>
public static class FindOutputFormatter
{
    public static string FormatOneLineOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern, bool grouped)
    {
        if (grouped)
        {
            // Grouped: one line per pattern with matching type names
            var lines = new List<string>();
            foreach (var (pattern, results) in resultsByPattern)
            {
                var typeNames = results.Select(r => r.TypeName).Distinct().OrderBy(n => n);
                lines.Add($"{pattern}: {string.Join(", ", typeNames)}");
            }
            return string.Join(Environment.NewLine, lines);
        }
        else
        {
            // Flat: all type names space-separated on one line
            var allTypeNames = resultsByPattern.Values
                .SelectMany(r => r)
                .Select(r => r.TypeName)
                .Distinct()
                .OrderBy(n => n);
            return string.Join(" ", allTypeNames);
        }
    }

    public static string FormatNameOnlyOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern)
    {
        var allTypeNames = resultsByPattern.Values
            .SelectMany(r => r)
            .Select(r => r.TypeName)
            .Distinct()
            .OrderBy(n => n);
        return string.Join(Environment.NewLine, allTypeNames);
    }

    public static string FormatMultiPatternOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern)
    {
        var writer = new MarkoutWriter();
        writer.WriteHeading(1, "Find Results");

        foreach (var (pattern, results) in resultsByPattern)
        {
            writer.WriteHeading(2, pattern);
            writer.WriteField("Matches", results.Count);

            if (results.Count == 0)
            {
                writer.WriteParagraph("*No types found.*");
            }
            else
            {
                WriteResultTable(writer, results);
            }
        }

        return writer.ToString().TrimEnd();
    }

    public static string FormatMarkoutOutput(List<TypeSearchResult> results, string pattern, int totalCount, int? limit)
    {
        var writer = new MarkoutWriter();
        writer.WriteHeading(1, $"Find: {pattern}");
        writer.WriteField("Matches", totalCount);

        if (results.Count == 0)
        {
            writer.WriteParagraph("*No types found matching the pattern.*");
        }
        else
        {
            WriteResultTable(writer, results);

            if (limit.HasValue && totalCount > limit.Value)
            {
                writer.WriteParagraph($"... *and {totalCount - limit.Value} more types*");
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static void WriteResultTable(MarkoutWriter writer, List<TypeSearchResult> results)
    {
        var headers = new[] { "Type", "Namespace", "Kind", "Assembly", "Source" };
        var rows = results.Select(result =>
        {
            var ns = result.Namespace ?? "";
            var source = result.SourceVersion != null
                ? $"{result.Source}@{result.SourceVersion}"
                : result.Source ?? "";
            return new[] { result.TypeName, ns, result.Kind ?? "", result.Assembly ?? "", source };
        });
        writer.WriteTable(headers, rows);
    }
}
