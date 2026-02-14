using DotnetInspector.Metadata;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Formats diff command results for display.
/// </summary>
public static class DiffOutputFormatter
{
    public static void RenderNameOnly(MarkoutWriter writer, IReadOnlyList<TypeDiff> typeDiffs)
    {
        foreach (var name in typeDiffs.Select(td => td.TypeFullName).OrderBy(n => n))
        {
            writer.WriteListItem(name);
        }
    }

    public static DiffOneLineView BuildOneLineView(string name, IReadOnlyList<TypeDiff> typeDiffs, string fromVersion, string toVersion)
    {
        int totalBreaking = 0, totalAdditive = 0, totalPotentiallyBreaking = 0;
        foreach (var td in typeDiffs)
        {
            totalBreaking += td.BreakingCount;
            totalAdditive += td.AdditiveCount;
            totalPotentiallyBreaking += td.PotentiallyBreakingCount;
        }

        var rows = typeDiffs.OrderBy(td => td.TypeFullName).Select(td =>
        {
            string symbol;
            string detail;

            if (td.IsAdded)
            {
                symbol = "+";
                detail = "added";
            }
            else if (td.IsRemoved)
            {
                symbol = "-";
                detail = "removed";
            }
            else if (td.BreakingCount > 0)
            {
                symbol = "\u2717"; // ✗
                detail = FormatSummaryCounts(td.BreakingCount, td.AdditiveCount, td.PotentiallyBreakingCount);
            }
            else
            {
                symbol = "~";
                detail = FormatSummaryCounts(td.BreakingCount, td.AdditiveCount, td.PotentiallyBreakingCount);
            }

            return new DiffOneLineRow(symbol, TypeMatcher.GetSimpleName(td.TypeFullName), detail);
        }).ToList();

        return new DiffOneLineView
        {
            Title = $"API Diff: {name}",
            Versions = $"{fromVersion} → {toVersion}",
            Summary = FormatSummaryCounts(totalBreaking, totalAdditive, totalPotentiallyBreaking),
            Rows = rows.Count > 0 ? rows : null
        };
    }

    public static string RenderFullMarkdown(string name, IReadOnlyList<TypeDiff> typeDiffs, string fromVersion, string toVersion)
    {
        var writer = new MarkdownWriter();

        writer.WriteHeading(1, $"API Diff: {name}");
        writer.WriteParagraph($"**{fromVersion}** → **{toVersion}**");

        if (typeDiffs.Count == 0)
        {
            writer.WriteParagraph("*No API changes detected.*");
            return writer.ToString().TrimEnd();
        }

        int totalBreaking = 0, totalAdditive = 0, totalPotentiallyBreaking = 0;
        foreach (var td in typeDiffs)
        {
            totalBreaking += td.BreakingCount;
            totalAdditive += td.AdditiveCount;
            totalPotentiallyBreaking += td.PotentiallyBreakingCount;
        }

        var summary = FormatSummaryCounts(totalBreaking, totalAdditive, totalPotentiallyBreaking);
        writer.WriteParagraph($"**Summary:** {summary} across {typeDiffs.Count} types");

        // Group by classification: breaking first, then potentially breaking, then additive
        WriteSection(writer, "Breaking Changes", ChangeClassification.Breaking, typeDiffs);
        WriteSection(writer, "Potentially Breaking Changes", ChangeClassification.PotentiallyBreaking, typeDiffs);
        WriteSection(writer, "Additive Changes", ChangeClassification.Additive, typeDiffs);

        return writer.ToString().TrimEnd();
    }

    internal static string FormatSummaryCounts(int breaking, int additive, int potentiallyBreaking)
    {
        var parts = new List<string>(3);
        if (breaking > 0) parts.Add($"{breaking} breaking");
        if (additive > 0) parts.Add($"{additive} additive");
        if (potentiallyBreaking > 0) parts.Add($"{potentiallyBreaking} potentially breaking");
        return parts.Count > 0 ? string.Join(", ", parts) : "no changes";
    }

    private static void WriteSection(MarkoutWriter writer, string heading, ChangeClassification classification, IReadOnlyList<TypeDiff> typeDiffs)
    {
        // Collect types that have changes of this classification
        var relevantTypes = typeDiffs
            .Where(td => td.Changes.Any(c => c.Classification == classification))
            .OrderBy(td => td.TypeFullName)
            .ToList();

        if (relevantTypes.Count == 0)
            return;

        writer.WriteHeading(2, heading);

        foreach (var td in relevantTypes)
        {
            writer.WriteHeading(3, TypeMatcher.GetSimpleName(td.TypeFullName));

            var changes = td.Changes
                .Where(c => c.Classification == classification)
                .ToList();

            foreach (var change in changes)
            {
                var message = change.Message;

                // For signature changes, append old → new values
                if (change.Kind == ChangeKind.MemberSignatureChanged &&
                    change.OldValue != null && change.NewValue != null)
                {
                    message += $": `{change.OldValue}` → `{change.NewValue}`";
                }

                writer.WriteListItem(message);
            }
        }
    }
}
