using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays the public API shape of a specific type.
/// </summary>
public class ApiCommand : ICommand
{
    public async Task<int> ExecuteAsync(string[] args)
    {
        var (options, typeName, showHelp) = ParseOptions(args);

        if (showHelp)
        {
            return await new HelpCommand("api").ExecuteAsync([]);
        }

        var logger = new VerboseLogger(options.Verbose);
        string? tempDir = null;

        try
        {
            string searchPath;

            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                // Extract from package
                var extracted = await ExtractPackageAsync(options.PackagePath, logger);
                if (extracted == null)
                {
                    return 1;
                }
                (searchPath, tempDir) = extracted.Value;
            }
            else if (!string.IsNullOrEmpty(options.AssemblyPath))
            {
                // Use local assembly
                if (!File.Exists(options.AssemblyPath))
                {
                    Console.Error.WriteLine($"Error: File not found: {options.AssemblyPath}");
                    return 1;
                }
                searchPath = options.AssemblyPath;
            }
            else
            {
                Console.Error.WriteLine("Error: Must specify --package or --assembly.");
                Console.Error.WriteLine("Run 'dotnet-inspect api --help' for usage.");
                return 1;
            }

            if (string.IsNullOrEmpty(typeName))
            {
                // No type specified - list all types in the assembly
                var api = ExtractFullApi(searchPath, logger);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from assembly.");
                    return 1;
                }
                WriteFullApiOutput(api, options);
            }
            else
            {
                // Find specific type
                var (apiType, foundIn) = FindType(typeName, searchPath, logger);
                if (apiType == null)
                {
                    Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                    return 1;
                }
                WriteTypeOutput(apiType, foundIn, options);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    private static (ApiType? type, string? assembly) FindType(string typeName, string searchPath, VerboseLogger logger)
    {
        // Determine if searchPath is a file or directory
        string[] dllFiles;
        if (File.Exists(searchPath) && searchPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            dllFiles = [searchPath];
        }
        else if (Directory.Exists(searchPath))
        {
            dllFiles = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories);
        }
        else
        {
            return (null, null);
        }

        foreach (var dllFile in dllFiles)
        {
            try
            {
                using FileStream stream = File.OpenRead(dllFile);
                using PEReader peReader = new(stream);

                if (!peReader.HasMetadata)
                    continue;

                var api = ApiSurfaceExtractor.Extract(peReader);
                
                // Search for the type by full name or simple name
                var match = api.Types.FirstOrDefault(t =>
                {
                    var fullName = string.IsNullOrEmpty(t.Namespace) ? t.Name : $"{t.Namespace}.{t.Name}";
                    return fullName.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                           t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase);
                });

                if (match != null)
                {
                    logger.Log($"Found in: {Path.GetFileName(dllFile)}");
                    return (match, Path.GetFileName(dllFile));
                }
            }
            catch
            {
                // Skip unreadable files
            }
        }

        return (null, null);
    }

    private static ApiSurface? ExtractFullApi(string searchPath, VerboseLogger logger)
    {
        // Determine if searchPath is a file or directory
        string? dllFile;
        if (File.Exists(searchPath) && searchPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            dllFile = searchPath;
        }
        else if (Directory.Exists(searchPath))
        {
            // Find the first DLL in lib directory, preferring highest TFM
            var libDir = Path.Combine(searchPath, "lib");
            if (Directory.Exists(libDir))
            {
                var dlls = Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories)
                    .OrderByDescending(f => f) // Higher TFMs sort later (net9.0 > net8.0)
                    .ToList();
                dllFile = dlls.FirstOrDefault();
            }
            else
            {
                dllFile = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories).FirstOrDefault();
            }
        }
        else
        {
            return null;
        }

        if (dllFile == null)
        {
            return null;
        }

        logger.Log($"Extracting API from: {Path.GetFileName(dllFile)}");

        try
        {
            using FileStream stream = File.OpenRead(dllFile);
            using PEReader peReader = new(stream);

            if (!peReader.HasMetadata)
                return null;

            return ApiSurfaceExtractor.Extract(peReader);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteFullApiOutput(ApiSurface api, ApiOptions options)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(api, ApiJsonContext.Default.ApiSurface));
        }
        else
        {
            Console.WriteLine(RenderFullApiMarkdown(api));
        }
    }

    private static string RenderFullApiMarkdown(ApiSurface api)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"**{api.PublicTypeCount}** types, **{api.PublicMethodCount}** methods, **{api.PublicPropertyCount}** properties");
        sb.AppendLine();
        sb.AppendLine("| Type | Kind | Members |");
        sb.AppendLine("|------|------|---------|");

        foreach (var type in api.Types)
        {
            var memberCount = type.Members?.Count ?? 0;
            var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
            sb.AppendLine($"| {fullName} | {type.Kind} | {memberCount} |");
        }

        return sb.ToString().TrimEnd();
    }

    private static void WriteTypeOutput(ApiType type, string? foundIn, ApiOptions options)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(type, ApiTypeJsonContext.Default.ApiType));
        }
        else
        {
            Console.WriteLine(RenderTypeMarkdown(type, foundIn));
        }
    }

    private static string RenderTypeMarkdown(ApiType type, string? foundIn)
    {
        var sb = new StringBuilder();

        var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
        sb.AppendLine($"## {fullName}");
        sb.AppendLine();

        // Type modifiers
        var modifiers = new List<string>();
        if (type.IsStatic) modifiers.Add("static");
        if (type.IsAbstract && type.Kind == "class") modifiers.Add("abstract");
        if (type.IsSealed && type.Kind == "class") modifiers.Add("sealed");
        modifiers.Add(type.Kind);
        
        sb.AppendLine($"*{string.Join(" ", modifiers)}*");

        if (!string.IsNullOrEmpty(type.BaseType) && type.BaseType != "System.Object" && type.BaseType != "System.ValueType" && type.BaseType != "System.Enum")
        {
            sb.AppendLine($"  : {type.BaseType}");
        }

        if (type.Interfaces is { Count: > 0 })
        {
            sb.AppendLine($"  implements {string.Join(", ", type.Interfaces)}");
        }

        if (foundIn != null)
        {
            sb.AppendLine();
            sb.AppendLine($"*from {foundIn}*");
        }

        if (type.Members is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("| Member | Kind | Signature |");
            sb.AppendLine("|--------|------|-----------|");

            // Filter out compiler-generated members and sort by kind for readability
            var grouped = type.Members
                .Where(m => !IsCompilerGenerated(m.Name))
                .OrderBy(m => GetMemberSortOrder(m.Kind))
                .ThenBy(m => m.Name);

            foreach (var member in grouped)
            {
                string sig = member.Signature ?? member.ReturnType ?? "";
                // Escape pipes in signatures
                sig = sig.Replace("|", "\\|");
                sb.AppendLine($"| {member.Name} | {member.Kind} | `{sig}` |");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static bool IsCompilerGenerated(string name)
    {
        // Filter out compiler-generated and internal members
        return name.StartsWith('<') ||           // Lambda/iterator state machines
               name.StartsWith("__") ||          // Compiler internals
               name.StartsWith("s_") ||          // Static backing fields
               name.Contains("__BackingField"); // Auto-property backing fields
    }

    private static int GetMemberSortOrder(string kind) => kind switch
    {
        "constructor" => 0,
        "field" => 1,
        "property" => 2,
        "method" => 3,
        "event" => 4,
        _ => 5
    };

    private static async Task<(string extractPath, string tempDir)?> ExtractPackageAsync(string packageSource, VerboseLogger logger)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"inspect-api-{Guid.NewGuid():N}");
        string extractPath = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(tempDir);

        bool isLocalFile = packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

        if (isLocalFile)
        {
            if (!File.Exists(packageSource))
            {
                Console.Error.WriteLine($"Error: Package not found: {packageSource}");
                try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }
            logger.Log($"Extracting package: {Path.GetFileName(packageSource)}");
            ZipFile.ExtractToDirectory(packageSource, extractPath);
        }
        else
        {
            using HttpClient client = new();
            
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
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                    return null;
                }
            }

            string nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/{version}/{packageName}.{version}.nupkg";
            logger.Log($"Downloading: {packageName} {version}");

            try
            {
                byte[] packageBytes = await client.GetByteArrayAsync(nupkgUrl);
                string nupkgPath = Path.Combine(tempDir, $"{packageName}.{version}.nupkg");
                await File.WriteAllBytesAsync(nupkgPath, packageBytes);
                ZipFile.ExtractToDirectory(nupkgPath, extractPath);
                logger.Log("Package downloaded successfully.");
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"Error: Failed to download package: {ex.Message}");
                try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }
        }

        return (extractPath, tempDir);
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

    private static (ApiOptions options, string? typeName, bool showHelp) ParseOptions(string[] args)
    {
        bool jsonOutput = false;
        bool verbose = false;
        bool showHelp = false;
        string? packagePath = null;
        string? assemblyPath = null;
        string? typeName = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var lower = arg.ToLowerInvariant();

            switch (lower)
            {
                case "--json":
                    jsonOutput = true;
                    break;
                case "--markout":
                    jsonOutput = false;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--help":
                case "help":
                    showHelp = true;
                    break;
                case "--package":
                    if (i + 1 < args.Length)
                    {
                        packagePath = args[++i];
                    }
                    break;
                case "--assembly":
                    if (i + 1 < args.Length)
                    {
                        assemblyPath = args[++i];
                    }
                    break;
                default:
                    if (lower.StartsWith("--package="))
                    {
                        packagePath = arg[10..];
                    }
                    else if (lower.StartsWith("--assembly="))
                    {
                        assemblyPath = arg[11..];
                    }
                    else if (!arg.StartsWith("-") && typeName == null)
                    {
                        typeName = arg;
                    }
                    break;
            }
        }

        var options = new ApiOptions
        {
            PackagePath = packagePath,
            AssemblyPath = assemblyPath,
            JsonOutput = jsonOutput,
            Verbose = verbose
        };

        return (options, typeName, showHelp);
    }
}

/// <summary>
/// Options for the api command.
/// </summary>
public record ApiOptions
{
    public string? PackagePath { get; init; }
    public string? AssemblyPath { get; init; }
    public bool JsonOutput { get; init; }
    public bool Verbose { get; init; }
}
