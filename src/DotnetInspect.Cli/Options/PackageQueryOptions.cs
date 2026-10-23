using System.Collections.Immutable;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Output;
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
    public bool SemanticHeadPushedDown { get; init; }

    /// <summary>
    /// The candidate bound is part of the request: an explicit --take, or a
    /// direct row path's Head. Reaching it is the answer's scope, not
    /// incompleteness (cli-execution-bounds.md#count). A default ceiling is
    /// not requested, so reaching it leaves a partial view.
    /// </summary>
    public bool CandidateBoundRequested { get; init; }

    /// <summary>The user set the candidate bound with --take.</summary>
    public bool ExplicitTake { get; init; }
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
            PackageQueryCommandCapability.Binding.ExposedQueryTerms.Contains(
                PackageQuery.TermBindingIdentity(
                    term.Descriptor.Key),
                StringComparer.Ordinal)),
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
                term.Descriptor.ExecutionClass),
            PackageQueryCapabilityResourcePaths
                .QueryFacet(term.Descriptor.Key)
                .Value)),
    ];

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
        string? targetFramework,
        out PackageQueryOptions? options,
        out OptionError error)
        => TryCreate(
            input,
            ecosystem: null,
            expressions,
            nuspecOnly,
            take,
            rowSelection,
            includePrerelease,
            targetFramework,
            out options,
            out error);

    /// <summary>
    /// Plans a package query whose population is a positional package ID or
    /// prefix, or an Ecosystem from --ecosystem or --where ecosystem=.
    /// </summary>
    public static bool TryCreate(
        string? input,
        string? ecosystem,
        IReadOnlyList<string> expressions,
        bool nuspecOnly,
        int? take,
        RowSelectionIntent<string>? rowSelection,
        bool includePrerelease,
        string? targetFramework,
        out PackageQueryOptions? options,
        out OptionError error)
    {
        options = null;
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

            if (syntax.Field.Equals(
                    PackageQuery.EcosystemTermKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (RowPredicateSyntaxParser.PortableOperator(syntax.Operator)
                    != PortableQueryOperator.Equal)
                {
                    error =
                        $"Package Query term '{PackageQuery.EcosystemTermKey}' "
                        + "supports only '=' predicates.";
                    return false;
                }

                if (ecosystem is null)
                    ecosystem = syntax.Value;
                else
                {
                    terms.Add(new PortableQueryTerm(
                        PackageQuery.EcosystemTermKey,
                        PortableQueryOperator.Equal,
                        syntax.Value));
                }
                continue;
            }

            if (syntax.Field.Equals(
                    PackageQuery.PackageTermKey,
                    StringComparison.OrdinalIgnoreCase)
                || syntax.Field.Equals(
                    PackageQuery.PrefixTermKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    $"Package Query term '{syntax.Field}' is positional; pass "
                    + "an exact package ID or a prefix ending in '*' as the "
                    + "package argument.";
                return false;
            }

            PackageQueryRegisteredTerm? registeredTerm = CliTerms.FirstOrDefault(
                term => term.Descriptor.Key.Equals(
                    syntax.Field,
                    StringComparison.OrdinalIgnoreCase));
            if (registeredTerm is null)
            {
                error =
                    $"Package Query does not define term '{syntax.Field}'; run "
                    + "'package query -Q Packages' for the current vocabulary.";
                return false;
            }

            PortableQueryOperator @operator =
                RowPredicateSyntaxParser.PortableOperator(
                    syntax.Operator);
            if (!registeredTerm.Operators.Contains(@operator))
            {
                error =
                    $"Package Query term '{registeredTerm.Descriptor.Key}' "
                    + "does not support "
                    + $"'{RowPredicateSyntaxParser.Comparison(@operator)}' "
                    + "predicates; run 'package query -Q Packages' for "
                    + "admitted operators and values.";
                return false;
            }

            terms.Add(new PortableQueryTerm(
                registeredTerm.Descriptor.Key,
                @operator,
                registeredTerm.Descriptor.ControlKind
                    == PackageQueryTermControlKind.MultilineInput
                        ? syntax.ExactValue
                        : syntax.Value));
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
        bool requiresMetadataExpensive = terms.Any(term =>
            CliTerms.Any(registered =>
                registered.Descriptor.Key == term.Key
                && registered.Descriptor.ExecutionClass
                    == PackageQueryExecutionClass.MetadataExpensive));
        if (nuspecOnly && requiresPackageContent)
        {
            error =
                "The selected Package Query terms require package archive content "
                + "and cannot be combined with --nuspec-only.";
            return false;
        }

        // A lone semantic Head within the match-budget maximum is pushed into
        // execution with or without an explicit --take; inside a --take window
        // it stops at the Nth match or the window's end, whichever comes first.
        int? requestedHead = SingleHeadCount(rowSelection);
        int? semanticHead = requestedHead is <= MaximumCandidates
            ? requestedHead
            : null;
        // Without --take, a direct metadata row path is one candidate per
        // final row, so the Head is also its candidate bound: `-n 2` has the
        // work shape `--take 2 -n 2`.
        bool directHeadPath =
            !requiresMetadataExpensive
            && !requiresNuspecExpensive
            && !requiresPackageContent
            && requestedHead is not null
            && (ecosystem is not null
                || input?.Trim().EndsWith('*') == true)
            && terms.Count == 0;
        int defaultMaximumCandidates = requiresMetadataExpensive
            ? PackageQuery.MaximumMetadataExpensiveCandidates
            : requiresNuspecExpensive
            ? PackageQuery.MaximumNuspecExpensiveCandidates
            : requiresPackageContent
                ? PackageQuery.MaximumPackageContentCandidates
                : directHeadPath
                    ? Math.Min(requestedHead!.Value, MaximumCandidates)
                    : PackageQuery.DefaultMaximumCandidates;
        int maximumCandidates = take ?? defaultMaximumCandidates;
        if (maximumCandidates is <= 0 or > MaximumCandidates)
        {
            error =
                $"Package Query --take must be between 1 and {MaximumCandidates}.";
            return false;
        }

        if (ecosystem is not null && input is not null)
        {
            error =
                "Package Query --ecosystem and --where ecosystem= cannot be "
                + "combined with a package ID or prefix argument.";
            return false;
        }
        if (ecosystem is null && input is null)
        {
            error =
                "Package Query requires an exact package ID, a literal "
                + "package-ID prefix ending in '*', or --ecosystem <id>.";
            return false;
        }

        PackageQueryPlanResult result = ecosystem is not null
            ? PackageQuery.PlanEcosystemInput(
                ecosystem,
                EcosystemPackCatalog.PackageQueryMemberships,
                terms.ToImmutable(),
                maximumCandidates,
                maximumMatches: semanticHead,
                includePrerelease,
                rowSelection,
                targetFramework)
            : PackageQuery.PlanInput(
                input!,
                EcosystemPackCatalog.PackageQueryMemberships,
                terms.ToImmutable(),
                maximumCandidates,
                maximumMatches: semanticHead,
                includePrerelease,
                rowSelection,
                targetFramework);
        if (result is PackageQueryPlanResult.Rejected rejected)
        {
            error = rejected.Failure.Message;
            return false;
        }

        options = new PackageQueryOptions
        {
            Plan = ((PackageQueryPlanResult.Accepted)result).Plan,
            SemanticHeadPushedDown = semanticHead is not null,
            CandidateBoundRequested =
                take is not null
                || (directHeadPath && semanticHead is not null),
            ExplicitTake = take is not null,
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
