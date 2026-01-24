using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;

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
            Console.Error.WriteLine("Run 'dotnet-inspect help package' for usage.");
            return 1;
        }

        var logger = new VerboseLogger(options.Verbose);

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

            // Audit assemblies if audit or api flag is set
            if (options.IncludeAudit || options.IncludeApi)
            {
                AssemblyAuditor.AuditAssemblies(extractPath, result, options);
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
            OutputFormatter.WriteResult(result, options.JsonOutput);

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
        bool includeAudit = false;
        bool includeApi = false;
        bool includeDeps = false;
        bool jsonOutput = false;
        bool verbose = false;
        bool showHelp = false;

        var packageArgs = new List<string>();

        foreach (var arg in args)
        {
            switch (arg.ToLowerInvariant())
            {
                case "--audit":
                    includeAudit = true;
                    break;
                case "--api":
                    includeApi = true;
                    break;
                case "--deps":
                    includeDeps = true;
                    break;
                case "--all":
                    includeAudit = true;
                    includeApi = true;
                    includeDeps = true;
                    break;
                case "--json":
                    jsonOutput = true;
                    break;
                case "--mdf":
                    jsonOutput = false;
                    break;
                case "--verbose":
                case "-v":
                    verbose = true;
                    break;
                case "--help":
                case "help":
                    showHelp = true;
                    break;
                default:
                    if (!arg.StartsWith("-"))
                    {
                        packageArgs.Add(arg);
                    }
                    break;
            }
        }

        var options = new InspectionOptions
        {
            IncludeAudit = includeAudit,
            IncludeApi = includeApi,
            IncludeDeps = includeDeps,
            JsonOutput = jsonOutput,
            Verbose = verbose
        };

        return (options, packageArgs.ToArray(), showHelp);
    }

    private static void FilterResultForOutput(InspectionResult result, InspectionOptions options)
    {
        // If audit is not requested, clear audit data
        if (!options.IncludeAudit)
        {
            result.AuditSummary = null;

            // Keep assembly audits but strip audit-specific fields
            if (result.AssemblyAudits != null)
            {
                foreach (var audit in result.AssemblyAudits)
                {
                    audit.HasReproducibleFlag = false;
                    audit.HasNormalizedPaths = null;
                    audit.HasSourceLink = false;
                    audit.HasEmbeddedPdb = false;
                    audit.IsDeterministic = false;
                    audit.PdbPath = null;
                    audit.PdbFormat = null;
                    audit.SourceLinkJson = null;
                    audit.RepositoryUrl = null;
                    audit.NonNormalizedPaths = null;
                }
            }
        }

        // If API is not requested, clear API surface data
        if (!options.IncludeApi)
        {
            if (result.AssemblyAudits != null)
            {
                foreach (var audit in result.AssemblyAudits)
                {
                    audit.ApiSurface = null;
                }
            }
        }

        // If neither audit nor API is requested, clear assembly audits entirely
        if (!options.IncludeAudit && !options.IncludeApi)
        {
            result.AssemblyAudits = null;
        }

        // If deps is not requested, clear runtime dependencies
        if (!options.IncludeDeps)
        {
            result.RuntimeDependencies = null;
        }
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
