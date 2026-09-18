using System.Collections.Immutable;
using DotnetInspector.Ecosystems;
using DotnetInspect.Cli.Models;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Options;

public sealed record IntegrationQueryOptions
{
    private readonly ImmutableArray<IntegrationConceptDescriptor>
        _ecosystemConcepts;

    private IntegrationQueryOptions(
        EcosystemPackId? ecosystem,
        IntegrationConceptDescriptor? integration,
        ImmutableArray<IntegrationConceptDescriptor> ecosystemConcepts)
    {
        Ecosystem = ecosystem;
        Integration = integration;
        _ecosystemConcepts = ecosystemConcepts;
    }

    public static IntegrationQueryOptions Default { get; } =
        new(null, null, []);

    public static SectionQueryKey EcosystemQueryKey { get; } = new(
        "ecosystem",
        ["--where"],
        ["="],
        "canonical ecosystem ID (exactly one predicate)",
        [.. LibraryIntegrationCatalog.EcosystemBindings
            .Select(binding => binding.Ecosystem.Value)],
        "--where \"ecosystem=ecosystem.aspire\"");

    public static SectionQueryKey IntegrationQueryKey { get; } = new(
        "integration",
        ["--where"],
        ["="],
        "canonical Integration concept ID (exactly one predicate)",
        [.. LibraryIntegrationCatalog.All.Select(
            descriptor => descriptor.Concept.Id.Value)],
        "--where \"integration=integration.aspire\"");

    public static ImmutableArray<SectionQueryKey> QueryKeys { get; } =
        [IntegrationQueryKey, EcosystemQueryKey];

    public EcosystemPackId? Ecosystem { get; }
    public IntegrationConceptDescriptor? Integration { get; }
    public bool HasFilter => Ecosystem is not null || Integration is not null;

    internal bool Matches(IntegrationConceptDescriptor? concept)
        => (Integration is null || ReferenceEquals(Integration, concept))
           && (Ecosystem is null
               || _ecosystemConcepts.Any(
                   candidate => ReferenceEquals(candidate, concept)));

    public static bool TryExtract(
        IReadOnlyList<string> expressions,
        out IntegrationQueryOptions options,
        out string[] remaining,
        out OptionError error)
    {
        EcosystemPackId? ecosystem = null;
        IntegrationConceptDescriptor? integration = null;
        ImmutableArray<IntegrationConceptDescriptor> ecosystemConcepts = [];
        options = Default;
        remaining = [];
        List<string> unclaimed = [];
        foreach (string expression in expressions)
        {
            if (!RowPredicateSyntaxParser.TryParse(expression, out var syntax, out error))
                return false;
            string field =
                RowPredicateSyntaxParser.NormalizeFieldName(syntax.Field);
            if (!field.Equals("ecosystem", StringComparison.OrdinalIgnoreCase)
                && !field.Equals(
                    "integration",
                    StringComparison.OrdinalIgnoreCase))
            {
                unclaimed.Add(expression);
                continue;
            }
            if (syntax.Operator != RowPredicateOperator.Equals)
            {
                error =
                    $"Field '{field}' in Integrations supports only = predicates.";
                return false;
            }

            if (field.Equals("integration", StringComparison.OrdinalIgnoreCase))
            {
                if (integration is not null)
                {
                    error =
                        "Integrations accepts exactly one --where integration=... predicate.";
                    return false;
                }
                if (!syntax.Value.StartsWith(
                        "integration.",
                        StringComparison.Ordinal))
                {
                    error =
                        "Field 'integration' requires a canonical Integration concept ID, such as integration.aspire.";
                    return false;
                }
                integration = LibraryIntegrationCatalog.All
                    .Select(descriptor => descriptor.Concept)
                    .FirstOrDefault(concept => concept.Id.Value.Equals(
                        syntax.Value,
                        StringComparison.Ordinal));
                if (integration is null)
                {
                    error = $"Unknown Integration concept '{syntax.Value}'.";
                    return false;
                }
                continue;
            }

            if (ecosystem is not null)
            {
                error =
                    "Integrations accepts exactly one --where ecosystem=... predicate.";
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
            ecosystemConcepts = LibraryIntegrationCatalog.ConceptsFor(id);
            if (ecosystemConcepts.IsEmpty)
            {
                error = new OptionError(
                    $"Ecosystem '{id}' has no CLI Integration query binding.",
                    ["Run 'library -Q Integrations' to discover supported values."]);
                return false;
            }
            ecosystem = id;
        }
        options = new(ecosystem, integration, ecosystemConcepts);
        remaining = [.. unclaimed];
        error = "";
        return true;
    }
}
