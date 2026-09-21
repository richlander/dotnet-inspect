using DotnetInspector.Cache;
using DotnetInspect.Cli.CommandLine;
using DotnetInspector.Ecosystems;
using DotnetInspector.MetadataRendering;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Inspectors;
using ILInspector.Metadata;
using ILInspector.Research;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using SignatureVerificationResult = DotnetInspector.Services.SignatureVerificationResult;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using DotnetInspector.SourceSelection;
using Markout;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using InertText;
using Inspector.Findings;

namespace DotnetInspect.Cli.Commands;

public partial class LibraryCommand
{
    private sealed record LibraryExecutionState(
        LibraryOptions Options,
        LibrarySourceBinding Source,
        InspectionTrace? Trace,
        PackageExtractionResult? PreResolvedPackage,
        InspectionQueryCatalog<InspectionQueryContext> QueryCatalog,
        InspectionQueryCatalog<AssemblyContextGroup> GroupQueryCatalog,
        SectionPipeline<LibraryInspection> Pipeline,
        HashSet<InspectionQueryDefinition> Queries,
        LibraryOptions InspectionOptions,
        HashSet<string>? DiscoveryExecutionScope,
        bool DiscoveryInspection,
        bool FullEffectiveDiscovery,
        bool UseEffectiveDiscoveryCache,
        bool WantsEcosystemDependencies,
        Verbosity UserVerbosity);

    private static async Task<int> ExecuteSourceAsync(
        LibraryExecutionState execution)
    {
        LibraryOptions options = execution.Options;
        LibrarySourceBinding source = execution.Source;
        InspectionTrace? trace = execution.Trace;
        PackageExtractionResult? preResolvedPackage =
            execution.PreResolvedPackage;
        InspectionQueryCatalog<InspectionQueryContext> queryCatalog =
            execution.QueryCatalog;
        InspectionQueryCatalog<AssemblyContextGroup> groupQueryCatalog =
            execution.GroupQueryCatalog;
        SectionPipeline<LibraryInspection> pipeline = execution.Pipeline;
        HashSet<InspectionQueryDefinition> queries = execution.Queries;
        LibraryOptions inspectionOptions = execution.InspectionOptions;
        HashSet<string>? discoveryExecutionScope =
            execution.DiscoveryExecutionScope;
        bool discoveryInspection = execution.DiscoveryInspection;
        bool fullEffectiveDiscovery = execution.FullEffectiveDiscovery;
        bool useEffectiveDiscoveryCache =
            execution.UseEffectiveDiscoveryCache;
        bool wantsEcosystemDependencies =
            execution.WantsEcosystemDependencies;
        Verbosity userVerbosity = execution.UserVerbosity;
        string? assemblyPath = source.AssemblyName;

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        try
        {
            string? packageName = null;
            string? packageVersion = null;

            if (source.Selector
                is SourceSelector.PlatformLibrary platform)
            {
                var (resolvedPath, framework, version, error) = await PlatformResolver.ResolveAssemblyAsync(
                    platform.Name,
                    context.HttpClient,
                    logger.Log,
                    options.PlatformFramework,
                    useRuntimeAssemblies: true,
                    platformVersion: options.PlatformVersion,
                    sourceOptions: options.SourceOptions);

                if (error != null)
                {
                    CommandError.Write($"{error}");
                    return 1;
                }

                logger.Log($"Using platform runtime library: {framework} {version}");

                AssemblyResolutionProvenance inspectionProvenance =
                    AssemblyResolutionProvenance.Platform(
                        framework!,
                        version,
                        "library --platform");
                if (options.CoordinateRequest
                        is LibraryCoordinateRequest.FilePopulation
                    && options.Discover is null)
                {
                    LibraryInspectionSubject? coordinateSubject =
                        SelectInspectionSubjectOrReportFailure(
                            resolvedPath!,
                            inspectionProvenance);
                    if (coordinateSubject is null)
                        return 1;

                    return await WriteILCoordinateBatchAsync(
                        coordinateSubject,
                        null,
                        null,
                        isPlatformAssembly: true,
                        options,
                        context.HttpClient,
                        logger);
                }

                AssemblyContextIntegrationsBatch? integrations =
                    await AssemblyContextIntegrationsRunner.RunIfRequestedAsync(
                        queries,
                        groupQueryCatalog,
                        [
                            new AssemblyContextIntegrationsInput(
                                resolvedPath!,
                                inspectionProvenance),
                        ],
                        trace);
                LibraryInspectionSubject? subject =
                    SelectInspectionSubjectOrReportFailure(
                        resolvedPath!,
                        inspectionProvenance,
                        integrations?.AssemblyForInspection(resolvedPath!));
                if (subject is null)
                    return 1;
                if (CloneCandidatesCommand.IsSelected(
                        options.IncludeSections))
                {
                    return await ExecuteCloneCandidatesAsync(
                        subject,
                        options,
                        rootPackageDirectory: null);
                }

                // Network-free SourceLink availability probe: drives the SourceLink section
                // family in -D and keys the effective cache so a warmed/cleared PDB busts a
                // stale catalog. Skipped (false) outside discovery.
                bool sourceLinkAvailable = fullEffectiveDiscovery
                    && options.CoordinateRequest
                        is not LibraryCoordinateRequest.IlPoint
                    && await ProbeLocalSourceLinkAsync(
                        subject,
                        context.HttpClient,
                        logger,
                        isPlatformAssembly: true,
                        sourceOptions: options.SourceOptions);

                // Identity of the bytes about to be inspected. Computed once and reused for the
                // lookup, the pre-inspection snapshot, and (via CacheEffective) the write, so a
                // discovery run hashes the assembly at most twice.
                string? inspectedContentHash = fullEffectiveDiscovery ? TryGetContentHash(resolvedPath!) : null;

                // Check effective sections cache before running full inspection
                if (useEffectiveDiscoveryCache && inspectedContentHash != null)
                {
                    var cached = TryGetCachedEffective(resolvedPath!, inspectedContentHash, sourceLinkAvailable);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(resolvedPath!);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, pipeline, userVerbosity, rootLabel);
                    }
                }

                InspectionQueryPlan<InspectionQueryContext> queryPlan =
                    queryCatalog.Plan(queries);
                var inspection = await LibraryMetadataService.InspectAsync(
                    resolvedPath!, inspectionOptions, logger, null, null, context.HttpClient,
                    isPlatformAssembly: true,
                    queryPlan: queryPlan,
                    assemblyReference: subject.AssemblyReference,
                    integrationsEntry: integrations?.EntryFor(resolvedPath!),
                    integrationOpportunitiesEntry:
                        integrations?.OpportunitiesEntryFor(resolvedPath!),
                    discoveryOnly: discoveryInspection && !fullEffectiveDiscovery, trace: trace);
                if (inspection == null)
                {
                    CommandError.Write($"Could not read library: {resolvedPath}");
                    return 1;
                }

                inspection.Source = SourceKind.Platform;
                inspection.PlatformVersion = version;
                ApplyLibraryEcosystemDependencies(
                    inspection,
                    subject,
                    wantsEcosystemDependencies,
                    RequiresLibraryEcosystemDiagnosticDisclosure(options),
                    logger);
                if (!discoveryInspection)
                {
                    await PopulateReferenceHierarchyAsync(
                        inspection,
                        resolvedPath!,
                        options,
                        context);
                }
                if (RejectFailedExactIdentifierAudit(
                        inspection,
                        options))
                {
                    return 1;
                }
                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspection, subject, null, null, isPlatformAssembly: true,
                    options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                int heapExitCode = PopulateMetadataHeapIfRequested(inspection, options, logger);
                if (heapExitCode != 0)
                    return heapExitCode;
                if (discoveryInspection)
                    return WriteEffectiveSections(
                        resolvedPath!, inspection, options, pipeline, userVerbosity,
                        fullEffectiveDiscovery, discoveryExecutionScope, sourceLinkAvailable,
                        cache: useEffectiveDiscoveryCache,
                        inspectedContentHash: inspectedContentHash);
                if (!TrySelectAssemblyReferences(inspection, options.ReferenceRowSelection))
                    return 1;
                if (!TrySelectLibraryEcosystemDependencies(
                        inspection,
                        options.EcosystemDependencyRowSelection))
                {
                    return 1;
                }
                if (options.Print)
                    return await WriteLibraryPrintProjectionAsync(inspection, options);
                if (options.Value || options.Urls || options.Paths)
                    return WriteLibraryShapeProjection(inspection, options);
                if (RejectEmptyExactSection(inspection, options, pipeline))
                    return 1;
                WarnEmptySections(inspection, options, pipeline);
                ExtractResourcesIfRequested(resolvedPath!, options);
                if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
                    return 1;
                if (RejectInexactReferenceHierarchyCount(
                        options,
                        inspection))
                {
                    return 1;
                }

                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                return Math.Max(
                    IntegrityExitCode(inspection),
                    SelectedInspectionFailureExitCode(
                        options,
                        pipeline,
                        inspection));
            }
            else if (source.Selector
                is SourceSelector.PackageSource)
            {
                // Extract from package
                var extractResult = await ExtractFromPackageAsync(
                    assemblyPath,
                    source.PackageTarget!,
                    options.Tfm,
                    options.SourceOptions,
                    options.IncludePrerelease,
                    logger,
                    context.HttpClient,
                    preResolvedPackage);
                if (extractResult == null)
                {
                    return 1;
                }

                var (assemblyPaths, extractPath, extractTempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion) = extractResult.Value;
                tempDir = extractTempDir;
                packageName = resolvedPackageName;
                packageVersion = resolvedPackageVersion;

                if (options.CoordinateRequest
                        is LibraryCoordinateRequest.FilePopulation
                    && options.Discover is null)
                {
                    LibraryInspectionSubject? coordinateSubject =
                        SelectInspectionSubjectOrReportFailure(
                            assemblyPaths[0],
                            PackageIntegrationProvenance(
                                assemblyPaths[0],
                                extractPath,
                                packageName,
                                packageVersion));
                    if (coordinateSubject is null)
                        return 1;

                    return await WriteILCoordinateBatchAsync(
                        coordinateSubject,
                        packageName,
                        packageVersion,
                        isPlatformAssembly: false,
                        options,
                        context.HttpClient,
                        logger);
                }

                var inspectionPaths = discoveryInspection && assemblyPaths.Count > 0
                    ? [assemblyPaths[0]]
                    : assemblyPaths;
                AssemblyContextIntegrationsBatch? integrations =
                    await AssemblyContextIntegrationsRunner.RunIfRequestedAsync(
                        queries,
                        groupQueryCatalog,
                        inspectionPaths.Select(path =>
                            new AssemblyContextIntegrationsInput(
                                path,
                                PackageIntegrationProvenance(
                                    path,
                                    extractPath,
                                    packageName,
                                    packageVersion))),
                        trace);
                List<LibraryInspectionSubjectSelection> subjectSelections =
                    inspectionPaths.Select(path =>
                        LibraryInspectionSubject.Select(
                            path,
                            PackageIntegrationProvenance(
                                path,
                                extractPath,
                                packageName,
                                packageVersion),
                            integrations?.AssemblyForInspection(
                                path)))
                    .ToList();
                LibraryInspectionSubjectSelection.Ready? primaryReady =
                    subjectSelections
                        .OfType<LibraryInspectionSubjectSelection.Ready>()
                        .FirstOrDefault();
                if (CloneCandidatesCommand.IsSelected(
                        options.IncludeSections))
                {
                    if (subjectSelections.Count != 1
                        || primaryReady is null)
                    {
                        CommandError.Write(
                            $"Section '{SectionNames.CloneCandidates}' requires one exact library. "
                            + "Name the assembly within the package.");
                        return 1;
                    }

                    return await ExecuteCloneCandidatesAsync(
                        primaryReady.Subject,
                        options,
                        extractPath);
                }

                // Network-free SourceLink availability probe (see platform branch).
                bool sourceLinkAvailable = fullEffectiveDiscovery
                    && primaryReady is not null
                    && options.CoordinateRequest
                        is not LibraryCoordinateRequest.IlPoint
                    && await ProbeLocalSourceLinkAsync(
                        primaryReady.Subject,
                        context.HttpClient,
                        logger,
                        isPlatformAssembly: false,
                        packageName: packageName,
                        packageVersion: packageVersion,
                        sourceOptions: options.SourceOptions);

                // Identity of the bytes about to be inspected; see the platform path above.
                string? inspectedContentHash =
                    fullEffectiveDiscovery && primaryReady is not null
                    ? TryGetContentHash(primaryReady.Subject.Path)
                    : null;

                // Check effective sections cache before running full inspection
                if (useEffectiveDiscoveryCache
                    && inspectedContentHash != null
                    && primaryReady is not null)
                {
                    var cached = TryGetCachedEffective(
                        primaryReady.Subject.Path,
                        inspectedContentHash,
                        sourceLinkAvailable);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(
                            primaryReady.Subject.Path);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, pipeline, userVerbosity, rootLabel);
                    }
                }

                // Verify package signature if nupkg is available
                SignatureVerificationResult? signatureResult = null;
                if (nupkgPath != null && !discoveryInspection)
                {
                    logger.Log($"Verifying package signature: {Path.GetFileName(nupkgPath)}");
                    signatureResult = await SignatureVerifier.VerifyAsync(nupkgPath);
                }

                // Inspect all assemblies
                InspectionQueryPlan<InspectionQueryContext> queryPlan =
                    queryCatalog.Plan(queries);
                PackageInspectionCollection collection =
                    await CollectPackageInspectionsAsync(
                    inspectionPaths, inspectionOptions, logger, packageName, packageVersion,
                    extractPath, context.HttpClient, signatureResult,
                    queryPlan, integrations,
                    discoveryInspection && !fullEffectiveDiscovery, trace,
                    subjectSelections);
                List<LibraryInspection> inspections =
                    collection.Inspections;
                int descriptorSelectionExitCode =
                    collection.DescriptorSelectionFailures.Count > 0 ? 1 : 0;

                if (inspections.Count == 0)
                {
                    PackageCommand.WriteIdentifierAuditFailures(
                        collection.IdentifierAuditFailures);
                    CommandError.Write("No libraries could be read from the package.");
                    return 1;
                }

                foreach (var insp in inspections)
                    insp.Source = SourceKind.NuGet;
                if (wantsEcosystemDependencies)
                {
                    for (int index = 0;
                         index < inspections.Count;
                         index++)
                    {
                        ApplyLibraryEcosystemDependencies(
                            inspections[index],
                            collection.Subjects[index],
                            wantsEcosystemDependencies: true,
                            RequiresLibraryEcosystemDiagnosticDisclosure(
                                options),
                            logger);
                    }
                }
                if (!discoveryInspection)
                {
                    for (int index = 0;
                         index < inspections.Count;
                         index++)
                    {
                        await PopulateReferenceHierarchyAsync(
                            inspections[index],
                            collection.Subjects[index].Path,
                            options,
                            context);
                    }
                }
                if (inspections.Count == 1
                    && RejectFailedExactIdentifierAudit(
                        inspections[0],
                        options))
                {
                    return 1;
                }
                bool identifierAuditIncomplete =
                    PackageCommand.WriteIdentifierAuditFailures(
                        collection.IdentifierAuditFailures);
                int identifierAuditExitCode =
                    identifierAuditIncomplete ? 1 : 0;

                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspections[0],
                    collection.Subjects[0],
                    packageName, packageVersion, isPlatformAssembly: false,
                    options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                int heapExitCode = PopulateMetadataHeapIfRequested(inspections[0], options, logger);
                if (heapExitCode != 0)
                    return heapExitCode;
                if (discoveryInspection)
                    return Math.Max(
                        Math.Max(
                            identifierAuditExitCode,
                            descriptorSelectionExitCode),
                        WriteEffectiveSections(
                            collection.Subjects[0].Path,
                            inspections[0], options,
                            pipeline, userVerbosity,
                            fullEffectiveDiscovery,
                            discoveryExecutionScope,
                            sourceLinkAvailable,
                            cache: useEffectiveDiscoveryCache,
                            inspectedContentHash:
                                inspectedContentHash,
                            reportIdentifierFailures:
                                !identifierAuditIncomplete));
                if (inspections.Count == 1
                    && !TrySelectAssemblyReferences(
                        inspections[0],
                        options.ReferenceRowSelection))
                {
                    return 1;
                }
                if (inspections.Count == 1
                    && !TrySelectLibraryEcosystemDependencies(
                        inspections[0],
                        options.EcosystemDependencyRowSelection))
                {
                    return 1;
                }
                if (options.Print)
                    return IntegrityExitCode(
                        Math.Max(
                            Math.Max(
                                identifierAuditExitCode,
                                descriptorSelectionExitCode),
                            await WriteLibraryPrintProjectionAsync(
                                inspections[0],
                                options)),
                        !identifierAuditIncomplete,
                        inspections[0]);
                if (options.Value || options.Urls || options.Paths)
                    return IntegrityExitCode(
                        Math.Max(
                            Math.Max(
                                identifierAuditExitCode,
                                descriptorSelectionExitCode),
                            WriteLibraryShapeProjection(
                                inspections[0],
                                options)),
                        !identifierAuditIncomplete,
                        inspections[0]);
                if (RejectEmptyExactSection(inspections, options, pipeline))
                    return 1;
                WarnEmptySections(inspections, options, pipeline);
                if (collection.Subjects.Count > 0)
                    ExtractResourcesIfRequested(
                        collection.Subjects[0].Path,
                        options);

                if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
                    return 1;
                if (RejectInexactReferenceHierarchyCount(
                        options,
                        [.. inspections]))
                {
                    return 1;
                }

                if (inspections.Count == 1 && !IsAllTfmPackageSelection(options))
                    OutputFormatter.WriteLibraryResult(inspections[0], options, pipeline);
                else
                {
                    if (RejectMultiAssemblyMetadataSelection(inspections, options))
                        return 1;
                    OutputFormatter.WriteLibraryResults(inspections, options, pipeline);
                }

                return Math.Max(
                    IntegrityExitCode(
                        Math.Max(
                            identifierAuditExitCode,
                            descriptorSelectionExitCode),
                        !identifierAuditIncomplete,
                        [.. inspections]),
                    SelectedInspectionFailureExitCode(
                        options,
                        pipeline,
                        [.. inspections]));
            }
            else
            {
                // Load from filesystem
                if (!File.Exists(assemblyPath))
                {
                    CommandError.Write($"File not found: {assemblyPath}");
                    return 1;
                }

                AssemblyResolutionProvenance inspectionProvenance =
                    AssemblyResolutionProvenance.Local("library path");
                if (options.CoordinateRequest
                        is LibraryCoordinateRequest.FilePopulation
                    && options.Discover is null)
                {
                    LibraryInspectionSubject? coordinateSubject =
                        SelectInspectionSubjectOrReportFailure(
                            assemblyPath!,
                            inspectionProvenance);
                    if (coordinateSubject is null)
                        return 1;

                    return await WriteILCoordinateBatchAsync(
                        coordinateSubject,
                        null,
                        null,
                        isPlatformAssembly: false,
                        options,
                        context.HttpClient,
                        logger);
                }

                AssemblyContextIntegrationsBatch? integrations =
                    await AssemblyContextIntegrationsRunner.RunIfRequestedAsync(
                        queries,
                        groupQueryCatalog,
                        [
                            new AssemblyContextIntegrationsInput(
                                assemblyPath!,
                                inspectionProvenance),
                        ],
                        trace);
                LibraryInspectionSubject? subject =
                    SelectInspectionSubjectOrReportFailure(
                        assemblyPath!,
                        inspectionProvenance,
                        integrations?.AssemblyForInspection(assemblyPath!));
                if (subject is null)
                    return 1;
                if (CloneCandidatesCommand.IsSelected(
                        options.IncludeSections))
                {
                    return await ExecuteCloneCandidatesAsync(
                        subject,
                        options,
                        rootPackageDirectory: null);
                }

                // Network-free SourceLink availability probe (see platform branch).
                bool sourceLinkAvailable = fullEffectiveDiscovery
                    && options.CoordinateRequest
                        is not LibraryCoordinateRequest.IlPoint
                    && await ProbeLocalSourceLinkAsync(
                        subject,
                        context.HttpClient,
                        logger,
                        isPlatformAssembly: false,
                        sourceOptions: options.SourceOptions);

                // Identity of the bytes about to be inspected; see the platform path above.
                string? inspectedContentHash = fullEffectiveDiscovery ? TryGetContentHash(assemblyPath!) : null;

                // Check effective sections cache before running full inspection
                if (useEffectiveDiscoveryCache && inspectedContentHash != null)
                {
                    var cached = TryGetCachedEffective(assemblyPath!, inspectedContentHash, sourceLinkAvailable);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(assemblyPath!);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, pipeline, userVerbosity, rootLabel);
                    }
                }

                InspectionQueryPlan<InspectionQueryContext> queryPlan =
                    queryCatalog.Plan(queries);
                var inspection = await LibraryMetadataService.InspectAsync(
                    assemblyPath!, inspectionOptions, logger, null, null, context.HttpClient,
                    queryPlan: queryPlan,
                    assemblyReference: subject.AssemblyReference,
                    integrationsEntry: integrations?.EntryFor(assemblyPath!),
                    integrationOpportunitiesEntry:
                        integrations?.OpportunitiesEntryFor(assemblyPath!),
                    discoveryOnly: discoveryInspection && !fullEffectiveDiscovery, trace: trace);
                if (inspection == null)
                {
                    CommandError.Write($"Could not read library: {assemblyPath}");
                    return 1;
                }

                inspection.Source = SourceKind.File;
                ApplyLibraryEcosystemDependencies(
                    inspection,
                    subject,
                    wantsEcosystemDependencies,
                    RequiresLibraryEcosystemDiagnosticDisclosure(options),
                    logger);
                if (!discoveryInspection)
                {
                    await PopulateReferenceHierarchyAsync(
                        inspection,
                        assemblyPath!,
                        options,
                        context);
                }
                if (RejectFailedExactIdentifierAudit(
                        inspection,
                        options))
                {
                    return 1;
                }
                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspection, subject, null, null, isPlatformAssembly: false,
                    options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                int heapExitCode = PopulateMetadataHeapIfRequested(inspection, options, logger);
                if (heapExitCode != 0)
                    return heapExitCode;
                if (discoveryInspection)
                    return WriteEffectiveSections(
                        assemblyPath!, inspection, options, pipeline, userVerbosity,
                        fullEffectiveDiscovery, discoveryExecutionScope, sourceLinkAvailable,
                        cache: useEffectiveDiscoveryCache,
                        inspectedContentHash: inspectedContentHash);
                if (!TrySelectAssemblyReferences(inspection, options.ReferenceRowSelection))
                    return 1;
                if (!TrySelectLibraryEcosystemDependencies(
                        inspection,
                        options.EcosystemDependencyRowSelection))
                {
                    return 1;
                }
                if (options.Print)
                    return await WriteLibraryPrintProjectionAsync(inspection, options);
                if (options.Value || options.Urls || options.Paths)
                    return WriteLibraryShapeProjection(inspection, options);
                if (RejectEmptyExactSection(inspection, options, pipeline))
                    return 1;
                WarnEmptySections(inspection, options, pipeline);
                ExtractResourcesIfRequested(assemblyPath!, options);
                if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
                    return 1;
                if (RejectInexactReferenceHierarchyCount(
                        options,
                        inspection))
                {
                    return 1;
                }

                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                return Math.Max(
                    IntegrityExitCode(inspection),
                    SelectedInspectionFailureExitCode(
                        options,
                        pipeline,
                        inspection));
            }
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        finally
        {
            // Cleanup temp directory if we extracted from a package
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}