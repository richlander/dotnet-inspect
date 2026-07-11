using ILInspector.Metadata;
using ILInspector.Research;
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

    public static DiffDetailedChangesView BuildDetailedChangesView(string name, IReadOnlyList<TypeDiff> typeDiffs, string fromVersion, string toVersion)
    {
        int totalBreaking = 0, totalAdditive = 0, totalPotentiallyBreaking = 0;
        foreach (var td in typeDiffs)
        {
            totalBreaking += td.BreakingCount;
            totalAdditive += td.AdditiveCount;
            totalPotentiallyBreaking += td.PotentiallyBreakingCount;
        }

        var rows = typeDiffs
            .OrderBy(td => td.TypeFullName, StringComparer.Ordinal)
            .SelectMany(td => td.Changes.Select(change => BuildDetailedRow(td.TypeFullName, change)))
            .ToList();

        return new DiffDetailedChangesView
        {
            Title = $"API Diff: {name}",
            Versions = $"{fromVersion} -> {toVersion}",
            Summary = FormatSummaryCounts(totalBreaking, totalAdditive, totalPotentiallyBreaking),
            Rows = rows.Count > 0 ? rows : null
        };
    }

    public static FindingTransitionsView BuildFindingTransitionsView(
        string name,
        IReadOnlyList<FindingTransitionRow> rows,
        string fromVersion,
        string toVersion)
        => new()
        {
            Title = $"Finding Transitions: {name}",
            Versions = $"{fromVersion} -> {toVersion}",
            Status = rows.Count == 0
                ? new Callout(CalloutSeverity.Note, "No selected Finding exists at either endpoint.")
                : new Callout(CalloutSeverity.Note, $"{rows.Count} selected Finding transition{(rows.Count == 1 ? "" : "s")}."),
            Rows = rows.Count > 0 ? rows.ToList() : null
        };

    public static string RenderFindingTransitionsView(FindingTransitionsView view)
    {
        var writer = new MarkoutWriter(new MarkdownFormatter());
        DiffViewContext.Default.Serialize(view, writer);
        return writer.ToString().TrimEnd();
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
        var writer = new MarkoutWriter(new MarkdownFormatter());
        DiffViewContext.Default.Serialize(view, writer);
        return writer.ToString().TrimEnd();
    }

    /// <summary>
    /// Builds the Analysis Diff view with a caller-supplied summary line.
    /// </summary>
    public static AnalysisDiffView BuildAnalysisDiffView(string name, IReadOnlyList<AnalysisDiffRow> rows, string summary, string fromVersion, string toVersion)
        => new()
        {
            Title = $"Analysis Diff: {name}",
            Versions = $"{fromVersion} -> {toVersion}",
            Summary = summary,
            Status = rows.Count == 0
                ? new Callout(CalloutSeverity.Note, summary)
                : new Callout(CalloutSeverity.Note, "Analysis signal changes are body-level evidence, not public API compatibility changes."),
            Rows = rows.Count > 0 ? rows.ToList() : null
        };

    /// <summary>
    /// Renders an Analysis Diff view to markdown.
    /// </summary>
    public static string RenderAnalysisDiffView(AnalysisDiffView view)
    {
        var writer = new MarkoutWriter(new MarkdownFormatter());
        DiffViewContext.Default.Serialize(view, writer);
        return writer.ToString().TrimEnd();
    }

    public static string RenderAnalysisDiffMarkdown(string name, IReadOnlyList<AnalysisDiffRow> rows, string fromVersion, string toVersion)
    {
        var summary = rows.Count == 0 ? "No analysis signal changes detected." : $"{rows.Count} changed analysis signals";
        return RenderAnalysisDiffView(BuildAnalysisDiffView(name, rows, summary, fromVersion, toVersion));
    }

    public static ImplementationDiffView BuildImplementationDiffView(
        string name,
        ImplementationDiffResult diff,
        string fromVersion,
        string toVersion)
    {
        List<ImplementationDiffRow> rows = [];
        foreach (var member in diff.Members)
        {
            foreach (var change in member.Changes)
            {
                var mechanism = change.Mechanism switch
                {
                    ResearchChangeMechanism.CSharp => "C#",
                    ResearchChangeMechanism.IlBody => "IL",
                    _ => change.Mechanism.ToString()
                };
                var evidenceLines = ImplementationDiff.UnifiedLines(change);
                if (evidenceLines.IsDefaultOrEmpty)
                {
                    rows.Add(new ImplementationDiffRow(
                        member.Subject.Display,
                        mechanism,
                        change.Kind.ToString().ToLowerInvariant(),
                        change.Detail ?? change.Descriptor.Title));
                    continue;
                }

                foreach (var evidence in evidenceLines)
                {
                    rows.Add(new ImplementationDiffRow(
                        member.Subject.Display,
                        mechanism,
                        change.Kind.ToString().ToLowerInvariant(),
                        evidence));
                }
            }
        }

        var csharpCount = rows.Count(row => row.Mechanism == "C#");
        var ilCount = rows.Count(row => row.Mechanism == "IL");
        var summary = rows.Count == 0
            ? "No implementation differences detected."
            : $"{diff.Members.Count} changed member{(diff.Members.Count == 1 ? "" : "s")}; "
              + $"{csharpCount} C# and {ilCount} IL evidence row{(rows.Count == 1 ? "" : "s")}.";

        return new ImplementationDiffView
        {
            Title = $"Implementation Diff: {name}",
            Versions = $"{fromVersion} -> {toVersion}",
            Summary = summary,
            Status = rows.Count == 0
                ? new Callout(CalloutSeverity.Note, summary)
                : new Callout(
                    CalloutSeverity.Note,
                    "C# and IL implementation evidence is body-level evidence, not public API compatibility."),
            Rows = rows.Count > 0 ? rows : null
        };
    }

    public static string RenderImplementationDiffView(ImplementationDiffView view)
    {
        var writer = new MarkoutWriter(new MarkdownFormatter());
        DiffViewContext.Default.Serialize(view, writer);
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
                    message += $": {MarkoutInline.Code(change.OldValue)} -> {MarkoutInline.Code(change.NewValue)}";
                }
                rows.Add(new DiffChangeRow(typeName, message));
            }
        }

        return rows.Count > 0 ? rows : null;
    }

    private static DiffDetailedChangeRow BuildDetailedRow(string typeFullName, ApiChange change)
        => new(
            ChangeSymbol(change.Classification, change.Kind),
            ClassificationText(change.Classification),
            TypeMatcher.GetSimpleName(typeFullName),
            change.Subject?.OldMember?.StableSelector
                ?? change.Subject?.NewMember?.StableSelector
                ?? "",
            ChangeKindText(change.Kind),
            change.Message,
            change.OldValue ?? "",
            change.NewValue ?? "");

    private static string ChangeSymbol(ChangeClassification classification, ChangeKind kind)
        => kind switch
        {
            ChangeKind.TypeAdded or ChangeKind.MemberAdded => "+",
            ChangeKind.TypeRemoved or ChangeKind.MemberRemoved => "-",
            _ when classification == ChangeClassification.Breaking => "x",
            _ => "~"
        };

    private static string ClassificationText(ChangeClassification classification)
        => classification switch
        {
            ChangeClassification.Breaking => "breaking",
            ChangeClassification.Additive => "additive",
            ChangeClassification.PotentiallyBreaking => "potentially-breaking",
            _ => classification.ToString()
        };

    private static string ChangeKindText(ChangeKind kind)
        => kind.ToString();
}
