using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
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
            Console.WriteLine(RenderFullApiMarkdown(api, options));
        }
    }

    private static string RenderFullApiMarkdown(ApiSurface api, ApiOptions options)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"**{api.PublicTypeCount}** types, **{api.PublicMethodCount}** methods, **{api.PublicPropertyCount}** properties");
        sb.AppendLine();
        sb.AppendLine("| Type | Kind | Members |");
        sb.AppendLine("|------|------|---------|");

        var types = api.Types.AsEnumerable();
        var totalCount = api.Types.Count;

        if (options.Limit.HasValue && options.Limit.Value < totalCount)
        {
            types = types.Take(options.Limit.Value);
        }

        foreach (var type in types)
        {
            var memberCount = type.Members?.Count ?? 0;
            var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
            sb.AppendLine($"| {fullName} | {type.Kind} | {memberCount} |");
        }

        if (options.Limit.HasValue && options.Limit.Value < totalCount)
        {
            var remaining = totalCount - options.Limit.Value;
            sb.AppendLine();
            sb.AppendLine($"*... and {remaining} more types*");
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
            Console.WriteLine(RenderTypeMarkdown(type, foundIn, options));
        }
    }

    private static string RenderTypeMarkdown(ApiType type, string? foundIn, ApiOptions options)
    {
        return options.Verbosity switch
        {
            Verbosity.Quiet => RenderTypeQuiet(type, foundIn),
            Verbosity.Minimal => RenderTypeMinimal(type, foundIn),
            _ => RenderTypeNormalOrDetailed(type, foundIn, options)
        };
    }

    private static string RenderTypeHeader(ApiType type, string? foundIn, int? memberCount = null)
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

        if (memberCount.HasValue)
        {
            sb.AppendLine($"*{string.Join(" ", modifiers)}, {memberCount} members*");
        }
        else
        {
            sb.AppendLine($"*{string.Join(" ", modifiers)}*");
        }

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

        return sb.ToString();
    }

    private static Dictionary<string, List<ApiMember>> GroupMembersByKind(ApiType type)
    {
        var members = type.Members?
            .Where(m => !IsCompilerGenerated(m.Name))
            .ToList() ?? [];

        return members
            .GroupBy(m => m.Kind)
            .OrderBy(g => GetMemberSortOrder(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static string PluralizeKind(string kind) => kind switch
    {
        "property" => "Properties",
        "method" => "Methods",
        "field" => "Fields",
        "event" => "Events",
        "constructor" => "Constructors",
        _ => char.ToUpper(kind[0]) + kind[1..] + "s"
    };

    private static string ExtractFirstParamType(string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return "";

        // Find the opening parenthesis for parameters
        var openParen = signature.IndexOf('(');
        if (openParen < 0) return "";

        var closeParen = signature.IndexOf(')', openParen);
        if (closeParen <= openParen + 1) return ""; // Empty params

        var paramsPart = signature.Substring(openParen + 1, closeParen - openParen - 1);
        if (string.IsNullOrWhiteSpace(paramsPart)) return "";

        // Get first parameter (before first comma)
        var firstParam = paramsPart.Split(',')[0].Trim();

        // Extract just the type name (last word, or handle generics)
        var parts = firstParam.Split(' ');
        var typePart = parts[0];

        // Simplify type name: remove namespace, keep just the simple name
        var dotIndex = typePart.LastIndexOf('.');
        if (dotIndex >= 0)
        {
            typePart = typePart[(dotIndex + 1)..];
        }

        // Handle generic types - take just the base name
        var genericIndex = typePart.IndexOf('<');
        if (genericIndex >= 0)
        {
            typePart = typePart[..genericIndex];
        }

        return typePart;
    }

    private static string RenderTypeQuiet(ApiType type, string? foundIn)
    {
        var sb = new StringBuilder();
        sb.Append(RenderTypeHeader(type, foundIn));

        var grouped = GroupMembersByKind(type);
        if (grouped.Count == 0) return sb.ToString().TrimEnd();

        sb.AppendLine();

        foreach (var (kind, members) in grouped)
        {
            var names = members.Select(m => m.Name).Distinct().OrderBy(n => n);
            sb.AppendLine($"**{PluralizeKind(kind)}:** {string.Join(", ", names)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderTypeMinimal(ApiType type, string? foundIn)
    {
        var sb = new StringBuilder();
        var allMembers = type.Members?.Where(m => !IsCompilerGenerated(m.Name)).ToList() ?? [];
        sb.Append(RenderTypeHeader(type, foundIn, allMembers.Count));

        var grouped = GroupMembersByKind(type);
        if (grouped.Count == 0) return sb.ToString().TrimEnd();

        sb.AppendLine();

        foreach (var (kind, members) in grouped)
        {
            // Group by name to find overloads
            var byName = members.GroupBy(m => m.Name).OrderBy(g => g.Key).ToList();

            if (kind == "method" || kind == "constructor")
            {
                foreach (var nameGroup in byName)
                {
                    var overloads = nameGroup.ToList();
                    if (overloads.Count > 1)
                    {
                        // Multiple overloads - show count and parameter hints
                        var paramHints = overloads
                            .Select(m => ExtractFirstParamType(m.Signature))
                            .Where(p => !string.IsNullOrEmpty(p))
                            .Distinct()
                            .Take(4)
                            .ToList();

                        var hintText = paramHints.Count > 0 ? $" ({string.Join(", ", paramHints)}, ...)" : "";
                        sb.AppendLine($"- **{nameGroup.Key}**: {overloads.Count} overloads{hintText}");
                    }
                    else
                    {
                        // Single method
                        sb.AppendLine($"- **{nameGroup.Key}**");
                    }
                }
            }
            else
            {
                // Properties, fields, events - just list names
                var names = byName.Select(g => g.Key);
                sb.AppendLine($"**{PluralizeKind(kind)}:** {string.Join(", ", names)}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderTypeNormalOrDetailed(ApiType type, string? foundIn, ApiOptions options)
    {
        var sb = new StringBuilder();
        sb.Append(RenderTypeHeader(type, foundIn));

        if (type.Members is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("| Member | Kind | Signature |");
            sb.AppendLine("|--------|------|-----------|");

            // Filter out compiler-generated members and sort by kind for readability
            var members = type.Members
                .Where(m => !IsCompilerGenerated(m.Name))
                .OrderBy(m => GetMemberSortOrder(m.Kind))
                .ThenBy(m => m.Name)
                .ToList();

            var totalCount = members.Count;
            var displayMembers = members.AsEnumerable();

            if (options.Limit.HasValue && options.Limit.Value < totalCount)
            {
                displayMembers = displayMembers.Take(options.Limit.Value);
            }

            foreach (var member in displayMembers)
            {
                string sig = member.Signature ?? member.ReturnType ?? "";
                // Escape pipes in signatures
                sig = sig.Replace("|", "\\|");
                sb.AppendLine($"| {member.Name} | {member.Kind} | `{sig}` |");
            }

            if (options.Limit.HasValue && options.Limit.Value < totalCount)
            {
                var remaining = totalCount - options.Limit.Value;
                sb.AppendLine();
                sb.AppendLine($"*... and {remaining} more members*");
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
        int? limit = null;
        var verbosity = Verbosity.Minimal;

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
                case "-n":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var n))
                    {
                        limit = n;
                        i++;
                    }
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
            Verbose = verbose,
            Limit = limit,
            Verbosity = verbosity
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
    public int? Limit { get; init; }
    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;
}
