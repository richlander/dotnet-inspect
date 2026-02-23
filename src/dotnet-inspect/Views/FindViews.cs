using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.LineBreaksDoubleSpace)]
[MarkoutIgnoreFields(nameof(OneLineWriter))]
public class FindResultView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description { get; set; }
    public int Matches { get; set; }
    [MarkoutSkipNull] public int? Showing { get; set; }

    [MarkoutSection(Name = "Results")]
    [MarkoutIgnoreColumnWhen(nameof(PatternIsUniform), "Pattern")]
    [MarkoutIgnoreColumnWhen(nameof(SimilarityIsUniform), "Similarity")]
    public List<FindRow>? Results { get; set; }

    [MarkoutSection(Name = "Partial Matches")]
    [MarkoutIgnoreColumnWhen(nameof(PatternIsUniform), "Pattern")]
    [MarkoutSkipNull]
    public List<FindRow>? PartialMatches { get; set; }

    [MarkoutSection(Name = "Not Found")]
    [MarkoutSkipNull]
    public List<string>? NotFoundPatterns { get; set; }

    // Condition methods - Markout passes the section value
    public static bool PatternIsUniform(List<FindRow>? rows)
        => rows?.Select(r => r.Pattern).Distinct().Count() <= 1;

    public static bool SimilarityIsUniform(List<FindRow>? rows)
        => rows?.Select(r => r.Similarity).Distinct().Count() <= 1;
}

[MarkoutSerializable]
public record FindRow(
    string Pattern,
    string Type,
    string Namespace,
    string Kind,
    string Library,
    string Source,
    string Similarity);

/// <summary>
/// Flat view for oneline output - renders raw data as a single table.
/// </summary>
[MarkoutSerializable]
public class FindOneLineView
{
    [MarkoutIgnoreColumnWhen(nameof(PatternIsUniform), "Pattern")]
    public List<FindOneLineRow>? Results { get; set; }

    public static bool PatternIsUniform(List<FindOneLineRow>? rows)
        => rows?.Select(r => r.Pattern).Distinct().Count() <= 1;
}

[MarkoutSerializable]
public record FindOneLineRow(
    string Pattern,
    string Type,
    string Namespace,
    string Kind,
    string Library,
    string Source,
    string Match,
    string Sim);
