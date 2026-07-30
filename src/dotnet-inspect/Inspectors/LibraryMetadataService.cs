using DotnetInspector.Core;
using DotnetInspector.Models;
using System.Globalization;
using System.Reflection;
using System.Text;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using ILInspector.Research;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
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
    /// <summary>
    /// The scanner keys the shared metadata read responds to. Requesting any of them makes the
    /// read extract assembly references (see the <c>needsReferences</c> use in
    /// <see cref="InspectAsync"/>), which is the only data this read produces on a section's
    /// behalf.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This set is the single source of truth for what the read consults. An earlier revision
    /// maintained a second, free-standing literal listing the keys the read "satisfies", and a key
    /// added to that literal and to a section — but not here — passed the gate while nothing
    /// collected it. That is the silence #3453 found, reintroduced one level up. Anything the gate
    /// needs is therefore <em>derived</em> from this set rather than restated alongside it.
    /// </para>
    /// <para>
    /// <c>ScannerTransitiveRefs</c> is listed because the transitive scan consumes the reference
    /// list this read extracts; a registered scanner still performs the transitive walk itself.
    /// </para>
    /// </remarks>
    internal static readonly HashSet<string> ReferenceReadingScannerKeys =
    [
        LibrarySections.ScannerReferences,
        LibrarySections.ScannerTransitiveRefs,
        LibrarySections.ScannerAuditSignals
    ];

    /// <summary>
    /// Whether the shared metadata read extracts assembly references. The References, Dependencies
    /// and Audit sections all consume them, and they are extracted during that read rather than by
    /// a registered scanner, so their keys have to be consulted before it runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parameters are the whole contract: the requested scanner keys, and the two flags by
    /// which a user explicitly asks for references. Nothing else about the request participates.
    /// </para>
    /// <para>
    /// It takes flags rather than the options object on purpose. Every structural gate in
    /// <see cref="SectionPipelineTests"/> quantifies over the scanner <em>sets</em>, holding the
    /// rest of the request at one shape, so a condition here that consulted anything else was
    /// invisible to all of them: adding <c>|| !string.IsNullOrWhiteSpace(options.AssemblyName)</c>
    /// made unrelated sections serialize references for real users while the whole suite stayed
    /// green, because the gate never set a name and the CLI always does. A function that cannot
    /// reach the rest of the request cannot grow that dependency without changing this signature.
    /// </para>
    /// </remarks>
    internal static bool ReadsAssemblyReferences(
        IReadOnlyCollection<string>? scanners,
        bool includeReferences,
        bool includeDependencies) =>
        includeReferences
        || includeDependencies
        || scanners?.Any(ReferenceReadingScannerKeys.Contains) == true;

    /// <summary>
    /// The keys in <see cref="ReferenceReadingScannerKeys"/> that the read only <em>feeds</em>:
    /// it extracts their input, but a registered scanner still does the work the key names.
    /// </summary>
    /// <remarks>
    /// These are excluded from <see cref="SharedReadScannerKeys"/> because a section declaring one
    /// is not satisfied by the read alone. Counting them as satisfied made the registration gate
    /// blind in one direction: <c>ScannerTransitiveRefs</c> reached the gate's union from both the
    /// registry and the read, so <em>deleting</em> its registration left the union unchanged and
    /// the gate still passed while <c>Dependencies</c> silently stopped producing its tree. An
    /// earlier revision argued the two sets were interchangeable "because the union is identical
    /// either way" — that identity was the defect, not the justification.
    /// <see cref="SectionPipelineTests"/> pins the property that makes the exclusion safe: every
    /// declared key the read does not satisfy must have a registered scanner, and this set names
    /// only keys the read actually reads — a member outside
    /// <see cref="ReferenceReadingScannerKeys"/> would be a silent no-op, since the exclusion
    /// filters that set.
    /// </remarks>
    internal static readonly IReadOnlySet<string> KeysTheReadOnlyFeeds =
        new HashSet<string>(StringComparer.Ordinal)
        {
            LibrarySections.ScannerTransitiveRefs,
            LibrarySections.ScannerAuditSignals
        };

    /// <summary>
    /// The keys the shared metadata read fully honors, unioned with the registry's registered keys
    /// to form the set a section is allowed to declare. Derived from
    /// <see cref="ReferenceReadingScannerKeys"/> so it cannot claim a key the read ignores.
    /// </summary>
    internal static IReadOnlySet<string> SharedReadScannerKeys { get; } =
        ReferenceReadingScannerKeys
            .Where(k => !KeysTheReadOnlyFeeds.Contains(k))
            .ToHashSet(StringComparer.Ordinal);

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
        HashSet<string>? scanners = null,
        ScannerRegistry? scannerRegistry = null,
        bool discoveryOnly = false)
    {
        logger.Log($"Inspecting: {Path.GetFileName(path)}");

        try
        {
            var bodyAnalysisFeatures = scanners is null
                ? Analysis.LibraryBodyAnalysisFeatures.None
                : SelectBodyAnalysisFeatures(scanners);
            using var service = bodyAnalysisFeatures
                == Analysis.LibraryBodyAnalysisFeatures.None
                    ? SourceLinkService.Open(path, logger.Log)
                    : SourceLinkService.OpenPrefetched(path, logger.Log);
            var pdbContext = service.Context;

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

                return nativeAudit;
            }

            var needsAuditSignals = scanners?.Contains(LibrarySections.ScannerAuditSignals) == true;

            var inspection = new LibraryInspection
            {
                FileName = Path.GetFileName(path),
                FileType = "dll",
                IsFacadeAssembly = isPlatformAssembly ? PlatformResolver.IsFacadeOnlyAssembly(path) : null,
                UseDependenciesView = options.IncludeDependencies,
                PerformanceTriageOptions = options.PerformanceTriage
            };

            inspection.AssemblyInfo = pdbContext.ExtractAssemblyInfo(
                ReadsAssemblyReferences(scanners, options.IncludeReferences, options.IncludeDependencies));
            if (inspection.AssemblyInfo?.References is { } references)
            {
                inspection.AssemblyReferenceInspection = MetadataFindings.InspectAssemblyReferences(
                    references,
                    FindingSubjectFor(path));
            }

            // Populate cheap presence flags for fast -s discovery
            var presenceFlags = pdbContext.ScanPresenceFlags();
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
            HashSet<SwitchInfo> appContextSwitches = [];
            AddAppContextSwitches(
                appContextSwitches,
                AppContextSwitchProjectionProducer.Produce(pdbContext.MethodBodies));
            inspection.SwitchCount = presenceFlags.SwitchCount + appContextSwitches.Count;
            inspection.HasSwitches = inspection.SwitchCount > 0;

            // PE debug directory fields
            inspection.HasReproducibleFlag = pdbContext.HasReproducibleFlag;
            inspection.HasEmbeddedPdb = pdbContext.HasEmbeddedPdb;
            inspection.PdbPath = pdbContext.CodeViewPdbPath;
            inspection.HasNormalizedPaths = pdbContext.HasNormalizedPaths;
            inspection.NonNormalizedPaths = pdbContext.NonNormalizedPaths;
            inspection.IsDeterministic = pdbContext.HasReproducibleFlag && pdbContext.HasNormalizedPaths != false;

            // Build transitive reference tree if requested. The Dependencies section requests the
            // same work through the ScannerTransitiveRefs scanner, which runs below.
            if (options.IncludeDependencies)
                ScanTransitiveReferences(path, inspection, logger);

            // Run registered scanners for the requested sections
            if (scannerRegistry != null && scanners != null)
            {
                scannerRegistry.RunScanners(scanners, new Sections.ScannerContext
                {
                    AssemblyPath = path,
                    Model = inspection,
                    Logger = logger,
                    MetadataContext = pdbContext,
                    BodyAnalysisFeatures = bodyAnalysisFeatures,
                });
            }
            else if (options.Verbosity == Options.Verbosity.Detailed)
            {
                // Fallback for non-pipeline callers — open the assembly once for all five scans.
                try
                {
                    using var session = AssemblyInspectionSession.Open(path);
                    ScanExtensionMembers(session, path, inspection, logger);
                    ScanClassifiedMethods(session, path, inspection, logger);
                    inspection.ResourceInspection = ScanResources(session, path, logger);
                    ScanCustomAttributes(session, path, inspection, logger);
                    inspection.UnionTypeInspection = ScanUnionTypes(session, path, logger);
                    ScanTypeForwarders(session, path, inspection, logger);
                }
                catch (Exception ex)
                {
                    logger.Log($"Warning: Error opening {path} for scanning: {ex.Message}");
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

            inspection.FileSize = pdbContext.FileSize;
            inspection.LastModified = pdbContext.LastWriteTimeUtc;

            // Effective-section discovery (-D) must be network-free regardless of
            // verbosity or -S filters. The SourceLink family is listed from the
            // network-free ProbeLocalSourceLinkAsync gate, so a discovery inspection
            // runs no network-capable source stage: no PDB download, no source-URL
            // HEAD audit, no integrity GET, and no source-file collection. (For an
            // embedded/adjacent PDB the local audit stages would otherwise fire.)
            // This keeps -D listings verbosity-independent and keeps the effective
            // cache token (probe-driven) consistent with what the inspection records
            // for HasSourceLink.
            var sourcePlan = discoveryOnly
                ? default
                : LibrarySourcePlans.For(
                    options.UserVerbosity,
                    options.IncludeSections);

            await AuditAsync(
                service,
                inspection,
                path,
                packageName,
                packageVersion,
                logger,
                httpClient,
                isPlatformAssembly,
                allowPdbDownload: sourcePlan.AllowPdbDownload,
                readCachedPdb: sourcePlan.ReadCachedPdb);

            var sourceSubject = FindingSubjectFor(path);
            inspection.SourceDocumentInspection = MetadataFindings.InspectSourceDocuments(
                service,
                sourceSubject);
            inspection.CompilationOptionInspection = MetadataFindings.InspectCompilationOptions(
                service,
                sourceSubject);
            inspection.CompilationReferenceInspection = MetadataFindings.InspectCompilationReferences(
                service,
                sourceSubject);

            if (needsAuditSignals)
                AuditSignalBuilder.PopulateLibraryAudit(path, inspection, logger);

            if (sourcePlan.RunHeadAudit && service.HasSourceLink && pdbContext.HasPdb)
            {
                // SourceLink URLs are untrusted: probe them with the SSRF-hardened client, not the
                // shared client used for trusted NuGet/symbol endpoints.
                await SourceAuditService.PopulateAsync(
                    inspection.SourceDocumentInspection,
                    inspection,
                    DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch,
                    logger);
                if (needsAuditSignals)
                    AuditSignalBuilder.PopulateLibraryAudit(path, inspection, logger);
            }

            if (sourcePlan.RunIntegrity && service.HasSourceLink && pdbContext.HasPdb)
            {
                await SourceIntegrityService.PopulateAsync(
                    inspection.SourceDocumentInspection,
                    inspection,
                    logger);
                if (needsAuditSignals)
                    AuditSignalBuilder.PopulateLibraryAudit(path, inspection, logger);
            }

            if (sourcePlan.CollectSourceFiles)
            {
                inspection.SourceFiles = await SourceFileCollector.CollectAsync(
                    service,
                    path,
                    logger,
                    httpClient,
                    browsableUrls: options.BrowsableUrls,
                    typeFilter: options.TypeFilter);
            }

            return inspection;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Failed to inspect {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    static Analysis.LibraryBodyAnalysisFeatures SelectBodyAnalysisFeatures(
        IReadOnlySet<string> scanners)
    {
        var features = Analysis.LibraryBodyAnalysisFeatures.None;
        if (scanners.Contains(Sections.LibrarySections.ScannerUnsafeMembers)
            || scanners.Contains(Sections.LibrarySections.ScannerTopLeverage))
        {
            features |= Analysis.LibraryBodyAnalysisFeatures.MethodEvidence;
        }
        if (scanners.Contains(
            Sections.LibrarySections.ScannerOptimizationOpportunities))
        {
            features |=
                Analysis.LibraryBodyAnalysisFeatures.OptimizationOpportunities;
        }
        if (scanners.Contains(Sections.LibrarySections.ScannerResourceTriage))
            features |= Analysis.LibraryBodyAnalysisFeatures.LeakTriage;
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
        bool readCachedPdb = false)
    {
        var pdbContext = service.Context;

        if (pdbContext.HasPdb)
        {
            inspection.PdbFormat = pdbContext.PdbFormat;
            inspection.PdbLocation = pdbContext.PdbLocation;
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
            await SourceEnricher.AcquirePdbAsync(pdbContext, httpClient, packageName, packageVersion, isPlatformAssembly, logger.Log);

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
            await SourceEnricher.AcquirePdbAsync(
                pdbContext, httpClient, packageName, packageVersion,
                isPlatformAssembly, logger.Log, cacheOnly: true);

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
            if (inspection.WindowsPdbDetected)
            {
                inspection.SourceLinkUnavailableReason = "Windows PDB";
            }
            else if (!pdbContext.HasPdb && !allowPdbDownload && inspection.PdbPath != null)
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

        inspection.Builder = InferBuilder(inspection);
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
        string? packageVersion = null)
    {
        try
        {
            using var service = SourceLinkService.Open(assemblyPath, logger.Log);
            var context = service.Context;

            if (!context.HasPdb && !context.WindowsPdbDetected && context.NeedsPdb)
            {
                await SourceEnricher.AcquirePdbAsync(
                    context, httpClient, packageName, packageVersion,
                    isPlatformAssembly, logger.Log, cacheOnly: true);
            }

            return context.HasPdb && service.HasSourceLink;
        }
        catch (Exception ex)
        {
            logger.Log($"SourceLink discovery probe failed for {assemblyPath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Infers who built the assembly based on symbol availability and SourceLink.
    /// </summary>
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

        if (inspection.HasSourceLink && inspection.SourceLinkJson != null)
        {
            if (inspection.SourceLinkJson.Contains("github.com/dotnet/", StringComparison.OrdinalIgnoreCase) ||
                inspection.SourceLinkJson.Contains("raw.githubusercontent.com/dotnet/", StringComparison.OrdinalIgnoreCase))
            {
                return "Microsoft";
            }
        }

        if (inspection.SourceLinkUnavailableReason == "no symbols")
        {
            return "Unknown";
        }

        return null;
    }

    /// <summary>
    /// Populates <see cref="AssemblyInfo.TransitiveReferences"/> for the Dependencies section.
    /// Idempotent: the <c>--dependencies</c> flag builds the tree before the scanner registry runs,
    /// so an already-populated tree is left alone rather than rebuilt.
    /// </summary>
    public static void ScanTransitiveReferences(string path, LibraryInspection model, VerboseLogger logger)
    {
        if (model.AssemblyInfo is not { References: { } references } info
            || info.TransitiveReferences is { Count: > 0 })
        {
            return;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            info.AssemblyName ?? Path.GetFileNameWithoutExtension(path)
        };

        info.TransitiveReferences = BuildTransitiveReferences(
            references,
            Path.GetDirectoryName(path),
            visited,
            logger,
            deduplicate: true);
    }

    /// <summary>
    /// Windows device names, which resolve to devices rather than files in any directory and can
    /// block or hang a read. Compared without extension and case-insensitively.
    /// </summary>
    /// <remarks>
    /// The superscript spellings are listed <em>literally</em> because Windows reserves those exact
    /// names, not because a superscript folds to a digit. Windows' matcher uppercases ASCII letters
    /// and strips trailing dots and spaces; it performs no Unicode normalization and no best-fit
    /// mapping. So <c>COM\u00b9</c> is a device while <c>COM\u2074</c>, <c>COM\uff11</c> and
    /// <c>\uff23\uff2f\uff2d1</c> are ordinary names. An earlier revision folded every character
    /// whose Unicode numeric value is a single digit, which refused a real SDK-built dependency
    /// named <c>COM\uff14</c> and truncated the tree at it.
    /// </remarks>
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "CLOCK$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM\u00b9", "COM\u00b2", "COM\u00b3",
        "LPT\u00b9", "LPT\u00b2", "LPT\u00b3"
    ];

    /// <summary>
    /// Whether an assembly simple name read from untrusted metadata is safe to use as a single
    /// path component. Mirrors <c>NuGetCache.ValidatePathComponent</c>'s rejection list — empty or
    /// whitespace, separators, volume qualifiers (<c>:</c>), null and other control characters, and
    /// rooted values — plus reserved device names, names the host would canonicalize to something
    /// else, and an upper length bound. A legitimate assembly simple name contains none of these.
    /// <para>
    /// Traversal is stopped by the separator rejections, not by looking for <c>..</c>:
    /// this name becomes one path component, and a component with no separator cannot leave its
    /// directory whatever dots it contains. Refusing every embedded <c>..</c> refused
    /// <c>Valid..Dependency</c>, a name the C# compiler accepts and emits, and the tree then showed
    /// that node with no company and no children because it was never resolved. Names made only of
    /// dots (<c>.</c>, <c>..</c>) are host-special and stay refused, but by the trailing-dot rule
    /// below, which every all-dot name reaches; there is no separate check for them here.
    /// </para>
    /// <para>
    /// There is deliberately no <c>Path.IsPathRooted</c> check. Rooting requires a separator on
    /// Unix, and a separator or a drive colon on Windows, so every rooted string is already refused
    /// by the three rejections above and the call could not return true for any input that reached
    /// it. Naming it as a gate here would describe a line that enforces nothing.
    /// </para>
    /// </summary>
    /// <summary>
    /// Renders an attacker-controlled name safe to write into a one-line diagnostic.
    /// </summary>
    /// <remarks>
    /// The refusal message is the one place a rejected name is echoed back, so the names it prints
    /// are exactly the hostile ones. Written verbatim, a name containing U+000A forges additional
    /// diagnostic lines and one containing U+001B injects terminal escape sequences into the
    /// operator's console. Escaping is a display concern only: the node keeps the raw
    /// <c>reference.Name</c> as its identity, because identity and presentation are separate and
    /// the structured writers escape on their own terms.
    /// A name that passes <see cref="IsSafeAssemblySimpleName"/> contains no control characters, so
    /// this is a no-op for every name that is not already refused.
    /// </remarks>
    internal static string DescribeUntrustedName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    internal static bool IsSafeAssemblySimpleName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > 256
            || name.Contains('/')
            || name.Contains('\\')
            || name.Contains(':'))
        {
            return false;
        }

        // Unpaired surrogates are checked before the rune scan, because EnumerateRunes replaces a
        // lone half with U+FFFD -- which is not Format -- so a malformed name would otherwise be
        // scanned as a well-formed one.
        for (var i = 0; i < name.Length; i++)
        {
            if (!char.IsSurrogate(name[i]))
                continue;

            if (i + 1 >= name.Length
                || !char.IsHighSurrogate(name[i])
                || !char.IsLowSurrogate(name[i + 1]))
            {
                return false;
            }

            i++;
        }

        foreach (var rune in name.EnumerateRunes())
        {
            if (!Rune.IsControl(rune) && Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format)
                continue;

            // Format characters are invisible: a zero-width space or a bidi override renders as
            // nothing, or reorders what follows it, so the name shown in the reference tree is not
            // the name being resolved. That is the Trojan Source problem (CVE-2021-42574) applied
            // to an identifier read from untrusted metadata. char.IsControl does not cover these.
            //
            // The scan is over RUNES, not chars. The Plane 14 tag block (U+E0000-U+E007F) is
            // entirely Format, but each of its code points is a surrogate pair, so a per-char scan
            // sees two Surrogate halves, finds neither Control nor Format, and accepts a name that
            // renders as nothing.
            return false;
        }

        // Windows strips trailing spaces and dots from a path component, so "CON " and "CON"
        // name the same thing there and "Foo." opens "Foo". A name that the host would rewrite
        // is ambiguous: it denotes one assembly in metadata and another on disk. Reject it rather
        // than trim it, for the same reason the caller rejects instead of sanitizing -- a trimmed
        // name silently designates a different assembly.
        //
        // Only the ASCII space and dot are canonicalized away, so only those make a name ambiguous
        // on disk. Edge whitespace is tested with char.IsWhiteSpace anyway: a name padded with
        // U+00A0 or U+3000 renders indistinguishably from the unpadded one in the reference tree
        // while denoting a different assembly, and no legitimate simple name carries it.
        if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[^1]) || name[^1] == '.')
        {
            return false;
        }

        // A device name is reserved with or without an extension, so compare the stem.
        var stem = name;
        var dot = stem.IndexOf('.');
        if (dot >= 0)
            stem = stem[..dot];

        // Windows also strips trailing dots and spaces from the STEM before matching, so
        // "COM1 .txt" reaches COM1. The edge-whitespace rule above cannot cover this: the space is
        // interior to the name, and only becomes trailing once the extension is split off.
        stem = stem.TrimEnd(' ', '.');

        foreach (var reserved in ReservedDeviceNames)
        {
            if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Classifies a resolved assembly by where the file lives, so provenance is a function of the
    /// file rather than of the route that reached it. Anything under a .NET shared-framework root
    /// or reference pack is platform; everything else ships with the inspected assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This asks <em>every</em> root that exists, not the one this process would run on.
    /// <c>GetSharedDirectory()</c> answers "which runtime is preferred" and returns only its first
    /// hit, so with <c>DOTNET_ROOT</c> pointing at one install, dependencies resolved out of
    /// another were all reported <c>local</c> -- the same mislabelling this method exists to
    /// prevent, reachable through an environment variable.
    /// </para>
    /// <para>
    /// Roots are canonicalised before comparison because <see cref="Path.GetFullPath(string)"/>
    /// does not resolve symlinks: a symlinked install made the prefix test fail against a real
    /// path underneath it.
    /// </para>
    /// <para>
    /// Whether case matters is asked of the volume holding each root, not of the operating system.
    /// Case sensitivity is a filesystem property, not a host property: macOS formats APFS
    /// case-insensitively by default but supports case-sensitive volumes, Linux supports
    /// case-insensitive ext4/F2FS directories, and Windows supports per-directory case sensitivity.
    /// Assuming the host default made a case-sensitive install alias distinct directories and
    /// report <c>platform</c> for files that are not under any root.
    /// </para>
    /// </remarks>
    internal static string ProvenanceOf(string resolvedPath) =>
        ProvenanceOf(resolvedPath, PlatformRoots.Value);

    /// <summary>
    /// The classification itself, over an explicit root set so a test can supply roots whose
    /// case sensitivity this host cannot produce.
    /// </summary>
    internal static string ProvenanceOf(string resolvedPath, IReadOnlyList<PlatformRoot> roots)
    {
        var full = Canonicalize(resolvedPath);

        foreach (var root in roots)
        {
            if (full.StartsWith(root.Path, root.Comparison))
            {
                return "platform";
            }
        }

        return "local";
    }

    /// <summary>
    /// A canonical, separator-terminated platform root paired with the comparison its own volume
    /// honours.
    /// </summary>
    internal readonly record struct PlatformRoot(string Path, StringComparison Comparison);

    /// <summary>
    /// Canonical, separator-terminated platform roots. Computed once: the candidate scan probes the
    /// filesystem, and classification runs per resolved reference across a recursive walk.
    /// </summary>
    private static readonly Lazy<List<PlatformRoot>> PlatformRoots = new(() =>
    {
        List<PlatformRoot> roots = [];
        foreach (var dir in PlatformResolver.GetAllSharedDirectories()
            .Concat(PlatformResolver.GetAllPacksDirectories()))
        {
            var root = Canonicalize(dir);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
            {
                root += Path.DirectorySeparatorChar;
            }

            if (!roots.Any(r => r.Path == root))
            {
                roots.Add(new PlatformRoot(root, ComparisonForVolumeHolding(root)));
            }
        }

        return roots;
    });

    /// <summary>
    /// Asks the filesystem whether <paramref name="directory"/> can be reached under a spelling
    /// that differs only in case, and reports the comparison that answer implies.
    /// </summary>
    /// <remarks>
    /// Existence of the flipped spelling is not on its own evidence of aliasing: a case-sensitive
    /// volume can hold <c>dotnet</c> and <c>DOTNET</c> as two genuinely different directories, and
    /// a probe that only asked whether the flipped path existed concluded "case-insensitive" there
    /// and reported files under one tree as belonging to the other. So the parent's listing is
    /// consulted too. If the flipped name is really there, both spellings coexist and the volume
    /// plainly distinguishes case; if it is absent from the listing yet still resolves, the
    /// filesystem is aliasing case. Both checks are read-only, which matters because platform
    /// roots are not writable.
    /// </remarks>
    internal static StringComparison ComparisonForVolumeHolding(string directory) =>
        ComparisonForVolumeHolding(directory, Directory.Exists, EnumerateDirectoryNames);

    /// <summary>
    /// The probe over explicit filesystem queries, so a test can model volumes this host cannot
    /// create -- a case-sensitive one, and one holding both spellings as distinct directories.
    /// </summary>
    internal static StringComparison ComparisonForVolumeHolding(
        string directory,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> enumerateDirectoryNames)
    {
        var hostDefault = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var trimmed = Path.TrimEndingDirectorySeparator(directory);
        var parent = Path.GetDirectoryName(trimmed);
        var name = Path.GetFileName(trimmed);

        if (string.IsNullOrEmpty(parent) || FlipLetterCase(name) is not { } flippedName)
            return hostDefault;

        if (!directoryExists(directory))
            return hostDefault;

        // A real sibling under the flipped spelling means the volume is holding both, so it
        // distinguishes case whatever the flipped path resolves to.
        foreach (var entry in enumerateDirectoryNames(parent))
        {
            if (string.Equals(entry, flippedName, StringComparison.Ordinal))
                return StringComparison.Ordinal;
        }

        return directoryExists(Path.Combine(parent, flippedName))
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static IEnumerable<string> EnumerateDirectoryNames(string parent)
    {
        try
        {
            return Directory.EnumerateDirectories(parent).Select(Path.GetFileName).OfType<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? FlipLetterCase(string value)
    {
        var flipped = new char[value.Length];
        var sawLetter = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                flipped[i] = char.ToLowerInvariant(c);
                sawLetter = true;
            }
            else if (char.IsLower(c))
            {
                flipped[i] = char.ToUpperInvariant(c);
                sawLetter = true;
            }
            else
            {
                flipped[i] = c;
            }
        }

        return sawLetter ? new string(flipped) : null;
    }

    /// <summary>
    /// Resolves <paramref name="full"/> one component at a time, following a link wherever one
    /// appears in the chain. Each component is visited once, so this terminates; following a chain
    /// of links at a single component is bounded by <see cref="FileSystemInfo.ResolveLinkTarget"/>
    /// itself.
    /// </summary>
    private static string ResolveLinkChain(string full)
    {
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;

        foreach (var segment in full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);

            var target = Directory.Exists(current)
                ? new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true)?.FullName
                : File.Exists(current)
                    ? new FileInfo(current).ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    : null;

            if (!string.IsNullOrEmpty(target))
            {
                current = target;
            }
        }

        return current;
    }

    /// <summary>
    /// Internal so a test can build a synthetic root the way <see cref="PlatformRoots"/> builds a
    /// real one. A fixture root that skipped this was not comparable to the paths classification
    /// canonicalizes: on macOS <c>/var</c> is a link to <c>/private/var</c>, so a root under the
    /// temp directory failed to prefix-match a probe path beneath it.
    /// </summary>
    internal static string Canonicalize(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            // Every ancestor is resolved, not just the leaf and its immediate parent. A .NET
            // install reached through a symlinked ancestor -- /opt/sdk -> /usr/local/share/dotnet,
            // then /opt/sdk/shared/Microsoft.NETCore.App/9.0.0/System.Text.Json.dll -- has no link
            // at the leaf and none at its parent, so resolving only those two returned the
            // unresolved path, it matched no canonical platform root, and the assembly was
            // reported resolved_from: "local". Path.GetFullPath does not resolve links at all.
            return ResolveLinkChain(full);
        }
        catch (IOException)
        {
            // An unreadable or broken link is not a classification failure; fall back to the
            // lexical path rather than dropping the node.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return full;
    }

    /// <summary>
    /// Builds a recursive tree of assembly references with resolution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provenance is derived from where a resolved file actually lives, never from who asked for
    /// it. That is deliberate, and two defects motivate it.
    /// </para>
    /// <para>
    /// Probing "beside <paramref name="sourceDir"/>" is not the same claim at depth 3 as at depth
    /// 0, because recursion replaces <paramref name="sourceDir"/> with the resolved parent's
    /// directory. Treating a hit as <c>local</c> therefore reported a platform assembly's own
    /// dependency -- found beside it, inside the shared framework -- as "shipped next to the
    /// assembly you inspected". Inheriting the parent's kind instead fixes that for depth >= 1 but
    /// leaves depth 0 wrong whenever the inspected assembly is itself a platform assembly, since
    /// the root callers have no parent to inherit from.
    /// </para>
    /// <para>
    /// Inheritance is also order-dependent under <paramref name="deduplicate"/>: <c>visited</c> is
    /// shared across the walk, so the first route to an assembly wins and the reported kind follows
    /// alphabetical visit order rather than the file. Deriving from the path makes the answer a
    /// function of the file alone, so both routes agree.
    /// </para>
    /// </remarks>
    public static List<AssemblyReferenceNode> BuildTransitiveReferences(
        List<AssemblyReference> references,
        string? sourceDir,
        HashSet<string> visited,
        VerboseLogger logger,
        int depth = 0,
        bool deduplicate = false,
        Dictionary<string, int>? globalSeen = null)
    {
        globalSeen ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        List<AssemblyReferenceNode> nodes = [];

        foreach (var reference in references.OrderBy(r => r.Name))
        {
            if (deduplicate && globalSeen.TryGetValue(reference.Name, out int seenDepth) && seenDepth <= depth)
            {
                continue;
            }

            var node = new AssemblyReferenceNode
            {
                Name = reference.Name,
                Version = reference.Version,
                PublicKeyToken = reference.PublicKeyToken,
                Depth = depth
            };

            if (visited.Contains(reference.Name))
            {
                if (!deduplicate)
                {
                    node.IsCyclic = true;
                    nodes.Add(node);
                }
                continue;
            }

            if (deduplicate)
            {
                globalSeen[reference.Name] = depth;
            }

            visited.Add(reference.Name);

            // Reference names come from the inspected assembly's metadata, which is attacker-
            // controlled, and they are about to be joined onto a directory and probed. The threat
            // model is explicit that Path.Combine(root, untrustedValue) is not a containment check
            // (docs/design/untrusted-data-threat-model.md, "Derived paths"), so reject unsafe names
            // rather than sanitize them. The node is still emitted -- the reference genuinely exists
            // in the metadata, and dropping it would hide evidence; only resolution is refused.
            if (!IsSafeAssemblySimpleName(reference.Name))
            {
                logger.Warn($"refusing to resolve reference with unsafe assembly name: '{DescribeUntrustedName(reference.Name)}'");
                nodes.Add(node);
                continue;
            }

            string? resolvedPath = null;
            string? resolvedFrom = null;

            if (!string.IsNullOrEmpty(sourceDir))
            {
                var localPath = Path.Combine(sourceDir, reference.Name + ".dll");
                if (File.Exists(localPath))
                {
                    resolvedPath = localPath;
                    resolvedFrom = ProvenanceOf(localPath);
                }
            }

            if (resolvedPath == null)
            {
                var (platformPath, _, _, error) = PlatformResolver.ResolveAssembly(reference.Name);
                if (error == null && platformPath != null)
                {
                    resolvedPath = platformPath;
                    resolvedFrom = "platform";
                }
            }

            node.Path = resolvedPath;
            node.ResolvedFrom = resolvedFrom;
            nodes.Add(node);

            if (resolvedPath != null)
            {
                try
                {
                    var (childRefs, company) = AssemblyInspector.ExtractReferencesAndCompany(resolvedPath);
                    node.Company = company;
                    if (childRefs.Count > 0)
                    {
                        var branchVisited = deduplicate ? visited : new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase);
                        var childNodes = BuildTransitiveReferences(
                            childRefs,
                            Path.GetDirectoryName(resolvedPath),
                            branchVisited,
                            logger,
                            depth + 1,
                            deduplicate,
                            globalSeen);
                        nodes.AddRange(childNodes);
                    }
                }
                catch
                {
                    // Couldn't read the assembly - just skip children
                }
            }
        }

        return nodes;
    }

    /// <summary>
    /// Scans an assembly for extension members and retains Metadata's typed census.
    /// </summary>
    internal static void ScanExtensionMembers(
        string path,
        LibraryInspection inspection,
        VerboseLogger logger)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            ScanExtensionMembers(session, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning extensions in {path}: {ex.Message}");
            inspection.SetExtensionMemberInspection(
                FailedInspection<ExtensionMemberObservation>(
                    path, MetadataFindings.ExtensionMemberDescriptor, ex),
                displayOrder: null);
        }
    }

    internal static void ScanExtensionMembers(
        AssemblyInspectionSession session,
        string path,
        LibraryInspection inspection,
        VerboseLogger logger)
    {
        try
        {
            var extensions = session.ExtensionMethods().ToArray();
            inspection.SetExtensionMemberInspection(
                MetadataFindings.InspectExtensionMembers(
                    extensions,
                    FindingSubjectFor(path)),
                extensions);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning extensions in {path}: {ex.Message}");
            inspection.SetExtensionMemberInspection(
                FailedInspection<ExtensionMemberObservation>(
                    path, MetadataFindings.ExtensionMemberDescriptor, ex),
                displayOrder: null);
        }
    }

    /// <summary>
    /// Scans an assembly for unsafe and P/Invoke methods.
    /// </summary>
    internal static void ScanClassifiedMethods(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            ScanClassifiedMethods(session, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning classified methods in {path}: {ex.Message}");
            inspection.ClassifiedMethodInspection = FailedInspection<ClassifiedMethodObservation>(
                path, MetadataFindings.ClassifiedMethodDescriptor, ex);
        }
    }

    internal static void ScanClassifiedMethods(AssemblyInspectionSession session, string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            ApplyClassifiedMethods(
                session.ClassifiedMethods(),
                inspection,
                FindingSubjectFor(path));
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning classified methods in {path}: {ex.Message}");
            inspection.ClassifiedMethodInspection = FailedInspection<ClassifiedMethodObservation>(
                path, MetadataFindings.ClassifiedMethodDescriptor, ex);
        }
    }

    static void ApplyClassifiedMethods(
        List<ClassifiedMethodInfo> classified,
        LibraryInspection inspection,
        FindingSubject subject)
    {
        inspection.ClassifiedMethodInspection =
            MetadataFindings.InspectClassifiedMethods(classified, subject);

        if (classified.Count == 0) return;

        var unsafe_ = classified
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

        var pinvoke = classified
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

        inspection.UnsafeMethods = unsafe_.Count > 0 ? unsafe_ : null;
        inspection.PInvokeMethods = pinvoke.Count > 0 ? pinvoke : null;

        var async = classified
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
            // Runtime async first (sorts before "State machine"), then by type/name.
            .OrderBy(m => m.Kind, StringComparer.Ordinal)
            .ThenBy(m => m.DeclaringType)
            .ThenBy(m => m.MethodName)
            .ToList();

        inspection.AsyncMethods = async.Count > 0 ? async : null;
    }

    internal static List<UnsafeMemberSummary>? ScanUnsafeMembers(Func<Analysis.LibraryBodyIndex> openIndex, string path, VerboseLogger logger)
    {
        try
        {
            var index = openIndex();
            var rows = index.UnsafeEvidence
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

            foreach (var diagnostic in index.Diagnostics)
                logger.Log($"Warning: unsafe analysis skipped {diagnostic.Method}: {diagnostic.Message}");

            return rows.Count > 0 ? rows : null;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning unsafe members in {path}: {ex.Message}");
            return null;
        }
    }

    private static string FormatMethod(Analysis.MethodIdentity method)
        => $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}({string.Join(", ", method.ParameterTypes.Select(p => p.ToQualifiedDisplayString()))})";

    // Compiler/source-generated implementation details (display classes, state machines,
    // the <>c lambda cache, <PrivateImplementationDetails>, System.Text.Json context
    // helpers) are not actionable source-shape fixes, so optimization scans suppress them
    // and leverage scans label them as generated.
    internal static bool IsGeneratedMethod(Analysis.MethodIdentity method)
        => ILInspector.Metadata.MemberFilters.IsCompilerGenerated(method.Name)
           || ILInspector.Metadata.TypeFilters.IsCompilerGeneratedNested(method.DeclaringType.Name)
           || IsSystemTextJsonContextGeneratedMethod(method);

    // Overload that also treats members of structurally-detected generated framework types
    // (protobuf/gRPC, see LibraryBodyIndex.GeneratedFrameworkTypeNames) as generated, so their
    // thick static initializers and stubs are marked in Top Leverage and suppressed from
    // Performance Triage even though no [GeneratedCode] attribute is emitted.
    internal static bool IsGeneratedMethod(Analysis.MethodIdentity method, IReadOnlySet<string> generatedFrameworkTypes)
        => IsGeneratedMethod(method)
           || generatedFrameworkTypes.Contains(method.DeclaringType.ToQualifiedDisplayString());

    private static bool IsSystemTextJsonContextGeneratedMethod(Analysis.MethodIdentity method)
        => method.Name is "TryGetTypeInfoForRuntimeCustomConverter"
           && method.IsStatic
           && method.ReturnType.Equals(Analysis.TypeRef.CoreLib("System", "Boolean"))
           && method.ParameterTypes.Length == 2
           && method.ParameterTypes[0].Equals(Analysis.TypeRef.Definition("System.Text.Json", "System.Text.Json", "JsonSerializerOptions"))
           && method.ParameterTypes[1] is { Kind: Analysis.TypeRefKind.ByRef, ElementType: { } jsonTypeInfo }
           && IsJsonTypeInfo(jsonTypeInfo);

    private static bool IsJsonTypeInfo(Analysis.TypeRef type)
        => type.Kind == Analysis.TypeRefKind.GenericInstance
           && type.ElementType is { } definition
           && definition.Equals(Analysis.TypeRef.Definition("System.Text.Json", "System.Text.Json.Serialization.Metadata", "JsonTypeInfo`1"));

    /// <summary>
    /// Ranks the assembly's methods by call-graph leverage (distinct direct callers,
    /// then outbound shape). Emits the full ranked set so the row limiter (<c>-n</c>/
    /// <c>--rows</c>) controls how many rows are shown, matching the type-scoped view.
    /// </summary>
    internal static List<MethodLeverageSummary>? ScanTopLeverage(
        Func<Analysis.LibraryBodyIndex> openIndex,
        Func<IReadOnlyDictionary<int, (string? Stable, string Visibility, string Selector)>>
            getDrillMap,
        string path,
        VerboseLogger logger)
    {
        try
        {
            var index = openIndex();
            var generatedFrameworkTypes = index.GeneratedFrameworkTypeNames;
            // Reuse the exact Member Index canonical-signature/digest path (via the
            // extracted API surface) so library-scope rows carry the same round-tripping
            // Stable selector, Visibility, and Name:N Selector as the type-scoped view.
            var drillByToken = getDrillMap();
            var rows = index.TopLeverage(int.MaxValue)
                .Select(entry =>
                {
                    drillByToken.TryGetValue(entry.Method.MetadataToken, out var drill);
                    return new MethodLeverageSummary
                    {
                        Member = FormatMethod(entry.Method),
                        Callers = entry.DirectCallerCount,
                        RootReach = entry.RootReach,
                        Fanout = entry.Fanout,
                        Depth = entry.MaxDepth,
                        LoopCalls = entry.LoopCallCount,
                        Generated = IsGeneratedMethod(entry.Method, generatedFrameworkTypes),
                        Visibility = drill.Visibility,
                        Stable = drill.Stable,
                        Selector = drill.Selector,
                    };
                })
                .ToList();
            return rows.Count > 0 ? rows : null;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning leverage in {path}: {ex.Message}");
            return null;
        }
    }

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

            // All-members first (covers non-public, numbered as `--all` drilling resolves them).
            AddSurface(context.ExtractApiSurface(includeAll: true), map);
            // Default surface overwrites public members with their public-only Name:N, which is
            // what `member Name:N` resolves without `--all`.
            AddSurface(context.ExtractApiSurface(includeAll: false), map);
        }
        catch (Exception ex)
        {
            logger.Log(
                $"Warning: Error building leverage selectors for {context.AssemblyPath}: {ex.Message}");
        }
        return map;

        static void AddSurface(ILInspector.Metadata.ApiSurface surface, Dictionary<int, (string? Stable, string Visibility, string Selector)> target)
        {
            foreach (var type in surface.Types)
            {
                foreach (var (token, drill) in ApiOutputFormatter.BuildMemberDrillMap(type))
                    target[token] = drill;
            }
        }
    }

    /// <summary>
    /// Collects safe, local optimization opportunities across the whole assembly. Emits the
    /// filtered set in triage priority order so the highest-value pay-dirt surfaces first.
    /// </summary>
    internal static List<OptimizationOpportunitySummary>? ScanOptimizationOpportunities(
        Func<Analysis.LibraryBodyIndex> openIndex,
        string path,
        VerboseLogger logger,
        PerformanceTriageOptions? options = null)
    {
        try
        {
            var index = openIndex();
            var generatedFrameworkTypes = index.GeneratedFrameworkTypeNames;
            var rows = FilterAndOrderTriageOpportunities(
                    TriageOpportunities(index, options)
                        .Where(opportunity => !IsGeneratedMethod(opportunity.Method, generatedFrameworkTypes)),
                    options)
                .Select(opportunity => new OptimizationOpportunitySummary
                {
                    Member = FormatMethod(opportunity.Method),
                    Candidate = opportunity.CandidateId,
                    Finding = opportunity.SourceFinding,
                    Provenance = FormatProvenance(opportunity.Provenance),
                    RootReach = opportunity.RootReach,
                    Shape = opportunity.Shape,
                    Operation = opportunity.Operation,
                    Token = FormatToken(opportunity.OperandToken),
                    Evidence = opportunity.Evidence,
                    Fix = opportunity.SafeFixDirection,
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
                })
                .ToList();
            return rows.Count > 0 ? rows : null;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning optimization opportunities in {path}: {ex.Message}");
            return null;
        }
    }

    internal static void ScanResourceTriage(
        Func<Analysis.LibraryBodyIndex> openIndex,
        Func<IReadOnlyDictionary<int, (string? Stable, string Visibility, string Selector)>>
            getDrillMap,
        string path,
        LibraryInspection inspection,
        VerboseLogger logger)
    {
        var result = Analysis.ResourceLifecycleAnalysis.InspectAssembly(
            openIndex,
            new FindingSubject(Path.GetFullPath(path), Path.GetFileName(path)));
        inspection.ResourceLifecycleInspection = result;
        inspection.ResourceTriage =
            result.Value
                is FindingInspection<Analysis.ResourceLifecycleOccurrence>.Complete complete
                ? ProjectResourceTriage(
                    complete,
                    getDrillMap())
                : null;
    }

    static List<ResourceTriageSummary> ProjectResourceTriage(
        FindingInspection<Analysis.ResourceLifecycleOccurrence>.Complete inspection,
        IReadOnlyDictionary<int, (string? Stable, string Visibility, string Selector)>
            drillByToken)
    {
        return Analysis.ResourceTriageAnalysis
            .Assess(inspection)
            .Where(assessment =>
                assessment.Actionability
                    == Analysis.ResourceTriageActionability.UntrustedActionable)
            .Select(assessment =>
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
            })
            .OrderBy(
                static row => row.Member,
                StringComparer.Ordinal)
            .ThenBy(
                static row => row.AcquireOffset)
            .ThenBy(
                static row => row.Boundaries.Count > 0
                    ? row.Boundaries[0].ILOffset
                    : -1)
            .ToList();
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

    // Performance Triage ordering: surface pay-dirt first. In-loop (repeated, hot)
    // allocations lead, then by confidence, then by call-graph leverage (root reach),
    // then a stable structural tie-break. This is distinct from Top Leverage, which ranks
    // purely by reach. Extracted so the ranking model is guarded by a labeled, non-vacuous
    // test (analysis quality ladder #1623 rung 5), not only by self-consistent monotonicity.
    internal static IEnumerable<Analysis.OptimizationOpportunity> OrderByTriagePriority(IEnumerable<Analysis.OptimizationOpportunity> opportunities)
        => opportunities
            .OrderByDescending(IteratesInLoop)
            .ThenByDescending(opportunity => ConfidenceRank(opportunity.Confidence))
            .ThenByDescending(opportunity => opportunity.RootReach)
            .ThenBy(opportunity => opportunity.Method.DeclaringType.ToQualifiedDisplayString(), StringComparer.Ordinal)
            .ThenBy(opportunity => opportunity.Method.Name, StringComparer.Ordinal)
            .ThenBy(opportunity => opportunity.ILOffset ?? -1)
            .ThenBy(opportunity => opportunity.Shape, StringComparer.Ordinal);

    // Whether an allocation opportunity actually iterates as a hot loop, per the
    // semantic per-invocation multiplicity (#2127). A structural in-loop offset that
    // is really a return/throw early-exit (Multiplicity Conditional/Unknown) is NOT a
    // hot loop; fall back to the structural InLoop flag only when multiplicity is
    // unknown. This is the single source of truth for the Loop column, triage sort,
    // and the --loop filter.
    internal static bool IteratesInLoop(Analysis.OptimizationOpportunity opportunity)
        => opportunity.Multiplicity == "loop"
            || (opportunity.Multiplicity is null && opportunity.InLoop);

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
            "Evidence" => opportunity.Evidence,
            "Fix" => opportunity.SafeFixDirection,
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

    internal static FindingInspection<OpenTelemetrySignalInfo> ScanOpenTelemetry(
        string path,
        VerboseLogger logger)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            return ScanOpenTelemetry(session, path, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning OpenTelemetry support in {path}: {ex.Message}");
            return FailedInspection<OpenTelemetrySignalInfo>(
                path, MetadataFindings.OpenTelemetrySignalDescriptor, ex);
        }
    }

    internal static void ScanIntegrations(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            ScanIntegrations(session, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning ecosystem integrations in {path}: {ex.Message}");
            MarkIntegrationFailuresIfMissing(path, inspection, ex);
        }
    }

    internal static void ScanIntegrations(AssemblyInspectionSession session, string path, LibraryInspection inspection, VerboseLogger logger)
    {
        inspection.OpenTelemetryInspection = ScanOpenTelemetry(session, path, logger);
        inspection.EcosystemIntegrationInspection = ScanEcosystemIntegrations(session, path, logger);
    }

    internal static void ScanIntegrationOpportunities(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            ScanIntegrationOpportunities(session, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning integration opportunities in {path}: {ex.Message}");
            MarkIntegrationFailuresIfMissing(path, inspection, ex);
        }
    }

    internal static void ScanIntegrationOpportunities(AssemblyInspectionSession session, string path, LibraryInspection inspection, VerboseLogger logger)
    {
        if (inspection.EcosystemIntegrationInspection is null
            || inspection.OpenTelemetryInspection is null)
            ScanIntegrations(session, path, inspection, logger);

        var existing = new HashSet<string>(
            LibraryIntegrationCatalog.All
                .Where(descriptor => descriptor.GetSignals(inspection).Count > 0)
                .Select(descriptor => descriptor.Name),
            StringComparer.Ordinal);
        var gaps = session.IntegrationOpportunities(existing);
        inspection.IntegrationOpportunities = gaps.Count > 0 ? gaps : null;
    }

    internal static FindingInspection<OpenTelemetrySignalInfo> ScanOpenTelemetry(
        AssemblyInspectionSession session,
        string path,
        VerboseLogger logger)
    {
        try
        {
            return MetadataFindings.InspectOpenTelemetrySignals(
                session.OpenTelemetrySignals(),
                FindingSubjectFor(path));
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning OpenTelemetry support in {path}: {ex.Message}");
            return FailedInspection<OpenTelemetrySignalInfo>(
                path, MetadataFindings.OpenTelemetrySignalDescriptor, ex);
        }
    }

    static FindingInspection<EcosystemIntegrationSignalInfo> ScanEcosystemIntegrations(
        AssemblyInspectionSession session,
        string path,
        VerboseLogger logger)
    {
        try
        {
            return MetadataFindings.InspectEcosystemIntegrations(
                session.EcosystemIntegrations(),
                FindingSubjectFor(path));
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning ecosystem integrations in {path}: {ex.Message}");
            return FailedInspection<EcosystemIntegrationSignalInfo>(
                path, MetadataFindings.EcosystemIntegrationDescriptor, ex);
        }
    }

    internal static void ScanInfoCounts(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        // Open the assembly once and share the reader across all five scans instead of re-opening
        // the same file per scan.
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            if (inspection.ExtensionMemberInspection is null)
                ScanExtensionMembers(session, path, inspection, logger);
            ScanClassifiedMethods(session, path, inspection, logger);
            inspection.ResourceInspection ??= ScanResources(session, path, logger);
            ScanCustomAttributes(session, path, inspection, logger);
            ScanTypeForwarders(session, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error opening {path} for scanning: {ex.Message}");
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
            inspection.TypeForwarderInspection ??= FailedInspection<TypeForwarderInfo>(
                path, MetadataFindings.TypeForwarderDescriptor, ex);
        }
    }

    /// <summary>
    /// Scans an assembly for manifest resources.
    /// </summary>
    internal static FindingInspection<MetadataResource> ScanResources(
        string path,
        VerboseLogger logger)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            return ScanResources(session, path, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning resources in {path}: {ex.Message}");
            return FailedInspection<MetadataResource>(
                path, MetadataFindings.ResourceDescriptor, ex);
        }
    }

    internal static FindingInspection<MetadataResource> ScanResources(
        AssemblyInspectionSession session,
        string path,
        VerboseLogger logger)
    {
        try
        {
            return MetadataFindings.InspectResources(
                session.Resources(),
                FindingSubjectFor(path));
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning resources in {path}: {ex.Message}");
            return FailedInspection<MetadataResource>(
                path, MetadataFindings.ResourceDescriptor, ex);
        }
    }

    internal static FindingInspection<SwitchInfo> ScanSwitches(
        string path,
        VerboseLogger logger)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            HashSet<SwitchInfo> switches = [.. session.Switches()];
            if (session.HasMetadata)
            {
                AddAppContextSwitches(
                    switches,
                    AppContextSwitchProjectionProducer.Produce(session.MethodBodies));
            }

            var orderedSwitches = switches
                .OrderBy(s => s.Kind, StringComparer.Ordinal)
                .ThenBy(s => s.Switch, StringComparer.Ordinal)
                .ThenBy(s => s.Api, StringComparer.Ordinal)
                .ToList();
            return MetadataFindings.InspectSwitches(
                orderedSwitches,
                FindingSubjectFor(path));
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning switches in {path}: {ex.Message}");
            return FailedInspection<SwitchInfo>(
                path, MetadataFindings.SwitchDescriptor, ex);
        }
    }

    static void AddAppContextSwitches(
        HashSet<SwitchInfo> switches,
        IEnumerable<AppContextSwitchOccurrence> occurrences)
    {
        foreach (var occurrence in occurrences)
        {
            if (occurrence.Switch.StartsWith("System.Resources.UseSystemResourceKeys", StringComparison.Ordinal)
                || occurrence.Switch.StartsWith("TestSwitch.", StringComparison.Ordinal)
                || occurrence.Switch.StartsWith("Switch.", StringComparison.Ordinal))
            {
                continue;
            }

            switches.Add(new SwitchInfo("AppContext", occurrence.Switch, occurrence.Api));
        }
    }

    /// <summary>
    /// Scans an assembly for custom attributes (assembly-level and module-level).
    /// </summary>
    internal static void ScanCustomAttributes(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            ScanCustomAttributes(session, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning custom attributes in {path}: {ex.Message}");
            inspection.SetAssemblyAttributeInspection(
                FailedInspection<AssemblyAttributeInfo>(
                    path, MetadataFindings.AssemblyAttributeDescriptor, ex),
                jsonOrder: null);
        }
    }

    internal static void ScanCustomAttributes(AssemblyInspectionSession session, string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            var attributes = session.CustomAttributes();
            inspection.SetAssemblyAttributeInspection(
                MetadataFindings.InspectAssemblyAttributes(
                    attributes,
                    FindingSubjectFor(path)),
                attributes);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning custom attributes in {path}: {ex.Message}");
            inspection.SetAssemblyAttributeInspection(
                FailedInspection<AssemblyAttributeInfo>(
                    path, MetadataFindings.AssemblyAttributeDescriptor, ex),
                jsonOrder: null);
        }
    }

    internal static FindingInspection<UnionTypeInfo> ScanUnionTypes(
        string path,
        VerboseLogger logger)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            return ScanUnionTypes(session, path, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning union types in {path}: {ex.Message}");
            return FailedInspection<UnionTypeInfo>(
                path, MetadataFindings.UnionTypeDescriptor, ex);
        }
    }

    internal static FindingInspection<UnionTypeInfo> ScanUnionTypes(
        AssemblyInspectionSession session,
        string path,
        VerboseLogger logger)
    {
        try
        {
            return MetadataFindings.InspectUnionTypes(
                session.UnionTypes(),
                FindingSubjectFor(path));
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning union types in {path}: {ex.Message}");
            return FailedInspection<UnionTypeInfo>(
                path, MetadataFindings.UnionTypeDescriptor, ex);
        }
    }

    /// <summary>
    /// Scans an assembly for type forwarders.
    /// </summary>
    internal static void ScanTypeForwarders(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            ScanTypeForwarders(session, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning type forwarders in {path}: {ex.Message}");
            inspection.TypeForwarderInspection = FailedInspection<TypeForwarderInfo>(
                path, MetadataFindings.TypeForwarderDescriptor, ex);
        }
    }

    internal static void ScanTypeForwarders(AssemblyInspectionSession session, string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            inspection.TypeForwarderInspection = MetadataFindings.InspectTypeForwarders(
                session.TypeForwarders(),
                FindingSubjectFor(path));
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning type forwarders in {path}: {ex.Message}");
            inspection.TypeForwarderInspection = FailedInspection<TypeForwarderInfo>(
                path, MetadataFindings.TypeForwarderDescriptor, ex);
        }
    }

    /// <summary>
    /// Scans the image-level metadata facts backing the <c>@Metadata</c> lens: metadata version,
    /// heap sizes, and per-table physical row counts.
    ///
    /// This is the cheap half of the lens deliberately. It reads table row counts, never rows, so
    /// selecting one metadata section does not pay to project every table; the per-table sections
    /// consult these counts to decide whether they have anything to render, and the row projection
    /// happens at render time for the selected tables only.
    /// </summary>
    internal static void ScanMetadataImage(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        // Recorded even when the describe below fails, so the render path can tell "the scanner
        // never ran" (path is null) from "the scanner ran and found no metadata" (path is set,
        // overview is null) and report the second rather than rendering empty sections.
        inspection.MetadataAssemblyPath = path;

        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            inspection.MetadataOverview = session.MetadataImage();
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error reading metadata image of {path}: {ex.Message}");
            inspection.MetadataOverview = null;
        }
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
}
