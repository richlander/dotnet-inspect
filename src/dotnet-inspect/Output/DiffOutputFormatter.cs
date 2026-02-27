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
                symbol = "x";
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
            Versions = $"{fromVersion} -> {toVersion}",
            Summary = FormatSummaryCounts(totalBreaking, totalAdditive, totalPotentiallyBreaking),
            Rows = rows.Count > 0 ? rows : null
        };
    }

    public static DiffFullView BuildFullView(string name, IReadOnlyList<TypeDiff> typeDiffs, string fromVersion, string toVersion)
    {
        var view = new DiffFullView
        {
            Title = $"API Diff: {name}",
            Versions = $"**{fromVersion}** -> **{toVersion}**",
        };

        if (typeDiffs.Count == 0)
        {
            view.Status = new Callout(CalloutSeverity.Note, "No API changes detected.");
            return view;
        }

        int totalBreaking = 0, totalAdditive = 0, totalPotentiallyBreaking = 0;
        foreach (var td in typeDiffs)
        {
            totalBreaking += td.BreakingCount;
            totalAdditive += td.AdditiveCount;
            totalPotentiallyBreaking += td.PotentiallyBreakingCount;
        }

        view.Summary = $"**Summary:** {FormatSummaryCounts(totalBreaking, totalAdditive, totalPotentiallyBreaking)} across {typeDiffs.Count} types";

        view.BreakingChanges = BuildChangeRows(ChangeClassification.Breaking, typeDiffs);
        view.PotentiallyBreakingChanges = BuildChangeRows(ChangeClassification.PotentiallyBreaking, typeDiffs);
        view.AdditiveChanges = BuildChangeRows(ChangeClassification.Additive, typeDiffs);

        return view;
    }

    public static string RenderFullMarkdown(string name, IReadOnlyList<TypeDiff> typeDiffs, string fromVersion, string toVersion)
    {
        var view = BuildFullView(name, typeDiffs, fromVersion, toVersion);
        var writer = new MarkdownFormatter();
        new MarkoutContext().Serialize(view, writer);
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

    private static List<DiffChangeRow>? BuildChangeRows(ChangeClassification classification, IReadOnlyList<TypeDiff> typeDiffs)
    {
        var rows = new List<DiffChangeRow>();

        foreach (var td in typeDiffs.OrderBy(td => td.TypeFullName))
        {
            var typeName = TypeMatcher.GetSimpleName(td.TypeFullName);
            foreach (var change in td.Changes.Where(c => c.Classification == classification))
            {
                var message = change.Message;
                if (change.Kind == ChangeKind.MemberSignatureChanged &&
                    change.OldValue != null && change.NewValue != null)
                {
                    message += $": `{change.OldValue}` -> `{change.NewValue}`";
                }
                rows.Add(new DiffChangeRow(typeName, message));
            }
        }

        return rows.Count > 0 ? rows : null;
    }
}
