using System.Collections.Immutable;

using DotnetInspect.Cli.Sections;
using QuerySpace;
using DotnetInspector.Queries;
using ILInspector.CSharp;

namespace DotnetInspect.Cli.Options;

public sealed record LibraryCallUseQueryOptions
{
    private LibraryCallUseQueryOptions(GraphLibrariesQueryPlan plan)
    {
        Plan = plan;
    }

    public GraphLibrariesQueryPlan Plan { get; }

    internal static ImmutableArray<SectionQueryKey> QueryKeys(
        string section) =>
    [
        .. GraphLibrariesQuery.RegisteredTermsForRowSet(
                RowSet(section))
            .Select(term => new SectionQueryKey(
                term.Descriptor.Key,
                ["--where"],
                [
                    .. term.Operators.Select(
                        RowPredicateSyntaxParser.Comparison),
                ],
                term.Descriptor.ValueKind,
                [],
                $"--where \"{term.Descriptor.Key}"
                    + $"={term.Descriptor.ExampleValue}\"")),
    ];

    public static bool TryParse(
        IReadOnlyList<string> expressions,
        out LibraryCallUseQueryOptions options,
        out OptionError error)
    {
        if (expressions.Count > 1)
        {
            options = null!;
            error =
                "graph libraries accepts exactly one --where Cluster=... predicate.";
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
                options = null!;
                return false;
            }

            GraphLibrariesQueryRegisteredTerm? term =
                GraphLibrariesQuery.RegisteredTerms
                    .SingleOrDefault(candidate =>
                        RowPredicateSyntaxParser.NormalizeFieldName(
                                syntax.Field)
                            .Equals(
                                candidate.Descriptor.Key,
                                StringComparison.OrdinalIgnoreCase));
            if (term is null)
            {
                string field =
                    CSharpIdentifier.ContainRenderedText(syntax.Field);
                options = null!;
                error = new OptionError(
                    $"Field '{field}' is not queryable by graph libraries.",
                    ["Use --where \"Cluster=<positive ordinal>\"."]);
                return false;
            }

            terms.Add(
                new(
                    term.Descriptor.Key,
                    RowPredicateSyntaxParser.PortableOperator(
                        syntax.Operator),
                    syntax.Value));
        }

        GraphLibrariesQueryPlanResult result =
            GraphLibrariesQuery.ResolveIntent(
                PortableQueryIntent.Create(
                    [.. terms],
                    [],
                    [],
                    []));
        if (result is GraphLibrariesQueryPlanResult.Rejected rejected)
        {
            options = null!;
            error = Error(rejected.Failure);
            return false;
        }

        options = new(
            ((GraphLibrariesQueryPlanResult.Accepted)result).Plan);
        error = "";
        return true;
    }

    private static string RowSet(string section) =>
        section switch
        {
            LibraryCallUseSections.ConsumerUseSites =>
                GraphLibrariesQuery.ConsumerUseSitesRowSet,
            LibraryCallUseSections.ProviderApiTypes =>
                GraphLibrariesQuery.ProviderApiTypesRowSet,
            LibraryCallUseSections.DirectUseClusters =>
                GraphLibrariesQuery.DirectUseClustersRowSet,
            LibraryCallUseSections.CallSites =>
                GraphLibrariesQuery.CallSitesRowSet,
            LibraryCallUseSections.PublicRootPaths =>
                GraphLibrariesQuery.PublicRootPathsRowSet,
            _ => throw new ArgumentOutOfRangeException(
                nameof(section),
                section,
                "Graph Libraries declares no such section."),
        };

    private static OptionError Error(PortableQueryFailure failure) =>
        failure.Reason switch
        {
            PortableQueryFailureReason.OperatorNotAdmitted =>
                "Field 'Cluster' in graph libraries supports only = predicates.",
            PortableQueryFailureReason.ValueRejected =>
                "Field 'Cluster' requires a positive integer ordinal.",
            PortableQueryFailureReason.DuplicateAfterBinding
                or PortableQueryFailureReason.TermsIncompatible =>
                "graph libraries accepts exactly one --where Cluster=... predicate.",
            PortableQueryFailureReason.UnknownKey =>
                new OptionError(
                    $"Field '{CSharpIdentifier.ContainRenderedText(
                        failure.Offender!)}' is not queryable by graph libraries.",
                    ["Use --where \"Cluster=<positive ordinal>\"."]),
            _ =>
                "The graph libraries query could not be resolved.",
        };
}
