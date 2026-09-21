using System.Collections.Immutable;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Ecosystems;
using QuerySpace;
using DotnetInspector.Queries;
using QuerySpace.Rows;
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
    public bool EnvelopeOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool Tabular { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool NoHeader { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public string[]? Discover { get; init; }
    public HashSet<string>? IncludeSections { get; init; }
    public bool SelectDefault { get; init; }
    public bool Tree { get; init; }

    internal bool IsContentJson =>
        JsonOutput
        && RowSelection.Operations.Count == 0
        && !Count
        && Columns is null
        && Fields is null
        && !Tree
        && IncludeSections is null
        && !SelectDefault;

    private static ImmutableArray<
        PackageQueryRegisteredTerm> CliTerms { get; } =
    [
        .. PackageQuery.RegisteredTerms.Where(term =>
            term.Descriptor.Role == PackageQueryTermRole.Inspection),
    ];

    public static ImmutableArray<SectionQueryKey> QueryKeys { get; } =
    [
        .. CliTerms.Select(term => new SectionQueryKey(
            term.Descriptor.Key,
            ["--where"],
            [
                .. term.Operators.Select(
                    RowPredicateSyntaxParser.Comparison),
            ],
            term.Descriptor.ValueKind,
            [.. term.Descriptor.Options.Select(option => option.Value)],
            $"--where \"{term.Descriptor.Key}={term.Descriptor.ExampleValue}\"",
            PackageQuery.ExecutionClassIdentity(
                term.Descriptor.ExecutionClass))),
    ];

    public static string DiscoverySummary =>
        "Use package query with repeated --where terms. "
        + "Terms are ANDed; repeated tool-format values are ORed. "
        + "depends=<package ID> matches a direct declared dependency; "
        + "depends starts-with <package ID prefix> matches a direct declared "
        + "dependency by literal prefix; "
        + "depends-transitive=<package ID> requires dependency-target=<TFM> "
        + "and dependency-depth=2|3|4; "
        + "dependencies=cross-prefix matches a dependency from another first ID segment; "
        + "depends-ecosystem=<ecosystem ID> matches a registered package population; "
        + "dependency-target=all|<TFM> selects its manifest-group scope. "
        + "--take bounds package candidates; -n and --rows select final matching package rows. "
        + "A lone Head is pushed into execution when no explicit --take is present. "
        + "Selecting a nuspec-expensive term evaluates at most "
        + PackageQuery.MaximumNuspecExpensiveCandidates
        + " candidates. "
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

            PortableQueryOperator @operator =
                RowPredicateSyntaxParser.PortableOperator(
                    syntax.Operator);
            if (!CliTerms.Any(term =>
                    term.Operators.Contains(@operator)))
            {
                error =
                    "Package Query terms currently support equality and "
                    + "starts-with; run 'package query -Q Packages' for "
                    + "admitted operators and values.";
                return false;
            }

            PackageQueryRegisteredTerm? registeredTerm = CliTerms.FirstOrDefault(
                term => term.Descriptor.Key.Equals(
                    syntax.Field,
                    StringComparison.OrdinalIgnoreCase));
            if (registeredTerm is not null)
            {
                terms.Add(new PortableQueryTerm(
                    registeredTerm.Descriptor.Key,
                    @operator,
                    syntax.Value));
                continue;
            }

            error =
                $"Package Query does not define term '{syntax.Field}'; run "
                + "'package query -Q Packages' for the current vocabulary.";
            return false;
        }

        bool requiresPackageContent = terms.Any(term =>
            CliTerms.Any(registered =>
                registered.Descriptor.Key == term.Key
                && registered.Descriptor.Tier
                    == PackageQueryAcquisitionTier.PackageContent));
        bool requiresNuspecExpensive = terms.Any(term =>
            CliTerms.Any(registered =>
                registered.Descriptor.Key == term.Key
                && registered.Descriptor.ExecutionClass
                    == PackageQueryExecutionClass.NuspecExpensive));
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
        int defaultMaximumCandidates = requiresNuspecExpensive
            ? PackageQuery.MaximumNuspecExpensiveCandidates
            : requiresPackageContent
                ? PackageQuery.MaximumPackageContentCandidates
                : requestedHead is int head
                    && input.Trim().EndsWith('*')
                    && terms.Count == 0
                    ? Math.Min(head, MaximumCandidates)
                    : PackageQuery.DefaultMaximumCandidates;
        int maximumCandidates = take ?? defaultMaximumCandidates;
        if (maximumCandidates is <= 0 or > MaximumCandidates)
        {
            error =
                $"Package Query --take must be between 1 and {MaximumCandidates}.";
            return false;
        }

        PackageQueryPlanResult result = PackageQuery.PlanInput(
            input,
            EcosystemPackCatalog.PackageQueryMemberships,
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
