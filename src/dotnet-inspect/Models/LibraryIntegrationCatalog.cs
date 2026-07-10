using ILInspector.Metadata;

namespace DotnetInspector.Models;

internal enum LibraryIntegrationSource
{
    Ecosystem,
    OpenTelemetry,
}

internal sealed record LibraryIntegrationDescriptor(
    string Name,
    LibraryIntegrationSource Source,
    Func<LibraryInspection, bool> HasPresence,
    bool IncludeTypesWhenApisPresent)
{
    public bool CanRender(LibraryInspection inspection)
        => HasSignals(inspection) || HasPresence(inspection);

    public bool HasSignals(LibraryInspection inspection)
        => Source switch
        {
            LibraryIntegrationSource.Ecosystem =>
                inspection.EcosystemIntegrationInspection
                    .Payloads()
                    .Any(signal => signal.Integration.Equals(Name, StringComparison.Ordinal)),
            LibraryIntegrationSource.OpenTelemetry =>
                inspection.OpenTelemetryInspection.Payloads().Any(),
            _ => throw new InvalidOperationException($"Unknown integration source: {Source}."),
        };

    public List<(string Kind, string Name, string Shape)> GetSignals(
        LibraryInspection inspection)
        => Source switch
        {
            LibraryIntegrationSource.Ecosystem =>
            [
                .. inspection.EcosystemIntegrationInspection
                    .Payloads()
                    .Where(signal => signal.Integration.Equals(Name, StringComparison.Ordinal))
                    .Select(static signal => (signal.Kind, signal.Name, signal.Shape)),
            ],
            LibraryIntegrationSource.OpenTelemetry =>
            [
                .. inspection.OpenTelemetryInspection
                    .Payloads()
                    .Select(static signal => (signal.Kind, signal.Name, signal.Shape)),
            ],
            _ => throw new InvalidOperationException($"Unknown integration source: {Source}."),
        };

    public int CountRenderedRows(
        IReadOnlyCollection<(string Kind, string Name, string Shape)> signals)
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
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasAISupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor AspNetCore = new(
        EcosystemIntegrationNames.AspNetCore,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasAspNetCoreSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Authentication = new(
        EcosystemIntegrationNames.Authentication,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasAuthenticationSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Configuration = new(
        EcosystemIntegrationNames.Configuration,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasConfigurationSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Aspire = new(
        EcosystemIntegrationNames.Aspire,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasAspireSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor DependencyInjection = new(
        EcosystemIntegrationNames.DependencyInjection,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasDependencyInjectionSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor Logging = new(
        EcosystemIntegrationNames.Logging,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasLoggingSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor OpenTelemetry = new(
        EcosystemIntegrationNames.OpenTelemetry,
        LibraryIntegrationSource.OpenTelemetry,
        inspection => inspection.HasOpenTelemetrySupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor OpenAPI = new(
        EcosystemIntegrationNames.OpenAPI,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasOpenApiSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Options = new(
        EcosystemIntegrationNames.Options,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasOptionsSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor Hosting = new(
        EcosystemIntegrationNames.Hosting,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasHostingSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor HealthChecks = new(
        EcosystemIntegrationNames.HealthChecks,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasHealthChecksSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor HttpClient = new(
        EcosystemIntegrationNames.HttpClient,
        LibraryIntegrationSource.Ecosystem,
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
        HttpClient,
    ];

    public static string[] CategorySections => [RollupName, .. All.Select(descriptor => descriptor.Name)];

    public static bool CanRenderAny(LibraryInspection inspection)
        => All.Any(descriptor => descriptor.CanRender(inspection));

    public static int CountPresence(LibraryInspection inspection)
        => All.Count(descriptor => descriptor.HasPresence(inspection));
}
