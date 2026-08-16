using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

public sealed record IntegrationOpportunityTarget
{
    public IntegrationOpportunityTarget(
        string assemblyName,
        MetadataTypeDefinitionName type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(type);
        AssemblyName = assemblyName;
        Type = type;
    }

    public string AssemblyName { get; }
    public MetadataTypeDefinitionName Type { get; }
}

public record IntegrationOpportunityInfo(
    string Integration,
    string Api,
    string IntegrationType,
    string LookFor)
{
    internal MetadataTypeDefinitionName? SourceTypeDefinition { get; init; }
    internal IntegrationOpportunityTarget? Target { get; init; }

    public MetadataTypeDefinitionName? GetSourceTypeDefinition() =>
        SourceTypeDefinition;

    public IntegrationOpportunityTarget? GetTarget() => Target;

    // Preserve the original four-field row contract. Structured source and
    // target evidence is derived from the same policy and metadata.
    public virtual bool Equals(IntegrationOpportunityInfo? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && EqualityContract == other.EqualityContract
        && string.Equals(
            Integration,
            other.Integration,
            StringComparison.Ordinal)
        && string.Equals(Api, other.Api, StringComparison.Ordinal)
        && string.Equals(
            IntegrationType,
            other.IntegrationType,
            StringComparison.Ordinal)
        && string.Equals(LookFor, other.LookFor, StringComparison.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(
            EqualityContract,
            Integration,
            Api,
            IntegrationType,
            LookFor);
}

public static class IntegrationOpportunityScanner
{
    public static List<IntegrationOpportunityInfo> Scan(PEReader peReader, IReadOnlySet<string> existingIntegrations)
    {
        if (!peReader.HasMetadata)
            return [];

        var reader = peReader.GetMetadataReader();
        Dictionary<string, IntegrationOpportunityInfo> gaps = new(StringComparer.Ordinal);

        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDefinition = reader.GetTypeDefinition(handle);
            if (!typeDefinition.IsPublic)
                continue;

            var typeName = reader.GetFullTypeName(typeDefinition);
            var simpleName = TypeMatcher.GetSimpleName(typeName);
            MetadataTypeDefinitionName? sourceType =
                MetadataTypeDefinitionNameReader.Read(reader, handle)
                    is MetadataTypeDefinitionNameReadResult.Read read
                        ? read.Name
                        : null;

            AddAuthDomainGaps(
                gaps,
                existingIntegrations,
                typeName,
                simpleName,
                sourceType);
            AddCloudClientGaps(
                gaps,
                existingIntegrations,
                typeName,
                simpleName,
                sourceType);
            AddConfigurationGaps(
                gaps,
                existingIntegrations,
                typeName,
                simpleName,
                sourceType);
            AddDatabaseGaps(
                gaps,
                existingIntegrations,
                typeName,
                simpleName,
                sourceType);
            AddAiClientGaps(
                gaps,
                existingIntegrations,
                typeName,
                simpleName,
                sourceType);
        }

        return gaps.Values
            .OrderBy(g => g.Integration, StringComparer.Ordinal)
            .ThenBy(g => g.Api, StringComparer.Ordinal)
            .ThenBy(g => g.IntegrationType, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddAuthDomainGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<string> existingIntegrations,
        string typeName,
        string simpleName,
        MetadataTypeDefinitionName? sourceType)
    {
        if (Has(existingIntegrations, EcosystemIntegrationNames.Authentication))
            return;

        if (!simpleName.Contains("Cognito", StringComparison.Ordinal)
            && !simpleName.Contains("UserPool", StringComparison.Ordinal)
            && !simpleName.Contains("UserSession", StringComparison.Ordinal))
            return;

        AddGap(gaps,
            integration: EcosystemIntegrationNames.Authentication,
            api: typeName,
            integrationType: "Authentication/Identity registration",
            lookFor: "AuthenticationBuilder, Add*Identity*, Add*Cognito*",
            sourceType);
    }

    private static void AddCloudClientGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<string> existingIntegrations,
        string typeName,
        string simpleName,
        MetadataTypeDefinitionName? sourceType)
    {
        var cloudClient = (typeName.StartsWith("Amazon.", StringComparison.Ordinal)
                           || typeName.StartsWith("Azure.", StringComparison.Ordinal))
                          && simpleName.EndsWith("Client", StringComparison.Ordinal);
        if (!cloudClient)
            return;

        if (!Has(existingIntegrations, EcosystemIntegrationNames.DependencyInjection))
        {
            AddGap(gaps,
                integration: EcosystemIntegrationNames.DependencyInjection,
                api: typeName,
                integrationType: "IServiceCollection registration",
                lookFor: "IServiceCollection, Add*",
                sourceType);
        }

        if (!Has(existingIntegrations, EcosystemIntegrationNames.Aspire))
        {
            AddGap(gaps,
                integration: EcosystemIntegrationNames.Aspire,
                api: typeName,
                integrationType: "AppHost resource builder",
                lookFor: "IResourceBuilder<T>, Add*, *Resource",
                sourceType);
        }
    }

    private static void AddConfigurationGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<string> existingIntegrations,
        string typeName,
        string simpleName,
        MetadataTypeDefinitionName? sourceType)
    {
        if (Has(existingIntegrations, EcosystemIntegrationNames.Configuration))
            return;

        if (typeName.StartsWith("Azure.Data.AppConfiguration.", StringComparison.Ordinal)
            && simpleName == "ConfigurationClient")
        {
            AddGap(gaps,
                integration: EcosystemIntegrationNames.Configuration,
                api: typeName,
                integrationType: "IConfigurationBuilder source",
                lookFor: "IConfigurationBuilder, AddAzureAppConfiguration",
                sourceType);
        }
    }

    private static void AddDatabaseGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<string> existingIntegrations,
        string typeName,
        string simpleName,
        MetadataTypeDefinitionName? sourceType)
    {
        var databaseShape = typeName is "Npgsql.NpgsqlConnection"
            or "Microsoft.Data.SqlClient.SqlConnection"
            or "System.Data.SqlClient.SqlConnection"
            || typeName == "StackExchange.Redis.ConnectionMultiplexer"
            || simpleName.EndsWith("DataSource", StringComparison.Ordinal);

        if (!databaseShape)
            return;

        if (!Has(existingIntegrations, EcosystemIntegrationNames.HealthChecks))
        {
            AddGap(gaps,
                integration: EcosystemIntegrationNames.HealthChecks,
                api: typeName,
                integrationType: "IHealthChecksBuilder registration",
                lookFor: "IHealthChecksBuilder, Add*",
                sourceType);
        }

        if (!Has(existingIntegrations, EcosystemIntegrationNames.Aspire))
        {
            AddGap(gaps,
                integration: EcosystemIntegrationNames.Aspire,
                api: typeName,
                integrationType: "AppHost resource builder",
                lookFor: "IResourceBuilder<T>, Add*, *Resource",
                sourceType);
        }
    }

    private static void AddAiClientGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<string> existingIntegrations,
        string typeName,
        string simpleName,
        MetadataTypeDefinitionName? sourceType)
    {
        if (Has(existingIntegrations, EcosystemIntegrationNames.AI))
            return;

        var aiClient = (typeName.StartsWith("OpenAI.", StringComparison.Ordinal)
                        || typeName.StartsWith("Azure.AI.", StringComparison.Ordinal))
                       && (simpleName.EndsWith("Client", StringComparison.Ordinal)
                           || simpleName.Contains("Chat", StringComparison.Ordinal)
                           || simpleName.Contains("Embedding", StringComparison.Ordinal));

        if (!aiClient)
            return;

        AddGap(gaps,
            integration: EcosystemIntegrationNames.AI,
            api: typeName,
            integrationType: "Microsoft.Extensions.AI adapter",
            lookFor: "IChatClient, AsIChatClient, IEmbeddingGenerator",
            sourceType,
            IChatClientTarget());
    }

    private static bool Has(IReadOnlySet<string> integrations, string integration)
        => integrations.Contains(integration);

    private static void AddGap(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        string integration,
        string api,
        string integrationType,
        string lookFor,
        MetadataTypeDefinitionName? sourceType,
        IntegrationOpportunityTarget? target = null)
    {
        var key = $"{integration}\0{integrationType}";
        var formattedApi = TypeResolver.FormatDisplayName(api);
        if (gaps.TryGetValue(key, out var existing)
            && GetEvidenceRank(existing.Api) <= GetEvidenceRank(formattedApi))
            return;

        gaps[key] = new IntegrationOpportunityInfo(
            integration,
            formattedApi,
            integrationType,
            lookFor)
        {
            SourceTypeDefinition = sourceType,
            Target = target,
        };
    }

    static IntegrationOpportunityTarget? IChatClientTarget() =>
        MetadataTypeDefinitionName.Create(
            "Microsoft.Extensions.AI",
            ["IChatClient"])
            is MetadataTypeDefinitionNameResult.Valid valid
                ? new IntegrationOpportunityTarget(
                    "Microsoft.Extensions.AI.Abstractions",
                    valid.Name)
                : null;

    private static int GetEvidenceRank(string evidence)
    {
        var simpleName = TypeMatcher.GetSimpleName(evidence);
        return simpleName switch
        {
            "CognitoUser" => 0,
            "CognitoUserPool" => 1,
            "CognitoUserSession" => 2,
            _ when simpleName.EndsWith("Client", StringComparison.Ordinal) => 3,
            _ when simpleName.EndsWith("Connection", StringComparison.Ordinal) => 3,
            _ when simpleName.EndsWith("DataSource", StringComparison.Ordinal) => 3,
            _ => 10
        };
    }
}
