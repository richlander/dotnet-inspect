using ILInspector.Analysis;
using ILInspector.CSharp;
using ILInspector.Research;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class MatchResultView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description { get; set; }

    public string Disposition { get; set; } = "";

    [MarkoutSkipNull] public string? Relation { get; set; }

    [MarkoutSkipNull] public string? Outcome { get; set; }

    [MarkoutSection(Name = "Blockers")]
    [MarkoutSkipNull]
    public List<MatchBlockerRow>? Blockers { get; set; }

    [MarkoutSection(Name = "Block Correspondence")]
    [MarkoutSkipNull]
    public List<MatchBlockCorrespondenceRow>? BlockCorrespondence { get; set; }
}

[MarkoutSerializable]
public record MatchBlockerRow(string Kind, string Side, string Detail)
{
    /// <summary>Untrusted producer detail is contained here (no upstream owner). See <see cref="Detail"/>.</summary>
    public string Detail { get; init; } = CSharpIdentifier.ContainRenderedText(Detail);
}

[MarkoutSerializable]
public record MatchBlockCorrespondenceRow(int LeftBlock, string RightBlocks, string Kind);

public static class MatchOutputFormatter
{
    public static MatchResultView BuildView(string leftDisplay, string rightDisplay, ResearchMatchResult result)
    {
        var document = result.Document;
        var view = new MatchResultView
        {
            Title = $"Match: {CSharpIdentifier.ContainRenderedText(leftDisplay)} vs {CSharpIdentifier.ContainRenderedText(rightDisplay)}",
            Disposition = document.Disposition.ToString(),
            Relation = document.Relation?.ToString(),
            Outcome = result.Outcome?.ToString(),
        };

        if (document.Blockers.Length > 0)
        {
            view.Blockers = document.Blockers
                .Select(b => new MatchBlockerRow(b.Kind.ToString(), b.Side.ToString(), b.Detail))
                .ToList();
        }

        if (document.Correspondence is { } correspondence && correspondence.Blocks.Length > 0)
        {
            view.BlockCorrespondence = correspondence.Blocks
                .OrderBy(b => b.LeftBlock)
                .Select(b => new MatchBlockCorrespondenceRow(
                    b.LeftBlock,
                    string.Join(", ", b.RightBlocks),
                    correspondence.Kind.ToString()))
                .ToList();
        }

        return view;
    }
}

/// <summary>
/// JSON envelope for <c>match --body --json</c>. The structural result stays independent
/// of the native body comparisons; plain <c>match --json</c> keeps its flat document.
/// </summary>
public sealed record MatchBodyDocument(
    StructuralCloneComparisonDocument Match,
    MethodBodyDiffDocument Body);
