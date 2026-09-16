using System.Collections.Immutable;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.PortableQueries;
using DotnetInspector.Queries;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Options;

public sealed record PackageQueryOptions : IProjectionOptions
{
    public const int MaximumCandidates = 1_000;

    public required PackageQueryPlan Plan { get; init; }
    public bool SemanticHeadPushedDown { get; init; }
    public RowSelectionIntent<string>? RowSelection { get; init; }
    public bool Count { get; init; }
    public bool JsonOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool Tabular { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool NoHeader { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public string[]? Discover { get; init; }
    public bool Tree { get; init; }

    private static ImmutableArray<PackageQueryFacetDescriptor> CliFacets { get; } =
    [
        .. PackageQuery.Facets.Where(facet => facet.Id is
            PackageQuery.ToolFacetId
            or PackageQuery.ToolV1FacetId
            or PackageQuery.ToolV2FacetId),
    ];

    public static SectionQueryKey QueryKey { get; } = new(
        "facet",
        ["--where"],
        ["="],
        "product-issued Package Query facet ID",
        [.. CliFacets.Select(facet => facet.Id)],
        "--where \"facet=package.query.dotnet-tool\"");

    public static SectionQueryKey DependsTerm { get; } = new(
        PackageQuery.DependsTermKey,
        ["--where"],
        ["="],
        "NuGet package ID",
        [],
        "--where \"depends=Microsoft.Extensions.DependencyInjection\"");

    public static ImmutableArray<SectionQueryKey> QueryKeys { get; } =
        [DependsTerm, QueryKey];

    public static string DiscoverySummary =>
        "Use package query with repeated --where terms. "
        + "depends=<package ID> matches a direct declared dependency; repeated "
        + "depends terms are ANDed. Existing facet=<product facet ID> selections "
        + "remain available while the Browser adopts the shared term vocabulary. "
        + "Independent facet selections are ANDed; compatible tool-format alternatives are ORed. "
        + "--take bounds package candidates; -n and --rows select final matching package rows. "
        + "A lone Head is pushed into execution when no explicit --take is present. "
        + "Selecting an initial CLI facet authorizes package content and at most "
        + PackageQuery.MaximumPackageContentCandidates
        + " candidates; --nuspec-only rejects those facets. "
        + "Ordering and --top are not supported.";

    public static bool TryCreate(
        string input,
        IReadOnlyList<string> expressions,
        bool nuspecOnly,
        int? take,
        RowSelectionIntent<string>? rowSelection,
        bool includePrerelease,
        out PackageQueryOptions? options,
        out OptionError error)
    {
        options = null;
        var ids = ImmutableArray.CreateBuilder<string>();
        var terms = ImmutableArray.CreateBuilder<PortableQueryTerm>();
        foreach (string expression in expressions)
        {
            if (!RowPredicateSyntaxParser.TryParse(
                    expression,
                    out RowPredicateSyntax syntax,
                    out error))
            {
                return false;
            }

            if (syntax.Operator != RowPredicateOperator.Equals)
            {
                error =
                    "Package Query terms currently support equality; run "
                    + "'package query -Q Packages' for keys and values.";
                return false;
            }

            if (syntax.Field.Equals("facet", StringComparison.OrdinalIgnoreCase))
            {
                if (!CliFacets.Any(facet =>
                        facet.Id.Equals(syntax.Value, StringComparison.Ordinal)))
                {
                    error =
                        $"Package Query facet '{syntax.Value}' is not available in the CLI; "
                        + "run 'package query -Q Packages' for values.";
                    return false;
                }

                ids.Add(syntax.Value);
                continue;
            }

            if (syntax.Field.Equals(
                    PackageQuery.DependsTermKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                terms.Add(new PortableQueryTerm(
                    PackageQuery.DependsTermKey,
                    PortableQueryOperator.Equal,
                    syntax.Value));
                continue;
            }

            error =
                "Package Query supports --where \"depends=<package ID>\" "
                + "and the staged \"facet=<product facet ID>\" form; run "
                + "'package query -Q Packages' for the current vocabulary.";
            return false;
        }

        bool requiresPackageContent = ids.Any(id =>
            CliFacets.Any(facet =>
                facet.Id.Equals(id, StringComparison.Ordinal)
                && facet.Tier == PackageQueryFacetTier.PackageContent));
        if (nuspecOnly && requiresPackageContent)
        {
            error =
                "The selected Package Query facets require package archive content "
                + "and cannot be combined with --nuspec-only.";
            return false;
        }

        int? requestedHead = take is null
            ? SingleHeadCount(rowSelection)
            : null;
        int? semanticHead = requestedHead is <= MaximumCandidates
            ? requestedHead
            : null;
        int maximumCandidates = take ?? (requiresPackageContent
            ? PackageQuery.MaximumPackageContentCandidates
            : requestedHead is int head
                && input.Trim().EndsWith('*')
                && ids.Count == 0
                && terms.Count == 0
                ? Math.Min(head, MaximumCandidates)
                    : PackageQuery.DefaultMaximumCandidates);
        if (maximumCandidates is <= 0 or > MaximumCandidates)
        {
            error =
                $"Package Query --take must be between 1 and {MaximumCandidates}.";
            return false;
        }

        PackageQueryPlanResult result = PackageQuery.PlanInput(
            input,
            ids.ToImmutable(),
            terms.ToImmutable(),
            maximumCandidates,
            maximumMatches: semanticHead,
            includePrerelease);
        if (result is PackageQueryPlanResult.Rejected rejected)
        {
            error = rejected.Failure.Message;
            return false;
        }

        options = new PackageQueryOptions
        {
            Plan = ((PackageQueryPlanResult.Accepted)result).Plan,
            SemanticHeadPushedDown = semanticHead is not null,
        };
        error = "";
        return true;
    }

    private static int? SingleHeadCount(
        RowSelectionIntent<string>? rowSelection)
    {
        if (rowSelection?.Operations.Count != 1)
            return null;

        RowSelectionIntentOperation<string> operation =
            rowSelection.Operations[0];
        return operation.Kind == RowSelectionStageKind.Head
            ? operation.Count
            : null;
    }
}
