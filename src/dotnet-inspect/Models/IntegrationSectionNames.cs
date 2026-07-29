using ILInspector.Metadata;

namespace DotnetInspector.Models;

// Display/selector names for the ecosystem-integration sections. Prefixed with
// "Integration: " (like "Performance: ") so the whole family clusters together
// under alphabetical section ordering. These are a presentation concern and are
// deliberately distinct from the integration identity strings in
// EcosystemIntegrationNames, which remain unprefixed for signal matching and
// finding/JSON payloads.
internal static class IntegrationSectionNames
{
    public const string Prefix = "Integration: ";

    public const string AI = Prefix + EcosystemIntegrationNames.AI;
    public const string AspNetCore = Prefix + EcosystemIntegrationNames.AspNetCore;
    public const string Aspire = Prefix + EcosystemIntegrationNames.Aspire;
    public const string Authentication = Prefix + EcosystemIntegrationNames.Authentication;
    public const string Configuration = Prefix + EcosystemIntegrationNames.Configuration;
    public const string DependencyInjection = Prefix + EcosystemIntegrationNames.DependencyInjection;
    public const string HealthChecks = Prefix + EcosystemIntegrationNames.HealthChecks;
    public const string Hosting = Prefix + EcosystemIntegrationNames.Hosting;
    public const string HttpClient = Prefix + EcosystemIntegrationNames.HttpClient;
    public const string Logging = Prefix + EcosystemIntegrationNames.Logging;
    public const string OpenAPI = Prefix + EcosystemIntegrationNames.OpenAPI;
    public const string OpenTelemetry = Prefix + EcosystemIntegrationNames.OpenTelemetry;
    public const string Options = Prefix + EcosystemIntegrationNames.Options;

    public const string Opportunities = Prefix + "Opportunities";
}
