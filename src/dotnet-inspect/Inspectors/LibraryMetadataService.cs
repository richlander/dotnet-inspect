using DotnetInspector.Core;
using DotnetInspector.Models;
using System.Globalization;
using System.Reflection;
using System.Reflection.PortableExecutable;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
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
        ScannerRegistry? scannerRegistry = null)
    {
        logger.Log($"Inspecting: {Path.GetFileName(path)}");

        try
        {
            using var service = SourceLinkService.Open(path, logger.Log);
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

            inspection.AssemblyInfo = pdbContext.ExtractAssemblyInfo(options.IncludeReferences || options.IncludeDependencies || needsAuditSignals);
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
            inspection.HasSwitches = presenceFlags.HasSwitches;
            inspection.SwitchCount = presenceFlags.SwitchCount;

            // PE debug directory fields
            inspection.HasReproducibleFlag = pdbContext.HasReproducibleFlag;
            inspection.HasEmbeddedPdb = pdbContext.HasEmbeddedPdb;
            inspection.PdbPath = pdbContext.CodeViewPdbPath;
            inspection.HasNormalizedPaths = pdbContext.HasNormalizedPaths;
            inspection.NonNormalizedPaths = pdbContext.NonNormalizedPaths;
            inspection.IsDeterministic = pdbContext.HasReproducibleFlag && pdbContext.HasNormalizedPaths != false;

            // Build transitive reference tree if requested
            if (options.IncludeDependencies && inspection.AssemblyInfo?.References != null)
            {
                var sourceDir = Path.GetDirectoryName(path);
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                visited.Add(inspection.AssemblyInfo.AssemblyName ?? Path.GetFileNameWithoutExtension(path));

                inspection.AssemblyInfo.TransitiveReferences = BuildTransitiveReferences(
                    inspection.AssemblyInfo.References,
                    sourceDir,
                    visited,
                    logger,
                    deduplicate: options.IncludeDependencies);
            }

            // Run registered scanners for the requested sections
            if (scannerRegistry != null && scanners != null)
            {
                scannerRegistry.RunScanners(scanners, new Sections.ScannerContext
                {
                    AssemblyPath = path,
                    Model = inspection,
                    Logger = logger,
                    IncludeOpportunities = scanners.Contains(Sections.LibrarySections.ScannerOptimizationOpportunities),
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

            // Decide network work from section selection + capability authorization (keyed off the
            // user's original verbosity, never an internally force-bumped value). PDB download and
            // source verification are authorized only when a selected section declares the capability.
            var pipeline = LibrarySections.CreatePipeline();
            var include = options.IncludeSections;
            var pdbSections = pipeline.GetAuthorizedSections(
                SectionCapabilities.MayDownloadPdb, options.UserVerbosity, include);
            bool allowPdbDownload = pdbSections.Count > 0;
            bool runHeadAudit = pipeline.GetAuthorizedSections(
                SectionCapabilities.MayAuditSources, options.UserVerbosity, include).Count > 0;
            bool runIntegrity = pipeline.GetAuthorizedSections(
                SectionCapabilities.MayFetchSources, options.UserVerbosity, include).Count > 0;

            await AuditAsync(service, inspection, path, packageName, packageVersion, logger, httpClient, isPlatformAssembly, allowPdbDownload: allowPdbDownload);

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

            if (runHeadAudit && service.HasSourceLink && pdbContext.HasPdb)
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

            if (runIntegrity && service.HasSourceLink && pdbContext.HasPdb)
            {
                await SourceIntegrityService.PopulateAsync(
                    inspection.SourceDocumentInspection,
                    inspection,
                    logger);
                if (needsAuditSignals)
                    AuditSignalBuilder.PopulateLibraryAudit(path, inspection, logger);
            }

            if (include?.Contains("Source Files") == true)
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
        bool allowPdbDownload = false)
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
    /// Builds a recursive tree of assembly references with resolution.
    /// </summary>
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

            string? resolvedPath = null;
            string? resolvedFrom = null;

            if (!string.IsNullOrEmpty(sourceDir))
            {
                var localPath = Path.Combine(sourceDir, reference.Name + ".dll");
                if (File.Exists(localPath))
                {
                    resolvedPath = localPath;
                    resolvedFrom = "local";
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
                        var childNodes = BuildTransitiveReferences(childRefs, Path.GetDirectoryName(resolvedPath), branchVisited, logger, depth + 1, deduplicate, globalSeen);
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
    internal static List<MethodLeverageSummary>? ScanTopLeverage(Func<Analysis.LibraryBodyIndex> openIndex, string path, VerboseLogger logger)
    {
        try
        {
            var index = openIndex();
            var generatedFrameworkTypes = index.GeneratedFrameworkTypeNames;
            // Reuse the exact Member Index canonical-signature/digest path (via the
            // extracted API surface) so library-scope rows carry the same round-tripping
            // Stable selector, Visibility, and Name:N Selector as the type-scoped view.
            var drillByToken = BuildLibraryDrillMap(path, logger);
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
    static Dictionary<int, (string? Stable, string Visibility, string Selector)> BuildLibraryDrillMap(string path, VerboseLogger logger)
    {
        var map = new Dictionary<int, (string? Stable, string Visibility, string Selector)>();
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            if (!session.HasMetadata)
                return map;

            // All-members first (covers non-public, numbered as `--all` drilling resolves them).
            AddSurface(session.ApiSurface(includeAll: true), map);
            // Default surface overwrites public members with their public-only Name:N, which is
            // what `member Name:N` resolves without `--all`.
            AddSurface(session.ApiSurface(includeAll: false), map);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error building leverage selectors for {path}: {ex.Message}");
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
                    index.OptimizationOpportunities
                        .Where(opportunity => !IsGeneratedMethod(opportunity.Method, generatedFrameworkTypes)),
                    options)
                .Select(opportunity => new OptimizationOpportunitySummary
                {
                    Member = FormatMethod(opportunity.Method),
                    Candidate = opportunity.CandidateId,
                    Finding = opportunity.SourceFinding,
                    RootReach = opportunity.RootReach,
                    Shape = opportunity.Shape,
                    Operation = opportunity.Operation,
                    Token = FormatToken(opportunity.OperandToken),
                    Evidence = opportunity.Evidence,
                    Fix = opportunity.SafeFixDirection,
                    Confidence = opportunity.Confidence,
                    Loop = IteratesInLoop(opportunity) ? "loop" : "",
                    Allocation = opportunity.RuntimeAllocationType,
                    Path = opportunity.PathContext,
                    PathConfidence = opportunity.PathConfidence,
                    PostDominance = opportunity.PostDominance,
                    IL = opportunity.ILOffset is { } offset ? $"IL_{offset:X4}" : null,
                    Weight = opportunity.Weight,
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

        var ordered = options.TryGetOrderTerms(out var orderTerms, out _)
            ? OrderTriageRows(filtered, orderTerms)
            : OrderByTriagePriority(filtered);
        return options.Top is { } top ? ordered.Take(top) : ordered;
    }

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
        if (predicate.Field == "RootReach")
        {
            if (!int.TryParse(predicate.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return false;
            int compare = opportunity.RootReach.CompareTo(value);
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
        if (field == "RootReach")
            return left.RootReach.CompareTo(right.RootReach);
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
            "Shape" => opportunity.Shape,
            "Operation" => opportunity.Operation,
            "Token" => FormatToken(opportunity.OperandToken),
            "Evidence" => opportunity.Evidence,
            "Fix" => opportunity.SafeFixDirection,
            "Confidence" => opportunity.Confidence,
            "Loop" => IteratesInLoop(opportunity) ? "loop" : "",
            "Allocation" => opportunity.RuntimeAllocationType,
            "Path" => opportunity.PathContext,
            "PathConfidence" => opportunity.PathConfidence,
            "PostDominance" => opportunity.PostDominance,
            "Weight" => opportunity.Weight,
            "IL" => opportunity.ILOffset is { } offset ? $"IL_{offset:X4}" : null,
            "RootReach" => opportunity.RootReach.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

    static string? FormatToken(int? token)
        => token is { } value ? $"0x{value:X8}" : null;

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
            return ScanSwitches(session, path, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning switches in {path}: {ex.Message}");
            return FailedInspection<SwitchInfo>(
                path, MetadataFindings.SwitchDescriptor, ex);
        }
    }

    internal static FindingInspection<SwitchInfo> ScanSwitches(
        AssemblyInspectionSession session,
        string path,
        VerboseLogger logger)
    {
        try
        {
            return MetadataFindings.InspectSwitches(
                session.Switches(),
                FindingSubjectFor(path));
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning switches in {path}: {ex.Message}");
            return FailedInspection<SwitchInfo>(
                path, MetadataFindings.SwitchDescriptor, ex);
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
