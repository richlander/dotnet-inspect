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
    // Scanner keys identify data collection steps in LibraryMetadataService.
    // Every key here must be registered in CreateScannerRegistry and declared by at least one
    // section. Gate: SectionPipelineTests.LibraryScannerRegistry_RegistrationMatchesDeclaration.
    public const string ScannerExtensionMethods = "ExtensionMethods";
    public const string ScannerClassifiedMethods = "ClassifiedMethods";
    public const string ScannerResources = "Resources";
    public const string ScannerCustomAttributes = "CustomAttributes";
    public const string ScannerUnionTypes = "UnionTypes";
    public const string ScannerTypeForwarders = "TypeForwarders";
    public const string ScannerInfoCounts = "InfoCounts";
    public const string ScannerAuditSignals = "AuditSignals";
    public const string ScannerIntegrations = LibraryIntegrationCatalog.RollupName;
    public const string ScannerIntegrationOpportunities = "IntegrationOpportunities";
    public const string ScannerSwitches = "Switches";
    public const string ScannerUnsafeMembers = "UnsafeMembers";
    public const string ScannerTopLeverage = "TopLeverage";
    public const string ScannerOptimizationOpportunities = "OptimizationOpportunities";
    public const string ScannerResourceTriage = "ResourceTriage";
    public const string ScannerMetadata = "Metadata";

    /// <summary>Builds the section pipeline with all library sections registered.</summary>
    public static SectionPipeline<LibraryInspection> CreatePipeline()
    {
        return new SectionPipeline<LibraryInspection>()
            .UseCuratedCatalog()
            .Add<LibraryInfo>()
            .Add<InspectionFailures>()
            .Add<ILOffset>()
            .Add<MemberContext>()
            .Add<InstructionContext>()
            .Add<ExceptionContext>()
            .Add<CallsiteContext>()
            .Add<ReturnAddressContext>()
            .Add<AllocationContext>()
            .Add<SafetyContext>()
            .Add<CostContext>()
            .Add<SourceFiles>(SourceLinkDiscoverable)
            .Add<SourceLinkAudit>(SourceLinkDiscoverable)
            .Add<MissingSourceFiles>(SourceLinkDiscoverable)
            .Add<SourceIntegrity>(SourceLinkDiscoverable)
            .Add<Symbols>()
            .Add<Signals>()
            .Add<Switches>()
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
            .Add<References>(HasReferenceData)
            .Add<Dependencies>(HasReferenceData)
            .Add<ExtensionMethods>()
            .Add<UnsafeMembers>()
            .Add<TopLeverage>()
            .Add<PerformanceBoxing>()
            .Add<PerformanceArrays>()
            .Add<PerformanceClosures>()
            .Add<PerformanceEnumerators>()
            .Add<PerformanceLoops>()
            .Add<PerformanceHotspots>()
            .Add<PerformanceAsync>()
            .Add<PerformanceOther>()
            .Add<ArrayPoolEscapes>()
            .Add<PInvokeMethods>()
            .Add<AsyncMethods>()
            .Add<Resources>()
            .Add<CustomAttributes>()
            .Add<UnionTypes>()
            .Add<TypeForwarders>()
            .Add<NonNormalizedPaths>()
            .AddMetadataLens()
            .AddCategory(SectionCategoryNames.Audit,
                SectionNames.UnsafeMembers,
                SectionNames.PInvokeMethods,
                SectionNames.NonNormalizedPaths,
                SectionNames.Signals,
                SectionNames.Symbols)
            .AddCategory(SectionCategoryNames.Performance,
                PerformanceKinds.Sections)
            .AddCategory(SectionCategoryNames.Surface,
                SectionNames.AsyncMethods,
                SectionNames.CustomAttributes,
                SectionNames.ExtensionMethods,
                SectionNames.Resources,
                SectionNames.Switches,
                SectionNames.TypeForwarders,
                SectionNames.UnionTypes,
                SectionNames.PInvokeMethods)
            .AddCategory(SectionCategoryNames.SourceLink,
                SectionNames.SourceLinkFiles,
                SectionNames.SourceLinkAvailability,
                SectionNames.SourceLinkMissingFiles,
                SectionNames.SourceLinkIntegrity)
            .AddCategory(SectionCategoryNames.Integrations, [.. LibraryIntegrationCatalog.CategorySections, IntegrationSectionNames.Opportunities]);
    }

    /// <summary>Builds the scanner registry with all library scanners registered.</summary>
    public static ScannerRegistry CreateScannerRegistry()
    {
        return new ScannerRegistry()
            .Add(ScannerExtensionMethods, ctx =>
                LibraryMetadataService.ScanExtensionMembers(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerClassifiedMethods, ctx =>
                LibraryMetadataService.ScanClassifiedMethods(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerResources, ctx =>
                ctx.Model.ResourceInspection = LibraryMetadataService.ScanResources(ctx.AssemblyPath, ctx.Logger))
            .Add(ScannerCustomAttributes, ctx =>
                LibraryMetadataService.ScanCustomAttributes(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerUnionTypes, ctx =>
                ctx.Model.UnionTypeInspection = LibraryMetadataService.ScanUnionTypes(ctx.AssemblyPath, ctx.Logger))
            .Add(ScannerTypeForwarders, ctx =>
                LibraryMetadataService.ScanTypeForwarders(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerInfoCounts, ctx =>
                LibraryMetadataService.ScanInfoCounts(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerAuditSignals, ctx =>
                AuditSignalBuilder.PopulateLibraryAudit(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerSwitches, ctx =>
                ctx.Model.SwitchInspection = LibraryMetadataService.ScanSwitches(ctx.AssemblyPath, ctx.Logger))
            .Add(ScannerUnsafeMembers, ctx =>
                ctx.Model.UnsafeMembers = LibraryMetadataService.ScanUnsafeMembers(ctx.BodyIndex, ctx.AssemblyPath, ctx.Logger))
            .Add(ScannerTopLeverage, ctx =>
                ctx.Model.TopLeverage = LibraryMetadataService.ScanTopLeverage(
                    ctx.BodyIndex,
                    ctx.DrillMap,
                    ctx.AssemblyPath,
                    ctx.Logger))
            .Add(ScannerOptimizationOpportunities, ctx =>
                ctx.Model.OptimizationOpportunities = LibraryMetadataService.ScanOptimizationOpportunities(
                    ctx.BodyIndex, ctx.AssemblyPath, ctx.Logger, ctx.Model.PerformanceTriageOptions))
            .Add(ScannerResourceTriage, ctx =>
                LibraryMetadataService.ScanResourceTriage(
                    ctx.BodyIndex,
                    ctx.DrillMap,
                    ctx.AssemblyPath,
                    ctx.Model,
                    ctx.Logger))
            .Add(ScannerIntegrations, ctx =>
                LibraryMetadataService.ScanIntegrations(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerIntegrationOpportunities, ctx =>
                LibraryMetadataService.ScanIntegrationOpportunities(ctx.AssemblyPath, ctx.Model, ctx.Logger))
            .Add(ScannerMetadata, ctx =>
                LibraryMetadataService.ScanMetadataImage(ctx.AssemblyPath, ctx.Model, ctx.Logger));
    }

    // ===== Primary section =====

    public sealed class LibraryInfo : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.LibraryInfo;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static string? ScannerKey => ScannerInfoCounts;
        public static bool CanRender(LibraryInspection model) => model.AssemblyInfo != null;
    }

    public sealed class InspectionFailures : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.InspectionFailures;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model)
            => model.InspectionFailures is { Count: > 0 };
    }

    public sealed class ILOffset : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.ILOffset;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.ILOffset != null;
    }

    public sealed class MemberContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.MemberContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.MemberContext != null;
    }

    public sealed class InstructionContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.InstructionContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.InstructionContext != null;
    }

    public sealed class ExceptionContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.ExceptionContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.ExceptionContext is { Count: > 0 };
    }

    public sealed class CallsiteContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.CallsiteContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.CallsiteContext != null;
    }

    public sealed class ReturnAddressContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.ReturnAddressContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.ReturnAddressContext != null;
    }

    public sealed class AllocationContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.AllocationContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.AllocationContext is { Count: > 0 };
    }

    public sealed class SafetyContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.SafetyContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.SafetyContext is { Count: > 0 };
    }

    public sealed class CostContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.CostContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.CostContext is { Count: > 0 };
    }

    // ===== Symbol/provenance sections (network-capable, acceptable default cost) =====

    public sealed class SourceFiles : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.SourceLinkFiles;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.AssemblyInfo != null;
    }

    public sealed class Symbols : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.Symbols;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static string? ScannerKey => null; // data comes from PdbContext (always collected)
        public static bool CanRender(LibraryInspection model) => true;
    }

    public sealed class Signals : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.Signals;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static string? ScannerKey => ScannerAuditSignals;
        public static bool CanRender(LibraryInspection model)
            => model.AuditSignals is { Count: > 0 };
    }

    public sealed class Switches : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.Switches;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static string? ScannerKey => ScannerSwitches;
        public static bool CanRender(LibraryInspection model)
            => model.SwitchInspection.CanRenderWithPresence(model.HasSwitches);
    }

    public sealed class OpenTelemetry : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.OpenTelemetry.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        // @Integrations door member: kept behind the @Integrations door in -D (like the
        // @Performance kind-scoped sub-group) so the ecosystem sections never clutter the flat
        // standalone catalog. ListedInCatalog governs catalog listing, not render — these still
        // auto-render by size class and remain reachable by exact name.
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model)
            => LibraryIntegrationCatalog.OpenTelemetry.CanRender(model);
    }

    public sealed class IntegrationOpportunities : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => IntegrationSectionNames.Opportunities;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrationOpportunities;
        public static bool CanRender(LibraryInspection model)
            => model.IntegrationOpportunities is { Count: > 0 };
    }

    public sealed class AI : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.AI.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.AI.CanRender(model);
    }

    public sealed class AspNetCore : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.AspNetCore.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.AspNetCore.CanRender(model);
    }

    public sealed class Authentication : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Authentication.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Authentication.CanRender(model);
    }

    public sealed class Aspire : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Aspire.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Aspire.CanRender(model);
    }

    public sealed class Configuration : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Configuration.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Configuration.CanRender(model);
    }

    public sealed class DependencyInjection : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.DependencyInjection.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model)
            => LibraryIntegrationCatalog.DependencyInjection.CanRender(model);
    }

    public sealed class Logging : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Logging.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Logging.CanRender(model);
    }

    public sealed class Options : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Options.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Options.CanRender(model);
    }

    public sealed class OpenAPI : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.OpenAPI.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.OpenAPI.CanRender(model);
    }

    public sealed class Hosting : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Hosting.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Hosting.CanRender(model);
    }

    public sealed class HealthChecks : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.HealthChecks.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.HealthChecks.CanRender(model);
    }

    public sealed class HttpClient : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.HttpClient.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerIntegrations;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.HttpClient.CanRender(model);
    }

    // ===== Opt-in SourceLink sections =====

    // Discovery-time applicability for the SourceLink section family. A section is only
    // listed by -D when a local PDB (embedded, adjacent, or already in the symbol cache)
    // exposes a SourceLink document. HasSourceLink is populated network-free during
    // discovery by LibraryMetadataService.ProbeLocalSourceLinkAsync; rendering (HEAD/fetch)
    // still runs on demand when a section is explicitly selected.
    private static bool SourceLinkDiscoverable(LibraryInspection model)
        => model.AssemblyInfo != null && model.HasSourceLink;

    private static bool HasReferenceData(LibraryInspection model)
        => model.AssemblyInfo?.References is { Count: > 0 }
           || model.AssemblyInfo?.TransitiveReferences is { Count: > 0 };

    public sealed class SourceLinkAudit : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.SourceLinkAvailability;
        public static bool IsExpensive => true;
        // Opt-in only: issues one HEAD per source file, which scales with source count and is too
        // slow to render as a full default section. Signals may still summarize this high-value audit.
        public static bool ExplicitOnly => true;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model)
            => model.AllSourcesAccessible.HasValue || model.TotalSourceFiles > 0;
    }

    public sealed class MissingSourceFiles : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.SourceLinkMissingFiles;
        public static bool IsExpensive => true;
        // Opt-in only: derived from the same per-file HEAD pass as SourceLink: Availability.
        public static bool ExplicitOnly => true;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model)
            => model.MissingSourceFiles is { Count: > 0 };
    }

    public sealed class SourceIntegrity : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.SourceLinkIntegrity;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model) => model.SourceIntegrityChecked;
    }

    // ===== Normal sections (offline, cheap) =====

    public sealed class References : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.References;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model)
            => model.AssemblyReferenceInspection.HasFindings()
               && model.AssemblyInfo?.TransitiveReferences is not { Count: > 0 };
    }

    public sealed class Dependencies : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.Dependencies;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        // Built inline by LibraryMetadataService under options.IncludeDependencies, before the
        // scanner phase — not by a scanner.
        public static string? ScannerKey => null;
        public static bool CanRender(LibraryInspection model)
            => model.UseDependenciesView
               && model.AssemblyInfo?.TransitiveReferences is { Count: > 0 };
    }

    public sealed class ExtensionMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.ExtensionMethods;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static string? ScannerKey => ScannerExtensionMethods;
        public static bool CanRender(LibraryInspection model)
            => model.ExtensionMemberInspection.CanRenderWithPresence(model.HasExtensionTypes);
    }

    public sealed class UnsafeMembers : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.UnsafeMembers;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static string? ScannerKey => ScannerUnsafeMembers;
        public static bool CanRender(LibraryInspection model)
            => model.UnsafeMembers is { Count: > 0 }
                || model.HasUnsafeCode
                || model.UnsafeSignatureDecodeStatus is not null;
    }

    public sealed class TopLeverage : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.TopLeverage;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static string? ScannerKey => ScannerTopLeverage;
        public static bool CanRender(LibraryInspection model)
            => model.TopLeverage is { Count: > 0 } || model.HasMethodBodies;
    }

    // Kind-scoped performance sections. Each shares the holistic optimization-opportunity scan
    // and gates render on its own bucket having rows (via the view's ShowWhenProperty). The
    // `|| HasMethodBodies` applicability keeps the section selectable/scannable pre-scan, exactly
    // as the retired monolith did; the empty-when-no-rows suppression is the view's job.
    private static bool HasPerformanceKind(LibraryInspection model, string section)
        => model.OptimizationOpportunities is { } rows
           && rows.Any(o => PerformanceKinds.SectionForShape(o.Shape) == section);

    public sealed class PerformanceBoxing : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceBoxing;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        // Kept behind the @Performance door in -D (a large kind-scoped sub-group), yet still
        // auto-rendered at -v:d by size class — ListedInCatalog governs catalog listing, not render.
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerOptimizationOpportunities;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceBoxing) || model.HasMethodBodies;
    }

    public sealed class PerformanceArrays : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceArrays;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        // Kept behind the @Performance door in -D (a large kind-scoped sub-group), yet still
        // auto-rendered at -v:d by size class — ListedInCatalog governs catalog listing, not render.
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerOptimizationOpportunities;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceArrays) || model.HasMethodBodies;
    }

    public sealed class PerformanceClosures : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceClosures;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        // Kept behind the @Performance door in -D (a large kind-scoped sub-group), yet still
        // auto-rendered at -v:d by size class — ListedInCatalog governs catalog listing, not render.
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerOptimizationOpportunities;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceClosures) || model.HasMethodBodies;
    }

    public sealed class PerformanceEnumerators : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceEnumerators;
        public static bool IsExpensive => false;
        // Kept behind the @Performance door in -D (a kind-scoped sub-group), yet still auto-rendered
        // at -v:n/-v:d by size class — ListedInCatalog governs catalog listing, not render.
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerOptimizationOpportunities;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceEnumerators) || model.HasMethodBodies;
    }

    public sealed class PerformanceLoops : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceLoops;
        public static bool IsExpensive => false;
        // Kept behind the @Performance door in -D (a kind-scoped sub-group), yet still auto-rendered
        // at -v:n/-v:d by size class — ListedInCatalog governs catalog listing, not render.
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerOptimizationOpportunities;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceLoops) || model.HasMethodBodies;
    }

    public sealed class PerformanceHotspots : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceHotspots;
        public static bool IsExpensive => false;
        // Kept behind the @Performance door in -D (a kind-scoped sub-group), yet still auto-rendered
        // at -v:n/-v:d by size class — ListedInCatalog governs catalog listing, not render.
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerOptimizationOpportunities;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceHotspots) || model.HasMethodBodies;
    }

    public sealed class PerformanceAsync : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceAsync;
        public static bool IsExpensive => false;
        // Kept behind the @Performance door in -D (a kind-scoped sub-group), yet still auto-rendered
        // at -v:n/-v:d by size class — ListedInCatalog governs catalog listing, not render.
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerOptimizationOpportunities;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceAsync) || model.HasMethodBodies;
    }

    public sealed class PerformanceOther : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceOther;
        public static bool IsExpensive => false;
        // Kept behind the @Performance door in -D (a kind-scoped sub-group), yet still auto-rendered
        // at -v:n/-v:d by size class — ListedInCatalog governs catalog listing, not render.
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerOptimizationOpportunities;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceOther) || model.HasMethodBodies;
    }

    public sealed class ArrayPoolEscapes : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.ArrayPoolEscapes;
        public static bool IsExpensive => false;
        // Catalog-hidden: applicability is a capability predicate (the scan can run), not a
        // content predicate (the scan found something), and determining content costs a full
        // resource-triage scan. Sections list in -D only when >0 rows can be established
        // cheaply, so this one is reachable by exact name instead of advertising itself and
        // then rendering nothing.
        public static bool ListedInCatalog => false;
        public static string? ScannerKey => ScannerResourceTriage;
        public static bool CanRender(LibraryInspection model)
            => model.ResourceLifecycleInspection?.Value
                    is ILInspector.Findings.FindingInspection<
                        ILInspector.Analysis.ResourceLifecycleOccurrence>.Complete
                || model.HasMethodBodies;
    }

    public sealed class PInvokeMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PInvokeMethods;
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerClassifiedMethods;
        public static bool CanRender(LibraryInspection model)
            => model.ClassifiedMethodInspection.Failure() is null
               && (model.PInvokeMethodCount > 0 || model.HasPInvokeImports);
    }

    public sealed class AsyncMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.AsyncMethods;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static string? ScannerKey => ScannerClassifiedMethods;
        public static bool CanRender(LibraryInspection model)
            => model.ClassifiedMethodInspection.Failure() is null
               && (model.AsyncMethodCount > 0
                   || model.HasRuntimeAsync || model.HasStateMachineAsync);
    }

    public sealed class Resources : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.Resources;
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerResources;
        public static bool CanRender(LibraryInspection model)
            => model.ResourceInspection.CanRenderWithPresence(model.HasManifestResources);
    }

    public sealed class CustomAttributes : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.CustomAttributes;
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerCustomAttributes;
        public static bool CanRender(LibraryInspection model)
            => model.AssemblyAttributeInspection.CanRenderWithPresence(model.HasAssemblyAttributes);
    }

    public sealed class UnionTypes : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.UnionTypes;
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerUnionTypes;
        public static bool CanRender(LibraryInspection model)
            => model.UnionTypeInspection.CanRenderWithPresence(model.HasUnionTypes);
    }

    public sealed class TypeForwarders : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.TypeForwarders;
        public static bool IsExpensive => false;
        public static string? ScannerKey => ScannerTypeForwarders;
        public static bool CanRender(LibraryInspection model)
            => model.TypeForwarderInspection.CanRenderWithPresence(model.HasExportedTypeForwarders);
    }

    public sealed class NonNormalizedPaths : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.NonNormalizedPaths;
        public static bool IsExpensive => false;
        public static string? ScannerKey => null; // data comes from PdbContext (always collected)
        public static bool CanRender(LibraryInspection model) => model.NonNormalizedPaths is { Count: > 0 };
    }

}
