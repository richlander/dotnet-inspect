using System.Globalization;
using DotnetInspector.Queries;
using DotnetInspector.Views;
using InertText;

namespace DotnetInspector.Output;

public static class PackageProfileFindOutputFormatter
{
    public static PackageProfileFindView BuildView(
        string prefix,
        IReadOnlyList<PackageProfileEvent> events)
    {
        var rows = new List<PackageProfileFindRow>();
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
            rows.Add(new PackageProfileFindRow(
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
                error:
                    $"The package profile reached its {summary.Candidates.ToString(CultureInfo.InvariantCulture)}-package limit."));
        }

        return new PackageProfileFindView(
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

    private static void AddMatchRows(
        List<PackageProfileFindRow> rows,
        PackageProfileMatch match)
    {
        if (match.DependencyGroups.Groups.IsEmpty)
        {
            rows.Add(MatchRow(match, targetFramework: "", dependency: null));
            return;
        }

        foreach (DeclaredPackageDependencyGroup group
            in match.DependencyGroups.Groups)
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

    private static PackageProfileFindRow MatchRow(
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
            match.Authors ?? "",
            match.Verified ? "yes" : "no",
            match.TotalDownloads.ToString(CultureInfo.InvariantCulture),
            match.Producer.Value,
            "matched",
            "");

    private static PackageProfileFindRow FailureRow(
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

}
