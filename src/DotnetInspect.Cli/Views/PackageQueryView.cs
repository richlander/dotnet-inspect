using System.Collections.Immutable;
using System.Globalization;
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
        ["Package", "Version", "Tier", "Source", "Answer", "Evidence"],
        ["package", "version", "tier", "source", "answer", "evidence"],
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
        AnswerItems = match.Answers;
        EvidenceItems = match.Evidence;
    }

    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString VersionText { get; }
    [MarkoutIgnore] public InertString SourceText { get; }
    [MarkoutIgnore] public PackageQueryAcquisitionTier EvaluationTier { get; }
    [MarkoutIgnore] public ImmutableArray<PackageQueryAnswer> AnswerItems { get; }
    [MarkoutIgnore] public ImmutableArray<PackageQueryEvidence> EvidenceItems { get; }
    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string Tier => EvaluationTier.ToString();
    public string Source => SourceText.ToString();
    public string Answer => string.Join(
        "; ",
        AnswerItems.Select(item => item.Value));
    public string Evidence => string.Join(
        "; ",
        EvidenceItems.Select(FormatEvidence));

    private static string FormatEvidence(PackageQueryEvidence evidence)
    {
        string? Property(string name) =>
            evidence.Properties.FirstOrDefault(property =>
                property.Name == name)?.Value;

        return evidence.Id switch
        {
            PackageQuery.PrefixEvidenceId =>
                $"Prefix: {Property("prefix")}",
            PackageQuery.ExactPackageEvidenceId =>
                $"Package: {Property("package")}",
            PackageQuery.DependenciesTermKey =>
                FormatSummary("dependency", "dependencies", evidence.Summary),
            PackageQuery.DependencyTargetTermKey =>
                FormatDependencyTarget(evidence),
            PackageQuery.DependsTermKey =>
                FormatSummary(
                    "dependency declaration",
                    "dependency declarations",
                    evidence.Summary),
            PackageQuery.DownloadsTermKey =>
                $"Downloads: {evidence.Number?.ToString("N0", CultureInfo.InvariantCulture)}",
            PackageQuery.LicenseTermKey =>
                $"Nuspec license {Property("declaration-kind")}: "
                + Property("declaration-value"),
            PackageQuery.ReadmeTermKey =>
                $"README: {Property("path")}",
            PackageQuery.ToolTermKey =>
                $"Package type: {Property("package-type")}",
            PackageQuery.ToolFormatTermKey =>
                $".NET tool settings version: {Property("settings-version")}",
            PackageQuery.SkillTermKey =>
                FormatSummary("skill document", "skill documents", evidence.Summary),
            _ => evidence.Id,
        };
    }

    private static string FormatDependencyTarget(
        PackageQueryEvidence evidence)
    {
        string? Property(string name) =>
            evidence.Properties.FirstOrDefault(property =>
                property.Name == name)?.Value;
        if (Property("target") is { } target)
            return $"Dependency target: {target}";
        string requested = Property("requested-target") ?? "";
        string status = Property("selection-status") ?? "";
        string? selected = Property("selected-group");
        return selected is null
            ? $"Dependency target: {requested} ({status})"
            : $"Dependency target: {requested} -> {selected} ({status})";
    }

    private static string FormatSummary(
        string singular,
        string plural,
        PackageQueryEvidenceSummary? summary)
    {
        if (summary is null)
            return "";
        string label = summary.Count == 1 ? singular : plural;
        string heading =
            $"{summary.Count.ToString(CultureInfo.InvariantCulture)} {label}";
        if (summary.Preview.IsEmpty)
            return heading;
        string preview = string.Join(
            ", ",
            summary.Preview.Select(item => item.ToString()));
        int remaining = summary.Count - summary.Preview.Length;
        return remaining > 0
            ? $"{heading}: {preview} (+{remaining.ToString(CultureInfo.InvariantCulture)} more)"
            : $"{heading}: {preview}";
    }
}
