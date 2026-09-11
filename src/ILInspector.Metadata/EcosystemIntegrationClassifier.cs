using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Pure ecosystem policy over metadata type names and decoded extension-method
/// signatures. It owns no traversal, evidence projection, or output ordering.
/// </summary>
internal static class EcosystemIntegrationClassifier
{
    internal static ImmutableArray<EcosystemIntegrationClassification> ClassifyAspire(
        EcosystemIntegrationObservationContext context)
    {
        var classifications =
            ImmutableArray.CreateBuilder<EcosystemIntegrationClassification>();
        foreach (EcosystemIntegrationTypeObservation type in context.Types)
        {
            if (TryGetAspireKind(type.MetadataName, out string kind))
                classifications.Add(type.Classify(IntegrationConceptCatalog.Aspire, kind));
        }
        foreach (EcosystemIntegrationMethodObservation method in context.StarterMethods)
        {
            if (TryClassifyAspireStarterMethod(
                    method.DeclaringType.MetadataName,
                    method.Name,
                    method.Signature,
                    out string kind))
            {
                classifications.Add(method.Classify(IntegrationConceptCatalog.Aspire, kind));
            }
        }
        return classifications.ToImmutable();
    }

    internal static ImmutableArray<EcosystemIntegrationClassification> Classify(
        EcosystemIntegrationObservationContext context)
    {
        var classifications =
            ImmutableArray.CreateBuilder<EcosystemIntegrationClassification>();
        foreach (EcosystemIntegrationTypeObservation type in context.Types)
            ClassifyType(type, classifications);
        foreach (EcosystemIntegrationMethodObservation method in context.StarterMethods)
            ClassifyStarterMethod(method, classifications);
        return classifications.ToImmutable();
    }

    static void ClassifyType(
        EcosystemIntegrationTypeObservation type,
        ImmutableArray<EcosystemIntegrationClassification>.Builder classifications)
    {
        string typeName = type.MetadataName;
        if (TryGetAspNetCoreKind(typeName, out var aspNetCoreKind))
            classifications.Add(type.Classify(IntegrationConceptCatalog.AspNetCore, aspNetCoreKind));
        if (TryGetAspireKind(typeName, out var aspireKind))
            classifications.Add(type.Classify(IntegrationConceptCatalog.Aspire, aspireKind));
        if (TryGetAIKind(typeName, out var aiKind))
            classifications.Add(type.Classify(IntegrationConceptCatalog.AI, aiKind));
        if (TryGetAuthenticationKind(typeName, out var authenticationKind))
            classifications.Add(type.Classify(IntegrationConceptCatalog.Authentication, authenticationKind));
        if (TryGetConfigurationKind(typeName, out var configurationKind))
            classifications.Add(type.Classify(IntegrationConceptCatalog.Configuration, configurationKind));
        if (TryGetOpenApiKind(typeName, out var openApiKind))
            classifications.Add(type.Classify(IntegrationConceptCatalog.OpenAPI, openApiKind));
        if (TryGetDependencyInjectionKind(typeName, out var dependencyInjectionKind))
            classifications.Add(type.Classify(IntegrationConceptCatalog.DependencyInjection, dependencyInjectionKind));
        if (IsLoggingType(typeName))
            classifications.Add(type.Classify(IntegrationConceptCatalog.Logging, "Logging"));
        if (IsOptionsType(typeName))
            classifications.Add(type.Classify(IntegrationConceptCatalog.Options, "Options"));
        if (IsHostingType(typeName))
            classifications.Add(type.Classify(IntegrationConceptCatalog.Hosting, "Hosting"));
        if (IsHealthChecksType(typeName))
            classifications.Add(type.Classify(IntegrationConceptCatalog.HealthChecks, "Health Check"));
        if (TryGetHttpClientKind(typeName, out var httpClientKind))
            classifications.Add(type.Classify(IntegrationConceptCatalog.HttpClient, httpClientKind));
    }

    static void ClassifyStarterMethod(
        EcosystemIntegrationMethodObservation method,
        ImmutableArray<EcosystemIntegrationClassification>.Builder classifications)
    {
        string typeName = method.DeclaringType.MetadataName;
        string methodName = method.Name;
        MethodSignature<string> signature = method.Signature;
        if (TryClassifyAspireStarterMethod(typeName, methodName, signature, out var aspireKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.Aspire, aspireKind));
        if (TryClassifyAIStarterMethod(typeName, methodName, signature, out var aiKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.AI, aiKind));
        if (TryClassifyAuthenticationStarterMethod(methodName, signature, out var authenticationKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.Authentication, authenticationKind));
        if (TryClassifyConfigurationStarterMethod(methodName, signature, out var configurationKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.Configuration, configurationKind));
        if (TryClassifyDependencyInjectionStarterMethod(typeName, methodName, signature, out var dependencyInjectionKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.DependencyInjection, dependencyInjectionKind));
        if (TryClassifyLoggingStarterMethod(typeName, methodName, signature, out var loggingKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.Logging, loggingKind));
        if (TryClassifyOpenApiStarterMethod(typeName, methodName, signature, out var openApiKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.OpenAPI, openApiKind));
        if (TryClassifyOptionsStarterMethod(methodName, signature, out var optionsKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.Options, optionsKind));
        if (TryClassifyAspNetCoreStarterMethod(methodName, signature, out var aspNetCoreKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.AspNetCore, aspNetCoreKind));
        if (TryClassifyHealthChecksStarterMethod(methodName, signature, out var healthChecksKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.HealthChecks, healthChecksKind));
        if (TryClassifyHostingStarterMethod(typeName, methodName, signature, out var hostingKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.Hosting, hostingKind));
        if (TryClassifyHttpClientStarterMethod(typeName, methodName, signature, out var httpClientKind))
            classifications.Add(method.Classify(IntegrationConceptCatalog.HttpClient, httpClientKind));
    }

    internal static bool IsDependencyInjectionType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.DependencyInjection.", StringComparison.Ordinal);

    internal static bool TryGetDependencyInjectionKind(string typeName, out string kind)
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

    internal static bool IsLoggingType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Logging.", StringComparison.Ordinal);

    internal static bool IsConfigurationType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Configuration.", StringComparison.Ordinal)
           || typeName.Contains(".Configuration.", StringComparison.Ordinal);

    internal static bool TryGetConfigurationKind(string typeName, out string kind)
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

    internal static bool IsOptionsType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Options.", StringComparison.Ordinal);

    internal static bool IsHostingType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Hosting.", StringComparison.Ordinal);

    internal static bool IsHealthChecksType(string typeName)
        => typeName.StartsWith("Microsoft.Extensions.Diagnostics.HealthChecks.", StringComparison.Ordinal);

    internal static bool IsAspNetCoreType(string typeName)
        => typeName.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
           || typeName.Contains(".AspNetCore.", StringComparison.Ordinal);

    internal static bool TryGetAspNetCoreKind(string typeName, out string kind)
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

    internal static bool IsAuthenticationType(string typeName)
        => typeName.StartsWith("Microsoft.AspNetCore.Authentication.", StringComparison.Ordinal)
           || typeName.StartsWith("Microsoft.AspNetCore.Authorization.", StringComparison.Ordinal)
           || typeName.StartsWith("Microsoft.AspNetCore.Components.Authorization.", StringComparison.Ordinal)
           || typeName.StartsWith("OpenIddict.Validation.AspNetCore.", StringComparison.Ordinal)
           || typeName.StartsWith("HotChocolate.Authorization.", StringComparison.Ordinal)
           || typeName.StartsWith("GraphQL.Authorization.", StringComparison.Ordinal);

    internal static bool TryGetAuthenticationKind(string typeName, out string kind)
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

    internal static bool IsOpenApiType(string typeName)
        => typeName.Contains("OpenApi", StringComparison.Ordinal)
           || typeName.Contains("OpenAPI", StringComparison.Ordinal)
           || typeName.Contains("Swagger", StringComparison.Ordinal);

    internal static bool TryGetOpenApiKind(string typeName, out string kind)
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

    internal static bool IsHttpClientType(string typeName) => TryGetHttpClientKind(typeName, out _);

    internal static bool TryGetHttpClientKind(string typeName, out string kind)
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

    internal static bool TryClassifyAIAdapterReturnType(string returnType, out string kind)
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

    internal static bool IsAspireType(string typeName) => TryGetAspireKind(typeName, out _);

    internal static bool TryGetAspireKind(string typeName, out string kind)
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

    internal static bool IsAIType(string typeName) => TryGetAIKind(typeName, out _);

    internal static bool TryGetAIKind(string typeName, out string kind)
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

    internal static bool TryClassifyDependencyInjectionStarterMethod(
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

    internal static bool TryClassifyLoggingStarterMethod(
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

    internal static bool TryClassifyConfigurationStarterMethod(
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

    internal static bool TryClassifyAuthenticationStarterMethod(
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

    internal static bool TryClassifyOpenApiStarterMethod(
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

    internal static bool TryClassifyOptionsStarterMethod(
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

    internal static bool TryClassifyAspNetCoreStarterMethod(
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

    internal static bool TryClassifyHealthChecksStarterMethod(
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

    internal static bool TryClassifyAspireStarterMethod(
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

    internal static bool TryClassifyHostingStarterMethod(
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

    internal static bool TryClassifyHttpClientStarterMethod(
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

    internal static bool TryClassifyAIStarterMethod(
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

}
