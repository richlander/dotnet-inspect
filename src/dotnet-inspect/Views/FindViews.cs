using InertText;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class FindResultView
{
    public FindResultView(InertString titleText, InertString? descriptionText = null)
    {
        TitleText = titleText;
        DescriptionText = descriptionText;
    }

    [MarkoutIgnore] public InertString TitleText { get; }
    [MarkoutIgnore] public InertString? DescriptionText { get; }
    [MarkoutIgnore] public string Title => TitleText.ToString();
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description => DescriptionText?.ToString();
    [MarkoutIgnore] public int Matches { get; set; }

    [MarkoutSection(Name = "Results")]
    [MarkoutIgnoreColumnWhen(nameof(PatternIsUniform), "Pattern")]
    [MarkoutIgnoreColumnWhen(nameof(MatchIsUniform), "Match")]
    [MarkoutIgnoreColumnWhen(nameof(SimIsUniform), "Sim")]
    public List<FindRow>? Results { get; set; }

    public static bool PatternIsUniform(List<FindRow>? rows)
        => rows?.Select(r => r.Pattern).Distinct().Count() <= 1;

    public static bool MatchIsUniform(List<FindRow>? rows)
        => rows?.Select(r => r.Match).Distinct().Count() <= 1;

    public static bool SimIsUniform(List<FindRow>? rows)
        => rows?.All(r => r.Similarity is "1.00" or "-") ?? true;
}

[MarkoutSerializable]
public record FindRow(
    [property: MarkoutIgnore] InertString PatternText,
    [property: MarkoutIgnore] InertString TypeText,
    [property: MarkoutIgnore] InertString NamespaceText,
    [property: MarkoutIgnore] InertString KindText,
    [property: MarkoutIgnore] InertString LibraryText,
    [property: MarkoutIgnore] InertString SourceText,
    [property: MarkoutIgnore] InertString MatchText,
    [property: MarkoutIgnore] InertString SimilarityText)
{
    public string Pattern => PatternText.ToString();
    public string Type => TypeText.ToString();
    public string Namespace => NamespaceText.ToString();
    public string Kind => KindText.ToString();
    public string Library => LibraryText.ToString();
    public string Source => SourceText.ToString();
    public string Match => MatchText.ToString();
    [MarkoutPropertyName("Sim")]
    public string Similarity => SimilarityText.ToString();
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class FindMembersResultView
{
    public FindMembersResultView(InertString titleText, InertString? descriptionText = null)
    {
        TitleText = titleText;
        DescriptionText = descriptionText;
    }

    [MarkoutIgnore] public InertString TitleText { get; }
    [MarkoutIgnore] public InertString? DescriptionText { get; }
    [MarkoutIgnore] public string Title => TitleText.ToString();
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description => DescriptionText?.ToString();
    [MarkoutIgnore] public int Matches { get; set; }

    [MarkoutSection(Name = "Members")]
    [MarkoutIgnoreColumnWhen(nameof(PatternIsUniform), "Pattern")]
    [MarkoutIgnoreColumnWhen(nameof(SignatureIsEmpty), "Signature")]
    public List<FindMemberRow>? Results { get; set; }

    public static bool PatternIsUniform(List<FindMemberRow>? rows)
        => rows?.Select(r => r.Pattern).Distinct().Count() <= 1;

    public static bool SignatureIsEmpty(List<FindMemberRow>? rows)
        => rows?.All(r => string.IsNullOrEmpty(r.Signature)) ?? true;
}

[MarkoutSerializable]
public record FindMemberRow(
    [property: MarkoutIgnore] InertString PatternText,
    [property: MarkoutIgnore] InertString MemberText,
    [property: MarkoutIgnore] InertString KindText,
    [property: MarkoutIgnore] InertString TypeText,
    [property: MarkoutIgnore] InertString SignatureText,
    [property: MarkoutIgnore] InertString LibraryText,
    [property: MarkoutIgnore] InertString SourceText)
{
    public string Pattern => PatternText.ToString();
    public string Member => MemberText.ToString();
    public string Kind => KindText.ToString();
    public string Type => TypeText.ToString();
    public string Signature => SignatureText.ToString();
    public string Library => LibraryText.ToString();
    public string Source => SourceText.ToString();
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(FindResultView))]
[MarkoutContext(typeof(FindRow))]
[MarkoutContext(typeof(FindMembersResultView))]
[MarkoutContext(typeof(FindMemberRow))]
[MarkoutContext(typeof(ImplementsResultView))]
[MarkoutContext(typeof(ImplementerRow))]
[MarkoutContext(typeof(ExtensionsResultView))]
[MarkoutContext(typeof(ExtensionCountRow))]
[MarkoutContext(typeof(ExtensionRow))]
public partial class SearchViewContext : MarkoutSerializerContext
{
}
