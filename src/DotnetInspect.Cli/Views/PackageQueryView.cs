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
    [MarkoutIgnore] public required InertString TitleText { get; init; }
    [MarkoutIgnore] public string Title => TitleText.ToString();

    [MarkoutSection(Name = "Packages")]
    [MarkoutIgnoreInTable]
    public MarkoutTable Results { get; } = new(
        ["Package", "Version", "Tier", "Source", "Evidence"],
        ["package", "version", "tier", "source", "evidence"],
        []);

    [MarkoutSection(Name = PackageQuerySections.QuerySummaryName)]
    public required List<PackageQuerySummaryRow> QuerySummary { get; init; }

    public static EmptyPackageQueryView From(PackageQueryView view) =>
        new()
        {
            TitleText = view.TitleText,
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
        EvidenceItems = match.Evidence;
    }

    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString VersionText { get; }
    [MarkoutIgnore] public InertString SourceText { get; }
    [MarkoutIgnore] public PackageQueryAcquisitionTier EvaluationTier { get; }
    [MarkoutIgnore] public ImmutableArray<PackageQueryEvidence> EvidenceItems { get; }
    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string Tier => EvaluationTier.ToString();
    public string Source => SourceText.ToString();
    public string Evidence => string.Join("; ", EvidenceItems.Select(item => item.Value));
}
