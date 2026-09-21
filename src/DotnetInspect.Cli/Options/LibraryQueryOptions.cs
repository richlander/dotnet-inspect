using System.Collections.Immutable;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using QuerySpace;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspect.Cli.Options;

public abstract record LibraryQueryPopulation
{
    private LibraryQueryPopulation()
    {
    }

    public abstract string DisplayName { get; }

    public sealed record Directory(string Path) : LibraryQueryPopulation
    {
        public override string DisplayName => Path;
    }

    public sealed record PlatformFramework(string Framework)
        : LibraryQueryPopulation
    {
        public override string DisplayName => Framework;
    }
}

public sealed record LibraryQueryOptions : IProjectionOptions
{
    public required LibraryQueryPlan Plan { get; init; }
    public LibraryQueryPopulation? Population { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
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
        && Plan.RowSelection.Operations.Count == 0
        && !Count
        && Columns is null
        && Fields is null
        && !Tree
        && IncludeSections is null
        && !SelectDefault;

    public static ImmutableArray<SectionQueryKey> QueryKeys { get; } =
    [
        .. LibraryQuery.RegisteredTerms.Select(term =>
            new SectionQueryKey(
                term.Descriptor.Key,
                ["--where"],
                [
                    .. term.Operators.Select(
                        RowPredicateSyntaxParser.Comparison),
                ],
                term.Descriptor.ValueKind,
                [],
                $"--where \"{term.Descriptor.Key}={term.Descriptor.ExampleValue}\"",
                "metadata")),
    ];

    public static string DiscoverySummary =>
        "Use library query with repeated --where terms. "
        + "references=<assembly simple name> matches a direct AssemblyRef; "
        + "repeated reference terms are ANDed. "
        + "--take bounds candidate Libraries; -n and --rows select final "
        + "matching Library rows. Ordering and --top are not supported.";

    public static bool TryCreate(
        IReadOnlyList<string> expressions,
        int? take,
        RowSelectionIntent<string>? rowSelection,
        out LibraryQueryPlan? plan,
        out OptionError error)
    {
        if (expressions.Count
            > PortableQueryPayloadCodec.MaxTerms)
        {
            plan = null;
            error =
                "Library Query accepts at most "
                + $"{PortableQueryPayloadCodec.MaxTerms} --where terms.";
            return false;
        }

        var terms = ImmutableArray.CreateBuilder<PortableQueryTerm>();
        foreach (string expression in expressions)
        {
            if (!RowPredicateSyntaxParser.TryParse(
                    expression,
                    out RowPredicateSyntax syntax,
                    out error))
            {
                plan = null;
                return false;
            }
            PortableQueryOperator @operator =
                RowPredicateSyntaxParser.PortableOperator(
                    syntax.Operator);
            if (!LibraryQuery.RegisteredTerms.Any(term =>
                    term.Operators.Contains(@operator)))
            {
                plan = null;
                error =
                    "Library Query terms currently support equality; run "
                    + "'library query -Q Libraries' for keys and values.";
                return false;
            }

            LibraryQueryRegisteredTerm? registered =
                LibraryQuery.RegisteredTerms.FirstOrDefault(term =>
                    term.Descriptor.Key.Equals(
                        syntax.Field,
                        StringComparison.OrdinalIgnoreCase));
            if (registered is null)
            {
                plan = null;
                error =
                    $"Library Query does not define term '{syntax.Field}'; "
                    + "run 'library query -Q Libraries' for the current vocabulary.";
                return false;
            }

            terms.Add(
                new(
                    registered.Descriptor.Key,
                    @operator,
                    syntax.Value));
        }

        int maximumCandidates =
            take ?? LibraryQuery.DefaultMaximumCandidates;
        if (maximumCandidates is <= 0
            or > LibraryQuery.MaximumCandidates)
        {
            plan = null;
            error =
                "Library Query --take must be between 1 and "
                + $"{LibraryQuery.MaximumCandidates}.";
            return false;
        }

        LibraryQueryPlanResult result = LibraryQuery.Plan(
            new(
                terms.ToImmutable(),
                maximumCandidates,
                rowSelection));
        if (result is LibraryQueryPlanResult.Rejected rejected)
        {
            plan = null;
            error =
                "Library Query could not resolve its registered capabilities "
                + $"({rejected.Failure.Reason}).";
            return false;
        }

        plan = ((LibraryQueryPlanResult.Accepted)result).Plan;
        error = "";
        return true;
    }

    internal AssemblySetRequest CreateAssemblySetRequest(
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(Population);
        return Population switch
        {
            LibraryQueryPopulation.Directory directory =>
                new()
                {
                    Directories = [directory.Path],
                    SourceOptions = SourceOptions,
                    SourceOrder = [AssemblySetSourceKind.Directory],
                    CancellationToken = cancellationToken,
                },
            LibraryQueryPopulation.PlatformFramework platform =>
                new()
                {
                    PlatformFrameworks = [platform.Framework],
                    SourceOptions = SourceOptions,
                    SourceOrder =
                        [AssemblySetSourceKind.PlatformFramework],
                    CancellationToken = cancellationToken,
                },
            _ => throw new InvalidOperationException(
                "Unknown Library Query population."),
        };
    }

}
