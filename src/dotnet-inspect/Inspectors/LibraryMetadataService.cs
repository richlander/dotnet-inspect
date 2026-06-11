using DotnetInspector.Core;
using DotnetInspector.Models;
using System.Reflection.PortableExecutable;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using AssemblyReference = DotnetInspector.Metadata.AssemblyReference;

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
                UseDependenciesView = options.IncludeDependencies
            };

            inspection.AssemblyInfo = pdbContext.ExtractAssemblyInfo(options.IncludeReferences || options.IncludeDependencies || needsAuditSignals);

            // Populate cheap presence flags for fast -s discovery
            var presenceFlags = pdbContext.ScanPresenceFlags();
            inspection.HasExtensionTypes = presenceFlags.HasExtensionTypes;
            inspection.HasPInvokeImports = presenceFlags.HasPInvokeImports;
            inspection.HasUnsafeCode = presenceFlags.HasUnsafeCode;
            inspection.HasRuntimeAsync = presenceFlags.HasRuntimeAsync;
            inspection.HasStateMachineAsync = presenceFlags.HasStateMachineAsync;
            inspection.HasManifestResources = presenceFlags.HasManifestResources;
            inspection.HasOpenTelemetrySupport = presenceFlags.HasOpenTelemetrySupport;
            inspection.HasAspireSupport = presenceFlags.HasAspireSupport;
            inspection.HasAISupport = presenceFlags.HasAISupport;
            inspection.HasDependencyInjectionSupport = presenceFlags.HasDependencyInjectionSupport;
            inspection.HasLoggingSupport = presenceFlags.HasLoggingSupport;
            inspection.HasOptionsSupport = presenceFlags.HasOptionsSupport;
            inspection.HasHostingSupport = presenceFlags.HasHostingSupport;
            inspection.HasHealthChecksSupport = presenceFlags.HasHealthChecksSupport;
            inspection.HasHttpClientSupport = presenceFlags.HasHttpClientSupport;
            inspection.HasAssemblyAttributes = presenceFlags.HasAssemblyAttributes;
            inspection.HasExportedTypeForwarders = presenceFlags.HasTypeForwarders;

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
