using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Options;

/// <summary>
/// Candidate-population controls for the Clone Candidates section.
/// </summary>
public sealed record CloneCandidateQueryOptions
{
    public static CloneCandidateQueryOptions Default { get; } = new();

    public static SectionQueryFacet BreadthFacet { get; } = new(
        "Breadth",
        ["--where"],
        ["="],
        "candidate library population",
        [.. Enum.GetNames<StructuralCloneCandidateBreadth>()],
        "--where \"Breadth=Everything\"");

    public static SectionQueryFacet DiscoveryFacet { get; } = new(
        "Discovery",
        ["--where"],
        ["="],
        "candidate method admission",
        [.. Enum.GetNames<StructuralCloneCandidateDiscovery>()],
        "--where \"Discovery=SimilarNames\"");

    public static IReadOnlyList<SectionQueryFacet> QueryFacets { get; } =
        [BreadthFacet, DiscoveryFacet];

    public StructuralCloneCandidateBreadth Breadth { get; init; } =
        StructuralCloneCandidateBreadth.Everything;

    public StructuralCloneCandidateDiscovery Discovery { get; init; } =
        StructuralCloneCandidateDiscovery.SimilarNames;

    public bool BreadthSpecified { get; init; }
    public bool DiscoverySpecified { get; init; }
    public bool HasPredicates => BreadthSpecified || DiscoverySpecified;

    public static bool TryExtract(
        IReadOnlyList<string> expressions,
        out CloneCandidateQueryOptions options,
        out string[] remaining,
        out OptionError error)
    {
        options = Default;
        List<string> unclaimed = [];

        foreach (string expression in expressions)
        {
            if (!RowPredicateSyntaxParser.TryParse(
                    expression,
                    out RowPredicateSyntax syntax,
                    out error))
            {
                remaining = [];
                return false;
            }

            string field =
                RowPredicateSyntaxParser.NormalizeFieldName(syntax.Field);
            if (field.Equals("Breadth", StringComparison.OrdinalIgnoreCase))
            {
                if (syntax.Operator != RowPredicateOperator.Equals)
                {
                    remaining = [];
                    error =
                        "Field 'Breadth' in section 'Clone Candidates' supports only = predicates.";
                    return false;
                }
                if (options.BreadthSpecified)
                {
                    remaining = [];
                    error =
                        "Section 'Clone Candidates' accepts at most one --where Breadth=... predicate.";
                    return false;
                }
                if (!Enum.TryParse(
                        syntax.Value,
                        ignoreCase: true,
                        out StructuralCloneCandidateBreadth breadth)
                    || !Enum.IsDefined(breadth))
                {
                    remaining = [];
                    error = UnknownValue(
                        "Breadth",
                        syntax.Value,
                        Enum.GetNames<StructuralCloneCandidateBreadth>());
                    return false;
                }

                options = options with
                {
                    Breadth = breadth,
                    BreadthSpecified = true,
                };
                continue;
            }

            if (field.Equals(
                    "Discovery",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (syntax.Operator != RowPredicateOperator.Equals)
                {
                    remaining = [];
                    error =
                        "Field 'Discovery' in section 'Clone Candidates' supports only = predicates.";
                    return false;
                }
                if (options.DiscoverySpecified)
                {
                    remaining = [];
                    error =
                        "Section 'Clone Candidates' accepts at most one --where Discovery=... predicate.";
                    return false;
                }
                if (!Enum.TryParse(
                        syntax.Value,
                        ignoreCase: true,
                        out StructuralCloneCandidateDiscovery discovery)
                    || !Enum.IsDefined(discovery))
                {
                    remaining = [];
                    error = UnknownValue(
                        "Discovery",
                        syntax.Value,
                        Enum.GetNames<StructuralCloneCandidateDiscovery>());
                    return false;
                }

                options = options with
                {
                    Discovery = discovery,
                    DiscoverySpecified = true,
                };
                continue;
            }

            unclaimed.Add(expression);
        }

        remaining = [.. unclaimed];
        error = "";
        return true;
    }

    static OptionError UnknownValue(
        string field,
        string value,
        IReadOnlyList<string> values) =>
        new(
            $"Unknown Clone Candidates {field} value '{value}'.",
            [$"Supported values: {string.Join(", ", values)}."]);
}
