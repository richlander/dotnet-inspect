using CSharpText;

using ILInspector.Analysis;
using ILInspector.Research;

using Markout;

namespace DotnetInspector.Presentation;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public sealed class StructuralMatchView
{
    [MarkoutIgnore]
    public string Title { get; set; } = "";

    [MarkoutIgnore]
    [MarkoutSkipNull]
    public string? Description { get; set; }

    public string Disposition { get; set; } = "";

    [MarkoutSkipNull]
    public string? Relation { get; set; }

    [MarkoutSkipNull]
    public string? Outcome { get; set; }

    [MarkoutSection(Name = "Blockers")]
    [MarkoutSkipNull]
    public List<StructuralMatchBlockerRow>? Blockers { get; set; }

    [MarkoutSection(Name = "Block Correspondence")]
    [MarkoutSkipNull]
    public List<StructuralMatchBlockCorrespondenceRow>?
        BlockCorrespondence { get; set; }
}

[MarkoutSerializable]
public sealed record StructuralMatchBlockerRow(
    string Kind,
    string Side,
    string Detail)
{
    public string Detail { get; init; } =
        CSharpIdentifier.ContainRenderedText(Detail);
}

[MarkoutSerializable]
public sealed record StructuralMatchBlockCorrespondenceRow(
    int LeftBlock,
    string RightBlocks,
    string Kind);

public static class StructuralMatchPresentation
{
    public static StructuralMatchView Create(
        string leftDisplay,
        string rightDisplay,
        ResearchMatchResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leftDisplay);
        ArgumentException.ThrowIfNullOrWhiteSpace(rightDisplay);
        ArgumentNullException.ThrowIfNull(result);

        StructuralCloneComparisonDocument document = result.Document;
        var view = new StructuralMatchView
        {
            Title =
                "Match: "
                + CSharpIdentifier.ContainRenderedText(leftDisplay)
                + " vs "
                + CSharpIdentifier.ContainRenderedText(rightDisplay),
            Disposition = document.Disposition.ToString(),
            Relation = document.Relation?.ToString(),
            Outcome = result.Outcome?.ToString(),
        };

        if (!document.Blockers.IsEmpty)
        {
            view.Blockers =
            [
                .. document.Blockers.Select(blocker =>
                    new StructuralMatchBlockerRow(
                        blocker.Kind.ToString(),
                        blocker.Side.ToString(),
                        blocker.Detail)),
            ];
        }

        if (document.Correspondence is { Blocks.IsEmpty: false }
            correspondence)
        {
            view.BlockCorrespondence =
            [
                .. correspondence.Blocks
                    .OrderBy(block => block.LeftBlock)
                    .Select(block =>
                        new StructuralMatchBlockCorrespondenceRow(
                            block.LeftBlock,
                            string.Join(", ", block.RightBlocks),
                            correspondence.Kind.ToString())),
            ];
        }

        return view;
    }
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(StructuralMatchView))]
[MarkoutContext(typeof(StructuralMatchBlockerRow))]
[MarkoutContext(typeof(StructuralMatchBlockCorrespondenceRow))]
public partial class StructuralMatchViewContext
    : MarkoutSerializerContext;
