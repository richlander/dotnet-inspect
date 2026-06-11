using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

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
    public bool HasAspireSupport { get; init; }
    public bool HasAISupport { get; init; }
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

        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDefinition = reader.GetTypeDefinition(handle);
            if (!typeDefinition.IsPublic)
                continue;

            var typeName = reader.GetFullTypeName(typeDefinition);
            AddType(buckets, typeName, "TypeDef");
            AddStarterMethods(buckets, reader, typeDefinition, typeName);
        }

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

        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDefinition = reader.GetTypeDefinition(handle);
            if (!typeDefinition.IsPublic)
                continue;

            MarkTypePresence(presence, reader.GetFullTypeName(typeDefinition));
        }

        return presence.ToImmutable();
    }

    private static void AddType(IntegrationBuckets buckets, string typeName, string source)
    {
        if (source == "TypeDef" && TryGetAspireKind(typeName, out var aspireKind))
            AddType(buckets.Aspire, typeName, source, aspireKind);
        if (source == "TypeDef" && TryGetAIKind(typeName, out var aiKind))
            AddType(GetAIBucket(buckets, aiKind), typeName, source);
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
        if (TryGetHttpClientKind(typeName, out var httpClientKind))
            AddType(buckets.HttpClient, typeName, source, httpClientKind);
    }

    private static void MarkTypePresence(MutablePresence presence, string typeName)
    {
        if (IsAspireType(typeName))
            presence.HasAspireSupport = true;
        if (IsAIType(typeName))
            presence.HasAISupport = true;
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

    private static bool IsHttpClientType(string typeName) => TryGetHttpClientKind(typeName, out _);

    private static bool TryGetHttpClientKind(string typeName, out string kind)
    {
        kind = typeName switch
        {
            "System.Net.Http.IHttpClientFactory" => "Factory",
            "Microsoft.Extensions.DependencyInjection.IHttpClientBuilder" => "Builder",
            _ => ""
        };
        if (kind.Length > 0)
            return true;

        if (typeName.StartsWith("Microsoft.Extensions.Http.Logging.", StringComparison.Ordinal))
            kind = "HTTP Logging";
        else if (typeName.StartsWith("Microsoft.Extensions.Http.Latency.", StringComparison.Ordinal))
            kind = "HTTP Latency";
        else if (typeName.StartsWith("Microsoft.Extensions.Http.Diagnostics.", StringComparison.Ordinal))
            kind = "HTTP Diagnostics";
        else if (typeName.StartsWith("Microsoft.Extensions.Http.", StringComparison.Ordinal))
            kind = "HTTP Client";

        return kind.Length > 0;
    }

    private static bool TryClassifyAIAdapterReturnType(string returnType, out string kind)
    {
        kind = "";
        if (returnType.StartsWith("Microsoft.Extensions.AI.IChatClient", StringComparison.Ordinal))
            kind = "Chat";
        else if (returnType.StartsWith("Microsoft.Extensions.AI.IEmbeddingGenerator", StringComparison.Ordinal))
            kind = "Embeddings";
        else if (returnType.StartsWith("Microsoft.Extensions.AI.IImageGenerator", StringComparison.Ordinal))
            kind = "Images";
        else if (returnType.StartsWith("Microsoft.Extensions.AI.IRealtimeClient", StringComparison.Ordinal))
            kind = "Realtime";
        else if (returnType.StartsWith("Microsoft.Extensions.AI.ISpeechToTextClient", StringComparison.Ordinal))
            kind = "Speech to Text";
        else if (returnType.StartsWith("Microsoft.Extensions.AI.ITextToSpeechClient", StringComparison.Ordinal))
            kind = "Text to Speech";
        else if (returnType.StartsWith("Microsoft.Extensions.AI.IHostedFileClient", StringComparison.Ordinal))
            kind = "Hosted Files";
        else if (returnType.StartsWith("Microsoft.Extensions.AI.AITool", StringComparison.Ordinal))
            kind = "Tools";

        return kind.Length > 0;
    }

    private static bool IsAspireType(string typeName) => TryGetAspireKind(typeName, out _);

    private static bool TryGetAspireKind(string typeName, out string kind)
    {
        kind = "";
        if (!typeName.StartsWith("Aspire.Hosting.", StringComparison.Ordinal))
            return false;

        if (typeName.EndsWith("Resource", StringComparison.Ordinal))
        {
            var simpleName = typeName[(typeName.LastIndexOf('.') + 1)..];
            kind = simpleName is ['I', >= 'A' and <= 'Z', ..]
                ? "Resource Interface"
                : "Resource";
            return true;
        }

        return false;
    }

    private static bool IsAIType(string typeName) => TryGetAIKind(typeName, out _);

    private static bool TryGetAIKind(string typeName, out string kind)
    {
        if (AITypes.TryGetValue(typeName, out kind!))
            return true;

        if (typeName.StartsWith("Microsoft.Extensions.AI.", StringComparison.Ordinal)
            && typeName.Contains("OpenAI", StringComparison.Ordinal)
            && typeName.Contains("RealtimeClient", StringComparison.Ordinal))
        {
            kind = "Realtime";
            return true;
        }

        if (!typeName.StartsWith("Aspire.", StringComparison.Ordinal)
            || !typeName.Contains("OpenAI", StringComparison.Ordinal))
            return false;

        if (typeName.EndsWith("ClientBuilder", StringComparison.Ordinal))
        {
            kind = "Builder";
            return true;
        }

        if (typeName.EndsWith("Settings", StringComparison.Ordinal))
        {
            kind = "Configuration";
            return true;
        }

        return false;
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

    private static readonly Dictionary<string, string> AITypes = new(StringComparer.Ordinal)
    {
        ["Microsoft.Extensions.AI.IChatClient"] = "Chat",
        ["Microsoft.Extensions.AI.ChatClientBuilder"] = "Chat",
        ["Microsoft.Extensions.AI.ChatClientBuilderChatClientExtensions"] = "Chat",
        ["Microsoft.Extensions.AI.ChatClientExtensions"] = "Chat",
        ["Microsoft.Extensions.AI.ChatClientStructuredOutputExtensions"] = "Chat",
        ["Microsoft.Extensions.AI.ChatMessage"] = "Chat",
        ["Microsoft.Extensions.AI.ChatOptions"] = "Chat",
        ["Microsoft.Extensions.AI.ChatResponse"] = "Chat",
        ["Microsoft.Extensions.AI.ChatResponse`1"] = "Chat",
        ["Microsoft.Extensions.AI.ChatResponseUpdate"] = "Chat",
        ["Microsoft.Extensions.AI.ChatRole"] = "Chat",
        ["Microsoft.Extensions.AI.ChatToolMode"] = "Chat",

        ["Microsoft.Extensions.AI.IEmbeddingGenerator"] = "Embeddings",
        ["Microsoft.Extensions.AI.IEmbeddingGenerator`2"] = "Embeddings",
        ["Microsoft.Extensions.AI.EmbeddingGeneratorBuilder`2"] = "Embeddings",
        ["Microsoft.Extensions.AI.EmbeddingGeneratorBuilderEmbeddingGeneratorExtensions"] = "Embeddings",
        ["Microsoft.Extensions.AI.EmbeddingGeneratorExtensions"] = "Embeddings",
        ["Microsoft.Extensions.AI.EmbeddingGenerationOptions"] = "Embeddings",
        ["Microsoft.Extensions.AI.Embedding"] = "Embeddings",
        ["Microsoft.Extensions.AI.Embedding`1"] = "Embeddings",
        ["Microsoft.Extensions.AI.GeneratedEmbeddings`1"] = "Embeddings",

        ["Microsoft.Extensions.AI.IImageGenerator"] = "Images",
        ["Microsoft.Extensions.AI.ImageGeneratorBuilder"] = "Images",
        ["Microsoft.Extensions.AI.ImageGeneratorBuilderImageGeneratorExtensions"] = "Images",
        ["Microsoft.Extensions.AI.ImageGeneratorExtensions"] = "Images",
        ["Microsoft.Extensions.AI.ImageGenerationOptions"] = "Images",
        ["Microsoft.Extensions.AI.ImageGenerationRequest"] = "Images",
        ["Microsoft.Extensions.AI.ImageGenerationResponse"] = "Images",
        ["Microsoft.Extensions.AI.ImageGenerationResponseFormat"] = "Images",

        ["Microsoft.Extensions.AI.IRealtimeClient"] = "Realtime",
        ["Microsoft.Extensions.AI.IRealtimeClientSession"] = "Realtime",
        ["Microsoft.Extensions.AI.RealtimeClientBuilder"] = "Realtime",
        ["Microsoft.Extensions.AI.RealtimeClientBuilderRealtimeClientExtensions"] = "Realtime",
        ["Microsoft.Extensions.AI.RealtimeClientExtensions"] = "Realtime",
        ["Microsoft.Extensions.AI.RealtimeClientSessionExtensions"] = "Realtime",
        ["Microsoft.Extensions.AI.RealtimeSessionOptions"] = "Realtime",

        ["Microsoft.Extensions.AI.ISpeechToTextClient"] = "Speech to Text",
        ["Microsoft.Extensions.AI.SpeechToTextClientBuilder"] = "Speech to Text",
        ["Microsoft.Extensions.AI.SpeechToTextClientBuilderSpeechToTextClientExtensions"] = "Speech to Text",
        ["Microsoft.Extensions.AI.SpeechToTextClientExtensions"] = "Speech to Text",
        ["Microsoft.Extensions.AI.SpeechToTextOptions"] = "Speech to Text",
        ["Microsoft.Extensions.AI.SpeechToTextResponse"] = "Speech to Text",
        ["Microsoft.Extensions.AI.SpeechToTextResponseUpdate"] = "Speech to Text",

        ["Microsoft.Extensions.AI.ITextToSpeechClient"] = "Text to Speech",
        ["Microsoft.Extensions.AI.TextToSpeechClientBuilder"] = "Text to Speech",
        ["Microsoft.Extensions.AI.TextToSpeechClientBuilderTextToSpeechClientExtensions"] = "Text to Speech",
        ["Microsoft.Extensions.AI.TextToSpeechClientExtensions"] = "Text to Speech",
        ["Microsoft.Extensions.AI.TextToSpeechOptions"] = "Text to Speech",
        ["Microsoft.Extensions.AI.TextToSpeechResponse"] = "Text to Speech",
        ["Microsoft.Extensions.AI.TextToSpeechResponseUpdate"] = "Text to Speech",

        ["Microsoft.Extensions.AI.AITool"] = "Tools",
        ["Microsoft.Extensions.AI.AIFunction"] = "Tools",
        ["Microsoft.Extensions.AI.AIFunctionArguments"] = "Tools",
        ["Microsoft.Extensions.AI.AIFunctionDeclaration"] = "Tools",
        ["Microsoft.Extensions.AI.AIFunctionFactory"] = "Tools",
        ["Microsoft.Extensions.AI.FunctionCallContent"] = "Tools",
        ["Microsoft.Extensions.AI.FunctionResultContent"] = "Tools",
        ["Microsoft.Extensions.AI.ToolCallContent"] = "Tools",
        ["Microsoft.Extensions.AI.ToolResultContent"] = "Tools",

        ["Microsoft.Extensions.AI.IHostedFileClient"] = "Hosted Files",
        ["Microsoft.Extensions.AI.HostedFileClientBuilder"] = "Hosted Files",
        ["Microsoft.Extensions.AI.HostedFileClientBuilderHostedFileClientExtensions"] = "Hosted Files",
        ["Microsoft.Extensions.AI.HostedFileClientExtensions"] = "Hosted Files",
        ["Microsoft.Extensions.AI.HostedFileClientOptions"] = "Hosted Files",
        ["Microsoft.Extensions.AI.HostedFileContent"] = "Hosted Files",
    };

    private static void AddType(IntegrationBucket bucket, string typeName, string source)
        => AddType(bucket, typeName, source, bucket.ApiKind);

    private static void AddType(IntegrationBucket bucket, string typeName, string source, string kind)
    {
        if (bucket.Types.TryGetValue(typeName, out var existing))
        {
            if (!existing.Split('/').Contains(source, StringComparer.Ordinal))
                bucket.Types[typeName] = $"{existing}/{source}";
            return;
        }

        bucket.Types.Add(typeName, source);
        bucket.Kinds[typeName] = kind;
    }

    private static void AddStarterMethods(
        IntegrationBuckets buckets,
        MetadataReader reader,
        TypeDefinition typeDefinition,
        string typeName)
    {
        var attributes = typeDefinition.Attributes;
        var isStatic = (attributes & TypeAttributes.Sealed) != 0
                       && (attributes & TypeAttributes.Abstract) != 0;
        if (!isStatic || !AttributeReader.HasExtensionAttribute(reader, typeDefinition.GetCustomAttributes()))
            return;

        foreach (var methodHandle in typeDefinition.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if ((method.Attributes & MethodAttributes.Public) == 0
                || (method.Attributes & MethodAttributes.Static) == 0
                || !AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes()))
                continue;

            var methodName = reader.GetString(method.Name);
            var context = GenericContext.ForMethod(reader, typeDefinition, method);
            var signature = method.DecodeSignature(SignatureDecoder.Instance, context);
            var api = $"{TypeResolver.FormatDisplayName(typeName)}.{methodName}(...)";
            if (TryClassifyAspireStarterMethod(typeName, methodName, signature, out var aspireKind))
                buckets.Aspire.Apis.TryAdd(api, aspireKind);
            if (TryClassifyAIStarterMethod(typeName, methodName, signature, out var aiKind))
                GetAIBucket(buckets, aiKind).Apis.TryAdd(api, aiKind);
            if (TryClassifyDependencyInjectionStarterMethod(typeName, methodName, signature, out var dependencyInjectionKind))
                buckets.DependencyInjection.Apis.TryAdd(api, dependencyInjectionKind);
            if (TryClassifyHostingStarterMethod(typeName, methodName, signature, out var hostingKind))
                buckets.Hosting.Apis.TryAdd(api, hostingKind);
            if (TryClassifyHttpClientStarterMethod(typeName, methodName, signature, out var httpClientKind))
                buckets.HttpClient.Apis.TryAdd(api, httpClientKind);
        }
    }

    private static bool TryClassifyDependencyInjectionStarterMethod(
        string declaringType,
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (!declaringType.StartsWith("Microsoft.Extensions.DependencyInjection.", StringComparison.Ordinal)
            || !methodName.StartsWith("Add", StringComparison.Ordinal)
            || signature.ParameterTypes.Length == 0
            || signature.ParameterTypes[0] != "Microsoft.Extensions.DependencyInjection.IServiceCollection")
            return false;

        kind = "Service Registration";
        return true;
    }

    private static bool TryClassifyAspireStarterMethod(
        string declaringType,
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (!declaringType.StartsWith("Aspire.Hosting.", StringComparison.Ordinal)
            || !methodName.StartsWith("Add", StringComparison.Ordinal)
            || signature.ParameterTypes.Length == 0
            || signature.ParameterTypes[0] != "Aspire.Hosting.IDistributedApplicationBuilder"
            || !signature.ReturnType.StartsWith("Aspire.Hosting.ApplicationModel.IResourceBuilder<", StringComparison.Ordinal))
            return false;

        kind = "Resource Builder";
        return true;
    }

    private static bool TryClassifyHostingStarterMethod(
        string declaringType,
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (!declaringType.StartsWith("Microsoft.Extensions.Hosting.", StringComparison.Ordinal)
            || !methodName.StartsWith("Add", StringComparison.Ordinal)
            || signature.ParameterTypes.Length == 0
            || signature.ParameterTypes[0] != "Microsoft.Extensions.Hosting.IHostApplicationBuilder")
            return false;

        kind = "Hosting";
        return true;
    }

    private static bool TryClassifyHttpClientStarterMethod(
        string declaringType,
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (!methodName.StartsWith("Add", StringComparison.Ordinal)
            || signature.ParameterTypes.Length == 0
            || !declaringType.Contains("HttpClient", StringComparison.Ordinal))
            return false;

        if (declaringType.Contains("Logging", StringComparison.Ordinal))
            kind = "HTTP Logging";
        else if (declaringType.Contains("Latency", StringComparison.Ordinal))
            kind = "HTTP Latency";
        else if (declaringType.Contains("Diagnostics", StringComparison.Ordinal))
            kind = "HTTP Diagnostics";
        else
            kind = signature.ParameterTypes[0] switch
            {
                "Microsoft.Extensions.DependencyInjection.IHttpClientBuilder" => "Builder Configuration",
                "Microsoft.Extensions.DependencyInjection.IServiceCollection" => "Service Registration",
                _ => ""
            };
        return kind.Length > 0;
    }

    private static bool TryClassifyAIStarterMethod(
        string declaringType,
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (!declaringType.Contains("OpenAI", StringComparison.Ordinal)
            && !methodName.Contains("AI", StringComparison.Ordinal)
            && !methodName.Contains("Chat", StringComparison.Ordinal)
            && !methodName.Contains("Embedding", StringComparison.Ordinal)
            && !methodName.Contains("Image", StringComparison.Ordinal)
            && !methodName.Contains("Speech", StringComparison.Ordinal)
            && !methodName.Contains("Realtime", StringComparison.Ordinal)
            && !methodName.Contains("HostedFile", StringComparison.Ordinal))
            return false;

        var returnType = signature.ReturnType;
        if (methodName.StartsWith("AsI", StringComparison.Ordinal)
            || methodName == "AsAITool")
        {
            if (TryClassifyAIAdapterReturnType(returnType, out kind))
                return true;
        }

        if (returnType.Contains("ChatClientBuilder", StringComparison.Ordinal))
            kind = "Chat";
        else if (returnType.Contains("EmbeddingGeneratorBuilder", StringComparison.Ordinal))
            kind = "Embeddings";
        else if (returnType.Contains("ImageGeneratorBuilder", StringComparison.Ordinal))
            kind = "Images";
        else if (returnType.Contains("RealtimeClientBuilder", StringComparison.Ordinal))
            kind = "Realtime";
        else if (returnType.Contains("SpeechToTextClientBuilder", StringComparison.Ordinal))
            kind = "Speech to Text";
        else if (returnType.Contains("TextToSpeechClientBuilder", StringComparison.Ordinal))
            kind = "Text to Speech";
        else if (returnType.Contains("HostedFileClientBuilder", StringComparison.Ordinal))
            kind = "Hosted Files";
        else if (returnType.Contains("OpenAI", StringComparison.Ordinal)
                 && returnType.EndsWith("ClientBuilder", StringComparison.Ordinal))
            kind = "Hosting";

        return kind.Length > 0;
    }

    private static void AddRows(List<EcosystemIntegrationSignalInfo> results, IntegrationBucket bucket)
    {
        foreach (var api in OrderApis(bucket.Apis))
            results.Add(new EcosystemIntegrationSignalInfo(
                bucket.Integration,
                bucket.Apis[api],
                api,
                IntegrationSignalShape.Api));

        foreach (var type in OrderTypes(bucket.Types))
            results.Add(new EcosystemIntegrationSignalInfo(
                bucket.Integration,
                bucket.Kinds.TryGetValue(type, out var kind) ? kind : bucket.ApiKind,
                TypeResolver.FormatDisplayName(type)));
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
        "Resource Interface" => 2,
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
        "Factory" => 26,
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
        public Dictionary<string, string> Types { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Kinds { get; } = new(StringComparer.Ordinal);
    }

    private sealed class IntegrationBuckets
    {
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
        public required IntegrationBucket DependencyInjection { get; init; }
        public required IntegrationBucket Logging { get; init; }
        public required IntegrationBucket Options { get; init; }
        public required IntegrationBucket Hosting { get; init; }
        public required IntegrationBucket HealthChecks { get; init; }
        public required IntegrationBucket HttpClient { get; init; }

        public IntegrationBucket[] All =>
        [
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
            DependencyInjection,
            Logging,
            Options,
            Hosting,
            HealthChecks,
            HttpClient
        ];

        public static IntegrationBuckets Create() => new()
        {
            Aspire = new IntegrationBucket("Aspire", "Aspire"),
            AIChat = new IntegrationBucket("AI", "Chat"),
            AIEmbeddings = new IntegrationBucket("AI", "Embeddings"),
            AIImages = new IntegrationBucket("AI", "Images"),
            AIRealtime = new IntegrationBucket("AI", "Realtime"),
            AIHosting = new IntegrationBucket("AI", "Hosting"),
            AISpeechToText = new IntegrationBucket("AI", "Speech to Text"),
            AITextToSpeech = new IntegrationBucket("AI", "Text to Speech"),
            AITools = new IntegrationBucket("AI", "Tools"),
            AIHostedFiles = new IntegrationBucket("AI", "Hosted Files"),
            AIBuilder = new IntegrationBucket("AI", "Builder"),
            AIConfiguration = new IntegrationBucket("AI", "Configuration"),
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
        public bool HasAspireSupport { get; set; }
        public bool HasAISupport { get; set; }
        public bool HasOpenTelemetrySupport { get; init; }
        public bool HasDependencyInjectionSupport { get; set; }
        public bool HasLoggingSupport { get; set; }
        public bool HasOptionsSupport { get; set; }
        public bool HasHostingSupport { get; set; }
        public bool HasHealthChecksSupport { get; set; }
        public bool HasHttpClientSupport { get; set; }

        public EcosystemIntegrationPresence ToImmutable() => new()
        {
            HasAspireSupport = HasAspireSupport,
            HasAISupport = HasAISupport,
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
