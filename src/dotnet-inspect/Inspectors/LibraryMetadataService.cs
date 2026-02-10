using DotnetInspector.Models;
using System.Collections.Concurrent;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using AssemblyReference = DotnetInspector.Metadata.AssemblyReference;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Inspects assemblies/libraries: metadata extraction, PDB audit, SourceLink verification,
/// builder inference, and transitive reference resolution.
/// </summary>
internal static class LibraryMetadataService
{
    /// <summary>
    /// Full inspection pipeline for a single assembly.
    /// </summary>
    public static async Task<AssemblyAudit?> InspectAsync(
        string path,
        AssemblyOptions options,
        VerboseLogger logger,
        string? packageName,
        string? packageVersion,
        HttpClient httpClient,
        bool isPlatformAssembly = false)
    {
        logger.Log($"Inspecting: {Path.GetFileName(path)}");

        try
        {
            using var pdbContext = PdbContext.Open(path, logger.Log);

            if (!pdbContext.HasMetadata)
            {
                var nativeInfo = pdbContext.CreateNativeInfo();

                return new AssemblyAudit
                {
                    FileName = Path.GetFileName(path),
                    FileType = "native",
                    AssemblyInfo = nativeInfo
                };
            }

            var audit = new AssemblyAudit
            {
                FileName = Path.GetFileName(path),
                FileType = "dll",
                UseDependenciesView = options.IncludeDependencies
            };

            audit.AssemblyInfo = pdbContext.ExtractAssemblyInfo(options.IncludeReferences || options.IncludeDependencies);

            // PE debug directory fields
            audit.HasReproducibleFlag = pdbContext.HasReproducibleFlag;
            audit.HasEmbeddedPdb = pdbContext.HasEmbeddedPdb;
            audit.PdbPath = pdbContext.CodeViewPdbPath;
            audit.HasNormalizedPaths = pdbContext.HasNormalizedPaths;
            audit.NonNormalizedPaths = pdbContext.NonNormalizedPaths;
            audit.IsDeterministic = pdbContext.HasReproducibleFlag && pdbContext.HasNormalizedPaths != false;

            // Build transitive reference tree if requested
            if (options.IncludeDependencies && audit.AssemblyInfo?.References != null)
            {
                var sourceDir = Path.GetDirectoryName(path);
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                visited.Add(audit.AssemblyInfo.AssemblyName ?? Path.GetFileNameWithoutExtension(path));

                audit.AssemblyInfo.TransitiveReferences = BuildTransitiveReferences(
                    audit.AssemblyInfo.References,
                    sourceDir,
                    visited,
                    logger,
                    deduplicate: options.IncludeDependencies);
            }

            // Scan for extension methods, classified methods, and resources in detailed mode
            if (options.Verbosity == Options.Verbosity.Detailed)
            {
                audit.ExtensionMethods = ScanExtensionMethods(path, logger);
                ScanClassifiedMethods(path, audit, logger);
                audit.Resources = ScanResources(path, logger);
            }

            await AuditAsync(pdbContext, audit, path, packageName, packageVersion, logger, httpClient, isPlatformAssembly, options.IncludeSourcelinkAudit);

            return audit;
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
        PdbContext pdbContext,
        AssemblyAudit audit,
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        VerboseLogger logger,
        HttpClient httpClient,
        bool isPlatformAssembly = false,
        bool includeSourcelinkAudit = false)
    {
        if (pdbContext.HasPdb)
        {
            audit.PdbFormat = pdbContext.PdbFormat;
            audit.PdbLocation = pdbContext.PdbLocation;
            audit.HasSourceLink = pdbContext.HasSourceLink;
            audit.SourceLinkJson = pdbContext.SourceLinkJson;
        }

        if (!pdbContext.HasPdb && pdbContext.WindowsPdbDetected)
        {
            audit.WindowsPdbDetected = true;
            audit.PdbFormat = pdbContext.PdbFormat;
            audit.PdbLocation = pdbContext.PdbLocation;
        }

        // If no local PDB, try downloading
        if (!pdbContext.HasPdb && !pdbContext.WindowsPdbDetected)
        {
            await SourceEnricher.AcquirePdbAsync(pdbContext, httpClient, packageName, packageVersion, isPlatformAssembly, logger.Log);

            if (pdbContext.HasPdb)
            {
                audit.PdbFormat = pdbContext.PdbFormat;
                audit.PdbLocation = pdbContext.PdbLocation;
                audit.SymbolServer = pdbContext.SymbolServer;
                audit.HasSourceLink = pdbContext.HasSourceLink;
                audit.SourceLinkJson = pdbContext.SourceLinkJson;
            }
            else if (pdbContext.WindowsPdbDetected)
            {
                audit.WindowsPdbDetected = true;
                audit.PdbFormat = "Windows";
            }
        }

        // Determine reason for missing SourceLink
        if (!audit.HasSourceLink)
        {
            if (audit.WindowsPdbDetected)
            {
                audit.SourceLinkUnavailableReason = "Windows PDB";
            }
            else if (audit.PdbLocation == null && audit.PdbPath != null)
            {
                audit.SourceLinkUnavailableReason = "no symbols";
            }
            else if (!audit.HasEmbeddedPdb && audit.PdbPath != null)
            {
                audit.SourceLinkUnavailableReason = "external PDB not found";
            }
        }

        audit.Builder = InferBuilder(audit);

        // SourceLink audit: verify that all source files are accessible
        if (includeSourcelinkAudit && pdbContext.HasPdb && audit.HasSourceLink && audit.SourceLinkJson != null)
        {
            logger.Log("Running strict source verification...");
            await VerifySourceAccessibilityAsync(pdbContext, audit, httpClient, logger);
        }
    }

    /// <summary>
    /// Verifies that all source files in the PDB are accessible via SourceLink or embedded.
    /// </summary>
    public static async Task VerifySourceAccessibilityAsync(
        PdbContext pdbContext,
        AssemblyAudit audit,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        var documents = pdbContext.EnumerateSourceDocuments().ToList();
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

        audit.TotalSourceFiles = documents.Count;
        audit.AccessibleSourceFiles = accessibleCount;
        audit.EmbeddedSourceFiles = embeddedFiles;
        audit.AllSourcesAccessible = missingFiles.IsEmpty;
        audit.MissingSourceFiles = missingFiles.IsEmpty ? null : [.. missingFiles.OrderBy(f => f)];

        logger.Log($"Source coverage: {accessibleCount + embeddedFiles}/{documents.Count} files accessible");
    }

    /// <summary>
    /// Infers who built the assembly based on symbol availability and SourceLink.
    /// </summary>
    public static string? InferBuilder(AssemblyAudit audit)
    {
        var company = audit.AssemblyInfo?.Company;
        bool isMicrosoftAssembly = company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true;

        if (!isMicrosoftAssembly)
        {
            return null;
        }

        if (audit.SymbolServer == "msdl.microsoft.com" && audit.HasSourceLink)
        {
            return "Microsoft";
        }

        if (audit.HasSourceLink && audit.SourceLinkJson != null)
        {
            if (audit.SourceLinkJson.Contains("github.com/dotnet/", StringComparison.OrdinalIgnoreCase) ||
                audit.SourceLinkJson.Contains("raw.githubusercontent.com/dotnet/", StringComparison.OrdinalIgnoreCase))
            {
                return "Microsoft";
            }
        }

        if (audit.SourceLinkUnavailableReason == "no symbols")
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
    private static List<ExtensionMethodSummary>? ScanExtensionMethods(string path, VerboseLogger logger)
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
    private static void ScanClassifiedMethods(string path, AssemblyAudit audit, VerboseLogger logger)
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

            audit.UnsafeMethods = unsafe_.Count > 0 ? unsafe_ : null;
            audit.PInvokeMethods = pinvoke.Count > 0 ? pinvoke : null;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning classified methods in {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Scans an assembly for manifest resources.
    /// </summary>
    private static List<ResourceSummary>? ScanResources(string path, VerboseLogger logger)
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
}
