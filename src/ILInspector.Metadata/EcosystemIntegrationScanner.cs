using System.Reflection;
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

public static class EcosystemIntegrationScanner
{
    public static List<EcosystemIntegrationSignalInfo> Scan(PEReader peReader)
    {
        if (!peReader.HasMetadata)
            return [];

        return Scan(peReader.GetMetadataReader());
    }

    private static List<EcosystemIntegrationSignalInfo> Scan(MetadataReader reader)
    {
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
        var openTelemetrySupport = OpenTelemetryScanner.HasSupport(reader);
        var presence = new MutablePresence
        {
            HasOpenTelemetrySupport = openTelemetrySupport
        };

        var signals = Scan(reader);
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

    private static void AddType(IntegrationBuckets buckets, string typeName, string source)
    {
        if (source == "TypeDef" && TryGetAspNetCoreKind(typeName, out var aspNetCoreKind))
            AddType(buckets.AspNetCore, typeName, source, aspNetCoreKind);
        if (source == "TypeDef" && TryGetAspireKind(typeName, out var aspireKind))
            AddType(buckets.Aspire, typeName, source, aspireKind);
        if (source == "TypeDef" && TryGetAIKind(typeName, out var aiKind))
            AddType(GetAIBucket(buckets, aiKind), typeName, source);
        if (source == "TypeDef" && TryGetAuthenticationKind(typeName, out var authenticationKind))
            AddType(buckets.Authentication, typeName, source, authenticationKind);
        if (source == "TypeDef" && TryGetConfigurationKind(typeName, out var configurationKind))
            AddType(buckets.Configuration, typeName, source, configurationKind);
        if (source == "TypeDef" && TryGetOpenApiKind(typeName, out var openApiKind))
            AddType(buckets.OpenApi, typeName, source, openApiKind);
        if (TryGetDependencyInjectionKind(typeName, out var dependencyInjectionKind))
            AddType(buckets.DependencyInjection, typeName, source, dependencyInjectionKind);
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
        if (IsAspNetCoreType(typeName))
            presence.HasAspNetCoreSupport = true;
        if (IsAspireType(typeName))
            presence.HasAspireSupport = true;
        if (IsAIType(typeName))
            presence.HasAISupport = true;
        if (IsAuthenticationType(typeName))
            presence.HasAuthenticationSupport = true;
        if (TryGetConfigurationKind(typeName, out _))
            presence.HasConfigurationSupport = true;
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
        if (IsOpenApiType(typeName))
            presence.HasOpenApiSupport = true;
    }

    private static bool IsDependencyInjectionType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.DependencyInjection.", StringComparison.Ordinal);

    private static bool TryGetDependencyInjectionKind(string typeName, out string kind)
    {
        kind = "";
        if (typeName is "Microsoft.Extensions.DependencyInjection.IServiceCollection"
            or "Microsoft.Extensions.DependencyInjection.IServiceProviderFactory`1"
            or "Microsoft.Extensions.DependencyInjection.IServiceScope"
            or "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"
            or "Microsoft.Extensions.DependencyInjection.ServiceDescriptor"
            or "Microsoft.Extensions.DependencyInjection.ServiceLifetime")
        {
            kind = "Dependency Injection";
            return true;
        }

        return false;
    }

    private static bool IsLoggingType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Logging.", StringComparison.Ordinal);

    private static bool IsConfigurationType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Configuration.", StringComparison.Ordinal)
           || typeName.Contains(".Configuration.", StringComparison.Ordinal);

    private static bool TryGetConfigurationKind(string typeName, out string kind)
    {
        kind = "";
        if (!IsConfigurationType(typeName))
            return false;

        var simpleName = TypeMatcher.GetSimpleName(typeName);
        if (simpleName.EndsWith("ConfigurationProvider", StringComparison.Ordinal))
            kind = "Provider";
        else if (simpleName.EndsWith("ConfigurationSource", StringComparison.Ordinal))
            kind = "Source";
        else if (simpleName.EndsWith("ConfigurationOptions", StringComparison.Ordinal)
                 || simpleName.EndsWith("Options", StringComparison.Ordinal))
            kind = "Configuration";
        else if (simpleName.EndsWith("SecretManager", StringComparison.Ordinal))
            kind = "Configuration";

        return kind.Length > 0;
    }

    private static bool IsOptionsType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Options.", StringComparison.Ordinal);

    private static bool IsHostingType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Hosting.", StringComparison.Ordinal);

    private static bool IsHealthChecksType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Diagnostics.HealthChecks.", StringComparison.Ordinal);

    private static bool IsAspNetCoreType(string typeName)
        => typeName.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
           || typeName.Contains(".AspNetCore.", StringComparison.Ordinal);

    private static bool TryGetAspNetCoreKind(string typeName, out string kind)
    {
        kind = "";
        if (!IsAspNetCoreType(typeName))
            return false;

        var simpleName = TypeMatcher.GetSimpleName(typeName);
        if (simpleName.EndsWith("Options", StringComparison.Ordinal)
            || simpleName.EndsWith("Settings", StringComparison.Ordinal))
        {
            kind = "Configuration";
            return true;
        }

        if (simpleName.Contains("EndpointConventionBuilder", StringComparison.Ordinal))
        {
            kind = "Endpoint";
            return true;
        }

        return false;
    }

    private static bool IsAuthenticationType(string typeName)
        => typeName.StartsWith("Microsoft.AspNetCore.Authentication.", StringComparison.Ordinal)
           || typeName.StartsWith("Microsoft.AspNetCore.Authorization.", StringComparison.Ordinal)
           || typeName.StartsWith("Microsoft.AspNetCore.Components.Authorization.", StringComparison.Ordinal)
           || typeName.StartsWith("OpenIddict.Validation.AspNetCore.", StringComparison.Ordinal)
           || typeName.StartsWith("HotChocolate.Authorization.", StringComparison.Ordinal)
           || typeName.StartsWith("GraphQL.Authorization.", StringComparison.Ordinal);

    private static bool TryGetAuthenticationKind(string typeName, out string kind)
    {
        kind = "";
        if (!IsAuthenticationType(typeName))
            return false;

        var simpleName = TypeMatcher.GetSimpleName(typeName);
        if (simpleName.EndsWith("Options", StringComparison.Ordinal)
            || simpleName.EndsWith("Events", StringComparison.Ordinal)
            || simpleName.EndsWith("Defaults", StringComparison.Ordinal)
            || simpleName.EndsWith("ChallengeProperties", StringComparison.Ordinal))
        {
            kind = "Configuration";
            return true;
        }

        if (typeName.StartsWith("Microsoft.AspNetCore.Components.Authorization.", StringComparison.Ordinal))
        {
            kind = simpleName.Contains("Authorize", StringComparison.Ordinal) ? "Authorization UI" : "Authentication State";
            return true;
        }

        if (typeName.StartsWith("OpenIddict.Validation.AspNetCore.", StringComparison.Ordinal)
            && simpleName.Contains("Validation", StringComparison.Ordinal))
        {
            kind = "Validation";
            return true;
        }

        if (typeName.StartsWith("HotChocolate.Authorization.", StringComparison.Ordinal)
            || typeName.StartsWith("GraphQL.Authorization.", StringComparison.Ordinal))
        {
            if (simpleName.EndsWith("Requirement", StringComparison.Ordinal))
                kind = "Requirement";
            else if (simpleName.Contains("Policy", StringComparison.Ordinal))
                kind = "Policy";
            else if (simpleName.EndsWith("ValidationRule", StringComparison.Ordinal))
                kind = "Validation";
            else if (simpleName.EndsWith("Handler", StringComparison.Ordinal))
                kind = "Handler";
            else if (simpleName.EndsWith("Attribute", StringComparison.Ordinal)
                     || simpleName.EndsWith("Directive", StringComparison.Ordinal))
                kind = "Annotation";
            else if (simpleName.EndsWith("Options", StringComparison.Ordinal)
                     || simpleName.EndsWith("Settings", StringComparison.Ordinal))
                kind = "Configuration";

            if (kind.Length > 0)
                return true;
        }

        if (simpleName is "AuthorizationBuilder" or "AuthorizationPolicyBuilder")
        {
            kind = "Builder";
            return true;
        }

        return false;
    }

    private static bool IsOpenApiType(string typeName)
        => typeName.Contains("OpenApi", StringComparison.Ordinal)
           || typeName.Contains("OpenAPI", StringComparison.Ordinal)
           || typeName.Contains("Swagger", StringComparison.Ordinal);

    private static bool TryGetOpenApiKind(string typeName, out string kind)
    {
        kind = "";
        if (!IsOpenApiType(typeName))
            return false;

        var simpleName = TypeMatcher.GetSimpleName(typeName);
        if (simpleName.EndsWith("Options", StringComparison.Ordinal)
            || simpleName.EndsWith("Settings", StringComparison.Ordinal))
        {
            kind = "Configuration";
            return true;
        }

        if (simpleName.EndsWith("Attribute", StringComparison.Ordinal))
        {
            kind = "Annotation";
            return true;
        }

        if (simpleName.EndsWith("Filter", StringComparison.Ordinal))
        {
            kind = "Filter";
            return true;
        }

        return false;
    }

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
            var simpleName = TypeMatcher.GetSimpleName(typeName);
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
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                || (method.Attributes & MethodAttributes.Static) == 0
                || !AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes()))
                continue;

            var methodName = reader.GetString(method.Name);
            var context = GenericContext.ForMethod(reader, typeDefinition, method);
            MethodSignature<string> signature;
            try
            {
                signature = method.DecodeSignature(SignatureDecoder.Instance, context);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            var api = $"{TypeResolver.FormatDisplayName(typeName)}.{methodName}(...)";
            if (TryClassifyAspireStarterMethod(typeName, methodName, signature, out var aspireKind))
                buckets.Aspire.Apis.TryAdd(api, aspireKind);
            if (TryClassifyAIStarterMethod(typeName, methodName, signature, out var aiKind))
                GetAIBucket(buckets, aiKind).Apis.TryAdd(api, aiKind);
            if (TryClassifyAuthenticationStarterMethod(methodName, signature, out var authenticationKind))
                buckets.Authentication.Apis.TryAdd(api, authenticationKind);
            if (TryClassifyConfigurationStarterMethod(methodName, signature, out var configurationKind))
                buckets.Configuration.Apis.TryAdd(api, configurationKind);
            if (TryClassifyDependencyInjectionStarterMethod(typeName, methodName, signature, out var dependencyInjectionKind))
                buckets.DependencyInjection.Apis.TryAdd(api, dependencyInjectionKind);
            if (TryClassifyLoggingStarterMethod(typeName, methodName, signature, out var loggingKind))
                buckets.Logging.Apis.TryAdd(api, loggingKind);
            if (TryClassifyOpenApiStarterMethod(typeName, methodName, signature, out var openApiKind))
                buckets.OpenApi.Apis.TryAdd(api, openApiKind);
            if (TryClassifyOptionsStarterMethod(methodName, signature, out var optionsKind))
                buckets.Options.Apis.TryAdd(api, optionsKind);
            if (TryClassifyAspNetCoreStarterMethod(methodName, signature, out var aspNetCoreKind))
                buckets.AspNetCore.Apis.TryAdd(api, aspNetCoreKind);
            if (TryClassifyHealthChecksStarterMethod(methodName, signature, out var healthChecksKind))
                buckets.HealthChecks.Apis.TryAdd(api, healthChecksKind);
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
        if (signature.ParameterTypes.Length == 0
            || signature.ParameterTypes[0] != "Microsoft.Extensions.DependencyInjection.IServiceCollection")
            return false;

        if (methodName.StartsWith("Add", StringComparison.Ordinal))
            kind = "Service Registration";
        else if (methodName == "Scan")
            kind = "Assembly Scanning";
        else if (methodName is "Decorate" or "TryDecorate")
            kind = "Decoration";

        return kind.Length > 0;
    }

    private static bool TryClassifyLoggingStarterMethod(
        string declaringType,
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (!methodName.StartsWith("Add", StringComparison.Ordinal)
            || signature.ParameterTypes.Length == 0
            || signature.ParameterTypes[0] is not ("Microsoft.Extensions.Logging.ILoggingBuilder" or "Microsoft.Extensions.Logging.ILoggerFactory"))
            return false;

        kind = "Provider";
        return true;
    }

    private static bool TryClassifyConfigurationStarterMethod(
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (!methodName.StartsWith("Add", StringComparison.Ordinal)
            || signature.ParameterTypes.Length == 0
            || signature.ParameterTypes[0] != "Microsoft.Extensions.Configuration.IConfigurationBuilder"
            || signature.ReturnType != "Microsoft.Extensions.Configuration.IConfigurationBuilder")
        {
            if (signature.ParameterTypes.Length == 0)
                return false;

            var receiver = signature.ParameterTypes[0];
            if (receiver == "Microsoft.Extensions.Configuration.IConfiguration"
                && methodName is "Bind" or "Get" or "GetValue")
            {
                kind = "Binding";
                return true;
            }

            if (receiver.StartsWith("Microsoft.Extensions.Options.OptionsBuilder<", StringComparison.Ordinal)
                && methodName is "Bind" or "BindConfiguration")
            {
                kind = "Options Binding";
                return true;
            }

            if (receiver == "Microsoft.Extensions.DependencyInjection.IServiceCollection"
                && methodName == "Configure"
                && signature.ParameterTypes.Any(type => type == "Microsoft.Extensions.Configuration.IConfiguration"))
            {
                kind = "Options Binding";
                return true;
            }

            if (methodName is "Configuration" or "AddConfiguration"
                && signature.ParameterTypes.Any(type => type == "Microsoft.Extensions.Configuration.IConfiguration"))
            {
                kind = "Configuration Consumer";
                return true;
            }

            return false;
        }

        kind = "Configuration Source";
        return true;
    }

    private static bool TryClassifyAuthenticationStarterMethod(
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (signature.ParameterTypes.Length == 0)
            return false;

        var receiver = signature.ParameterTypes[0];
        if (receiver == "Microsoft.Extensions.DependencyInjection.IServiceCollection"
            && methodName == "AddCascadingAuthenticationState")
        {
            kind = "Authentication State";
            return true;
        }

        if (receiver == "Microsoft.AspNetCore.Authentication.AuthenticationBuilder"
            && methodName.StartsWith("Add", StringComparison.Ordinal))
        {
            kind = "Authentication";
            return true;
        }

        if (receiver == "Microsoft.Extensions.DependencyInjection.IServiceCollection"
            && methodName.StartsWith("AddAuthentication", StringComparison.Ordinal))
        {
            kind = "Authentication";
            return true;
        }

        if (receiver == "Microsoft.Extensions.DependencyInjection.IServiceCollection"
            && methodName.StartsWith("AddAuthorization", StringComparison.Ordinal))
        {
            kind = "Authorization";
            return true;
        }

        if (receiver == "Microsoft.Extensions.DependencyInjection.IServiceCollection"
            && methodName.StartsWith("Add", StringComparison.Ordinal)
            && methodName.Contains("Identity", StringComparison.Ordinal)
            && (signature.ReturnType == "Microsoft.Extensions.DependencyInjection.IServiceCollection"
                || signature.ReturnType.EndsWith("IdentityBuilder", StringComparison.Ordinal)
                || signature.ReturnType.EndsWith("AuthenticationBuilder", StringComparison.Ordinal)))
        {
            kind = "Identity";
            return true;
        }

        if (receiver.Contains("OpenIddictValidationBuilder", StringComparison.Ordinal)
            && methodName == "UseAspNetCore")
        {
            kind = "Validation";
            return true;
        }

        if (receiver == "HotChocolate.Execution.Configuration.IRequestExecutorBuilder"
            && methodName.StartsWith("AddAuthorization", StringComparison.Ordinal))
        {
            kind = "Authorization";
            return true;
        }

        if (receiver == "GraphQL.DI.IGraphQLBuilder"
            && methodName is "AddAuthorization" or "AddGraphQLAuthorization")
        {
            kind = "Authorization";
            return true;
        }

        if (receiver == "Microsoft.AspNetCore.Builder.IApplicationBuilder"
            && methodName is "UseAuthentication" or "UseAuthorization")
        {
            kind = "Middleware";
            return true;
        }

        return false;
    }

    private static bool TryClassifyOpenApiStarterMethod(
        string declaringType,
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (signature.ParameterTypes.Length == 0
            || !IsOpenApiType($"{declaringType}.{methodName}"))
            return false;

        kind = (methodName, signature.ParameterTypes[0]) switch
        {
            ({ } name, "Microsoft.Extensions.DependencyInjection.IServiceCollection") when name.StartsWith("Add", StringComparison.Ordinal)
                => "Service Registration",
            ({ } name, "Microsoft.AspNetCore.Builder.IApplicationBuilder") when name.StartsWith("Use", StringComparison.Ordinal)
                => "Middleware",
            ({ } name, "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder") when name.StartsWith("Map", StringComparison.Ordinal)
                => "Endpoint",
            ({ } name, { } receiver) when name.StartsWith("Enable", StringComparison.Ordinal)
                                          && (receiver.Contains("Swagger", StringComparison.Ordinal)
                                              || receiver.Contains("OpenApi", StringComparison.Ordinal)
                                              || receiver.Contains("OpenAPI", StringComparison.Ordinal))
                => "Configuration",
            _ => ""
        };

        return kind.Length > 0;
    }

    private static bool TryClassifyOptionsStarterMethod(
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (signature.ParameterTypes.Length == 0)
            return false;

        var receiver = signature.ParameterTypes[0];
        if (receiver.StartsWith("Microsoft.Extensions.Options.OptionsBuilder<", StringComparison.Ordinal))
        {
            if (methodName.Contains("Validate", StringComparison.Ordinal))
                kind = "Validation";
            else if (methodName.StartsWith("Bind", StringComparison.Ordinal)
                     || methodName.StartsWith("Configure", StringComparison.Ordinal))
                kind = "Configuration";
        }
        else if (receiver == "Microsoft.Extensions.DependencyInjection.IServiceCollection")
        {
            if (methodName.Contains("Validate", StringComparison.Ordinal)
                && signature.ReturnType == "Microsoft.Extensions.DependencyInjection.IServiceCollection")
                kind = "Validation";
            else if (methodName == "Configure"
                     && signature.ParameterTypes.Any(type => type == "Microsoft.Extensions.Configuration.IConfiguration"))
                kind = "Configuration";
        }

        return kind.Length > 0;
    }

    private static bool TryClassifyAspNetCoreStarterMethod(
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (signature.ParameterTypes.Length == 0)
            return false;

        kind = (methodName, signature.ParameterTypes[0]) switch
        {
            ({ } name, "Microsoft.AspNetCore.Builder.IApplicationBuilder") when name.StartsWith("Use", StringComparison.Ordinal)
                => "Middleware",
            ({ } name, "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder") when name.StartsWith("Map", StringComparison.Ordinal)
                => "Endpoint",
            ({ } name, "Microsoft.AspNetCore.DataProtection.IDataProtectionBuilder") when signature.ReturnType == "Microsoft.AspNetCore.DataProtection.IDataProtectionBuilder"
                => "Data Protection",
            ({ } name, "Microsoft.Extensions.DependencyInjection.IServiceCollection") when name.StartsWith("AddDataProtection", StringComparison.Ordinal)
                                                                                    && signature.ReturnType == "Microsoft.AspNetCore.DataProtection.IDataProtectionBuilder"
                => "Data Protection",
            _ => ""
        };

        return kind.Length > 0;
    }

    private static bool TryClassifyHealthChecksStarterMethod(
        string methodName,
        MethodSignature<string> signature,
        out string kind)
    {
        kind = "";
        if (signature.ParameterTypes.Length == 0)
            return false;

        var receiver = signature.ParameterTypes[0];
        if (methodName.StartsWith("Add", StringComparison.Ordinal)
            && receiver == "Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder")
            kind = "Health Check";
        else if (methodName.StartsWith("UseHealthChecks", StringComparison.Ordinal)
                 && receiver == "Microsoft.AspNetCore.Builder.IApplicationBuilder")
            kind = "Middleware";
        else if (methodName.StartsWith("MapHealthChecks", StringComparison.Ordinal)
                 && receiver == "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder")
            kind = "Endpoint";

        return kind.Length > 0;
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
        if (signature.ParameterTypes.Length == 0)
            return false;

        if (methodName.StartsWith("Add", StringComparison.Ordinal)
            && signature.ParameterTypes[0] == "Microsoft.Extensions.Hosting.IHostApplicationBuilder")
        {
            kind = "Hosting";
            return true;
        }

        if (methodName.StartsWith("Use", StringComparison.Ordinal)
            && signature.ParameterTypes[0] == "Microsoft.Extensions.Hosting.IHostBuilder")
        {
            kind = "Hosting";
            return true;
        }

        if (methodName.Contains("HostedService", StringComparison.Ordinal)
            && signature.ParameterTypes[0] == "Microsoft.Extensions.DependencyInjection.IServiceCollection")
        {
            kind = "Hosted Service";
            return true;
        }

        return false;
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
        public Dictionary<string, string> Types { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Kinds { get; } = new(StringComparer.Ordinal);
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
