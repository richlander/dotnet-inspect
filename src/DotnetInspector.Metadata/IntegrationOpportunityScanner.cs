using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

public record IntegrationOpportunityInfo(
    string Integration,
    string Api,
    string IntegrationType,
    string LookFor);

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
            var simpleName = typeName[(typeName.LastIndexOf('.') + 1)..];

            AddAuthDomainGaps(gaps, existingIntegrations, typeName, simpleName);
            AddCloudClientGaps(gaps, existingIntegrations, typeName, simpleName);
            AddDatabaseGaps(gaps, existingIntegrations, typeName, simpleName);
            AddAiClientGaps(gaps, existingIntegrations, typeName, simpleName);
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
        string simpleName)
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
            lookFor: "AuthenticationBuilder, Add*Identity*, Add*Cognito*");
    }

    private static void AddCloudClientGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<string> existingIntegrations,
        string typeName,
        string simpleName)
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
                lookFor: "IServiceCollection, Add*");
        }

        if (!Has(existingIntegrations, EcosystemIntegrationNames.Aspire))
        {
            AddGap(gaps,
                integration: EcosystemIntegrationNames.Aspire,
                api: typeName,
                integrationType: "AppHost resource builder",
                lookFor: "IResourceBuilder<T>, Add*, *Resource");
        }
    }

    private static void AddDatabaseGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<string> existingIntegrations,
        string typeName,
        string simpleName)
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
                lookFor: "IHealthChecksBuilder, Add*");
        }

        if (!Has(existingIntegrations, EcosystemIntegrationNames.Aspire))
        {
            AddGap(gaps,
                integration: EcosystemIntegrationNames.Aspire,
                api: typeName,
                integrationType: "AppHost resource builder",
                lookFor: "IResourceBuilder<T>, Add*, *Resource");
        }
    }

    private static void AddAiClientGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<string> existingIntegrations,
        string typeName,
        string simpleName)
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
            lookFor: "IChatClient, AsIChatClient, IEmbeddingGenerator");
    }

    private static bool Has(IReadOnlySet<string> integrations, string integration)
        => integrations.Contains(integration);

    private static void AddGap(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        string integration,
        string api,
        string integrationType,
        string lookFor)
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
            lookFor);
    }

    private static int GetEvidenceRank(string evidence)
    {
        var simpleName = evidence[(evidence.LastIndexOf('.') + 1)..];
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
