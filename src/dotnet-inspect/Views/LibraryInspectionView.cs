using DotnetInspector.Models;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(FileName), TitleContextProperty = nameof(Tfm), AutoFieldsCount = 7, FieldLayout = FieldLayout.Inline)]
public class LibraryInspectionView
{
    private readonly LibraryInspection _data;
    private readonly bool _topFieldsOnly;
    private readonly Dictionary<LibraryIntegrationDescriptor, List<(string Kind, string Name, string Shape)>> _integrationSignals = [];

    public LibraryInspectionView(LibraryInspection data, bool topFieldsOnly = false)
    {
        _data = data;
        _topFieldsOnly = topFieldsOnly;
    }

    [MarkoutIgnore]
    public string? Tfm => _topFieldsOnly ? null : _data.Tfm;

    [MarkoutPropertyName("File")]
    public string FileName => _data.FileName;

    // ===== Top fields (first 7 auto-fields, rendered inline for -v:q compact summary) =====

    [MarkoutSkipNull]
    public string? Name => _topFieldsOnly ? _data.AssemblyInfo?.AssemblyName : null;

    [MarkoutSkipNull]
    public string? Version => _topFieldsOnly ? LibraryInspectionDisplay.ResolveVersion(_data) : null;

    [MarkoutPropertyName("TFM")]
    [MarkoutSkipNull]
    public string? TargetFramework => _topFieldsOnly ? _data.AssemblyInfo?.TargetFramework : null;

    [MarkoutPropertyName("Arch")]
    [MarkoutSkipNull]
    public string? Architecture => _topFieldsOnly ? _data.AssemblyInfo?.Architecture : null;

    [MarkoutPropertyName("Size")]
    [MarkoutSkipNull]
    public string? FileSize => _topFieldsOnly ? (_data.FileSize > 0 ? ByteSizeFormatter.FormatBytes(_data.FileSize) : null) : null;

    [MarkoutSkipNull]
    public string? Source => _topFieldsOnly ? _data.Source : null;

    [MarkoutSkipNull]
    public string? Modified => _topFieldsOnly ? _data.LastModified?.ToString("yyyy-MM-dd") : null;

    [MarkoutPropertyName("Library")]
    public string? AssemblySummary => _data.AssemblyInfo switch
    {
        null => null,
        var info => string.Join(", ", new[]
        {
            info.Architecture,
            info.TargetFramework,
            info.CompilationType,
            info.IsSigned ? "Signed" : null
        }.Where(s => !string.IsNullOrEmpty(s)))
    };

    [MarkoutPropertyName("API")]
    public string? ApiSummary => _data.ApiSurface switch
    {
        null => null,
        var api => $"{api.PublicTypeCount} types, {api.PublicMethodCount} methods"
    };

    // ===== Field Collection Sections =====

    [MarkoutIgnore]
    public bool HasInspectionFailures => _data.InspectionFailures is { Count: > 0 };

    [MarkoutSection(Name = "Inspection Failures", ShowWhenProperty = nameof(HasInspectionFailures))]
    public List<InspectionFailureRow>? InspectionFailuresSection =>
        _data.InspectionFailures?
            .Select(failure => new InspectionFailureRow(
                failure.Section,
                failure.Finding,
                failure.Reason))
            .ToList();

    [MarkoutIgnore]
    public bool HasAsyncMethods => _data.AsyncMethodCount > 0;

    [MarkoutSection(Name = "Async Methods", ShowWhenProperty = nameof(HasAsyncMethods))]
    public List<AsyncMethodRow>? AsyncMethodsSection =>
        _data.AsyncMethods?
            .OrderBy(m => m.DeclaringType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.MethodName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Signature, StringComparer.OrdinalIgnoreCase)
            .Select(m => new AsyncMethodRow(
                m.MethodName,
                MarkoutInline.Code(MetadataTypeNameFormatter.FormatGenericTypeName(m.DeclaringType)),
                m.Kind,
                MarkoutInline.Code(m.Signature)))
            .ToList();

    [MarkoutIgnore]
    public bool HasCustomAttributes => _data.AssemblyAttributeInspection.HasFindings();

    [MarkoutSection(Name = "Custom Attributes", ShowWhenProperty = nameof(HasCustomAttributes))]
    public List<CustomAttributeRow>? CustomAttributesSection =>
        _data.AssemblyAttributeInspection.PayloadsForRendering()
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Target, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Value, StringComparer.OrdinalIgnoreCase)
            .Select(a => new CustomAttributeRow(a.Name, a.Target, a.Value ?? ""))
            .ToList() is { Count: > 0 } rows ? rows : null;

    [MarkoutIgnore]
    public bool HasUnionTypes => _data.UnionTypeInspection.HasFindings();

    [MarkoutSection(Name = "Union Types", ShowWhenProperty = nameof(HasUnionTypes))]
    public List<UnionTypeRow>? UnionTypesSection =>
        _data.UnionTypeInspection.PayloadsForRendering()
            .OrderBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase)
            .Select(t => new UnionTypeRow(t.TypeName, t.Kind, t.ImplementsIUnion ? "Yes" : "No", string.Join(", ", t.CaseTypes)))
            .ToList() is { Count: > 0 } rows ? rows : null;

    [MarkoutIgnore]
    public bool UseDependenciesView => _data.UseDependenciesView;

    [MarkoutSection(Name = "Dependencies")]
    public List<TreeNode>? DependenciesSection =>
        !_data.UseDependenciesView || _data.AssemblyInfo?.TransitiveReferences is not { Count: > 0 } ? null :
        BuildNestedDependencyTree(_data.AssemblyInfo.TransitiveReferences);

    [MarkoutIgnore]
    public bool HasExtensionMethods => _data.ExtensionMethods is { Count: > 0 };

    [MarkoutSection(Name = "Extension Methods", ShowWhenProperty = nameof(HasExtensionMethods))]
    public List<ExtensionMethodRow>? ExtensionMethodsSection =>
        _data.ExtensionMethods?
            .OrderBy(e => e.ExtendedType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.MethodName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.ExtensionClass, StringComparer.OrdinalIgnoreCase)
            .Select(e =>
            {
                var name = e.Overloads > 1 ? $"{e.MethodName} ({e.Overloads} overloads)" : e.MethodName;
                return new ExtensionMethodRow(name, e.Kind, e.ExtendedType, e.ExtensionClass);
            })
            .ToList();

    [MarkoutSection(Name = "Library Info")]
    public LibraryInfoSection? AssemblyInfoSection => _data.AssemblyInfo is not { } info ? null : new LibraryInfoSection
    {
        Architecture = info.Architecture,
        AssemblyVersion = info.AssemblyVersion,
        AsyncMethods = _data.AsyncMethodCount,
        Company = info.Company,
        Compilation = info.CompilationType,
        Copyright = info.Copyright,
        CustomAttributes = _data.AssemblyAttributeInspection.FindingCount(),
        Deterministic = _data.IsDeterministic,
        ExtensionMethods = CountExtensionMethods(_data.ExtensionMethods),
        Facade = _data.IsFacadeAssembly,
        FileSize = _data.FileSize > 0 ? ByteSizeFormatter.FormatBytes(_data.FileSize) : null,
        InformationalVersion = info.InformationalVersion,
        Integrations = CountIntegrations(_data),
        Methods = info.MethodDefinitionCount > 0 ? info.MethodDefinitionCount.ToString("N0") : null,
        Modified = _data.LastModified?.ToString("yyyy-MM-dd"),
        Name = info.AssemblyName,
        Product = info.Product,
        PublicKeyToken = info.PublicKeyToken,
        Reproducible = _data.HasReproducibleFlag,
        Resources = _data.ResourceInspection.FindingCount(),
        Signed = info.IsSigned ? "Yes" : null,
        Source = _data.Source,
        Switches = CountSwitches(_data),
        TargetFramework = info.TargetFramework,
        TypeForwarders = _data.TypeForwarderInspection.FindingCount(),
        Types = info.TypeDefinitionCount > 0 ? info.TypeDefinitionCount.ToString("N0") : null,
        UnionTypes = _data.UnionTypeInspection.FindingCount(),
        Version = LibraryInspectionDisplay.ResolveVersion(_data),
    };

    [MarkoutSection(Name = "References")]
    public List<ReferenceRow>? AssemblyReferencesSection =>
        _data.AssemblyInfo?.TransitiveReferences is { Count: > 0 } ? null :
        _data.AssemblyReferenceInspection.PayloadsForRendering().OrderBy(r => r.Name)
            .Select(r => new ReferenceRow(r.Name, r.Version, r.PublicKeyToken ?? "-"))
            .ToList() is { Count: > 0 } list ? list : null;

    [MarkoutIgnore]
    public bool HasNonNormalizedPaths => _data.NonNormalizedPaths is { Count: > 0 };

    [MarkoutSection(Name = "Non-normalized Paths", ShowWhenProperty = nameof(HasNonNormalizedPaths))]
    public List<string>? NonNormalizedPathsSection =>
        _data.NonNormalizedPaths?.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

    [MarkoutIgnore]
    public bool HasPInvokeMethods => _data.PInvokeMethodCount > 0;

    [MarkoutSection(Name = "P/Invoke Methods", ShowWhenProperty = nameof(HasPInvokeMethods))]
    public List<PInvokeMethodRow>? PInvokeMethodsSection =>
        _data.PInvokeMethods?
            .OrderBy(m => m.DeclaringType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.MethodName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Signature, StringComparer.OrdinalIgnoreCase)
            .Select(m => new PInvokeMethodRow(
                m.MethodName,
                MarkoutInline.Code(MetadataTypeNameFormatter.FormatGenericTypeName(m.DeclaringType)),
                m.ModuleName ?? "",
                MarkoutInline.Code(m.Signature)))
            .ToList();

    [MarkoutIgnore]
    public bool HasResources => _data.ResourceInspection.HasFindings();

    [MarkoutSection(Name = "Resources", ShowWhenProperty = nameof(HasResources))]
    public List<ResourceRow>? ResourcesSection =>
        _data.ResourceInspection.PayloadsForRendering()
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new ResourceRow(
                r.Name,
                r.IsPublic ? "public" : "private",
                r.Size == 0 ? "" : ByteSizeFormatter.FormatBytes(r.Size)))
            .ToList() is { Count: > 0 } rows ? rows : null;

    [MarkoutIgnore]
    public bool HasAuditSignals => _data.AuditSignals is { Count: > 0 };

    [MarkoutSection(Name = "Signals", ShowWhenProperty = nameof(HasAuditSignals))]
    public List<AuditSignalRow>? SignalsSection =>
        _data.AuditSignals?.Select(s => new AuditSignalRow(s.Area, s.Signal, s.Value, s.Evidence)).ToList();

    [MarkoutIgnore]
    public bool HasSwitches => _data.SwitchInspection.HasFindings();

    [MarkoutSection(Name = "Switches", ShowWhenProperty = nameof(HasSwitches))]
    [MarkoutIgnoreColumnWhen(nameof(SwitchKindIsUniform), "Kind")]
    public List<SwitchRow>? SwitchesSection =>
        _data.SwitchInspection.PayloadsForRendering()
            .OrderBy(s => s.Kind, StringComparer.Ordinal)
            .ThenBy(s => s.Switch, StringComparer.Ordinal)
            .ThenBy(s => s.Api, StringComparer.Ordinal)
            .Select(s => new SwitchRow(s.Kind, MarkoutInline.Code(s.Switch), MarkoutInline.Code(s.Api)))
            .ToList() is { Count: > 0 } rows ? rows : null;

    [MarkoutIgnore]
    public bool HasIntegrations => LibraryIntegrationCatalog.All.Any(
        descriptor => descriptor.GetSignals(_data).Count > 0);

    [MarkoutSection(Name = EcosystemIntegrationNames.Integrations, ShowWhenProperty = nameof(HasIntegrations))]
    public List<IntegrationRow>? IntegrationsSection =>
        LibraryIntegrationCatalog.All
            .Select(descriptor =>
            {
                var signals = descriptor.GetSignals(_data);
                return new IntegrationRow(
                    descriptor.Name,
                    descriptor.CountRenderedRows(signals));
            })
            .Where(row => row.Apis > 0)
            .ToList() is { Count: > 0 } rows ? rows : null;

    [MarkoutIgnore]
    public bool HasIntegrationOpportunities => _data.IntegrationOpportunities is { Count: > 0 };

    [MarkoutSection(Name = "Integration Opportunities", ShowWhenProperty = nameof(HasIntegrationOpportunities))]
    public List<IntegrationOpportunityRow>? IntegrationOpportunitiesSection =>
        _data.IntegrationOpportunities?
            .OrderBy(g => g.Integration, StringComparer.Ordinal)
            .ThenBy(g => g.Api, StringComparer.Ordinal)
            .ThenBy(g => g.IntegrationType, StringComparer.Ordinal)
            .Select(g => new IntegrationOpportunityRow(
                g.Integration,
                MarkoutInline.Code(g.Api),
                g.IntegrationType,
                g.LookFor))
            .ToList();

    [MarkoutIgnore]
    public bool HasAI => HasSignals(LibraryIntegrationCatalog.AI);

    [MarkoutIgnore]
    public bool HasAIApis => HasApis(LibraryIntegrationCatalog.AI);

    [MarkoutIgnore]
    public bool HasAITypesOnly => HasAI && !HasAIApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.AI, ShowWhenProperty = nameof(HasAI))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? AIApiSection => HasAIApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.AI), includeTypes: true) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.AI, ShowWhenProperty = nameof(HasAITypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? AITypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.AI));

    [MarkoutIgnore]
    public bool HasAspNetCore => HasSignals(LibraryIntegrationCatalog.AspNetCore);

    [MarkoutIgnore]
    public bool HasAspNetCoreApis => HasApis(LibraryIntegrationCatalog.AspNetCore);

    [MarkoutIgnore]
    public bool HasAspNetCoreTypesOnly => HasAspNetCore && !HasAspNetCoreApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.AspNetCore, ShowWhenProperty = nameof(HasAspNetCoreApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? AspNetCoreApiSection => HasAspNetCoreApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.AspNetCore), includeTypes: true) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.AspNetCore, ShowWhenProperty = nameof(HasAspNetCoreTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? AspNetCoreTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.AspNetCore));

    [MarkoutIgnore]
    public bool HasAuthentication => HasSignals(LibraryIntegrationCatalog.Authentication);

    [MarkoutIgnore]
    public bool HasAuthenticationApis => HasApis(LibraryIntegrationCatalog.Authentication);

    [MarkoutIgnore]
    public bool HasAuthenticationTypesOnly => HasAuthentication && !HasAuthenticationApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.Authentication, ShowWhenProperty = nameof(HasAuthenticationApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? AuthenticationApiSection => HasAuthenticationApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Authentication), includeTypes: true) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.Authentication, ShowWhenProperty = nameof(HasAuthenticationTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? AuthenticationTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Authentication));

    [MarkoutIgnore]
    public bool HasAspire => HasSignals(LibraryIntegrationCatalog.Aspire);

    [MarkoutIgnore]
    public bool HasAspireApis => HasApis(LibraryIntegrationCatalog.Aspire);

    [MarkoutIgnore]
    public bool HasAspireTypesOnly => HasAspire && !HasAspireApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.Aspire, ShowWhenProperty = nameof(HasAspireApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? AspireApiSection => HasAspireApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Aspire), includeTypes: true) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.Aspire, ShowWhenProperty = nameof(HasAspireTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? AspireTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Aspire));

    [MarkoutIgnore]
    public bool HasConfiguration => HasSignals(LibraryIntegrationCatalog.Configuration);

    [MarkoutIgnore]
    public bool HasConfigurationApis => HasApis(LibraryIntegrationCatalog.Configuration);

    [MarkoutIgnore]
    public bool HasConfigurationTypesOnly => HasConfiguration && !HasConfigurationApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.Configuration, ShowWhenProperty = nameof(HasConfigurationApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? ConfigurationApiSection => HasConfigurationApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Configuration), includeTypes: true) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.Configuration, ShowWhenProperty = nameof(HasConfigurationTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? ConfigurationTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Configuration));

    [MarkoutIgnore]
    public bool HasDependencyInjection => HasSignals(LibraryIntegrationCatalog.DependencyInjection);

    [MarkoutIgnore]
    public bool HasDependencyInjectionApis => HasApis(LibraryIntegrationCatalog.DependencyInjection);

    [MarkoutIgnore]
    public bool HasDependencyInjectionTypesOnly => HasDependencyInjection && !HasDependencyInjectionApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.DependencyInjection, ShowWhenProperty = nameof(HasDependencyInjectionApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? DependencyInjectionApiSection => HasDependencyInjectionApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.DependencyInjection), includeTypes: false) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.DependencyInjection, ShowWhenProperty = nameof(HasDependencyInjectionTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? DependencyInjectionTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.DependencyInjection));

    [MarkoutIgnore]
    public bool HasLogging => HasSignals(LibraryIntegrationCatalog.Logging);

    [MarkoutIgnore]
    public bool HasLoggingApis => HasApis(LibraryIntegrationCatalog.Logging);

    [MarkoutIgnore]
    public bool HasLoggingTypesOnly => HasLogging && !HasLoggingApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.Logging, ShowWhenProperty = nameof(HasLoggingApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? LoggingApiSection => HasLoggingApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Logging), includeTypes: false) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.Logging, ShowWhenProperty = nameof(HasLoggingTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? LoggingTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Logging));

    [MarkoutIgnore]
    public bool HasOpenTelemetry => HasSignals(LibraryIntegrationCatalog.OpenTelemetry);

    [MarkoutIgnore]
    public bool HasOpenTelemetryApis => HasApis(LibraryIntegrationCatalog.OpenTelemetry);

    [MarkoutIgnore]
    public bool HasOpenTelemetryTypesOnly => HasOpenTelemetry && !HasOpenTelemetryApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.OpenTelemetry, ShowWhenProperty = nameof(HasOpenTelemetryApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? OpenTelemetryApiSection => HasOpenTelemetryApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.OpenTelemetry), includeTypes: true) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.OpenTelemetry, ShowWhenProperty = nameof(HasOpenTelemetryTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? OpenTelemetryTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.OpenTelemetry));

    [MarkoutIgnore]
    public bool HasOpenApi => HasSignals(LibraryIntegrationCatalog.OpenAPI);

    [MarkoutIgnore]
    public bool HasOpenApiApis => HasApis(LibraryIntegrationCatalog.OpenAPI);

    [MarkoutIgnore]
    public bool HasOpenApiTypesOnly => HasOpenApi && !HasOpenApiApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.OpenAPI, ShowWhenProperty = nameof(HasOpenApiApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? OpenApiApiSection => HasOpenApiApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.OpenAPI), includeTypes: true) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.OpenAPI, ShowWhenProperty = nameof(HasOpenApiTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? OpenApiTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.OpenAPI));

    [MarkoutIgnore]
    public bool HasOptions => HasSignals(LibraryIntegrationCatalog.Options);

    [MarkoutIgnore]
    public bool HasOptionsApis => HasApis(LibraryIntegrationCatalog.Options);

    [MarkoutIgnore]
    public bool HasOptionsTypesOnly => HasOptions && !HasOptionsApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.Options, ShowWhenProperty = nameof(HasOptionsApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? OptionsApiSection => HasOptionsApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Options), includeTypes: false) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.Options, ShowWhenProperty = nameof(HasOptionsTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? OptionsTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Options));

    [MarkoutIgnore]
    public bool HasHosting => HasSignals(LibraryIntegrationCatalog.Hosting);

    [MarkoutIgnore]
    public bool HasHostingApis => HasApis(LibraryIntegrationCatalog.Hosting);

    [MarkoutIgnore]
    public bool HasHostingTypesOnly => HasHosting && !HasHostingApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.Hosting, ShowWhenProperty = nameof(HasHostingApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? HostingApiSection => HasHostingApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Hosting), includeTypes: false) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.Hosting, ShowWhenProperty = nameof(HasHostingTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? HostingTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Hosting));

    [MarkoutIgnore]
    public bool HasHealthChecks => HasSignals(LibraryIntegrationCatalog.HealthChecks);

    [MarkoutIgnore]
    public bool HasHealthChecksApis => HasApis(LibraryIntegrationCatalog.HealthChecks);

    [MarkoutIgnore]
    public bool HasHealthChecksTypesOnly => HasHealthChecks && !HasHealthChecksApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.HealthChecks, ShowWhenProperty = nameof(HasHealthChecksApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? HealthChecksApiSection => HasHealthChecksApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.HealthChecks), includeTypes: false) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.HealthChecks, ShowWhenProperty = nameof(HasHealthChecksTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? HealthChecksTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.HealthChecks));

    [MarkoutIgnore]
    public bool HasHttpClient => HasSignals(LibraryIntegrationCatalog.HttpClient);

    [MarkoutIgnore]
    public bool HasHttpClientApis => HasApis(LibraryIntegrationCatalog.HttpClient);

    [MarkoutIgnore]
    public bool HasHttpClientTypesOnly => HasHttpClient && !HasHttpClientApis;

    [MarkoutSection(Name = EcosystemIntegrationNames.HttpClient, ShowWhenProperty = nameof(HasHttpClientApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? HttpClientApiSection => HasHttpClientApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.HttpClient), includeTypes: true) : null;

    [MarkoutSection(Name = EcosystemIntegrationNames.HttpClient, ShowWhenProperty = nameof(HasHttpClientTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? HttpClientTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.HttpClient));

    [MarkoutIgnore]
    public bool HasSourceLinkAudit => _data.AllSourcesAccessible.HasValue || _data.TotalSourceFiles > 0;

    [MarkoutSection(Name = "Source Files", EmptyText = "No SourceLink source files found for this library.")]
    public List<SourceFileRow>? SourceFilesSection =>
        _data.SourceFiles?
            .Select(file => new SourceFileRow(file.Type, file.Url))
            .ToList();

    [MarkoutIgnore]
    public bool HasILOffset => _data.ILOffset != null;

    [MarkoutSection(Name = SectionNames.ILOffset, ShowWhenProperty = nameof(HasILOffset))]
    public ILOffsetSection? ILOffsetSection => _data.ILOffset is not { } result ? null : new ILOffsetSection
    {
        Method = result.Method,
        Token = result.Token,
        ILOffset = result.ILOffset,
        MatchedOffset = result.MatchedOffset,
        File = result.File,
        Line = result.Line,
        Url = result.Url
    };

    [MarkoutIgnore]
    public bool HasILOffsetMemberContext => _data.ILOffset?.MemberContext != null;

    [MarkoutSection(Name = SectionNames.MemberContext, ShowWhenProperty = nameof(HasILOffsetMemberContext))]
    public ILOffsetMemberContextSection? ILOffsetMemberContextSection =>
        _data.ILOffset?.MemberContext is not { } context ? null : new ILOffsetMemberContextSection
        {
            Assembly = context.Assembly,
            Type = context.Type,
            TypeKind = context.TypeKind,
            Member = context.Member,
            Signature = context.Signature,
            MemberKind = context.MemberKind,
            Visibility = context.Visibility,
            Static = context.Static,
            Async = context.Async,
            MetadataToken = context.MetadataToken,
            ILOffset = context.ILOffset
        };

    [MarkoutIgnore]
    public bool HasILOffsetInstructionContext => _data.ILOffset?.InstructionContext != null;

    [MarkoutSection(Name = SectionNames.InstructionContext, ShowWhenProperty = nameof(HasILOffsetInstructionContext))]
    public ILOffsetInstructionContextSection? ILOffsetInstructionContextSection =>
        _data.ILOffset?.InstructionContext is not { } context ? null : new ILOffsetInstructionContextSection
        {
            ILOffset = context.ILOffset,
            Boundary = context.Boundary,
            Opcode = context.Opcode,
            OperandKind = context.OperandKind,
            Operand = context.Operand,
            OperandToken = context.OperandToken,
            BranchTargets = context.BranchTargets,
            NextOffset = context.NextOffset,
            Length = context.Length,
            Block = context.Block,
            TerminatesBlock = context.TerminatesBlock,
            FallsThrough = context.FallsThrough
        };

    [MarkoutIgnore]
    public bool HasILOffsetExceptionContext => _data.ILOffset?.ExceptionContext is { Count: > 0 };

    [MarkoutSection(Name = SectionNames.ExceptionContext, ShowWhenProperty = nameof(HasILOffsetExceptionContext))]
    public List<ILOffsetExceptionContextRow>? ILOffsetExceptionContextSection =>
        _data.ILOffset?.ExceptionContext?
            .Select(context => new ILOffsetExceptionContextRow(
                context.Region,
                context.Context,
                context.Clause,
                context.TryRange,
                context.HandlerRange,
                context.FilterRange,
                context.CaughtType))
            .ToList();

    [MarkoutIgnore]
    public bool HasILOffsetCallsiteContext => _data.ILOffset?.CallsiteContext != null;

    [MarkoutSection(Name = SectionNames.CallsiteContext, ShowWhenProperty = nameof(HasILOffsetCallsiteContext))]
    public ILOffsetCallsiteContextSection? ILOffsetCallsiteContextSection =>
        _data.ILOffset?.CallsiteContext is not { } context ? null : new ILOffsetCallsiteContextSection
        {
            CallOffset = context.CallOffset,
            Opcode = context.Opcode,
            CallKind = context.CallKind,
            Callee = context.Callee,
            OperandToken = context.OperandToken,
            ReturnAddress = context.ReturnAddress
        };

    [MarkoutIgnore]
    public bool HasILOffsetReturnAddressContext => _data.ILOffset?.ReturnAddressContext != null;

    [MarkoutSection(Name = SectionNames.ReturnAddressContext, ShowWhenProperty = nameof(HasILOffsetReturnAddressContext))]
    public ILOffsetReturnAddressContextSection? ILOffsetReturnAddressContextSection =>
        _data.ILOffset?.ReturnAddressContext is not { } context ? null : new ILOffsetReturnAddressContextSection
        {
            ILOffset = context.ILOffset,
            CallOffset = context.CallOffset,
            Opcode = context.Opcode,
            CallKind = context.CallKind,
            Callee = context.Callee,
            OperandToken = context.OperandToken
        };

    [MarkoutIgnore]
    public bool HasILOffsetAllocationContext => _data.ILOffset?.AllocationContext is { Count: > 0 };

    [MarkoutSection(Name = SectionNames.AllocationContext, ShowWhenProperty = nameof(HasILOffsetAllocationContext))]
    [MarkoutIgnoreColumnWhen(nameof(AllocationContextEscapeKindIsEmpty), nameof(ILOffsetAllocationContextRow.EscapeKind))]
    [MarkoutIgnoreColumnWhen(nameof(AllocationContextMultiplicityIsEmpty), nameof(ILOffsetAllocationContextRow.Multiplicity))]
    [MarkoutIgnoreColumnWhen(nameof(AllocationContextChurnedTypeIsEmpty), nameof(ILOffsetAllocationContextRow.ChurnedType))]
    public List<ILOffsetAllocationContextRow>? ILOffsetAllocationContextSection =>
        _data.ILOffset?.AllocationContext?
            .Select(context => new ILOffsetAllocationContextRow(
                context.ILOffset,
                context.AllocationKind,
                context.AllocatedType,
                context.CountedAsHeap,
                context.Frequency,
                context.Escape,
                context.EscapeKind,
                context.EstimatedSizeBytes,
                context.SizeTier,
                context.InLoop,
                context.Path,
                context.PathConfidence,
                context.PostDominance,
                context.Evidence,
                context.Multiplicity,
                context.ChurnedType))
            .ToList();

    [MarkoutIgnore]
    public bool HasILOffsetSafetyContext => _data.ILOffset?.SafetyContext is { Count: > 0 };

    [MarkoutSection(Name = SectionNames.SafetyContext, ShowWhenProperty = nameof(HasILOffsetSafetyContext))]
    public List<ILOffsetSafetyContextRow>? ILOffsetSafetyContextSection =>
        _data.ILOffset?.SafetyContext?
            .Select(context => new ILOffsetSafetyContextRow(
                context.ILOffset,
                context.SafetyKind,
                context.Operation,
                context.Requirement,
                context.Evidence))
            .ToList();

    [MarkoutIgnore]
    public bool HasILOffsetCostContext => _data.ILOffset?.CostContext is { Count: > 0 };

    [MarkoutSection(Name = SectionNames.CostContext, ShowWhenProperty = nameof(HasILOffsetCostContext))]
    public List<ILOffsetCostContextRow>? ILOffsetCostContextSection =>
        _data.ILOffset?.CostContext?
            .Select(context => new ILOffsetCostContextRow(
                context.ILOffset,
                context.CostKind,
                context.Operation,
                context.InLoop,
                context.Evidence))
            .ToList();

    [MarkoutSection(Name = "SourceLink Availability", ShowWhenProperty = nameof(HasSourceLinkAudit))]
    public SourceLinkAuditSection? SourceLinkAuditSection => !HasSourceLinkAudit ? null : new SourceLinkAuditSection
    {
        Status = _data.AllSourcesAccessible == true ? "Complete" : "Partial",
        SourceFiles = $"{_data.AccessibleSourceFiles}/{_data.TotalSourceFiles} available",
        Embedded = _data.EmbeddedSourceFiles,
        Missing = _data.MissingSourceFiles?.Count ?? 0
    };

    [MarkoutIgnore]
    public bool HasSourceIntegrity => _data.SourceIntegrityChecked;

    [MarkoutSection(Name = "SourceLink Integrity", ShowWhenProperty = nameof(HasSourceIntegrity))]
    public SourceIntegritySection? SourceIntegritySection => !HasSourceIntegrity ? null : new SourceIntegritySection
    {
        CrlfMismatch = _data.SourceIntegrityLineEndingNormalized > 0
            ? $"{_data.SourceIntegrityLineEndingNormalized} normalized"
            : null,
        Mismatched = _data.SourceIntegrityMismatched,
        MismatchedFiles = _data.SourceIntegrityMismatches is { Count: > 0 } mismatches
            ? string.Join(", ", mismatches.Select(MarkoutInline.Code))
            : null,
        Status = _data.SourceIntegrityMismatched > 0 ? "Mismatch"
            : _data.SourceIntegrityUnverifiable > 0 ? "Partial" : "Verified",
        Unverifiable = _data.SourceIntegrityUnverifiable,
        Verified = _data.SourceIntegrityVerified,
    };

    [MarkoutIgnore]
    public bool HasMissingSourceFiles => _data.MissingSourceFiles is { Count: > 0 };

    [MarkoutSection(Name = "SourceLink Missing Files", ShowWhenProperty = nameof(HasMissingSourceFiles))]
    public List<string>? MissingSourceFilesSection =>
        _data.MissingSourceFiles?
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(MarkoutInline.Code)
            .ToList();

    [MarkoutSection(Name = "Symbols")]
    public SymbolsSection? SymbolsSection => new SymbolsSection
    {
        PdbFormat = _data.PdbFormat ?? "Unknown",
        PdbLocation = _data.PdbLocation ?? "Unknown",
        SymbolServer = _data.SymbolServer,
        PdbPath = _data.PdbPath,
        SourceLink = _data.HasSourceLink ? "Yes"
            : _data.SourceLinkUnavailableReason != null ? $"No ({_data.SourceLinkUnavailableReason})" : "No",
        Builder = _data.Builder,
        Publisher = !string.IsNullOrEmpty(_data.Publisher)
            ? $"{_data.Publisher}{(_data.PublisherVerified ? " (Verified)" : "")}"
            : null,
        Repository = _data.RepositoryVerified ? "nuget.org (Verified)" : null,
        Signature = _data.SignatureStatus,
        RepositoryUrl = _data.RepositoryUrl,
        Warning = _data.WindowsPdbDetected ? "Windows PDB format is not supported by this tool" : null,
        Recommendation = _data.WindowsPdbDetected ? "Consider asking the package maintainer to publish Portable PDBs" : null,
    };

    [MarkoutIgnore]
    public bool HasTypeForwarders => _data.TypeForwarderInspection.HasFindings();

    [MarkoutSection(Name = "Type Forwarders", ShowWhenProperty = nameof(HasTypeForwarders))]
    public List<TypeForwarderRow>? TypeForwardersSection =>
        _data.TypeForwarderInspection.PayloadsForRendering()
            .OrderBy(f => f.TypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.TargetAssembly, StringComparer.OrdinalIgnoreCase)
            .Select(f => new TypeForwarderRow(f.TypeName, f.TargetAssembly))
            .ToList() is { Count: > 0 } rows ? rows : null;

    [MarkoutIgnore]
    public bool HasUnsafeMembers =>
        _data.UnsafeMembers is { Count: > 0 }
        || _data.UnsafeSignatureDecodeStatus is SignatureDecodeStatus.Degraded;

    [MarkoutSection(Name = "Unsafe Members", ShowWhenProperty = nameof(HasUnsafeMembers))]
    public List<UnsafeMemberRow>? UnsafeMembersSection =>
        (_data.UnsafeMembers ?? [])
            .OrderBy(m => m.Member, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.IL, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Reason, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Detail, StringComparer.OrdinalIgnoreCase)
            .Select(m => new UnsafeMemberRow(
                MarkoutInline.Code(m.Member), m.Reason, MarkoutInline.Code(m.Detail), m.Kind,
                m.IL is null ? null : MarkoutInline.Code(m.IL), m.Token is null ? null : MarkoutInline.Code(m.Token)))
            .Concat(_data.UnsafeSignatureDecodeStatus is SignatureDecodeStatus.Degraded
                ? [new UnsafeMemberRow(
                    MarkoutInline.Code("signature scan"),
                    "Decode degraded",
                    MarkoutInline.Code("unsafe-code presence may be incomplete"),
                    "Diagnostic",
                    null,
                    null)]
                : [])
            .ToList();

    public bool HasTopLeverage => _data.TopLeverage is { Count: > 0 };

    // Rows arrive pre-ranked from the scanner; preserve that order (most leveraged first).
    [MarkoutSection(Name = "Top Leverage", ShowWhenProperty = nameof(HasTopLeverage))]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageVisibilityEmpty), nameof(TopLeverageRow.Visibility))]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageGeneratedEmpty), nameof(TopLeverageRow.Generated))]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageStableEmpty), nameof(TopLeverageRow.Stable))]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageSelectorEmpty), nameof(TopLeverageRow.Selector))]
    public List<TopLeverageRow>? TopLeverageSection =>
        _data.TopLeverage?
            .Select(m => new TopLeverageRow(
                MarkoutInline.Code(m.Member),
                m.Callers.ToString(),
                m.RootReach.ToString(),
                m.Fanout.ToString(),
                m.Depth.ToString(),
                m.LoopCalls.ToString(),
                m.Visibility,
                Generated: m.Generated ? "generated" : null,
                Stable: m.Stable is { } stable ? MarkoutInline.Code(stable) : null,
                Selector: m.Selector is { } selector ? MarkoutInline.Code(selector) : null))
            .ToList();

    // Kind-scoped performance sections. The optimization-opportunity scan is holistic; each
    // section renders the subset whose shape maps to it (see PerformanceKinds) with a tight,
    // human column set. Rows arrive pre-ordered by triage priority (in-loop first, then
    // confidence, then root reach). Deep per-row diagnostics remain in the JSON projection.
    // Each section is absent when its kind has no findings (il-offset context-section model).
    private List<PerformanceRow>? PerformanceRowsFor(string section)
    {
        var rows = _data.OptimizationOpportunities?
            .Where(o => PerformanceKinds.SectionForShape(o.Shape) == section)
            .Select(o => new PerformanceRow(
                MarkoutInline.Code(o.Member),
                o.Evidence,
                o.Allocation is null ? null : MarkoutInline.Code(o.Allocation),
                string.IsNullOrEmpty(o.Loop) ? null : o.Loop,
                o.RootReach.ToString(),
                o.Weight,
                o.Confidence))
            .ToList();
        return rows is { Count: > 0 } ? rows : null;
    }

    // Flattens the selected performance kind sections into one kind-labeled list for tabular group
    // output. Iterates PerformanceKinds.Sections (curated order) so rows stay grouped by kind, and
    // reuses PerformanceRowsFor so the per-kind rows, ordering, and inline-code spelling are identical
    // to the markdown sections — only a leading Kind label is added.
    internal List<PerformanceGroupRow> PerformanceGroupRows(IReadOnlyCollection<string> selectedSections)
    {
        var rows = new List<PerformanceGroupRow>();
        foreach (var section in PerformanceKinds.Sections)
        {
            if (!selectedSections.Contains(section))
                continue;
            var kindRows = PerformanceRowsFor(section);
            if (kindRows is null)
                continue;
            var label = PerformanceKinds.KindLabel(section);
            foreach (var row in kindRows)
                rows.Add(new PerformanceGroupRow(
                    label, row.Member, row.Evidence, row.Allocation, row.Loop, row.Reach, row.Weight, row.Confidence));
        }
        return rows;
    }

    [MarkoutIgnore] public bool HasPerformanceBoxing => PerformanceBoxingSection is not null;
    [MarkoutSection(Name = SectionNames.PerformanceBoxing, ShowWhenProperty = nameof(HasPerformanceBoxing))]
    public List<PerformanceRow>? PerformanceBoxingSection => PerformanceRowsFor(SectionNames.PerformanceBoxing);

    [MarkoutIgnore] public bool HasPerformanceArrays => PerformanceArraysSection is not null;
    [MarkoutSection(Name = SectionNames.PerformanceArrays, ShowWhenProperty = nameof(HasPerformanceArrays))]
    public List<PerformanceRow>? PerformanceArraysSection => PerformanceRowsFor(SectionNames.PerformanceArrays);

    [MarkoutIgnore] public bool HasPerformanceClosures => PerformanceClosuresSection is not null;
    [MarkoutSection(Name = SectionNames.PerformanceClosures, ShowWhenProperty = nameof(HasPerformanceClosures))]
    public List<PerformanceRow>? PerformanceClosuresSection => PerformanceRowsFor(SectionNames.PerformanceClosures);

    [MarkoutIgnore] public bool HasPerformanceEnumerators => PerformanceEnumeratorsSection is not null;
    [MarkoutSection(Name = SectionNames.PerformanceEnumerators, ShowWhenProperty = nameof(HasPerformanceEnumerators))]
    public List<PerformanceRow>? PerformanceEnumeratorsSection => PerformanceRowsFor(SectionNames.PerformanceEnumerators);

    [MarkoutIgnore] public bool HasPerformanceLoops => PerformanceLoopsSection is not null;
    [MarkoutSection(Name = SectionNames.PerformanceLoops, ShowWhenProperty = nameof(HasPerformanceLoops))]
    public List<PerformanceRow>? PerformanceLoopsSection => PerformanceRowsFor(SectionNames.PerformanceLoops);

    [MarkoutIgnore] public bool HasPerformanceHotspots => PerformanceHotspotsSection is not null;
    [MarkoutSection(Name = SectionNames.PerformanceHotspots, ShowWhenProperty = nameof(HasPerformanceHotspots))]
    public List<PerformanceRow>? PerformanceHotspotsSection => PerformanceRowsFor(SectionNames.PerformanceHotspots);

    [MarkoutIgnore] public bool HasPerformanceAsync => PerformanceAsyncSection is not null;
    [MarkoutSection(Name = SectionNames.PerformanceAsync, ShowWhenProperty = nameof(HasPerformanceAsync))]
    public List<PerformanceRow>? PerformanceAsyncSection => PerformanceRowsFor(SectionNames.PerformanceAsync);

    [MarkoutIgnore] public bool HasPerformanceOther => PerformanceOtherSection is not null;
    [MarkoutSection(Name = SectionNames.PerformanceOther, ShowWhenProperty = nameof(HasPerformanceOther))]
    public List<PerformanceRow>? PerformanceOtherSection => PerformanceRowsFor(SectionNames.PerformanceOther);

    [MarkoutIgnore]
    public bool HasResourceTriage =>
        _data.ResourceLifecycleInspection?.Value
            is ILInspector.Findings.FindingInspection<
                ILInspector.Analysis.ResourceLifecycleOccurrence>.Complete;

    [MarkoutSection(
        Name = SectionNames.ResourceTriage,
        ShowWhenProperty = nameof(HasResourceTriage),
        EmptyText = "No actionable resource lifecycle candidates found.")]
    public List<ResourceTriageRow> ResourceTriageSection =>
        (_data.ResourceTriage ?? [])
            .SelectMany(row => row.Boundaries.Select(boundary =>
                new ResourceTriageRow(
                    MarkoutInline.Code(row.Member),
                    MarkoutInline.Code(row.Candidate),
                    row.Finding,
                    row.Provenance,
                    row.Resource,
                    row.Shape,
                    row.Impact,
                    row.Actionability,
                    MarkoutInline.Code(boundary.Operation),
                    MarkoutInline.Code($"IL_{row.AcquireOffset:X4}"),
                    MarkoutInline.Code($"IL_{boundary.ILOffset:X4}"),
                    row.Evidence,
                    row.Direction,
                    row.Confidence,
                    row.Visibility,
                    row.Stable is null ? null : MarkoutInline.Code(row.Stable),
                    row.Selector is null ? null : MarkoutInline.Code(row.Selector))))
            .ToList();

    public static bool TopLeverageVisibilityEmpty(List<TopLeverageRow>? rows) => rows is null || rows.All(r => string.IsNullOrEmpty(r.Visibility));
    public static bool TopLeverageGeneratedEmpty(List<TopLeverageRow>? rows) => rows is null || rows.All(r => string.IsNullOrEmpty(r.Generated));
    public static bool TopLeverageStableEmpty(List<TopLeverageRow>? rows) => rows is null || rows.All(r => string.IsNullOrEmpty(r.Stable));
    public static bool TopLeverageSelectorEmpty(List<TopLeverageRow>? rows) => rows is null || rows.All(r => string.IsNullOrEmpty(r.Selector));

    private static int CountOrZero<T>(List<T>? values) => values?.Count ?? 0;

    private static int CountExtensionMethods(List<LibraryExtensionMethodJson>? methods)
        => methods?.Sum(m => m.Overloads ?? 1) ?? 0;

    private static int CountIntegrations(LibraryInspection inspection)
    {
        var findingCount = LibraryIntegrationCatalog.All.Count(
            descriptor => descriptor.HasSignals(inspection));
        if (findingCount > 0)
            return findingCount;

        if (inspection.IntegrationCount > 0)
            return inspection.IntegrationCount;

        return LibraryIntegrationCatalog.CountPresence(inspection);
    }

    private static int CountSwitches(LibraryInspection inspection)
    {
        var count = inspection.SwitchInspection.FindingCount();
        return count > 0 ? count : inspection.SwitchCount;
    }

    private List<(string Kind, string Name, string Shape)> Signals(
        LibraryIntegrationDescriptor descriptor)
    {
        if (_integrationSignals.TryGetValue(descriptor, out var signals))
            return signals;

        signals = descriptor.GetSignals(_data);
        _integrationSignals.Add(descriptor, signals);
        return signals;
    }

    private bool HasSignals(LibraryIntegrationDescriptor descriptor)
        => Signals(descriptor).Count > 0;

    private bool HasApis(LibraryIntegrationDescriptor descriptor)
        => Signals(descriptor).Any(signal => signal.Shape == IntegrationSignalShape.Api);

    private static List<IntegrationSignalRow>? ToIntegrationSignalRows(
        IReadOnlyCollection<(string Kind, string Name, string Shape)> signals)
        => signals
            .Where(signal => signal.Shape == IntegrationSignalShape.Type)
            .OrderBy(signal => signal.Kind, StringComparer.Ordinal)
            .ThenBy(signal => signal.Name, StringComparer.Ordinal)
            .Select(s => new IntegrationSignalRow(s.Kind, MarkoutInline.Code(s.Name)))
            .ToList() is { Count: > 0 } rows ? rows : null;

    private static List<IntegrationApiSignalRow>? ToIntegrationApiSignalRows(
        IReadOnlyCollection<(string Kind, string Name, string Shape)> signals,
        bool includeTypes)
        => signals
            .Where(signal => includeTypes || signal.Shape == IntegrationSignalShape.Api)
            .OrderBy(signal => signal.Kind, StringComparer.Ordinal)
            .ThenBy(signal => signal.Name, StringComparer.Ordinal)
            .Select(s => new IntegrationApiSignalRow(s.Kind, MarkoutInline.Code(s.Name)))
            .ToList() is { Count: > 0 } rows ? rows : null;

    public static bool IntegrationKindIsUniform(List<IntegrationSignalRow>? rows)
        => rows?.Select(row => row.Kind).Distinct(StringComparer.Ordinal).Count() <= 1;

    public static bool IntegrationApiKindIsUniform(List<IntegrationApiSignalRow>? rows)
        => rows?.Select(row => row.Kind).Distinct(StringComparer.Ordinal).Count() <= 1;

    public static bool SwitchKindIsUniform(List<SwitchRow>? rows)
        => rows?.Select(row => row.Kind).Distinct(StringComparer.Ordinal).Count() <= 1;

    // The refined escape kind is only present on Escapes allocations (null for the
    // ~70% that are Unknown/ThrowPath/LocalOnly), so drop the column when no row
    // carries one — matching how the fact tables hide uniformly-empty columns.
    public static bool AllocationContextEscapeKindIsEmpty(List<ILOffsetAllocationContextRow>? rows)
        => rows is null || rows.All(row => string.IsNullOrEmpty(row.EscapeKind));

    // The per-invocation multiplicity is Unknown (null) for allocations whose count
    // can't be proven, so drop the column when no row carries one — matching how the
    // other optional coordinate columns hide when uniformly empty.
    public static bool AllocationContextMultiplicityIsEmpty(List<ILOffsetAllocationContextRow>? rows)
        => rows is null || rows.All(row => string.IsNullOrEmpty(row.Multiplicity));

    // The churned/backing type is only present on known growable-collection allocations,
    // so drop the column when no row carries one.
    public static bool AllocationContextChurnedTypeIsEmpty(List<ILOffsetAllocationContextRow>? rows)
        => rows is null || rows.All(row => string.IsNullOrEmpty(row.ChurnedType));

    private static List<TreeNode> BuildNestedDependencyTree(List<AssemblyReferenceNode> nodes)
    {
        List<TreeNode> result = [];
        int i = 0;
        BuildNestedNodes(nodes, ref i, 0, result);
        return result;
    }

    private static void BuildNestedNodes(List<AssemblyReferenceNode> nodes, ref int index, int currentDepth, List<TreeNode> target)
    {
        while (index < nodes.Count && nodes[index].Depth == currentDepth)
        {
            var node = nodes[index];
            var label = !string.IsNullOrEmpty(node.Company)
                ? $"{node.Name} {node.Version} [{node.Company}]"
                : $"{node.Name} {node.Version}";
            index++;

            List<TreeNode> children = [];
            if (index < nodes.Count && nodes[index].Depth > currentDepth)
            {
                BuildNestedNodes(nodes, ref index, currentDepth + 1, children);
            }

            target.Add(children.Count > 0 ? new TreeNode(label) { Children = children } : new TreeNode(label));
        }
    }
}

[MarkoutSerializable]
public record ReferenceRow(
    string Name,
    string Version,
    [property: MarkoutPropertyName("Public Key Token")] string PublicKeyToken);

[MarkoutSerializable]
public record ExtensionMethodRow(
    string Name,
    string Kind,
    [property: MarkoutPropertyName("Extended Type")] string ExtendedType,
    string Class);

[MarkoutSerializable]
public record ClassifiedMethodRow(
    string Name,
    [property: MarkoutPropertyName("Declaring Type")] string DeclaringType,
    string Signature);

[MarkoutSerializable]
public record PInvokeMethodRow(
    string Name,
    [property: MarkoutPropertyName("Declaring Type")] string DeclaringType,
    string Module,
    string Signature);

[MarkoutSerializable]
public record AsyncMethodRow(
    string Name,
    [property: MarkoutPropertyName("Declaring Type")] string DeclaringType,
    string Kind,
    string Signature);

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ILOffsetSection
{
    public string? Method { get; init; }
    public string? Token { get; init; }
    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get; init; }
    [MarkoutPropertyName("Matched Offset")]
    public string? MatchedOffset { get; init; }
    public string? File { get; init; }
    public int? Line { get; init; }
    public string? Url { get; init; }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ILOffsetMemberContextSection
{
    public string? Assembly { get; init; }
    public string? Type { get; init; }
    [MarkoutPropertyName("Type Kind")]
    public string? TypeKind { get; init; }
    public string? Member { get; init; }
    public string? Signature { get; init; }
    [MarkoutPropertyName("Member Kind")]
    public string? MemberKind { get; init; }
    public string? Visibility { get; init; }
    public string? Static { get; init; }
    public string? Async { get; init; }
    [MarkoutPropertyName("Metadata Token")]
    public string? MetadataToken { get; init; }
    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get; init; }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ILOffsetInstructionContextSection
{
    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get; init; }
    public string? Boundary { get; init; }
    public string? Opcode { get; init; }
    [MarkoutPropertyName("Operand Kind")]
    public string? OperandKind { get; init; }
    public string? Operand { get; init; }
    [MarkoutPropertyName("Operand Token")]
    public string? OperandToken { get; init; }
    [MarkoutPropertyName("Branch Targets")]
    public string? BranchTargets { get; init; }
    [MarkoutPropertyName("Next Offset")]
    public string? NextOffset { get; init; }
    public int? Length { get; init; }
    public int? Block { get; init; }
    [MarkoutPropertyName("Terminates Block")]
    public string? TerminatesBlock { get; init; }
    [MarkoutPropertyName("Falls Through")]
    public string? FallsThrough { get; init; }
}

[MarkoutSerializable]
public record ILOffsetExceptionContextRow(
    int Region,
    [property: MarkoutSkipNull] string? Context,
    [property: MarkoutSkipNull] string? Clause,
    [property: MarkoutPropertyName("Try Range")]
    [property: MarkoutSkipNull] string? TryRange,
    [property: MarkoutPropertyName("Handler Range")]
    [property: MarkoutSkipNull] string? HandlerRange,
    [property: MarkoutPropertyName("Filter Range")]
    [property: MarkoutSkipNull] string? FilterRange,
    [property: MarkoutPropertyName("Caught Type")]
    [property: MarkoutSkipNull] string? CaughtType);

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ILOffsetCallsiteContextSection
{
    [MarkoutPropertyName("Call Offset")]
    public string? CallOffset { get; init; }
    public string? Opcode { get; init; }
    [MarkoutPropertyName("Call Kind")]
    public string? CallKind { get; init; }
    public string? Callee { get; init; }
    [MarkoutPropertyName("Operand Token")]
    public string? OperandToken { get; init; }
    [MarkoutPropertyName("Return Address")]
    public string? ReturnAddress { get; init; }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ILOffsetReturnAddressContextSection
{
    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get; init; }
    [MarkoutPropertyName("Call Offset")]
    public string? CallOffset { get; init; }
    public string? Opcode { get; init; }
    [MarkoutPropertyName("Call Kind")]
    public string? CallKind { get; init; }
    public string? Callee { get; init; }
    [MarkoutPropertyName("Operand Token")]
    public string? OperandToken { get; init; }
}

[MarkoutSerializable]
public record ILOffsetAllocationContextRow(
    [property: MarkoutPropertyName("IL Offset")] string? ILOffset,
    [property: MarkoutPropertyName("Allocation Kind")] string? AllocationKind,
    [property: MarkoutPropertyName("Allocated Type")] string? AllocatedType,
    [property: MarkoutPropertyName("Counted As Heap")] string? CountedAsHeap,
    string? Frequency,
    string? Escape,
    [property: MarkoutPropertyName("Escape Kind")][property: MarkoutSkipNull] string? EscapeKind,
    [property: MarkoutPropertyName("Est Size")] int? EstSize,
    string? SizeTier,
    [property: MarkoutPropertyName("In Loop")] string? InLoop,
    string? Path,
    [property: MarkoutPropertyName("Path Confidence")] string? PathConfidence,
    [property: MarkoutPropertyName("Post Dominance")] string? PostDominance,
    string? Evidence,
    [property: MarkoutSkipNull] string? Multiplicity,
    [property: MarkoutPropertyName("Churned Type")][property: MarkoutSkipNull] string? ChurnedType);

[MarkoutSerializable]
public record ILOffsetSafetyContextRow(
    [property: MarkoutPropertyName("IL Offset")] string? ILOffset,
    [property: MarkoutPropertyName("Safety Kind")] string? SafetyKind,
    string? Operation,
    string? Requirement,
    string? Evidence);

[MarkoutSerializable]
public record ILOffsetCostContextRow(
    [property: MarkoutPropertyName("IL Offset")] string? ILOffset,
    [property: MarkoutPropertyName("Cost Kind")] string? CostKind,
    string? Operation,
    [property: MarkoutPropertyName("In Loop")] string? InLoop,
    string? Evidence);

[MarkoutSerializable]
public record ResourceRow(
    string Name,
    string Visibility,
    string Size);

[MarkoutSerializable]
public record ResourceTriageRow(
    string Member,
    string Candidate,
    string Finding,
    string Provenance,
    string Resource,
    string Shape,
    string Impact,
    string Actionability,
    string Boundary,
    [property: MarkoutPropertyName("Acquire IL")] string AcquireIL,
    [property: MarkoutPropertyName("Boundary IL")] string BoundaryIL,
    string Evidence,
    string Direction,
    string Confidence,
    string? Visibility,
    string? Stable,
    string? Selector);

// Tight, human column set for the kind-scoped performance sections. Deep per-row diagnostics
// (provenance, path counts, post-dominance, token, fix guidance) live in the JSON projection.
[MarkoutSerializable]
public record PerformanceRow(
    string Member,
    string Evidence,
    [property: MarkoutSkipNull] string? Allocation,
    [property: MarkoutSkipNull] string? Loop,
    string Reach,
    [property: MarkoutSkipNull] string? Weight,
    string Confidence);

/// <summary>
/// A <see cref="PerformanceRow"/> prefixed with its kind label, used to flatten the per-kind
/// performance sections into one self-describing tabular table (<c>-S @Performance --tsv</c>/
/// <c>--jsonl</c>/<c>--table</c>). The leading <c>Kind</c> column tells consumers which performance
/// kind each row belongs to, since the flattened table has no per-section headings.
/// </summary>
public record PerformanceGroupRow(
    string Kind,
    string Member,
    string Evidence,
    [property: MarkoutSkipNull] string? Allocation,
    [property: MarkoutSkipNull] string? Loop,
    string Reach,
    [property: MarkoutSkipNull] string? Weight,
    string Confidence);

/// <summary>
/// Single-section view that renders the flattened, kind-labeled performance rows as one table.
/// Used only for tabular group output; markdown keeps the per-kind sections of
/// <see cref="LibraryInspectionView"/> with their <c>## Performance: Kind</c> headings.
/// </summary>
[MarkoutSerializable]
public sealed class PerformanceGroupView
{
    public PerformanceGroupView(List<PerformanceGroupRow> rows) => Performance = rows;

    [MarkoutSection(Name = "Performance")]
    public List<PerformanceGroupRow> Performance { get; }
}

[MarkoutSerializable]
public record CustomAttributeRow(
    string Name,
    string Target,
    string Value);

[MarkoutSerializable]
public record TypeForwarderRow(
    [property: MarkoutPropertyName("Type")] string TypeName,
    [property: MarkoutPropertyName("Target Assembly")] string TargetAssembly);

[MarkoutSerializable]
public record AuditSignalRow(
    string Area,
    string Signal,
    string Value,
    string Evidence);

[MarkoutSerializable]
public record InspectionFailureRow(
    string Section,
    string Finding,
    string Reason);

[MarkoutSerializable]
public record SwitchRow(
    string Kind,
    string Switch,
    [property: MarkoutPropertyName("API")] string Api);

[MarkoutSerializable]
public record IntegrationRow(
    string Integration,
    [property: MarkoutPropertyName("APIs")] int Apis);

[MarkoutSerializable]
public record IntegrationOpportunityRow(
    string Integration,
    [property: MarkoutPropertyName("API")] string Api,
    [property: MarkoutPropertyName("Integration Type")] string IntegrationType,
    [property: MarkoutPropertyName("Look For")] string LookFor);

[MarkoutSerializable]
public record IntegrationSignalRow(
    string Kind,
    string Type);

[MarkoutSerializable]
public record IntegrationApiSignalRow(
    string Kind,
    [property: MarkoutPropertyName("API")] string Api);

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class LibraryInfoSection
{
    public string? Architecture { get; init; }
    public string? AssemblyVersion { get; init; }
    public int AsyncMethods { get; init; }
    public string? Company { get; init; }
    public string? Compilation { get; init; }
    public string? Copyright { get; init; }
    public int CustomAttributes { get; init; }
    [MarkoutBoolFormat("Yes", "No")]
    public bool Deterministic { get; init; }
    public int ExtensionMethods { get; init; }
    [MarkoutBoolFormat("Yes", "No")]
    public bool? Facade { get; init; }
    public string? FileSize { get; init; }
    public string? InformationalVersion { get; init; }
    public int Integrations { get; init; }
    public string? Methods { get; init; }
    public string? Modified { get; init; }
    public string? Name { get; init; }
    public string? Product { get; init; }
    public string? PublicKeyToken { get; init; }
    [MarkoutBoolFormat("Yes", "No")]
    public bool Reproducible { get; init; }
    public int Resources { get; init; }
    public string? Signed { get; init; }
    public string? Source { get; init; }
    public int Switches { get; init; }
    public string? TargetFramework { get; init; }
    public int TypeForwarders { get; init; }
    public string? Types { get; init; }
    public int UnionTypes { get; init; }
    public string? Version { get; init; }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
public record UnionTypeRow(
    string Type,
    string Kind,
    [property: MarkoutPropertyName("IUnion")] string IUnion,
    string Cases);

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class SymbolsSection
{
    public string? Builder { get; init; }
    [MarkoutPropertyName("PDB Format")]
    public string PdbFormat { get; init; } = "Unknown";
    [MarkoutPropertyName("PDB Location")]
    public string PdbLocation { get; init; } = "Unknown";
    [MarkoutPropertyName("PDB Path")]
    public string? PdbPath { get; init; }
    public string? Publisher { get; init; }
    public string? Recommendation { get; init; }
    public string? Repository { get; init; }
    [MarkoutPropertyName("Repository URL")]
    public string? RepositoryUrl { get; init; }
    public string? Signature { get; init; }
    public string? SourceLink { get; init; }
    public string? SymbolServer { get; init; }
    public string? Warning { get; init; }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class SourceLinkAuditSection
{
    public int Embedded { get; init; }
    public int Missing { get; init; }
    public string SourceFiles { get; init; } = "";
    public string Status { get; init; } = "";
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class SourceIntegritySection
{
    [MarkoutPropertyName("CR/LF Mismatch")]
    public string? CrlfMismatch { get; init; }
    public int Mismatched { get; init; }
    [MarkoutPropertyName("Mismatched Files")]
    public string? MismatchedFiles { get; init; }
    public string Status { get; init; } = "";
    public int Unverifiable { get; init; }
    public int Verified { get; init; }
}
