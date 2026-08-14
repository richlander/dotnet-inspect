using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Aggregates legacy assembly-level integration presence from projected signals,
/// broad public-type evidence, and the OpenTelemetry support predicate.
/// </summary>
internal static class EcosystemIntegrationPresenceBuilder
{
    internal static EcosystemIntegrationPresence Scan(MetadataReader reader)
    {
        var openTelemetrySupport = OpenTelemetryScanner.HasSupport(reader);
        var presence = new MutablePresence
        {
            HasOpenTelemetrySupport = openTelemetrySupport
        };

        var signals = EcosystemIntegrationProjection.Scan(reader);
        foreach (var integration in signals.Select(signal => signal.Integration).Distinct(StringComparer.Ordinal))
            MarkIntegrationPresence(presence, integration);

        presence.IntegrationCount = signals
            .Select(signal => signal.Integration)
            .Distinct(StringComparer.Ordinal)
            .Count()
            + (openTelemetrySupport ? 1 : 0);

        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDefinition = reader.GetTypeDefinition(handle);
            if (!typeDefinition.IsPublic)
                continue;

            MarkTypePresence(presence, reader.GetFullTypeName(typeDefinition));
        }

        return presence.ToImmutable();
    }

    internal static EcosystemIntegrationPresence Summarize(
        PEReader peReader,
        IEnumerable<EcosystemIntegrationSignalInfo> signals,
        bool hasOpenTelemetrySupport)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentNullException.ThrowIfNull(signals);

        string[] integrations =
        [
            .. signals
                .Select(static signal => signal.Integration)
                .Distinct(StringComparer.Ordinal),
        ];
        var presence = new MutablePresence
        {
            HasOpenTelemetrySupport = hasOpenTelemetrySupport
        };
        foreach (string integration in integrations)
            MarkIntegrationPresence(presence, integration);

        presence.IntegrationCount =
            integrations.Length
            + (hasOpenTelemetrySupport ? 1 : 0);

        if (peReader.HasMetadata)
        {
            MetadataReader reader = peReader.GetMetadataReader();
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition typeDefinition =
                    reader.GetTypeDefinition(handle);
                if (!typeDefinition.IsPublic)
                    continue;

                MarkTypePresence(
                    presence,
                    reader.GetFullTypeName(typeDefinition));
            }
        }

        return presence.ToImmutable();
    }

    private static void MarkIntegrationPresence(MutablePresence presence, string integration)
    {
        switch (integration)
        {
            case EcosystemIntegrationNames.AI:
                presence.HasAISupport = true;
                break;
            case EcosystemIntegrationNames.AspNetCore:
                presence.HasAspNetCoreSupport = true;
                break;
            case EcosystemIntegrationNames.Aspire:
                presence.HasAspireSupport = true;
                break;
            case EcosystemIntegrationNames.Authentication:
                presence.HasAuthenticationSupport = true;
                break;
            case EcosystemIntegrationNames.Configuration:
                presence.HasConfigurationSupport = true;
                break;
            case EcosystemIntegrationNames.DependencyInjection:
                presence.HasDependencyInjectionSupport = true;
                break;
            case EcosystemIntegrationNames.Logging:
                presence.HasLoggingSupport = true;
                break;
            case EcosystemIntegrationNames.OpenAPI:
                presence.HasOpenApiSupport = true;
                break;
            case EcosystemIntegrationNames.Options:
                presence.HasOptionsSupport = true;
                break;
            case EcosystemIntegrationNames.Hosting:
                presence.HasHostingSupport = true;
                break;
            case EcosystemIntegrationNames.HealthChecks:
                presence.HasHealthChecksSupport = true;
                break;
            case EcosystemIntegrationNames.HttpClient:
                presence.HasHttpClientSupport = true;
                break;
        }
    }

    private static void MarkTypePresence(MutablePresence presence, string typeName)
    {
        if (EcosystemIntegrationClassifier.IsAspNetCoreType(typeName))
            presence.HasAspNetCoreSupport = true;
        if (EcosystemIntegrationClassifier.IsAspireType(typeName))
            presence.HasAspireSupport = true;
        if (EcosystemIntegrationClassifier.IsAIType(typeName))
            presence.HasAISupport = true;
        if (EcosystemIntegrationClassifier.IsAuthenticationType(typeName))
            presence.HasAuthenticationSupport = true;
        if (EcosystemIntegrationClassifier.TryGetConfigurationKind(typeName, out _))
            presence.HasConfigurationSupport = true;
        if (EcosystemIntegrationClassifier.IsDependencyInjectionType(typeName))
            presence.HasDependencyInjectionSupport = true;
        if (EcosystemIntegrationClassifier.IsLoggingType(typeName))
            presence.HasLoggingSupport = true;
        if (EcosystemIntegrationClassifier.IsOptionsType(typeName))
            presence.HasOptionsSupport = true;
        if (EcosystemIntegrationClassifier.IsHostingType(typeName))
            presence.HasHostingSupport = true;
        if (EcosystemIntegrationClassifier.IsHealthChecksType(typeName))
            presence.HasHealthChecksSupport = true;
        if (EcosystemIntegrationClassifier.IsHttpClientType(typeName))
            presence.HasHttpClientSupport = true;
        if (EcosystemIntegrationClassifier.IsOpenApiType(typeName))
            presence.HasOpenApiSupport = true;
    }

    private sealed class MutablePresence
    {
        public int IntegrationCount { get; set; }
        public bool HasAspNetCoreSupport { get; set; }
        public bool HasAspireSupport { get; set; }
        public bool HasAISupport { get; set; }
        public bool HasAuthenticationSupport { get; set; }
        public bool HasConfigurationSupport { get; set; }
        public bool HasOpenTelemetrySupport { get; init; }
        public bool HasDependencyInjectionSupport { get; set; }
        public bool HasLoggingSupport { get; set; }
        public bool HasOptionsSupport { get; set; }
        public bool HasHostingSupport { get; set; }
        public bool HasHealthChecksSupport { get; set; }
        public bool HasHttpClientSupport { get; set; }
        public bool HasOpenApiSupport { get; set; }

        public EcosystemIntegrationPresence ToImmutable() => new()
        {
            IntegrationCount = IntegrationCount,
            HasAspNetCoreSupport = HasAspNetCoreSupport,
            HasAspireSupport = HasAspireSupport,
            HasAISupport = HasAISupport,
            HasAuthenticationSupport = HasAuthenticationSupport,
            HasConfigurationSupport = HasConfigurationSupport,
            HasOpenTelemetrySupport = HasOpenTelemetrySupport,
            HasDependencyInjectionSupport = HasDependencyInjectionSupport,
            HasLoggingSupport = HasLoggingSupport,
            HasOptionsSupport = HasOptionsSupport,
            HasHostingSupport = HasHostingSupport,
            HasHealthChecksSupport = HasHealthChecksSupport,
            HasHttpClientSupport = HasHttpClientSupport,
            HasOpenApiSupport = HasOpenApiSupport
        };
    }
}
