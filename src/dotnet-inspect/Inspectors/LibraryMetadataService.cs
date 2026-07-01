using DotnetInspector.Core;
using DotnetInspector.Models;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using AssemblyReference = ILInspector.Metadata.AssemblyReference;
using Analysis = ILInspector.Analysis;

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
                });
            }
            else if (options.Verbosity == Options.Verbosity.Detailed)
            {
                // Fallback for non-pipeline callers — open the assembly once for all five scans.
                try
                {
                    using var stream = File.OpenRead(path);
                    using var peReader = new PEReader(stream);
                    inspection.ExtensionMethods = ScanExtensionMethods(peReader, path, logger);
                    ScanClassifiedMethods(peReader, path, inspection, logger);
                    inspection.Resources = ScanResources(peReader, path, logger);
                    ScanCustomAttributes(peReader, path, inspection, logger);
                    inspection.UnionTypes = ScanUnionTypes(peReader, path, logger);
                    ScanTypeForwarders(peReader, path, inspection, logger);
                }
                catch (Exception ex)
                {
                    logger.Log($"Warning: Error opening {path} for scanning: {ex.Message}");
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

            if (needsAuditSignals)
                AuditSignalBuilder.PopulateLibraryAudit(path, inspection, logger);

            if (runHeadAudit && service.HasSourceLink && pdbContext.HasPdb)
            {
                // SourceLink URLs are untrusted: probe them with the SSRF-hardened client, not the
                // shared client used for trusted NuGet/symbol endpoints.
                await SourceAuditService.PopulateAsync(
                    service, inspection, DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch, logger);
                if (needsAuditSignals)
                    AuditSignalBuilder.PopulateLibraryAudit(path, inspection, logger);
            }

            if (runIntegrity && service.HasSourceLink && pdbContext.HasPdb)
            {
                await SourceIntegrityService.PopulateAsync(service, inspection, logger);
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
    /// Scans an assembly for all extension methods and returns collapsed summaries.
    /// </summary>
    internal static List<ExtensionMethodSummary>? ScanExtensionMethods(string path, VerboseLogger logger)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            return ScanExtensionMethods(peReader, path, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning extensions in {path}: {ex.Message}");
            return null;
        }
    }

    internal static List<ExtensionMethodSummary>? ScanExtensionMethods(PEReader peReader, string path, VerboseLogger logger)
    {
        try
        {
            var extensions = ExtensionMethodScanner.FindAllExtensions(peReader);

            var collapsed = extensions
                .GroupBy(e => (e.MethodName, e.Kind, e.ExtensionClass, e.ExtendedType))
                .Select(g =>
                {
                    var count = g.Count();
                    return new ExtensionMethodSummary
                    {
                        MethodName = g.Key.MethodName,
                        ExtendedType = g.Key.ExtendedType,
                        ExtensionClass = g.Key.ExtensionClass,
                        Kind = g.Key.Kind,
                        Overloads = count > 1 ? count : null
                    };
                })
                .OrderBy(e => e.ExtendedType)
                .ThenBy(e => e.MethodName)
                .ToList();

            return collapsed.Count > 0 ? collapsed : null;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning extensions in {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Scans an assembly for unsafe and P/Invoke methods.
    /// </summary>
    internal static void ScanClassifiedMethods(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            ScanClassifiedMethods(peReader, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning classified methods in {path}: {ex.Message}");
        }
    }

    internal static void ScanClassifiedMethods(PEReader peReader, string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            var classified = MethodClassificationScanner.Scan(peReader);
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
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning classified methods in {path}: {ex.Message}");
        }
    }

    internal static List<UnsafeMemberSummary>? ScanUnsafeMembers(string path, VerboseLogger logger)
    {
        try
        {
            var index = Analysis.LibraryBodyIndex.Open(path);
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
    internal static List<MethodLeverageSummary>? ScanTopLeverage(string path, VerboseLogger logger)
    {
        try
        {
            var index = Analysis.LibraryBodyIndex.Open(path);
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
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return map;

            // All-members first (covers non-public, numbered as `--all` drilling resolves them).
            AddSurface(ILInspector.Metadata.ApiSurfaceExtractor.Extract(peReader, includeAll: true), map);
            // Default surface overwrites public members with their public-only Name:N, which is
            // what `member Name:N` resolves without `--all`.
            AddSurface(ILInspector.Metadata.ApiSurfaceExtractor.Extract(peReader, includeAll: false), map);
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
        string path,
        VerboseLogger logger,
        PerformanceTriageOptions? options = null)
    {
        try
        {
            var index = Analysis.LibraryBodyIndex.Open(path);
            var generatedFrameworkTypes = index.GeneratedFrameworkTypeNames;
            var rows = FilterAndOrderTriageOpportunities(
                    index.OptimizationOpportunities
                        .Where(opportunity => !IsGeneratedMethod(opportunity.Method, generatedFrameworkTypes)),
                    options)
                .Select(opportunity => new OptimizationOpportunitySummary
                {
                    Member = FormatMethod(opportunity.Method),
                    RootReach = opportunity.RootReach,
                    Shape = opportunity.Shape,
                    Evidence = opportunity.Evidence,
                    Fix = opportunity.SafeFixDirection,
                    Confidence = opportunity.Confidence,
                    Loop = opportunity.InLoop ? "loop" : "",
                    Allocation = opportunity.RuntimeAllocationType,
                    Path = opportunity.PathContext,
                    IL = opportunity.ILOffset is { } offset ? $"IL_{offset:X4}" : null,
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
            .OrderByDescending(opportunity => opportunity.InLoop)
            .ThenByDescending(opportunity => ConfidenceRank(opportunity.Confidence))
            .ThenByDescending(opportunity => opportunity.RootReach)
            .ThenBy(opportunity => opportunity.Method.DeclaringType.ToQualifiedDisplayString(), StringComparer.Ordinal)
            .ThenBy(opportunity => opportunity.Method.Name, StringComparer.Ordinal)
            .ThenBy(opportunity => opportunity.ILOffset ?? -1)
            .ThenBy(opportunity => opportunity.Shape, StringComparer.Ordinal);

    // Triage ordering weight for a confidence label (high allocations are the surest pay-dirt).
    static int ConfidenceRank(string confidence)
    {
        if (confidence.Equals("high", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (confidence.Equals("medium", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 0;
    }

    internal static IEnumerable<Analysis.OptimizationOpportunity> FilterAndOrderTriageOpportunities(
        IEnumerable<Analysis.OptimizationOpportunity> opportunities,
        PerformanceTriageOptions? options)
    {
        options ??= PerformanceTriageOptions.Default;
        var filtered = opportunities;
        if (options.LoopOnly)
            filtered = filtered.Where(opportunity => opportunity.InLoop);
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

        var ordered = OrderByTriagePriority(filtered);
        return options.Top is { } top ? ordered.Take(top) : ordered;
    }

    internal static List<IntegrationSignal>? ScanOpenTelemetry(string path, VerboseLogger logger)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            return ScanOpenTelemetry(peReader, path, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning OpenTelemetry support in {path}: {ex.Message}");
            return null;
        }
    }

    internal static void ScanIntegrations(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            ScanIntegrations(peReader, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning ecosystem integrations in {path}: {ex.Message}");
        }
    }

    internal static void ScanIntegrations(PEReader peReader, string path, LibraryInspection inspection, VerboseLogger logger)
    {
        LibraryIntegrationCatalog.OpenTelemetry.SetSignals(inspection, ScanOpenTelemetry(peReader, path, logger));
        var signals = EcosystemIntegrationScanner.Scan(peReader);
        foreach (var descriptor in LibraryIntegrationCatalog.EcosystemScanned)
            descriptor.SetSignals(inspection, SelectIntegrationSignals(signals, descriptor.Name));
        inspection.Integrations = BuildIntegrations(inspection);
    }

    internal static void ScanIntegrationOpportunities(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            ScanIntegrationOpportunities(peReader, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning integration opportunities in {path}: {ex.Message}");
        }
    }

    internal static void ScanIntegrationOpportunities(PEReader peReader, string path, LibraryInspection inspection, VerboseLogger logger)
    {
        if (inspection.Integrations == null)
            ScanIntegrations(peReader, path, inspection, logger);

        var existing = new HashSet<string>(
            inspection.Integrations?.Select(integration => integration.Integration) ?? [],
            StringComparer.Ordinal);
        var gaps = IntegrationOpportunityScanner.Scan(peReader, existing);
        inspection.IntegrationOpportunities = gaps.Count > 0 ? gaps : null;
    }

    internal static List<IntegrationSignal>? ScanOpenTelemetry(PEReader peReader, string path, VerboseLogger logger)
    {
        try
        {
            var signals = OpenTelemetryScanner.Scan(peReader)
                .Select(s => new IntegrationSignal(s.Kind, s.Name, s.Shape))
                .ToList();

            return signals.Count > 0 ? signals : null;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning OpenTelemetry support in {path}: {ex.Message}");
            return null;
        }
    }

    private static List<IntegrationSignal>? SelectIntegrationSignals(
        List<EcosystemIntegrationSignalInfo> signals,
        string integration)
    {
        var selected = signals
            .Where(s => s.Integration.Equals(integration, StringComparison.Ordinal))
            .Select(s => new IntegrationSignal(s.Kind, s.Name, s.Shape))
            .ToList();

        return selected.Count > 0 ? selected : null;
    }

    private static List<IntegrationSummary>? BuildIntegrations(LibraryInspection inspection)
    {
        List<IntegrationSummary> integrations = [];
        foreach (var descriptor in LibraryIntegrationCatalog.All)
            AddIntegration(integrations, descriptor, descriptor.GetSignals(inspection));
        return integrations.Count > 0 ? integrations : null;
    }

    private static void AddIntegration(
        List<IntegrationSummary> integrations,
        LibraryIntegrationDescriptor descriptor,
        List<IntegrationSignal>? signals)
    {
        if (signals is { Count: > 0 })
            integrations.Add(new IntegrationSummary(descriptor.Name, descriptor.CountRenderedRows(signals)));
    }

    internal static void ScanInfoCounts(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        // Open the assembly once and share the reader across all five scans instead of re-opening
        // the same file per scan.
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            inspection.ExtensionMethods ??= ScanExtensionMethods(peReader, path, logger);
            ScanClassifiedMethods(peReader, path, inspection, logger);
            inspection.Resources ??= ScanResources(peReader, path, logger);
            ScanCustomAttributes(peReader, path, inspection, logger);
            ScanTypeForwarders(peReader, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error opening {path} for scanning: {ex.Message}");
        }
    }

    /// <summary>
    /// Scans an assembly for manifest resources.
    /// </summary>
    internal static List<ResourceSummary>? ScanResources(string path, VerboseLogger logger)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            return ScanResources(peReader, path, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning resources in {path}: {ex.Message}");
            return null;
        }
    }

    internal static List<ResourceSummary>? ScanResources(PEReader peReader, string path, VerboseLogger logger)
    {
        try
        {
            var resources = ResourceScanner.Scan(peReader);
            if (resources.Count == 0) return null;

            return resources
                .Select(r => new ResourceSummary
                {
                    Name = r.Name,
                    Visibility = r.IsPublic ? "public" : "private",
                    Size = r.Size
                })
                .OrderBy(r => r.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning resources in {path}: {ex.Message}");
            return null;
        }
    }

    internal static List<SwitchInfo>? ScanSwitches(string path, VerboseLogger logger)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            return ScanSwitches(peReader, path, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning switches in {path}: {ex.Message}");
            return null;
        }
    }

    internal static List<SwitchInfo>? ScanSwitches(PEReader peReader, string path, VerboseLogger logger)
    {
        try
        {
            var switches = SwitchScanner.Scan(peReader);
            return switches.Count > 0 ? switches : null;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning switches in {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Scans an assembly for custom attributes (assembly-level and module-level).
    /// </summary>
    internal static void ScanCustomAttributes(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            ScanCustomAttributes(peReader, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning custom attributes in {path}: {ex.Message}");
        }
    }

    internal static void ScanCustomAttributes(PEReader peReader, string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            var attrs = AssemblyDetailScanner.ScanCustomAttributes(peReader);
            if (attrs.Count > 0)
            {
                inspection.CustomAttributes = attrs
                    .Select(a => new CustomAttributeSummary
                    {
                        Name = a.Name,
                        Target = a.Target,
                        Value = a.Value
                    })
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning custom attributes in {path}: {ex.Message}");
        }
    }

    internal static List<UnionTypeSummary>? ScanUnionTypes(string path, VerboseLogger logger)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            return ScanUnionTypes(peReader, path, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning union types in {path}: {ex.Message}");
            return null;
        }
    }

    internal static List<UnionTypeSummary>? ScanUnionTypes(PEReader peReader, string path, VerboseLogger logger)
    {
        try
        {
            var reader = peReader.GetMetadataReader();
            var results = new List<UnionTypeSummary>();

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                if (!AttributeReader.HasAttribute(reader, typeDef.GetCustomAttributes(), KnownAttributeNames.UnionAttribute))
                    continue;

                var context = GenericContext.ForType(reader, typeDef);
                bool implementsIUnion = ImplementsIUnion(reader, typeDef, context);
                var caseTypes = UnionCaseTypes(reader, typeDef).ToList();

                results.Add(new UnionTypeSummary
                {
                    TypeName = reader.GetFullTypeName(typeDef),
                    Kind = TypeKind(reader, typeDef),
                    ImplementsIUnion = implementsIUnion,
                    CaseTypes = caseTypes
                });
            }

            return results.Count == 0 ? null : results;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning union types in {path}: {ex.Message}");
            return null;
        }
    }

    static bool ImplementsIUnion(MetadataReader reader, TypeDefinition typeDef, GenericContext context)
    {
        foreach (var interfaceHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = reader.GetInterfaceImplementation(interfaceHandle);
            if (TypeResolver.GetTypeName(reader, iface.Interface, context) == "System.Runtime.CompilerServices.IUnion")
                return true;
        }

        return false;
    }

    static IEnumerable<string> UnionCaseTypes(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != ".ctor")
                continue;
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                continue;
            if ((method.Attributes & MethodAttributes.Static) != 0)
                continue;

            MethodSignature<string> signature;
            try
            {
                signature = method.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForMethod(reader, typeDef, method));
            }
            catch
            {
                continue;
            }

            if (signature.ParameterTypes.Length == 1)
                yield return signature.ParameterTypes[0];
        }
    }

    static string TypeKind(MetadataReader reader, TypeDefinition typeDef)
    {
        var attrs = typeDef.Attributes;
        if ((attrs & TypeAttributes.Interface) != 0)
            return "interface";
        if (TypeResolver.GetTypeName(reader, typeDef.BaseType) == "System.ValueType")
            return "struct";
        return "class";
    }

    /// <summary>
    /// Scans an assembly for type forwarders.
    /// </summary>
    internal static void ScanTypeForwarders(string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            ScanTypeForwarders(peReader, path, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning type forwarders in {path}: {ex.Message}");
        }
    }

    internal static void ScanTypeForwarders(PEReader peReader, string path, LibraryInspection inspection, VerboseLogger logger)
    {
        try
        {
            var forwarders = AssemblyDetailScanner.ScanTypeForwarders(peReader);
            if (forwarders.Count > 0)
            {
                inspection.TypeForwarders = forwarders
                    .Select(f => new TypeForwarderSummary
                    {
                        TypeName = f.TypeName,
                        TargetAssembly = f.TargetAssembly
                    })
                    .OrderBy(f => f.TypeName)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning type forwarders in {path}: {ex.Message}");
        }
    }
}
