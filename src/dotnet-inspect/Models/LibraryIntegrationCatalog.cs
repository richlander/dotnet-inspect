using DotnetInspector.Ecosystems;
using ILInspector.Metadata;

namespace DotnetInspector.Models;

internal enum LibraryIntegrationSource
{
    Ecosystem,
    OpenTelemetry,
}

internal sealed record LibraryIntegrationDescriptor(
    IntegrationConceptDescriptor Concept,
    LibraryIntegrationSource Source,
    Func<LibraryInspection, bool> HasPresence,
    bool IncludeTypesWhenApisPresent)
{
    public EcosystemPackId? Ecosystem { get; init; }

    public string Name => Concept.DisplayLabel;

    // User-facing section name/selector for this integration (e.g. "Integration: AI").
    // Distinct from Name, which stays the unprefixed integration identity used for
    // signal matching and finding payloads.
    public string SectionName => IntegrationSectionNames.Prefix + Name;

    public bool CanRender(LibraryInspection inspection)
    {
        if (!inspection.IntegrationQuery.Matches(Concept))
            return false;
        var failed = Source switch
        {
            LibraryIntegrationSource.Ecosystem =>
                inspection.EcosystemIntegrationInspection.Failure() is not null,
            LibraryIntegrationSource.OpenTelemetry =>
                inspection.OpenTelemetryInspection.Failure() is not null,
            _ => throw new InvalidOperationException($"Unknown integration source: {Source}."),
        };

        return !failed && (HasSignals(inspection) || HasPresence(inspection));
    }

    public bool HasSignals(LibraryInspection inspection)
        => Source switch
        {
            LibraryIntegrationSource.Ecosystem =>
                inspection.EcosystemIntegrationInspection
                    .PayloadsForRendering()
                    .Any(signal => ReferenceEquals(signal.GetConcept(), Concept)),
            LibraryIntegrationSource.OpenTelemetry =>
                inspection.OpenTelemetryInspection.PayloadsForRendering().Any(),
            _ => throw new InvalidOperationException($"Unknown integration source: {Source}."),
        };

    public List<(string Kind, string Name, string Shape)> GetSignals(
        LibraryInspection inspection)
        => !inspection.IntegrationQuery.Matches(Concept) ? [] : Source switch
        {
            LibraryIntegrationSource.Ecosystem =>
            [
                .. inspection.EcosystemIntegrationInspection
                    .PayloadsForRendering()
                    .Where(signal => ReferenceEquals(signal.GetConcept(), Concept))
                    .Select(static signal => (signal.Kind, signal.Name, signal.Shape)),
            ],
            LibraryIntegrationSource.OpenTelemetry =>
            [
                .. inspection.OpenTelemetryInspection
                    .PayloadsForRendering()
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
        IntegrationConceptCatalog.AI,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasAISupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor AspNetCore = new(
        IntegrationConceptCatalog.AspNetCore,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasAspNetCoreSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Authentication = new(
        IntegrationConceptCatalog.Authentication,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasAuthenticationSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Configuration = new(
        IntegrationConceptCatalog.Configuration,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasConfigurationSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Aspire = new(
        IntegrationConceptCatalog.Aspire,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasAspireSupport,
        IncludeTypesWhenApisPresent: true)
    {
        Ecosystem = EcosystemPackIds.Aspire,
    };

    public static readonly LibraryIntegrationDescriptor DependencyInjection = new(
        IntegrationConceptCatalog.DependencyInjection,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasDependencyInjectionSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor Logging = new(
        IntegrationConceptCatalog.Logging,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasLoggingSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor OpenTelemetry = new(
        IntegrationConceptCatalog.OpenTelemetry,
        LibraryIntegrationSource.OpenTelemetry,
        inspection => inspection.HasOpenTelemetrySupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor OpenAPI = new(
        IntegrationConceptCatalog.OpenAPI,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasOpenApiSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor Options = new(
        IntegrationConceptCatalog.Options,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasOptionsSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor Hosting = new(
        IntegrationConceptCatalog.Hosting,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasHostingSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor HealthChecks = new(
        IntegrationConceptCatalog.HealthChecks,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasHealthChecksSupport,
        IncludeTypesWhenApisPresent: false);

    public static readonly LibraryIntegrationDescriptor HttpClient = new(
        IntegrationConceptCatalog.HttpClient,
        LibraryIntegrationSource.Ecosystem,
        inspection => inspection.HasHttpClientSupport,
        IncludeTypesWhenApisPresent: true);

    public static readonly LibraryIntegrationDescriptor[] All =
        [.. IntegrationConceptCatalog.Concepts.Select(DescriptorFor)];

    public static string[] CategorySections => [.. All.Select(descriptor => descriptor.SectionName)];

    public static bool CanRenderAny(LibraryInspection inspection)
        => All.Any(descriptor => descriptor.CanRender(inspection));

    public static int CountPresence(LibraryInspection inspection)
        => All.Count(descriptor => descriptor.HasPresence(inspection));

    static LibraryIntegrationDescriptor DescriptorFor(
        IntegrationConceptDescriptor concept)
    {
        if (ReferenceEquals(concept, IntegrationConceptCatalog.AI))
            return AI;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.AspNetCore))
            return AspNetCore;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Authentication))
            return Authentication;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Configuration))
            return Configuration;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Aspire))
            return Aspire;
        if (ReferenceEquals(
                concept,
                IntegrationConceptCatalog.DependencyInjection))
        {
            return DependencyInjection;
        }
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Logging))
            return Logging;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.OpenAPI))
            return OpenAPI;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.OpenTelemetry))
            return OpenTelemetry;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Options))
            return Options;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.Hosting))
            return Hosting;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.HealthChecks))
            return HealthChecks;
        if (ReferenceEquals(concept, IntegrationConceptCatalog.HttpClient))
            return HttpClient;
        throw new InvalidOperationException(
            $"Unknown configured Integration concept '{concept.Id}'.");
    }
}
