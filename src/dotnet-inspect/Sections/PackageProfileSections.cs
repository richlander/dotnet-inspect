using System.Globalization;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Views;
using InertText;
using Markout;
using NuGetFetch;

namespace DotnetInspector.Sections;

public sealed record PackageProfileSectionCatalog(
    SectionPipeline<PackageProfileView> Pipeline,
    InspectionQueryRegistry<PackageProfileQueryContext> QueryRegistry);

/// <summary>
/// Sections and row projection for a package-prefix profile.
/// </summary>
public static class PackageProfileSections
{
    public const string Packages = "Packages";

    public static PackageProfileSectionCatalog CreateCatalog()
    {
        InspectionQueryRegistry<PackageProfileQueryContext> queryRegistry =
            CreateQueryRegistry();
        return new PackageProfileSectionCatalog(
            CreatePipeline(queryRegistry.CostOf),
            queryRegistry);
    }

    public static SectionPipeline<PackageProfileView> CreatePipeline()
    {
        InspectionQueryRegistry<PackageProfileQueryContext> queryRegistry =
            CreateQueryRegistry();
        return CreatePipeline(queryRegistry.CostOf);
    }

    private static InspectionQueryRegistry<PackageProfileQueryContext>
        CreateQueryRegistry() =>
        new InspectionQueryRegistry<PackageProfileQueryContext>()
            .AddAsync(
                PackageProfileQuery.Definition,
                static (context, cancellationToken) =>
                    PackageProfileQuery.ExecuteToArrayAsync(
                    context.Source,
                    context.Request,
                    cancellationToken));

    private static SectionPipeline<PackageProfileView> CreatePipeline(
        Func<InspectionQueryDefinition, InspectionCost> queryCost) =>
        new SectionPipeline<PackageProfileView>()
            .UseCuratedCatalog()
            .UseQueryCosts(queryCost)
            .WithoutComputedPoles()
            .Add<PackageRows>(PackageProfileQuery.Definition);

    public static DocumentSchema CreateSchema() =>
        SearchViewContext.Default
            .GetSchemaInfo<PackageProfileView>()!
            .ToDocumentSchema();

    public static PackageProfileView CreateDocument(
        string prefix,
        IReadOnlyList<PackageProfileEvent> events,
        RowWindow? rowWindow = null)
    {
        PackageProfileSummary? summary = null;
        int totalRows = 0;
        foreach (PackageProfileEvent profileEvent in events)
        {
            switch (profileEvent)
            {
                case PackageProfileEvent.Match match:
                    totalRows = checked(
                        totalRows + MatchRowCount(match.Value));
                    break;
                case PackageProfileEvent.Failure:
                    totalRows = checked(totalRows + 1);
                    break;
                case PackageProfileEvent.Completed completed:
                    summary = completed.Value;
                    break;
            }
        }

        if (summary?.Truncated == true)
            totalRows = checked(totalRows + 1);

        (int keepStart, int keepEnd) = rowWindow is { IsUnlimited: false }
            ? rowWindow.Value.Resolve(totalRows)
            : (0, totalRows);
        var rows = new List<PackageProfileRow>(keepEnd - keepStart);
        int rowIndex = 0;
        foreach (PackageProfileEvent profileEvent in events)
        {
            if (rowIndex >= keepEnd)
                break;

            switch (profileEvent)
            {
                case PackageProfileEvent.Match match:
                    AddMatchRows(
                        rows,
                        match.Value,
                        ref rowIndex,
                        keepStart,
                        keepEnd);
                    break;
                case PackageProfileEvent.Failure failure:
                    if (rowIndex >= keepStart)
                        rows.Add(FailureRow(failure.Value));
                    rowIndex++;
                    break;
            }
        }

        if (summary?.Truncated == true && rowIndex < keepEnd)
        {
            if (rowIndex >= keepStart)
            {
                rows.Add(new PackageProfileRow(
                    EmptyCell,
                    EmptyCell,
                    EmptyCell,
                    EmptyCell,
                    EmptyCell,
                    EmptyCell,
                    EmptyCell,
                    EmptyCell,
                    EmptyCell,
                    Cell(summary.Producer.Value),
                    TruncatedCell,
                    Cell(TruncationMessage(summary))));
            }

            rowIndex++;
        }

        return new PackageProfileView(
            new InertString(TextPolicy.Prose, $"Find packages: {prefix}"),
            new InertString(TextPolicy.Prose, prefix),
            totalRows == 0
                ? new InertString(
                    TextPolicy.Prose,
                    "No packages found.")
                : null)
        {
            Packages = summary?.Matches ?? 0,
            Failures = summary?.Failures ?? 0,
            Truncated = summary?.Truncated ?? false,
            Results = totalRows == 0 ? null : rows,
        };
    }

    public static int CountRows(
        PackageProfileView view) =>
        view.Results?.Count ?? 0;

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
        PackageProfileMatch match,
        ref int rowIndex,
        int keepStart,
        int keepEnd)
    {
        int matchRowCount = MatchRowCount(match);
        if (rowIndex + matchRowCount <= keepStart
            || rowIndex >= keepEnd)
        {
            rowIndex += matchRowCount;
            return;
        }

        var packageCells = new PackageCells(
            Cell(match.PackageId),
            Cell(match.Version),
            Cell(string.Join(", ", match.Owners)),
            Cell(match.Manifest.Authors ?? ""),
            match.Verified ? YesCell : NoCell,
            Cell(match.TotalDownloads.ToString(CultureInfo.InvariantCulture)),
            Cell(match.Producer.Value));

        if (match.Manifest.DependencyGroups.IsEmpty)
        {
            AddMatchRow(
                rows,
                packageCells,
                EmptyCell,
                dependency: null,
                ref rowIndex,
                keepStart,
                keepEnd);
            return;
        }

        foreach (DeclaredPackageDependencyGroup group
            in match.Manifest.DependencyGroups)
        {
            if (group.Dependencies.IsEmpty)
            {
                AddMatchRow(
                    rows,
                    packageCells,
                    Cell(group.TargetFramework),
                    dependency: null,
                    ref rowIndex,
                    keepStart,
                    keepEnd);
                continue;
            }

            int groupRows = group.Dependencies.Length;
            if (rowIndex + groupRows <= keepStart)
            {
                rowIndex += groupRows;
                continue;
            }
            if (rowIndex >= keepEnd)
                return;

            InertString targetFramework = Cell(group.TargetFramework);
            foreach (DeclaredPackageDependency dependency
                in group.Dependencies)
            {
                AddMatchRow(
                    rows,
                    packageCells,
                    targetFramework,
                    dependency,
                    ref rowIndex,
                    keepStart,
                    keepEnd);
                if (rowIndex >= keepEnd)
                    return;
            }
        }
    }

    private static void AddMatchRow(
        List<PackageProfileRow> rows,
        PackageCells package,
        InertString targetFramework,
        DeclaredPackageDependency? dependency,
        ref int rowIndex,
        int keepStart,
        int keepEnd)
    {
        if (rowIndex >= keepStart && rowIndex < keepEnd)
        {
            rows.Add(new PackageProfileRow(
                package.Package,
                dependency is null ? EmptyCell : Cell(dependency.Id),
                package.Version,
                package.Owners,
                targetFramework,
                dependency is null
                    ? EmptyCell
                    : Cell(dependency.VersionRange),
                package.Authors,
                package.Verified,
                package.Downloads,
                package.Source,
                MatchedCell,
                EmptyCell));
        }

        rowIndex++;
    }

    private static PackageProfileRow FailureRow(
        PackageProfileFailure failure) =>
        new(
            Cell(failure.PackageId ?? ""),
            EmptyCell,
            Cell(failure.Version ?? ""),
            EmptyCell,
            EmptyCell,
            EmptyCell,
            EmptyCell,
            EmptyCell,
            EmptyCell,
            Cell(failure.Producer.Value),
            Cell(FailureStatus(failure)),
            Cell(failure.Message));

    private static string FailureStatus(PackageProfileFailure failure) =>
        failure.ManifestFailureReason is { } manifestReason
            ? $"{failure.Kind}:{manifestReason}"
            : failure.Kind.ToString();

    private static int MatchRowCount(PackageProfileMatch match)
    {
        if (match.Manifest.DependencyGroups.IsEmpty)
            return 1;

        int count = 0;
        foreach (DeclaredPackageDependencyGroup group
            in match.Manifest.DependencyGroups)
        {
            count = checked(
                count + Math.Max(1, group.Dependencies.Length));
        }

        return count;
    }

    private static InertString Cell(string value) =>
        new(TextPolicy.Field, value);

    private static readonly InertString EmptyCell =
        Cell("");
    private static readonly InertString MatchedCell =
        Cell("matched");
    private static readonly InertString TruncatedCell =
        Cell("truncated");
    private static readonly InertString YesCell =
        Cell("yes");
    private static readonly InertString NoCell =
        Cell("no");

    private readonly record struct PackageCells(
        InertString Package,
        InertString Version,
        InertString Owners,
        InertString Authors,
        InertString Verified,
        InertString Downloads,
        InertString Source);

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
