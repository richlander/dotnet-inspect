using System.Collections.Immutable;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.CommandLine;
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
    internal PackageAssemblySemanticQueryCliPlan? LibraryLiteralPlan { get; init; }
    public bool SemanticHeadPushedDown { get; init; }
    public RowSelectionIntent<string> RowSelection => Plan.RowSelection;
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

    private static ImmutableArray<PackageQueryTermDescriptor> CliTerms { get; } =
    [
        .. PackageQuery.Terms.Where(term =>
            term.Role == PackageQueryTermRole.Inspection),
    ];

    public static ImmutableArray<SectionQueryKey> QueryKeys { get; } =
    [
        .. CliTerms.Select(term => new SectionQueryKey(
            term.Key,
            ["--where"],
            ["="],
            term.ValueKind,
            [.. term.Options.Select(option => option.Value)],
            $"--where \"{term.Key}={term.ExampleValue}\"")),
    ];

    public static string DiscoverySummary =>
        "Use package query with repeated --where terms. "
        + "Terms are ANDed; repeated tool-format values are ORed. "
        + "depends=<package ID> matches a direct declared dependency; "
        + "dependency-target=all|<TFM> selects its manifest-group scope. "
        + "--take bounds package candidates; -n and --rows select final matching package rows. "
        + "A lone Head is pushed into execution when no explicit --take is present. "
        + "Selecting a package-content term authorizes at most "
        + PackageQuery.MaximumPackageContentCandidates
        + " candidates; --nuspec-only rejects those terms. "
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
        => TryCreate(
            input,
            expressions,
            nuspecOnly,
            take,
            rowSelection,
            includePrerelease,
            libraryLiteral: null,
            targetFramework: null,
            out options,
            out error);

    public static bool TryCreate(
        string input,
        IReadOnlyList<string> expressions,
        bool nuspecOnly,
        int? take,
        RowSelectionIntent<string>? rowSelection,
        bool includePrerelease,
        string? libraryLiteral,
        string? targetFramework,
        out PackageQueryOptions? options,
        out OptionError error)
    {
        options = null;
        if (libraryLiteral is not null)
        {
            if (expressions.Count > 0)
            {
                error =
                    "--library-literal cannot yet be combined with --where; "
                    + "run the package filters and library-literal query separately.";
                return false;
            }
            if (nuspecOnly)
            {
                error =
                    "--library-literal requires package and assembly content "
                    + "and cannot be combined with --nuspec-only.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(targetFramework))
            {
                error =
                    "--library-literal requires an explicit --tfm "
                    + "(for example --tfm net10.0).";
                return false;
            }

            try
            {
                PackageAssemblySemanticQueryCliPlan semanticPlan =
                    PackageAssemblySemanticQueryCliPlan.Create(
                        input,
                        libraryLiteral,
                        targetFramework,
                        take,
                        includePrerelease);
                int semanticMaximumCandidates =
                    semanticPlan.Population
                        is PackageAssemblySemanticQueryPopulationPlan.Prefix
                            prefix
                        ? prefix.MaximumCandidates
                        : 1;
                PackageQueryPlanResult packagePlan = PackageQuery.PlanInput(
                    input,
                    terms: null,
                    maximumCandidates: semanticMaximumCandidates,
                    maximumMatches: null,
                    includePrerelease: includePrerelease,
                    rowSelection: rowSelection);
                if (packagePlan
                    is PackageQueryPlanResult.Rejected semanticRejected)
                {
                    error = semanticRejected.Failure.Message;
                    return false;
                }

                options = new PackageQueryOptions
                {
                    Plan = ((PackageQueryPlanResult.Accepted)packagePlan).Plan,
                    LibraryLiteralPlan = semanticPlan,
                };
                error = "";
                return true;
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return false;
            }
        }

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

            PackageQueryTermDescriptor? descriptor = CliTerms.FirstOrDefault(
                term => term.Key.Equals(
                    syntax.Field,
                    StringComparison.OrdinalIgnoreCase));
            if (descriptor is not null)
            {
                terms.Add(new PortableQueryTerm(
                    descriptor.Key,
                    PortableQueryOperator.Equal,
                    syntax.Value));
                continue;
            }

            error =
                $"Package Query does not define term '{syntax.Field}'; run "
                + "'package query -Q Packages' for the current vocabulary.";
            return false;
        }

        bool requiresPackageContent = terms.Any(term =>
            CliTerms.Any(descriptor =>
                descriptor.Key == term.Key
                && descriptor.Tier == PackageQueryAcquisitionTier.PackageContent));
        if (nuspecOnly && requiresPackageContent)
        {
            error =
                "The selected Package Query terms require package archive content "
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
            terms.ToImmutable(),
            maximumCandidates,
            maximumMatches: semanticHead,
            includePrerelease,
            rowSelection);
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
