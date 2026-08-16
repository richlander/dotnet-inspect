using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Performs the single SRM traversal and projects classified integration evidence
/// into the ordered signal rows consumed by queries and Findings.
/// </summary>
internal static class EcosystemIntegrationProjection
{
    internal static List<EcosystemIntegrationSignalInfo> Scan(MetadataReader reader)
    {
        var buckets = IntegrationBuckets.Create();

        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDefinition = reader.GetTypeDefinition(handle);
            if (!typeDefinition.IsPublic)
                continue;

            var typeName = reader.GetFullTypeName(typeDefinition);
            MetadataTypeDefinitionName? definitionName =
                MetadataTypeDefinitionNameReader.Read(reader, handle)
                    is MetadataTypeDefinitionNameReadResult.Read read
                        ? read.Name
                        : null;
            AddType(
                buckets,
                typeName,
                "TypeDef",
                definitionName);
            AddStarterMethods(
                buckets,
                reader,
                handle,
                typeDefinition,
                typeName,
                definitionName);
        }

        List<EcosystemIntegrationSignalInfo> results = [];
        foreach (var bucket in buckets.All)
            AddRows(results, bucket);
        return results;
    }

    private static void AddType(
        IntegrationBuckets buckets,
        string typeName,
        string source,
        MetadataTypeDefinitionName? definitionName)
    {
        if (source == "TypeDef" && EcosystemIntegrationClassifier.TryGetAspNetCoreKind(typeName, out var aspNetCoreKind))
            AddType(buckets.AspNetCore, typeName, source, aspNetCoreKind, definitionName);
        if (source == "TypeDef" && EcosystemIntegrationClassifier.TryGetAspireKind(typeName, out var aspireKind))
            AddType(buckets.Aspire, typeName, source, aspireKind, definitionName);
        if (source == "TypeDef" && EcosystemIntegrationClassifier.TryGetAIKind(typeName, out var aiKind))
            AddType(GetAIBucket(buckets, aiKind), typeName, source, definitionName);
        if (source == "TypeDef" && EcosystemIntegrationClassifier.TryGetAuthenticationKind(typeName, out var authenticationKind))
            AddType(buckets.Authentication, typeName, source, authenticationKind, definitionName);
        if (source == "TypeDef" && EcosystemIntegrationClassifier.TryGetConfigurationKind(typeName, out var configurationKind))
            AddType(buckets.Configuration, typeName, source, configurationKind, definitionName);
        if (source == "TypeDef" && EcosystemIntegrationClassifier.TryGetOpenApiKind(typeName, out var openApiKind))
            AddType(buckets.OpenApi, typeName, source, openApiKind, definitionName);
        if (EcosystemIntegrationClassifier.TryGetDependencyInjectionKind(typeName, out var dependencyInjectionKind))
            AddType(buckets.DependencyInjection, typeName, source, dependencyInjectionKind, definitionName);
        if (EcosystemIntegrationClassifier.IsLoggingType(typeName))
            AddType(buckets.Logging, typeName, source, definitionName);
        if (EcosystemIntegrationClassifier.IsOptionsType(typeName))
            AddType(buckets.Options, typeName, source, definitionName);
        if (EcosystemIntegrationClassifier.IsHostingType(typeName))
            AddType(buckets.Hosting, typeName, source, definitionName);
        if (EcosystemIntegrationClassifier.IsHealthChecksType(typeName))
            AddType(buckets.HealthChecks, typeName, source, definitionName);
        if (EcosystemIntegrationClassifier.TryGetHttpClientKind(typeName, out var httpClientKind))
            AddType(buckets.HttpClient, typeName, source, httpClientKind, definitionName);
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
        MetadataTypeDefinitionName? definitionName)
        => AddType(
            bucket,
            typeName,
            source,
            bucket.ApiKind,
            definitionName);

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

    private static void AddStarterMethods(
        IntegrationBuckets buckets,
        MetadataReader reader,
        TypeDefinitionHandle typeDefinitionHandle,
        TypeDefinition typeDefinition,
        string typeName,
        MetadataTypeDefinitionName? definitionName)
    {
        var attributes = typeDefinition.Attributes;
        var isStatic = (attributes & TypeAttributes.Sealed) != 0
                       && (attributes & TypeAttributes.Abstract) != 0;
        if (!isStatic || !AttributeReader.HasExtensionAttribute(reader, typeDefinition.GetCustomAttributes()))
            return;

        foreach (var methodHandle in typeDefinition.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                || (method.Attributes & MethodAttributes.Static) == 0
                || !AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes()))
                continue;

            var methodName = reader.GetString(method.Name);
            var context = GenericContext.ForMethod(reader, typeDefinition, method);
            MethodSignature<string> signature;
            try
            {
                signature = GuardedSignatureText.MethodText(reader, method, context)
                    .GetValueOrThrow();
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            var api = $"{TypeResolver.FormatDisplayName(typeName)}.{methodName}(...)";
            EcosystemIntegrationApiEvidence? evidence = null;
            if (definitionName is not null)
            {
                ExtensionMemberAnchorInfo anchor =
                    ApiMemberIdentity.CreateExtensionMethodAnchorInfo(
                        reader,
                        typeDefinitionHandle,
                        method);
                evidence = new EcosystemIntegrationApiEvidence(
                    anchor.Anchor,
                    definitionName,
                    anchor.ExtendedTypeReference,
                    anchor.ReturnTypeReference);
            }
            if (EcosystemIntegrationClassifier.TryClassifyAspireStarterMethod(typeName, methodName, signature, out var aspireKind))
                AddApi(buckets.Aspire, api, aspireKind, evidence);
            if (EcosystemIntegrationClassifier.TryClassifyAIStarterMethod(typeName, methodName, signature, out var aiKind))
                AddApi(GetAIBucket(buckets, aiKind), api, aiKind, evidence);
            if (EcosystemIntegrationClassifier.TryClassifyAuthenticationStarterMethod(methodName, signature, out var authenticationKind))
                AddApi(buckets.Authentication, api, authenticationKind, evidence);
            if (EcosystemIntegrationClassifier.TryClassifyConfigurationStarterMethod(methodName, signature, out var configurationKind))
                AddApi(buckets.Configuration, api, configurationKind, evidence);
            if (EcosystemIntegrationClassifier.TryClassifyDependencyInjectionStarterMethod(typeName, methodName, signature, out var dependencyInjectionKind))
                AddApi(buckets.DependencyInjection, api, dependencyInjectionKind, evidence);
            if (EcosystemIntegrationClassifier.TryClassifyLoggingStarterMethod(typeName, methodName, signature, out var loggingKind))
                AddApi(buckets.Logging, api, loggingKind, evidence);
            if (EcosystemIntegrationClassifier.TryClassifyOpenApiStarterMethod(typeName, methodName, signature, out var openApiKind))
                AddApi(buckets.OpenApi, api, openApiKind, evidence);
            if (EcosystemIntegrationClassifier.TryClassifyOptionsStarterMethod(methodName, signature, out var optionsKind))
                AddApi(buckets.Options, api, optionsKind, evidence);
            if (EcosystemIntegrationClassifier.TryClassifyAspNetCoreStarterMethod(methodName, signature, out var aspNetCoreKind))
                AddApi(buckets.AspNetCore, api, aspNetCoreKind, evidence);
            if (EcosystemIntegrationClassifier.TryClassifyHealthChecksStarterMethod(methodName, signature, out var healthChecksKind))
                AddApi(buckets.HealthChecks, api, healthChecksKind, evidence);
            if (EcosystemIntegrationClassifier.TryClassifyHostingStarterMethod(typeName, methodName, signature, out var hostingKind))
                AddApi(buckets.Hosting, api, hostingKind, evidence);
            if (EcosystemIntegrationClassifier.TryClassifyHttpClientStarterMethod(typeName, methodName, signature, out var httpClientKind))
                AddApi(buckets.HttpClient, api, httpClientKind, evidence);
        }
    }

    static void AddApi(
        IntegrationBucket bucket,
        string api,
        string kind,
        EcosystemIntegrationApiEvidence? evidence)
    {
        if (!bucket.Apis.TryAdd(api, kind))
            return;
        if (evidence is not null)
            bucket.ApiEvidence.Add(api, evidence);
    }

    private static void AddRows(List<EcosystemIntegrationSignalInfo> results, IntegrationBucket bucket)
    {
        foreach (var api in OrderApis(bucket.Apis))
            results.Add(new EcosystemIntegrationSignalInfo(
                bucket.Integration,
                bucket.Apis[api],
                api,
                IntegrationSignalShape.Api)
            {
                ApiEvidence = bucket.ApiEvidence.GetValueOrDefault(api),
            });

        foreach (var type in OrderTypes(bucket.Types))
            results.Add(new EcosystemIntegrationSignalInfo(
                bucket.Integration,
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

    private sealed class IntegrationBucket(string integration, string apiKind)
    {
        public string Integration { get; } = integration;
        public string ApiKind { get; } = apiKind;
        public Dictionary<string, string> Apis { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, EcosystemIntegrationApiEvidence>
            ApiEvidence { get; } = new(StringComparer.Ordinal);
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
            AspNetCore = new IntegrationBucket(EcosystemIntegrationNames.AspNetCore, "ASP.NET Core"),
            Aspire = new IntegrationBucket(EcosystemIntegrationNames.Aspire, "Aspire"),
            AIChat = new IntegrationBucket(EcosystemIntegrationNames.AI, "Chat"),
            AIEmbeddings = new IntegrationBucket(EcosystemIntegrationNames.AI, "Embeddings"),
            AIImages = new IntegrationBucket(EcosystemIntegrationNames.AI, "Images"),
            AIRealtime = new IntegrationBucket(EcosystemIntegrationNames.AI, "Realtime"),
            AIHosting = new IntegrationBucket(EcosystemIntegrationNames.AI, "Hosting"),
            AISpeechToText = new IntegrationBucket(EcosystemIntegrationNames.AI, "Speech to Text"),
            AITextToSpeech = new IntegrationBucket(EcosystemIntegrationNames.AI, "Text to Speech"),
            AITools = new IntegrationBucket(EcosystemIntegrationNames.AI, "Tools"),
            AIHostedFiles = new IntegrationBucket(EcosystemIntegrationNames.AI, "Hosted Files"),
            AIBuilder = new IntegrationBucket(EcosystemIntegrationNames.AI, "Builder"),
            AIConfiguration = new IntegrationBucket(EcosystemIntegrationNames.AI, "Configuration"),
            Authentication = new IntegrationBucket(EcosystemIntegrationNames.Authentication, "Authentication"),
            Configuration = new IntegrationBucket(EcosystemIntegrationNames.Configuration, "Configuration"),
            DependencyInjection = new IntegrationBucket(EcosystemIntegrationNames.DependencyInjection, "Dependency Injection"),
            Logging = new IntegrationBucket(EcosystemIntegrationNames.Logging, "Logging"),
            OpenApi = new IntegrationBucket(EcosystemIntegrationNames.OpenAPI, "OpenAPI"),
            Options = new IntegrationBucket(EcosystemIntegrationNames.Options, "Options"),
            Hosting = new IntegrationBucket(EcosystemIntegrationNames.Hosting, "Hosting"),
            HealthChecks = new IntegrationBucket(EcosystemIntegrationNames.HealthChecks, "Health Check"),
            HttpClient = new IntegrationBucket(EcosystemIntegrationNames.HttpClient, "HTTP Client")
        };
    }

}
