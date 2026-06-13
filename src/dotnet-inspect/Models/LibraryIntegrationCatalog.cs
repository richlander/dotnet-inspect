using ILInspector.Metadata;

namespace DotnetInspector.Models;

internal sealed record LibraryIntegrationDescriptor(
    string Name,
    Func<LibraryInspection, List<IntegrationSignal>?> GetSignals,
    Action<LibraryInspection, List<IntegrationSignal>?> SetSignals,
    Func<LibraryInspection, bool> HasPresence,
    bool IncludeTypesWhenApisPresent)
{
    public bool CanRender(LibraryInspection inspection)
        => GetSignals(inspection) is { Count: > 0 } || HasPresence(inspection);

    public int CountRenderedRows(List<IntegrationSignal> signals)
    {
        var apiCount = signals.Count(signal => signal.Shape == IntegrationSignalShape.Api);
        return apiCount > 0 && !IncludeTypesWhenApisPresent ? apiCount : signals.Count;
    }
}

internal static class LibraryIntegrationCatalog
{
    public const string RollupName = EcosystemIntegrationNames.Integrations;

    public static readonly LibraryIntegrationDescriptor AI = new(
        EcosystemIntegrationNames.AI,
        inspection => inspection.AI,
        (inspection, signals) => inspection.AI = signals,
        inspection => inspection.HasAISupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor AspNetCore = new(
        EcosystemIntegrationNames.AspNetCore,
        inspection => inspection.AspNetCore,
        (inspection, signals) => inspection.AspNetCore = signals,
        inspection => inspection.HasAspNetCoreSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Authentication = new(
        EcosystemIntegrationNames.Authentication,
        inspection => inspection.Authentication,
        (inspection, signals) => inspection.Authentication = signals,
        inspection => inspection.HasAuthenticationSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Configuration = new(
        EcosystemIntegrationNames.Configuration,
        inspection => inspection.Configuration,
        (inspection, signals) => inspection.Configuration = signals,
        inspection => inspection.HasConfigurationSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Aspire = new(
        EcosystemIntegrationNames.Aspire,
        inspection => inspection.Aspire,
        (inspection, signals) => inspection.Aspire = signals,
        inspection => inspection.HasAspireSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor DependencyInjection = new(
        EcosystemIntegrationNames.DependencyInjection,
        inspection => inspection.DependencyInjection,
        (inspection, signals) => inspection.DependencyInjection = signals,
        inspection => inspection.HasDependencyInjectionSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor Logging = new(
        EcosystemIntegrationNames.Logging,
        inspection => inspection.Logging,
        (inspection, signals) => inspection.Logging = signals,
        inspection => inspection.HasLoggingSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor OpenTelemetry = new(
        EcosystemIntegrationNames.OpenTelemetry,
        inspection => inspection.OpenTelemetry,
        (inspection, signals) => inspection.OpenTelemetry = signals,
        inspection => inspection.HasOpenTelemetrySupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor OpenAPI = new(
        EcosystemIntegrationNames.OpenAPI,
        inspection => inspection.OpenApi,
        (inspection, signals) => inspection.OpenApi = signals,
        inspection => inspection.HasOpenApiSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Options = new(
        EcosystemIntegrationNames.Options,
        inspection => inspection.Options,
        (inspection, signals) => inspection.Options = signals,
        inspection => inspection.HasOptionsSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor Hosting = new(
        EcosystemIntegrationNames.Hosting,
        inspection => inspection.Hosting,
        (inspection, signals) => inspection.Hosting = signals,
        inspection => inspection.HasHostingSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor HealthChecks = new(
        EcosystemIntegrationNames.HealthChecks,
        inspection => inspection.HealthChecks,
        (inspection, signals) => inspection.HealthChecks = signals,
        inspection => inspection.HasHealthChecksSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor HttpClient = new(
        EcosystemIntegrationNames.HttpClient,
        inspection => inspection.HttpClient,
        (inspection, signals) => inspection.HttpClient = signals,
        inspection => inspection.HasHttpClientSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor[] All =
    [
        AI,
        AspNetCore,
        Authentication,
        Configuration,
        Aspire,
        DependencyInjection,
        Logging,
        OpenTelemetry,
        OpenAPI,
        Options,
        Hosting,
        HealthChecks,
        HttpClient
    ];

    public static readonly LibraryIntegrationDescriptor[] EcosystemScanned =
    [
        AI,
        AspNetCore,
        Authentication,
        Configuration,
        Aspire,
        DependencyInjection,
        Logging,
        OpenAPI,
        Options,
        Hosting,
        HealthChecks,
        HttpClient
    ];

    public static string[] CategorySections => [RollupName, .. All.Select(descriptor => descriptor.Name)];

    public static bool CanRenderAny(LibraryInspection inspection)
        => inspection.Integrations is { Count: > 0 } || All.Any(descriptor => descriptor.CanRender(inspection));

    public static int CountPresence(LibraryInspection inspection)
        => All.Count(descriptor => descriptor.HasPresence(inspection));
}
