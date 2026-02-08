using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using AssemblyReference = DotnetInspector.Metadata.AssemblyReference;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects a single .NET assembly.
/// </summary>
public class AssemblyCommand
{
    public static async Task<int> ExecuteAsync(string? assemblyPath, AssemblyOptions options)
    {
        // Check for valid input source
        if (string.IsNullOrEmpty(assemblyPath) && 
            string.IsNullOrEmpty(options.PackagePath) && 
            string.IsNullOrEmpty(options.PlatformAssembly))
        {
            Console.Error.WriteLine("Error: Assembly path, --package, or --platform required.");
            Console.Error.WriteLine("Run 'dotnet-inspect assembly --help' for usage.");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        try
        {
            // Parse package name and version for symbol package download
            string? packageName = null;
            string? packageVersion = null;
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                (packageName, packageVersion) = PackageReferenceParser.ParsePackageReference(options.PackagePath);
            }

            if (!string.IsNullOrEmpty(options.PlatformAssembly))
            {
                // Resolve platform assembly - use runtime assemblies for full debug info
                var (resolvedPath, framework, version, error) = PlatformResolver.ResolveAssembly(
                    options.PlatformAssembly,
                    options.PlatformFramework,
                    packsDirectory: null,
                    useRuntimeAssemblies: true);  // Use runtime for audit (has debug info)

                if (error != null)
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return 1;
                }

                logger.Log($"Using platform runtime assembly: {framework} {version}");
                
                var audit = await InspectAssemblyAsync(resolvedPath!, options, logger, null, null, context.HttpClient, isPlatformAssembly: true);
                if (audit == null)
                {
                    Console.Error.WriteLine($"Error: Could not read assembly: {resolvedPath}");
                    return 1;
                }

                OutputFormatter.WriteAssemblyResult(audit, options);
                return 0;
            }
            else if (!string.IsNullOrEmpty(options.PackagePath))
            {
                // Extract from package
                var extractResult = await ExtractFromPackageAsync(assemblyPath, options.PackagePath, options.Tfm, logger, context.HttpClient);
                if (extractResult == null)
                {
                    return 1;
                }

                var (assemblyPaths, extractPath, extractTempDir, nupkgPath) = extractResult.Value;
                tempDir = extractTempDir;

                // Verify package signature if --audit is specified and nupkg is available
                SignatureVerificationResult? signatureResult = null;
                if (options.IncludeAudit && nupkgPath != null)
                {
                    logger.Log($"Verifying package signature: {Path.GetFileName(nupkgPath)}");
                    signatureResult = await SignatureVerifier.VerifyAsync(nupkgPath);
                }

                // Inspect all assemblies
                bool first = true;
                foreach (var targetPath in assemblyPaths)
                {
                    // Extract version from path if not already known
                    var version = packageVersion ?? (packageName != null ? ExtractVersionFromPath(targetPath, packageName) : null);

                    var audit = await InspectAssemblyAsync(targetPath, options, logger, packageName, version, context.HttpClient);
                    if (audit == null)
                    {
                        logger.Log($"Warning: Could not read assembly: {Path.GetFileName(targetPath)}");
                        continue;
                    }

                    // Add signature verification results
                    if (signatureResult != null)
                    {
                        audit.Publisher = signatureResult.Publisher;
                        audit.PublisherVerified = signatureResult.AuthorVerified;
                        audit.RepositoryVerified = signatureResult.RepositoryVerified;
                        audit.SignatureStatus = signatureResult.StatusMessage;
                    }

                    if (!first)
                    {
                        Console.WriteLine();
                    }
                    first = false;

                    OutputFormatter.WriteAssemblyResult(audit, options);
                }

                return 0;
            }
            else
            {
                // Load from filesystem
                if (!File.Exists(assemblyPath))
                {
                    Console.Error.WriteLine($"Error: File not found: {assemblyPath}");
                    return 1;
                }

                var audit = await InspectAssemblyAsync(assemblyPath!, options, logger, null, null, context.HttpClient);
                if (audit == null)
                {
                    Console.Error.WriteLine($"Error: Could not read assembly: {assemblyPath}");
                    return 1;
                }

                OutputFormatter.WriteAssemblyResult(audit, options);
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
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

    private static async Task<(List<string> assemblyPaths, string extractPath, string? tempDir, string? nupkgPath)?> ExtractFromPackageAsync(string? assemblyName, string packageSource, string? tfm, VerboseLogger logger, HttpClient httpClient)
    {
        // Check if it's a local file or a NuGet package name
        bool isLocalFile = packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

        string tempDir;
        string extractPath;
        string? nupkgPath = null;

        if (isLocalFile)
        {
            if (!File.Exists(packageSource))
            {
                Console.Error.WriteLine($"Error: Package not found: {packageSource}");
                return null;
            }

            tempDir = Path.Combine(Path.GetTempPath(), $"inspect-assembly-{Guid.NewGuid():N}");
            extractPath = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(tempDir);

            logger.Log($"Extracting package: {Path.GetFileName(packageSource)}");
            ZipFile.ExtractToDirectory(packageSource, extractPath);
            nupkgPath = packageSource;
        }
        else
        {
            // Treat as NuGet package name - download from nuget.org
            // Support format: PackageName or PackageName@version
            var client = httpClient;

            string packageName;
            string? version;

            int atIndex = packageSource.IndexOf('@');
            if (atIndex > 0)
            {
                packageName = packageSource[..atIndex].ToLowerInvariant();
                version = packageSource[(atIndex + 1)..].ToLowerInvariant();
                logger.Log($"Using specified version: {version}");
            }
            else
            {
                packageName = packageSource.ToLowerInvariant();
                version = await GetLatestVersionAsync(client, packageName, logger);
                if (version == null)
                {
                    Console.Error.WriteLine($"Error: Package '{packageSource}' not found on nuget.org");
                    return null;
                }
            }

            // Check NuGet cache first
            var cachedPath = NuGetCache.TryGetCachedPackage(packageName, version);
            if (cachedPath != null && NuGetCache.IsCachedPackageValid(cachedPath))
            {
                logger.Log($"Using cached package: {cachedPath}");
                extractPath = cachedPath;
                tempDir = null!; // Will be handled by caller
                // Try to find .nupkg in cache
                var expectedNupkg = Path.Combine(cachedPath, $"{packageName}.{version}.nupkg");
                nupkgPath = File.Exists(expectedNupkg) ? expectedNupkg : null;
            }
            else
            {
                tempDir = Path.Combine(Path.GetTempPath(), $"inspect-assembly-{Guid.NewGuid():N}");
                extractPath = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(tempDir);

                string nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/{version}/{packageName}.{version}.nupkg";
                logger.Log($"Downloading: {packageName} {version}");

                byte[]? packageBytes = await HttpRetryHelper.GetBytesWithRetryAsync(client, nupkgUrl);
                if (packageBytes == null)
                {
                    Console.Error.WriteLine($"Error: Package '{packageName}' version '{version}' not found on nuget.org.");
                    Console.Error.WriteLine("Use 'dotnet-inspect package <name> --versions' to list available versions.");
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                    return null;
                }

                nupkgPath = Path.Combine(tempDir, $"{packageName}.{version}.nupkg");
                await File.WriteAllBytesAsync(nupkgPath, packageBytes);
                ZipFile.ExtractToDirectory(nupkgPath, extractPath);
                logger.Log("Package downloaded successfully.");

                // Cache the package for future use
                var newCachePath = NuGetCache.CachePackage(extractPath, packageName, version);
                if (newCachePath != null)
                {
                    logger.Log($"Cached to: {newCachePath}");
                }
            }
        }

        // Find DLLs in the extracted package
        string[] allDlls = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);

        // If --tfm is specified, find assembly by TFM
        if (!string.IsNullOrEmpty(tfm))
        {
            var tfmAssembly = TfmSelector.FindAssemblyByTfm(extractPath, tfm);
            if (tfmAssembly == null)
            {
                Console.Error.WriteLine($"Error: No assembly found for TFM '{tfm}'.");
                Console.Error.WriteLine("Available TFMs:");
                var tfms = allDlls
                    .Select(d => ExtractTfmFromPath(Path.GetRelativePath(extractPath, d).Replace('\\', '/')))
                    .Where(t => t != null)
                    .Distinct()
                    .OrderByDescending(t => GetTfmPriority(t!));
                foreach (var t in tfms)
                {
                    Console.Error.WriteLine($"  {t}");
                }
                if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }
            logger.Log($"Using TFM: {tfm}");
            return ([tfmAssembly], extractPath, tempDir, nupkgPath);
        }

        // If no assembly name specified, return all assemblies from tools or lib directory
        if (string.IsNullOrEmpty(assemblyName))
        {
            var toolsDir = Path.Combine(extractPath, "tools");
            var libDir = Path.Combine(extractPath, "lib");

            string[] candidates;
            if (Directory.Exists(toolsDir))
            {
                candidates = Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories);
            }
            else if (Directory.Exists(libDir))
            {
                candidates = Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories);
            }
            else
            {
                candidates = allDlls;
            }

            if (candidates.Length == 0)
            {
                Console.Error.WriteLine("Error: No DLLs found in package.");
                if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }

            // Return all assemblies, sorted by path
            var assemblyPaths = candidates.OrderBy(f => f).ToList();
            return (assemblyPaths, extractPath, tempDir, nupkgPath);
        }

        // Normalize the assembly path for comparison
        string normalizedAssemblyName = assemblyName.Replace('\\', '/');

        // First try to match by relative path (for disambiguation)
        string[] matchingFiles = allDlls
            .Where(f =>
            {
                string relativePath = Path.GetRelativePath(extractPath, f).Replace('\\', '/');
                return relativePath.Equals(normalizedAssemblyName, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        // If no exact path match, try matching by filename
        if (matchingFiles.Length == 0)
        {
            matchingFiles = allDlls
                .Where(f => Path.GetFileName(f).Equals(assemblyName, StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(f).Equals(assemblyName + ".dll", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (matchingFiles.Length == 0)
        {
            Console.Error.WriteLine($"Error: Assembly '{assemblyName}' not found in package.");
            Console.Error.WriteLine("Use 'dotnet-inspect package <name> --files' to list available assemblies.");
            if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }

        if (matchingFiles.Length > 1)
        {
            Console.Error.WriteLine($"Multiple matches found for '{assemblyName}':");
            foreach (var f in matchingFiles)
            {
                Console.Error.WriteLine($"  {Path.GetRelativePath(extractPath, f)}");
            }
            Console.Error.WriteLine("Specify the full relative path to disambiguate.");
            if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }

        logger.Log($"Found: {Path.GetRelativePath(extractPath, matchingFiles[0])}");
        return ([matchingFiles[0]], extractPath, tempDir, nupkgPath);
    }

    private static async Task<string?> GetLatestVersionAsync(HttpClient client, string packageName, VerboseLogger logger)
    {
        try
        {
            string indexUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/index.json";
            logger.Log($"Fetching versions from: {indexUrl}");

            string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, indexUrl);
            if (json == null)
                return null;
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("versions", out var versions))
            {
                var versionList = versions.EnumerateArray().Select(v => v.GetString()).ToList();
                if (versionList.Count > 0)
                {
                    // Prefer stable versions (those without a hyphen)
                    var stableVersions = versionList.Where(v => v != null && !v.Contains('-')).ToList();
                    string? latest = stableVersions.Count > 0 ? stableVersions[^1] : versionList[^1];
                    logger.Log($"Latest version: {latest}");
                    return latest;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            logger.Log($"Error fetching versions: {ex.Message}");
        }

        return null;
    }

    private static async Task<AssemblyAudit?> InspectAssemblyAsync(
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
            using FileStream stream = File.OpenRead(path);
            using PEReader peReader = new(stream);

            if (!peReader.HasMetadata)
            {
                // Native binary - still provide basic info
                var nativeInfo = AssemblyInspector.CreateNativeInfo(peReader);
                bool isNativeAot = AssemblyInspector.DetectNativeAot(peReader);
                nativeInfo.IsNativeAot = isNativeAot;
                nativeInfo.CompilationType = isNativeAot ? "NativeAOT" : "Native";

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
                FileType = "dll"
            };

            // Always extract basic assembly info
            audit.AssemblyInfo = AssemblyInspector.ExtractAssemblyInfo(peReader, options.IncludeReferences || options.TransitiveReferences);

            // Build transitive reference tree if requested
            if (options.TransitiveReferences && audit.AssemblyInfo?.References != null)
            {
                var sourceDir = Path.GetDirectoryName(path);
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                visited.Add(audit.AssemblyInfo.AssemblyName ?? Path.GetFileNameWithoutExtension(path));

                audit.AssemblyInfo.TransitiveReferences = BuildTransitiveReferences(
                    audit.AssemblyInfo.References,
                    sourceDir,
                    visited,
                    logger);
            }

            // Audit if requested
            if (options.IncludeAudit)
            {
                await AuditAssemblyAsync(peReader, audit, path, packageName, packageVersion, logger, httpClient, isPlatformAssembly, options.StrictAudit);
            }

            return audit;
        }
        catch
        {
            return null;
        }
    }

    private static async Task AuditAssemblyAsync(
        PEReader peReader,
        AssemblyAudit audit,
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        VerboseLogger logger,
        HttpClient httpClient,
        bool isPlatformAssembly = false,
        bool strictAudit = false)
    {
        MetadataReaderProvider? embeddedPdbProvider = null;
        IDisposable? externalPdbProvider = null;
        MetadataReader? pdbReader = null;
        foreach (var entry in peReader.ReadDebugDirectory())
        {
            if (entry.Type == DebugDirectoryEntryType.Reproducible)
            {
                audit.HasReproducibleFlag = true;
            }

            if (entry.Type == DebugDirectoryEntryType.CodeView)
            {
                var cvData = peReader.ReadCodeViewDebugDirectoryData(entry);
                audit.PdbPath = cvData.Path;

                if (!cvData.Path.StartsWith("/_/", StringComparison.Ordinal) &&
                    Path.GetDirectoryName(cvData.Path) is string dir && !string.IsNullOrEmpty(dir))
                {
                    audit.HasNormalizedPaths = false;
                    audit.NonNormalizedPaths ??= [];
                    audit.NonNormalizedPaths.Add($"PDB Path: {cvData.Path}");
                }
                else
                {
                    audit.HasNormalizedPaths = true;
                }
            }

            if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                audit.HasEmbeddedPdb = true;
                audit.PdbFormat = "Portable";
                audit.PdbLocation = "Embedded";
                embeddedPdbProvider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                pdbReader = embeddedPdbProvider.GetMetadataReader();

                string? sourceLink = AssemblyInspector.ExtractSourceLinkFromReader(pdbReader);
                if (sourceLink != null)
                {
                    audit.HasSourceLink = true;
                    audit.SourceLinkJson = sourceLink;
                }
            }
        }

        // Check for standalone PDB file (if no embedded PDB)
        if (!audit.HasEmbeddedPdb)
        {
            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (File.Exists(pdbPath))
            {
                if (AssemblyInspector.IsWindowsPdb(pdbPath))
                {
                    audit.WindowsPdbDetected = true;
                    audit.PdbFormat = "Windows";
                    audit.PdbLocation = "Standalone";
                }
                else if (AssemblyInspector.IsPortablePdb(pdbPath))
                {
                    audit.PdbFormat = "Portable";
                    audit.PdbLocation = "Standalone";
                    // Open the PDB reader for potential strict verification
                    var stream = File.OpenRead(pdbPath);
                    var standalonePdbProvider = MetadataReaderProvider.FromPortablePdbStream(stream);
                    externalPdbProvider = standalonePdbProvider;
                    pdbReader = standalonePdbProvider.GetMetadataReader();
                    audit.SourceLinkJson = AssemblyInspector.ExtractSourceLinkFromReader(pdbReader);
                    audit.HasSourceLink = audit.SourceLinkJson != null;
                }
            }
            else
            {
                // No local PDB - try to fetch from symbol package or symbol server
                var symbolDownloader = new SymbolPackageDownloader(httpClient);
                var pdbResult = await symbolDownloader.GetPdbReaderAsync(
                    peReader, assemblyPath, packageName, packageVersion, logger.Log, isPlatformAssembly);

                if (pdbResult.Reader != null && pdbResult.Provider != null)
                {
                    externalPdbProvider = pdbResult.Provider;
                    pdbReader = pdbResult.Reader;
                    audit.PdbFormat = "Portable";
                    audit.PdbLocation = "Symbol Package";
                    audit.SymbolServer = pdbResult.SymbolServer;

                    string? sourceLink = AssemblyInspector.ExtractSourceLinkFromReader(pdbReader);
                    if (sourceLink != null)
                    {
                        audit.HasSourceLink = true;
                        audit.SourceLinkJson = sourceLink;
                    }
                }
                else if (pdbResult.WindowsPdbDetected)
                {
                    audit.WindowsPdbDetected = true;
                    audit.PdbFormat = "Windows";
                    // Location unknown - we found it but can't read it
                }
                // else: no PDB found, leave Format and Location as null (Unknown)
            }
        }

        audit.IsDeterministic = audit.HasReproducibleFlag && audit.HasNormalizedPaths != false;

        // Determine reason for missing SourceLink
        if (!audit.HasSourceLink)
        {
            if (audit.WindowsPdbDetected)
            {
                // Windows PDB format found but not supported
                audit.SourceLinkUnavailableReason = "Windows PDB";
            }
            else if (audit.PdbLocation == null && audit.PdbPath != null)
            {
                // PDB path exists in assembly but we couldn't locate/download the actual PDB
                audit.SourceLinkUnavailableReason = "no symbols";
            }
            else if (!audit.HasEmbeddedPdb && audit.PdbPath != null)
            {
                // External PDB referenced but not found
                audit.SourceLinkUnavailableReason = "external PDB not found";
            }
        }

        // Infer builder based on symbol availability and SourceLink
        audit.Builder = InferBuilder(audit);

        // Strict verification: check that all source files are accessible
        if (strictAudit && pdbReader != null && audit.HasSourceLink && audit.SourceLinkJson != null)
        {
            logger.Log("Running strict source verification...");
            await VerifySourceAccessibilityAsync(pdbReader, audit, httpClient, logger);
        }

        // Cleanup PDB providers
        embeddedPdbProvider?.Dispose();
        externalPdbProvider?.Dispose();
    }

    /// <summary>
    /// Verifies that all source files in the PDB are accessible via SourceLink or embedded.
    /// </summary>
    private static async Task VerifySourceAccessibilityAsync(
        MetadataReader pdbReader,
        AssemblyAudit audit,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        var sourceLinkMappings = ParseSourceLinkMappings(audit.SourceLinkJson!);
        if (sourceLinkMappings.Count == 0)
        {
            audit.AllSourcesAccessible = false;
            return;
        }

        // GUID for embedded source: 0E8A571B-6926-466E-B4AD-8AB04611F5FE
        var embeddedSourceGuid = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

        int totalFiles = 0;
        int accessibleFiles = 0;
        int embeddedFiles = 0;
        var missingFiles = new List<string>();

        foreach (var docHandle in pdbReader.Documents)
        {
            var document = pdbReader.GetDocument(docHandle);
            string filePath = pdbReader.GetString(document.Name);

            // Skip non-source files (e.g., compiled resources)
            if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !filePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase) &&
                !filePath.EndsWith(".fs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            totalFiles++;

            // Check if source is embedded
            bool isEmbedded = false;
            foreach (var cdiHandle in pdbReader.GetCustomDebugInformation(docHandle))
            {
                var cdi = pdbReader.GetCustomDebugInformation(cdiHandle);
                if (pdbReader.GetGuid(cdi.Kind) == embeddedSourceGuid)
                {
                    isEmbedded = true;
                    embeddedFiles++;
                    break;
                }
            }

            if (isEmbedded)
            {
                continue; // Source is embedded, no need to verify URL
            }

            // Try to resolve SourceLink URL and verify accessibility
            string? sourceUrl = ApplySourceLinkMapping(filePath, sourceLinkMappings);
            if (sourceUrl == null)
            {
                missingFiles.Add(filePath);
                continue;
            }

            // Check if URL is accessible (HTTP HEAD request)
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, sourceUrl);
                using var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    accessibleFiles++;
                }
                else
                {
                    missingFiles.Add(filePath);
                    logger.Log($"  Source not accessible: {filePath} ({response.StatusCode})");
                }
            }
            catch
            {
                missingFiles.Add(filePath);
                logger.Log($"  Source not accessible: {filePath} (request failed)");
            }
        }

        audit.TotalSourceFiles = totalFiles;
        audit.AccessibleSourceFiles = accessibleFiles;
        audit.EmbeddedSourceFiles = embeddedFiles;
        audit.AllSourcesAccessible = missingFiles.Count == 0;
        audit.MissingSourceFiles = missingFiles.Count > 0 ? missingFiles : null;

        logger.Log($"Source coverage: {accessibleFiles + embeddedFiles}/{totalFiles} files accessible");
    }

    private static Dictionary<string, string> ParseSourceLinkMappings(string sourceLinkJson)
    {
        var mappings = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(sourceLinkJson);
            if (doc.RootElement.TryGetProperty("documents", out var documents))
            {
                foreach (var prop in documents.EnumerateObject())
                {
                    mappings[prop.Name] = prop.Value.GetString() ?? "";
                }
            }
        }
        catch
        {
            // Invalid JSON
        }
        return mappings;
    }

    private static string? ApplySourceLinkMapping(string filePath, Dictionary<string, string> mappings)
    {
        foreach (var (pattern, urlTemplate) in mappings)
        {
            if (pattern.EndsWith("*"))
            {
                string prefix = pattern[..^1];
                if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = filePath[prefix.Length..];
                    return urlTemplate.Replace("*", relativePath);
                }
            }
            else if (filePath.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return urlTemplate;
            }
        }
        return null;
    }

    /// <summary>
    /// Infers who built the assembly based on symbol availability and SourceLink.
    /// Only meaningful for Microsoft-branded assemblies where we distinguish
    /// between genuine Microsoft builds and distro rebuilds.
    /// </summary>
    private static string? InferBuilder(AssemblyAudit audit)
    {
        var company = audit.AssemblyInfo?.Company;
        bool isMicrosoftAssembly = company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true;

        // Only relevant for Microsoft-branded assemblies
        if (!isMicrosoftAssembly)
        {
            return null;
        }

        // If we got symbols from MSDL with SourceLink, it's a verified Microsoft build
        if (audit.SymbolServer == "msdl.microsoft.com" && audit.HasSourceLink)
        {
            return "Microsoft";
        }

        // If SourceLink points to a dotnet repo, it's Microsoft
        if (audit.HasSourceLink && audit.SourceLinkJson != null)
        {
            if (audit.SourceLinkJson.Contains("github.com/dotnet/", StringComparison.OrdinalIgnoreCase) ||
                audit.SourceLinkJson.Contains("raw.githubusercontent.com/dotnet/", StringComparison.OrdinalIgnoreCase))
            {
                return "Microsoft";
            }
        }

        // Microsoft metadata but no symbols - can't verify, likely distro build
        if (audit.SourceLinkUnavailableReason == "no symbols")
        {
            return "Unknown";
        }

        // Can't determine (e.g., Windows PDB case)
        return null;
    }

    private static List<AssemblyReferenceNode> BuildTransitiveReferences(
        List<AssemblyReference> references,
        string? sourceDir,
        HashSet<string> visited,
        VerboseLogger logger,
        int depth = 0)
    {
        var nodes = new List<AssemblyReferenceNode>();

        foreach (var reference in references.OrderBy(r => r.Name))
        {
            var node = new AssemblyReferenceNode
            {
                Name = reference.Name,
                Version = reference.Version,
                PublicKeyToken = reference.PublicKeyToken,
                Depth = depth
            };

            // Check for cycles
            if (visited.Contains(reference.Name))
            {
                node.IsCyclic = true;
                nodes.Add(node);
                continue;
            }

            visited.Add(reference.Name);

            // Try to resolve the assembly
            string? resolvedPath = null;
            string? resolvedFrom = null;

            // 1. Try same directory as source assembly
            if (!string.IsNullOrEmpty(sourceDir))
            {
                var localPath = Path.Combine(sourceDir, reference.Name + ".dll");
                if (File.Exists(localPath))
                {
                    resolvedPath = localPath;
                    resolvedFrom = "local";
                }
            }

            // 2. Try platform resolver for System.*/Microsoft.* assemblies
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

            // Recursively resolve children if we found the assembly
            if (resolvedPath != null)
            {
                try
                {
                    var childRefs = AssemblyInspector.ExtractReferences(resolvedPath);
                    if (childRefs.Count > 0)
                    {
                        // Clone visited set for this branch to allow diamond dependencies
                        var branchVisited = new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase);
                        var childNodes = BuildTransitiveReferences(childRefs, Path.GetDirectoryName(resolvedPath), branchVisited, logger, depth + 1);
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

    private static string? ExtractTfmFromPath(string relativePath)
    {
        // Patterns: lib/net8.0/Foo.dll, tools/net8.0/any/Foo.dll
        var parts = relativePath.Split('/');
        foreach (var part in parts)
        {
            if (IsTfmFolder(part))
                return part;
        }
        return null;
    }

    private static bool IsTfmFolder(string folderName)
    {
        // Common TFM patterns: net8.0, net9.0, net10.0, netstandard2.0, netcoreapp3.1, net472, etc.
        return folderName.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
               (folderName.Contains('.') || (folderName.Length > 3 && char.IsDigit(folderName[3])));
    }

    private static int GetTfmPriority(string tfm)
    {
        // Higher number = higher priority (newer/preferred)
        var lower = tfm.ToLowerInvariant();

        // .NET (net5.0+) - highest priority
        if (lower.StartsWith("net") && !lower.StartsWith("netstandard") && !lower.StartsWith("netcoreapp") && !lower.StartsWith("netframework"))
        {
            // Extract version: net8.0 -> 8.0, net10.0 -> 10.0
            var versionPart = lower[3..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 10000 + (version.Major * 100) + version.Minor;
            }
            // net472, net48, etc. (old .NET Framework)
            if (int.TryParse(versionPart.Replace(".", ""), out var legacyVersion))
            {
                return 1000 + legacyVersion;
            }
        }

        // .NET Core
        if (lower.StartsWith("netcoreapp"))
        {
            var versionPart = lower[10..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 5000 + (version.Major * 100) + version.Minor;
            }
        }

        // .NET Standard
        if (lower.StartsWith("netstandard"))
        {
            var versionPart = lower[11..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 3000 + (version.Major * 100) + version.Minor;
            }
        }

        return 0;
    }


    /// <summary>
    /// Extracts version from a cached package path.
    /// Path format: .../packages/packagename/version/lib/tfm/assembly.dll
    /// </summary>
    private static string? ExtractVersionFromPath(string dllPath, string packageName)
    {
        var normalizedPath = dllPath.Replace('\\', '/');
        var normalizedPackageName = packageName.ToLowerInvariant();

        // Look for pattern: /packagename/version/
        var searchPattern = $"/{normalizedPackageName}/";
        var index = normalizedPath.ToLowerInvariant().IndexOf(searchPattern, StringComparison.Ordinal);
        if (index < 0)
            return null;

        // Extract what comes after the package name
        var afterPackage = normalizedPath[(index + searchPattern.Length)..];
        var nextSlash = afterPackage.IndexOf('/');
        if (nextSlash > 0)
        {
            var possibleVersion = afterPackage[..nextSlash];
            // Verify it looks like a version (starts with digit)
            if (possibleVersion.Length > 0 && char.IsDigit(possibleVersion[0]))
            {
                return possibleVersion;
            }
        }

        return null;
    }
}
