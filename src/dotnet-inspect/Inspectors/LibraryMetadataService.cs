using DotnetInspector.Models;
using System.Collections.Concurrent;
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
        AssemblyOptions options,
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
                    FileSize = AssemblyDetailScanner.GetFileSize(path)
                };

                nativeAudit.HasReproducibleFlag = pdbContext.HasReproducibleFlag;
                nativeAudit.IsDeterministic = pdbContext.HasReproducibleFlag;

                return nativeAudit;
            }

            var inspection = new LibraryInspection
            {
                FileName = Path.GetFileName(path),
                FileType = "dll",
                UseDependenciesView = options.IncludeDependencies
            };

            inspection.AssemblyInfo = pdbContext.ExtractAssemblyInfo(options.IncludeReferences || options.IncludeDependencies);

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
                // Fallback for non-pipeline callers
                inspection.ExtensionMethods = ScanExtensionMethods(path, logger);
                ScanClassifiedMethods(path, inspection, logger);
                inspection.Resources = ScanResources(path, logger);
                ScanCustomAttributes(path, inspection, logger);
                ScanTypeForwarders(path, inspection, logger);
            }

            inspection.FileSize = AssemblyDetailScanner.GetFileSize(path);

            await AuditAsync(service, inspection, path, packageName, packageVersion, logger, httpClient, isPlatformAssembly, options.IncludeSourcelinkAudit);

            return inspection;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// PDB acquisition, SourceLink detection, and builder inference.
    /// </summary>
    public static async Task AuditAsync(
        SourceLinkService service,
        LibraryInspection inspection,
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        VerboseLogger logger,
        HttpClient httpClient,
        bool isPlatformAssembly = false,
        bool includeSourcelinkAudit = false)
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

        // If no local PDB, try downloading
        if (!pdbContext.HasPdb && !pdbContext.WindowsPdbDetected)
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
        if (!inspection.HasSourceLink)
        {
            if (inspection.WindowsPdbDetected)
            {
                inspection.SourceLinkUnavailableReason = "Windows PDB";
            }
            else if (inspection.PdbLocation == null && inspection.PdbPath != null)
            {
                inspection.SourceLinkUnavailableReason = "no symbols";
            }
            else if (!inspection.HasEmbeddedPdb && inspection.PdbPath != null)
            {
                inspection.SourceLinkUnavailableReason = "external PDB not found";
            }
        }

        inspection.Builder = InferBuilder(inspection);

        // SourceLink inspection: verify that all source files are accessible
        if (includeSourcelinkAudit && pdbContext.HasPdb && service.HasSourceLink && service.SourceLinkJson != null)
        {
            logger.Log("Running strict source verification...");
            await VerifySourceAccessibilityAsync(service, inspection, httpClient, logger);
        }
    }

    /// <summary>
    /// Verifies that all source files in the PDB are accessible via SourceLink or embedded.
    /// </summary>
    public static async Task VerifySourceAccessibilityAsync(
        SourceLinkService service,
        LibraryInspection inspection,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        var documents = service.GetTrackedFiles();
        int embeddedFiles = 0;
        int accessibleCount = 0;
        var missingFiles = new ConcurrentBag<string>();
        List<SourceDocument> urlDocs = [];

        foreach (var doc in documents)
        {
            if (doc.IsEmbedded) { embeddedFiles++; continue; }
            if (doc.ResolvedUrl == null) { missingFiles.Add(doc.FilePath); continue; }
            urlDocs.Add(doc);
        }

        await Parallel.ForEachAsync(urlDocs,
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            async (doc, ct) =>
            {
                using var response = await HttpRetryHelper.HeadWithRetryAsync(
                    httpClient, doc.ResolvedUrl!, log: logger.Log, cancellationToken: ct);
                if (response != null)
                    Interlocked.Increment(ref accessibleCount);
                else
                    missingFiles.Add(doc.FilePath);
            });

        inspection.TotalSourceFiles = documents.Count;
        inspection.AccessibleSourceFiles = accessibleCount;
        inspection.EmbeddedSourceFiles = embeddedFiles;
        inspection.AllSourcesAccessible = missingFiles.IsEmpty;
        inspection.MissingSourceFiles = missingFiles.IsEmpty ? null : [.. missingFiles.OrderBy(f => f)];

        logger.Log($"Source coverage: {accessibleCount + embeddedFiles}/{documents.Count} files accessible");
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
            var extensions = ExtensionMethodScanner.FindAllExtensions(stream);

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
            var classified = MethodClassificationScanner.Scan(stream);
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
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning classified methods in {path}: {ex.Message}");
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
            var resources = ResourceScanner.Scan(stream);
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
