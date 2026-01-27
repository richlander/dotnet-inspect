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
public class PackageCommand : ICommand
{
    public async Task<int> ExecuteAsync(string[] args)
    {
        // Parse options from args
        var (options, packageArgs, showHelp) = ParseOptions(args);

        if (showHelp)
        {
            return await new HelpCommand("package").ExecuteAsync([]);
        }

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

        using HttpClient client = new();

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
            packageName = packageArgs[0].ToLowerInvariant();

            if (packageArgs.Length >= 2)
            {
                version = packageArgs[1].ToLowerInvariant();
            }
            else
            {
                // Auto-discover latest version
                string? latestVersion = await GetLatestVersionAsync(client, packageName, logger);
                if (latestVersion == null)
                {
                    Console.Error.WriteLine($"Failed to get latest version for package: {packageName}");
                    return 1;
                }
                version = latestVersion;
            }

            tempDir = Path.Combine(Path.GetTempPath(), $"inspect-{packageName}-{version}-{Guid.NewGuid():N}");
        }

        try
        {
            Directory.CreateDirectory(tempDir);
            string extractPath = Path.Combine(tempDir, "extracted");

            if (isLocalFile)
            {
                string localPath = packageArgs[0];
                logger.Log($"Processing local package: {Path.GetFileName(localPath)}");
                ZipFile.ExtractToDirectory(localPath, extractPath);
            }
            else
            {
                string nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/{version}/{packageName}.{version}.nupkg";
                logger.Log($"Downloading: {nupkgUrl}");

                byte[] packageBytes = await client.GetByteArrayAsync(nupkgUrl);
                string nupkgPath = Path.Combine(tempDir, $"{packageName}.{version}.nupkg");
                await File.WriteAllBytesAsync(nupkgPath, packageBytes);
                ZipFile.ExtractToDirectory(nupkgPath, extractPath);

                logger.Log("Package downloaded successfully.");
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

            // Always parse nuspec for basic metadata
            string[] nuspecFiles = Directory.GetFiles(extractPath, "*.nuspec", SearchOption.TopDirectoryOnly);
            if (nuspecFiles.Length > 0)
            {
                NuspecParser.Parse(nuspecFiles[0], result);
            }

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

            // Filter output based on options
            FilterResultForOutput(result, options);

            // Output results
            OutputFormatter.WriteResult(result, options);

            return 0;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Failed to download package: {ex.Message}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(tempDir))
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

    private static (InspectionOptions options, string[] packageArgs, bool showHelp) ParseOptions(string[] args)
    {
        bool includeDeps = false;
        bool listFiles = false;
        bool listAllFiles = false;
        bool treeView = false;
        bool listVersions = false;
        bool includePrerelease = false;
        int? limit = null;
        bool jsonOutput = false;
        bool verbose = false;
        bool showHelp = false;
        var verbosity = Verbosity.Normal;
        HashSet<int>? includeSections = null;
        HashSet<int>? excludeSections = null;

        var packageArgs = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var lower = arg.ToLowerInvariant();
            switch (lower)
            {
                case "--deps":
                    includeDeps = true;
                    break;
                case "--files":
                    listFiles = true;
                    break;
                case "--all":
                    listAllFiles = true;
                    break;
                case "--tree":
                    treeView = true;
                    break;
                case "--versions":
                    listVersions = true;
                    break;
                case "--preview":
                case "--prerelease":
                    includePrerelease = true;
                    break;
                case "-n":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int n) && n > 0)
                    {
                        limit = n;
                        i++;
                    }
                    break;
                case "--json":
                    jsonOutput = true;
                    break;
                case "--markout":
                    jsonOutput = false;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "-v:q":
                    verbosity = Verbosity.Quiet;
                    break;
                case "-v:m":
                    verbosity = Verbosity.Minimal;
                    break;
                case "-v:n":
                    verbosity = Verbosity.Normal;
                    break;
                case "-v:d":
                    verbosity = Verbosity.Detailed;
                    break;
                case "--help":
                case "help":
                    showHelp = true;
                    break;
                default:
                    if (lower.StartsWith("-s:") || lower.StartsWith("-s="))
                    {
                        includeSections = ParseSectionList(arg[3..]);
                    }
                    else if (lower.StartsWith("-x:") || lower.StartsWith("-x="))
                    {
                        excludeSections = ParseSectionList(arg[3..]);
                    }
                    else if (!arg.StartsWith("-"))
                    {
                        packageArgs.Add(arg);
                    }
                    break;
            }
        }

        var options = new InspectionOptions
        {
            IncludeDeps = includeDeps,
            ListFiles = listFiles,
            ListAllFiles = listAllFiles,
            TreeView = treeView,
            ListVersions = listVersions,
            IncludePrerelease = includePrerelease,
            Limit = limit,
            JsonOutput = jsonOutput,
            Verbose = verbose,
            Verbosity = verbosity,
            IncludeSections = includeSections,
            ExcludeSections = excludeSections
        };

        return (options, packageArgs.ToArray(), showHelp);
    }

    private static HashSet<int> ParseSectionList(string value)
    {
        var sections = new HashSet<int>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out int section) && section > 0)
            {
                sections.Add(section);
            }
        }
        return sections;
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

    private static async Task<int> ListVersionsAsync(string packageName, bool includePrerelease, int? limit, VerboseLogger logger)
    {
        using HttpClient client = new();
        string normalizedName = packageName.ToLowerInvariant();

        try
        {
            string indexUrl = $"https://api.nuget.org/v3-flatcontainer/{normalizedName}/index.json";
            logger.Log($"Fetching versions from: {indexUrl}");

            string json = await client.GetStringAsync(indexUrl);
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

            string json = await client.GetStringAsync(indexUrl);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("versions", out var versions))
            {
                var versionList = versions.EnumerateArray().Select(v => v.GetString()).ToList();
                if (versionList.Count > 0)
                {
                    string? latest = versionList[^1];
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
}
