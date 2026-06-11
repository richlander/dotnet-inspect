using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

public record EcosystemIntegrationSignalInfo(
    string Integration,
    string Kind,
    string Name);

public record EcosystemIntegrationPresence
{
    public bool HasOpenTelemetrySupport { get; init; }
    public bool HasDependencyInjectionSupport { get; init; }
    public bool HasLoggingSupport { get; init; }
    public bool HasOptionsSupport { get; init; }
    public bool HasHostingSupport { get; init; }
    public bool HasHealthChecksSupport { get; init; }
    public bool HasHttpClientSupport { get; init; }
}

public static class EcosystemIntegrationScanner
{
    public static List<EcosystemIntegrationSignalInfo> Scan(PEReader peReader)
    {
        if (!peReader.HasMetadata)
            return [];

        var reader = peReader.GetMetadataReader();
        var buckets = IntegrationBuckets.Create();

        foreach (var handle in reader.TypeReferences)
            AddType(buckets, reader.GetFullTypeName(reader.GetTypeReference(handle)), "TypeRef");

        foreach (var handle in reader.TypeDefinitions)
            AddType(buckets, reader.GetFullTypeName(reader.GetTypeDefinition(handle)), "TypeDef");

        List<EcosystemIntegrationSignalInfo> results = [];
        foreach (var bucket in buckets.All)
            AddRows(results, bucket);
        return results;
    }

    public static EcosystemIntegrationPresence ScanPresence(MetadataReader reader)
    {
        var presence = new MutablePresence
        {
            HasOpenTelemetrySupport = OpenTelemetryScanner.HasSupport(reader)
        };

        foreach (var handle in reader.TypeReferences)
            MarkTypePresence(presence, reader.GetFullTypeName(reader.GetTypeReference(handle)));

        foreach (var handle in reader.TypeDefinitions)
            MarkTypePresence(presence, reader.GetFullTypeName(reader.GetTypeDefinition(handle)));

        return presence.ToImmutable();
    }

    private static void AddType(IntegrationBuckets buckets, string typeName, string source)
    {
        if (IsDependencyInjectionType(typeName))
            AddType(buckets.DependencyInjection, typeName, source);
        if (IsLoggingType(typeName))
            AddType(buckets.Logging, typeName, source);
        if (IsOptionsType(typeName))
            AddType(buckets.Options, typeName, source);
        if (IsHostingType(typeName))
            AddType(buckets.Hosting, typeName, source);
        if (IsHealthChecksType(typeName))
            AddType(buckets.HealthChecks, typeName, source);
        if (IsHttpClientType(typeName))
            AddType(buckets.HttpClient, typeName, source);
    }

    private static void MarkTypePresence(MutablePresence presence, string typeName)
    {
        if (IsDependencyInjectionType(typeName))
            presence.HasDependencyInjectionSupport = true;
        if (IsLoggingType(typeName))
            presence.HasLoggingSupport = true;
        if (IsOptionsType(typeName))
            presence.HasOptionsSupport = true;
        if (IsHostingType(typeName))
            presence.HasHostingSupport = true;
        if (IsHealthChecksType(typeName))
            presence.HasHealthChecksSupport = true;
        if (IsHttpClientType(typeName))
            presence.HasHttpClientSupport = true;
    }

    private static bool IsDependencyInjectionType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.DependencyInjection.", StringComparison.Ordinal);

    private static bool IsLoggingType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Logging.", StringComparison.Ordinal);

    private static bool IsOptionsType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Options.", StringComparison.Ordinal);

    private static bool IsHostingType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Hosting.", StringComparison.Ordinal);

    private static bool IsHealthChecksType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Diagnostics.HealthChecks.", StringComparison.Ordinal);

    private static bool IsHttpClientType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Http.", StringComparison.Ordinal)
           || typeName.Equals("System.Net.Http.IHttpClientFactory", StringComparison.Ordinal)
           || (typeName.StartsWith("Microsoft.Extensions.DependencyInjection.", StringComparison.Ordinal)
               && typeName.Contains("HttpClient", StringComparison.Ordinal));

    private static void AddType(IntegrationBucket bucket, string typeName, string source)
    {
        if (bucket.Types.TryGetValue(typeName, out var existing))
        {
            if (!existing.Split('/').Contains(source, StringComparer.Ordinal))
                bucket.Types[typeName] = $"{existing}/{source}";
            return;
        }

        bucket.Types.Add(typeName, source);
    }

    private static void AddRows(List<EcosystemIntegrationSignalInfo> results, IntegrationBucket bucket)
    {
        foreach (var type in OrderTypes(bucket.Types))
            results.Add(new EcosystemIntegrationSignalInfo(
                bucket.Integration,
                bucket.ApiKind,
                TypeResolver.FormatDisplayName(type)));
    }

    private static IEnumerable<string> OrderTypes(Dictionary<string, string> types)
        => types
            .OrderBy(kv => GetEvidenceRank(kv.Key))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key);

    private static int GetEvidenceRank(string typeName)
        => typeName switch
        {
            "Microsoft.Extensions.DependencyInjection.IServiceCollection" => 0,
            "Microsoft.Extensions.Logging.ILogger" => 0,
            "Microsoft.Extensions.Logging.ILogger`1" => 0,
            "Microsoft.Extensions.Options.IOptions`1" => 0,
            "Microsoft.Extensions.Hosting.IHostedService" => 0,
            "Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck" => 0,
            "System.Net.Http.IHttpClientFactory" => 0,
            "Microsoft.Extensions.DependencyInjection.IHttpClientBuilder" => 1,
            "Microsoft.Extensions.Logging.LoggerMessageAttribute" => 1,
            "Microsoft.Extensions.Options.IOptionsMonitor`1" => 1,
            "Microsoft.Extensions.Hosting.BackgroundService" => 1,
            "Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult" => 1,
            _ => 10,
        };

    private sealed class IntegrationBucket(string integration, string apiKind)
    {
        public string Integration { get; } = integration;
        public string ApiKind { get; } = apiKind;
        public Dictionary<string, string> Types { get; } = new(StringComparer.Ordinal);
    }

    private sealed class IntegrationBuckets
    {
        public required IntegrationBucket DependencyInjection { get; init; }
        public required IntegrationBucket Logging { get; init; }
        public required IntegrationBucket Options { get; init; }
        public required IntegrationBucket Hosting { get; init; }
        public required IntegrationBucket HealthChecks { get; init; }
        public required IntegrationBucket HttpClient { get; init; }

        public IntegrationBucket[] All =>
        [
            DependencyInjection,
            Logging,
            Options,
            Hosting,
            HealthChecks,
            HttpClient
        ];

        public static IntegrationBuckets Create() => new()
        {
            DependencyInjection = new IntegrationBucket("Dependency Injection", "Dependency Injection"),
            Logging = new IntegrationBucket("Logging", "Logging"),
            Options = new IntegrationBucket("Options", "Options"),
            Hosting = new IntegrationBucket("Hosting", "Hosting"),
            HealthChecks = new IntegrationBucket("Health Checks", "Health Check"),
            HttpClient = new IntegrationBucket("HTTP Client", "HTTP Client")
        };
    }

    private sealed class MutablePresence
    {
        public bool HasOpenTelemetrySupport { get; init; }
        public bool HasDependencyInjectionSupport { get; set; }
        public bool HasLoggingSupport { get; set; }
        public bool HasOptionsSupport { get; set; }
        public bool HasHostingSupport { get; set; }
        public bool HasHealthChecksSupport { get; set; }
        public bool HasHttpClientSupport { get; set; }

        public EcosystemIntegrationPresence ToImmutable() => new()
        {
            HasOpenTelemetrySupport = HasOpenTelemetrySupport,
            HasDependencyInjectionSupport = HasDependencyInjectionSupport,
            HasLoggingSupport = HasLoggingSupport,
            HasOptionsSupport = HasOptionsSupport,
            HasHostingSupport = HasHostingSupport,
            HasHealthChecksSupport = HasHealthChecksSupport,
            HasHttpClientSupport = HasHttpClientSupport
        };
    }
}
