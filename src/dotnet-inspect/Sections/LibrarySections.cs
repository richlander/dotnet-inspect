using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Queries;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public sealed record LibrarySectionCatalog(
    SectionCatalog<LibraryInspection> Sections,
    InspectionQueryCatalog<InspectionQueryContext> QueryCatalog,
    InspectionQueryCatalog<AssemblyContextGroup> GroupQueryCatalog)
{
    public SectionPipeline<LibraryInspection> Pipeline => Sections.Pipeline;
}

/// <summary>
/// Section descriptors for the library command.
/// Each descriptor declares its name, cost classification, query binding, and a
/// <c>CanRender</c> check against <see cref="LibraryInspection"/>.
/// </summary>
public static class LibrarySections
{
    /// <summary>The reusable fixed-domain catalog for per-assembly library queries.</summary>
    public static InspectionQueryCatalog<InspectionQueryContext> QueryCatalog { get; } =
        BuildQueryCatalog();

    /// <summary>The reusable fixed-domain catalog for assembly-context group queries.</summary>
    public static InspectionQueryCatalog<AssemblyContextGroup> GroupQueryCatalog { get; } =
        BuildGroupQueryCatalog();

    /// <summary>The reusable fixed-domain catalog for library sections and query-demand plans.</summary>
    public static SectionCatalog<LibraryInspection> SectionCatalog { get; } =
        CreatePipeline().Compile();

    /// <summary>The complete reusable library section and query catalog.</summary>
    public static LibrarySectionCatalog Catalog { get; } =
        new(SectionCatalog, QueryCatalog, GroupQueryCatalog);

    /// <summary>
    /// Builds the library catalog from typed query catalogs, so section costs, demand, and
    /// execution use the same immutable declarations.
    /// </summary>
    public static LibrarySectionCatalog CreateCatalog() => Catalog;

    /// <summary>Builds the section pipeline with all library sections registered.</summary>
    public static SectionPipeline<LibraryInspection> CreatePipeline()
        => CreatePipeline(
            query => GroupQueryCatalog.RegisteredQueries.Contains(query)
                ? GroupQueryCatalog.CostOf(query)
                : QueryCatalog.CostOf(query));

    /// <summary>
    /// Creates an independently mutable builder initialized from the production query catalog.
    /// Intended for focused host tests and extensions; production execution uses
    /// <see cref="QueryCatalog"/>.
    /// </summary>
    public static InspectionQueryRegistry<InspectionQueryContext> CreateQueryRegistry()
        => QueryCatalog.ToBuilder();

    /// <summary>
    /// Returns the reusable assembly-context group catalog.
    /// </summary>
    public static InspectionQueryCatalog<AssemblyContextGroup> CreateGroupQueryRegistry()
        => GroupQueryCatalog;

    private static SectionPipeline<LibraryInspection> CreatePipeline(
        Func<InspectionQueryDefinition, InspectionCost> queryCost)
    {
        return new SectionPipeline<LibraryInspection>()
            .UseCuratedCatalog()
            .UseQueryCosts(queryCost)
            .WithoutComputedPoles()
            .Add<LibraryInfo>(
                [
                    ClassifiedMethodsQuery.Definition,
                    CustomAttributesQuery.Definition,
                    ExtensionMethodsQuery.Definition,
                    ResourcesQuery.Definition,
                    TypeForwardersQuery.Definition,
                ])
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
            .Add<SourceLinkDiagnostics>(SourceLinkDiscoverable)
            .Add<SourceLinkAudit>(
                SourceAvailabilityQuery.Definition,
                SourceLinkDiscoverable)
            .Add<MissingSourceFiles>(
                SourceAvailabilityQuery.Definition,
                SourceLinkDiscoverable)
            .Add<SourceIntegrity>(
                SourceIntegrityQuery.Definition,
                SourceLinkDiscoverable)
            .Add<Symbols>()
            .Add<Signals>(
                [
                    AssemblyReferencesQuery.Definition,
                    AuditMetadataQuery.Definition,
                    ClassifiedMethodsQuery.Definition,
                ],
                HasAssemblyInfo)
            .Add<IdentifierConfusion>(AssemblyReferencesQuery.Definition)
            .Add<Switches>(SwitchesQuery.Definition)
            .Add<IntegrationOpportunities>(
                AssemblyContextIntegrationOpportunitiesQuery.Definition)
            .Add<AI>(AssemblyContextIntegrationsQuery.Definition)
            .Add<AspNetCore>(AssemblyContextIntegrationsQuery.Definition)
            .Add<Authentication>(AssemblyContextIntegrationsQuery.Definition)
            .Add<Aspire>(AssemblyContextIntegrationsQuery.Definition)
            .Add<Configuration>(AssemblyContextIntegrationsQuery.Definition)
            .Add<DependencyInjection>(
                AssemblyContextIntegrationsQuery.Definition)
            .Add<Logging>(AssemblyContextIntegrationsQuery.Definition)
            .Add<OpenTelemetry>(AssemblyContextIntegrationsQuery.Definition)
            .Add<OpenAPI>(AssemblyContextIntegrationsQuery.Definition)
            .Add<Options>(AssemblyContextIntegrationsQuery.Definition)
            .Add<Hosting>(AssemblyContextIntegrationsQuery.Definition)
            .Add<HealthChecks>(AssemblyContextIntegrationsQuery.Definition)
            .Add<HttpClient>(AssemblyContextIntegrationsQuery.Definition)
            .Add<References>(AssemblyReferencesQuery.Definition, HasReferenceData)
            .Add<ExtensionMethods>(ExtensionMethodsQuery.Definition)
            .Add<UnsafeMembers>(
                UnsafeEvidenceQuery.Definition,
                UnsafeMembersDiscoverable)
            .Add<TopLeverage>(
                TopLeverageQuery.Definition,
                HasMethodBodies)
            .Add<BodyShapes>(
                BodyShapesQuery.Definition,
                HasMethodBodies)
            .Add<PerformanceBoxing>(
                OptimizationOpportunitiesQuery.Definition,
                HasMethodBodies)
            .Add<PerformanceArrays>(
                OptimizationOpportunitiesQuery.Definition,
                HasMethodBodies)
            .Add<PerformanceClosures>(
                OptimizationOpportunitiesQuery.Definition,
                HasMethodBodies)
            .Add<PerformanceEnumerators>(
                OptimizationOpportunitiesQuery.Definition,
                HasMethodBodies)
            .Add<PerformanceLoops>(
                OptimizationOpportunitiesQuery.Definition,
                HasMethodBodies)
            .Add<PerformanceHotspots>(
                OptimizationOpportunitiesQuery.Definition,
                HasMethodBodies)
            .Add<PerformanceAsync>(
                OptimizationOpportunitiesQuery.Definition,
                HasMethodBodies)
            .Add<PerformanceOther>(
                OptimizationOpportunitiesQuery.Definition,
                HasMethodBodies)
            .Add<ArrayPoolEscapes>(
                ResourceTriageQuery.Definition,
                HasMethodBodies)
            .Add<PInvokeMethods>(ClassifiedMethodsQuery.Definition)
            .Add<AsyncMethods>(ClassifiedMethodsQuery.Definition)
            .Add<Resources>(ResourcesQuery.Definition)
            .Add<CustomAttributes>(CustomAttributesQuery.Definition)
            .Add<UnionTypes>(UnionTypesQuery.Definition)
            .Add<TypeForwarders>(TypeForwardersQuery.Definition)
            .Add<NonNormalizedPaths>()
            .AddMetadataLens()
            .AddBaseCategory(SectionCategoryNames.Library,
                SectionNames.LibraryInfo,
                SectionNames.InspectionFailures,
                SectionNames.References,
                SectionNames.Signals,
                SectionNames.Symbols)
            .AddBaseCategory(SectionCategoryNames.Surface,
                SectionNames.AsyncMethods,
                SectionNames.CustomAttributes,
                SectionNames.ExtensionMethods,
                SectionNames.Resources,
                SectionNames.Switches,
                SectionNames.TypeForwarders,
                SectionNames.UnionTypes,
                SectionNames.PInvokeMethods)
            .AddCategory(SectionCategoryNames.Audit,
                SectionNames.PInvokeMethods,
                SectionNames.NonNormalizedPaths,
                SectionNames.SourceLinkDiagnostics,
                SectionNames.Signals,
                SectionNames.IdentifierConfusion,
                SectionNames.Symbols)
            .AddCategory(SectionCategoryNames.Performance,
                [.. PerformanceKinds.Sections, SectionNames.ArrayPoolEscapes, SectionNames.TopLeverage])
            .AddCategory(SectionCategoryNames.SourceLink,
                SectionNames.SourceLinkFiles,
                SectionNames.SourceLinkDiagnostics,
                SectionNames.SourceLinkAvailability,
                SectionNames.SourceLinkMissingFiles,
                SectionNames.SourceLinkIntegrity)
            .AddCategory(SectionCategoryNames.Integrations, [.. LibraryIntegrationCatalog.CategorySections, IntegrationSectionNames.Opportunities])
            .AddCategory(SectionCategoryNames.Context,
                SectionNames.ILOffset,
                SectionNames.MemberContext,
                SectionNames.InstructionContext,
                SectionNames.ExceptionContext,
                SectionNames.CallsiteContext,
                SectionNames.ReturnAddressContext,
                SectionNames.AllocationContext,
                SectionNames.SafetyContext,
                SectionNames.CostContext);
    }

    private static InspectionQueryCatalog<InspectionQueryContext> BuildQueryCatalog()
    {
        return new InspectionQueryRegistry<InspectionQueryContext>(
            static (context, query, cost) =>
                context.EnterQuery(query.Name, cost.ToSectionCost(query)))
            .Add(MetadataImageQuery.Definition, ctx =>
                ctx.Scan(
                    session => MetadataLensQueries.Image(session, ctx.Model),
                    () =>
                    {
                        try
                        {
                            using var session = ILInspector.Metadata.AssemblyInspectionSession.Open(
                                ctx.AssemblyPath);
                            return MetadataLensQueries.Image(session, ctx.Model);
                        }
                        catch (Exception ex)
                        {
                            return new MetadataImageResult.Failed(ex);
                        }
                    }))
            .Add(MetadataLensQueries.ReadyToRun, ctx =>
                ctx.Scan(
                    MetadataLensQueries.InspectReadyToRun,
                    () =>
                    {
                        try
                        {
                            using var session = ILInspector.Metadata.AssemblyInspectionSession.Open(
                                ctx.AssemblyPath);
                            return MetadataLensQueries.InspectReadyToRun(session);
                        }
                        catch (Exception ex) when (ex is BadImageFormatException or IOException or NotSupportedException)
                        {
                            return new ReadyToRunInspection.Failed(ex);
                        }
                    }))
            .Add(AssemblyReferencesQuery.Definition, ctx =>
                ctx.Scan(
                    AssemblyReferencesQuery.Execute,
                    () =>
                    {
                        try
                        {
                            using var session = ILInspector.Metadata.AssemblyInspectionSession.Open(
                                ctx.AssemblyPath);
                            return AssemblyReferencesQuery.Execute(session);
                        }
                        catch (Exception ex)
                        {
                            return new AssemblyReferencesResult.Failed(ex);
                        }
                    }))
            .Add(AuditMetadataQuery.Definition, ctx =>
                ctx.Query(
                    AuditMetadataQuery.Execute,
                    ex => new AuditMetadataResult.Failed(ex)))
            .Add(ClassifiedMethodsQuery.Definition, ctx =>
                ctx.Query(
                    ClassifiedMethodsQuery.Execute,
                    ex => new ClassifiedMethodsResult.Failed(ex)))
            .Add(CustomAttributesQuery.Definition, ctx =>
                ctx.Scan(
                    CustomAttributesQuery.Execute,
                    () =>
                    {
                        try
                        {
                            using var session = ILInspector.Metadata.AssemblyInspectionSession.Open(
                                ctx.AssemblyPath);
                            return CustomAttributesQuery.Execute(session);
                        }
                        catch (Exception ex)
                        {
                            return new CustomAttributesResult.Failed(ex);
                        }
                    }))
            .Add(ExtensionMethodsQuery.Definition, ctx =>
                ctx.Scan(
                    ExtensionMethodsQuery.Execute,
                    () =>
                    {
                        try
                        {
                            using var session = ILInspector.Metadata.AssemblyInspectionSession.Open(
                                ctx.AssemblyPath);
                            return ExtensionMethodsQuery.Execute(session);
                        }
                        catch (Exception ex)
                        {
                            return new ExtensionMethodsResult.Failed(ex);
                        }
                    }))
            .Add(ResourcesQuery.Definition, ctx =>
                ctx.Query(
                    ResourcesQuery.Execute,
                    ex => new ResourcesResult.Failed(ex)))
            .Add(SwitchesQuery.Definition, ctx =>
                ctx.Query(
                    SwitchesQuery.Execute,
                    ex => new SwitchesResult.Failed(ex)))
            .Add(TypeForwardersQuery.Definition, ctx =>
                ctx.Query(
                    TypeForwardersQuery.Execute,
                    ex => new TypeForwardersResult.Failed(ex)))
            .Add(UnionTypesQuery.Definition, ctx =>
                ctx.Query(
                    UnionTypesQuery.Execute,
                    ex => new UnionTypesResult.Failed(ex)))
            .Add(UnsafeEvidencePresenceQuery.Definition, ctx =>
            {
                if (ctx.MetadataContext is not { } metadata)
                {
                    return new UnsafeEvidencePresenceResult.Failed(
                        new InvalidOperationException(
                            "Unsafe evidence discovery requires the command's opened metadata context."));
                }

                return UnsafeEvidencePresenceQuery.Execute(
                    ctx.AssemblyPath,
                    metadata);
            })
            .Add(UnsafeEvidenceQuery.Definition, ctx =>
                ExecuteUnsafeEvidenceQuery(
                    ctx.MetadataContext?.HasMetadata != false,
                    ctx.BodyIndex))
            .Add(
                ResourceTriageQuery.Definition,
                ExecuteResourceTriageQuery)
            .Add(
                OptimizationOpportunitiesQuery.Definition,
                ExecuteOptimizationOpportunitiesQuery)
            .AddWithOptional(
                BodyShapesQuery.Definition,
                ExecuteBodyShapesQuery,
                [OptimizationOpportunitiesQuery.Definition])
            .Add(
                TopLeverageQuery.Definition,
                ExecuteTopLeverageQuery)
            .AddSourceLinkQueries(RequireSourceLinkContext)
            .Compile();
    }

    internal static UnsafeEvidenceResult ExecuteUnsafeEvidenceQuery(
        bool hasMetadata,
        Func<ILInspector.Analysis.LibraryBodyIndex> acquireIndex)
    {
        ArgumentNullException.ThrowIfNull(acquireIndex);

        if (!hasMetadata)
            return new UnsafeEvidenceResult.NoMetadata();

        try
        {
            return UnsafeEvidenceQuery.Execute(acquireIndex());
        }
        catch (CostDeclarationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UnsafeEvidenceResult.Failed(ex);
        }
    }

    internal static ResourceTriageResult ExecuteResourceTriageQuery(
        InspectionQueryContext context)
    {
        ResourceTriageResult result = ExecuteResourceTriageQuery(
            context.MetadataContext?.HasMetadata != false,
            context.BodyIndex,
            new ILInspector.Findings.FindingSubject(
                Path.GetFullPath(context.AssemblyPath),
                Path.GetFileName(context.AssemblyPath)));
        if (result is ResourceTriageResult.Available)
            _ = context.DrillMap();
        return result;
    }

    internal static ResourceTriageResult ExecuteResourceTriageQuery(
        bool hasMetadata,
        Func<ILInspector.Analysis.LibraryBodyIndex> acquireIndex,
        ILInspector.Findings.FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(acquireIndex);
        ArgumentNullException.ThrowIfNull(subject);

        if (!hasMetadata)
            return new ResourceTriageResult.NoMetadata();

        try
        {
            return ResourceTriageQuery.Execute(
                acquireIndex(),
                subject);
        }
        catch (CostDeclarationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ResourceTriageResult.Failed(
                new ILInspector.Findings.InspectionError(
                    subject,
                    ILInspector.Analysis.AnalysisFindings
                        .ResourceLifecycleDescriptor,
                    $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    internal static TopLeverageResult ExecuteTopLeverageQuery(
        InspectionQueryContext context)
    {
        TopLeverageResult result = ExecuteTopLeverageQuery(
            context.MetadataContext?.HasMetadata != false,
            context.BodyIndex);
        if (result is TopLeverageResult.Available)
            _ = context.DrillMap();
        return result;
    }

    internal static OptimizationOpportunitiesResult
        ExecuteOptimizationOpportunitiesQuery(InspectionQueryContext context)
        => ExecuteOptimizationOpportunitiesQuery(
            context.MetadataContext?.HasMetadata != false,
            context.BodyIndex,
            context.Model.PerformanceTriageOptions.IncludesAllocationFanout);

    internal static OptimizationOpportunitiesResult
        ExecuteOptimizationOpportunitiesQuery(
            bool hasMetadata,
            Func<ILInspector.Analysis.LibraryBodyIndex> acquireIndex,
            bool includeAllocationFanout)
    {
        ArgumentNullException.ThrowIfNull(acquireIndex);

        if (!hasMetadata)
            return new OptimizationOpportunitiesResult.NoMetadata();

        try
        {
            return OptimizationOpportunitiesQuery.Execute(
                acquireIndex(),
                includeAllocationFanout);
        }
        catch (CostDeclarationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OptimizationOpportunitiesResult.Failed(ex);
        }
    }

    internal static BodyShapesResult ExecuteBodyShapesQuery(
        InspectionQueryContext context,
        InspectionQueryResults dependencies)
    {
        if (context.MetadataContext?.HasMetadata == false)
            return new BodyShapesResult.NoMetadata();

        string kind = context.Model.BodyKindQueryOptions.Kind
            ?? throw new InvalidOperationException(
                "The Body Shapes query requires a validated body-kind predicate.");
        IReadOnlySet<int>? methodTokens = null;
        if (context.Model.PerformanceTriageOptions.HasCandidateFilters)
        {
            if (!dependencies.TryGet(
                    OptimizationOpportunitiesQuery.Definition,
                    out OptimizationOpportunitiesResult? optimization))
            {
                throw new InspectionQueryException(
                    "Composed Body Shapes predicates require the typed "
                    + "Optimization Opportunities query.");
            }

            switch (optimization)
            {
                case OptimizationOpportunitiesResult.Available available:
                    methodTokens = LibraryMetadataService.PerformanceSourceMethods(
                            LibraryMetadataService.SelectPerformanceTriageOpportunities(
                                available,
                                context.Model.PerformanceTriageOptions))
                        .Select(static method => method.MetadataToken)
                        .ToHashSet();
                    break;

                case OptimizationOpportunitiesResult.Failed:
                case OptimizationOpportunitiesResult.NoMetadata:
                    return new BodyShapesResult.DependencyUnavailable();

                default:
                    throw new InvalidOperationException(
                        "Composed Body Shapes predicates received an unknown "
                        + "Optimization Opportunities result.");
            }
        }

        try
        {
            var metadata = context.MetadataContext
                ?? throw new InvalidOperationException(
                    "The Body Shapes query requires the command's prefetched PE image.");
            using var source = MetadataSource.OpenFromPrefetchedImage(
                context.AssemblyPath,
                metadata.GetPrefetchedImage(),
                metadata.PortablePdbPath,
                context.BodyReferenceResolver);
            return BodyShapesQuery.Execute(source, kind, methodTokens);
        }
        catch (CostDeclarationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new BodyShapesResult.Failed(ex);
        }
    }

    internal static TopLeverageResult ExecuteTopLeverageQuery(
        bool hasMetadata,
        Func<ILInspector.Analysis.LibraryBodyIndex> acquireIndex)
    {
        ArgumentNullException.ThrowIfNull(acquireIndex);

        if (!hasMetadata)
            return new TopLeverageResult.NoMetadata();

        try
        {
            return TopLeverageQuery.Execute(acquireIndex());
        }
        catch (CostDeclarationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new TopLeverageResult.Failed(ex);
        }
    }

    private static InspectionQueryCatalog<AssemblyContextGroup>
        BuildGroupQueryCatalog()
        => new InspectionQueryRegistry<AssemblyContextGroup>()
            .Add(
                AssemblyContextIntegrationsQuery.Definition,
                AssemblyContextIntegrationsQuery.Execute)
            .Add(
                AssemblyContextIntegrationOpportunitiesQuery.Definition,
                AssemblyContextIntegrationOpportunitiesQuery.Execute,
                AssemblyContextIntegrationsQuery.Definition)
            .Compile();

    private static SourceLinkQueryContext RequireSourceLinkContext(InspectionQueryContext context)
        => context.SourceLinkContext
            ?? throw new InspectionQueryException(
                "SourceLink query execution requires a SourceLink query context.");

    // ===== Primary section =====

    public sealed class LibraryInfo : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.LibraryInfo;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static bool CanRender(LibraryInspection model) => model.AssemblyInfo != null;
    }

    public sealed class InspectionFailures : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.InspectionFailures;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => model.InspectionFailures is { Count: > 0 };
    }

    public sealed class ILOffset : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.ILOffset;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(LibraryInspection model) => model.ILOffset != null;
    }

    public sealed class MemberContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.MemberContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.MemberContext != null;
    }

    public sealed class InstructionContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.InstructionContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.InstructionContext != null;
    }

    public sealed class ExceptionContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.ExceptionContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.ExceptionContext is { Count: > 0 };
    }

    public sealed class CallsiteContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.CallsiteContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.CallsiteContext != null;
    }

    public sealed class ReturnAddressContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.ReturnAddressContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.ReturnAddressContext != null;
    }

    public sealed class AllocationContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.AllocationContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.AllocationContext is { Count: > 0 };
    }

    public sealed class SafetyContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.SafetyContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(LibraryInspection model) => model.ILOffset?.SafetyContext is { Count: > 0 };
    }

    public sealed class CostContext : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.CostContext;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
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
        public static bool CanRender(LibraryInspection model) => model.AssemblyInfo != null;
    }

    public sealed class Symbols : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.Symbols;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static bool CanRender(LibraryInspection model) => true;
    }

    public sealed class Signals : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.Signals;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static bool CanRender(LibraryInspection model)
            => model.AuditSignals is { Count: > 0 };
    }

    public sealed class IdentifierConfusion : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.IdentifierConfusion;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(LibraryInspection model)
            => IdentifierConfusionAudit.InspectLibrary(model).Count > 0;
    }

    public sealed class Switches : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.Switches;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model)
            => model.SwitchInspection.CanRenderWithPresence(model.HasSwitches);
    }

    public sealed class OpenTelemetry : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.OpenTelemetry.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model)
            => LibraryIntegrationCatalog.OpenTelemetry.CanRender(model);
    }

    public sealed class IntegrationOpportunities : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => IntegrationSectionNames.Opportunities;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model)
            => model.IntegrationOpportunities is { Count: > 0 };
    }

    public sealed class AI : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.AI.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.AI.CanRender(model);
    }

    public sealed class AspNetCore : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.AspNetCore.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.AspNetCore.CanRender(model);
    }

    public sealed class Authentication : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Authentication.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Authentication.CanRender(model);
    }

    public sealed class Aspire : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Aspire.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Aspire.CanRender(model);
    }

    public sealed class Configuration : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Configuration.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Configuration.CanRender(model);
    }

    public sealed class DependencyInjection : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.DependencyInjection.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model)
            => LibraryIntegrationCatalog.DependencyInjection.CanRender(model);
    }

    public sealed class Logging : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Logging.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Logging.CanRender(model);
    }

    public sealed class Options : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Options.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Options.CanRender(model);
    }

    public sealed class OpenAPI : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.OpenAPI.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.OpenAPI.CanRender(model);
    }

    public sealed class Hosting : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.Hosting.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.Hosting.CanRender(model);
    }

    public sealed class HealthChecks : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.HealthChecks.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.HealthChecks.CanRender(model);
    }

    public sealed class HttpClient : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => LibraryIntegrationCatalog.HttpClient.SectionName;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model) => LibraryIntegrationCatalog.HttpClient.CanRender(model);
    }

    // ===== SourceLink domain sections =====

    // Discovery-time applicability for the SourceLink section family. A section is only
    // listed by -D when a local PDB (embedded, adjacent, or already in the symbol cache)
    // exposes a SourceLink document. HasSourceLink is populated network-free during
    // discovery by LibraryMetadataService.ProbeLocalSourceLinkAsync; rendering (HEAD/fetch)
    // still runs on demand when a section is explicitly selected.
    private static bool SourceLinkDiscoverable(LibraryInspection model)
        => model.AssemblyInfo != null && model.HasSourceLink;

    private static bool HasAssemblyInfo(LibraryInspection model)
        => model.AssemblyInfo != null;

    private static bool HasReferenceData(LibraryInspection model)
        => model.AssemblyInfo?.References is { Count: > 0 }
           || model.AssemblyInfo?.TransitiveReferences is { Count: > 0 };

    private static bool HasMethodBodies(LibraryInspection model)
        => model.HasMethodBodies;

    private static bool UnsafeMembersDiscoverable(LibraryInspection model)
        => model.UnsafeEvidenceInspection is not null
            ? UnsafeMembers.CanRender(model)
            : model.UnsafeEvidencePresent is true
              || UnsafeMembers.CanRender(model)
              || (model.UnsafeEvidencePresent is null && model.HasMethodBodies);

    public sealed class SourceLinkAudit : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.SourceLinkAvailability;
        public static bool IsExpensive => true;
        // Opt-in only: issues one HEAD per source file, which scales with source count and is too
        // slow to render as a full default section. Signals may still summarize this high-value audit.
        public static bool ExplicitOnly => true;
        public static bool CanRender(LibraryInspection model)
            => model.AllSourcesAccessible.HasValue || model.TotalSourceFiles > 0;
    }

    public sealed class MissingSourceFiles : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.SourceLinkMissingFiles;
        public static bool IsExpensive => true;
        // Opt-in only: derived from the same per-file HEAD pass as SourceLink: Availability.
        public static bool ExplicitOnly => true;
        public static bool CanRender(LibraryInspection model)
            => model.MissingSourceFiles is { Count: > 0 };
    }

    public sealed class SourceIntegrity : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.SourceLinkIntegrity;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static bool CanRender(LibraryInspection model) => model.SourceIntegrityChecked;
    }

    // ===== Normal sections (offline, cheap) =====

    public sealed class References : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.References;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static bool CanRender(LibraryInspection model)
            => model.AssemblyReferenceInspection.HasFindings()
               || model.AssemblyInfo?.TransitiveReferences is { Count: > 0 };
    }

    public sealed class ExtensionMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.ExtensionMethods;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
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
        public static bool CanRender(LibraryInspection model)
            => model.UnsafeEvidenceInspection.CanRenderWithPresence(
                model.HasUnsafeCode
                || model.UnsafeSignatureDecodeStatus is not null);
    }

    public sealed class TopLeverage : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.TopLeverage;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(LibraryInspection model)
            => model.TopLeverageQueryResult is TopLeverageResult.Available
                { Methods.IsEmpty: false };
    }

    public sealed class BodyShapes : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.BodyShapes;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(LibraryInspection model)
            => model.EffectiveBodyShapeSearchResult is not null;
    }

    // Kind-scoped performance sections. Each shares the holistic optimization-opportunity scan
    // and gates render on its own bucket having rows (via the view's ShowWhenProperty). The
    // Registration supplies the pre-scan method-body applicability gate; these predicates report
    // actual post-scan row effectiveness.
    private static bool HasPerformanceKind(LibraryInspection model, string section)
        => model.PerformanceTriageOpportunities.Any(
            opportunity =>
                PerformanceKinds.SectionForShape(opportunity.Shape) == section);

    public sealed class PerformanceBoxing : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceBoxing;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceBoxing);
    }

    public sealed class PerformanceArrays : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceArrays;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceArrays);
    }

    public sealed class PerformanceClosures : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceClosures;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceClosures);
    }

    public sealed class PerformanceEnumerators : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceEnumerators;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceEnumerators);
    }

    public sealed class PerformanceLoops : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceLoops;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceLoops);
    }

    public sealed class PerformanceHotspots : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceHotspots;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceHotspots);
    }

    public sealed class PerformanceAsync : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceAsync;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceAsync);
    }

    public sealed class PerformanceOther : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PerformanceOther;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => HasPerformanceKind(model, SectionNames.PerformanceOther);
    }

    public sealed class ArrayPoolEscapes : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.ArrayPoolEscapes;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => model.ResourceTriageAssessments.Length > 0
                || model.ResourceTriage is { Count: > 0 };
    }

    public sealed class PInvokeMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.PInvokeMethods;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => model.ClassifiedMethodInspection.Failure() is null
               && (model.PInvokeMethodCount > 0 || model.HasPInvokeImports);
    }

    public sealed class AsyncMethods : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.AsyncMethods;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static bool CanRender(LibraryInspection model)
            => model.ClassifiedMethodInspection.Failure() is null
               && (model.AsyncMethodCount > 0
                   || model.HasRuntimeAsync || model.HasStateMachineAsync);
    }

    public sealed class Resources : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.Resources;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => model.ResourceInspection.CanRenderWithPresence(model.HasManifestResources);
    }

    public sealed class CustomAttributes : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.CustomAttributes;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => model.AssemblyAttributeInspection.CanRenderWithPresence(model.HasAssemblyAttributes);
    }

    public sealed class UnionTypes : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.UnionTypes;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => model.UnionTypeInspection.CanRenderWithPresence(model.HasUnionTypes);
    }

    public sealed class TypeForwarders : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.TypeForwarders;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model)
            => model.TypeForwarderInspection.CanRenderWithPresence(model.HasExportedTypeForwarders);
    }

    public sealed class NonNormalizedPaths : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.NonNormalizedPaths;
        public static bool IsExpensive => false;
        public static bool CanRender(LibraryInspection model) => model.NonNormalizedPaths is { Count: > 0 };
    }

    public sealed class SourceLinkDiagnostics : ISectionDescriptor<LibraryInspection>
    {
        public static string Name => SectionNames.SourceLinkDiagnostics;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(LibraryInspection model)
            => model.SourceLinkMap?.HasDiagnostics == true;
    }

}
