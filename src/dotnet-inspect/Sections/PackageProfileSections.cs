using System.Globalization;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Views;
using InertText;
using Markout;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>
/// Sections and row projection for a package-prefix profile.
/// </summary>
public static class PackageProfileSections
{
    public const string Packages = "Packages";

    public static SectionPipeline<PackageProfileView> CreatePipeline() =>
        new SectionPipeline<PackageProfileView>()
            .UseCuratedCatalog()
            .UseQueryCosts(static query => query.Cost)
            .WithoutComputedPoles()
            .Add<PackageRows>(PackageProfileQuery.Definition);

    public static DocumentSchema CreateSchema() =>
        SearchViewContext.Default
            .GetSchemaInfo<PackageProfileView>()!
            .ToDocumentSchema();

    public static PackageProfileView CreateDocument(
        string prefix,
        IReadOnlyList<PackageProfileEvent> events)
    {
        var rows = new List<PackageProfileRow>();
        PackageProfileSummary? summary = null;
        foreach (PackageProfileEvent profileEvent in events)
        {
            switch (profileEvent)
            {
                case PackageProfileEvent.Match match:
                    AddMatchRows(rows, match.Value);
                    break;
                case PackageProfileEvent.Failure failure:
                    rows.Add(FailureRow(failure.Value));
                    break;
                case PackageProfileEvent.Completed completed:
                    summary = completed.Value;
                    break;
            }
        }

        if (summary?.Truncated == true)
        {
            rows.Add(new PackageProfileRow(
                package: "",
                dependency: "",
                version: "",
                owners: "",
                targetFramework: "",
                dependencyVersion: "",
                authors: "",
                verified: "",
                downloads: "",
                source: summary.Producer.Value,
                status: "truncated",
                error: TruncationMessage(summary)));
        }

        return new PackageProfileView(
            new InertString(TextPolicy.Prose, $"Find packages: {prefix}"),
            new InertString(TextPolicy.Prose, prefix),
            rows.Count == 0
                ? new InertString(
                    TextPolicy.Prose,
                    "No packages found.")
                : null)
        {
            Packages = summary?.Matches ?? 0,
            Failures = summary?.Failures ?? 0,
            Truncated = summary?.Truncated ?? false,
            Results = rows.Count == 0 ? null : rows,
        };
    }

    public static int CountRows(
        PackageProfileView view,
        RowWindow? rows) =>
        RowWindow.Apply(
            rows,
            (IReadOnlyList<PackageProfileRow>?)view.Results
                ?? []).Count;

    public sealed class PackageRows : ISectionDescriptor<PackageProfileView>
    {
        public static string Name => Packages;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static string? ScannerKey => null;
        public static bool CanRender(PackageProfileView model) =>
            model.Results is { Count: > 0 };
    }

    private static void AddMatchRows(
        List<PackageProfileRow> rows,
        PackageProfileMatch match)
    {
        if (match.Manifest.DependencyGroups.IsEmpty)
        {
            rows.Add(MatchRow(match, targetFramework: "", dependency: null));
            return;
        }

        foreach (DeclaredPackageDependencyGroup group
            in match.Manifest.DependencyGroups)
        {
            if (group.Dependencies.IsEmpty)
            {
                rows.Add(MatchRow(
                    match,
                    group.TargetFramework,
                    dependency: null));
                continue;
            }

            foreach (DeclaredPackageDependency dependency
                in group.Dependencies)
            {
                rows.Add(MatchRow(
                    match,
                    group.TargetFramework,
                    dependency));
            }
        }
    }

    private static PackageProfileRow MatchRow(
        PackageProfileMatch match,
        string targetFramework,
        DeclaredPackageDependency? dependency) =>
        new(
            match.PackageId,
            dependency?.Id ?? "",
            match.Version,
            string.Join(", ", match.Owners),
            targetFramework,
            dependency?.VersionRange ?? "",
            match.Manifest.Authors ?? "",
            match.Verified ? "yes" : "no",
            match.TotalDownloads.ToString(CultureInfo.InvariantCulture),
            match.Producer.Value,
            "matched",
            "");

    private static PackageProfileRow FailureRow(
        PackageProfileFailure failure) =>
        new(
            failure.PackageId ?? "",
            dependency: "",
            failure.Version ?? "",
            owners: "",
            targetFramework: "",
            dependencyVersion: "",
            authors: "",
            verified: "",
            downloads: "",
            failure.Producer.Value,
            failure.Kind.ToString(),
            failure.Message);

    private static string TruncationMessage(
        PackageProfileSummary summary) =>
        summary.TruncationReason switch
        {
            PackageSearchTruncationReason.RequestedLimit =>
                $"The package profile reached its {summary.Candidates.ToString(CultureInfo.InvariantCulture)}-package limit.",
            PackageSearchTruncationReason.SourcePageLimit =>
                $"The package profile is incomplete after {summary.Candidates.ToString(CultureInfo.InvariantCulture)} packages because the source pagination limit was reached.",
            PackageSearchTruncationReason.ClientPageLimit =>
                $"The package profile is incomplete after {summary.Candidates.ToString(CultureInfo.InvariantCulture)} packages because the client pagination limit was reached.",
            _ => throw new InvalidOperationException(
                "A truncation row requires a truncation reason."),
        };
}
