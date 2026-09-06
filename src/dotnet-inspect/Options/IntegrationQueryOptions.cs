using System.Collections.Immutable;
using DotnetInspector.Ecosystems;
using DotnetInspector.Models;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Options;

public sealed record IntegrationQueryOptions
{
    private readonly ImmutableArray<IntegrationConceptDescriptor> _concepts;

    private IntegrationQueryOptions(
        EcosystemPackId? ecosystem,
        ImmutableArray<IntegrationConceptDescriptor> concepts)
    {
        Ecosystem = ecosystem;
        _concepts = concepts;
    }

    public static IntegrationQueryOptions Default { get; } = new(null, []);

    public static SectionQueryFacet QueryFacet { get; } = new(
        "ecosystem",
        ["--where"],
        ["="],
        "canonical ecosystem ID (exactly one predicate)",
        [.. LibraryIntegrationCatalog.All
            .Select(descriptor => descriptor.Ecosystem?.Value)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)],
        "--where \"ecosystem=ecosystem.aspire\"");

    public EcosystemPackId? Ecosystem { get; }
    public bool HasFilter => Ecosystem is not null;

    internal bool Matches(IntegrationConceptDescriptor? concept)
        => !HasFilter || _concepts.Any(candidate => ReferenceEquals(candidate, concept));

    public static bool TryExtract(
        IReadOnlyList<string> expressions,
        out IntegrationQueryOptions options,
        out string[] remaining,
        out OptionError error)
    {
        options = Default;
        remaining = [];
        List<string> unclaimed = [];
        foreach (string expression in expressions)
        {
            if (!RowPredicateSyntaxParser.TryParse(expression, out var syntax, out error))
                return false;
            if (!RowPredicateSyntaxParser.NormalizeFieldName(syntax.Field).Equals(
                    "ecosystem", StringComparison.OrdinalIgnoreCase))
            {
                unclaimed.Add(expression);
                continue;
            }
            if (syntax.Operator != RowPredicateOperator.Equals)
            {
                error = "Field 'ecosystem' in Integrations supports only = predicates.";
                return false;
            }
            if (options.HasFilter)
            {
                error = "Integrations accepts exactly one --where ecosystem=... predicate.";
                return false;
            }
            if (!EcosystemPackId.TryCreate(syntax.Value, out var id))
            {
                error = "Field 'ecosystem' requires a canonical ecosystem ID, such as ecosystem.aspire.";
                return false;
            }
            if (EcosystemPackCatalog.Lookup(id) is EcosystemPackLookupResult.Unknown)
            {
                error = $"Unknown ecosystem '{id}'.";
                return false;
            }
            ImmutableArray<IntegrationConceptDescriptor> concepts =
                [.. LibraryIntegrationCatalog.All
                    .Where(descriptor => descriptor.Ecosystem == id)
                    .Select(descriptor => descriptor.Concept)];
            if (concepts.IsEmpty)
            {
                error = new OptionError(
                    $"Ecosystem '{id}' has no CLI Integration query binding.",
                    ["Run 'library -Q Integrations' to discover supported values."]);
                return false;
            }
            options = new(id, concepts);
        }
        remaining = [.. unclaimed];
        error = "";
        return true;
    }
}
