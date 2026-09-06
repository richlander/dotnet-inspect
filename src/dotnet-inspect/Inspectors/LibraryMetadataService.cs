using DotnetInspector.Core;
using DotnetInspector.Models;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using ILInspector.Research;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Findings;
using AssemblyReference = ILInspector.Metadata.AssemblyReference;
using Analysis = ILInspector.Analysis;
using MetadataResource = ILInspector.Metadata.ManifestResourceInfo;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Inspects assemblies/libraries: metadata extraction, PDB inspection, SourceLink verification,
/// builder inference, and transitive reference resolution.
/// </summary>
internal static class LibraryMetadataService
{
    internal const int DiscoveryMaxEmbeddedPdbBytes =
        64 * 1024 * 1024;

    /// <summary>
    /// Full inspection pipeline for a single assembly.
    /// </summary>
    public static async Task<LibraryInspection?> InspectAsync(
        string path,
        LibraryOptions options,
        VerboseLogger logger,
        string? packageName,
        string? packageVersion,
        HttpClient httpClient,
        bool isPlatformAssembly = false,
        HashSet<InspectionQueryDefinition>? queries = null,
        InspectionQueryCatalog<InspectionQueryContext>? queryCatalog = null,
        InspectionQueryPlan<InspectionQueryContext>? queryPlan = null,
        ResolvedAssemblyReference? assemblyReference = null,
        AssemblyIntegrationsEntry? integrationsEntry = null,
        AssemblyIntegrationOpportunitiesEntry?
            integrationOpportunitiesEntry = null,
        bool discoveryOnly = false,
        Sections.InspectionTrace? trace = null)
    {
        logger.Log($"Inspecting: {Path.GetFileName(path)}");

        try
        {
            queryPlan ??= queryCatalog is not null && queries is not null
                ? queryCatalog.Plan(queries)
                : null;
            IReadOnlyCollection<InspectionQueryDefinition>? requiredQueries =
                queryPlan is null
                    ? queries
                    : queryPlan.Queries;
            if (requiredQueries is not null)
                trace?.RecordQueryClosure(requiredQueries);
            var bodyAnalysisFeatures =
                SelectBodyAnalysisFeatures(requiredQueries);
            bool needsPrefetchedImage =
                bodyAnalysisFeatures
                    != Analysis.LibraryBodyAnalysisFeatures.None;
            bool needsBodyReferenceResolver =
                bodyAnalysisFeatures.HasFlag(
                    Analysis.LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities)
                || requiredQueries?.Contains(BodyShapesQuery.Definition) == true;
            IAssemblyReferenceResolver? bodyReferenceResolver =
                needsBodyReferenceResolver
                    ? new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(path)
                        {
                            TargetFramework = options.Tfm,
                            IncludeDepsJsonAssets = false,
                            IncludeAspNetCoreSharedFramework = false,
                            PreferImplementationAssemblies = true,
                        })
                    : null;
            var discoveryReadLimits =
                new SourceLinkReadLimits(
                    DiscoveryMaxEmbeddedPdbBytes,
                    maxMapBytes: 4 * 1024 * 1024,
                    maxMappings: 16 * 1024);
            using var service = discoveryOnly
                ? assemblyReference is not null
                    ? needsPrefetchedImage
                        ? SourceLinkService
                            .OpenEmbeddedPdbOnlyPrefetched(
                                assemblyReference,
                                discoveryReadLimits,
                                logger.Log)
                        : SourceLinkService.OpenEmbeddedPdbOnly(
                            assemblyReference,
                            discoveryReadLimits,
                            logger.Log)
                    : needsPrefetchedImage
                        ? SourceLinkService
                            .OpenEmbeddedPdbOnlyPrefetched(
                                path,
                                discoveryReadLimits,
                                logger.Log)
                        : SourceLinkService.OpenEmbeddedPdbOnly(
                            path,
                            discoveryReadLimits,
                            logger.Log)
                : assemblyReference is not null
                    ? !needsPrefetchedImage
                        ? SourceLinkService.Open(
                            assemblyReference,
                            logger.Log)
                        : SourceLinkService.OpenPrefetched(
                            assemblyReference,
                            logger.Log)
                    : !needsPrefetchedImage
                        ? SourceLinkService.Open(path, logger.Log)
                        : SourceLinkService.OpenPrefetched(path, logger.Log);
            var pdbContext = service.Context;
            bool projectOptimizationOpportunities =
                options.IncludeSections is null
                || options.IncludeSections.Overlaps(PerformanceKinds.Sections);
            var sourceLinkQueryContext = new SourceLinkQueryContext(
                service,
                FindingSubjectFor(path),
                httpClient,
                DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch,
                packageName,
                packageVersion,
                isPlatformAssembly,
                CoreSourceLinkQueryCache.Instance,
                logger.Log,
                options.SourceOptions);

            if (!pdbContext.HasMetadata)
            {
                var nativeInfo = pdbContext.CreateNativeInfo();

                var nativeAudit = new LibraryInspection
                {
                    FileName = Path.GetFileName(path),
                    FileType = "native",
                    AssemblyInfo = nativeInfo,
                    FileSize = pdbContext.FileSize
                };

                nativeAudit.HasReproducibleFlag = pdbContext.HasReproducibleFlag;
                nativeAudit.IsDeterministic = pdbContext.HasReproducibleFlag;

                if (queryPlan is not null)
                {
                    using var queryContext = new Sections.InspectionQueryContext
                    {
                        AssemblyPath = path,
                        AssemblyReference = assemblyReference,
                        BodyReferenceResolver = bodyReferenceResolver,
                        Model = nativeAudit,
                        Logger = logger,
                        MetadataContext = pdbContext,
                        SourceLinkContext = sourceLinkQueryContext,
                        BodyAnalysisFeatures = Analysis.LibraryBodyAnalysisFeatures.None,
                        Trace = trace,
                    };
                    await RunTypedQueriesAsync(
                        path,
                        nativeAudit,
                        logger,
                        queryPlan,
                        queryContext,
                        projectOptimizationOpportunities,
                        trace).ConfigureAwait(false);
                }

                return nativeAudit;
            }

            var needsAuditSignals =
                requiredQueries?.Contains(AuditMetadataQuery.Definition) == true;

            AssemblySurfaceClassificationOutcome? surfaceClassification =
                isPlatformAssembly
                    ? PlatformResolver.ClassifyAssemblySurface(path)
                    : null;
            var inspection = new LibraryInspection
            {
                FileName = Path.GetFileName(path),
                FileType = "dll",
                IsFacadeAssembly = surfaceClassification
                    is AssemblySurfaceClassificationOutcome.Classified classified
                        ? classified.Classification.Kind
                            == AssemblySurfaceKind.Facade
                        : null,
                SurfaceClassification = surfaceClassification,
                SurfaceClassificationInspection = surfaceClassification is null
                    ? null
                    : MetadataFindings.InspectAssemblySurface(
                        surfaceClassification,
                        FindingSubjectFor(path)),
                PerformanceTriageOptions = options.PerformanceTriage,
                BodyKindQueryOptions = options.BodyKindQuery,
                BodyShapeSections = options.IncludeSections,
            };

            inspection.AssemblyInfo = pdbContext.ExtractAssemblyInfo();

            // Populate cheap presence flags for fast -s discovery
            PresenceFlags presenceFlags = integrationsEntry switch
            {
                AssemblyIntegrationsEntry.Available available =>
                    pdbContext.ScanPresenceFlags(available.Presence),
                AssemblyIntegrationsEntry.Rejected
                    or AssemblyIntegrationsEntry.Failed =>
                    pdbContext.ScanPresenceFlagsWithoutIntegrations(),
                null => pdbContext.ScanPresenceFlags(),
                _ => throw new InvalidOperationException(
                    $"Unknown assembly Integrations result '{integrationsEntry.GetType().Name}'."),
            };
            inspection.HasExtensionTypes = presenceFlags.HasExtensionTypes;
            inspection.HasPInvokeImports = presenceFlags.HasPInvokeImports;
            inspection.HasUnsafeCode = presenceFlags.HasUnsafeCode;
            inspection.UnsafeSignatureDecodeStatus = presenceFlags.UnsafeSignatureDecodeStatus;
            inspection.HasMethodBodies = presenceFlags.HasMethodBodies;
            inspection.HasRuntimeAsync = presenceFlags.HasRuntimeAsync;
            inspection.HasStateMachineAsync = presenceFlags.HasStateMachineAsync;
            inspection.HasManifestResources = presenceFlags.HasManifestResources;
            inspection.HasOpenTelemetrySupport = presenceFlags.HasOpenTelemetrySupport;
            inspection.HasAspNetCoreSupport = presenceFlags.HasAspNetCoreSupport;
            inspection.HasAspireSupport = presenceFlags.HasAspireSupport;
            inspection.HasAISupport = presenceFlags.HasAISupport;
            inspection.HasAuthenticationSupport = presenceFlags.HasAuthenticationSupport;
            inspection.HasConfigurationSupport = presenceFlags.HasConfigurationSupport;
            inspection.HasDependencyInjectionSupport = presenceFlags.HasDependencyInjectionSupport;
            inspection.HasLoggingSupport = presenceFlags.HasLoggingSupport;
            inspection.HasOptionsSupport = presenceFlags.HasOptionsSupport;
            inspection.HasHostingSupport = presenceFlags.HasHostingSupport;
            inspection.HasHealthChecksSupport = presenceFlags.HasHealthChecksSupport;
            inspection.HasHttpClientSupport = presenceFlags.HasHttpClientSupport;
            inspection.HasOpenApiSupport = presenceFlags.HasOpenApiSupport;
            inspection.IntegrationCount = presenceFlags.IntegrationCount;
            inspection.HasAssemblyAttributes = presenceFlags.HasAssemblyAttributes;
            inspection.HasExportedTypeForwarders = presenceFlags.HasTypeForwarders;
            inspection.HasUnionTypes = presenceFlags.HasUnionTypes;
            var appContextSwitches =
                AppContextSwitchProjectionProducer.ProduceInventory(
                    pdbContext.MethodBodies);
            inspection.SwitchCount = presenceFlags.SwitchCount + appContextSwitches.Length;
            inspection.HasSwitches = inspection.SwitchCount > 0;

            if (integrationsEntry is not null)
            {
                ApplyAssemblyIntegrationsEntry(
                    path,
                    inspection,
                    logger,
                    integrationsEntry);
            }
            if (integrationOpportunitiesEntry is not null)
            {
                ApplyAssemblyIntegrationOpportunitiesEntry(
                    path,
                    inspection,
                    logger,
                    integrationOpportunitiesEntry);
            }

            // PE debug directory fields
            inspection.HasReproducibleFlag = pdbContext.HasReproducibleFlag;
            inspection.HasEmbeddedPdb = pdbContext.HasEmbeddedPdb;
            inspection.PdbPath = pdbContext.CodeViewPdbPath;
            ApplySourceLinkAudit(service, inspection);

            // Run typed queries against one shared assembly context.
            var collectReferenceTree = options.CollectReferenceTree;
            var referencesWillRun =
                queryPlan is not null
                && requiredQueries?.Contains(AssemblyReferencesQuery.Definition) == true;
            if ((collectReferenceTree || needsAuditSignals) && !referencesWillRun)
            {
                using var session = AssemblyInspectionSession.Borrow(pdbContext);
                ApplyAssemblyReferencesResult(
                    path,
                    inspection,
                    logger,
                    AssemblyReferencesQuery.Execute(session));
            }

            if (queryPlan is not null)
            {
                using var queryContext = new Sections.InspectionQueryContext
                {
                    AssemblyPath = path,
                    AssemblyReference = assemblyReference,
                    BodyReferenceResolver = bodyReferenceResolver,
                    Model = inspection,
                    Logger = logger,
                    MetadataContext = pdbContext,
                    SourceLinkContext = sourceLinkQueryContext,
                    BodyAnalysisFeatures = bodyAnalysisFeatures,
                    Trace = trace,
                };

                await RunTypedQueriesAsync(
                    path,
                    inspection,
                    logger,
                    queryPlan,
                    queryContext,
                    projectOptimizationOpportunities,
                    trace).ConfigureAwait(false);
            }
            else if (options.Verbosity == Options.Verbosity.Detailed)
            {
                // Fallback for non-pipeline callers — open the assembly once for all bounded scans.
                try
                {
                    using var session =
                        AssemblyInspectionSession.Borrow(pdbContext);
                    ApplyExtensionMethodsResult(
                        path,
                        inspection,
                        logger,
                        ExtensionMethodsQuery.Execute(session));
                    ApplyCustomAttributesResult(
                        path,
                        inspection,
                        logger,
                        CustomAttributesQuery.Execute(session));
                    ApplyResourcesResult(
                        path,
                        inspection,
                        logger,
                        ResourcesQuery.Execute(session));
                    ApplyTypeForwardersResult(
                        path,
                        inspection,
                        logger,
                        TypeForwardersQuery.Execute(session));
                    ApplyUnionTypesResult(
                        path,
                        inspection,
                        logger,
                        UnionTypesQuery.Execute(session));
                    ApplyClassifiedMethodsResult(
                        path,
                        inspection,
                        logger,
                        ClassifiedMethodsQuery.Execute(session));
                }

                catch (Exception ex)
                {
                    logger.LogWarning($"Error opening {path} for scanning: {ex.Message}");
                    if (inspection.ExtensionMemberInspection is null)
                    {
                        inspection.SetExtensionMemberInspection(
                            FailedInspection<ExtensionMemberObservation>(
                                path, MetadataFindings.ExtensionMemberDescriptor, ex),
                            displayOrder: null);
                    }
                    inspection.ClassifiedMethodInspection ??= FailedInspection<ClassifiedMethodObservation>(
                        path, MetadataFindings.ClassifiedMethodDescriptor, ex);
                    inspection.ResourceInspection ??= FailedInspection<MetadataResource>(
                        path, MetadataFindings.ResourceDescriptor, ex);
                    if (inspection.AssemblyAttributeInspection is null)
                    {
                        inspection.SetAssemblyAttributeInspection(
                            FailedInspection<AssemblyAttributeInfo>(
                                path, MetadataFindings.AssemblyAttributeDescriptor, ex),
                            jsonOrder: null);
                    }
                    inspection.UnionTypeInspection ??= FailedInspection<UnionTypeInfo>(
                        path, MetadataFindings.UnionTypeDescriptor, ex);
                    inspection.TypeForwarderInspection ??= FailedInspection<TypeForwarderInfo>(
                        path, MetadataFindings.TypeForwarderDescriptor, ex);
                }
            }

            if ((needsAuditSignals
                    || options.CollectIdentifierConfusionReferenceTree)
                && inspection.AssemblyReferenceInspection
                    is FindingInspection<AssemblyReference>.Failed)
            {
                IdentifierConfusionAuditFailureKind failure =
                    inspection.AssemblyReferenceFailureKind
                    ?? IdentifierConfusionAuditFailureKind.InspectionFailed;
                inspection.IdentifierConfusionFailure = failure;
            }

            // The query produces the flat direct-reference currency. Tree traversal remains a
            // path-owning CLI projection over that result.
            if (collectReferenceTree
                && inspection.AssemblyReferenceIdentities is { Count: > 0 } referenceIdentities)
            {
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                visited.Add(
                    inspection.AssemblyInfo.AssemblyName
                    ?? Path.GetFileNameWithoutExtension(path));

                inspection.AssemblyInfo.TransitiveReferences = BuildTransitiveReferences(
                    referenceIdentities,
                    path,
                    visited,
                    logger,
                    deduplicate: true,
                    maxDepth: options.ReferenceTreeDepth);
            }

            if (options.CollectIdentifierConfusionReferenceTree
                && inspection.AssemblyReferenceIdentities is { Count: > 0 } auditReferences)
            {
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                visited.Add(
                    inspection.AssemblyInfo.AssemblyName
                    ?? Path.GetFileNameWithoutExtension(path));

                try
                {
                    inspection.IdentifierConfusionReferenceClosure =
                        BuildTransitiveReferences(
                            auditReferences,
                            path,
                            visited,
                            logger,
                            deduplicate: true,
                            failOnReadError: true);
                }
                catch (
                    IdentifierConfusionReferenceTraversalException ex)
                {
                    inspection.IdentifierConfusionFailure =
                        ex.FailureKind;
                }
            }

            inspection.FileSize = pdbContext.FileSize;
            inspection.LastModified = pdbContext.LastWriteTimeUtc;

            // Cheap discovery needs local applicability facts, not the source-analysis model.
            // Preserve the PDB facts that drive section gates, then avoid source findings,
            // compilation records, builder inference, and every network-capable stage.
            if (discoveryOnly)
            {
                inspection.PdbFormat = pdbContext.PdbFormat;
                inspection.PdbLocation = pdbContext.PdbLocation;
                inspection.HasSourceLink = service.HasSourceLink;
                inspection.SourceLinkJson = service.SourceLinkJson;
                inspection.WindowsPdbDetected = pdbContext.WindowsPdbDetected;
                return inspection;
            }

            var sourcePlan = LibrarySourcePlans.For(options);
            bool sourceQueryCheckedPdb =
                requiredQueries?.Contains(SourceLinkDocumentsQuery.Definition) == true;

            await AuditAsync(
                service,
                inspection,
                path,
                packageName,
                packageVersion,
                logger,
                httpClient,
                isPlatformAssembly,
                allowPdbDownload: sourcePlan.AllowPdbDownload && !sourceQueryCheckedPdb,
                pdbAcquisitionAttempted: sourceQueryCheckedPdb,
                readCachedPdb: sourcePlan.ReadCachedPdb,
                sourceOptions: options.SourceOptions);

            var sourceSubject = FindingSubjectFor(path);
            inspection.SourceDocumentInspection ??= SourceLinkFindings.InspectSourceDocuments(
                service,
                sourceSubject);
            inspection.CompilationOptionInspection = MetadataFindings.InspectCompilationOptions(
                service.Context,
                sourceSubject);
            inspection.CompilationReferenceInspection = MetadataFindings.InspectCompilationReferences(
                service.Context,
                sourceSubject);

            if (needsAuditSignals)
                AuditSignalBuilder.RefreshLibraryAuditSignals(inspection);

            if (sourcePlan.CollectSourceFiles)
            {
                inspection.SourceFiles = await SourceFileCollector.CollectAsync(
                    service,
                    path,
                    browsableUrls: options.BrowsableUrls,
                    typeFilter: options.TypeFilter);
            }

            return inspection;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InspectionQueryException)
        {
            throw;
        }
        catch (CostDeclarationException)
        {
            throw;
        }
        catch (IdentifierConfusionReferenceTraversalException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Failed to inspect {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    static Analysis.LibraryBodyAnalysisFeatures SelectBodyAnalysisFeatures(
        IReadOnlyCollection<InspectionQueryDefinition>? queries)
    {
        var features = Analysis.LibraryBodyAnalysisFeatures.None;
        if (queries?.Contains(TopLeverageQuery.Definition) == true
            || queries?.Contains(UnsafeEvidenceQuery.Definition) == true)
        {
            features |= Analysis.LibraryBodyAnalysisFeatures.MethodEvidence;
        }
        if (queries?.Contains(OptimizationOpportunitiesQuery.Definition) == true)
        {
            features |=
                Analysis.LibraryBodyAnalysisFeatures.OptimizationOpportunities;
        }
        if (queries?.Contains(ResourceTriageQuery.Definition) == true)
            features |= Analysis.LibraryBodyAnalysisFeatures.LeakTriage;
        if (queries?.Contains(BodyShapesQuery.Definition) == true)
            features |= Analysis.LibraryBodyAnalysisFeatures.MethodEvidence;
        return features;
    }

    /// <summary>
    /// PDB acquisition, SourceLink detection, and builder inference.
    /// </summary>
    /// <param name="skipPdbDownload">Skip downloading PDB from symbol servers (for quiet/minimal verbosity).</param>
    public static async Task AuditAsync(
        SourceLinkService service,
        LibraryInspection inspection,
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        VerboseLogger logger,
        HttpClient httpClient,
        bool isPlatformAssembly = false,
        bool allowPdbDownload = false,
        bool pdbAcquisitionAttempted = false,
        bool readCachedPdb = false,
        NuGetSourceOptions? sourceOptions = null)
    {
        var pdbContext = service.Context;
        string? pdbStoreFailure = null;

        if (pdbContext.HasPdb)
        {
            inspection.PdbFormat = pdbContext.PdbFormat;
            inspection.PdbLocation = pdbContext.PdbLocation;
            inspection.SymbolServer = pdbContext.SymbolServer;
            inspection.HasSourceLink = service.HasSourceLink;
            inspection.SourceLinkJson = service.SourceLinkJson;
        }

        if (!pdbContext.HasPdb && pdbContext.WindowsPdbDetected)
        {
            inspection.WindowsPdbDetected = true;
            inspection.PdbFormat = pdbContext.PdbFormat;
            inspection.PdbLocation = pdbContext.PdbLocation;
        }

        // If no local PDB, try downloading (only when a selected section authorizes remote acquisition)
        if (!pdbContext.HasPdb && !pdbContext.WindowsPdbDetected && allowPdbDownload)
        {
            pdbStoreFailure = await AcquirePdbForAuditAsync(
                pdbContext,
                httpClient,
                packageName,
                packageVersion,
                isPlatformAssembly,
                logger,
                cacheOnly: false,
                sourceOptions: sourceOptions);

            if (pdbContext.HasPdb)
            {
                inspection.PdbFormat = pdbContext.PdbFormat;
                inspection.PdbLocation = pdbContext.PdbLocation;
                inspection.SymbolServer = pdbContext.SymbolServer;
                inspection.HasSourceLink = service.HasSourceLink;
                inspection.SourceLinkJson = service.SourceLinkJson;
            }
            else if (pdbContext.WindowsPdbDetected)
            {
                inspection.WindowsPdbDetected = true;
                inspection.PdbFormat = "Windows";
            }
        }

        // No embedded/adjacent PDB and download not authorized (Normal / bare-`S`): try a
        // network-free cache-only read so symbol-dependent sections (Symbols, Signals, SourceLink
        // provenance) can reflect an already-cached PDB. cacheOnly never touches the network.
        if (!pdbContext.HasPdb && !pdbContext.WindowsPdbDetected && !allowPdbDownload && readCachedPdb)
        {
            pdbStoreFailure = await AcquirePdbForAuditAsync(
                pdbContext, httpClient, packageName, packageVersion,
                isPlatformAssembly, logger, cacheOnly: true,
                sourceOptions: sourceOptions);

            if (pdbContext.HasPdb)
            {
                inspection.PdbFormat = pdbContext.PdbFormat;
                inspection.PdbLocation = pdbContext.PdbLocation;
                inspection.SymbolServer = pdbContext.SymbolServer;
                inspection.HasSourceLink = service.HasSourceLink;
                inspection.SourceLinkJson = service.SourceLinkJson;
            }
            else if (pdbContext.WindowsPdbDetected)
            {
                inspection.WindowsPdbDetected = true;
                inspection.PdbFormat = "Windows";
            }
        }

        // Determine reason for missing SourceLink
        if (inspection.HasSourceLink)
        {
            inspection.SourceLinkUnavailableReason = null;
        }
        else
        {
            if (pdbStoreFailure is not null)
            {
                inspection.SourceLinkUnavailableReason = pdbStoreFailure;
            }
            else if (inspection.WindowsPdbDetected)
            {
                inspection.SourceLinkUnavailableReason = "Windows PDB";
            }
            else if (!pdbContext.HasPdb
                     && !allowPdbDownload
                     && !pdbAcquisitionAttempted
                     && inspection.PdbPath != null)
            {
                inspection.SourceLinkUnavailableReason = "PDB not checked";
            }
            else if (!pdbContext.HasPdb && inspection.PdbPath != null)
            {
                inspection.SourceLinkUnavailableReason = "external PDB not found";
            }
            else if (!pdbContext.HasPdb)
            {
                inspection.SourceLinkUnavailableReason = "no symbols";
            }
            else
            {
                inspection.SourceLinkUnavailableReason = "PDB checked; no SourceLink data";
            }
        }

        ApplySourceLinkAudit(service, inspection);
        inspection.Builder = InferBuilder(inspection);
    }

    private static async Task<string?> AcquirePdbForAuditAsync(
        PdbContext context,
        HttpClient httpClient,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        VerboseLogger logger,
        bool cacheOnly,
        NuGetSourceOptions? sourceOptions)
    {
        try
        {
            await SourceEnricher.AcquirePdbAsync(
                context,
                httpClient,
                packageName,
                packageVersion,
                isPlatformAssembly,
                logger.Log,
                cacheOnly,
                sourceOptions).ConfigureAwait(false);
            return null;
        }
        catch (PdbStoreAcquisitionException exception)
        {
            logger.LogWarning(exception.Message);
            return exception.Message;
        }
    }

    private static void ApplySourceLinkAudit(
        SourceLinkService service,
        LibraryInspection inspection)
    {
        SourceLinkDebugAudit audit =
            SourceLinkInspector.InspectDebugInformation(service);
        inspection.HasSourceLink = audit.SourceLinkMap.IsPresent;
        inspection.SourceLinkJson = service.SourceLinkJson;
        inspection.SourceLinkMap = audit.SourceLinkMap.IsPresent
            ? audit.SourceLinkMap
            : null;
        inspection.HasNormalizedPaths = audit.HasNormalizedPaths;
        inspection.NonNormalizedPaths = audit.NonNormalizedPaths is null
            ? null
            : [.. audit.NonNormalizedPaths];
        inspection.IsDeterministic =
            service.Context.HasReproducibleFlag
            && audit.HasNormalizedPaths != false;
    }

    /// <summary>
    /// Network-free probe: does this assembly resolve a PDB (embedded, adjacent, or already in
    /// the symbol cache) that exposes a SourceLink document? Used by <c>-D</c> discovery to decide
    /// whether the SourceLink section family is effective without touching the network. The symbol
    /// cache is consulted read-only (no download); rendering a selected SourceLink section still
    /// performs its network work on demand.
    /// </summary>
    public static async Task<bool> ProbeLocalSourceLinkAsync(
        string assemblyPath,
        HttpClient httpClient,
        VerboseLogger logger,
        bool isPlatformAssembly = false,
        string? packageName = null,
        string? packageVersion = null,
        NuGetSourceOptions? sourceOptions = null)
        => await ProbeLocalSourceLinkAsync(
            () => SourceLinkService.Open(assemblyPath, logger.Log),
            assemblyPath,
            httpClient,
            logger,
            isPlatformAssembly,
            packageName,
            packageVersion,
            sourceOptions);

    public static async Task<bool> ProbeLocalSourceLinkAsync(
        ResolvedAssemblyReference assembly,
        HttpClient httpClient,
        VerboseLogger logger,
        bool isPlatformAssembly = false,
        string? packageName = null,
        string? packageVersion = null,
        NuGetSourceOptions? sourceOptions = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return await ProbeLocalSourceLinkAsync(
            () => SourceLinkService.Open(assembly, logger.Log),
            assembly.Path ?? assembly.Identity.Name,
            httpClient,
            logger,
            isPlatformAssembly,
            packageName,
            packageVersion,
            sourceOptions);
    }

    private static async Task<bool> ProbeLocalSourceLinkAsync(
        Func<SourceLinkService> openService,
        string subject,
        HttpClient httpClient,
        VerboseLogger logger,
        bool isPlatformAssembly,
        string? packageName,
        string? packageVersion,
        NuGetSourceOptions? sourceOptions)
    {
        try
        {
            using var service = openService();
            var context = service.Context;

            if (!context.HasPdb && !context.WindowsPdbDetected && context.NeedsPdb)
            {
                await SourceEnricher.AcquirePdbAsync(
                    context, httpClient, packageName, packageVersion,
                    isPlatformAssembly, logger.Log, cacheOnly: true,
                    sourceOptions: sourceOptions);
            }

            return context.HasPdb && service.HasSourceLink;
        }
        catch (Exception ex)
        {
            logger.Log(
                $"SourceLink discovery probe failed for {subject}: "
                + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Infers who built the assembly from symbol availability.
    /// </summary>
    /// <remarks>
    /// Deliberately does <em>not</em> consult SourceLink provenance. Establishing that an
    /// assembly's source is served from <c>dotnet/*</c> says where the source came from, not who
    /// produced the binary, and both the map and the <c>Company</c> attribute are supplied by the
    /// artifact under inspection. <c>raw.githubusercontent.com</c> serves any commit reachable in
    /// a repository, including the head of an outside contributor's unmerged pull request against
    /// <c>dotnet/runtime</c>, so a correctly established <c>dotnet</c> origin is consistent with an
    /// assembly Microsoft never built. A symbol server that served the PDB is evidence about the
    /// publisher; a self-declared source URL is not.
    /// </remarks>
    public static string? InferBuilder(LibraryInspection inspection)
    {
        var company = inspection.AssemblyInfo?.Company;
        bool isMicrosoftAssembly = company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true;

        if (!isMicrosoftAssembly)
        {
            return null;
        }

        if (inspection.SymbolServer == "msdl.microsoft.com" && inspection.HasSourceLink)
        {
            return "Microsoft";
        }

        if (inspection.SourceLinkUnavailableReason == "no symbols")
        {
            return "Unknown";
        }

        return null;
    }

    /// <summary>
    /// Builds a recursive tree of assembly references with resolution.
    /// </summary>
    public static List<AssemblyReferenceNode> BuildTransitiveReferences(
        IReadOnlyList<AssemblyReferenceIdentity> references,
        string assemblyPath,
        HashSet<string> visited,
        VerboseLogger logger,
        int depth = 0,
        bool deduplicate = false,
        int? maxDepth = null,
        bool failOnReadError = false)
    {
        string fullAssemblyPath = Path.GetFullPath(assemblyPath);
        StringComparer pathComparer = ReferenceTreePathComparer(
            OperatingSystem.IsWindows());
        var bindingPolicies = new Dictionary<string, IAssemblyBindingPolicy>(
            pathComparer);
        IAssemblyBindingPolicy bindingPolicy =
            ReferenceTreeBindingPolicyFor(assemblyPath, bindingPolicies);
        var visitedPaths = new HashSet<string>(pathComparer)
        {
            fullAssemblyPath
        };
        if (deduplicate)
        {
            return BuildDeduplicatedTransitiveReferences(
                references,
                bindingPolicy,
                bindingPolicies,
                Path.GetDirectoryName(fullAssemblyPath)
                    ?? throw new InvalidOperationException(
                        "The assembly path has no containing directory."),
                visited,
                visitedPaths,
                logger,
                depth,
                maxDepth,
                failOnReadError);
        }

        return BuildTransitiveReferences(
            references,
            bindingPolicy,
            bindingPolicies,
            Path.GetDirectoryName(fullAssemblyPath)
                ?? throw new InvalidOperationException(
                    "The assembly path has no containing directory."),
            visited,
            visitedPaths,
            logger,
            depth,
            maxDepth,
            failOnReadError);
    }

    private static IAssemblyBindingPolicy ReferenceTreeBindingPolicyFor(
        string assemblyPath,
        Dictionary<string, IAssemblyBindingPolicy> bindingPolicies)
    {
        string fullPath = Path.GetFullPath(assemblyPath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "The assembly path has no containing directory.");
        if (bindingPolicies.TryGetValue(directory, out var existing))
            return existing;

        var created = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(assemblyPath)
            {
                // Preserve the tree's existing local-sibling and platform scope.
                // Package/deps.json graph expansion belongs to the package resolver.
                PackageRoots = [],
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = false,
                IncludeInstalledPlatformFallback = true,
                // The tree describes the available sibling or installed platform
                // assembly, as it did before typed binding owned selection.
                IgnoreAssemblyVersion = true,
            });
        bindingPolicies.Add(directory, created);
        return created;
    }

    private sealed record DeduplicatedReferenceNode(
        AssemblyReferenceNode Value)
    {
        public List<DeduplicatedReferenceNode> Children { get; } = [];
    }

    private readonly record struct PendingAssemblyReference(
        AssemblyReferenceIdentity Reference,
        IAssemblyBindingPolicy BindingPolicy,
        string BindingScope,
        int Depth,
        DeduplicatedReferenceNode? Parent);

    private static List<AssemblyReferenceNode>
        BuildDeduplicatedTransitiveReferences(
            IReadOnlyList<AssemblyReferenceIdentity> references,
            IAssemblyBindingPolicy bindingPolicy,
            Dictionary<string, IAssemblyBindingPolicy> bindingPolicies,
            string bindingScope,
            HashSet<string> visited,
            HashSet<string> visitedPaths,
            VerboseLogger logger,
            int depth,
            int? maxDepth,
            bool failOnReadError)
    {
        List<DeduplicatedReferenceNode> roots = [];
        var seen = new HashSet<AssemblyReferenceTraversalKey>(
            AssemblyReferenceTraversalKeyComparer.Instance);
        var pending = new Queue<PendingAssemblyReference>(
            references
                .OrderBy(reference => reference.Name)
                .Select(
                    reference =>
                        new PendingAssemblyReference(
                            reference,
                            bindingPolicy,
                            bindingScope,
                            depth,
                            Parent: null)));

        while (pending.Count > 0)
        {
            PendingAssemblyReference next = pending.Dequeue();
            AssemblyReferenceIdentity reference = next.Reference;
            var node = new AssemblyReferenceNode
            {
                Name = reference.Name,
                Version = reference.Version?.ToString() ?? "",
                PublicKeyToken = reference.PublicKeyToken,
                Depth = next.Depth,
            };

            AssemblyBindingSelection selection =
                next.BindingPolicy.Select(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.Reference(reference),
                        AssemblyBindingOrigin.Global(),
                        AssemblyResolutionScope.Any)).Selection;
            ResolvedAssemblyReference? resolved =
                (selection as AssemblyBindingSelection.Selected)
                    ?.Assembly;
            if (selection is AssemblyBindingSelection.Unavailable
                unavailable)
            {
                node.ResolutionFailure =
                    AssemblyReferenceResolutionFailure.Unavailable;
                IdentifierConfusionAuditFailureKind failure =
                    ClassifyIdentifierConfusionBindingFailure(
                        unavailable.Failure);
                if (failOnReadError)
                {
                    throw new IdentifierConfusionReferenceTraversalException(
                        failure);
                }
                logger.LogWarning(
                    "Could not inspect a resolved assembly reference: "
                    + IdentifierConfusionAudit.DescribeFailure(failure));
            }
            else if (selection is AssemblyBindingSelection.Rejected
                rejected)
            {
                node.ResolutionFailure =
                    AssemblyReferenceResolutionFailure.Rejected;
                IdentifierConfusionAuditFailureKind failure =
                    ClassifyIdentifierConfusionBindingFailure(
                        rejected.Failure);
                if (failOnReadError)
                {
                    throw new IdentifierConfusionReferenceTraversalException(
                        failure);
                }
                logger.LogWarning(
                    "Could not inspect a resolved assembly reference: "
                    + IdentifierConfusionAudit.DescribeFailure(failure));
            }

            node.Path = resolved?.Path;
            node.ResolvedFrom =
                resolved?.Provenance
                    is AssemblyResolutionProvenance.PlatformAsset
                    ? "platform"
                    : resolved is null
                        ? null
                        : "local";

            string? resolvedPath =
                resolved?.Path is { } selectedPath
                    ? Path.GetFullPath(selectedPath)
                    : null;
            bool isRootCycle = resolved is not null
                && (resolvedPath is not null
                    ? visitedPaths.Contains(resolvedPath)
                    : visited.Contains(resolved.Identity.Name));
            if (isRootCycle)
                continue;

            AssemblyReferenceTraversalKey traversalKey =
                resolvedPath is not null
                    ? AssemblyReferenceTraversalKey.ForResolvedPath(
                        resolvedPath)
                    : AssemblyReferenceTraversalKey.ForReference(
                        reference,
                        next.BindingScope);
            if (!seen.Add(traversalKey))
                continue;

            var treeNode = new DeduplicatedReferenceNode(node);
            if (next.Parent is null)
                roots.Add(treeNode);
            else
                next.Parent.Children.Add(treeNode);

            if (resolved is null)
                continue;

            try
            {
                var (childReferences, company) =
                    AssemblyInspector
                        .ExtractReferenceIdentitiesAndCompany(
                            resolved);
                node.Company = company;
                if (childReferences.Count == 0
                    || (maxDepth is not null
                        && next.Depth + 1 >= maxDepth.Value))
                {
                    continue;
                }

                IAssemblyBindingPolicy childBindingPolicy =
                    resolved.Path is { } childPath
                        ? ReferenceTreeBindingPolicyFor(
                            childPath,
                            bindingPolicies)
                        : next.BindingPolicy;
                string childBindingScope =
                    resolvedPath is not null
                        ? Path.GetDirectoryName(resolvedPath)
                            ?? next.BindingScope
                        : next.BindingScope;
                foreach (AssemblyReferenceIdentity childReference in
                    childReferences.OrderBy(
                        childReference =>
                            childReference.Name))
                {
                    pending.Enqueue(
                        new PendingAssemblyReference(
                            childReference,
                            childBindingPolicy,
                            childBindingScope,
                            next.Depth + 1,
                            treeNode));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IdentifierConfusionReferenceTraversalException)
            {
                throw;
            }
            catch (Exception ex) when (
                failOnReadError
                || ex is IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException)
            {
                if (failOnReadError)
                {
                    throw new IdentifierConfusionReferenceTraversalException(
                        ClassifyIdentifierConfusionReferenceFailure(ex),
                        ex);
                }

                logger.LogWarning(
                    "Could not inspect a resolved assembly reference: "
                    + IdentifierConfusionAudit.DescribeFailure(
                        ClassifyIdentifierConfusionReferenceFailure(ex)));
            }
        }

        List<AssemblyReferenceNode> flattened = [];
        foreach (DeduplicatedReferenceNode root in roots)
            FlattenDeduplicatedReferenceTree(root, flattened);
        return flattened;
    }

    private static void FlattenDeduplicatedReferenceTree(
        DeduplicatedReferenceNode node,
        List<AssemblyReferenceNode> flattened)
    {
        flattened.Add(node.Value);
        foreach (DeduplicatedReferenceNode child in node.Children)
            FlattenDeduplicatedReferenceTree(child, flattened);
    }

    private static List<AssemblyReferenceNode> BuildTransitiveReferences(
        IReadOnlyList<AssemblyReferenceIdentity> references,
        IAssemblyBindingPolicy bindingPolicy,
        Dictionary<string, IAssemblyBindingPolicy> bindingPolicies,
        string bindingScope,
        HashSet<string> visited,
        HashSet<string> visitedPaths,
        VerboseLogger logger,
        int depth,
        int? maxDepth,
        bool failOnReadError)
    {
        List<AssemblyReferenceNode> nodes = [];

        foreach (var reference in references.OrderBy(r => r.Name))
        {
            var node = new AssemblyReferenceNode
            {
                Name = reference.Name,
                Version = reference.Version?.ToString() ?? "",
                PublicKeyToken = reference.PublicKeyToken,
                Depth = depth
            };

            AssemblyBindingSelection selection = bindingPolicy.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(reference),
                    AssemblyBindingOrigin.Global(),
                    AssemblyResolutionScope.Any)).Selection;
            ResolvedAssemblyReference? resolved =
                (selection as AssemblyBindingSelection.Selected)?.Assembly;
            if (selection is AssemblyBindingSelection.Unavailable unavailable)
            {
                node.ResolutionFailure =
                    AssemblyReferenceResolutionFailure.Unavailable;
                IdentifierConfusionAuditFailureKind failure =
                    ClassifyIdentifierConfusionBindingFailure(
                        unavailable.Failure);
                if (failOnReadError)
                {
                    throw new IdentifierConfusionReferenceTraversalException(
                        failure);
                }
                logger.LogWarning(
                    "Could not inspect a resolved assembly reference: "
                    + IdentifierConfusionAudit.DescribeFailure(failure));
            }
            else if (selection is AssemblyBindingSelection.Rejected rejected)
            {
                node.ResolutionFailure =
                    AssemblyReferenceResolutionFailure.Rejected;
                IdentifierConfusionAuditFailureKind failure =
                    ClassifyIdentifierConfusionBindingFailure(
                        rejected.Failure);
                if (failOnReadError)
                {
                    throw new IdentifierConfusionReferenceTraversalException(
                        failure);
                }
                logger.LogWarning(
                    "Could not inspect a resolved assembly reference: "
                    + IdentifierConfusionAudit.DescribeFailure(failure));
            }

            node.Path = resolved?.Path;
            node.ResolvedFrom = resolved?.Provenance is AssemblyResolutionProvenance.PlatformAsset
                ? "platform"
                : resolved is null
                    ? null
                    : "local";

            string? resolvedPath = resolved?.Path is { } selectedPath
                ? Path.GetFullPath(selectedPath)
                : null;
            bool isCyclic = resolved is not null
                && (resolvedPath is not null
                    ? visitedPaths.Contains(resolvedPath)
                    : visited.Contains(resolved.Identity.Name));
            if (isCyclic)
            {
                node.IsCyclic = true;
                nodes.Add(node);
                continue;
            }

            nodes.Add(node);

            if (resolved != null)
            {
                try
                {
                    var (childRefs, company) =
                        AssemblyInspector.ExtractReferenceIdentitiesAndCompany(resolved);
                    node.Company = company;
                    if (childRefs.Count > 0
                        && (maxDepth is null || depth + 1 < maxDepth.Value))
                    {
                        var branchVisited = new HashSet<string>(
                            visited,
                            StringComparer.OrdinalIgnoreCase)
                        {
                            resolved.Identity.Name
                        };
                        var branchVisitedPaths = new HashSet<string>(
                            visitedPaths,
                            visitedPaths.Comparer);
                        if (resolvedPath is not null)
                            branchVisitedPaths.Add(resolvedPath);
                        IAssemblyBindingPolicy childBindingPolicy =
                            resolved.Path is { } childPath
                                ? ReferenceTreeBindingPolicyFor(
                                    childPath,
                                    bindingPolicies)
                                : bindingPolicy;
                        var childNodes = BuildTransitiveReferences(
                            childRefs,
                            childBindingPolicy,
                            bindingPolicies,
                            resolvedPath is not null
                                ? Path.GetDirectoryName(resolvedPath)
                                    ?? bindingScope
                                : bindingScope,
                            branchVisited,
                            branchVisitedPaths,
                            logger,
                            depth + 1,
                            maxDepth,
                            failOnReadError);
                        nodes.AddRange(childNodes);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (IdentifierConfusionReferenceTraversalException)
                {
                    throw;
                }
                catch (Exception ex) when (
                    failOnReadError
                    || ex is IOException
                        or UnauthorizedAccessException
                        or BadImageFormatException)
                {
                    if (failOnReadError)
                    {
                        throw new IdentifierConfusionReferenceTraversalException(
                            ClassifyIdentifierConfusionReferenceFailure(ex),
                            ex);
                    }

                    logger.LogWarning(
                        "Could not inspect a resolved assembly reference: "
                        + IdentifierConfusionAudit.DescribeFailure(
                            ClassifyIdentifierConfusionReferenceFailure(ex)));
                }
            }
        }

        return nodes;
    }

    internal static StringComparer ReferenceTreePathComparer(bool isWindows) =>
        isWindows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly record struct AssemblyReferenceTraversalKey(
        string? ResolvedPath,
        AssemblyReferenceIdentity? Reference,
        string? BindingScope)
    {
        public static AssemblyReferenceTraversalKey ForResolvedPath(
            string resolvedPath) =>
            new(resolvedPath, null, null);

        public static AssemblyReferenceTraversalKey ForReference(
            AssemblyReferenceIdentity reference,
            string bindingScope) =>
            new(null, reference, bindingScope);
    }

    private sealed class AssemblyReferenceTraversalKeyComparer
        : IEqualityComparer<AssemblyReferenceTraversalKey>
    {
        private static readonly StringComparer PathComparer =
            ReferenceTreePathComparer(OperatingSystem.IsWindows());

        public static AssemblyReferenceTraversalKeyComparer Instance
        {
            get;
        } = new();

        public bool Equals(
            AssemblyReferenceTraversalKey x,
            AssemblyReferenceTraversalKey y)
        {
            if (x.ResolvedPath is not null
                || y.ResolvedPath is not null)
            {
                return x.ResolvedPath is not null
                    && y.ResolvedPath is not null
                    && PathComparer.Equals(
                        x.ResolvedPath,
                        y.ResolvedPath);
            }

            return x.Reference is { } xReference
                && y.Reference is { } yReference
                && PathComparer.Equals(
                    x.BindingScope,
                    y.BindingScope)
                && StringComparer.Ordinal.Equals(
                    xReference.Name,
                    yReference.Name)
                && EqualityComparer<Version?>.Default.Equals(
                    xReference.Version,
                    yReference.Version)
                && StringComparer.OrdinalIgnoreCase.Equals(
                    xReference.Culture,
                    yReference.Culture)
                && StringComparer.OrdinalIgnoreCase.Equals(
                    xReference.PublicKeyToken,
                    yReference.PublicKeyToken);
        }

        public int GetHashCode(AssemblyReferenceTraversalKey value)
        {
            var hash = new HashCode();
            if (value.ResolvedPath is not null)
            {
                hash.Add(value.ResolvedPath, PathComparer);
            }
            else if (value.Reference is { } reference)
            {
                hash.Add(value.BindingScope, PathComparer);
                hash.Add(
                    reference.Name,
                    StringComparer.Ordinal);
                hash.Add(reference.Version);
                hash.Add(
                    reference.Culture,
                    StringComparer.OrdinalIgnoreCase);
                hash.Add(
                    reference.PublicKeyToken,
                    StringComparer.OrdinalIgnoreCase);
            }
            return hash.ToHashCode();
        }
    }

    private static IdentifierConfusionAuditFailureKind
        ClassifyIdentifierConfusionBindingFailure(
            AssemblyBindingFailure failure) =>
        failure.CandidateFailureKind switch
        {
            CandidateOpenFailureKind.InvalidImage =>
                IdentifierConfusionAuditFailureKind.InvalidAssemblyMetadata,
            CandidateOpenFailureKind.Unreadable =>
                IdentifierConfusionAuditFailureKind.AssemblyUnreadable,
            CandidateOpenFailureKind.ResourceBudget =>
                IdentifierConfusionAuditFailureKind.InspectionFailed,
            _ => IdentifierConfusionAuditFailureKind.InspectionFailed,
        };

    private static IdentifierConfusionAuditFailureKind
        ClassifyIdentifierConfusionReferenceFailure(Exception exception) =>
        exception switch
        {
            BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException =>
                IdentifierConfusionAuditFailureKind.InvalidAssemblyMetadata,
            IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ObjectDisposedException =>
                IdentifierConfusionAuditFailureKind.AssemblyUnreadable,
            _ => IdentifierConfusionAuditFailureKind.InspectionFailed,
        };

    internal sealed class IdentifierConfusionReferenceTraversalException
        : InvalidOperationException
    {
        public IdentifierConfusionReferenceTraversalException(
            IdentifierConfusionAuditFailureKind failureKind,
            Exception? innerException = null)
            : base(
                "Identifier audit could not inspect assembly references: "
                + IdentifierConfusionAudit.DescribeFailure(failureKind)
                + ".",
                innerException)
        {
            FailureKind = failureKind;
        }

        public IdentifierConfusionAuditFailureKind FailureKind { get; }
    }

    internal static string FormatMethod(Analysis.MethodIdentity method)
        => $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}({string.Join(", ", method.ParameterTypes.Select(p => p.ToQualifiedDisplayString()))})";

    // Compiler/source-generated implementation details (display classes, state machines,
    // the <>c lambda cache, <PrivateImplementationDetails>, System.Text.Json context
    // helpers) are not actionable source-shape fixes, so optimization scans suppress them
    // and leverage scans label them as generated.
    internal static bool IsGeneratedMethod(Analysis.MethodIdentity method)
        => Analysis.OptimizationOpportunityRanking.IsGeneratedMethod(
            method);

    // Overload that also treats members of structurally-detected generated framework types
    // (protobuf/gRPC, see LibraryBodyIndex.GeneratedFrameworkTypes) as generated, so their
    // thick static initializers and stubs are marked in Top Leverage and suppressed from
    // Performance Triage even though no [GeneratedCode] attribute is emitted.
    internal static bool IsGeneratedMethod(
        Analysis.MethodIdentity method,
        IReadOnlySet<Analysis.TypeRef> generatedFrameworkTypes)
        => Analysis.OptimizationOpportunityRanking.IsGeneratedMethod(
            method,
            generatedFrameworkTypes);

    internal static bool IncludePerformanceOpportunity(
        Analysis.OptimizationOpportunity opportunity,
        IReadOnlySet<Analysis.TypeRef> generatedFrameworkTypes)
        => Analysis.OptimizationOpportunityRanking
            .IncludePerformanceOpportunity(
                opportunity,
                generatedFrameworkTypes);

    /// <summary>
    /// Builds a metadata-token → (Stable, Visibility, Selector) map across the whole
    /// assembly by running the shared <see cref="ApiOutputFormatter.BuildMemberDrillMap"/>
    /// per type. Two surfaces are extracted: the default (public) surface numbers public
    /// overloads as <c>member Name:N</c> resolves them <em>without</em> <c>--all</c>, and the
    /// all-members surface supplies non-public members (which require <c>--all</c> to drill).
    /// Preferring the default-surface entry for a token keeps the emitted <c>Name:N</c>
    /// selector round-trippable in the same context a reader would use it. Failures degrade
    /// to an empty map (rows simply omit the selector columns).
    /// </summary>
    internal static Dictionary<int, (string? Stable, string Visibility, string Selector)>
        BuildLibraryDrillMap(
            PdbContext context,
            VerboseLogger logger)
    {
        var map = new Dictionary<int, (string? Stable, string Visibility, string Selector)>();
        try
        {
            if (!context.HasMetadata)
                return map;

            AddSurface(context.ExtractApiSurface(includeAll: true), map);
            AddSurface(context.ExtractApiSurface(includeAll: false), map);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                $"Error building leverage selectors for {context.AssemblyPath}: {ex.Message}");
        }
        return map;

        static void AddSurface(
            ApiSurface surface,
            Dictionary<int, (string? Stable, string Visibility, string Selector)> target)
        {
            foreach (var type in surface.Types)
            {
                foreach (var (token, drill) in ApiOutputFormatter.BuildMemberDrillMap(type))
                    target[token] = drill;
            }
        }
    }

    internal static IReadOnlySet<Analysis.MethodIdentity> PerformanceSourceMethods(
        IEnumerable<Analysis.OptimizationOpportunity> opportunities)
        => opportunities
            .Select(static opportunity =>
                opportunity.SourceOwner ?? opportunity.Method)
            .ToHashSet();

    internal static void ReportOptimizationDiagnostics(
        Analysis.LibraryBodyIndex index,
        Func<Analysis.AnalysisDiagnostic, bool>? include = null)
        => ReportOptimizationDiagnostics(index.Diagnostics, include);

    internal static void ReportOptimizationDiagnostics(
        IEnumerable<Analysis.AnalysisDiagnostic> diagnostics,
        Func<Analysis.AnalysisDiagnostic, bool>? include = null)
    {
        foreach (Analysis.AnalysisDiagnostic diagnostic
            in diagnostics)
        {
            if (include is not null && !include(diagnostic))
                continue;
            CommandError.WriteWarning(
                $"performance analysis incomplete for "
                + $"{diagnostic.Method}: "
                + diagnostic.Message);
        }
    }

    internal static OptimizationOpportunitySummary ProjectOptimizationOpportunity(
        Analysis.OptimizationOpportunity opportunity)
        => new()
        {
            Member = FormatMethod(opportunity.Method),
            Assembly = opportunity.Method.AssemblyName,
            ModuleVersionId =
                opportunity.Method.ModuleVersionId == Guid.Empty
                    ? null
                    : opportunity.Method.ModuleVersionId,
            MethodToken = FormatToken(
                opportunity.Method.MetadataToken),
            Candidate = opportunity.CandidateId,
            Finding = opportunity.SourceFinding,
            Provenance = FormatProvenance(opportunity.Provenance),
            RootReach = opportunity.RootReach,
            Shape = opportunity.Shape,
            Operation = opportunity.Operation,
            Token = FormatToken(opportunity.OperandToken),
            EvidenceMethod = FormatToken(
                opportunity.EvidenceMethodToken),
            SupportingFinding =
                opportunity.SupportingCallSite
                    ?.SourceFinding,
            SupportingOperation =
                opportunity.SupportingCallSite
                    ?.Operation,
            SupportingToken = FormatToken(
                opportunity.SupportingCallSite
                    ?.OperandToken),
            SupportingEvidenceMethod = FormatToken(
                opportunity.SupportingCallSite
                    ?.EvidenceMethodToken),
            SupportingIL =
                opportunity.SupportingCallSite is
                { ILOffset: var supportingOffset }
                    ? $"IL_{supportingOffset:X4}"
                    : null,
            Evidence = opportunity.Evidence,
            Fix = opportunity.SafeFixDirection,
            Priority = TriagePriority(opportunity),
            Confidence = opportunity.Confidence,
            Loop = IteratesInLoop(opportunity) ? "loop" : "",
            CallerLoop = FormatCallerLoop(opportunity.CallerLoop),
            CallerLoopDepth = opportunity.CallerLoop?.Depth,
            CallerLoopWitness = FormatCallerLoopWitness(opportunity.CallerLoop),
            Allocation = opportunity.RuntimeAllocationType,
            Path = opportunity.PathContext,
            PathConfidence = opportunity.PathConfidence,
            PostDominance = opportunity.PostDominance,
            IL = opportunity.ILOffset is { } offset ? $"IL_{offset:X4}" : null,
            Weight = opportunity.Weight,
            DirectSites = opportunity.DirectAllocationSites,
            OncePaths = opportunity.OnceAllocationPaths,
            ConditionalPaths = opportunity.ConditionalAllocationPaths,
            RepeatedPaths = opportunity.RepeatedAllocationPaths,
            UnknownPaths = opportunity.UnknownAllocationPaths,
            CachedSites = opportunity.CachedAllocationSites,
            OpaquePaths = opportunity.OpaqueCallPaths,
            Saturated = opportunity.AllocationCountSaturated ? "yes" : null,
        };

    internal static void ApplyResourceTriageResult(
        LibraryInspection inspection,
        ResourceTriageResult result,
        Func<IReadOnlyDictionary<int, (string? Stable, string Visibility, string Selector)>>
            getDrillMap)
    {
        ArgumentNullException.ThrowIfNull(getDrillMap);

        inspection.ResourceTriageQueryResult = result;
        inspection.ResourceLifecycleInspection = null;
        inspection.ResourceTriageAssessments = [];
        inspection.ResourceTriageDrillMap = null;
        inspection.ResourceTriage = null;

        switch (result)
        {
            case ResourceTriageResult.Available available:
                inspection.ResourceLifecycleInspection =
                    available.Inspection;
                var drillByToken = getDrillMap();
                inspection.ResourceTriageDrillMap = drillByToken;
                ImmutableArray<Analysis.ResourceTriageAssessment> assessments =
                [
                    .. available.Assessments
                        .Where(assessment =>
                            assessment.Actionability
                                == Analysis.ResourceTriageActionability
                                    .UntrustedActionable)
                        .OrderBy(
                            assessment => FormatMethod(
                                assessment.Source.Payload.Method),
                            StringComparer.Ordinal)
                        .ThenBy(
                            assessment =>
                                assessment.Source.Payload.AcquireOffset)
                        .ThenBy(
                            assessment =>
                                assessment.Boundaries.Length > 0
                                    ? assessment.Boundaries[0]
                                        .Evidence.ILOffset
                                    : -1),
                ];
                inspection.ResourceTriageAssessments = assessments;
                var rows = assessments
                    .Select(assessment =>
                        ProjectResourceTriageAssessment(
                            assessment,
                            drillByToken))
                    .ToList();
                inspection.ResourceTriage = rows;
                break;

            case ResourceTriageResult.NoMetadata:
                break;

            case ResourceTriageResult.Failed failed:
                inspection.ResourceLifecycleInspection =
                    new FindingInspection<
                        Analysis.ResourceLifecycleOccurrence>.Failed(
                            failed.Error);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown resource triage result '{result.GetType().Name}'.");
        }
    }

    internal static ResourceTriageSummary ProjectResourceTriageAssessment(
        Analysis.ResourceTriageAssessment assessment,
        IReadOnlyDictionary<int, (string? Stable, string Visibility, string Selector)>
            drillByToken)
    {
        var occurrence = assessment.Source.Payload;
        drillByToken.TryGetValue(
            occurrence.Method.MetadataToken,
            out var drill);
        return new ResourceTriageSummary
        {
            Member = FormatMethod(occurrence.Method),
            Candidate = assessment.CandidateId,
            Finding = assessment.Source.Descriptor.Id,
            Provenance = "exact",
            Resource = occurrence.Resource,
            Shape = occurrence.Shape,
            Impact = FormatResourceTriageImpact(assessment.Impact),
            Actionability = FormatResourceTriageActionability(
                assessment.Actionability),
            AcquireOffset = occurrence.AcquireOffset,
            Boundaries = assessment.Boundaries
                .Select(boundary => new ResourceBoundarySummary(
                    boundary.Evidence.Operation.ToQualifiedDisplayString(),
                    boundary.Evidence.ILOffset))
                .Distinct()
                .ToList(),
            Evidence = FormatResourceTriageReason(assessment.Reason),
            Direction = FormatResourceTriageRemediation(
                assessment.Remediation),
            Confidence = FormatResourceTriageConfidence(
                assessment.Confidence),
            Visibility = drill.Visibility,
            Stable = drill.Stable,
            Selector = drill.Selector,
        };
    }

    static string FormatResourceTriageImpact(
        Analysis.ResourceTriageImpact impact)
        => impact switch
        {
            Analysis.ResourceTriageImpact.PoolChurnOnException =>
                "pool churn if boundary throws",
            _ => throw new ArgumentOutOfRangeException(nameof(impact)),
        };

    static string FormatResourceTriageActionability(
        Analysis.ResourceTriageActionability actionability)
        => actionability switch
        {
            Analysis.ResourceTriageActionability.UntrustedActionable =>
                "untrusted-input boundary",
            _ => throw new ArgumentOutOfRangeException(nameof(actionability)),
        };

    static string FormatResourceTriageReason(
        Analysis.ResourceTriageReason reason)
        => reason switch
        {
            Analysis.ResourceTriageReason.ExternalInputBoundaryBeforeCleanup =>
                "An exact external-input boundary is reached before modeled cleanup; an exception can bypass Return.",
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    static string FormatResourceTriageRemediation(
        Analysis.ResourceTriageRemediation remediation)
        => remediation switch
        {
            Analysis.ResourceTriageRemediation.EnsureExceptionalCleanup =>
                "Return the pooled array from finally or catch-all cleanup.",
            _ => throw new ArgumentOutOfRangeException(nameof(remediation)),
        };

    static string FormatResourceTriageConfidence(
        Analysis.ResourceTriageConfidence confidence)
        => confidence switch
        {
            Analysis.ResourceTriageConfidence.Medium => "medium",
            _ => throw new ArgumentOutOfRangeException(nameof(confidence)),
        };

    // Performance Triage ordering separates static actionability from evidence confidence.
    // Algorithmic amplification, avoidable cache-lookup factory allocations, and actionable
    // high allocation weight lead. Escape-unknown small arrays and other generic repeated costs
    // are medium priority; ordinary one-shot candidates are low. Confidence then ranks the
    // certainty of the evidence/rewrite within that tier, followed by weight and call-graph reach.
    internal static IEnumerable<Analysis.OptimizationOpportunity> OrderByTriagePriority(IEnumerable<Analysis.OptimizationOpportunity> opportunities)
        => Analysis.OptimizationOpportunityRanking.Order(opportunities);

    internal static string TriagePriority(Analysis.OptimizationOpportunity opportunity)
        => Analysis.OptimizationOpportunityRanking.Priority(opportunity) switch
        {
            Analysis.OptimizationOpportunityPriority.High => "high",
            Analysis.OptimizationOpportunityPriority.Medium => "medium",
            _ => "low",
        };

    // Whether an allocation opportunity actually iterates as a hot loop, per the
    // semantic per-invocation multiplicity (#2127). A structural in-loop offset that
    // is really a return/throw early-exit (Multiplicity Conditional/Unknown) is NOT a
    // hot loop; fall back to the structural InLoop flag only when multiplicity is
    // unknown. This is the single source of truth for the Loop column, triage sort,
    // and the --loop filter.
    internal static bool IteratesInLoop(Analysis.OptimizationOpportunity opportunity)
        => Analysis.OptimizationOpportunityRanking.IteratesInLoop(
            opportunity);

    // Triage ordering weight for a confidence label (high allocations are the surest pay-dirt).
    static int ConfidenceRank(string confidence)
    {
        if (confidence.Equals("high", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (confidence.Equals("medium", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 0;
    }

    // Null (non-allocation, no weight) sorts distinctly below "low" rather than tying it.
    static int WeightSortRank(string? weight) => weight is null ? -1 : ConfidenceRank(weight);

    internal static IEnumerable<Analysis.OptimizationOpportunity> FilterAndOrderTriageOpportunities(
        IEnumerable<Analysis.OptimizationOpportunity> opportunities,
        PerformanceTriageOptions? options)
    {
        options ??= PerformanceTriageOptions.Default;
        var filtered = opportunities;
        if (options.LoopOnly)
            filtered = filtered.Where(IteratesInLoop);
        if (options.MinConfidence is { Length: > 0 } confidence)
        {
            var minimumRank = ConfidenceRank(confidence);
            filtered = filtered.Where(opportunity => ConfidenceRank(opportunity.Confidence) >= minimumRank);
        }
        if (options.Shapes.Length > 0)
        {
            var shapes = options.Shapes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(opportunity => shapes.Contains(opportunity.Shape));
        }
        if (options.TryGetPredicates(out var predicates, out _))
        {
            foreach (var predicate in predicates)
                filtered = filtered.Where(opportunity => MatchesTriagePredicate(opportunity, predicate));
        }

        var ordered = options.OrderBy is null && options.IncludesAllocationFanout
            ? filtered
                .OrderByDescending(opportunity => opportunity.OnceAllocationPaths ?? -1)
                .ThenByDescending(opportunity => opportunity.RepeatedAllocationPaths ?? -1)
                .ThenByDescending(opportunity => opportunity.ConditionalAllocationPaths ?? -1)
                .ThenBy(opportunity => FormatMethod(opportunity.Method), StringComparer.Ordinal)
            : options.TryGetOrderTerms(out var orderTerms, out _)
                ? OrderTriageRows(filtered, orderTerms)
                : OrderByTriagePriority(filtered);
        return options.Top is { } top ? ordered.Take(top) : ordered;
    }

    internal static IEnumerable<Analysis.OptimizationOpportunity> TriageOpportunities(
        Analysis.LibraryBodyIndex index,
        PerformanceTriageOptions? options)
        => options?.IncludesAllocationFanout == true
            ? index.OptimizationOpportunities.Concat(index.AllocationFanoutOpportunities)
            : index.OptimizationOpportunities;

    static IEnumerable<Analysis.OptimizationOpportunity> OrderTriageRows(
        IEnumerable<Analysis.OptimizationOpportunity> opportunities,
        IReadOnlyList<PerformanceTriageOptions.OrderTerm> orderTerms)
    {
        if (orderTerms.Count == 1
            && orderTerms[0].Field.Equals("Triage", StringComparison.OrdinalIgnoreCase))
        {
            var ordered = OrderByTriagePriority(opportunities);
            return orderTerms[0].Descending ? ordered : ordered.Reverse();
        }

        return opportunities.OrderBy(opportunity => opportunity, Comparer<Analysis.OptimizationOpportunity>.Create((left, right) =>
        {
            foreach (var term in orderTerms)
            {
                if (term.Field == "CallerLoopDepth")
                {
                    bool leftMissing = left.CallerLoop is null;
                    bool rightMissing = right.CallerLoop is null;
                    if (leftMissing != rightMissing)
                        return leftMissing ? 1 : -1;
                }

                int compare = CompareTriageField(left, right, term.Field);
                if (compare != 0)
                    return term.Descending ? -compare : compare;
            }

            int memberCompare = string.Compare(FormatMethod(left.Method), FormatMethod(right.Method), StringComparison.OrdinalIgnoreCase);
            if (memberCompare != 0)
                return memberCompare;
            int ilCompare = (left.ILOffset ?? -1).CompareTo(right.ILOffset ?? -1);
            if (ilCompare != 0)
                return ilCompare;
            return string.Compare(left.Shape, right.Shape, StringComparison.OrdinalIgnoreCase);
        }));
    }

    static bool MatchesTriagePredicate(Analysis.OptimizationOpportunity opportunity, PerformanceTriageOptions.RowPredicate predicate)
    {
        if (NumericTriageField(opportunity, predicate.Field) is { } actualNumber)
        {
            if (!long.TryParse(predicate.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                return false;
            int compare = actualNumber.CompareTo(value);
            return MatchCompare(compare, predicate.Operator);
        }
        if (PerformanceTriageOptions.IsNumericField(predicate.Field))
            return false;

        if (predicate.Field == "Priority")
        {
            int expected = ConfidenceRank(predicate.Value);
            if (expected == 0 && !predicate.Value.Equals("low", StringComparison.OrdinalIgnoreCase))
                return false;
            int compare = Analysis.OptimizationOpportunityRanking
                .Priority(opportunity)
                .CompareTo(
                    (Analysis.OptimizationOpportunityPriority)expected);
            return MatchCompare(compare, predicate.Operator);
        }

        if (predicate.Field == "Confidence")
        {
            int expected = ConfidenceRank(predicate.Value);
            if (expected == 0 && !predicate.Value.Equals("low", StringComparison.OrdinalIgnoreCase))
                return false;
            int compare = ConfidenceRank(opportunity.Confidence).CompareTo(expected);
            return MatchCompare(compare, predicate.Operator);
        }

        if (predicate.Field == "Weight")
        {
            // Non-allocation opportunities have no weight; a weight predicate never
            // matches them (rather than treating null as the "low" rank).
            if (opportunity.Weight is null)
                return false;
            int expected = ConfidenceRank(predicate.Value);
            if (expected == 0 && !predicate.Value.Equals("low", StringComparison.OrdinalIgnoreCase))
                return false;
            int compare = ConfidenceRank(opportunity.Weight).CompareTo(expected);
            return MatchCompare(compare, predicate.Operator);
        }

        if (predicate.Field == "Member")
        {
            var full = FormatMethod(opportunity.Method);
            var shortSignature = ShortMemberSignature(opportunity.Method);
            bool memberMatches = WildcardMatch(full, predicate.Value)
                || WildcardMatch(shortSignature, predicate.Value);
            return predicate.Operator switch
            {
                PerformanceTriageOptions.RowOperator.Equals => memberMatches,
                PerformanceTriageOptions.RowOperator.NotEquals => !memberMatches,
                _ => false,
            };
        }

        if (predicate.Field == "Token"
            && !predicate.Value.Contains('*')
            && !predicate.Value.Contains('?')
            && TryParseMetadataToken(predicate.Value, out int expectedToken))
        {
            bool matches = opportunity.OperandToken == expectedToken;
            return predicate.Operator switch
            {
                PerformanceTriageOptions.RowOperator.Equals => matches,
                PerformanceTriageOptions.RowOperator.NotEquals => !matches,
                _ => false,
            };
        }

        var actual = TriageFieldValue(opportunity, predicate.Field) ?? "";
        bool match = WildcardMatch(actual, predicate.Value);
        return predicate.Operator switch
        {
            PerformanceTriageOptions.RowOperator.Equals => match,
            PerformanceTriageOptions.RowOperator.NotEquals => !match,
            _ => false,
        };
    }

    static bool MatchCompare(int compare, PerformanceTriageOptions.RowOperator op)
        => op switch
        {
            PerformanceTriageOptions.RowOperator.Equals => compare == 0,
            PerformanceTriageOptions.RowOperator.NotEquals => compare != 0,
            PerformanceTriageOptions.RowOperator.GreaterOrEqual => compare >= 0,
            PerformanceTriageOptions.RowOperator.LessOrEqual => compare <= 0,
            _ => false,
        };

    static int CompareTriageField(Analysis.OptimizationOpportunity left, Analysis.OptimizationOpportunity right, string field)
    {
        if (NumericTriageField(left, field) is { } leftNumber
            && NumericTriageField(right, field) is { } rightNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }
        if (field == "Priority")
            return Analysis.OptimizationOpportunityRanking
                .Priority(left)
                .CompareTo(
                    Analysis.OptimizationOpportunityRanking.Priority(
                        right));
        if (field == "Confidence")
            return ConfidenceRank(left.Confidence).CompareTo(ConfidenceRank(right.Confidence));
        if (field == "Weight")
            return WeightSortRank(left.Weight).CompareTo(WeightSortRank(right.Weight));
        if (field == "Loop")
            return IteratesInLoop(left).CompareTo(IteratesInLoop(right));
        if (field == "IL")
            return (left.ILOffset ?? -1).CompareTo(right.ILOffset ?? -1);
        return string.Compare(
            TriageFieldValue(left, field) ?? "",
            TriageFieldValue(right, field) ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    static string? TriageFieldValue(Analysis.OptimizationOpportunity opportunity, string field)
        => field switch
        {
            "Member" => FormatMethod(opportunity.Method),
            "Candidate" => opportunity.CandidateId,
            "Finding" => opportunity.SourceFinding,
            "Provenance" => FormatProvenance(opportunity.Provenance),
            "Shape" => opportunity.Shape,
            "Operation" => opportunity.Operation,
            "Token" => FormatToken(opportunity.OperandToken),
            "EvidenceMethod" => FormatToken(opportunity.EvidenceMethodToken),
            "Evidence" => opportunity.Evidence,
            "Fix" => opportunity.SafeFixDirection,
            "Priority" => TriagePriority(opportunity),
            "Confidence" => opportunity.Confidence,
            "Loop" => IteratesInLoop(opportunity) ? "loop" : "",
            "CallerLoop" => FormatCallerLoop(opportunity.CallerLoop),
            "CallerLoopDepth" => opportunity.CallerLoop?.Depth.ToString(CultureInfo.InvariantCulture),
            "CallerLoopWitness" => FormatCallerLoopWitness(opportunity.CallerLoop),
            "Allocation" => opportunity.RuntimeAllocationType,
            "Path" => opportunity.PathContext,
            "PathConfidence" => opportunity.PathConfidence,
            "PostDominance" => opportunity.PostDominance,
            "Weight" => opportunity.Weight,
            "DirectSites" => opportunity.DirectAllocationSites?.ToString(CultureInfo.InvariantCulture),
            "OncePaths" => opportunity.OnceAllocationPaths?.ToString(CultureInfo.InvariantCulture),
            "ConditionalPaths" => opportunity.ConditionalAllocationPaths?.ToString(CultureInfo.InvariantCulture),
            "RepeatedPaths" => opportunity.RepeatedAllocationPaths?.ToString(CultureInfo.InvariantCulture),
            "UnknownPaths" => opportunity.UnknownAllocationPaths?.ToString(CultureInfo.InvariantCulture),
            "CachedSites" => opportunity.CachedAllocationSites?.ToString(CultureInfo.InvariantCulture),
            "OpaquePaths" => opportunity.OpaqueCallPaths?.ToString(CultureInfo.InvariantCulture),
            "Saturated" => opportunity.AllocationCountSaturated ? "yes" : null,
            "IL" => opportunity.ILOffset is { } offset ? $"IL_{offset:X4}" : null,
            "RootReach" => opportunity.RootReach.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

    static long? NumericTriageField(Analysis.OptimizationOpportunity opportunity, string field)
        => field switch
        {
            "RootReach" => opportunity.RootReach,
            "CallerLoopDepth" => opportunity.CallerLoop?.Depth,
            "DirectSites" => opportunity.DirectAllocationSites,
            "OncePaths" => opportunity.OnceAllocationPaths,
            "ConditionalPaths" => opportunity.ConditionalAllocationPaths,
            "RepeatedPaths" => opportunity.RepeatedAllocationPaths,
            "UnknownPaths" => opportunity.UnknownAllocationPaths,
            "CachedSites" => opportunity.CachedAllocationSites,
            "OpaquePaths" => opportunity.OpaqueCallPaths,
            _ => null,
        };

    static string? FormatToken(int? token)
        => token is { } value ? $"0x{value:X8}" : null;

    internal static string? FormatProvenance(Analysis.PerformanceTriageProvenance provenance)
        => provenance switch
        {
            Analysis.PerformanceTriageProvenance.Exact => "exact",
            Analysis.PerformanceTriageProvenance.Aggregate => "aggregate",
            Analysis.PerformanceTriageProvenance.Unmatched => "unmatched",
            _ => null,
        };

    internal static string? FormatCallerLoop(Analysis.CallerLoopEvidence? evidence)
        => evidence is null ? null : evidence.Depth == 1 ? "direct" : "transitive";

    internal static string? FormatCallerLoopWitness(Analysis.CallerLoopEvidence? evidence)
    {
        if (evidence is null || evidence.Witness.IsDefaultOrEmpty)
            return null;

        var calls = evidence.Witness.Select(step => $"{FormatMethod(step.Caller)} @ IL_{step.ILOffset:X4}");
        return $"{string.Join(" -> ", calls)} -> {FormatMethod(evidence.Witness[^1].Callee)}";
    }

    static bool TryParseMetadataToken(string value, out int token)
    {
        token = default;
        value = value.Trim();
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(
                value.AsSpan(2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out token);
    }

    static string ShortMemberSignature(Analysis.MethodIdentity method)
        => $"{method.Name}({string.Join(", ", method.ParameterTypes.Select(p => p.ToQualifiedDisplayString()))})";

    static bool WildcardMatch(string actual, string pattern)
    {
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return string.Equals(actual, pattern, StringComparison.OrdinalIgnoreCase);

        return WildcardMatch(actual.AsSpan(), pattern.AsSpan());

        static bool WildcardMatch(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern)
        {
            int textIndex = 0;
            int patternIndex = 0;
            int starIndex = -1;
            int matchIndex = 0;
            while (textIndex < text.Length)
            {
                if (patternIndex < pattern.Length
                    && (pattern[patternIndex] == '?' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(text[textIndex])))
                {
                    textIndex++;
                    patternIndex++;
                }
                else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
                {
                    starIndex = patternIndex++;
                    matchIndex = textIndex;
                }
                else if (starIndex >= 0)
                {
                    patternIndex = starIndex + 1;
                    textIndex = ++matchIndex;
                }
                else
                {
                    return false;
                }
            }

            while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
                patternIndex++;
            return patternIndex == pattern.Length;
        }
    }

    private static void ApplyQueryResults(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        InspectionQueryResults results,
        InspectionQueryContext queryContext,
        bool projectOptimizationOpportunities)
    {
        if (results.TryGet(MetadataImageQuery.Definition, out MetadataImageResult? metadata))
        {
            // The path remains presentation-layer state for the legacy on-demand row projector. The
            // query itself consumes an already-open session and returns no filesystem location.
            inspection.MetadataAssemblyPath = path;
            inspection.MetadataImageResult = metadata;
            if (metadata is MetadataImageResult.Failed failed)
            {
                logger.LogWarning(
                    $"Error reading metadata image of {path}: {failed.Error.Message}");
            }
        }

        if (results.TryGet(
                AssemblyReferencesQuery.Definition,
                out AssemblyReferencesResult? references))
        {
            ApplyAssemblyReferencesResult(path, inspection, logger, references);
        }

        if (results.TryGet(
                ClassifiedMethodsQuery.Definition,
                out ClassifiedMethodsResult? classifiedMethods))
        {
            ApplyClassifiedMethodsResult(path, inspection, logger, classifiedMethods);
        }

        if (results.TryGet(
                UnsafeEvidencePresenceQuery.Definition,
                out UnsafeEvidencePresenceResult? unsafeEvidencePresence))
        {
            ApplyUnsafeEvidencePresenceResult(
                inspection,
                unsafeEvidencePresence);
        }

        if (results.TryGet(
                UnsafeEvidenceQuery.Definition,
                out UnsafeEvidenceResult? unsafeEvidence))
        {
            ApplyUnsafeEvidenceResult(path, inspection, logger, unsafeEvidence);
        }

        if (results.TryGet(
                ResourceTriageQuery.Definition,
                out ResourceTriageResult? resourceTriage))
        {
            ApplyResourceTriageResult(
                inspection,
                resourceTriage,
                queryContext.DrillMap);
        }

        if (results.TryGet(
                OptimizationOpportunitiesQuery.Definition,
                out OptimizationOpportunitiesResult? optimizationOpportunities))
        {
            ApplyOptimizationOpportunitiesResult(
                path,
                inspection,
                logger,
                optimizationOpportunities,
                projectOptimizationOpportunities);
        }

        if (results.TryGet(
                BodyShapesQuery.Definition,
                out BodyShapesResult? bodyShapes))
        {
            ApplyBodyShapesResult(inspection, logger, bodyShapes);
        }

        if (results.TryGet(
                TopLeverageQuery.Definition,
                out TopLeverageResult? topLeverage))
        {
            ApplyTopLeverageResult(
                path,
                inspection,
                logger,
                topLeverage,
                queryContext.DrillMap);
        }

        if (results.TryGet(
                AuditMetadataQuery.Definition,
                out AuditMetadataResult? auditMetadata))
        {
            ApplyAuditMetadataResult(path, inspection, logger, auditMetadata);
        }

        if (results.TryGet(
                ExtensionMethodsQuery.Definition,
                out ExtensionMethodsResult? extensionMethods))
        {
            ApplyExtensionMethodsResult(path, inspection, logger, extensionMethods);
        }

        if (results.TryGet(
                CustomAttributesQuery.Definition,
                out CustomAttributesResult? customAttributes))
        {
            ApplyCustomAttributesResult(path, inspection, logger, customAttributes);
        }

        if (results.TryGet(
                ResourcesQuery.Definition,
                out ResourcesResult? resources))
        {
            ApplyResourcesResult(path, inspection, logger, resources);
        }

        if (results.TryGet(
                SwitchesQuery.Definition,
                out SwitchesResult? switches))
        {
            ApplySwitchesResult(path, inspection, logger, switches);
        }

        if (results.TryGet(
                TypeForwardersQuery.Definition,
                out TypeForwardersResult? typeForwarders))
        {
            ApplyTypeForwardersResult(path, inspection, logger, typeForwarders);
        }

        if (results.TryGet(
                UnionTypesQuery.Definition,
                out UnionTypesResult? unionTypes))
        {
            ApplyUnionTypesResult(path, inspection, logger, unionTypes);
        }

        if (results.TryGet(
                SourceLinkDocumentsQuery.Definition,
                out SourceLinkDocumentsResult? sourceDocuments))
        {
            inspection.SourceDocumentInspection = sourceDocuments.Inspection;
        }

        if (results.TryGet(
                SourceAvailabilityQuery.Definition,
                out SourceAvailabilityResult? availability))
        {
            ApplySourceAvailabilityResult(path, inspection, logger, availability);
        }

        if (results.TryGet(
                SourceIntegrityQuery.Definition,
                out SourceIntegrityResult? integrity))
        {
            ApplySourceIntegrityResult(path, inspection, logger, integrity);
        }
    }

    internal static void ApplyAuditMetadataResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        AuditMetadataResult result)
    {
        switch (result)
        {
            case AuditMetadataResult.Available available:
                AuditSignalBuilder.ApplyLibraryAudit(
                    inspection,
                    available.Metadata);
                break;

            case AuditMetadataResult.NoMetadata:
                break;

            case AuditMetadataResult.Failed failed:
                logger.LogWarning(
                    $"Error scanning audit metadata in {path}: {failed.Error.Message}");
                AuditSignalBuilder.ApplyLibraryAudit(inspection, metadata: null);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown audit metadata result '{result.GetType().Name}'.");
        }
    }

    internal static void ApplyTopLeverageResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        TopLeverageResult result,
        Func<IReadOnlyDictionary<int, (string? Stable, string Visibility, string Selector)>>
            getDrillMap)
    {
        ArgumentNullException.ThrowIfNull(getDrillMap);

        inspection.TopLeverageQueryResult = result;
        inspection.TopLeverage = null;
        inspection.TopLeverageDrillMap = null;

        switch (result)
        {
            case TopLeverageResult.Available available:
                var drillByToken = getDrillMap();
                inspection.TopLeverageDrillMap = drillByToken;
                var rows = available.Methods
                    .Select(entry =>
                    {
                        drillByToken.TryGetValue(entry.Method.MetadataToken, out var drill);
                        return new MethodLeverageSummary
                        {
                            Member = ApiOutputFormatter.FormatMethod(entry.Method),
                            Callers = entry.DirectCallerCount,
                            RootReach = entry.RootReach,
                            Fanout = entry.Fanout,
                            Depth = entry.MaxDepth,
                            LoopCalls = entry.LoopCallCount,
                            Generated = IsGeneratedMethod(
                                entry.Method,
                                available.GeneratedFrameworkTypes),
                            Visibility = drill.Visibility,
                            Stable = drill.Stable,
                            Selector = drill.Selector,
                        };
                    })
                    .ToList();
                inspection.TopLeverage = rows.Count > 0 ? rows : null;
                break;

            case TopLeverageResult.NoMetadata:
                break;

            case TopLeverageResult.Failed failed:
                logger.LogWarning(
                    $"Error scanning leverage in {path}: {failed.Error.Message}");
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown top leverage result '{result.GetType().Name}'.");
        }
    }

    internal static void ApplyOptimizationOpportunitiesResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        OptimizationOpportunitiesResult result,
        bool projectCompatibilityRows = true)
    {
        inspection.OptimizationOpportunitiesQueryResult = result;
        inspection.PerformanceTriageOpportunities = [];
        inspection.OptimizationOpportunities = null;

        switch (result)
        {
            case OptimizationOpportunitiesResult.Available available:
                ReportOptimizationDiagnostics(available.Diagnostics);
                ImmutableArray<Analysis.OptimizationOpportunity> opportunities =
                    SelectPerformanceTriageOpportunities(
                        available,
                        inspection.PerformanceTriageOptions);
                inspection.PerformanceTriageOpportunities = opportunities;
                if (projectCompatibilityRows)
                {
                    var rows = opportunities
                        .Select(ProjectOptimizationOpportunity)
                        .ToList();
                    inspection.OptimizationOpportunities =
                        rows.Count > 0 ? rows : null;
                }
                break;

            case OptimizationOpportunitiesResult.NoMetadata:
                break;

            case OptimizationOpportunitiesResult.Failed failed:
                logger.LogWarning(
                    $"Error scanning optimization opportunities in {path}: "
                    + failed.Error.Message);
                break;

            default:
                throw new InvalidOperationException(
                    "Unknown optimization opportunities result "
                    + $"'{result.GetType().Name}'.");
        }
    }

    internal static ImmutableArray<Analysis.OptimizationOpportunity>
        SelectPerformanceTriageOpportunities(
            OptimizationOpportunitiesResult.Available available,
            PerformanceTriageOptions options)
        =>
        [
            .. FilterAndOrderTriageOpportunities(
                available.Opportunities
                    .Concat(available.AllocationFanoutOpportunities)
                    .Where(opportunity => IncludePerformanceOpportunity(
                        opportunity,
                        available.GeneratedFrameworkTypes)),
                options),
        ];

    internal static void ApplyBodyShapesResult(
        LibraryInspection inspection,
        VerboseLogger logger,
        BodyShapesResult result)
    {
        inspection.BodyShapesQueryResult = result;
        inspection.BodyShapeSearchResult = null;

        switch (result)
        {
            case BodyShapesResult.Available available:
                inspection.BodyShapeSearchResult = available.Search;
                if (available.Search.Failures.Count == 0)
                    break;

                if (logger.Enabled)
                {
                    foreach (var failure in available.Search.Failures)
                    {
                        logger.LogWarning(
                            $"Body Shapes skipped {failure.Subject}: {failure.Reason}");
                    }
                }
                else
                {
                    CommandError.WriteWarning(
                        $"Body Shapes skipped {available.Search.Failures.Count} candidates; "
                        + "rerun with --verbose for details.");
                }
                break;

            case BodyShapesResult.NoMetadata:
            case BodyShapesResult.DependencyUnavailable:
                break;

            case BodyShapesResult.Failed failed:
                logger.LogWarning(
                    $"Error searching body shapes: {failed.Error.Message}");
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown Body Shapes result '{result.GetType().Name}'.");
        }
    }

    private static void ApplySourceAvailabilityResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        SourceAvailabilityResult result)
    {
        inspection.SourceAvailabilityQueryResult = result;
        switch (result)
        {
            case SourceAvailabilityResult.Available available:
                inspection.TotalSourceFiles = available.Summary.TotalSourceFiles;
                inspection.AccessibleSourceFiles = available.Summary.AccessibleSourceFiles;
                inspection.EmbeddedSourceFiles = available.Summary.EmbeddedSourceFiles;
                inspection.MissingSourceFiles = available.Summary.MissingSourceFiles.IsEmpty
                    ? null
                    : [.. available.Summary.MissingSourceFiles];
                inspection.AllSourcesAccessible = available.Summary.AllSourcesAccessible;
                break;

            case SourceAvailabilityResult.Absent:
                break;

            case SourceAvailabilityResult.Failed failed:
                logger.LogWarning(
                    $"Error auditing SourceLink availability of {path}: {failed.Reason}");
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown SourceLink availability result '{result.GetType().Name}'.");
        }
    }

    private static void ApplySourceIntegrityResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        SourceIntegrityResult result)
    {
        inspection.SourceIntegrityQueryResult = result;
        switch (result)
        {
            case SourceIntegrityResult.Available available:
                inspection.SourceIntegrityChecked = true;
                inspection.SourceIntegrityVerified = available.Summary.Verified;
                inspection.SourceIntegrityMismatched = available.Summary.Mismatched;
                inspection.SourceIntegrityLineEndingNormalized =
                    available.Summary.LineEndingNormalized;
                inspection.SourceIntegrityUnverifiable = available.Summary.Unverifiable;
                inspection.SourceIntegrityMismatches =
                    available.Summary.MismatchedFiles.IsEmpty
                        ? null
                        : [.. available.Summary.MismatchedFiles];
                break;

            case SourceIntegrityResult.Absent:
                break;

            case SourceIntegrityResult.Failed failed:
                logger.LogWarning(
                    $"Error auditing SourceLink integrity of {path}: {failed.Reason}");
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown SourceLink integrity result '{result.GetType().Name}'.");
        }
    }

    private static void ApplyAssemblyReferencesResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        AssemblyReferencesResult result)
    {
        switch (result)
        {
            case AssemblyReferencesResult.Available available:
                inspection.AssemblyReferenceIdentities = available.Identities;
                inspection.AssemblyReferenceFailureKind = null;
                if (inspection.AssemblyInfo is not null)
                {
                    inspection.AssemblyInfo.References = available.References.IsEmpty
                        ? null
                        : [.. available.References];
                }
                inspection.AssemblyReferenceInspection =
                    MetadataFindings.InspectAssemblyReferences(
                        available.References,
                        FindingSubjectFor(path));
                break;

            case AssemblyReferencesResult.Failed failed:
                logger.LogWarning(
                    $"Error reading assembly references of {path}: {failed.Error.Message}");
                inspection.AssemblyReferenceFailureKind =
                    ClassifyIdentifierConfusionReferenceFailure(
                        failed.Error);
                inspection.AssemblyReferenceInspection =
                    FailedInspection<AssemblyReference>(
                        path,
                        MetadataFindings.AssemblyReferenceDescriptor,
                        failed.Error);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown assembly-reference result '{result.GetType().Name}'.");
        }
    }

    internal static void ApplyExtensionMethodsResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        ExtensionMethodsResult result)
    {
        switch (result)
        {
            case ExtensionMethodsResult.Available available:
                inspection.SetExtensionMemberInspection(
                    MetadataFindings.InspectExtensionMembers(
                        available.Methods,
                        FindingSubjectFor(path)),
                    available.Methods);
                break;

            case ExtensionMethodsResult.Failed failed:
                logger.LogWarning(
                    $"Error scanning extensions in {path}: {failed.Error.Message}");
                inspection.SetExtensionMemberInspection(
                    FailedInspection<ExtensionMemberObservation>(
                        path,
                        MetadataFindings.ExtensionMemberDescriptor,
                        failed.Error),
                    displayOrder: null);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown extension-method result '{result.GetType().Name}'.");
        }
    }

    internal static void ApplyClassifiedMethodsResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        ClassifiedMethodsResult result)
    {
        switch (result)
        {
            case ClassifiedMethodsResult.Available available:
                inspection.ClassifiedMethodInspection =
                    MetadataFindings.InspectClassifiedMethods(
                        available.Methods,
                        FindingSubjectFor(path));

                var unsafeMethods = available.Methods
                    .Where(m => m.Classification == MethodClassification.Unsafe)
                    .Select(m => new ClassifiedMethodSummary
                    {
                        MethodName = m.MethodName,
                        DeclaringType = m.DeclaringType,
                        Signature = m.Signature
                    })
                    .OrderBy(m => m.DeclaringType)
                    .ThenBy(m => m.MethodName)
                    .ToList();

                var pinvokeMethods = available.Methods
                    .Where(m => m.Classification == MethodClassification.PInvoke)
                    .Select(m => new ClassifiedMethodSummary
                    {
                        MethodName = m.MethodName,
                        DeclaringType = m.DeclaringType,
                        Signature = m.Signature,
                        ModuleName = m.ModuleName
                    })
                    .OrderBy(m => m.DeclaringType)
                    .ThenBy(m => m.MethodName)
                    .ToList();

                var asyncMethods = available.Methods
                    .Where(m => m.Classification is MethodClassification.RuntimeAsync
                                                 or MethodClassification.StateMachineAsync)
                    .Select(m => new AsyncMethodSummary
                    {
                        MethodName = m.MethodName,
                        DeclaringType = m.DeclaringType,
                        Signature = m.Signature,
                        Kind = m.Classification == MethodClassification.RuntimeAsync
                            ? AsyncMethodSummary.RuntimeKind
                            : AsyncMethodSummary.StateMachineKind
                    })
                    .OrderBy(m => m.Kind, StringComparer.Ordinal)
                    .ThenBy(m => m.DeclaringType)
                    .ThenBy(m => m.MethodName)
                    .ToList();

                inspection.UnsafeMethods =
                    unsafeMethods.Count > 0 ? unsafeMethods : null;
                inspection.PInvokeMethods =
                    pinvokeMethods.Count > 0 ? pinvokeMethods : null;
                inspection.AsyncMethods =
                    asyncMethods.Count > 0 ? asyncMethods : null;
                break;

            case ClassifiedMethodsResult.Failed failed:
                logger.LogWarning(
                    $"Error scanning classified methods in {path}: {failed.Error.Message}");
                inspection.ClassifiedMethodInspection =
                    FailedInspection<ClassifiedMethodObservation>(
                        path,
                        MetadataFindings.ClassifiedMethodDescriptor,
                        failed.Error);
                inspection.UnsafeMethods = null;
                inspection.PInvokeMethods = null;
                inspection.AsyncMethods = null;
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown classified-methods result '{result.GetType().Name}'.");
        }
    }

    internal static void ApplyUnsafeEvidenceResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        UnsafeEvidenceResult result)
    {
        switch (result)
        {
            case UnsafeEvidenceResult.Available available:
                inspection.UnsafeEvidenceInspection =
                    new FindingInspection<Analysis.UnsafeEvidence>.Complete(
                        [
                            .. available.Evidence
                                .GroupBy(evidence => (
                                    evidence.Member.ModuleVersionId,
                                    evidence.Member.MetadataToken))
                                .SelectMany(group =>
                                    Analysis.AnalysisFindings.InspectUnsafeEvidence(
                                        group,
                                        FindingSubjectFor(path, group.First().Member))),
                        ]);
                inspection.UnsafeEvidenceDiagnostics = available.Diagnostics;
                var rows = available.Evidence
                    .Select(evidence => new UnsafeMemberSummary
                    {
                        Member = FormatMethod(evidence.Member),
                        Reason = evidence.Reason,
                        Detail = evidence.Detail,
                        Kind = evidence.Kind,
                        IL = evidence.ILOffset is { } offset ? $"IL_{offset:X4}" : null,
                        Token = evidence.OperandToken is { } token ? $"0x{token:X8}" : null,
                    })
                    .OrderBy(row => row.Member, StringComparer.Ordinal)
                    .ThenBy(row => row.IL ?? "", StringComparer.Ordinal)
                    .ThenBy(row => row.Reason, StringComparer.Ordinal)
                    .ThenBy(row => row.Detail, StringComparer.Ordinal)
                    .ToList();
                inspection.UnsafeMembers = rows.Count > 0 ? rows : null;

                foreach (var diagnostic in available.Diagnostics)
                {
                    logger.LogWarning(
                        $"unsafe analysis skipped {diagnostic.Method}: {diagnostic.Message}");
                }
                break;

            case UnsafeEvidenceResult.NoMetadata:
                inspection.UnsafeEvidenceDiagnostics = [];
                break;

            case UnsafeEvidenceResult.Failed failed:
                logger.LogWarning(
                    $"Error scanning unsafe members in {path}: {failed.Error.Message}");
                inspection.UnsafeEvidenceInspection =
                    FailedInspection<Analysis.UnsafeEvidence>(
                        path,
                        Analysis.AnalysisFindings.UnsafeEvidenceDescriptor,
                        failed.Error);
                inspection.UnsafeEvidenceDiagnostics = [];
                inspection.UnsafeMembers = null;
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown unsafe-evidence result '{result.GetType().Name}'.");
        }
    }

    internal static void ApplyUnsafeEvidencePresenceResult(
        LibraryInspection inspection,
        UnsafeEvidencePresenceResult result)
    {
        switch (result)
        {
            case UnsafeEvidencePresenceResult.Available available:
                inspection.UnsafeEvidencePresent =
                    available.HasEvidence;
                inspection.UnsafeEvidencePresenceError = null;
                break;

            case UnsafeEvidencePresenceResult.Failed failed:
                inspection.UnsafeEvidencePresent = null;
                inspection.UnsafeEvidencePresenceError =
                    failed.Error;
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown unsafe-evidence-presence result '{result.GetType().Name}'.");
        }
    }

    internal static void ApplyCustomAttributesResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        CustomAttributesResult result)
    {
        switch (result)
        {
            case CustomAttributesResult.Available available:
                inspection.SetAssemblyAttributeInspection(
                    MetadataFindings.InspectAssemblyAttributes(
                        available.Attributes,
                        FindingSubjectFor(path)),
                    available.Attributes);
                break;

            case CustomAttributesResult.Failed failed:
                logger.LogWarning(
                    $"Error scanning custom attributes in {path}: {failed.Error.Message}");
                inspection.SetAssemblyAttributeInspection(
                    FailedInspection<AssemblyAttributeInfo>(
                        path,
                        MetadataFindings.AssemblyAttributeDescriptor,
                        failed.Error),
                    jsonOrder: null);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown custom-attributes result '{result.GetType().Name}'.");
        }
    }

    internal static void ApplyResourcesResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        ResourcesResult result)
    {
        switch (result)
        {
            case ResourcesResult.Available available:
                inspection.ResourceInspection =
                    MetadataFindings.InspectResources(
                        available.Resources,
                        FindingSubjectFor(path));
                break;

            case ResourcesResult.Failed failed:
                logger.LogWarning(
                    $"Error scanning resources in {path}: {failed.Error.Message}");
                inspection.ResourceInspection =
                    FailedInspection<MetadataResource>(
                        path,
                        MetadataFindings.ResourceDescriptor,
                        failed.Error);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown resources result '{result.GetType().Name}'.");
        }
    }

    internal static void ApplyTypeForwardersResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        TypeForwardersResult result)
    {
        switch (result)
        {
            case TypeForwardersResult.Available available:
                inspection.TypeForwarderInspection =
                    MetadataFindings.InspectTypeForwarders(
                        available.Forwarders,
                        FindingSubjectFor(path));
                break;

            case TypeForwardersResult.Failed failed:
                logger.LogWarning(
                    $"Error scanning type forwarders in {path}: {failed.Error.Message}");
                inspection.TypeForwarderInspection =
                    FailedInspection<TypeForwarderInfo>(
                        path,
                        MetadataFindings.TypeForwarderDescriptor,
                        failed.Error);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown type-forwarders result '{result.GetType().Name}'.");
        }
    }

    internal static void ApplyUnionTypesResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        UnionTypesResult result)
    {
        switch (result)
        {
            case UnionTypesResult.Available available:
                inspection.UnionTypeInspection =
                    MetadataFindings.InspectUnionTypes(
                        available.Unions,
                        FindingSubjectFor(path));
                break;

            case UnionTypesResult.Failed failed:
                logger.LogWarning(
                    $"Error scanning union types in {path}: {failed.Error.Message}");
                inspection.UnionTypeInspection =
                    FailedInspection<UnionTypeInfo>(
                        path,
                        MetadataFindings.UnionTypeDescriptor,
                        failed.Error);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown union-types result '{result.GetType().Name}'.");
        }
    }

    internal static void ApplySwitchesResult(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        SwitchesResult result)
    {
        switch (result)
        {
            case SwitchesResult.Available available:
                inspection.SwitchInspection =
                    MetadataFindings.InspectSwitches(
                        available.Switches,
                        FindingSubjectFor(path));
                break;

            case SwitchesResult.Failed failed:
                logger.LogWarning(
                    $"Error scanning switches in {path}: {failed.Error.Message}");
                inspection.SwitchInspection =
                    FailedInspection<SwitchInfo>(
                        path,
                        MetadataFindings.SwitchDescriptor,
                        failed.Error);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown switches result '{result.GetType().Name}'.");
        }
    }

    private static async Task RunTypedQueriesAsync(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        InspectionQueryPlan<InspectionQueryContext> queryPlan,
        InspectionQueryContext queryContext,
        bool projectOptimizationOpportunities,
        Sections.InspectionTrace? trace)
    {
        Action<InspectionQueryDefinition, TimeSpan>? recordQuery = trace is null
            ? null
            : trace.RecordQueryExecution;
        InspectionQueryResults results;
        try
        {
            results = await queryPlan.RunAsync(
                queryContext,
                recordQuery).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InspectionQueryException)
        {
            throw;
        }
        catch (CostDeclarationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InspectionQueryException("Typed query execution failed.", ex);
        }

        ApplyQueryResults(
            path,
            inspection,
            logger,
            results,
            queryContext,
            projectOptimizationOpportunities);
    }

    static FindingInspection<T> FailedInspection<T>(
        string path,
        FindingDescriptor descriptor,
        Exception exception)
        where T : notnull
        => new FindingInspection<T>.Failed(
            new InspectionError(
                FindingSubjectFor(path),
                descriptor,
                exception.Message));

    static FindingInspection<T> FailedInspection<T>(
        string path,
        FindingDescriptor descriptor,
        string reason)
        where T : notnull
        => new FindingInspection<T>.Failed(
            new InspectionError(
                FindingSubjectFor(path),
                descriptor,
                reason));

    internal static void ApplyAssemblyIntegrationsEntry(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        AssemblyIntegrationsEntry entry)
    {
        inspection.AssemblyIntegrationsEntry = entry;
        switch (entry)
        {
            case AssemblyIntegrationsEntry.Available available:
                inspection.EcosystemIntegrationInspection =
                    MetadataFindings.InspectEcosystemIntegrations(
                        available.EcosystemSignals,
                        FindingSubjectFor(path));
                inspection.OpenTelemetryInspection =
                    MetadataFindings.InspectOpenTelemetrySignals(
                        available.OpenTelemetrySignals,
                        FindingSubjectFor(path));
                break;

            case AssemblyIntegrationsEntry.Rejected rejected:
                string acquisitionReason =
                    $"{rejected.Failure.Kind}: {rejected.Failure.Detail}";
                logger.LogWarning(
                    $"Error acquiring integration metadata for {path}: {acquisitionReason}");
                inspection.EcosystemIntegrationInspection =
                    FailedInspection<EcosystemIntegrationSignalInfo>(
                        path,
                        MetadataFindings.EcosystemIntegrationDescriptor,
                        acquisitionReason);
                inspection.OpenTelemetryInspection =
                    FailedInspection<OpenTelemetrySignalInfo>(
                        path,
                        MetadataFindings.OpenTelemetrySignalDescriptor,
                        acquisitionReason);
                break;

            case AssemblyIntegrationsEntry.Failed failed:
                logger.LogWarning(
                    $"Error reading integration metadata of {path}: {failed.Error.Message}");
                MarkIntegrationFailuresIfMissing(path, inspection, failed.Error);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown assembly integrations entry '{entry.GetType().Name}'.");
        }
    }

    internal static void ApplyAssemblyIntegrationOpportunitiesEntry(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger,
        AssemblyIntegrationOpportunitiesEntry entry)
    {
        inspection.AssemblyIntegrationOpportunitiesEntry = entry;
        switch (entry)
        {
            case AssemblyIntegrationOpportunitiesEntry.Available available:
                inspection.IntegrationOpportunities =
                    available.Opportunities.IsDefaultOrEmpty
                        ? null
                        : [.. available.Opportunities];
                break;

            case AssemblyIntegrationOpportunitiesEntry.Rejected rejected:
                logger.LogWarning(
                    $"Error acquiring integration opportunity metadata for {path}: "
                    + $"{rejected.Failure.Kind}: {rejected.Failure.Detail}");
                inspection.IntegrationOpportunities = null;
                break;

            case AssemblyIntegrationOpportunitiesEntry.Failed failed:
                logger.LogWarning(
                    $"Error reading integration opportunity metadata of {path}: "
                    + failed.Error.Message);
                inspection.IntegrationOpportunities = null;
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown assembly integration opportunities entry '{entry.GetType().Name}'.");
        }
    }

    static void MarkIntegrationFailuresIfMissing(
        string path,
        LibraryInspection inspection,
        Exception exception)
    {
        inspection.EcosystemIntegrationInspection ??= FailedInspection<EcosystemIntegrationSignalInfo>(
            path, MetadataFindings.EcosystemIntegrationDescriptor, exception);
        inspection.OpenTelemetryInspection ??= FailedInspection<OpenTelemetrySignalInfo>(
            path, MetadataFindings.OpenTelemetrySignalDescriptor, exception);
    }

    private static FindingSubject FindingSubjectFor(string path)
        => new(Path.GetFullPath(path), Path.GetFileName(path));

    private static FindingSubject FindingSubjectFor(
        string path,
        Analysis.MethodIdentity method)
        => new(
            $"{Path.GetFullPath(path)}|{method.ModuleVersionId:N}:0x{method.MetadataToken:X8}",
            $"{Path.GetFileName(path)}: {FormatMethod(method)}");
}
