using DotnetInspector.Inspectors;
using DotnetInspector.Models;

namespace DotnetInspector.Sections;

/// <summary>
/// Section descriptors for the library command.
/// Each descriptor declares its name, cost classification, scanner key, and a
/// <c>CanRender</c> check against <see cref="LibraryInspection"/>.
/// </summary>
public static class LibrarySections
{
    // Scanner keys identify data collection steps in LibraryMetadataService
    public const string ScannerTransitiveRefs = "TransitiveRefs";
    public const string ScannerExtensionMethods = "ExtensionMethods";
    public const string ScannerClassifiedMethods = "ClassifiedMethods";
    public const string ScannerResources = "Resources";
    public const string ScannerCustomAttributes = "CustomAttributes";
    public const string ScannerTypeForwarders = "TypeForwarders";
    public const string ScannerInfoCounts = "InfoCounts";
    public const string ScannerSymbols = "Symbols";
    public const string ScannerAuditSignals = "AuditSignals";
    public const string ScannerIntegrations = LibraryIntegrationCatalog.RollupName;
    public const string ScannerIntegrationOpportunities = "IntegrationOpportunities";
    public const string ScannerSwitches = "Switches";
    public const string ScannerUnsafeMembers = "UnsafeMembers";

    /// <summary>Builds the section pipeline with all library sections registered.</summary>
    public static SectionPipeline<LibraryInspection> CreatePipeline()
    {
        return new SectionPipeline<LibraryInspection>()
            .Add<LibraryInfo>()
            .Add<SourceFiles>()
            .Add<SourceLinkAudit>()
            .Add<MissingSourceFiles>()
            .Add<SourceIntegrity>()
            .Add<Symbols>()
            .Add<Signals>()
            .Add<Switches>()
            .Add<Integrations>()
            .Add<IntegrationOpportunities>()
            .Add<AI>()
            .Add<AspNetCore>()
            .Add<Authentication>()
            .Add<Aspire>()
            .Add<Configuration>()
            .Add<DependencyInjection>()
            .Add<Logging>()
            .Add<OpenTelemetry>()
            .Add<OpenAPI>()
            .Add<Options>()
            .Add<Hosting>()
            .Add<HealthChecks>()
            .Add<HttpClient>()
            .Add<References>()
            .Add<Dependencies>()
            .Add<ExtensionMethods>()
            .Add<UnsafeMembers>()
            .Add<PInvokeMethods>()
            .Add<AsyncMethods>()
            .Add<Resources>()
            .Add<CustomAttributes>()
            .Add<TypeForwarders>()
            .Add<NonNormalizedPaths>()
            .AddCategory(SectionCategoryNames.Audit,
                SectionNames.UnsafeMembers,
                "P/Invoke Methods",
                "Switches")
            .AddCategory("@Integrations", [.. LibraryIntegrationCatalog.CategorySections, "Integration Opportunities"])
            .AddCategory("@Switches", "Switches");
    }

    /// <summary>Builds the scanner registry with all library scanners registered.</summary>
    public static ScannerRegistry CreateScannerRegistry()
    {
        return new ScannerRegistry()
            .Add(ScannerExtensionMethods, ctx =>
                ctx.Model.ExtensionMethods = LibraryMetadataService.ScanExtensionMethods(ctx.AssemblyPath, ctx.Logger))
            .Add(ScannerClassifiedMethods, ctx =>
                LibraryMetadataService.ScanClassifiedMethods(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerResources, ctx =>
                ctx.Model.Resources = LibraryMetadataService.ScanResources(ctx.AssemblyPath, ctx.Logger))
            .Add(ScannerCustomAttributes, ctx =>
                LibraryMetadataService.ScanCustomAttributes(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerTypeForwarders, ctx =>
                LibraryMetadataService.ScanTypeForwarders(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerInfoCounts, ctx =>
                LibraryMetadataService.ScanInfoCounts(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerAuditSignals, ctx =>
                AuditSignalBuilder.PopulateLibraryAudit(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerSwitches, ctx =>
                ctx.Model.Switches = LibraryMetadataService.ScanSwitches(ctx.AssemblyPath, ctx.Logger))
            .Add(ScannerUnsafeMembers, ctx =>
                ctx.Model.UnsafeMembers = LibraryMetadataService.ScanUnsafeMembers(ctx.AssemblyPath, ctx.Logger))
            .Add(ScannerIntegrations, ctx =>
                LibraryMetadataService.ScanIntegrations(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerIntegrationOpportunities, ctx =>
                LibraryMetadataService.ScanIntegrationOpportunities(ctx.AssemblyPath, ctx.Model, ctx.Logger));
    }

    // ===== Primary section =====

    public sealed class LibraryInfo : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Library Info";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => ScannerInfoCounts;
        public static bool CanRender(LibraryInspection model) => model.AssemblyInfo != null;
    }

    // ===== Symbol/provenance sections (network-capable, acceptable default cost) =====

    public sealed class SourceFiles : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Source Files";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.AssemblyInfo != null;
    }

    public sealed class Symbols : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Symbols";
        public static bool IsExpensive => false;
        public static SectionCapabilities Capabilities => SectionCapabilities.MayDownloadPdb;
        public static string? ScannerKey => ScannerSymbols;
        public static bool CanRender(LibraryInspection model) => true;
    }

    public sealed class Signals : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Signals";
        public static bool IsExpensive => false;
        public static SectionCapabilities Capabilities =>
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayAuditSources;
        public static string? ScannerKey => ScannerAuditSignals;
        public static bool CanRender(LibraryInspection model)
            => model.AuditSignals is { Count: > 0 };
    }

    public sealed class Switches : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Switches";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerSwitches;
        public static bool CanRender(LibraryInspection model)
            => model.Switches is { Count: > 0 } || model.HasSwitches;
    }

    public sealed class OpenTelemetry : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.OpenTelemetry.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model)
            => LibraryIntegrationCatalog.OpenTelemetry.CanRender(model);
    }

    public sealed class Integrations : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.RollupName;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.CanRenderAny(model);
    }

    public sealed class IntegrationOpportunities : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Integration Opportunities";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrationOpportunities;
        public static bool CanRender(LibraryInspection model)
            => model.IntegrationOpportunities is { Count: > 0 };
    }

    public sealed class AI : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.AI.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.AI.CanRender(model);
    }

    public sealed class AspNetCore : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.AspNetCore.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.AspNetCore.CanRender(model);
    }

    public sealed class Authentication : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Authentication.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Authentication.CanRender(model);
    }

    public sealed class Aspire : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Aspire.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Aspire.CanRender(model);
    }

    public sealed class Configuration : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Configuration.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Configuration.CanRender(model);
    }

    public sealed class DependencyInjection : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.DependencyInjection.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model)
            => LibraryIntegrationCatalog.DependencyInjection.CanRender(model);
    }

    public sealed class Logging : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Logging.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Logging.CanRender(model);
    }

    public sealed class Options : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Options.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Options.CanRender(model);
    }

    public sealed class OpenAPI : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.OpenAPI.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.OpenAPI.CanRender(model);
    }

    public sealed class Hosting : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Hosting.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Hosting.CanRender(model);
    }

    public sealed class HealthChecks : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.HealthChecks.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.HealthChecks.CanRender(model);
    }

    public sealed class HttpClient : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.HttpClient.Name;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.HttpClient.CanRender(model);
    }

    // ===== Opt-in SourceLink sections =====

    public sealed class SourceLinkAudit : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "SourceLink Availability";
        public static bool IsExpensive => true;
        // Opt-in only: issues one HEAD per source file, which scales with source count and is too
        // slow to render as a full default section. Signals may still summarize this high-value audit.
        public static bool ExplicitOnly => true;
        public static SectionCapabilities Capabilities =>
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayAuditSources;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model)
            => model.AllSourcesAccessible.HasValue || model.TotalSourceFiles > 0;
    }

    public sealed class MissingSourceFiles : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "SourceLink Missing Files";
        public static bool IsExpensive => true;
        // Opt-in only: derived from the same per-file HEAD pass as SourceLink Availability.
        public static bool ExplicitOnly => true;
        public static SectionCapabilities Capabilities =>
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayAuditSources;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model)
            => model.MissingSourceFiles is { Count: > 0 };
    }

    public sealed class SourceIntegrity : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "SourceLink Integrity";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionCapabilities Capabilities =>
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayFetchSources;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.SourceIntegrityChecked;
    }

    // ===== Normal sections (offline, cheap) =====

    public sealed class References : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "References";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model)
            => model.AssemblyInfo?.References is { Count: > 0 }
               && model.AssemblyInfo?.TransitiveReferences is not { Count: > 0 };
    }

    public sealed class Dependencies : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Dependencies";
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerTransitiveRefs;
        public static bool CanRender(LibraryInspection model)
            => model.UseDependenciesView
               && model.AssemblyInfo?.TransitiveReferences is { Count: > 0 };
    }

    public sealed class ExtensionMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Extension Methods";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerExtensionMethods;
        public static bool CanRender(LibraryInspection model)
            => model.ExtensionMethods is { Count: > 0 } || model.HasExtensionTypes;
    }

    public sealed class UnsafeMembers : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Unsafe Members";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerUnsafeMembers;
        public static bool CanRender(LibraryInspection model)
            => model.UnsafeMembers is { Count: > 0 } || model.HasUnsafeCode;
    }

    public sealed class PInvokeMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "P/Invoke Methods";
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerClassifiedMethods;
        public static bool CanRender(LibraryInspection model)
            => model.PInvokeMethods is { Count: > 0 } || model.HasPInvokeImports;
    }

    public sealed class AsyncMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Async Methods";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerClassifiedMethods;
        public static bool CanRender(LibraryInspection model)
            => model.AsyncMethods is { Count: > 0 }
               || model.HasRuntimeAsync || model.HasStateMachineAsync;
    }

    public sealed class Resources : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Resources";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerResources;
        public static bool CanRender(LibraryInspection model)
            => model.Resources is { Count: > 0 } || model.HasManifestResources;
    }

    public sealed class CustomAttributes : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Custom Attributes";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerCustomAttributes;
        public static bool CanRender(LibraryInspection model)
            => model.CustomAttributes is { Count: > 0 } || model.HasAssemblyAttributes;
    }

    public sealed class TypeForwarders : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Type Forwarders";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerTypeForwarders;
        public static bool CanRender(LibraryInspection model)
            => model.TypeForwarders is { Count: > 0 } || model.HasExportedTypeForwarders;
    }

    public sealed class NonNormalizedPaths : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => "Non-normalized Paths";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null; // data comes from PdbContext (always collected)
        public static bool CanRender(LibraryInspection model) => model.NonNormalizedPaths is { Count: > 0 };
    }

}
