using System.Collections.Immutable;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Sections;
using DotnetInspector.PortableQueries;
using DotnetInspector.Queries;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Options;

public sealed record LibraryQueryOptions : IProjectionOptions
{
    public required string[] Sources { get; init; }
    public required LibraryQueryPlan Plan { get; init; }
    public RowSelectionIntent<string>? RowSelection { get; init; }
    public bool Count { get; init; }
    public bool JsonOutput { get; init; }
    public bool EnvelopeOutput { get; init; }
    public bool Tabular { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool NoHeader { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public string[]? Discover { get; init; }
    public bool Tree { get; init; }

    public static ImmutableArray<SectionQueryKey> QueryKeys { get; } =
    [
        .. LibraryQuery.RegisteredTerms.Select(term =>
            new SectionQueryKey(
                term.Descriptor.Key,
                ["--where"],
                [
                    .. term.Operators.Select(@operator =>
                        @operator == PortableQueryOperator.Equal
                            ? "="
                            : @operator.ToString()),
                ],
                term.Descriptor.ValueKind,
                [],
                $"--where \"{term.Descriptor.Key}={term.Descriptor.ExampleValue}\"")),
    ];

    public static string DiscoverySummary =>
        "Use library query with repeated --where references=<assembly-simple-name> terms. "
        + "Terms are ANDed at Library grain. Explicit files and top-level .dll files "
        + "from explicit directories form the ordered population. --take bounds "
        + $"candidate evaluation to at most {LibraryQuery.DefaultMaximumCandidates}.";

    public static bool TryCreate(
        string[] sources,
        IReadOnlyList<string> expressions,
        int maximumCandidates,
        out LibraryQueryOptions? options,
        out OptionError error)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(expressions);
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

            if (syntax.Operator != RowPredicateOperator.Equals)
            {
                error =
                    "Library Query terms support equality only; run "
                    + "'library query -Q Libraries' for the current vocabulary.";
                return false;
            }

            LibraryQueryRegisteredTerm? registered =
                LibraryQuery.RegisteredTerms.FirstOrDefault(term =>
                    term.Descriptor.Key.Equals(
                        syntax.Field,
                        StringComparison.OrdinalIgnoreCase));
            if (registered is null)
            {
                error =
                    $"Library Query does not define term '{syntax.Field}'; run "
                    + "'library query -Q Libraries' for the current vocabulary.";
                return false;
            }

            terms.Add(
                new(
                    registered.Descriptor.Key,
                    PortableQueryOperator.Equal,
                    syntax.Value));
        }

        PortableQueryIntent intent = PortableQueryIntent.Create(
            terms.ToImmutable(),
            [],
            [],
            []);
        LibraryQueryPlanResult result;
        try
        {
            result = LibraryQuery.ResolveIntent(
                intent,
                maximumCandidates);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            error = ex.Message;
            return false;
        }

        if (result is LibraryQueryPlanResult.Rejected rejected)
        {
            error =
                $"Library Query intent was rejected: "
                + $"{rejected.Failure.Reason} at "
                + $"{rejected.Failure.Location}"
                + (rejected.Failure.Offender is { } offender
                    ? $" ('{offender}')."
                    : ".");
            return false;
        }

        options = new()
        {
            Sources = sources,
            Plan = ((LibraryQueryPlanResult.Accepted)result).Plan,
        };
        error = "";
        return true;
    }
}
