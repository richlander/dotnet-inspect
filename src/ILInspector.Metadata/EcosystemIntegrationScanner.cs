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
    internal EcosystemIntegrationApiEvidence? ApiEvidence { get; init; }
    internal MetadataTypeDefinitionName? TypeDefinition { get; init; }

    public EcosystemIntegrationApiEvidence? GetApiEvidence() =>
        ApiEvidence;

    public MetadataTypeDefinitionName? GetTypeDefinition() =>
        TypeDefinition;

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
    public static List<EcosystemIntegrationSignalInfo> Scan(PEReader peReader)
    {
        if (!peReader.HasMetadata)
            return [];

        return EcosystemIntegrationProjection.Scan(peReader.GetMetadataReader());
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