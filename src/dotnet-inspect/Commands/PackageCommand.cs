using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects a NuGet package.
/// </summary>
public class PackageCommand
{
    public static async Task<int> ExecuteAsync(string[] packageArgs, InspectionOptions options)
    {
        if (packageArgs.Length < 1)
        {
            Console.Error.WriteLine("Error: Package name or path required.");
            Console.Error.WriteLine("Run 'dotnet-inspect package --help' for usage.");
            return 1;
        }

        var logger = new VerboseLogger(options.Verbose);

        // Handle --versions mode: list versions and exit early
        if (options.ListVersions)
        {
            return await ListVersionsAsync(packageArgs[0], options.IncludePrerelease, options.Limit, logger);
        }

        // Check if first argument is a local file path
        bool isLocalFile = packageArgs.Length >= 1 &&
            packageArgs[0].EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

        using HttpClient client = HttpClientFactory.Create();

        string packageName;
        string version;
        string tempDir;

        if (isLocalFile)
        {
            string localPath = packageArgs[0];
            if (!File.Exists(localPath))
            {
                Console.Error.WriteLine($"Error: File not found: {localPath}");
                return 1;
            }

            string fileName = Path.GetFileNameWithoutExtension(localPath);
            packageName = fileName;
            version = "local";
            tempDir = Path.Combine(Path.GetTempPath(), $"inspect-local-{Path.GetFileName(localPath)}-{Guid.NewGuid():N}");
        }
        else
        {
            // Support format: PackageName or PackageName@version
            string packageArg = packageArgs[0];
            int atIndex = packageArg.IndexOf('@');

            if (atIndex > 0)
            {
                packageName = packageArg[..atIndex].ToLowerInvariant();
                version = packageArg[(atIndex + 1)..].ToLowerInvariant();
                logger.Log($"Using specified version: {version}");
            }
            else if (packageArgs.Length >= 2)
            {
                packageName = packageArg.ToLowerInvariant();
                version = packageArgs[1].ToLowerInvariant();
            }
            else
            {
                packageName = packageArg.ToLowerInvariant();
                // Auto-discover latest version
                string? latestVersion = await GetLatestVersionAsync(client, packageName, logger);
                if (latestVersion == null)
                {
                    Console.Error.WriteLine($"Error: Package '{packageArg}' not found on nuget.org");
                    return 1;
                }
                version = latestVersion;
            }

            tempDir = Path.Combine(Path.GetTempPath(), $"inspect-{packageName}-{version}-{Guid.NewGuid():N}");
        }

        bool usingCache = false;
        string? extractPath = null;

        try
        {
            if (isLocalFile)
            {
                Directory.CreateDirectory(tempDir);
                extractPath = Path.Combine(tempDir, "extracted");
                string localPath = packageArgs[0];
                logger.Log($"Processing local package: {Path.GetFileName(localPath)}");
                ZipFile.ExtractToDirectory(localPath, extractPath);
            }
            else
            {
                // Check NuGet cache first
                var cachedPath = NuGetCache.TryGetCachedPackage(packageName, version);
                if (cachedPath != null && NuGetCache.IsCachedPackageValid(cachedPath))
                {
                    logger.Log($"Using cached package: {cachedPath}");
                    extractPath = cachedPath;
                    usingCache = true;
                }
                else
                {
                    Directory.CreateDirectory(tempDir);
                    extractPath = Path.Combine(tempDir, "extracted");

                    string nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/{version}/{packageName}.{version}.nupkg";
                    logger.Log($"Downloading: {nupkgUrl}");

                    byte[] packageBytes = await client.GetByteArrayAsync(nupkgUrl);
                    string nupkgPath = Path.Combine(tempDir, $"{packageName}.{version}.nupkg");
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

            // Handle --files mode: list files and exit early
            if (options.ListFiles)
            {
                ListPackageFiles(extractPath, options);
                return 0;
            }

            // Create result and run inspections
            var result = new InspectionResult
            {
                PackageName = packageName,
                Version = version
            };

            // Always parse nuspec for basic metadata (needed for --readme to find correct file)
            string[] nuspecFiles = Directory.GetFiles(extractPath, "*.nuspec", SearchOption.TopDirectoryOnly);
            if (nuspecFiles.Length > 0)
            {
                NuspecParser.Parse(nuspecFiles[0], result);
            }

            // Handle --readme mode: print README and exit early
            if (options.ShowReadme)
            {
                return PrintReadme(extractPath, result.ReadmeFile, options);
            }

            // Check for README (use nuspec-specified file or fall back to README.md)
            string readmeFileName = result.ReadmeFile ?? "README.md";
            result.HasReadme = File.Exists(Path.Combine(extractPath, readmeFileName));

            // Always analyze directory structure
            string toolsDir = Path.Combine(extractPath, "tools");
            if (Directory.Exists(toolsDir))
            {
                result.IsToolPackage = true;
                ToolsAnalyzer.AnalyzeToolsDirectory(toolsDir, result);
            }
            else
            {
                result.IsToolPackage = false;
                string libDir = Path.Combine(extractPath, "lib");
                if (Directory.Exists(libDir))
                {
                    ToolsAnalyzer.AnalyzeLibDirectory(libDir, result);
                }

                string runtimesDir = Path.Combine(extractPath, "runtimes");
                if (Directory.Exists(runtimesDir))
                {
                    ToolsAnalyzer.AnalyzeRuntimesDirectory(runtimesDir, result);
                }
            }

            // Analyze content directories and count assemblies
            ToolsAnalyzer.AnalyzeContentDirectories(extractPath, result);
            result.AssemblyCount = ToolsAnalyzer.CountAssemblies(extractPath);

            // Parse deps.json files if deps flag is set
            if (options.IncludeDeps)
            {
                string[] depsFiles = Directory.GetFiles(extractPath, "*.deps.json", SearchOption.AllDirectories);
                foreach (string depsFile in depsFiles)
                {
                    DepsJsonParser.Parse(depsFile, result);
                }
            }

            // Verify RID-specific packages exist (always do this for RID pointer packages)
            if (result.IsRidSpecificPointerPackage && result.RuntimeIdentifierPackages is { Count: > 0 })
            {
                string? localDir = isLocalFile ? Path.GetDirectoryName(Path.GetFullPath(packageArgs[0])) : null;
                await RidPackageVerifier.VerifyAsync(client, result, result.Version, localDir, logger);
            }

            // Populate files for detailed verbosity
            if (options.Verbosity == Verbosity.Detailed)
            {
                PopulateFilesForDetailedView(extractPath, result);
            }

            // Fetch package metadata from NuGet (only for remote packages)
            if (!isLocalFile)
            {
                await FetchNuGetMetadataAsync(client, packageName, version, result, logger);
            }

            // Filter output based on options
            FilterResultForOutput(result, options);

            // Output results
            OutputFormatter.WriteResult(result, options);

            return 0;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.Error.WriteLine($"Error: Package '{packageName}' version '{version}' not found on nuget.org.");
            Console.Error.WriteLine("Use 'dotnet-inspect package <name> --versions' to list available versions.");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Failed to download package: {ex.Message}");
            return 1;
        }
        finally
        {
            // Only clean up temp directory if we created one (not using cache)
            if (!usingCache && Directory.Exists(tempDir))
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

    private static void FilterResultForOutput(InspectionResult result, InspectionOptions options)
    {
        // Assembly audits are no longer part of package inspection
        result.AssemblyAudits = null;
        result.AuditSummary = null;

        // If deps is not requested, clear runtime dependencies
        if (!options.IncludeDeps)
        {
            result.RuntimeDependencies = null;
        }
    }

    /// <summary>
    /// Populates the Files list for detailed verbosity output.
    /// </summary>
    private static void PopulateFilesForDetailedView(string extractPath, InspectionResult result)
    {
        // Get DLLs from tools or lib directory
        string toolsDir = Path.Combine(extractPath, "tools");
        string libDir = Path.Combine(extractPath, "lib");

        string searchPath;
        if (Directory.Exists(toolsDir))
        {
            searchPath = toolsDir;
        }
        else if (Directory.Exists(libDir))
        {
            searchPath = libDir;
        }
        else
        {
            return;
        }

        var files = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(extractPath, f))
            .Where(p => !p.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p)
            .ToList();

        if (files.Count > 0)
        {
            result.Files = files;
        }
    }

    private static void ListPackageFiles(string extractPath, InspectionOptions options)
    {
        string searchPath;
        string pattern;

        if (options.ListAllFiles)
        {
            // --all: search entire package for all files
            searchPath = extractPath;
            pattern = "*";
        }
        else
        {
            // Tools packages use 'tools' directory, regular packages use 'lib'
            string toolsDir = Path.Combine(extractPath, "tools");
            string libDir = Path.Combine(extractPath, "lib");

            if (Directory.Exists(toolsDir))
            {
                searchPath = toolsDir;
            }
            else if (Directory.Exists(libDir))
            {
                searchPath = libDir;
            }
            else
            {
                Console.Error.WriteLine("No lib or tools directory found. Use --all to list all files.");
                return;
            }
            pattern = "*.dll";
        }

        string[] files = Directory.GetFiles(searchPath, pattern, SearchOption.AllDirectories);

        var relativePaths = files
            .Select(f => Path.GetRelativePath(extractPath, f))
            .Where(p => !p.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.StartsWith("_rels", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.StartsWith("[Content_Types]", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p);

        var results = options.Limit.HasValue 
            ? relativePaths.Take(options.Limit.Value).ToList()
            : relativePaths.ToList();

        if (options.TreeView)
        {
            WriteFileTree(results);
        }
        else
        {
            foreach (var path in results)
            {
                Console.WriteLine(path);
            }
        }
    }

    private static void WriteFileTree(List<string> paths)
    {
        // Build tree structure from file paths
        var root = new Dictionary<string, object>();
        
        foreach (var path in paths)
        {
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = root;
            
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (i == parts.Length - 1)
                {
                    // Leaf node (file)
                    current[part] = new Dictionary<string, object>();
                }
                else
                {
                    // Directory node
                    if (!current.TryGetValue(part, out var next))
                    {
                        next = new Dictionary<string, object>();
                        current[part] = next;
                    }
                    current = (Dictionary<string, object>)next;
                }
            }
        }

        // Convert to TreeNode structure
        var treeNodes = BuildTreeNodes(root);
        
        // Write using Markout
        var writer = new MarkoutWriter(Console.Out);
        writer.WriteTree(treeNodes);
    }

    private static List<TreeNode> BuildTreeNodes(Dictionary<string, object> dict)
    {
        var nodes = new List<TreeNode>();
        
        foreach (var kvp in dict.OrderBy(k => k.Key))
        {
            var children = (Dictionary<string, object>)kvp.Value;
            if (children.Count == 0)
            {
                nodes.Add(new TreeNode(kvp.Key));
            }
            else
            {
                nodes.Add(new TreeNode(kvp.Key, BuildTreeNodes(children)));
            }
        }
        
        return nodes;
    }

    private static int PrintReadme(string extractPath, string? readmeFile, InspectionOptions options)
    {
        // Use nuspec-specified readme file or fall back to README.md
        string readmeFileName = readmeFile ?? "README.md";
        string readmePath = Path.Combine(extractPath, readmeFileName);
        
        if (!File.Exists(readmePath))
        {
            Console.Error.WriteLine("Error: This package does not contain a readme file.");
            return 1;
        }

        string content = File.ReadAllText(readmePath);
        
        if (!string.IsNullOrEmpty(options.OutputPath))
        {
            File.WriteAllText(options.OutputPath, content);
        }
        else
        {
            Console.WriteLine(content);
        }
        
        return 0;
    }

    private static async Task<int> ListVersionsAsync(string packageName, bool includePrerelease, int? limit, VerboseLogger logger)
    {
        using HttpClient client = HttpClientFactory.Create();
        string normalizedName = packageName.ToLowerInvariant();

        try
        {
            string indexUrl = $"https://api.nuget.org/v3-flatcontainer/{normalizedName}/index.json";
            logger.Log($"Fetching versions from: {indexUrl}");

            string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, indexUrl);
            if (json == null)
            {
                Console.Error.WriteLine($"Error: Package '{packageName}' not found on nuget.org");
                return 1;
            }
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("versions", out var versions))
            {
                var versionList = versions.EnumerateArray()
                    .Select(v => v.GetString())
                    .Where(v => v != null)
                    .Where(v => includePrerelease || !IsPrerelease(v!))
                    .ToList();

                // Print in reverse order (newest first), with optional limit
                int count = 0;
                for (int i = versionList.Count - 1; i >= 0; i--)
                {
                    Console.WriteLine(versionList[i]);
                    count++;
                    if (limit.HasValue && count >= limit.Value)
                        break;
                }

                return 0;
            }

            Console.Error.WriteLine($"Error: No versions found for package '{packageName}'");
            return 1;
        }
        catch (HttpRequestException)
        {
            Console.Error.WriteLine($"Error: Package '{packageName}' not found on nuget.org");
            return 1;
        }
    }

    private static bool IsPrerelease(string version)
    {
        // Prerelease versions contain a hyphen (e.g., 10.0.0-preview.1, 9.0.0-rc.2)
        return version.Contains('-');
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

    private static async Task<DateTimeOffset?> GetPackagePublishedDateAsync(HttpClient client, string packageName, string version, VerboseLogger logger)
    {
        try
        {
            // Use NuGet registration API to get package metadata including publish date
            string registrationUrl = $"https://api.nuget.org/v3/registration5-semver1/{packageName.ToLowerInvariant()}/{version}.json";
            logger.Log($"Fetching package metadata from: {registrationUrl}");

            string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, registrationUrl);
            if (json == null)
                return null;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("published", out var publishedElement))
            {
                if (DateTimeOffset.TryParse(publishedElement.GetString(), out var published))
                {
                    logger.Log($"Package published: {published:yyyy-MM-dd}");
                    return published;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Error fetching publish date: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Fetches all NuGet metadata for a package: published date, downloads, verified status, deprecation, vulnerabilities.
    /// </summary>
    private static async Task FetchNuGetMetadataAsync(HttpClient client, string packageName, string version, InspectionResult result, VerboseLogger logger)
    {
        var normalizedName = packageName.ToLowerInvariant();

        // Fetch from registration API (published, deprecation, vulnerabilities)
        try
        {
            string registrationUrl = $"https://api.nuget.org/v3/registration5-semver1/{normalizedName}/{version}.json";
            logger.Log($"Fetching registration metadata: {registrationUrl}");

            string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, registrationUrl);
            if (json != null)
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Published date
                if (root.TryGetProperty("published", out var publishedElement))
                {
                    if (DateTimeOffset.TryParse(publishedElement.GetString(), out var published))
                    {
                        result.Published = published;
                        logger.Log($"Published: {published:yyyy-MM-dd}");
                    }
                }

                // Deprecation
                if (root.TryGetProperty("deprecation", out var deprecationElement) && 
                    deprecationElement.ValueKind == JsonValueKind.Object)
                {
                    result.Deprecation = new PackageDeprecation();
                    
                    if (deprecationElement.TryGetProperty("reasons", out var reasons))
                    {
                        result.Deprecation.Reasons = reasons.EnumerateArray()
                            .Select(r => r.GetString())
                            .Where(r => r != null)
                            .Cast<string>()
                            .ToList();
                    }
                    if (deprecationElement.TryGetProperty("message", out var message))
                    {
                        result.Deprecation.Message = message.GetString();
                    }
                    if (deprecationElement.TryGetProperty("alternatePackage", out var altPkg) &&
                        altPkg.TryGetProperty("id", out var altId))
                    {
                        result.Deprecation.AlternatePackageId = altId.GetString();
                    }
                    logger.Log($"Deprecation: {result.Deprecation.Summary}");
                }

                // Vulnerabilities
                if (root.TryGetProperty("vulnerabilities", out var vulnsElement) && 
                    vulnsElement.ValueKind == JsonValueKind.Array)
                {
                    result.Vulnerabilities = vulnsElement.EnumerateArray()
                        .Select(v => new PackageVulnerability
                        {
                            Severity = v.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "" : "",
                            AdvisoryUrl = v.TryGetProperty("advisoryUrl", out var url) ? url.GetString() : null
                        })
                        .ToList();
                    
                    if (result.Vulnerabilities.Count > 0)
                    {
                        logger.Log($"Vulnerabilities: {result.VulnerabilitiesDisplay}");
                    }
                    else
                    {
                        result.Vulnerabilities = null;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Error fetching registration metadata: {ex.Message}");
        }

        // Fetch from search API (downloads, verified, owners)
        try
        {
            string searchUrl = $"https://azuresearch-usnc.nuget.org/query?q=packageid:{normalizedName}&take=1";
            logger.Log($"Fetching search metadata: {searchUrl}");

            string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, searchUrl);
            if (json != null)
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data) && 
                    data.GetArrayLength() > 0)
                {
                    var pkg = data[0];

                    // Total downloads
                    if (pkg.TryGetProperty("totalDownloads", out var downloads))
                    {
                        result.TotalDownloads = downloads.GetInt64();
                        logger.Log($"Downloads: {result.DownloadsDisplay}");
                    }

                    // Verified
                    if (pkg.TryGetProperty("verified", out var verified))
                    {
                        result.IsVerified = verified.GetBoolean();
                        logger.Log($"Verified: {result.IsVerified}");
                    }

                    // Owners
                    if (pkg.TryGetProperty("owners", out var owners))
                    {
                        result.Owners = owners.EnumerateArray()
                            .Select(o => o.GetString())
                            .Where(o => o != null)
                            .Cast<string>()
                            .ToList();
                    }

                    // Deprecation (from search API - more reliable than registration API)
                    if (result.Deprecation == null && 
                        pkg.TryGetProperty("deprecation", out var deprecationElement) && 
                        deprecationElement.ValueKind == JsonValueKind.Object)
                    {
                        result.Deprecation = new PackageDeprecation();
                        
                        if (deprecationElement.TryGetProperty("reasons", out var reasons))
                        {
                            result.Deprecation.Reasons = reasons.EnumerateArray()
                                .Select(r => r.GetString())
                                .Where(r => r != null)
                                .Cast<string>()
                                .ToList();
                        }
                        if (deprecationElement.TryGetProperty("message", out var message))
                        {
                            result.Deprecation.Message = message.GetString();
                        }
                        if (deprecationElement.TryGetProperty("alternatePackage", out var altPkg) &&
                            altPkg.TryGetProperty("id", out var altId))
                        {
                            result.Deprecation.AlternatePackageId = altId.GetString();
                        }
                        logger.Log($"Deprecation: {result.Deprecation.Summary}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Error fetching search metadata: {ex.Message}");
        }

        // Fetch from vulnerability API
        try
        {
            var vulnerabilities = await GetPackageVulnerabilitiesAsync(client, normalizedName, version, logger);
            if (vulnerabilities.Count > 0)
            {
                result.Vulnerabilities = vulnerabilities;
                logger.Log($"Vulnerabilities: {result.VulnerabilitiesDisplay}");
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Error fetching vulnerability data: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches vulnerability data from NuGet's VulnerabilityInfo API and checks if the package version is affected.
    /// </summary>
    private static async Task<List<PackageVulnerability>> GetPackageVulnerabilitiesAsync(
        HttpClient client, string packageName, string version, VerboseLogger logger)
    {
        var result = new List<PackageVulnerability>();

        // Parse package version for comparison
        if (!NuGet.Versioning.NuGetVersion.TryParse(version, out var packageVersion))
        {
            logger.Log($"Could not parse version: {version}");
            return result;
        }

        // Fetch vulnerability index
        string indexUrl = "https://api.nuget.org/v3/vulnerabilities/index.json";
        string? indexJson = await HttpRetryHelper.GetStringWithRetryAsync(client, indexUrl);
        if (indexJson == null)
            return result;

        using var indexDoc = JsonDocument.Parse(indexJson);
        var pages = indexDoc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("@id").GetString())
            .Where(url => url != null)
            .ToList();

        // Fetch each vulnerability page and check for matches
        foreach (var pageUrl in pages)
        {
            if (pageUrl == null) continue;

            logger.Log($"Fetching vulnerability page: {pageUrl}");
            string? pageJson = await HttpRetryHelper.GetStringWithRetryAsync(client, pageUrl);
            if (pageJson == null) continue;

            using var pageDoc = JsonDocument.Parse(pageJson);
            
            // The page is a dictionary keyed by lowercase package name
            if (pageDoc.RootElement.TryGetProperty(packageName, out var vulnArray))
            {
                foreach (var vuln in vulnArray.EnumerateArray())
                {
                    string? versionsRange = vuln.TryGetProperty("versions", out var v) ? v.GetString() : null;
                    int severityNum = vuln.TryGetProperty("severity", out var s) ? s.GetInt32() : 0;
                    string? advisoryUrl = vuln.TryGetProperty("url", out var u) ? u.GetString() : null;

                    if (versionsRange != null && IsVersionInRange(packageVersion, versionsRange))
                    {
                        var vulnerability = new PackageVulnerability
                        {
                            Severity = SeverityToString(severityNum),
                            AdvisoryUrl = advisoryUrl
                        };

                        // Extract GHSA ID from URL and fetch details from GitHub
                        if (advisoryUrl != null && advisoryUrl.Contains("github.com/advisories/GHSA-"))
                        {
                            var ghsaId = ExtractGhsaId(advisoryUrl);
                            if (ghsaId != null)
                            {
                                vulnerability.GhsaId = ghsaId;
                                await EnrichFromGitHubAdvisoryAsync(client, vulnerability, ghsaId, logger);
                            }
                        }

                        result.Add(vulnerability);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts GHSA ID from a GitHub advisory URL.
    /// </summary>
    private static string? ExtractGhsaId(string url)
    {
        // URL format: https://github.com/advisories/GHSA-xxxx-xxxx-xxxx
        var match = System.Text.RegularExpressions.Regex.Match(url, @"GHSA-[a-z0-9]{4}-[a-z0-9]{4}-[a-z0-9]{4}");
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Fetches additional vulnerability details from GitHub Advisory API.
    /// </summary>
    private static async Task EnrichFromGitHubAdvisoryAsync(HttpClient client, PackageVulnerability vuln, string ghsaId, VerboseLogger logger)
    {
        try
        {
            string apiUrl = $"https://api.github.com/advisories/{ghsaId}";
            logger.Log($"Fetching GitHub advisory: {apiUrl}");

            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("User-Agent", "dotnet-inspect");
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                logger.Log($"GitHub API returned {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("cve_id", out var cveId) && cveId.ValueKind == JsonValueKind.String)
                vuln.CveId = cveId.GetString();

            if (root.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String)
                vuln.Summary = summary.GetString();

            // Use GitHub's severity if available (it's more authoritative)
            if (root.TryGetProperty("severity", out var severity) && severity.ValueKind == JsonValueKind.String)
            {
                var sev = severity.GetString();
                if (!string.IsNullOrEmpty(sev))
                    vuln.Severity = char.ToUpper(sev[0]) + sev[1..].ToLower();
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Error fetching GitHub advisory: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a version falls within a NuGet version range.
    /// </summary>
    private static bool IsVersionInRange(NuGet.Versioning.NuGetVersion version, string rangeString)
    {
        if (NuGet.Versioning.VersionRange.TryParse(rangeString, out var range))
        {
            return range.Satisfies(version);
        }
        return false;
    }

    /// <summary>
    /// Converts numeric severity to string.
    /// </summary>
    private static string SeverityToString(int severity)
    {
        return severity switch
        {
            0 => "Low",
            1 => "Moderate",
            2 => "High",
            3 => "Critical",
            _ => "Unknown"
        };
    }
}
