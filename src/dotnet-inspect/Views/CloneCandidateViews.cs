using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using InertText;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Inline)]
public sealed class CloneCandidateView
{
    [MarkoutIgnore]
    public string Title => SectionNames.CloneCandidates;

    [MarkoutIgnore]
    public string Description =>
        "Ranked structural-similarity candidates; a row is not a checked clone relation.";

    public string Breadth
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    } = "";

    public string Discovery
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    } = "";

    [MarkoutPropertyName("Name similarity threshold")]
    public double NameSimilarityThreshold { get; init; }

    [MarkoutPropertyName("Participant scope")]
    public int ParticipantCount { get; init; }

    [MarkoutPropertyName("Coverage")]
    public string Coverage
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    } = "";

    [MarkoutPropertyName("Result limit")]
    public string ResultLimit
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    } = "";

    [MarkoutPropertyName("Work")]
    public string Work
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    } = "";

    [MarkoutSection(
        Name = SectionNames.CloneCandidates,
        EmptyText = "No structural clone candidates found.")]
    public List<CloneCandidateRowView> Candidates { get; init; } = [];
}

[MarkoutSerializable(AutoFields = false)]
public sealed class CloneCandidateTableView
{
    [MarkoutSection(
        Name = SectionNames.CloneCandidates,
        EmptyText = "No structural clone candidates found.")]
    public List<CloneCandidateRowView> Candidates { get; init; } = [];
}

[MarkoutSerializable]
public sealed class CloneCandidateRowView
{
    public int Rank { get; init; }

    [MarkoutIgnore]
    public InertString LeftText { get; init; }

    public string Left => LeftText.ToString();

    [MarkoutIgnore]
    public InertString RightText { get; init; }

    public string Right => RightText.ToString();

    public int Score { get; init; }
    public int Operations { get; init; }
    public int Positions { get; init; }
    public int Blocks { get; init; }
    public int Edges { get; init; }
    public int Locals { get; init; }

    [MarkoutPropertyName("Type Name")]
    public double? TypeNameSimilarity { get; init; }

    [MarkoutPropertyName("Member Name")]
    public double? MemberNameSimilarity { get; init; }

    internal static CloneCandidateRowView Create(
        CloneCandidateRow row) =>
        new()
        {
            Rank = row.Rank,
            LeftText = row.Left.AddressDisplay,
            RightText = row.Right.AddressDisplay,
            Score = row.Similarity.Score,
            Operations = row.Similarity.OperationScore,
            Positions = row.Similarity.PositionScore,
            Blocks = row.Similarity.BlockScore,
            Edges = row.Similarity.EdgeScore,
            Locals = row.Similarity.LocalScore,
            TypeNameSimilarity =
                row.NameQualification?.DeclaringTypeSimilarity,
            MemberNameSimilarity =
                row.NameQualification?.MemberSimilarity,
        };
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(CloneCandidateView))]
[MarkoutContext(typeof(CloneCandidateTableView))]
[MarkoutContext(typeof(CloneCandidateRowView))]
public partial class CloneCandidateViewContext : MarkoutSerializerContext
{
}
