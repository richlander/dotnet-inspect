using System.Collections.Concurrent;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using AssemblyReference = DotnetInspector.Metadata.AssemblyReference;

namespace DotnetInspector.Commands;

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

            audit.AssemblyInfo = pdbContext.ExtractAssemblyInfo(options.IncludeReferences || options.TransitiveReferences || options.IncludeDependencies);

            // PE debug directory fields
            audit.HasReproducibleFlag = pdbContext.HasReproducibleFlag;
            audit.HasEmbeddedPdb = pdbContext.HasEmbeddedPdb;
            audit.PdbPath = pdbContext.CodeViewPdbPath;
            audit.HasNormalizedPaths = pdbContext.HasNormalizedPaths;
            audit.NonNormalizedPaths = pdbContext.NonNormalizedPaths;
            audit.IsDeterministic = pdbContext.HasReproducibleFlag && pdbContext.HasNormalizedPaths != false;

            // Build transitive reference tree if requested
            if ((options.TransitiveReferences || options.IncludeDependencies) && audit.AssemblyInfo?.References != null)
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

            if (options.HasAuditTier)
            {
                await AuditAsync(pdbContext, audit, path, packageName, packageVersion, logger, httpClient, isPlatformAssembly, options.IncludeSourcelinkAudit);
            }

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
            await ApiServices.AcquirePdbAsync(pdbContext, httpClient, packageName, packageVersion, isPlatformAssembly, logger.Log);

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
        var urlDocs = new List<SourceDocument>();

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
        var nodes = new List<AssemblyReferenceNode>();

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
                var (platformPath, _, _, error) = Inspectors.PlatformResolver.ResolveAssembly(reference.Name);
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
}
