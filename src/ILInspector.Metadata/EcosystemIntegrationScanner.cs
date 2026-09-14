using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Structured API evidence retained behind one integration currency row.
/// </summary>
public sealed record EcosystemIntegrationApiEvidence
{
    public EcosystemIntegrationApiEvidence(
        MemberAnchor member,
        MetadataTypeDefinitionName declaringType,
        MetadataNamedTypeReference? receiverType,
        MetadataNamedTypeReference? returnType)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(declaringType);
        Member = member;
        DeclaringType = declaringType;
        ReceiverType = receiverType;
        ReturnType = returnType;
    }

    public MemberAnchor Member { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    public MetadataNamedTypeReference? ReceiverType { get; }
    public MetadataNamedTypeReference? ReturnType { get; }
}

public record EcosystemIntegrationSignalInfo(
    string Integration,
    string Kind,
    string Name,
    string Shape = IntegrationSignalShape.Type)
{
    string _integration = Integration;
    IntegrationConceptDescriptor? _concept = ResolveConcept(Integration);

    public string Integration
    {
        get => _integration;
        init
        {
            _integration = value;
            _concept = ResolveConcept(value);
        }
    }
    public string Kind { get; init; } = Kind;
    public string Name { get; init; } = Name;
    public string Shape { get; init; } = Shape;

    internal EcosystemIntegrationSignalInfo(
        IntegrationConceptDescriptor concept,
        string kind,
        string name,
        string shape = IntegrationSignalShape.Type)
        : this(concept.DisplayLabel, kind, name, shape)
    {
        _concept = concept;
    }

    internal ImmutableArray<EcosystemIntegrationApiEvidence> ApiEvidence
        { get; init; } = [];
    internal bool ApiEvidenceUnavailable { get; init; }
    internal MetadataTypeDefinitionName? TypeDefinition { get; init; }

    public EcosystemIntegrationApiEvidence? GetApiEvidence() =>
        ApiEvidence.IsEmpty ? null : ApiEvidence[0];

    public ImmutableArray<EcosystemIntegrationApiEvidence>
        GetApiEvidenceSet() => ApiEvidence;

    public bool IsApiEvidenceIncomplete() => ApiEvidenceUnavailable;

    public MetadataTypeDefinitionName? GetTypeDefinition() =>
        TypeDefinition;

    public IntegrationConceptDescriptor? GetConcept() => _concept;

    public IntegrationProducerPolicyDescriptor? GetProducerPolicy()
    {
        IntegrationProducerPolicyDescriptor policy =
            IntegrationConceptCatalog.EcosystemObserved;
        return _concept is not null
            && policy.Concepts.Contains(
                _concept,
                ReferenceEqualityComparer.Instance)
                ? policy
                : null;
    }

    static IntegrationConceptDescriptor? ResolveConcept(string? integration) =>
        integration is not null
        && IntegrationConceptCatalog.TryGetByDisplayLabel(
            integration,
            out IntegrationConceptDescriptor? concept)
                ? concept
                : null;

    // Preserve the original four-field signal contract. Structured evidence is
    // derived from the same metadata and intentionally does not affect row
    // equality.
    public virtual bool Equals(EcosystemIntegrationSignalInfo? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && EqualityContract == other.EqualityContract
        && string.Equals(
            Integration,
            other.Integration,
            StringComparison.Ordinal)
        && string.Equals(Kind, other.Kind, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(Shape, other.Shape, StringComparison.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(
            EqualityContract,
            Integration,
            Kind,
            Name,
            Shape);
}

public static class IntegrationSignalShape
{
    public const string Type = "Type";
    public const string Api = "API";
}

public record EcosystemIntegrationPresence
{
    public int IntegrationCount { get; init; }
    public bool HasAspNetCoreSupport { get; init; }
    public bool HasAspireSupport { get; init; }
    public bool HasAISupport { get; init; }
    public bool HasAuthenticationSupport { get; init; }
    public bool HasConfigurationSupport { get; init; }
    public bool HasOpenTelemetrySupport { get; init; }
    public bool HasDependencyInjectionSupport { get; init; }
    public bool HasLoggingSupport { get; init; }
    public bool HasOptionsSupport { get; init; }
    public bool HasHostingSupport { get; init; }
    public bool HasHealthChecksSupport { get; init; }
    public bool HasHttpClientSupport { get; init; }
    public bool HasOpenApiSupport { get; init; }
}

/// <summary>
/// Public validation and source-image facade for ecosystem integration inspection.
/// Metadata traversal, classification policy, ordered projection, and presence
/// aggregation are owned by focused internal collaborators.
/// </summary>
public static class EcosystemIntegrationScanner
{
    /// <summary>Existing Aspire interpretation for staged application-catalog adoption.</summary>
    public static EcosystemIntegrationScannerBinding AspireBinding { get; } =
        EcosystemIntegrationScannerBinding.Create(EcosystemIntegrationClassifier.ClassifyAspire);

    /// <summary>
    /// Runs one selected interpretation over owner-produced observations.
    /// The returned rows do not constitute full-library presence or a Census.
    /// </summary>
    public static List<EcosystemIntegrationSignalInfo> Scan(
        EcosystemIntegrationObservationContext context,
        EcosystemIntegrationScannerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(binding);
        return EcosystemIntegrationProjection.Project(binding.Scan(context));
    }

    public static List<EcosystemIntegrationSignalInfo> Scan(PEReader peReader)
    {
        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return [];

        return EcosystemIntegrationProjection.Scan(MetadataFormatAdmission.GetMetadataReader(peReader));
    }

    public static EcosystemIntegrationPresence ScanPresence(MetadataReader reader)
        => EcosystemIntegrationPresenceBuilder.Scan(reader);

    public static EcosystemIntegrationPresence SummarizePresence(
        PEReader peReader,
        IEnumerable<EcosystemIntegrationSignalInfo> signals,
        bool hasOpenTelemetrySupport)
        => EcosystemIntegrationPresenceBuilder.Summarize(
            peReader,
            signals,
            hasOpenTelemetrySupport);
}