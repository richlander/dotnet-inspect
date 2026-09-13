using System.Globalization;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspect.Cli.Views;
using InertText;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Sections;

public sealed record PackageProfileSectionCatalog(
    CompiledInspectionLens<PackageProfileQueryContext, PackageProfileView> Lens)
{
    public SectionCatalog<PackageProfileView> Sections => Lens.Sections;
    public InspectionQueryCatalog<PackageProfileQueryContext> QueryCatalog =>
        Lens.QueryCatalog;
    public SectionPipeline<PackageProfileView> Pipeline => Sections.Pipeline;
}

/// <summary>
/// Sections and row projection for a package-prefix profile.
/// </summary>
public static class PackageProfileSections
{
    public const string Packages = "Packages";

    /// <summary>The reusable fixed-domain catalog for package-profile queries.</summary>
    public static InspectionQueryCatalog<PackageProfileQueryContext>
        QueryCatalog { get; } = BuildQueryCatalog();

    /// <summary>The fixed package-profile producer domain.</summary>
    public static CompiledInspectionDomain<PackageProfileQueryContext> Domain
        { get; } = new(QueryCatalog);

    /// <summary>The reusable package-profile lens over the fixed producer domain.</summary>
    public static CompiledInspectionLens<
        PackageProfileQueryContext,
        PackageProfileView> Lens { get; } =
        Domain.CompileLens<PackageProfileView>(ConfigurePipeline);

    /// <summary>The reusable fixed-domain catalog for package-profile sections.</summary>
    public static SectionCatalog<PackageProfileView> SectionCatalog { get; } =
        Lens.Sections;

    /// <summary>The complete reusable package-profile section and query catalog.</summary>
    public static PackageProfileSectionCatalog Catalog { get; } =
        new(Lens);

    public static PackageProfileSectionCatalog CreateCatalog() => Catalog;

    public static SectionPipeline<PackageProfileView> CreatePipeline()
    {
        var pipeline = new SectionPipeline<PackageProfileView>()
            .UseQueryCosts(QueryCatalog.CostOf);
        ConfigurePipeline(pipeline);
        return pipeline;
    }

    public static InspectionQueryRegistry<PackageProfileQueryContext>
        CreateQueryRegistry() =>
        QueryCatalog.ToBuilder();

    private static InspectionQueryCatalog<PackageProfileQueryContext>
        BuildQueryCatalog() =>
        new InspectionQueryRegistry<PackageProfileQueryContext>()
            .AddAsync(
                PackageProfileQuery.Definition,
                static (context, cancellationToken) =>
                    PackageProfileQuery.ExecuteToArrayAsync(
                    context.Source,
                    context.Request,
                    cancellationToken,
                    context.OperationContext))
            .Compile();

    private static void ConfigurePipeline(
        SectionPipeline<PackageProfileView> pipeline)
    {
        pipeline
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<PackageRows>(PackageProfileQuery.Definition);
    }

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
        var matches = new List<PackageProfileMatch>();
        foreach (PackageProfileEvent profileEvent in events)
        {
            switch (profileEvent)
            {
                case PackageProfileEvent.Match match:
                    matches.Add(match.Value);
                    break;
                case PackageProfileEvent.Completed completed:
                    summary = completed.Value;
                    break;
            }
        }

        IReadOnlyList<PackageProfileMatch> selected =
            RowWindow.Apply(rowWindow, matches);
        List<PackageProfileRow> rows =
            [.. selected.Select(MatchRow)];

        return new PackageProfileView(
            new InertString(TextPolicy.Prose, $"Find packages: {prefix}"),
            new InertString(TextPolicy.Prose, prefix),
            matches.Count == 0
                ? new InertString(
                    TextPolicy.Prose,
                    "No packages found.")
                : null)
        {
            Packages = rows.Count,
            Failures = summary?.Failures ?? 0,
            Truncated = summary?.Truncated ?? false,
            Results = matches.Count == 0 ? null : rows,
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

    private static PackageProfileRow MatchRow(
        PackageProfileMatch match) =>
        new(
            Cell(match.PackageId),
            Cell(match.Version),
            Cell(string.Join(", ", match.Owners)),
            Cell(match.Manifest.Authors ?? ""),
            match.Verified ? YesCell : NoCell,
            Cell(match.TotalDownloads.ToString(CultureInfo.InvariantCulture)),
            match.Source.Producer.Display);

    private static InertString Cell(string value) =>
        new(TextPolicy.Field, value);

    private static readonly InertString YesCell =
        Cell("yes");
    private static readonly InertString NoCell =
        Cell("no");
}
