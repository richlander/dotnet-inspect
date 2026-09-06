using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspector.Options;

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
        + "--candidates bounds candidate work; --matches stops after matching packages. "
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
        int? candidates,
        int? matches,
        bool count,
        string? typeFilter,
        out PackageQueryOptions? options,
        out OptionError error)
    {
        options = null;
        if (typeFilter is not null)
        {
            error = "Package Query uses --candidates and --matches, not -t/--type.";
            return false;
        }
        if (count && matches is not null)
        {
            error = "--count cannot be combined with --matches; it counts matching rows within the candidate budget.";
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

        int maximumCandidates = candidates ?? (packageContent
            ? PackageQuery.MaximumPackageContentCandidates
            : PackageQuery.DefaultMaximumCandidates);
        int maximumMatches = matches ?? (count
            ? maximumCandidates
            : PackageQuery.DefaultMaximumMatches);
        if (maximumCandidates is <= 0 or > Commands.FindCommand.PackageProfileMaximumLimit
            || maximumMatches is <= 0 or > Commands.FindCommand.PackageProfileMaximumLimit)
        {
            error = "Package Query --candidates and --matches must be between 1 and 1000.";
            return false;
        }
        var request = new PackageQueryRequest(
            prefix, ids.ToImmutable(), maximumCandidates, maximumMatches);
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
