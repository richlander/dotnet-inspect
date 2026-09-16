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
    string _integration = Integration;
    IntegrationConceptDescriptor? _concept = ResolveConcept(Integration);

    public string Integration
    {
        get => _integration;
        init
        {
            _integration = value;
            _concept = ResolveConcept(value);
        }
    }
    public string Api { get; init; } = Api;
    public string IntegrationType { get; init; } = IntegrationType;
    public string LookFor { get; init; } = LookFor;

    internal IntegrationOpportunityInfo(
        IntegrationConceptDescriptor concept,
        string api,
        string integrationType,
        string lookFor)
        : this(concept.DisplayLabel, api, integrationType, lookFor)
    {
        _concept = concept;
    }

    internal MetadataTypeDefinitionName? SourceTypeDefinition { get; init; }
    internal IntegrationOpportunityTarget? Target { get; init; }

    public MetadataTypeDefinitionName? GetSourceTypeDefinition() =>
        SourceTypeDefinition;

    public IntegrationOpportunityTarget? GetTarget() => Target;

    public IntegrationConceptDescriptor? GetConcept() => _concept;

    public IntegrationProducerPolicyDescriptor? GetProducerPolicy()
    {
        IntegrationProducerPolicyDescriptor policy =
            IntegrationConceptCatalog.Opportunity;
        return _concept is not null
            && policy.Concepts.Contains(
                _concept,
                ReferenceEqualityComparer.Instance)
                ? policy
                : null;
    }

    static IntegrationConceptDescriptor? ResolveConcept(string? integration) =>
        integration is not null
        && IntegrationConceptCatalog.TryGetByDisplayLabel(
            integration,
            out IntegrationConceptDescriptor? concept)
                ? concept
                : null;

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
        ArgumentNullException.ThrowIfNull(existingIntegrations);
        HashSet<IntegrationConceptDescriptor> concepts = new(
            IntegrationConceptCatalog.Concepts.Where(concept =>
                existingIntegrations.Contains(concept.DisplayLabel)),
            ReferenceEqualityComparer.Instance);
        return Scan(peReader, concepts);
    }

    public static List<IntegrationOpportunityInfo> Scan(
        PEReader peReader,
        IReadOnlySet<IntegrationConceptDescriptor> existingIntegrations)
    {
        ArgumentNullException.ThrowIfNull(existingIntegrations);
        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return [];

        HashSet<IntegrationConceptDescriptor> exactExistingIntegrations = new(
            existingIntegrations,
            ReferenceEqualityComparer.Instance);
        var reader = MetadataFormatAdmission.GetMetadataReader(peReader);
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
                exactExistingIntegrations,
                typeName,
                simpleName,
                sourceType);
            AddCloudClientGaps(
                gaps,
                exactExistingIntegrations,
                typeName,
                simpleName,
                sourceType);
            AddConfigurationGaps(
                gaps,
                exactExistingIntegrations,
                typeName,
                simpleName,
                sourceType);
            AddDatabaseGaps(
                gaps,
                exactExistingIntegrations,
                typeName,
                simpleName,
                sourceType);
            AddAiClientGaps(
                gaps,
                exactExistingIntegrations,
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
        IReadOnlySet<IntegrationConceptDescriptor> existingIntegrations,
        string typeName,
        string simpleName,
        MetadataTypeDefinitionName? sourceType)
    {
        if (Has(existingIntegrations, IntegrationConceptCatalog.Authentication))
            return;

        if (!simpleName.Contains("Cognito", StringComparison.Ordinal)
            && !simpleName.Contains("UserPool", StringComparison.Ordinal)
            && !simpleName.Contains("UserSession", StringComparison.Ordinal))
            return;

        AddGap(gaps,
            concept: IntegrationConceptCatalog.Authentication,
            api: typeName,
            integrationType: "Authentication/Identity registration",
            lookFor: "AuthenticationBuilder, Add*Identity*, Add*Cognito*",
            sourceType);
    }

    private static void AddCloudClientGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<IntegrationConceptDescriptor> existingIntegrations,
        string typeName,
        string simpleName,
        MetadataTypeDefinitionName? sourceType)
    {
        var cloudClient = (typeName.StartsWith("Amazon.", StringComparison.Ordinal)
                           || typeName.StartsWith("Azure.", StringComparison.Ordinal))
                          && simpleName.EndsWith("Client", StringComparison.Ordinal);
        if (!cloudClient)
            return;

        if (!Has(existingIntegrations, IntegrationConceptCatalog.DependencyInjection))
        {
            AddGap(gaps,
                concept: IntegrationConceptCatalog.DependencyInjection,
                api: typeName,
                integrationType: "IServiceCollection registration",
                lookFor: "IServiceCollection, Add*",
                sourceType);
        }

        if (!Has(existingIntegrations, IntegrationConceptCatalog.Aspire))
        {
            AddGap(gaps,
                concept: IntegrationConceptCatalog.Aspire,
                api: typeName,
                integrationType: "AppHost resource builder",
                lookFor: "IResourceBuilder<T>, Add*, *Resource",
                sourceType);
        }
    }

    private static void AddConfigurationGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<IntegrationConceptDescriptor> existingIntegrations,
        string typeName,
        string simpleName,
        MetadataTypeDefinitionName? sourceType)
    {
        if (Has(existingIntegrations, IntegrationConceptCatalog.Configuration))
            return;

        if (typeName.StartsWith("Azure.Data.AppConfiguration.", StringComparison.Ordinal)
            && simpleName == "ConfigurationClient")
        {
            AddGap(gaps,
                concept: IntegrationConceptCatalog.Configuration,
                api: typeName,
                integrationType: "IConfigurationBuilder source",
                lookFor: "IConfigurationBuilder, AddAzureAppConfiguration",
                sourceType);
        }
    }

    private static void AddDatabaseGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<IntegrationConceptDescriptor> existingIntegrations,
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

        if (!Has(existingIntegrations, IntegrationConceptCatalog.HealthChecks))
        {
            AddGap(gaps,
                concept: IntegrationConceptCatalog.HealthChecks,
                api: typeName,
                integrationType: "IHealthChecksBuilder registration",
                lookFor: "IHealthChecksBuilder, Add*",
                sourceType);
        }

        if (!Has(existingIntegrations, IntegrationConceptCatalog.Aspire))
        {
            AddGap(gaps,
                concept: IntegrationConceptCatalog.Aspire,
                api: typeName,
                integrationType: "AppHost resource builder",
                lookFor: "IResourceBuilder<T>, Add*, *Resource",
                sourceType);
        }
    }

    private static void AddAiClientGaps(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IReadOnlySet<IntegrationConceptDescriptor> existingIntegrations,
        string typeName,
        string simpleName,
        MetadataTypeDefinitionName? sourceType)
    {
        if (Has(existingIntegrations, IntegrationConceptCatalog.AI))
            return;

        var aiClient = (typeName.StartsWith("OpenAI.", StringComparison.Ordinal)
                        || typeName.StartsWith("Azure.AI.", StringComparison.Ordinal))
                       && (simpleName.EndsWith("Client", StringComparison.Ordinal)
                           || simpleName.Contains("Chat", StringComparison.Ordinal)
                           || simpleName.Contains("Embedding", StringComparison.Ordinal));

        if (!aiClient)
            return;

        AddGap(gaps,
            concept: IntegrationConceptCatalog.AI,
            api: typeName,
            integrationType: "Microsoft.Extensions.AI adapter",
            lookFor: "IChatClient, AsIChatClient, IEmbeddingGenerator",
            sourceType,
            IChatClientTarget());
    }

    private static bool Has(
        IReadOnlySet<IntegrationConceptDescriptor> integrations,
        IntegrationConceptDescriptor concept)
        => integrations.Contains(concept);

    private static void AddGap(
        Dictionary<string, IntegrationOpportunityInfo> gaps,
        IntegrationConceptDescriptor concept,
        string api,
        string integrationType,
        string lookFor,
        MetadataTypeDefinitionName? sourceType,
        IntegrationOpportunityTarget? target = null)
    {
        var key = $"{concept.Id.Value}\0{integrationType}";
        var formattedApi = TypeResolver.FormatDisplayName(api);
        if (gaps.TryGetValue(key, out var existing)
            && GetEvidenceRank(existing.Api) <= GetEvidenceRank(formattedApi))
            return;

        gaps[key] = new IntegrationOpportunityInfo(
            concept,
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
