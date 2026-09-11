using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Options;

public sealed record PackageQueryOptions
{
    private PackageQueryOptions(PackageQueryPlan plan, bool packageContent)
    {
        Plan = plan;
        PackageContent = packageContent;
    }

    public PackageQueryPlan Plan { get; }
    public bool PackageContent { get; }

    public static SectionQueryFacet QueryFacet { get; } = new(
        "facet",
        ["--where"],
        ["="],
        "product-issued Package Query facet ID",
        [.. PackageQuery.Facets.Select(facet => facet.Id)],
        "--where \"facet=package.query.dotnet-tool\"");

    public static string DiscoverySummary =>
        "Use patternless find --package-prefix with repeated --where facet=... selections. "
        + "Independent selections are ANDed; compatible alternatives in one product selection group are ORed. "
        + "--take bounds candidate work; -n and --rows select final matching packages. "
        + "Package-content values require --package-content and at most "
        + PackageQuery.MaximumPackageContentCandidates + " candidates: "
        + string.Join(", ", PackageQuery.Facets
            .Where(facet => facet.Tier == PackageQueryFacetTier.PackageContent)
            .Select(facet => facet.Id))
        + ". Ordering and --top are not supported.";

    public static bool TryCreate(
        string prefix,
        IReadOnlyList<string> expressions,
        bool packageContent,
        int? take,
        string? typeFilter,
        out PackageQueryOptions? options,
        out OptionError error)
    {
        options = null;
        if (typeFilter is not null)
        {
            error = "Package Query does not support the --type API filter.";
            return false;
        }

        var ids = ImmutableArray.CreateBuilder<string>();
        foreach (string expression in expressions)
        {
            if (!RowPredicateSyntaxParser.TryParse(expression, out var syntax, out error))
                return false;
            if (!syntax.Field.Equals("facet", StringComparison.OrdinalIgnoreCase)
                || syntax.Operator != RowPredicateOperator.Equals)
            {
                error = "Package Query supports --where \"facet=<product facet ID>\"; run 'find -Q Packages' for values.";
                return false;
            }
            ids.Add(syntax.Value);
        }

        if (!packageContent && PackageQuery.Facets.Any(facet =>
            facet.Tier == PackageQueryFacetTier.PackageContent
            && ids.Contains(facet.Id)))
        {
            error = "Package-content facets require --package-content (at most 20 candidates).";
            return false;
        }

        int maximumCandidates = take ?? (packageContent
            ? PackageQuery.MaximumPackageContentCandidates
            : PackageQuery.DefaultMaximumCandidates);
        if (maximumCandidates is <= 0
            or > Commands.FindCommand.PackageProfileMaximumLimit)
        {
            error = "Package Query --take must be between 1 and 1000.";
            return false;
        }
        var request = new PackageQueryRequest(
            prefix,
            ids.ToImmutable(),
            maximumCandidates,
            MaximumMatches: null);
        PackageQueryPlanResult result = PackageQuery.Plan(request);
        if (result is PackageQueryPlanResult.Rejected rejected)
        {
            error = rejected.Failure.Message;
            return false;
        }
        var accepted = (PackageQueryPlanResult.Accepted)result;
        options = new(accepted.Plan, packageContent);
        error = "";
        return true;
    }
}
