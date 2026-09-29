using ILInspector.CSharp;
using Markout;

namespace DotnetInspect.Cli.Views;

public sealed record TypeRelationResult(
    string Type,
    string Relationship,
    string Library,
    string Source,
    string? SourceVersion);

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public sealed class TypeRelationsResultView
{
    private string _title = "";
    private string? _description;

    [MarkoutIgnore]
    public string Title
    {
        get => _title;
        init => _title =
            CSharpIdentifier.ContainRenderedText(value);
    }

    [MarkoutIgnore]
    [MarkoutSkipNull]
    public string? Description
    {
        get => _description;
        init => _description = value is null
            ? null
            : CSharpIdentifier.ContainRenderedText(value);
    }

    [MarkoutSection(Name = "Implementers")]
    public List<TypeRelationRow>? Implementers { get; init; }

    [MarkoutSection(Name = "Derived Types")]
    public List<TypeRelationRow>? DerivedTypes { get; init; }
}

[MarkoutSerializable]
public sealed record TypeRelationRow(
    string Type,
    string Relationship,
    string Library,
    string Source)
{
    public string Type { get; init; } =
        CSharpIdentifier.ContainRenderedText(Type);

    public string Relationship { get; init; } =
        CSharpIdentifier.ContainRenderedText(Relationship);

    public string Library { get; init; } =
        CSharpIdentifier.ContainRenderedText(Library);

    public string Source { get; init; } =
        CSharpIdentifier.ContainRenderedText(Source);
}
