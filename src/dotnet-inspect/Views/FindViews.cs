using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class FindResultView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description { get; set; }
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
    string Pattern,
    string Type,
    string Namespace,
    string Kind,
    string Library,
    string Source,
    string Match,
    [property: MarkoutPropertyName("Sim")] string Similarity);

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class FindMembersResultView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description { get; set; }
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
    string Pattern,
    string Member,
    string Kind,
    string Type,
    string Signature,
    string Library,
    string Source);

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
