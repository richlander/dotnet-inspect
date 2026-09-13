using DotnetInspector.Vocabulary;
using Markout;

namespace DotnetInspect.Cli.Views;

/// <summary>Serializer-backed presentation of the product vocabulary catalog.</summary>
[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class VocabularyView
{
    [MarkoutIgnore]
    public string Title => "Vocabulary";

    [MarkoutUnwrap]
    public List<VocabularySectionView> Sections { get; init; } = [];

    public static VocabularyView Create(IEnumerable<VocabularySection> sections) =>
        new()
        {
            Sections = [.. sections.Select(VocabularySectionView.Create)],
        };
}

/// <summary>One runtime-named vocabulary section with owner-issued runtime columns.</summary>
[MarkoutSerializable(
    TitleProperty = nameof(Name),
    DescriptionProperty = nameof(Summary))]
public sealed class VocabularySectionView
{
    [MarkoutIgnore]
    public string Name
    {
        get;
        init => field = LibraryViewText.Contain(value) ?? "";
    } = "";

    [MarkoutIgnore]
    public string Summary
    {
        get;
        init => field = LibraryViewText.Contain(value) ?? "";
    } = "";

    [MarkoutIgnoreInTable]
    public MarkoutTable? Values { get; init; }

    public static VocabularySectionView Create(VocabularySection section) =>
        new()
        {
            Name = section.Name,
            Summary = section.Summary,
            Values = new MarkoutTable(
                [.. section.Fields.Select(field => field.Label)],
                [.. section.Fields.Select(field => field.Id)],
                [.. section.Values.Select(row =>
                    section.Fields.Select(field =>
                        row.TryGetValue(field.Id, out VocabularyValue value)
                            ? value.ToDisplayString()
                            : "").ToArray())]),
        };
}

[MarkoutContext(typeof(VocabularyView))]
[MarkoutContext(typeof(VocabularySectionView))]
public partial class VocabularyViewContext : MarkoutSerializerContext;
