using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Projects classifications with their original evidence into the ordered signal
/// rows consumed by queries and Findings.
/// </summary>
internal static class EcosystemIntegrationProjection
{
    internal static List<EcosystemIntegrationSignalInfo> Scan(MetadataReader reader)
        => Project(EcosystemIntegrationClassifier.Classify(
            EcosystemIntegrationObservationReader.Read(reader)));

    internal static List<EcosystemIntegrationSignalInfo> Project(
        ImmutableArray<EcosystemIntegrationClassification> classifications)
    {
        var buckets = IntegrationBuckets.Create();
        foreach (EcosystemIntegrationClassification classification in classifications)
        {
            IntegrationBucket bucket = GetBucket(buckets, classification);
            switch (classification.Observation)
            {
                case EcosystemIntegrationTypeObservation type:
                    AddType(
                        bucket,
                        type.MetadataName,
                        "TypeDef",
                        classification.Kind,
                        type.Definition);
                    break;
                case EcosystemIntegrationMethodObservation method:
                    AddApi(
                        bucket,
                        $"{TypeResolver.FormatDisplayName(method.DeclaringType.MetadataName)}.{method.Name}(...)",
                        classification.Kind,
                        method.Evidence);
                    break;
                default:
                    throw new InvalidOperationException("Unknown Integration observation.");
            }
        }

        List<EcosystemIntegrationSignalInfo> results = [];
        foreach (var bucket in buckets.All)
            AddRows(results, bucket);
        return results;
    }

    private static IntegrationBucket GetBucket(
        IntegrationBuckets buckets,
        EcosystemIntegrationClassification classification)
    {
        IntegrationConceptDescriptor concept = classification.Concept;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.AI))
            return GetAIBucket(buckets, classification.Kind);
        if (ReferenceEquals(concept, IntegrationConceptCatalog.AspNetCore))
            return buckets.AspNetCore;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Aspire))
            return buckets.Aspire;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Authentication))
            return buckets.Authentication;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Configuration))
            return buckets.Configuration;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.OpenAPI))
            return buckets.OpenApi;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.DependencyInjection))
            return buckets.DependencyInjection;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Logging))
            return buckets.Logging;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Options))
            return buckets.Options;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Hosting))
            return buckets.Hosting;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.HealthChecks))
            return buckets.HealthChecks;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.HttpClient))
            return buckets.HttpClient;
        throw new InvalidOperationException("Unknown ecosystem Integration concept.");
    }

    private static IntegrationBucket GetAIBucket(IntegrationBuckets buckets, string kind) => kind switch
    {
        "Chat" => buckets.AIChat,
        "Embeddings" => buckets.AIEmbeddings,
        "Images" => buckets.AIImages,
        "Realtime" => buckets.AIRealtime,
        "Hosting" => buckets.AIHosting,
        "Speech to Text" => buckets.AISpeechToText,
        "Text to Speech" => buckets.AITextToSpeech,
        "Tools" => buckets.AITools,
        "Hosted Files" => buckets.AIHostedFiles,
        "Builder" => buckets.AIBuilder,
        "Configuration" => buckets.AIConfiguration,
        _ => throw new InvalidOperationException($"Unknown AI integration kind '{kind}'.")
    };

    private static void AddType(
        IntegrationBucket bucket,
        string typeName,
        string source,
        string kind,
        MetadataTypeDefinitionName? definitionName)
    {
        if (bucket.Types.TryGetValue(typeName, out var existing))
        {
            if (!existing.Split('/').Contains(source, StringComparer.Ordinal))
                bucket.Types[typeName] = $"{existing}/{source}";
            if (definitionName is not null)
                bucket.TypeDefinitions.TryAdd(typeName, definitionName);
            return;
        }

        bucket.Types.Add(typeName, source);
        bucket.Kinds[typeName] = kind;
        if (definitionName is not null)
            bucket.TypeDefinitions.Add(typeName, definitionName);
    }

    static void AddApi(
        IntegrationBucket bucket,
        string api,
        string kind,
        EcosystemIntegrationApiEvidence? evidence)
    {
        bucket.Apis.TryAdd(api, kind);
        if (evidence is null)
        {
            bucket.ApiEvidenceUnavailable.Add(api);
            return;
        }

        if (!bucket.ApiEvidence.TryGetValue(
                api,
                out List<EcosystemIntegrationApiEvidence>? evidenceSet))
        {
            evidenceSet = [];
            bucket.ApiEvidence.Add(api, evidenceSet);
        }
        if (!evidenceSet.Contains(evidence))
            evidenceSet.Add(evidence);
    }

    private static void AddRows(List<EcosystemIntegrationSignalInfo> results, IntegrationBucket bucket)
    {
        foreach (var api in OrderApis(bucket.Apis))
            results.Add(new EcosystemIntegrationSignalInfo(
                bucket.Concept,
                bucket.Apis[api],
                api,
                IntegrationSignalShape.Api)
            {
                ApiEvidence =
                [
                    .. bucket.ApiEvidence.GetValueOrDefault(api) ?? [],
                ],
                ApiEvidenceUnavailable =
                    bucket.ApiEvidenceUnavailable.Contains(api),
            });

        foreach (var type in OrderTypes(bucket.Types))
            results.Add(new EcosystemIntegrationSignalInfo(
                bucket.Concept,
                bucket.Kinds.TryGetValue(type, out var kind) ? kind : bucket.ApiKind,
                TypeResolver.FormatDisplayName(type))
            {
                TypeDefinition =
                    bucket.TypeDefinitions.GetValueOrDefault(type),
            });
    }

    private static IEnumerable<string> OrderApis(Dictionary<string, string> apis)
        => apis
            .OrderBy(kv => GetKindRank(kv.Value))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key);

    private static IEnumerable<string> OrderTypes(Dictionary<string, string> types)
        => types
            .OrderBy(kv => GetEvidenceRank(kv.Key))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key);

    private static int GetKindRank(string kind) => kind switch
    {
        "Hosting" => 0,
        "Resource Builder" => 0,
        "Builder" => 1,
        "Resource" => 1,
        "Configuration" => 2,
        "Configuration Source" => 2,
        "Options Binding" => 2,
        "Binding" => 2,
        "Configuration Consumer" => 2,
        "Resource Interface" => 2,
        "Data Protection" => 3,
        "Chat" => 3,
        "Embeddings" => 4,
        "Images" => 5,
        "Realtime" => 6,
        "Speech to Text" => 7,
        "Text to Speech" => 8,
        "Tools" => 9,
        "Hosted Files" => 10,
        "HTTP Logging" => 20,
        "HTTP Latency" => 21,
        "HTTP Diagnostics" => 22,
        "Builder Configuration" => 23,
        "Service Registration" => 24,
        "Assembly Scanning" => 25,
        "Factory" => 26,
        "Decoration" => 27,
        "Hosted Service" => 28,
        "Health Check" => 29,
        "Middleware" => 30,
        "Endpoint" => 31,
        "Authentication State" => 32,
        "Authorization UI" => 33,
        "Authorization" => 34,
        "Validation" => 35,
        "Policy" => 36,
        "Requirement" => 37,
        "Handler" => 38,
        "Annotation" => 39,
        "Filter" => 40,
        _ => 100
    };

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
            "Microsoft.Extensions.AI.IChatClient" => 0,
            "Microsoft.Extensions.AI.IEmbeddingGenerator" => 0,
            "Microsoft.Extensions.AI.IEmbeddingGenerator`2" => 0,
            "Microsoft.Extensions.AI.IImageGenerator" => 0,
            "Microsoft.Extensions.AI.IRealtimeClient" => 0,
            "Microsoft.Extensions.AI.ISpeechToTextClient" => 0,
            "Microsoft.Extensions.AI.ITextToSpeechClient" => 0,
            "Microsoft.Extensions.AI.IHostedFileClient" => 0,
            "Microsoft.Extensions.AI.AITool" => 0,
            "Microsoft.Extensions.AI.AIFunction" => 1,
            _ => 10,
        };

    private sealed class IntegrationBucket(
        IntegrationConceptDescriptor concept,
        string apiKind)
    {
        public IntegrationConceptDescriptor Concept { get; } = concept;
        public string ApiKind { get; } = apiKind;
        public Dictionary<string, string> Apis { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<EcosystemIntegrationApiEvidence>>
            ApiEvidence { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ApiEvidenceUnavailable { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, string> Types { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Kinds { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, MetadataTypeDefinitionName>
            TypeDefinitions { get; } = new(StringComparer.Ordinal);
    }

    private sealed class IntegrationBuckets
    {
        public required IntegrationBucket AspNetCore { get; init; }
        public required IntegrationBucket Aspire { get; init; }
        public required IntegrationBucket AIChat { get; init; }
        public required IntegrationBucket AIEmbeddings { get; init; }
        public required IntegrationBucket AIImages { get; init; }
        public required IntegrationBucket AIRealtime { get; init; }
        public required IntegrationBucket AIHosting { get; init; }
        public required IntegrationBucket AISpeechToText { get; init; }
        public required IntegrationBucket AITextToSpeech { get; init; }
        public required IntegrationBucket AITools { get; init; }
        public required IntegrationBucket AIHostedFiles { get; init; }
        public required IntegrationBucket AIBuilder { get; init; }
        public required IntegrationBucket AIConfiguration { get; init; }
        public required IntegrationBucket Authentication { get; init; }
        public required IntegrationBucket Configuration { get; init; }
        public required IntegrationBucket DependencyInjection { get; init; }
        public required IntegrationBucket Logging { get; init; }
        public required IntegrationBucket OpenApi { get; init; }
        public required IntegrationBucket Options { get; init; }
        public required IntegrationBucket Hosting { get; init; }
        public required IntegrationBucket HealthChecks { get; init; }
        public required IntegrationBucket HttpClient { get; init; }

        public IntegrationBucket[] All =>
        [
            AspNetCore,
            Aspire,
            AIHosting,
            AIBuilder,
            AIConfiguration,
            AIChat,
            AIEmbeddings,
            AIImages,
            AIRealtime,
            AISpeechToText,
            AITextToSpeech,
            AITools,
            AIHostedFiles,
            Authentication,
            Configuration,
            DependencyInjection,
            Logging,
            OpenApi,
            Options,
            Hosting,
            HealthChecks,
            HttpClient
        ];

        public static IntegrationBuckets Create() => new()
        {
            AspNetCore = new IntegrationBucket(IntegrationConceptCatalog.AspNetCore, "ASP.NET Core"),
            Aspire = new IntegrationBucket(IntegrationConceptCatalog.Aspire, "Aspire"),
            AIChat = new IntegrationBucket(IntegrationConceptCatalog.AI, "Chat"),
            AIEmbeddings = new IntegrationBucket(IntegrationConceptCatalog.AI, "Embeddings"),
            AIImages = new IntegrationBucket(IntegrationConceptCatalog.AI, "Images"),
            AIRealtime = new IntegrationBucket(IntegrationConceptCatalog.AI, "Realtime"),
            AIHosting = new IntegrationBucket(IntegrationConceptCatalog.AI, "Hosting"),
            AISpeechToText = new IntegrationBucket(IntegrationConceptCatalog.AI, "Speech to Text"),
            AITextToSpeech = new IntegrationBucket(IntegrationConceptCatalog.AI, "Text to Speech"),
            AITools = new IntegrationBucket(IntegrationConceptCatalog.AI, "Tools"),
            AIHostedFiles = new IntegrationBucket(IntegrationConceptCatalog.AI, "Hosted Files"),
            AIBuilder = new IntegrationBucket(IntegrationConceptCatalog.AI, "Builder"),
            AIConfiguration = new IntegrationBucket(IntegrationConceptCatalog.AI, "Configuration"),
            Authentication = new IntegrationBucket(IntegrationConceptCatalog.Authentication, "Authentication"),
            Configuration = new IntegrationBucket(IntegrationConceptCatalog.Configuration, "Configuration"),
            DependencyInjection = new IntegrationBucket(IntegrationConceptCatalog.DependencyInjection, "Dependency Injection"),
            Logging = new IntegrationBucket(IntegrationConceptCatalog.Logging, "Logging"),
            OpenApi = new IntegrationBucket(IntegrationConceptCatalog.OpenAPI, "OpenAPI"),
            Options = new IntegrationBucket(IntegrationConceptCatalog.Options, "Options"),
            Hosting = new IntegrationBucket(IntegrationConceptCatalog.Hosting, "Hosting"),
            HealthChecks = new IntegrationBucket(IntegrationConceptCatalog.HealthChecks, "Health Check"),
            HttpClient = new IntegrationBucket(IntegrationConceptCatalog.HttpClient, "HTTP Client")
        };
    }

}
