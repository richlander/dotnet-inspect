using System.Collections.Immutable;

namespace ILInspector.Metadata;

/// <summary>A stable owner-issued identifier for one Integration concept.</summary>
public sealed record IntegrationConceptId
{
    internal IntegrationConceptId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>A stable owner-issued identifier for one Integration producer policy.</summary>
public sealed record IntegrationProducerPolicyId
{
    internal IntegrationProducerPolicyId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// One producer-policy declaration retained by a catalog revision without
/// exposing the descriptor graph to serialization.
/// </summary>
public sealed class IntegrationProducerPolicyRevision
{
    internal IntegrationProducerPolicyRevision(
        IntegrationProducerPolicyDescriptor policy)
    {
        Id = policy.Id;
        RelationshipId = policy.RelationshipId;
        ConceptIds = [.. policy.Concepts.Select(concept => concept.Id)];
    }

    public IntegrationProducerPolicyId Id { get; }
    public string RelationshipId { get; }
    public ImmutableArray<IntegrationConceptId> ConceptIds { get; }
}

/// <summary>
/// The stable revision and declaration shape of one configured Integration
/// catalog.
/// </summary>
public sealed class IntegrationConceptCatalogRevision
{
    internal IntegrationConceptCatalogRevision(
        int number,
        IEnumerable<IntegrationConceptDescriptor> concepts,
        IEnumerable<IntegrationProducerPolicyDescriptor> producerPolicies)
    {
        if (number <= 0)
            throw new ArgumentOutOfRangeException(nameof(number));

        Number = number;
        ConceptIds = [.. concepts.Select(concept => concept.Id)];
        ProducerPolicies =
        [
            .. producerPolicies.Select(
                policy => new IntegrationProducerPolicyRevision(policy)),
        ];
    }

    public int Number { get; }
    public ImmutableArray<IntegrationConceptId> ConceptIds { get; }
    public ImmutableArray<IntegrationProducerPolicyRevision> ProducerPolicies
        { get; }
}

/// <summary>One configured Integration concept in the current product build.</summary>
public sealed class IntegrationConceptDescriptor
{
    ImmutableArray<IntegrationProducerPolicyDescriptor> _producerPolicies;

    internal IntegrationConceptDescriptor(
        IntegrationConceptId id,
        string displayLabel)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLabel);
        Id = id;
        DisplayLabel = displayLabel;
    }

    public IntegrationConceptId Id { get; }
    public string DisplayLabel { get; }
    public ImmutableArray<IntegrationProducerPolicyDescriptor> ProducerPolicies =>
        _producerPolicies;

    internal void BindProducerPolicies(
        IEnumerable<IntegrationProducerPolicyDescriptor> producerPolicies)
    {
        if (!_producerPolicies.IsDefault)
            throw new InvalidOperationException("Producer policies are already bound.");

        _producerPolicies = [.. producerPolicies];
        if (_producerPolicies.IsEmpty)
            throw new ArgumentException(
                "Every Integration concept requires a producer policy.",
                nameof(producerPolicies));
        if (_producerPolicies.Any(policy => policy is null))
        {
            throw new ArgumentException(
                "Producer policies cannot contain null.",
                nameof(producerPolicies));
        }
    }

    public override string ToString() => DisplayLabel;
}

/// <summary>
/// One configured policy that may issue structured evidence for Integration
/// concepts.
/// </summary>
public sealed class IntegrationProducerPolicyDescriptor
{
    internal IntegrationProducerPolicyDescriptor(
        IntegrationProducerPolicyId id,
        string relationshipId,
        IEnumerable<IntegrationConceptDescriptor> concepts)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipId);
        ArgumentNullException.ThrowIfNull(concepts);
        Id = id;
        RelationshipId = relationshipId;
        Concepts = [.. concepts];
        if (Concepts.IsEmpty)
            throw new ArgumentException(
                "A producer policy must name at least one concept.",
                nameof(concepts));
        if (Concepts.Any(concept => concept is null))
        {
            throw new ArgumentException(
                "Concepts cannot contain null.",
                nameof(concepts));
        }
        if (Concepts.Distinct(ReferenceEqualityComparer.Instance).Count()
            != Concepts.Length)
        {
            throw new ArgumentException(
                "Concept descriptors must be unique.",
                nameof(concepts));
        }
    }

    public IntegrationProducerPolicyId Id { get; }
    public string RelationshipId { get; }
    public ImmutableArray<IntegrationConceptDescriptor> Concepts { get; }

    public override string ToString() => Id.Value;
}

/// <summary>
/// The finite Integration concept and producer-policy catalog configured in
/// this product build.
/// </summary>
public static class IntegrationConceptCatalog
{
    static readonly Dictionary<string, IntegrationConceptDescriptor> ByLabel;

    static IntegrationConceptCatalog()
    {
        AI = Concept("integration.ai", EcosystemIntegrationNames.AI);
        AspNetCore = Concept(
            "integration.aspnetcore",
            EcosystemIntegrationNames.AspNetCore);
        Aspire = Concept(
            "integration.aspire",
            EcosystemIntegrationNames.Aspire);
        Authentication = Concept(
            "integration.authentication",
            EcosystemIntegrationNames.Authentication);
        Configuration = Concept(
            "integration.configuration",
            EcosystemIntegrationNames.Configuration);
        DependencyInjection = Concept(
            "integration.dependency-injection",
            EcosystemIntegrationNames.DependencyInjection);
        HealthChecks = Concept(
            "integration.health-checks",
            EcosystemIntegrationNames.HealthChecks);
        Hosting = Concept(
            "integration.hosting",
            EcosystemIntegrationNames.Hosting);
        HttpClient = Concept(
            "integration.http-client",
            EcosystemIntegrationNames.HttpClient);
        Logging = Concept(
            "integration.logging",
            EcosystemIntegrationNames.Logging);
        OpenAPI = Concept(
            "integration.openapi",
            EcosystemIntegrationNames.OpenAPI);
        OpenTelemetry = Concept(
            "integration.opentelemetry",
            EcosystemIntegrationNames.OpenTelemetry);
        Options = Concept(
            "integration.options",
            EcosystemIntegrationNames.Options);

        Concepts =
        [
            AI,
            AspNetCore,
            Authentication,
            Configuration,
            Aspire,
            DependencyInjection,
            Logging,
            OpenTelemetry,
            OpenAPI,
            Options,
            Hosting,
            HealthChecks,
            HttpClient,
        ];

        EcosystemObserved = Policy(
            "producer.integration.ecosystem-observed",
            "integration.observed",
            Concepts.Where(concept =>
                !ReferenceEquals(concept, OpenTelemetry)));
        OpenTelemetryObserved = Policy(
            "producer.integration.opentelemetry-observed",
            "integration.observed",
            [OpenTelemetry]);
        Opportunity = Policy(
            "producer.integration.opportunity",
            "integration.opportunity",
            [
                AI,
                Aspire,
                Authentication,
                Configuration,
                DependencyInjection,
                HealthChecks,
            ]);
        ProducerPolicies =
        [
            EcosystemObserved,
            OpenTelemetryObserved,
            Opportunity,
        ];

        foreach (IntegrationConceptDescriptor concept in Concepts)
        {
            concept.BindProducerPolicies(
                ProducerPolicies.Where(policy =>
                    policy.Concepts.Contains(
                        concept,
                        ReferenceEqualityComparer.Instance)));
        }

        Revision = new IntegrationConceptCatalogRevision(
            1,
            Concepts,
            ProducerPolicies);
        ByLabel = Concepts.ToDictionary(
            concept => concept.DisplayLabel,
            StringComparer.Ordinal);
    }

    public static IntegrationConceptDescriptor AI { get; }
    public static IntegrationConceptDescriptor AspNetCore { get; }
    public static IntegrationConceptDescriptor Aspire { get; }
    public static IntegrationConceptDescriptor Authentication { get; }
    public static IntegrationConceptDescriptor Configuration { get; }
    public static IntegrationConceptDescriptor DependencyInjection { get; }
    public static IntegrationConceptDescriptor HealthChecks { get; }
    public static IntegrationConceptDescriptor Hosting { get; }
    public static IntegrationConceptDescriptor HttpClient { get; }
    public static IntegrationConceptDescriptor Logging { get; }
    public static IntegrationConceptDescriptor OpenAPI { get; }
    public static IntegrationConceptDescriptor OpenTelemetry { get; }
    public static IntegrationConceptDescriptor Options { get; }

    public static IntegrationProducerPolicyDescriptor EcosystemObserved { get; }
    public static IntegrationProducerPolicyDescriptor OpenTelemetryObserved { get; }
    public static IntegrationProducerPolicyDescriptor Opportunity { get; }

    public static ImmutableArray<IntegrationConceptDescriptor> Concepts { get; }
    public static ImmutableArray<IntegrationProducerPolicyDescriptor>
        ProducerPolicies { get; }
    public static IntegrationConceptCatalogRevision Revision { get; }

    public static bool TryGetByDisplayLabel(
        string displayLabel,
        out IntegrationConceptDescriptor? concept)
    {
        ArgumentNullException.ThrowIfNull(displayLabel);
        return ByLabel.TryGetValue(displayLabel, out concept);
    }

    static IntegrationConceptDescriptor Concept(
        string id,
        string displayLabel) =>
        new(new IntegrationConceptId(id), displayLabel);

    static IntegrationProducerPolicyDescriptor Policy(
        string id,
        string relationshipId,
        IEnumerable<IntegrationConceptDescriptor> concepts) =>
        new(
            new IntegrationProducerPolicyId(id),
            relationshipId,
            concepts);
}
