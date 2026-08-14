using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

public record EcosystemIntegrationSignalInfo(
    string Integration,
    string Kind,
    string Name,
    string Shape = IntegrationSignalShape.Type);

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