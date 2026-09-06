using System.Collections.Immutable;
using DotnetInspector.Queries;
using InertText;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description))]
public sealed class PackageQueryView
{
    [MarkoutIgnore] public required InertString TitleText { get; init; }
    [MarkoutIgnore] public required PackageQuerySummary Summary { get; init; }
    [MarkoutIgnore] public string Title => TitleText.ToString();
    [MarkoutIgnore] public string Description =>
        $"Candidates: {Summary.Candidates}/{Summary.CandidateLimit}; "
        + $"matches: {Summary.Matches}/{Summary.MatchLimit}; "
        + $"failures: {Summary.Failures}; completion: {Summary.Completion}.";

    [MarkoutSection(Name = "Packages")]
    public required List<PackageQueryRow> Results { get; init; }
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
    [MarkoutIgnore] public PackageQueryFacetTier EvaluationTier { get; }
    [MarkoutIgnore] public ImmutableArray<PackageQueryEvidence> EvidenceItems { get; }
    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string Tier => EvaluationTier.ToString();
    public string Source => SourceText.ToString();
    public string Evidence => string.Join("; ", EvidenceItems.Select(item => item.Value));
}
