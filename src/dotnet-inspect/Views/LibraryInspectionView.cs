using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.CSharp;
using ILInspector.Metadata;
using InertText;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(FileName), TitleContextProperty = nameof(Tfm), AutoFieldsCount = 9, FieldLayout = FieldLayout.Inline)]
public class LibraryInspectionView
{
    private readonly LibraryInspection _data;
    private readonly bool _topFieldsOnly;
    private readonly bool _includeScannerContext;
    private readonly Dictionary<LibraryIntegrationDescriptor, List<(string Kind, string Name, string Shape)>> _integrationSignals = [];

    public LibraryInspectionView(
        LibraryInspection data, bool topFieldsOnly = false, bool includeScannerContext = true)
    {
        _data = data;
        _topFieldsOnly = topFieldsOnly;
        _includeScannerContext = includeScannerContext;
    }

    [MarkoutIgnore]
    public string? Tfm => _topFieldsOnly ? null : LibraryViewText.Contain(_data.Tfm);

    [MarkoutSection(Name = IntegrationSectionNames.Scan)]
    public List<IntegrationScanRow>? IntegrationScanSection =>
        _data.IntegrationScan?.Signals.Select(signal => new IntegrationScanRow(
            signal.Scanner, signal.Integration, signal.Kind,
            MarkoutInline.Code(signal.Name), signal.Shape)).ToList();

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("File")]
    public string FileName => LibraryViewText.Contain(_data.FileName);

    // ===== Compact context fields and explicit scanner scope =====

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Name => _topFieldsOnly ? LibraryViewText.Contain(_data.AssemblyInfo?.AssemblyName) : null;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Version => _topFieldsOnly ? LibraryViewText.Contain(LibraryInspectionDisplay.ResolveVersion(_data)) : null;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("TFM")]
    [MarkoutSkipNull]
    public string? TargetFramework => _topFieldsOnly ? LibraryViewText.Contain(_data.AssemblyInfo?.TargetFramework) : null;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Arch")]
    [MarkoutSkipNull]
    public string? Architecture => _topFieldsOnly ? LibraryViewText.Contain(_data.AssemblyInfo?.Architecture) : null;

    [MarkoutPropertyName("Size")]
    [MarkoutSkipNull]
    public string? FileSize => _topFieldsOnly ? (_data.FileSize > 0 ? ByteSizeFormatter.FormatBytes(_data.FileSize) : null) : null;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Source => _topFieldsOnly ? LibraryViewText.Contain(_data.Source) : null;

    [MarkoutSkipNull]
    public string? Modified => _topFieldsOnly ? _data.LastModified?.ToString("yyyy-MM-dd") : null;

    [MarkoutSkipNull]
    public string? Scanner => _includeScannerContext
        ? LibraryViewText.Contain(_data.IntegrationScan?.Scanner)
        : null;

    [MarkoutSkipNull]
    [MarkoutPropertyName("Scan Status")]
    public string? ScanStatus => _includeScannerContext ? _data.IntegrationScan?.Status : null;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Library")]
    public string? AssemblySummary => _data.AssemblyInfo switch
    {
        null => null,
        var info => LibraryViewText.Contain(string.Join(", ", new[]
        {
            info.Architecture,
            info.TargetFramework,
            info.CompilationType,
            info.IsSigned ? "Signed" : null
        }.Where(s => !string.IsNullOrEmpty(s))))
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
                LibraryViewText.Field(failure.Section),
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
                MetadataTypeNameFormatter.FormatGenericTypeName(m.DeclaringType),
                m.Kind,
                m.Signature))
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
            .Select(t => new UnionTypeRow(
                t.TypeName,
                t.Kind,
                LibraryViewText.Field(t.ImplementsIUnion ? "Yes" : "No"),
                string.Join(", ", t.CaseTypes)))
            .ToList() is { Count: > 0 } rows ? rows : null;

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
        _data.AssemblyReferenceInspection.PayloadsForRendering().OrderBy(r => r.Name)
            .Select(r => new ReferenceRow(
                r.Name,
                r.Version,
                LibraryViewText.Field(r.PublicKeyToken ?? "-")))
            .ToList() is { Count: > 0 } list ? list : null;

    [MarkoutIgnore]
    public bool HasNonNormalizedPaths => _data.NonNormalizedPaths is { Count: > 0 };

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSection(Name = "Non-normalized Paths", ShowWhenProperty = nameof(HasNonNormalizedPaths))]
    public List<string>? NonNormalizedPathsSection =>
        _data.NonNormalizedPaths?
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => LibraryViewText.Contain(p))
            .ToList();

    [MarkoutIgnore]
    public bool HasSourceLinkDiagnostics =>
        _data.SourceLinkMap?.HasDiagnostics == true;

    [MarkoutSection(
        Name = SectionNames.SourceLinkDiagnostics,
        ShowWhenProperty = nameof(HasSourceLinkDiagnostics))]
    public List<SourceLinkDiagnosticRow>? SourceLinkDiagnosticsSection
    {
        get
        {
            if (!HasSourceLinkDiagnostics || _data.SourceLinkMap is not { } map)
                return null;

            List<SourceLinkDiagnosticRow> rows = [];
            if (map.Error is { } error)
            {
                rows.Add(new SourceLinkDiagnosticRow(
                    "Map error",
                    null,
                    error));
            }

            rows.AddRange(
                (map.RejectedKeys ?? [])
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                    .Select(key => new SourceLinkDiagnosticRow(
                        "Rejected mapping",
                        key,
                        "entry does not conform to the SourceLink document-map schema")));
            return rows;
        }
    }

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
                MetadataTypeNameFormatter.FormatGenericTypeName(m.DeclaringType),
                m.ModuleName ?? "",
                m.Signature))
            .ToList();

    [MarkoutIgnore]
    public bool HasResources => _data.ResourceInspection.HasFindings();

    [MarkoutSection(Name = "Resources", ShowWhenProperty = nameof(HasResources))]
    public List<ResourceRow>? ResourcesSection =>
        _data.ResourceInspection.PayloadsForRendering()
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new ResourceRow(
                r.Name,
                LibraryViewText.Field(r.IsPublic ? "public" : "private"),
                LibraryViewText.Field(
                    r.Size == 0 ? "" : ByteSizeFormatter.FormatBytes(r.Size))))
            .ToList() is { Count: > 0 } rows ? rows : null;

    [MarkoutIgnore]
    public bool HasAuditSignals => _data.AuditSignals is { Count: > 0 };

    [MarkoutSection(Name = "Signals", ShowWhenProperty = nameof(HasAuditSignals))]
    public List<AuditSignalRow>? SignalsSection =>
        _data.AuditSignals?.Select(s => new AuditSignalRow(s.Area, s.Signal, s.Value, s.Evidence)).ToList();

    [MarkoutIgnore]
    public bool HasIdentifierConfusion =>
        IdentifierConfusionAudit.InspectLibrary(_data).Count > 0;

    [MarkoutSection(
        Name = SectionNames.IdentifierConfusion,
        ShowWhenProperty = nameof(HasIdentifierConfusion))]
    public List<IdentifierConfusionRow> IdentifierConfusion =>
        IdentifierConfusionRows.Create(IdentifierConfusionAudit.InspectLibrary(_data));

    [MarkoutIgnore]
    public bool HasSwitches => _data.SwitchInspection.HasFindings();

    [MarkoutSection(Name = "Switches", ShowWhenProperty = nameof(HasSwitches))]
    [MarkoutIgnoreColumnWhen(nameof(SwitchKindIsUniform), "Kind")]
    public List<SwitchRow>? SwitchesSection =>
        _data.SwitchInspection.PayloadsForRendering()
            .OrderBy(s => s.Kind, StringComparer.Ordinal)
            .ThenBy(s => s.Switch, StringComparer.Ordinal)
            .ThenBy(s => s.Api, StringComparer.Ordinal)
            .Select(s => new SwitchRow(s.Kind, s.Switch, s.Api))
            .ToList() is { Count: > 0 } rows ? rows : null;

    [MarkoutIgnore]
    public bool HasIntegrationOpportunities => _data.IntegrationOpportunities is { Count: > 0 };

    [MarkoutSection(Name = IntegrationSectionNames.Opportunities, ShowWhenProperty = nameof(HasIntegrationOpportunities))]
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

    [MarkoutSection(Name = IntegrationSectionNames.AI, ShowWhenProperty = nameof(HasAI))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? AIApiSection => HasAIApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.AI), includeTypes: true) : null;

    [MarkoutSection(Name = IntegrationSectionNames.AI, ShowWhenProperty = nameof(HasAITypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? AITypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.AI));

    [MarkoutIgnore]
    public bool HasAspNetCore => HasSignals(LibraryIntegrationCatalog.AspNetCore);

    [MarkoutIgnore]
    public bool HasAspNetCoreApis => HasApis(LibraryIntegrationCatalog.AspNetCore);

    [MarkoutIgnore]
    public bool HasAspNetCoreTypesOnly => HasAspNetCore && !HasAspNetCoreApis;

    [MarkoutSection(Name = IntegrationSectionNames.AspNetCore, ShowWhenProperty = nameof(HasAspNetCoreApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? AspNetCoreApiSection => HasAspNetCoreApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.AspNetCore), includeTypes: true) : null;

    [MarkoutSection(Name = IntegrationSectionNames.AspNetCore, ShowWhenProperty = nameof(HasAspNetCoreTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? AspNetCoreTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.AspNetCore));

    [MarkoutIgnore]
    public bool HasAuthentication => HasSignals(LibraryIntegrationCatalog.Authentication);

    [MarkoutIgnore]
    public bool HasAuthenticationApis => HasApis(LibraryIntegrationCatalog.Authentication);

    [MarkoutIgnore]
    public bool HasAuthenticationTypesOnly => HasAuthentication && !HasAuthenticationApis;

    [MarkoutSection(Name = IntegrationSectionNames.Authentication, ShowWhenProperty = nameof(HasAuthenticationApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? AuthenticationApiSection => HasAuthenticationApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Authentication), includeTypes: true) : null;

    [MarkoutSection(Name = IntegrationSectionNames.Authentication, ShowWhenProperty = nameof(HasAuthenticationTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? AuthenticationTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Authentication));

    [MarkoutIgnore]
    public bool HasAspire => HasSignals(LibraryIntegrationCatalog.Aspire);

    [MarkoutIgnore]
    public bool HasAspireApis => HasApis(LibraryIntegrationCatalog.Aspire);

    [MarkoutIgnore]
    public bool HasAspireTypesOnly => HasAspire && !HasAspireApis;

    [MarkoutSection(Name = IntegrationSectionNames.Aspire, ShowWhenProperty = nameof(HasAspireApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? AspireApiSection => HasAspireApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Aspire), includeTypes: true) : null;

    [MarkoutSection(Name = IntegrationSectionNames.Aspire, ShowWhenProperty = nameof(HasAspireTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? AspireTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Aspire));

    [MarkoutIgnore]
    public bool HasConfiguration => HasSignals(LibraryIntegrationCatalog.Configuration);

    [MarkoutIgnore]
    public bool HasConfigurationApis => HasApis(LibraryIntegrationCatalog.Configuration);

    [MarkoutIgnore]
    public bool HasConfigurationTypesOnly => HasConfiguration && !HasConfigurationApis;

    [MarkoutSection(Name = IntegrationSectionNames.Configuration, ShowWhenProperty = nameof(HasConfigurationApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? ConfigurationApiSection => HasConfigurationApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Configuration), includeTypes: true) : null;

    [MarkoutSection(Name = IntegrationSectionNames.Configuration, ShowWhenProperty = nameof(HasConfigurationTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? ConfigurationTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Configuration));

    [MarkoutIgnore]
    public bool HasDependencyInjection => HasSignals(LibraryIntegrationCatalog.DependencyInjection);

    [MarkoutIgnore]
    public bool HasDependencyInjectionApis => HasApis(LibraryIntegrationCatalog.DependencyInjection);

    [MarkoutIgnore]
    public bool HasDependencyInjectionTypesOnly => HasDependencyInjection && !HasDependencyInjectionApis;

    [MarkoutSection(Name = IntegrationSectionNames.DependencyInjection, ShowWhenProperty = nameof(HasDependencyInjectionApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? DependencyInjectionApiSection => HasDependencyInjectionApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.DependencyInjection), includeTypes: false) : null;

    [MarkoutSection(Name = IntegrationSectionNames.DependencyInjection, ShowWhenProperty = nameof(HasDependencyInjectionTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? DependencyInjectionTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.DependencyInjection));

    [MarkoutIgnore]
    public bool HasLogging => HasSignals(LibraryIntegrationCatalog.Logging);

    [MarkoutIgnore]
    public bool HasLoggingApis => HasApis(LibraryIntegrationCatalog.Logging);

    [MarkoutIgnore]
    public bool HasLoggingTypesOnly => HasLogging && !HasLoggingApis;

    [MarkoutSection(Name = IntegrationSectionNames.Logging, ShowWhenProperty = nameof(HasLoggingApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? LoggingApiSection => HasLoggingApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Logging), includeTypes: false) : null;

    [MarkoutSection(Name = IntegrationSectionNames.Logging, ShowWhenProperty = nameof(HasLoggingTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? LoggingTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Logging));

    [MarkoutIgnore]
    public bool HasOpenTelemetry => HasSignals(LibraryIntegrationCatalog.OpenTelemetry);

    [MarkoutIgnore]
    public bool HasOpenTelemetryApis => HasApis(LibraryIntegrationCatalog.OpenTelemetry);

    [MarkoutIgnore]
    public bool HasOpenTelemetryTypesOnly => HasOpenTelemetry && !HasOpenTelemetryApis;

    [MarkoutSection(Name = IntegrationSectionNames.OpenTelemetry, ShowWhenProperty = nameof(HasOpenTelemetryApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? OpenTelemetryApiSection => HasOpenTelemetryApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.OpenTelemetry), includeTypes: true) : null;

    [MarkoutSection(Name = IntegrationSectionNames.OpenTelemetry, ShowWhenProperty = nameof(HasOpenTelemetryTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? OpenTelemetryTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.OpenTelemetry));

    [MarkoutIgnore]
    public bool HasOpenApi => HasSignals(LibraryIntegrationCatalog.OpenAPI);

    [MarkoutIgnore]
    public bool HasOpenApiApis => HasApis(LibraryIntegrationCatalog.OpenAPI);

    [MarkoutIgnore]
    public bool HasOpenApiTypesOnly => HasOpenApi && !HasOpenApiApis;

    [MarkoutSection(Name = IntegrationSectionNames.OpenAPI, ShowWhenProperty = nameof(HasOpenApiApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? OpenApiApiSection => HasOpenApiApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.OpenAPI), includeTypes: true) : null;

    [MarkoutSection(Name = IntegrationSectionNames.OpenAPI, ShowWhenProperty = nameof(HasOpenApiTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? OpenApiTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.OpenAPI));

    [MarkoutIgnore]
    public bool HasOptions => HasSignals(LibraryIntegrationCatalog.Options);

    [MarkoutIgnore]
    public bool HasOptionsApis => HasApis(LibraryIntegrationCatalog.Options);

    [MarkoutIgnore]
    public bool HasOptionsTypesOnly => HasOptions && !HasOptionsApis;

    [MarkoutSection(Name = IntegrationSectionNames.Options, ShowWhenProperty = nameof(HasOptionsApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? OptionsApiSection => HasOptionsApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Options), includeTypes: false) : null;

    [MarkoutSection(Name = IntegrationSectionNames.Options, ShowWhenProperty = nameof(HasOptionsTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? OptionsTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Options));

    [MarkoutIgnore]
    public bool HasHosting => HasSignals(LibraryIntegrationCatalog.Hosting);

    [MarkoutIgnore]
    public bool HasHostingApis => HasApis(LibraryIntegrationCatalog.Hosting);

    [MarkoutIgnore]
    public bool HasHostingTypesOnly => HasHosting && !HasHostingApis;

    [MarkoutSection(Name = IntegrationSectionNames.Hosting, ShowWhenProperty = nameof(HasHostingApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? HostingApiSection => HasHostingApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.Hosting), includeTypes: false) : null;

    [MarkoutSection(Name = IntegrationSectionNames.Hosting, ShowWhenProperty = nameof(HasHostingTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? HostingTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.Hosting));

    [MarkoutIgnore]
    public bool HasHealthChecks => HasSignals(LibraryIntegrationCatalog.HealthChecks);

    [MarkoutIgnore]
    public bool HasHealthChecksApis => HasApis(LibraryIntegrationCatalog.HealthChecks);

    [MarkoutIgnore]
    public bool HasHealthChecksTypesOnly => HasHealthChecks && !HasHealthChecksApis;

    [MarkoutSection(Name = IntegrationSectionNames.HealthChecks, ShowWhenProperty = nameof(HasHealthChecksApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? HealthChecksApiSection => HasHealthChecksApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.HealthChecks), includeTypes: false) : null;

    [MarkoutSection(Name = IntegrationSectionNames.HealthChecks, ShowWhenProperty = nameof(HasHealthChecksTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? HealthChecksTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.HealthChecks));

    [MarkoutIgnore]
    public bool HasHttpClient => HasSignals(LibraryIntegrationCatalog.HttpClient);

    [MarkoutIgnore]
    public bool HasHttpClientApis => HasApis(LibraryIntegrationCatalog.HttpClient);

    [MarkoutIgnore]
    public bool HasHttpClientTypesOnly => HasHttpClient && !HasHttpClientApis;

    [MarkoutSection(Name = IntegrationSectionNames.HttpClient, ShowWhenProperty = nameof(HasHttpClientApis))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationApiKindIsUniform), "Kind")]
    public List<IntegrationApiSignalRow>? HttpClientApiSection => HasHttpClientApis ? ToIntegrationApiSignalRows(Signals(LibraryIntegrationCatalog.HttpClient), includeTypes: true) : null;

    [MarkoutSection(Name = IntegrationSectionNames.HttpClient, ShowWhenProperty = nameof(HasHttpClientTypesOnly))]
    [MarkoutIgnoreColumnWhen(nameof(IntegrationKindIsUniform), "Kind")]
    public List<IntegrationSignalRow>? HttpClientTypeSection => ToIntegrationSignalRows(Signals(LibraryIntegrationCatalog.HttpClient));

    [MarkoutIgnore]
    public bool HasSourceLinkAudit => _data.AllSourcesAccessible.HasValue || _data.TotalSourceFiles > 0;

    [MarkoutSection(Name = SectionNames.SourceLinkFiles, EmptyText = "No SourceLink source files found for this library.")]
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

    [MarkoutSection(Name = SectionNames.SourceLinkAvailability, ShowWhenProperty = nameof(HasSourceLinkAudit))]
    public SourceLinkAuditSection? SourceLinkAuditSection => !HasSourceLinkAudit ? null : new SourceLinkAuditSection
    {
        StatusText = LibraryViewText.Field(
            _data.AllSourcesAccessible == true ? "Complete" : "Partial"),
        SourceFilesText = LibraryViewText.Field(
            $"{_data.AccessibleSourceFiles}/{_data.TotalSourceFiles} available"),
        Embedded = _data.EmbeddedSourceFiles,
        Missing = _data.MissingSourceFiles?.Count ?? 0
    };

    [MarkoutIgnore]
    public bool HasSourceIntegrity => _data.SourceIntegrityChecked;

    [MarkoutSection(Name = SectionNames.SourceLinkIntegrity, ShowWhenProperty = nameof(HasSourceIntegrity))]
    public SourceIntegritySection? SourceIntegritySection => !HasSourceIntegrity ? null : new SourceIntegritySection
    {
        CrlfMismatchText = _data.SourceIntegrityLineEndingNormalized > 0
            ? LibraryViewText.Field(
                $"{_data.SourceIntegrityLineEndingNormalized} normalized")
            : null,
        Mismatched = _data.SourceIntegrityMismatched,
        MismatchedFileTexts = _data.SourceIntegrityMismatches is { Count: > 0 } mismatches
            ? mismatches.Select(LibraryViewText.Field).ToList()
            : null,
        StatusText = LibraryViewText.Field(
            _data.SourceIntegrityMismatched > 0 ? "Mismatch"
                : _data.SourceIntegrityUnverifiable > 0 ? "Partial" : "Verified"),
        Unverifiable = _data.SourceIntegrityUnverifiable,
        Verified = _data.SourceIntegrityVerified,
    };

    [MarkoutIgnore]
    public bool HasMissingSourceFiles => _data.MissingSourceFiles is { Count: > 0 };

    [MarkoutSection(Name = SectionNames.SourceLinkMissingFiles, ShowWhenProperty = nameof(HasMissingSourceFiles))]
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
        (_data.UnsafeEvidenceInspection is null
            ? _data.UnsafeMembers is { Count: > 0 }
            : _data.UnsafeEvidenceInspection.HasFindings())
        || _data.UnsafeSignatureDecodeStatus is SignatureDecodeStatus.Degraded;

    [MarkoutSection(Name = "Unsafe Members", ShowWhenProperty = nameof(HasUnsafeMembers))]
    public List<UnsafeMemberRow>? UnsafeMembersSection => UnsafeMemberRows()
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

    private IEnumerable<UnsafeMemberRow> UnsafeMemberRows()
    {
        if (_data.UnsafeEvidenceInspection is not null)
        {
            return _data.UnsafeEvidenceInspection.PayloadsForRendering()
                .OrderBy(
                    evidence => ApiOutputFormatter.FormatMethod(evidence.Member),
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    evidence => evidence.ILOffset is { } offset
                        ? $"IL_{offset:X4}"
                        : null,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(evidence => evidence.Reason, StringComparer.OrdinalIgnoreCase)
                .ThenBy(evidence => evidence.Detail, StringComparer.OrdinalIgnoreCase)
                .Select(evidence =>
                    ApiOutputFormatter.ToUnsafeMemberRow(
                        evidence,
                        includeDeclaringType: true));
        }

        return (_data.UnsafeMembers ?? [])
            .OrderBy(m => m.Member, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.IL, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Reason, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Detail, StringComparer.OrdinalIgnoreCase)
            .Select(m => new UnsafeMemberRow(
                MarkoutInline.Code(m.Member),
                m.Reason,
                MarkoutInline.Code(m.Detail),
                m.Kind,
                m.IL is null ? null : MarkoutInline.Code(m.IL),
                m.Token is null ? null : MarkoutInline.Code(m.Token)));
    }

    [MarkoutSection(
        Name = SectionNames.BodyShapes,
        EmptyText = "No matching body shapes found.")]
    public List<BodyShapeRow>? BodyShapesSection =>
        _data.EffectiveBodyShapeSearchResult?.Matches?
            .Select(BodyShapeRow.FromMatch)
            .ToList();

    public bool HasTopLeverage =>
        _data.TopLeverageQueryResult is TopLeverageResult.Available
            { Methods.IsEmpty: false };

    // Rows arrive pre-ranked from Analysis; preserve that order (most leveraged first).
    [MarkoutSection(Name = "Top Leverage", ShowWhenProperty = nameof(HasTopLeverage))]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageVisibilityEmpty), nameof(TopLeverageRow.Visibility))]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageGeneratedEmpty), nameof(TopLeverageRow.Generated))]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageStableEmpty), nameof(TopLeverageRow.Stable))]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageSelectorEmpty), nameof(TopLeverageRow.Selector))]
    public List<TopLeverageRow>? TopLeverageSection
    {
        get
        {
            if (_data.TopLeverageQueryResult is not TopLeverageResult.Available available)
                return null;

            var drillByToken = _data.TopLeverageDrillMap;
            var rows = available.Methods
                .Select(entry =>
                {
                    (string? Stable, string Visibility, string Selector) drill = default;
                    drillByToken?.TryGetValue(entry.Method.MetadataToken, out drill);
                    return new TopLeverageRow(
                        MarkoutInline.Code(ApiOutputFormatter.FormatMethod(entry.Method)),
                        entry.DirectCallerCount.ToString(),
                        entry.RootReach.ToString(),
                        entry.Fanout.ToString(),
                        entry.MaxDepth.ToString(),
                        entry.LoopCallCount.ToString(),
                        drill.Visibility,
                        Generated: LibraryMetadataService.IsGeneratedMethod(
                            entry.Method,
                            available.GeneratedFrameworkTypes)
                                ? "generated"
                                : null,
                        Stable: drill.Stable is { } stable
                            ? MarkoutInline.Code(stable)
                            : null,
                        Selector: drill.Selector is { } selector
                            ? MarkoutInline.Code(selector)
                            : null);
                })
                .ToList();
            return rows.Count > 0 ? rows : null;
        }
    }

    // Kind-scoped performance sections. The optimization-opportunity scan is holistic; each
    // section renders the subset whose shape maps to it (see PerformanceKinds) with a tight,
    // human column set. Rows arrive pre-ordered by triage priority. Deep per-row diagnostics
    // remain in the JSON projection.
    // Each section is absent when its kind has no findings (il-offset context-section model).
    private List<PerformanceRow>? PerformanceRowsFor(string section)
    {
        var rows = _data.PerformanceTriageOpportunities
            .Where(o => PerformanceKinds.SectionForShape(o.Shape) == section)
            .Select(o => new PerformanceRow(
                MarkoutInline.CodeText(LibraryViewText.Field(
                    LibraryMetadataService.FormatMethod(o.Method))),
                MarkoutInline.CodeText(LibraryViewText.Field(o.Evidence)),
                o.RuntimeAllocationType is null
                    ? null
                    : MarkoutInline.CodeText(
                        LibraryViewText.Field(o.RuntimeAllocationType)),
                LibraryMetadataService.IteratesInLoop(o) ? "loop" : null,
                LibraryViewText.Field(o.RootReach.ToString()),
                o.Weight,
                LibraryMetadataService.TriagePriority(o),
                o.Confidence))
            .ToList();
        return rows is { Count: > 0 } ? rows : null;
    }

    // Flattens selected performance kinds without regrouping them. The source list is globally
    // ranked, so preserving that order ensures --top and the first rendered rows agree.
    internal List<PerformanceGroupRow> PerformanceGroupRows(IReadOnlyCollection<string> selectedSections)
    {
        var rows = new List<PerformanceGroupRow>();
        foreach (var opportunity in _data.PerformanceTriageOpportunities)
        {
            var section = PerformanceKinds.SectionForShape(opportunity.Shape);
            if (!selectedSections.Contains(section))
                continue;
            rows.Add(new PerformanceGroupRow(
                LibraryViewText.Field(PerformanceKinds.KindLabel(section)),
                MarkoutInline.CodeText(LibraryViewText.Field(
                    LibraryMetadataService.FormatMethod(opportunity.Method))),
                MarkoutInline.CodeText(LibraryViewText.Field(opportunity.Evidence)),
                opportunity.RuntimeAllocationType is null
                    ? null
                    : MarkoutInline.CodeText(
                        LibraryViewText.Field(
                            opportunity.RuntimeAllocationType)),
                LibraryMetadataService.IteratesInLoop(opportunity)
                    ? LibraryViewText.Field("loop")
                    : null,
                LibraryViewText.Field(opportunity.RootReach.ToString()),
                opportunity.Weight is null
                    ? null
                    : LibraryViewText.Field(opportunity.Weight),
                LibraryViewText.Field(
                    LibraryMetadataService.TriagePriority(opportunity)),
                LibraryViewText.Field(opportunity.Confidence)));
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
    public bool HasResourceTriage => ResourceTriageSection.Count > 0;

    [MarkoutSection(
        Name = SectionNames.ArrayPoolEscapes,
        ShowWhenProperty = nameof(HasResourceTriage))]
    public List<ResourceTriageRow> ResourceTriageSection =>
        ResourceTriageSummaries()
            .SelectMany(row => row.Boundaries.Select(boundary =>
                new ResourceTriageRow(
                    MarkoutInline.CodeText(LibraryViewText.Field(row.Member)),
                    MarkoutInline.CodeText(LibraryViewText.Field(row.Candidate)),
                    row.Finding,
                    row.Provenance,
                    row.Resource,
                    row.Shape,
                    row.Impact,
                    row.Actionability,
                    MarkoutInline.CodeText(LibraryViewText.Field(boundary.Operation)),
                    MarkoutInline.CodeText(
                        LibraryViewText.Field($"IL_{row.AcquireOffset:X4}")),
                    MarkoutInline.CodeText(
                        LibraryViewText.Field($"IL_{boundary.ILOffset:X4}")),
                    row.Evidence,
                    row.Direction,
                    row.Confidence,
                    row.Visibility,
                    row.Stable is null ? null : MarkoutInline.Code(row.Stable),
                    row.Selector is null ? null : MarkoutInline.Code(row.Selector))))
            .ToList();

    private IEnumerable<ResourceTriageSummary> ResourceTriageSummaries()
    {
        if (_data.ResourceTriageQueryResult
            is ResourceTriageResult.Available)
        {
            var drillByToken = _data.ResourceTriageDrillMap
                ?? throw new InvalidOperationException(
                    "Typed Resource Triage results require CLI drill coordinates.");
            return _data.ResourceTriageAssessments.Select(
                assessment =>
                    LibraryMetadataService.ProjectResourceTriageAssessment(
                        assessment,
                        drillByToken));
        }

        return _data.ResourceTriage ?? [];
    }

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

    internal static List<TreeNode> BuildNestedReferenceTree(List<AssemblyReferenceNode> nodes)
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
            var label = ReferenceTreeText(node);
            label = LibraryViewText.Contain(label);
            index++;

            List<TreeNode> children = [];
            if (index < nodes.Count && nodes[index].Depth > currentDepth)
            {
                BuildNestedNodes(nodes, ref index, currentDepth + 1, children);
            }

            target.Add(children.Count > 0 ? new TreeNode(label) { Children = children } : new TreeNode(label));
        }
    }

    internal static string ReferenceTreeText(AssemblyReferenceNode node)
    {
        string label = !string.IsNullOrEmpty(node.Company)
                ? $"{node.Name} {node.Version} [{node.Company}]"
                : $"{node.Name} {node.Version}";
        return node.ResolutionFailure is { } failure
            ? $"{label} ({failure.ToString().ToLowerInvariant()})"
            : label;
    }
}

/// <summary>
/// Containment for text rendered by <see cref="LibraryInspectionView"/>.
/// Its rows embed untrusted assembly metadata — method, type, module, and
/// attribute names — which can carry line terminators, ANSI escapes, or bidi
/// overrides that break out of a Markdown table cell (issue #3319).
/// Containment lives on the display records themselves rather than at the row
/// construction sites, so a new call site cannot reopen the hole. These records
/// are presentation-only, never identity. Typed values also disambiguate
/// literal canonical escape spellings so their visual form remains invertible.
/// </summary>
internal static class LibraryViewText
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Contain(string? value) => value is null ? null : CSharpIdentifier.ContainRenderedText(value);

    public static InertString Field(string value) =>
        new(TextPolicy.Field, value);

    public static string DocumentTitle(LibraryInspection inspection)
    {
        var fileName = Contain(inspection.FileName) ?? string.Empty;
        return Contain(inspection.Tfm) is { Length: > 0 } tfm
            ? $"{fileName} ({tfm})"
            : fileName;
    }

    public static string DocumentTitle(LibraryInspectionView view) =>
        view.Tfm is { Length: > 0 } tfm
            ? $"{view.FileName} ({tfm})"
            : view.FileName;
}

[MarkoutSerializable]
public record ReferenceRow(
    string Name,
    string Version,
    InertString PublicKeyTokenText)
{
    public ReferenceRow(string name, string version, string publicKeyToken)
        : this(name, version, LibraryViewText.Field(publicKeyToken))
    {
    }

    /// <inheritdoc cref="LibraryViewText"/>
    public string Name { get; init; } = LibraryViewText.Contain(Name);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Version { get; init; } = LibraryViewText.Contain(Version);

    [MarkoutIgnore, JsonIgnore]
    public InertString PublicKeyTokenText { get; init; } = PublicKeyTokenText;

    [MarkoutPropertyName("Public Key Token")]
    public string PublicKeyToken => PublicKeyTokenText.ToString();
}

[MarkoutSerializable]
public record ExtensionMethodRow(
    string Name,
    string Kind,
    string ExtendedType,
    string Class)
{
    /// <inheritdoc cref="LibraryViewText"/>
    public string Name { get; init; } = LibraryViewText.Contain(Name);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Kind { get; init; } = LibraryViewText.Contain(Kind);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Extended Type")]
    public string ExtendedType { get; init; } = LibraryViewText.Contain(ExtendedType);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Class { get; init; } = LibraryViewText.Contain(Class);
}

[MarkoutSerializable]
public record ClassifiedMethodRow(
    string Name,
    InertString DeclaringTypeText,
    InertString SignatureText)
{
    public ClassifiedMethodRow(
        string name,
        string declaringType,
        string signature)
        : this(
            name,
            LibraryViewText.Field(declaringType),
            LibraryViewText.Field(signature))
    {
    }

    /// <inheritdoc cref="LibraryViewText"/>
    public string Name { get; init; } = LibraryViewText.Contain(Name);

    [MarkoutIgnore, JsonIgnore]
    public InertString DeclaringTypeText { get; init; } = DeclaringTypeText;

    [MarkoutPropertyName("Declaring Type")]
    public string DeclaringType => DeclaringTypeText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString SignatureText { get; init; } = SignatureText;

    public string Signature => SignatureText.ToString();
}

[MarkoutSerializable]
public record PInvokeMethodRow(
    string Name,
    string DeclaringType,
    string Module,
    string Signature)
{
    /// <summary>
    /// Crosses exact classified-method evidence into field-safe presentation text.
    /// Gate: LibraryFindingConsumerTests.ClassifiedMethodsQueryProjection_PreservesIdentityUntilInertViewBoundary.
    /// </summary>
    public string Name { get; init; } =
        new InertString(TextPolicy.Field, Name).ToString();

    [MarkoutPropertyName("Declaring Type")]
    public string DeclaringType { get; init; } =
        MarkoutInline.Code(
            new InertString(TextPolicy.Field, DeclaringType).ToString());

    /// <inheritdoc cref="Name"/>
    public string Module { get; init; } =
        new InertString(TextPolicy.Field, Module).ToString();

    /// <inheritdoc cref="Name"/>
    public string Signature { get; init; } =
        MarkoutInline.Code(
            new InertString(TextPolicy.Field, Signature).ToString());
}

[MarkoutSerializable]
public record AsyncMethodRow(
    string Name,
    string DeclaringType,
    string Kind,
    string Signature)
{
    /// <inheritdoc cref="PInvokeMethodRow.Name"/>
    public string Name { get; init; } =
        new InertString(TextPolicy.Field, Name).ToString();

    [MarkoutPropertyName("Declaring Type")]
    public string DeclaringType { get; init; } =
        MarkoutInline.Code(
            new InertString(TextPolicy.Field, DeclaringType).ToString());

    /// <inheritdoc cref="PInvokeMethodRow.Name"/>
    public string Kind { get; init; } =
        new InertString(TextPolicy.Field, Kind).ToString();

    /// <inheritdoc cref="PInvokeMethodRow.Name"/>
    public string Signature { get; init; } =
        MarkoutInline.Code(
            new InertString(TextPolicy.Field, Signature).ToString());
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ILOffsetSection
{
    private readonly string? _method;
    private readonly string? _token;
    private readonly string? _iLOffset;
    private readonly string? _matchedOffset;
    private readonly string? _file;
    private readonly string? _url;

    /// <inheritdoc cref="LibraryViewText"/>
    /// <remarks>
    /// <see cref="File"/> and <see cref="Url"/> are the two properties here
    /// that a hostile assembly controls outright: both are reconstructed from
    /// the PDB's document table and its SourceLink map, neither of which any
    /// compiler validates. This section sits between two neighbours that
    /// already contain every string they render, and was the one that did not,
    /// which is the shape issue #3319 keeps rediscovering -- an obligation
    /// spelled once per class disagrees with itself at the class nobody
    /// revisited.
    /// </remarks>
    public string? Method { get => _method; init => _method = LibraryViewText.Contain(value); }
    public string? Token { get => _token; init => _token = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get => _iLOffset; init => _iLOffset = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Matched Offset")]
    public string? MatchedOffset { get => _matchedOffset; init => _matchedOffset = LibraryViewText.Contain(value); }
    public string? File { get => _file; init => _file = LibraryViewText.Contain(value); }
    public int? Line { get; init; }
    public string? Url { get => _url; init => _url = LibraryViewText.Contain(value); }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ILOffsetMemberContextSection
{
    private readonly string? _assembly;
    private readonly string? _type;
    private readonly string? _typeKind;
    private readonly string? _member;
    private readonly string? _signature;
    private readonly string? _memberKind;
    private readonly string? _visibility;
    private readonly string? _static;
    private readonly string? _async;
    private readonly string? _metadataToken;
    private readonly string? _iLOffset;

    public string? Assembly { get => _assembly; init => _assembly = LibraryViewText.Contain(value); }
    public string? Type { get => _type; init => _type = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Type Kind")]
    public string? TypeKind { get => _typeKind; init => _typeKind = LibraryViewText.Contain(value); }
    public string? Member { get => _member; init => _member = LibraryViewText.Contain(value); }
    public string? Signature { get => _signature; init => _signature = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Member Kind")]
    public string? MemberKind { get => _memberKind; init => _memberKind = LibraryViewText.Contain(value); }
    public string? Visibility { get => _visibility; init => _visibility = LibraryViewText.Contain(value); }
    public string? Static { get => _static; init => _static = LibraryViewText.Contain(value); }
    public string? Async { get => _async; init => _async = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Metadata Token")]
    public string? MetadataToken { get => _metadataToken; init => _metadataToken = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get => _iLOffset; init => _iLOffset = LibraryViewText.Contain(value); }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ILOffsetInstructionContextSection
{
    private readonly string? _iLOffset;
    private readonly string? _boundary;
    private readonly string? _opcode;
    private readonly string? _operandKind;
    private readonly string? _operand;
    private readonly string? _operandToken;
    private readonly string? _branchTargets;
    private readonly string? _nextOffset;
    private readonly string? _terminatesBlock;
    private readonly string? _fallsThrough;

    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get => _iLOffset; init => _iLOffset = LibraryViewText.Contain(value); }
    public string? Boundary { get => _boundary; init => _boundary = LibraryViewText.Contain(value); }
    public string? Opcode { get => _opcode; init => _opcode = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Operand Kind")]
    public string? OperandKind { get => _operandKind; init => _operandKind = LibraryViewText.Contain(value); }
    public string? Operand { get => _operand; init => _operand = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Operand Token")]
    public string? OperandToken { get => _operandToken; init => _operandToken = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Branch Targets")]
    public string? BranchTargets { get => _branchTargets; init => _branchTargets = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Next Offset")]
    public string? NextOffset { get => _nextOffset; init => _nextOffset = LibraryViewText.Contain(value); }
    public int? Length { get; init; }
    public int? Block { get; init; }
    [MarkoutPropertyName("Terminates Block")]
    public string? TerminatesBlock { get => _terminatesBlock; init => _terminatesBlock = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Falls Through")]
    public string? FallsThrough { get => _fallsThrough; init => _fallsThrough = LibraryViewText.Contain(value); }
}

[MarkoutSerializable]
public record ILOffsetExceptionContextRow(
    int Region,
    string? Context,
    string? Clause,
    string? TryRange,
    string? HandlerRange,
    string? FilterRange,
    string? CaughtType)
{
    public int Region { get; init; } = Region;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Context { get; init; } = LibraryViewText.Contain(Context);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Clause { get; init; } = LibraryViewText.Contain(Clause);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Try Range")]
    [MarkoutSkipNull]
    public string? TryRange { get; init; } = LibraryViewText.Contain(TryRange);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Handler Range")]
    [MarkoutSkipNull]
    public string? HandlerRange { get; init; } = LibraryViewText.Contain(HandlerRange);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Filter Range")]
    [MarkoutSkipNull]
    public string? FilterRange { get; init; } = LibraryViewText.Contain(FilterRange);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Caught Type")]
    [MarkoutSkipNull]
    public string? CaughtType { get; init; } = LibraryViewText.Contain(CaughtType);
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ILOffsetCallsiteContextSection
{
    private readonly string? _callOffset;
    private readonly string? _opcode;
    private readonly string? _callKind;
    private readonly string? _callee;
    private readonly string? _operandToken;
    private readonly string? _returnAddress;

    [MarkoutPropertyName("Call Offset")]
    public string? CallOffset { get => _callOffset; init => _callOffset = LibraryViewText.Contain(value); }
    public string? Opcode { get => _opcode; init => _opcode = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Call Kind")]
    public string? CallKind { get => _callKind; init => _callKind = LibraryViewText.Contain(value); }
    public string? Callee { get => _callee; init => _callee = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Operand Token")]
    public string? OperandToken { get => _operandToken; init => _operandToken = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Return Address")]
    public string? ReturnAddress { get => _returnAddress; init => _returnAddress = LibraryViewText.Contain(value); }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ILOffsetReturnAddressContextSection
{
    private readonly string? _ilOffset;
    private readonly string? _callOffset;
    private readonly string? _opcode;
    private readonly string? _callKind;
    private readonly string? _callee;
    private readonly string? _operandToken;

    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get => _ilOffset; init => _ilOffset = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Call Offset")]
    public string? CallOffset { get => _callOffset; init => _callOffset = LibraryViewText.Contain(value); }
    public string? Opcode { get => _opcode; init => _opcode = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Call Kind")]
    public string? CallKind { get => _callKind; init => _callKind = LibraryViewText.Contain(value); }
    public string? Callee { get => _callee; init => _callee = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Operand Token")]
    public string? OperandToken { get => _operandToken; init => _operandToken = LibraryViewText.Contain(value); }
}

[MarkoutSerializable]
public record ILOffsetAllocationContextRow(
    string? ILOffset,
    string? AllocationKind,
    string? AllocatedType,
    string? CountedAsHeap,
    string? Frequency,
    string? Escape,
    string? EscapeKind,
    int? EstSize,
    string? SizeTier,
    string? InLoop,
    string? Path,
    string? PathConfidence,
    string? PostDominance,
    string? Evidence,
    string? Multiplicity,
    string? ChurnedType)
{
    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get; init; } = LibraryViewText.Contain(ILOffset);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Allocation Kind")]
    public string? AllocationKind { get; init; } = LibraryViewText.Contain(AllocationKind);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Allocated Type")]
    public string? AllocatedType { get; init; } = LibraryViewText.Contain(AllocatedType);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Counted As Heap")]
    public string? CountedAsHeap { get; init; } = LibraryViewText.Contain(CountedAsHeap);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Frequency { get; init; } = LibraryViewText.Contain(Frequency);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Escape { get; init; } = LibraryViewText.Contain(Escape);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Escape Kind")]
    [MarkoutSkipNull]
    public string? EscapeKind { get; init; } = LibraryViewText.Contain(EscapeKind);

    [MarkoutPropertyName("Est Size")]
    public int? EstSize { get; init; } = EstSize;

    /// <inheritdoc cref="LibraryViewText"/>
    public string? SizeTier { get; init; } = LibraryViewText.Contain(SizeTier);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("In Loop")]
    public string? InLoop { get; init; } = LibraryViewText.Contain(InLoop);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Path { get; init; } = LibraryViewText.Contain(Path);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Path Confidence")]
    public string? PathConfidence { get; init; } = LibraryViewText.Contain(PathConfidence);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Post Dominance")]
    public string? PostDominance { get; init; } = LibraryViewText.Contain(PostDominance);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Evidence { get; init; } = LibraryViewText.Contain(Evidence);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Multiplicity { get; init; } = LibraryViewText.Contain(Multiplicity);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Churned Type")]
    [MarkoutSkipNull]
    public string? ChurnedType { get; init; } = LibraryViewText.Contain(ChurnedType);
}

[MarkoutSerializable]
public record ILOffsetSafetyContextRow(
    string? ILOffset,
    string? SafetyKind,
    string? Operation,
    string? Requirement,
    string? Evidence)
{
    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get; init; } = LibraryViewText.Contain(ILOffset);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Safety Kind")]
    public string? SafetyKind { get; init; } = LibraryViewText.Contain(SafetyKind);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Operation { get; init; } = LibraryViewText.Contain(Operation);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Requirement { get; init; } = LibraryViewText.Contain(Requirement);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Evidence { get; init; } = LibraryViewText.Contain(Evidence);
}

[MarkoutSerializable]
public record ILOffsetCostContextRow(
    string? ILOffset,
    string? CostKind,
    string? Operation,
    string? InLoop,
    string? Evidence)
{
    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get; init; } = LibraryViewText.Contain(ILOffset);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Cost Kind")]
    public string? CostKind { get; init; } = LibraryViewText.Contain(CostKind);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Operation { get; init; } = LibraryViewText.Contain(Operation);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("In Loop")]
    public string? InLoop { get; init; } = LibraryViewText.Contain(InLoop);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Evidence { get; init; } = LibraryViewText.Contain(Evidence);
}

[MarkoutSerializable]
public record ResourceRow(
    string Name,
    InertString VisibilityText,
    InertString SizeText)
{
    public ResourceRow(string name, string visibility, string size)
        : this(
            name,
            LibraryViewText.Field(visibility),
            LibraryViewText.Field(size))
    {
    }

    /// <inheritdoc cref="LibraryViewText"/>
    public string Name { get; init; } = LibraryViewText.Contain(Name);

    [MarkoutIgnore, JsonIgnore]
    public InertString VisibilityText { get; init; } = VisibilityText;

    public string Visibility => VisibilityText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString SizeText { get; init; } = SizeText;

    public string Size => SizeText.ToString();
}

[MarkoutSerializable]
public record ResourceTriageRow(
    InertString MemberText,
    InertString CandidateText,
    string Finding,
    string Provenance,
    string Resource,
    string Shape,
    string Impact,
    string Actionability,
    InertString BoundaryText,
    InertString AcquireILText,
    InertString BoundaryILText,
    string Evidence,
    string Direction,
    string Confidence,
    string? Visibility,
    string? Stable,
    string? Selector)
{
    public ResourceTriageRow(
        string member,
        string candidate,
        string finding,
        string provenance,
        string resource,
        string shape,
        string impact,
        string actionability,
        string boundary,
        string acquireIL,
        string boundaryIL,
        string evidence,
        string direction,
        string confidence,
        string? visibility,
        string? stable,
        string? selector)
        : this(
            MarkoutInline.CodeText(LibraryViewText.Field(member)),
            MarkoutInline.CodeText(LibraryViewText.Field(candidate)),
            finding,
            provenance,
            resource,
            shape,
            impact,
            actionability,
            MarkoutInline.CodeText(LibraryViewText.Field(boundary)),
            MarkoutInline.CodeText(LibraryViewText.Field(acquireIL)),
            MarkoutInline.CodeText(LibraryViewText.Field(boundaryIL)),
            evidence,
            direction,
            confidence,
            visibility,
            stable,
            selector)
    {
    }

    // All or none, in constructor order -- see UnsafeMemberRow. The typed code
    // columns retain their concerns; the rest carry analyzer classification text.
    [MarkoutIgnore, JsonIgnore]
    public InertString MemberText { get; init; } = MemberText;

    public string Member => MemberText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString CandidateText { get; init; } = CandidateText;

    public string Candidate => CandidateText.ToString();

    /// <inheritdoc cref="LibraryViewText"/>
    public string Finding { get; init; } = LibraryViewText.Contain(Finding);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Provenance { get; init; } = LibraryViewText.Contain(Provenance);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Resource { get; init; } = LibraryViewText.Contain(Resource);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Shape { get; init; } = LibraryViewText.Contain(Shape);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Impact { get; init; } = LibraryViewText.Contain(Impact);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Actionability { get; init; } = LibraryViewText.Contain(Actionability);

    [MarkoutIgnore, JsonIgnore]
    public InertString BoundaryText { get; init; } = BoundaryText;

    public string Boundary => BoundaryText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString AcquireILText { get; init; } = AcquireILText;

    [MarkoutPropertyName("Acquire IL")]
    public string AcquireIL => AcquireILText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString BoundaryILText { get; init; } = BoundaryILText;

    [MarkoutPropertyName("Boundary IL")]
    public string BoundaryIL => BoundaryILText.ToString();

    /// <inheritdoc cref="LibraryViewText"/>
    public string Evidence { get; init; } = LibraryViewText.Contain(Evidence);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Direction { get; init; } = LibraryViewText.Contain(Direction);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Confidence { get; init; } = LibraryViewText.Contain(Confidence);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Visibility { get; init; } = LibraryViewText.Contain(Visibility);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Stable { get; init; } = LibraryViewText.Contain(Stable);

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Selector { get; init; } = LibraryViewText.Contain(Selector);
}

// Tight, human column set for the kind-scoped performance sections. Deep per-row diagnostics
// (provenance, path counts, post-dominance, token, fix guidance) live in the JSON projection.
[MarkoutSerializable]
public record PerformanceRow(
    InertString MemberText,
    InertString EvidenceText,
    InertString? AllocationText,
    string? Loop,
    InertString ReachText,
    string? Weight,
    string Priority,
    string Confidence)
{
    public PerformanceRow(
        string member,
        string evidence,
        string? allocation,
        string? loop,
        string reach,
        string? weight,
        string priority,
        string confidence)
        : this(
            MarkoutInline.CodeText(LibraryViewText.Field(member)),
            MarkoutInline.CodeText(LibraryViewText.Field(evidence)),
            allocation is null
                ? null
                : MarkoutInline.CodeText(LibraryViewText.Field(allocation)),
            loop,
            LibraryViewText.Field(reach),
            weight,
            priority,
            confidence)
    {
    }

    // All or none, in constructor order — see UnsafeMemberRow.
    [MarkoutIgnore, JsonIgnore]
    public InertString MemberText { get; init; } = MemberText;

    public string Member => MemberText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString EvidenceText { get; init; } = EvidenceText;

    public string Evidence => EvidenceText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? AllocationText { get; init; } = AllocationText;

    [MarkoutSkipNull]
    public string? Allocation => AllocationText?.ToString();

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Loop { get; init; } = LibraryViewText.Contain(Loop);

    [MarkoutIgnore, JsonIgnore]
    public InertString ReachText { get; init; } = ReachText;

    public string Reach => ReachText.ToString();

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Weight { get; init; } = LibraryViewText.Contain(Weight);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Priority { get; init; } = LibraryViewText.Contain(Priority);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Confidence { get; init; } = LibraryViewText.Contain(Confidence);
}

/// <summary>
/// A <see cref="PerformanceRow"/> prefixed with its kind label, used to flatten the per-kind
/// performance sections into one self-describing tabular table (<c>-S @Performance --tsv</c>/
/// <c>--jsonl</c>/<c>--table</c>). The leading <c>Kind</c> column tells consumers which performance
/// kind each row belongs to, since the flattened table has no per-section headings.
/// </summary>
[MarkoutSerializable]
public record PerformanceGroupRow(
    InertString KindText,
    InertString MemberText,
    InertString EvidenceText,
    InertString? AllocationText,
    InertString? LoopText,
    InertString ReachText,
    InertString? WeightText,
    InertString PriorityText,
    InertString ConfidenceText)
{
    public PerformanceGroupRow(
        string kind,
        string member,
        string evidence,
        string? allocation,
        string? loop,
        string reach,
        string? weight,
        string priority,
        string confidence)
        : this(
            LibraryViewText.Field(kind),
            MarkoutInline.CodeText(LibraryViewText.Field(member)),
            MarkoutInline.CodeText(LibraryViewText.Field(evidence)),
            allocation is null
                ? null
                : MarkoutInline.CodeText(LibraryViewText.Field(allocation)),
            loop is null ? null : LibraryViewText.Field(loop),
            LibraryViewText.Field(reach),
            weight is null ? null : LibraryViewText.Field(weight),
            LibraryViewText.Field(priority),
            LibraryViewText.Field(confidence))
    {
    }

    [MarkoutIgnore, JsonIgnore]
    public InertString KindText { get; init; } = KindText;

    public string Kind => KindText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString MemberText { get; init; } = MemberText;

    public string Member => MemberText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString EvidenceText { get; init; } = EvidenceText;

    public string Evidence => EvidenceText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? AllocationText { get; init; } = AllocationText;

    [MarkoutSkipNull]
    public string? Allocation => AllocationText?.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? LoopText { get; init; } = LoopText;

    [MarkoutSkipNull]
    public string? Loop => LoopText?.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString ReachText { get; init; } = ReachText;

    public string Reach => ReachText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? WeightText { get; init; } = WeightText;

    [MarkoutSkipNull]
    public string? Weight => WeightText?.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString PriorityText { get; init; } = PriorityText;

    public string Priority => PriorityText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString ConfidenceText { get; init; } = ConfidenceText;

    public string Confidence => ConfidenceText.ToString();
}

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
    string Value)
{
    /// <inheritdoc cref="LibraryViewText"/>
    public string Name { get; init; } = LibraryViewText.Contain(Name);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Target { get; init; } = LibraryViewText.Contain(Target);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Value { get; init; } = LibraryViewText.Contain(Value);
}

[MarkoutSerializable]
public record TypeForwarderRow(
    string TypeName,
    string TargetAssembly)
{
    /// <summary>
    /// Crosses exact forwarder identity into field-safe presentation text.
    /// Gate: LibraryFindingConsumerTests.TypeForwardersQueryProjection_PreservesIdentityUntilInertViewBoundary.
    /// </summary>
    [MarkoutPropertyName("Type")]
    public string TypeName { get; init; } =
        new InertString(TextPolicy.Field, TypeName).ToString();

    /// <inheritdoc cref="TypeName"/>
    [MarkoutPropertyName("Target Assembly")]
    public string TargetAssembly { get; init; } =
        new InertString(TextPolicy.Field, TargetAssembly).ToString();
}

[MarkoutSerializable]
public record AuditSignalRow(
    string Area,
    string Signal,
    string Value,
    string Evidence)
{
    /// <inheritdoc cref="LibraryViewText"/>
    public string Area { get; init; } = LibraryViewText.Contain(Area);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Signal { get; init; } = LibraryViewText.Contain(Signal);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Value { get; init; } = LibraryViewText.Contain(Value);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Evidence { get; init; } = LibraryViewText.Contain(Evidence);
}

[MarkoutSerializable]
public record SourceLinkDiagnosticRow(
    string Kind,
    string? Document,
    string Detail)
{
    public string Kind { get; init; } = LibraryViewText.Contain(Kind);

    [MarkoutSkipNull]
    public string? Document { get; init; } = LibraryViewText.Contain(Document);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Detail { get; init; } = LibraryViewText.Contain(Detail);
}

[MarkoutSerializable]
public record InspectionFailureRow(
    [property: MarkoutIgnore, JsonIgnore] InertString SectionText,
    string Finding,
    string Reason)
{
    public InspectionFailureRow(string section, string finding, string reason)
        : this(LibraryViewText.Field(section), finding, reason)
    {
    }

    public string Section => SectionText.ToString();

    /// <inheritdoc cref="LibraryViewText"/>
    public string Finding { get; init; } = LibraryViewText.Contain(Finding);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Reason { get; init; } = LibraryViewText.Contain(Reason);
}

[MarkoutSerializable]
public record SwitchRow(
    string Kind,
    string Switch,
    string Api)
{
    /// <summary>
    /// Crosses exact switch evidence into field-safe presentation text.
    /// Gate: LibraryFindingConsumerTests.SwitchesQueryProjection_PreservesIdentityUntilInertViewBoundary.
    /// </summary>
    public string Kind { get; init; } =
        new InertString(TextPolicy.Field, Kind).ToString();

    /// <inheritdoc cref="Kind"/>
    public string Switch { get; init; } =
        MarkoutInline.Code(
            new InertString(TextPolicy.Field, Switch).ToString());

    /// <inheritdoc cref="Kind"/>
    [MarkoutPropertyName("API")]
    public string Api { get; init; } =
        MarkoutInline.Code(
            new InertString(TextPolicy.Field, Api).ToString());
}

[MarkoutSerializable]
public record IntegrationOpportunityRow(
    string Integration,
    string Api,
    string IntegrationType,
    string LookFor)
{
    // All or none, in constructor order -- see UnsafeMemberRow.

    /// <inheritdoc cref="LibraryViewText"/>
    public string Integration { get; init; } = LibraryViewText.Contain(Integration);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("API")]
    public string Api { get; init; } = LibraryViewText.Contain(Api);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Integration Type")]
    public string IntegrationType { get; init; } = LibraryViewText.Contain(IntegrationType);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Look For")]
    public string LookFor { get; init; } = LibraryViewText.Contain(LookFor);
}

[MarkoutSerializable]
public record IntegrationSignalRow(
    string Kind,
    string Type)
{
    /// <inheritdoc cref="LibraryViewText"/>
    public string Kind { get; init; } = LibraryViewText.Contain(Kind);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Type { get; init; } = LibraryViewText.Contain(Type);
}

[MarkoutSerializable]
public sealed record IntegrationScanRow(
    string Scanner,
    string Integration,
    string Kind,
    string Name,
    string Shape)
{
    public string Scanner { get; init; } = LibraryViewText.Contain(Scanner);
    public string Integration { get; init; } = LibraryViewText.Contain(Integration);
    public string Kind { get; init; } = LibraryViewText.Contain(Kind);
    public string Name { get; init; } = LibraryViewText.Contain(Name);
    public string Shape { get; init; } = LibraryViewText.Contain(Shape);
}

[MarkoutSerializable]
public record IntegrationApiSignalRow(
    string Kind,
    string Api)
{
    /// <inheritdoc cref="LibraryViewText"/>
    public string Kind { get; init; } = LibraryViewText.Contain(Kind);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("API")]
    public string Api { get; init; } = LibraryViewText.Contain(Api);
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class LibraryInfoSection
{
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Architecture { get => field; init => field = LibraryViewText.Contain(value); }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? AssemblyVersion { get => field; init => field = LibraryViewText.Contain(value); }
    public int AsyncMethods { get; init; }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Company { get => field; init => field = LibraryViewText.Contain(value); }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Compilation { get => field; init => field = LibraryViewText.Contain(value); }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Copyright { get => field; init => field = LibraryViewText.Contain(value); }
    public int CustomAttributes { get; init; }
    [MarkoutBoolFormat("Yes", "No")]
    public bool Deterministic { get; init; }
    public int ExtensionMethods { get; init; }
    [MarkoutBoolFormat("Yes", "No")]
    public bool? Facade { get; init; }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? FileSize { get => field; init => field = LibraryViewText.Contain(value); }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? InformationalVersion { get => field; init => field = LibraryViewText.Contain(value); }
    public int Integrations { get; init; }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Methods { get => field; init => field = LibraryViewText.Contain(value); }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Modified { get => field; init => field = LibraryViewText.Contain(value); }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Name { get => field; init => field = LibraryViewText.Contain(value); }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Product { get => field; init => field = LibraryViewText.Contain(value); }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? PublicKeyToken { get => field; init => field = LibraryViewText.Contain(value); }
    [MarkoutBoolFormat("Yes", "No")]
    public bool Reproducible { get; init; }
    public int Resources { get; init; }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Signed { get => field; init => field = LibraryViewText.Contain(value); }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Source { get => field; init => field = LibraryViewText.Contain(value); }
    public int Switches { get; init; }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? TargetFramework { get => field; init => field = LibraryViewText.Contain(value); }
    public int TypeForwarders { get; init; }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Types { get => field; init => field = LibraryViewText.Contain(value); }
    public int UnionTypes { get; init; }
    /// <inheritdoc cref="LibraryViewText"/>
    public string? Version { get => field; init => field = LibraryViewText.Contain(value); }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
public record UnionTypeRow(
    string Type,
    string Kind,
    InertString IUnionText,
    string Cases)
{
    public UnionTypeRow(string type, string kind, string iUnion, string cases)
        : this(type, kind, LibraryViewText.Field(iUnion), cases)
    {
    }

    /// <summary>
    /// Crosses exact union identity into field-safe presentation text.
    /// Gate: LibraryFindingConsumerTests.UnionTypesQueryProjection_PreservesIdentityUntilInertViewBoundary.
    /// </summary>
    public string Type { get; init; } =
        new InertString(TextPolicy.Field, Type).ToString();

    /// <inheritdoc cref="Type"/>
    public string Kind { get; init; } =
        new InertString(TextPolicy.Field, Kind).ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString IUnionText { get; init; } = IUnionText;

    [MarkoutPropertyName("IUnion")]
    public string IUnion => IUnionText.ToString();

    /// <inheritdoc cref="Type"/>
    public string Cases { get; init; } =
        new InertString(TextPolicy.Field, Cases).ToString();
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
/// <summary>
/// The Symbols section.
/// </summary>
/// <remarks>
/// The CodeView and SourceLink records this reads sit inside the inspected
/// binary, so the build path, repository URL, and publisher are whatever the
/// assembly's author put there -- untrusted for the same reason a type name is
/// (issue #3319). Containment is on the properties so every producer of this
/// section inherits it.
/// </remarks>
public class SymbolsSection
{
    public string? Builder { get => field; init => field = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("PDB Format")]
    public string PdbFormat { get => field; init => field = LibraryViewText.Contain(value); } = "Unknown";
    [MarkoutPropertyName("PDB Location")]
    public string PdbLocation { get => field; init => field = LibraryViewText.Contain(value); } = "Unknown";
    [MarkoutPropertyName("PDB Path")]
    public string? PdbPath { get => field; init => field = LibraryViewText.Contain(value); }
    public string? Publisher { get => field; init => field = LibraryViewText.Contain(value); }
    public string? Recommendation { get => field; init => field = LibraryViewText.Contain(value); }
    public string? Repository { get => field; init => field = LibraryViewText.Contain(value); }
    [MarkoutPropertyName("Repository URL")]
    public string? RepositoryUrl { get => field; init => field = LibraryViewText.Contain(value); }
    public string? Signature { get => field; init => field = LibraryViewText.Contain(value); }
    public string? SourceLink { get => field; init => field = LibraryViewText.Contain(value); }
    public string? SymbolServer { get => field; init => field = LibraryViewText.Contain(value); }
    public string? Warning { get => field; init => field = LibraryViewText.Contain(value); }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class SourceLinkAuditSection
{
    public int Embedded { get; init; }
    public int Missing { get; init; }
    [MarkoutIgnore, JsonIgnore]
    public InertString SourceFilesText { get; init; } =
        LibraryViewText.Field(string.Empty);
    public string SourceFiles => SourceFilesText.ToString();
    [MarkoutIgnore, JsonIgnore]
    public InertString StatusText { get; init; } =
        LibraryViewText.Field(string.Empty);
    public string Status => StatusText.ToString();
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class SourceIntegritySection
{
    [MarkoutPropertyName("CR/LF Mismatch")]
    public string? CrlfMismatch => CrlfMismatchText?.ToString();
    [MarkoutIgnore, JsonIgnore]
    public InertString? CrlfMismatchText { get; init; }
    public int Mismatched { get; init; }
    [MarkoutPropertyName("Mismatched Files")]
    public string? MismatchedFiles => MismatchedFileTexts is { Count: > 0 } paths
        ? string.Join(", ", paths.Select(MarkoutInline.Code))
        : null;
    [MarkoutIgnore, JsonIgnore]
    public IReadOnlyList<InertString>? MismatchedFileTexts { get; init; }
    public string Status => StatusText.ToString();
    [MarkoutIgnore, JsonIgnore]
    public InertString StatusText { get; init; } =
        LibraryViewText.Field(string.Empty);
    public int Unverifiable { get; init; }
    public int Verified { get; init; }
}
