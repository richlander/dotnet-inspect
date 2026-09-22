using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspect.Cli.Sections;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class PackageQueryView
{
    [MarkoutIgnore] public required InertString TitleText { get; init; }
    [MarkoutIgnore] public required PackageQuerySummary Summary { get; init; }
    [MarkoutIgnore] public string Title => TitleText.ToString();

    [MarkoutSection(Name = "Packages")]
    public required List<PackageQueryRow> Results { get; init; }

    [MarkoutSection(Name = PackageQuerySections.LiteralStringsName)]
    public required List<PackageQueryLiteralStringRow> LiteralStrings { get; init; }

    [MarkoutSection(Name = PackageQuerySections.QuerySummaryName)]
    public required List<PackageQuerySummaryRow> QuerySummary { get; init; }
}

[MarkoutSerializable]
public sealed record PackageQuerySummaryRow(
    int Candidates,
    int Matches,
    int EvaluationFailures,
    PackageQueryCompletionKind Status);

[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class EmptyPackageQueryView
{
    private static readonly string[] SemanticHeaders =
    [
        "Package",
        "Version",
        "Tier",
        "Source",
        "Answer",
        "Library",
        "Assembly",
        "Target Framework",
        "Unevaluated Siblings",
        "Occurrences",
        "Evidence",
        "Root",
    ];

    private static readonly string[] SemanticKeys =
    [
        "package",
        "version",
        "tier",
        "source",
        "answer",
        "library",
        "assembly",
        "target_framework",
        "unevaluated_siblings",
        "occurrences",
        "evidence",
        "root",
    ];

    [MarkoutIgnore] public required InertString TitleText { get; init; }
    [MarkoutIgnore] public string Title => TitleText.ToString();

    [MarkoutSection(Name = "Packages")]
    [MarkoutIgnoreInTable]
    public required MarkoutTable Results { get; init; }

    [MarkoutSection(Name = PackageQuerySections.LiteralStringsName)]
    public required List<PackageQueryLiteralStringRow> LiteralStrings { get; init; }

    [MarkoutSection(Name = PackageQuerySections.QuerySummaryName)]
    public required List<PackageQuerySummaryRow> QuerySummary { get; init; }

    public static EmptyPackageQueryView From(PackageQueryView view) =>
        new()
        {
            TitleText = view.TitleText,
            Results = new(
                ["Package", "Version", "Tier", "Source", "Answer"],
                ["package", "version", "tier", "source", "answer"],
                []),
            LiteralStrings = view.LiteralStrings,
            QuerySummary = view.QuerySummary,
        };

    public static EmptyPackageQueryView From(PackageQuerySemanticView view) =>
        new()
        {
            TitleText = view.TitleText,
            Results = new(SemanticHeaders, SemanticKeys, []),
            LiteralStrings = view.LiteralStrings,
            QuerySummary = view.QuerySummary,
        };
}

[MarkoutSerializable]
public sealed class PackageQueryRow
{
    public PackageQueryRow(PackageQueryMatch match)
    {
        PackageText = new(TextPolicy.Field, match.Package.PackageId);
        VersionText = new(TextPolicy.Field, match.Package.Version);
        SourceText = match.Package.Source.Producer.Display;
        EvaluationTier = match.Tier;
        AnswerItems = match.Answers;
    }

    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString VersionText { get; }
    [MarkoutIgnore] public InertString SourceText { get; }
    [MarkoutIgnore] public PackageQueryAcquisitionTier EvaluationTier { get; }
    [MarkoutIgnore] public ImmutableArray<PackageQueryAnswer> AnswerItems { get; }
    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string Tier => EvaluationTier.ToString();
    public string Source => SourceText.ToString();
    public string Answer => string.Join(
        "; ",
        AnswerItems.Select(item => item.Value));
}

[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class PackageQuerySemanticView
{
    [MarkoutIgnore] public required InertString TitleText { get; init; }
    [MarkoutIgnore] public required PackageQuerySummary Summary { get; init; }
    [MarkoutIgnore] public string Title => TitleText.ToString();

    [MarkoutSection(Name = "Packages")]
    public required List<PackageQuerySemanticRow> Results { get; init; }

    [MarkoutSection(Name = PackageQuerySections.LiteralStringsName)]
    public required List<PackageQueryLiteralStringRow> LiteralStrings { get; init; }

    [MarkoutSection(Name = PackageQuerySections.QuerySummaryName)]
    public required List<PackageQuerySummaryRow> QuerySummary { get; init; }
}

[MarkoutSerializable]
public sealed class PackageQueryLiteralStringRow(InertString literalText)
{
    [MarkoutIgnore] public InertString LiteralText { get; } = literalText;
    public string Literal => LiteralText.ToString();
}

[MarkoutSerializable]
public sealed class PackageQuerySemanticRow
{
    public PackageQuerySemanticRow(PackageQueryMatch match)
        : this(
            new InertString(TextPolicy.Field, match.Package.PackageId),
            new InertString(TextPolicy.Field, match.Package.Version),
            match.Package.Source.Producer.Display,
            match.Tier,
            new InertString(
                TextPolicy.Field,
                string.Join(
                    "; ",
                    match.Answers.Select(item => item.Value))),
            RequireLiteral(match).SelectedAsset.PathText,
            RequireLiteral(match).SelectedAsset.AssemblyNameText,
            RequireLiteral(match).SelectedAsset.TargetFrameworkText,
            RequireLiteral(match).SelectedAsset.UnevaluatedSiblings,
            RequireLiteral(match).Occurrences.Length,
            new InertString(
                TextPolicy.Field,
                string.Join(
                    ", ",
                    RequireLiteral(match).Occurrences
                        .Take(PackageQuery.MaximumEvidencePreviewItems)
                        .Select(occurrence =>
                            $"0x{occurrence.Address.MethodDefinitionToken:X8}"
                            + $"/IL_{occurrence.Address.ILOffset:X4}"))),
            new InertString(
                TextPolicy.Field,
                RequireLiteral(match).RootRequest.Encode()))
    {
    }

    public PackageQuerySemanticRow(
        InertString package,
        InertString version,
        InertString source,
        PackageQueryAcquisitionTier evaluationTier,
        InertString answer,
        InertString library,
        InertString assembly,
        InertString targetFramework,
        int unevaluatedSiblingCount,
        int occurrenceCount,
        InertString evidence,
        InertString root)
    {
        PackageText = package;
        VersionText = version;
        SourceText = source;
        EvaluationTier = evaluationTier;
        AnswerText = answer;
        LibraryText = library;
        AssemblyText = assembly;
        TargetFrameworkText = targetFramework;
        UnevaluatedSiblingCount = unevaluatedSiblingCount;
        OccurrenceCount = occurrenceCount;
        EvidenceText = evidence;
        RootText = root;
    }

    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString VersionText { get; }
    [MarkoutIgnore] public InertString SourceText { get; }
    [MarkoutIgnore] public PackageQueryAcquisitionTier EvaluationTier { get; }
    [MarkoutIgnore] public InertString AnswerText { get; }
    [MarkoutIgnore] public InertString LibraryText { get; }
    [MarkoutIgnore] public InertString AssemblyText { get; }
    [MarkoutIgnore] public InertString TargetFrameworkText { get; }
    [MarkoutIgnore] public InertString EvidenceText { get; }
    [MarkoutIgnore] public InertString RootText { get; }
    [MarkoutIgnore] public int UnevaluatedSiblingCount { get; }
    [MarkoutIgnore] public int OccurrenceCount { get; }
    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string Tier => EvaluationTier.ToString();
    public string Source => SourceText.ToString();
    public string Answer => AnswerText.ToString();
    public string Library => LibraryText.ToString();
    public string Assembly => AssemblyText.ToString();
    [MarkoutPropertyName("Target Framework")]
    public string TargetFramework => TargetFrameworkText.ToString();
    [MarkoutPropertyName("Unevaluated Siblings")]
    public int UnevaluatedSiblings => UnevaluatedSiblingCount;
    public int Occurrences => OccurrenceCount;
    public string Evidence => EvidenceText.ToString();
    public string Root => RootText.ToString();

    private static PackageQueryLibraryLiteralResult RequireLiteral(
        PackageQueryMatch match) =>
        match.LibraryLiteral
        ?? throw new ArgumentException(
            "A semantic Package Query row requires library-literal evidence.",
            nameof(match));
}
