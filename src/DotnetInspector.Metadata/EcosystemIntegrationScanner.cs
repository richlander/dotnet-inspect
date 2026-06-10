using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

public record EcosystemIntegrationSignalInfo(
    string Integration,
    string Area,
    string Signal,
    string Value,
    string Evidence);

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
    private const int EvidenceLimit = 6;

    public static List<EcosystemIntegrationSignalInfo> Scan(PEReader peReader)
    {
        if (!peReader.HasMetadata)
            return [];

        var reader = peReader.GetMetadataReader();
        var buckets = IntegrationBuckets.Create();

        foreach (var handle in reader.AssemblyReferences)
        {
            var assemblyReference = reader.GetAssemblyReference(handle);
            AddAssemblyReference(buckets, reader.GetString(assemblyReference.Name));
        }

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

        foreach (var handle in reader.AssemblyReferences)
        {
            var assemblyReference = reader.GetAssemblyReference(handle);
            MarkAssemblyPresence(presence, reader.GetString(assemblyReference.Name));
        }

        foreach (var handle in reader.TypeReferences)
            MarkTypePresence(presence, reader.GetFullTypeName(reader.GetTypeReference(handle)));

        foreach (var handle in reader.TypeDefinitions)
            MarkTypePresence(presence, reader.GetFullTypeName(reader.GetTypeDefinition(handle)));

        return presence.ToImmutable();
    }

    private static void AddAssemblyReference(IntegrationBuckets buckets, string assemblyName)
    {
        if (IsDependencyInjectionAssembly(assemblyName))
            buckets.DependencyInjection.AssemblyReferences.Add(assemblyName);
        if (IsLoggingAssembly(assemblyName))
            buckets.Logging.AssemblyReferences.Add(assemblyName);
        if (IsOptionsAssembly(assemblyName))
            buckets.Options.AssemblyReferences.Add(assemblyName);
        if (IsHostingAssembly(assemblyName))
            buckets.Hosting.AssemblyReferences.Add(assemblyName);
        if (IsHealthChecksAssembly(assemblyName))
            buckets.HealthChecks.AssemblyReferences.Add(assemblyName);
        if (IsHttpClientAssembly(assemblyName))
            buckets.HttpClient.AssemblyReferences.Add(assemblyName);
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

    private static void MarkAssemblyPresence(MutablePresence presence, string assemblyName)
    {
        if (IsDependencyInjectionAssembly(assemblyName))
            presence.HasDependencyInjectionSupport = true;
        if (IsLoggingAssembly(assemblyName))
            presence.HasLoggingSupport = true;
        if (IsOptionsAssembly(assemblyName))
            presence.HasOptionsSupport = true;
        if (IsHostingAssembly(assemblyName))
            presence.HasHostingSupport = true;
        if (IsHealthChecksAssembly(assemblyName))
            presence.HasHealthChecksSupport = true;
        if (IsHttpClientAssembly(assemblyName))
            presence.HasHttpClientSupport = true;
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

    private static bool IsDependencyInjectionAssembly(string name)
        => IsAssemblyPrefix(name, "Microsoft.Extensions.DependencyInjection");

    private static bool IsLoggingAssembly(string name)
        => IsAssemblyPrefix(name, "Microsoft.Extensions.Logging");

    private static bool IsOptionsAssembly(string name)
        => IsAssemblyPrefix(name, "Microsoft.Extensions.Options");

    private static bool IsHostingAssembly(string name)
        => IsAssemblyPrefix(name, "Microsoft.Extensions.Hosting");

    private static bool IsHealthChecksAssembly(string name)
        => IsAssemblyPrefix(name, "Microsoft.Extensions.Diagnostics.HealthChecks");

    private static bool IsHttpClientAssembly(string name)
        => IsAssemblyPrefix(name, "Microsoft.Extensions.Http");

    private static bool IsAssemblyPrefix(string name, string prefix)
        => name.Equals(prefix, StringComparison.OrdinalIgnoreCase)
           || name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);

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
        if (bucket.AssemblyReferences.Count > 0)
        {
            results.Add(new EcosystemIntegrationSignalInfo(
                bucket.Integration,
                "Dependencies",
                "Assembly references",
                bucket.AssemblyReferences.Count.ToString(),
                $"AssemblyRef: {FormatEvidence(bucket.AssemblyReferences)}"));
        }

        if (bucket.Types.Count > 0)
        {
            results.Add(new EcosystemIntegrationSignalInfo(
                bucket.Integration,
                bucket.Integration,
                bucket.ApiSignal,
                bucket.Types.Count.ToString(),
                FormatTypeEvidence(bucket.Types)));
        }
    }

    private static string FormatTypeEvidence(Dictionary<string, string> types)
        => FormatEvidence(types
            .OrderBy(kv => GetEvidenceRank(kv.Key))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Value}: {kv.Key}"));

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

    private static string FormatEvidence(IEnumerable<string> values)
    {
        var ordered = values.ToArray();
        var visible = ordered.Take(EvidenceLimit).ToArray();
        var suffix = ordered.Length > EvidenceLimit ? $" (+{ordered.Length - EvidenceLimit} more)" : "";
        return string.Join(", ", visible) + suffix;
    }

    private sealed class IntegrationBucket(string integration, string apiSignal)
    {
        public string Integration { get; } = integration;
        public string ApiSignal { get; } = apiSignal;
        public SortedSet<string> AssemblyReferences { get; } = new(StringComparer.OrdinalIgnoreCase);
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
            DependencyInjection = new IntegrationBucket("Dependency Injection", "Dependency Injection APIs"),
            Logging = new IntegrationBucket("Logging", "Logging APIs"),
            Options = new IntegrationBucket("Options", "Options APIs"),
            Hosting = new IntegrationBucket("Hosting", "Hosting APIs"),
            HealthChecks = new IntegrationBucket("Health Checks", "Health Check APIs"),
            HttpClient = new IntegrationBucket("HTTP Client", "HTTP Client APIs")
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
