using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays the public API shape of a specific type.
/// </summary>
public class ApiCommand
{
    public static async Task<int> ExecuteAsync(string? typeName, ApiOptions options)
    {
        if (options.MemberFilter?.Count > 0 && string.IsNullOrEmpty(typeName))
        {
            Console.Error.WriteLine("Error: --member requires a type argument.");
            Console.Error.WriteLine("Usage: dotnet-inspect api <type> --package <pkg> --member <name>");
            return 1;
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

                // If --tfm is specified, find assembly by TFM
                if (!string.IsNullOrEmpty(options.Tfm))
                {
                    var tfmAssembly = FindAssemblyByTfm(searchPath, options.Tfm);
                    if (tfmAssembly == null)
                    {
                        Console.Error.WriteLine($"Error: No assembly found for TFM '{options.Tfm}'.");
                        Console.Error.WriteLine("Available TFMs:");
                        var dlls = GetPackageDlls(searchPath);
                        var tfms = dlls
                            .Select(d => ExtractTfmFromPath(Path.GetRelativePath(searchPath, d).Replace('\\', '/')))
                            .Where(t => t != null)
                            .Distinct()
                            .OrderByDescending(t => GetTfmPriority(t!));
                        foreach (var tfm in tfms)
                        {
                            Console.Error.WriteLine($"  {tfm}");
                        }
                        return 1;
                    }
                    searchPath = tfmAssembly;
                    logger.Log($"Using TFM: {options.Tfm}");
                }
                // If --assembly is also specified, use it to select a specific DLL within the package
                else if (!string.IsNullOrEmpty(options.AssemblyPath))
                {
                    var targetPath = Path.Combine(searchPath, options.AssemblyPath.Replace('\\', '/'));
                    if (!File.Exists(targetPath))
                    {
                        Console.Error.WriteLine($"Error: Assembly '{options.AssemblyPath}' not found in package.");
                        Console.Error.WriteLine("Available assemblies:");
                        foreach (var dll in GetPackageDlls(searchPath))
                        {
                            Console.Error.WriteLine($"  {Path.GetRelativePath(searchPath, dll)}");
                        }
                        return 1;
                    }
                    searchPath = targetPath;
                }
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

            string? selectedTfm = null;
            if (string.IsNullOrEmpty(typeName))
            {
                // No type specified - check if we need to auto-select TFM
                if (Directory.Exists(searchPath))
                {
                    var dlls = GetPackageDlls(searchPath);
                    if (dlls.Count > 1)
                    {
                        // Auto-select highest TFM
                        var (selectedPath, tfm) = SelectHighestTfmAssembly(dlls, searchPath);
                        if (selectedPath != null)
                        {
                            searchPath = selectedPath;
                            selectedTfm = tfm;
                            logger.Log($"Auto-selected TFM: {tfm}");
                        }
                        else
                        {
                            // No TFM pattern found - require explicit selection
                            Console.Error.WriteLine("Error: Multiple assemblies found. Please specify one with --assembly or --tfm:");
                            foreach (var dll in dlls)
                            {
                                Console.Error.WriteLine($"  {Path.GetRelativePath(searchPath, dll)}");
                            }
                            Console.Error.WriteLine();
                            Console.Error.WriteLine("Usage: dotnet-inspect api [type] --package <pkg> --assembly <path>");
                            return 1;
                        }
                    }
                }

                // List all types in the assembly
                var api = ExtractFullApi(searchPath, logger, options.IncludeAll);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from assembly.");
                    return 1;
                }
                WriteFullApiOutput(api, options, selectedTfm);
            }
            else
            {
                // Find specific type
                var (apiType, foundIn, dllPath) = FindType(typeName, searchPath, logger, options.IncludeAll);
                if (apiType == null || dllPath == null)
                {
                    Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                    return 1;
                }

                // Enrich with source info if requested
                if (options.ShowSourceUrl || options.ShowDocs)
                {
                    await EnrichTypeWithSourceInfoAsync(apiType, typeName, dllPath, options, logger);
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

    /// <summary>
    /// Extracts a specific type from a package or assembly. Used by TypeCommand.
    /// </summary>
    internal static async Task<(ApiType? type, string? foundIn)> ExtractTypeAsync(string typeName, ApiOptions options, VerboseLogger logger)
    {
        string? tempDir = null;
        try
        {
            string searchPath;

            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                var extracted = await ExtractPackageAsync(options.PackagePath, logger);
                if (extracted == null)
                    return (null, null);
                
                (searchPath, tempDir) = extracted.Value;

                if (!string.IsNullOrEmpty(options.Tfm))
                {
                    var tfmAssembly = FindAssemblyByTfm(searchPath, options.Tfm);
                    if (tfmAssembly != null)
                        searchPath = tfmAssembly;
                }
                else
                {
                    var (highestPath, _) = SelectHighestTfmAssembly(GetPackageDlls(searchPath), searchPath);
                    if (highestPath != null)
                        searchPath = highestPath;
                }
            }
            else if (!string.IsNullOrEmpty(options.AssemblyPath))
            {
                searchPath = options.AssemblyPath;
            }
            else
            {
                return (null, null);
            }

            var (apiType, foundIn, _) = FindType(typeName, searchPath, logger, options.IncludeAll);
            return (apiType, foundIn);
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    private static (ApiType? type, string? assembly, string? dllPath) FindType(string typeName, string searchPath, VerboseLogger logger, bool includeAll)
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
            return (null, null, null);
        }

        foreach (var dllFile in dllFiles)
        {
            try
            {
                using FileStream stream = File.OpenRead(dllFile);
                using PEReader peReader = new(stream);

                if (!peReader.HasMetadata)
                    continue;

                var api = ApiSurfaceExtractor.Extract(peReader, includeAll);

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
                    return (match, Path.GetFileName(dllFile), dllFile);
                }
            }
            catch
            {
                // Skip unreadable files
            }
        }

        return (null, null, null);
    }

    private static async Task EnrichTypeWithSourceInfoAsync(ApiType apiType, string typeName, string dllPath, ApiOptions options, VerboseLogger logger)
    {
        try
        {
            using FileStream stream = File.OpenRead(dllPath);
            using PEReader peReader = new(stream);

            if (!peReader.HasMetadata)
            {
                logger.Log("No metadata in assembly, cannot resolve source.");
                return;
            }

            // Find embedded PDB
            MetadataReaderProvider? pdbProvider = null;
            foreach (var entry in peReader.ReadDebugDirectory())
            {
                if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
                {
                    pdbProvider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                    break;
                }
            }

            if (pdbProvider == null)
            {
                logger.Log("No embedded PDB found, cannot resolve source.");
                return;
            }

            using var _ = pdbProvider;
            var pdbReader = pdbProvider.GetMetadataReader();
            var metadataReader = peReader.GetMetadataReader();

            // Create source link resolver
            var resolver = SourceLinkResolver.Create(pdbReader);
            if (resolver == null)
            {
                logger.Log("No SourceLink information found in PDB.");
                return;
            }

            // Find the TypeDefinitionHandle for the type
            TypeDefinitionHandle? typeHandle = FindTypeDefinitionHandle(metadataReader, typeName);
            if (typeHandle == null)
            {
                logger.Log($"Could not find type definition for '{typeName}'.");
                return;
            }

            // Resolve source info for the type
            var sourceInfo = resolver.ResolveTypeSource(metadataReader, pdbReader, typeHandle.Value);
            if (sourceInfo != null)
            {
                apiType.SourceFilePath = sourceInfo.SourceFilePath;
                apiType.SourceUrl = sourceInfo.SourceUrl;
                apiType.GitHubBrowseUrl = sourceInfo.GitHubBrowseUrl;
                apiType.SourceLineNumber = sourceInfo.LineNumber;
                logger.Log($"Source: {sourceInfo.SourceFilePath}:{sourceInfo.LineNumber}");
            }

            // Fetch docs if requested
            if (options.ShowDocs && sourceInfo?.SourceUrl != null)
            {
                var fetcher = new SourceFetcher();
                string? sourceContent = await fetcher.FetchSourceAsync(sourceInfo.SourceUrl);

                if (sourceContent != null)
                {
                    var parser = new DocCommentParser();

                    // Parse type documentation
                    var typeDoc = parser.ExtractTypeDocComment(sourceContent, apiType.Name);
                    if (typeDoc != null)
                    {
                        apiType.Documentation = new DocComment
                        {
                            Summary = typeDoc.Summary,
                            Remarks = typeDoc.Remarks,
                            Parameters = typeDoc.Parameters,
                            Returns = typeDoc.Returns
                        };
                        logger.Log("Extracted type documentation.");
                    }

                    // Parse member documentation if filtering by member
                    if (options.MemberFilter?.Count > 0 && apiType.Members != null)
                    {
                        foreach (var member in apiType.Members.Where(m => options.MemberFilter.Contains(m.Name)))
                        {
                            var memberDoc = parser.ExtractMemberDocComment(sourceContent, apiType.Name, member.Name);
                            if (memberDoc != null)
                            {
                                member.Documentation = new DocComment
                                {
                                    Summary = memberDoc.Summary,
                                    Remarks = memberDoc.Remarks,
                                    Parameters = memberDoc.Parameters,
                                    Returns = memberDoc.Returns
                                };
                            }
                        }
                    }
                }
                else
                {
                    logger.Log($"Could not fetch source from: {sourceInfo.SourceUrl}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Error enriching source info: {ex.Message}");
        }
    }

    private static TypeDefinitionHandle? FindTypeDefinitionHandle(MetadataReader reader, string typeName)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            string name = reader.GetString(typeDef.Name);
            string ns = reader.GetString(typeDef.Namespace);
            string fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

            if (fullName.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                return typeHandle;
            }
        }
        return null;
    }

    private static List<string> GetPackageDlls(string extractPath)
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
            candidates = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);
        }

        return candidates.OrderBy(f => f).ToList();
    }

    private static (string? path, string? tfm) SelectHighestTfmAssembly(List<string> dlls, string extractPath)
    {
        // Filter out resource DLLs
        dlls = dlls.Where(d => !d.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)).ToList();

        // Group DLLs by TFM extracted from path (e.g., lib/net8.0/Foo.dll -> net8.0)
        var byTfm = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in dlls)
        {
            var relativePath = Path.GetRelativePath(extractPath, dll).Replace('\\', '/');
            var tfm = ExtractTfmFromPath(relativePath);
            if (tfm != null)
            {
                if (!byTfm.TryGetValue(tfm, out var list))
                {
                    list = [];
                    byTfm[tfm] = list;
                }
                list.Add(dll);
            }
        }

        if (byTfm.Count == 0)
            return (null, null);

        // Sort TFMs by version (highest first)
        var sortedTfms = byTfm.Keys
            .Select(tfm => (tfm, priority: GetTfmPriority(tfm)))
            .OrderByDescending(x => x.priority)
            .ToList();

        var highestTfm = sortedTfms[0].tfm;
        var assemblies = byTfm[highestTfm];

        // Prefer DLLs directly in the TFM folder (not in locale subdirectories)
        var directDll = assemblies.FirstOrDefault(d =>
        {
            var relativePath = Path.GetRelativePath(extractPath, d).Replace('\\', '/');
            var parts = relativePath.Split('/');
            // lib/net8.0/Foo.dll has 3 parts, lib/net8.0/cs/Foo.dll has 4
            return parts.Length <= 3;
        });

        return (directDll ?? assemblies[0], highestTfm);
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
               (folderName.Contains('.') || char.IsDigit(folderName[3]));
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

    internal static string? FindAssemblyByTfm(string extractPath, string tfm)
    {
        var libDir = Path.Combine(extractPath, "lib");
        var toolsDir = Path.Combine(extractPath, "tools");

        // Try lib directory first
        if (Directory.Exists(libDir))
        {
            var tfmDir = Path.Combine(libDir, tfm);
            if (Directory.Exists(tfmDir))
            {
                var dlls = Directory.GetFiles(tfmDir, "*.dll");
                if (dlls.Length > 0)
                    return dlls[0];
            }
        }

        // Try tools directory
        if (Directory.Exists(toolsDir))
        {
            // tools/net8.0/any/Foo.dll pattern
            var searchPattern = $"*{tfm}*";
            var dlls = Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories)
                .Where(f => f.Replace('\\', '/').Contains($"/{tfm}/", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (dlls.Count > 0)
                return dlls[0];
        }

        return null;
    }

    private static ApiSurface? ExtractFullApi(string searchPath, VerboseLogger logger, bool includeAll)
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
                var dlls = Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories).ToList();
                var (selectedPath, selectedTfm) = SelectHighestTfmAssembly(dlls, searchPath);
                dllFile = selectedPath;
                if (selectedTfm != null)
                {
                    logger.Log($"Auto-selected TFM: {selectedTfm}");
                }
            }
            else
            {
                // Fall back to first DLL found if no lib directory
                var dlls = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories).ToList();
                var (selectedPath, _) = SelectHighestTfmAssembly(dlls, searchPath);
                dllFile = selectedPath ?? dlls.FirstOrDefault();
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

            return ApiSurfaceExtractor.Extract(peReader, includeAll);
        }
        catch
        {
            return null;
        }
    }

    private static string SerializeJson<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        return JsonSerializer.Serialize(value, typeInfo);
    }

    private static void WriteFullApiOutput(ApiSurface api, ApiOptions options, string? selectedTfm = null)
    {
        // Apply type filter if specified
        if (!string.IsNullOrEmpty(options.TypeFilter))
        {
            api.Types = api.Types.Where(t =>
            {
                var fullName = string.IsNullOrEmpty(t.Namespace) ? t.Name : $"{t.Namespace}.{t.Name}";
                return MatchesGlobPattern(fullName, options.TypeFilter) ||
                       MatchesGlobPattern(t.Name, options.TypeFilter);
            }).ToList();
            api.PublicTypeCount = api.Types.Count;
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(SerializeJson(api, ApiJsonContext.Default.ApiSurface));
        }
        else
        {
            Console.WriteLine(RenderFullApiMarkdown(api, options, selectedTfm));
        }
    }

    private static string RenderFullApiMarkdown(ApiSurface api, ApiOptions options, string? selectedTfm = null)
    {
        var sb = new StringBuilder();

        var types = api.Types.AsEnumerable();
        var totalCount = api.Types.Count;

        sb.AppendLine($"**{totalCount}** types, **{api.PublicMethodCount}** methods, **{api.PublicPropertyCount}** properties");
        if (selectedTfm != null)
        {
            sb.AppendLine($"*using {selectedTfm} (auto-selected highest TFM)*");
        }
        sb.AppendLine();
        sb.AppendLine("| Type | Kind | Members |");
        sb.AppendLine("|------|------|---------|");

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

    private static bool MatchesGlobPattern(string text, string pattern)
    {
        // Convert glob pattern to regex
        // * matches any characters, ? matches single character
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static void WriteTypeOutput(ApiType type, string? foundIn, ApiOptions options)
    {
        // Check for member filter miss and warn
        if (options.MemberFilter?.Count > 0 && type.Members != null)
        {
            var matchingMembers = type.Members
                .Where(m => options.MemberFilter.Contains(m.Name))
                .ToList();

            if (matchingMembers.Count == 0)
            {
                Console.Error.WriteLine($"Warning: No members matched filter '{string.Join(", ", options.MemberFilter)}'");
            }
        }

        if (options.JsonOutput)
        {
            var outputType = type;
            var members = type.Members;

            // Apply member filter
            if (options.MemberFilter?.Count > 0 && members != null)
            {
                members = members
                    .Where(m => options.MemberFilter.Contains(m.Name))
                    .ToList();
            }

            // Apply -n limit to JSON output
            if (options.Limit.HasValue && members != null && members.Count > options.Limit.Value)
            {
                members = members.Take(options.Limit.Value).ToList();
            }

            if (members != type.Members)
            {
                outputType = new ApiType
                {
                    Namespace = type.Namespace,
                    Name = type.Name,
                    Kind = type.Kind,
                    IsSealed = type.IsSealed,
                    IsAbstract = type.IsAbstract,
                    IsStatic = type.IsStatic,
                    BaseType = type.BaseType,
                    Interfaces = type.Interfaces,
                    Members = members,
                    // Preserve source info
                    SourceFilePath = type.SourceFilePath,
                    SourceUrl = type.SourceUrl,
                    GitHubBrowseUrl = type.GitHubBrowseUrl,
                    SourceLineNumber = type.SourceLineNumber,
                    Documentation = type.Documentation
                };
            }

            if (options.CompactJson)
            {
                Console.WriteLine(SerializeJson(outputType, ApiTypeCompactJsonContext.Default.ApiType));
            }
            else
            {
                Console.WriteLine(SerializeJson(outputType, ApiTypeJsonContext.Default.ApiType));
            }
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
            Verbosity.Quiet => RenderTypeQuiet(type, foundIn, options),
            Verbosity.Minimal => RenderTypeMinimal(type, foundIn, options),
            _ => RenderTypeNormalOrDetailed(type, foundIn, options)
        };
    }

    private static string RenderTypeHeader(ApiType type, string? foundIn, ApiOptions options, int? memberCount = null)
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

        if (options.ShowInterfaces && type.Interfaces is { Count: > 0 })
        {
            sb.AppendLine($"  implements {string.Join(", ", type.Interfaces)}");
        }

        if (foundIn != null)
        {
            sb.AppendLine();
            sb.AppendLine($"*from {foundIn}*");
        }

        // Show source link if available
        if (options.ShowSourceUrl && type.GitHubBrowseUrl != null)
        {
            sb.AppendLine();
            string fileName = Path.GetFileName(type.SourceFilePath ?? "source");
            string linkText = type.SourceLineNumber.HasValue
                ? $"{fileName}:{type.SourceLineNumber}"
                : fileName;
            sb.AppendLine($"**Source:** [{linkText}]({type.GitHubBrowseUrl})");
        }

        // Show documentation summary if available
        if (options.ShowDocs && type.Documentation?.Summary != null)
        {
            sb.AppendLine();
            sb.AppendLine($"> {type.Documentation.Summary}");
        }

        return sb.ToString();
    }

    private static Dictionary<string, List<ApiMember>> GroupMembersByKind(ApiType type, HashSet<string>? memberFilter = null)
    {
        var members = type.Members?
            .Where(m => !IsCompilerGenerated(m.Name))
            .ToList() ?? [];

        if (memberFilter?.Count > 0)
        {
            members = members.Where(m => memberFilter.Contains(m.Name)).ToList();
        }

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

    private static string RenderTypeQuiet(ApiType type, string? foundIn, ApiOptions options)
    {
        var sb = new StringBuilder();
        sb.Append(RenderTypeHeader(type, foundIn, options));

        var grouped = GroupMembersByKind(type, options.MemberFilter);
        if (grouped.Count == 0) return sb.ToString().TrimEnd();

        sb.AppendLine();

        // When filtering by member, show compact signatures instead of just names
        if (options.MemberFilter?.Count > 0)
        {
            foreach (var (kind, members) in grouped)
            {
                foreach (var member in members.OrderBy(m => m.Signature ?? m.Name))
                {
                    sb.AppendLine($"- `{member.Signature ?? member.Name}`");

                    // Show member documentation if available (when --docs is used with member filter)
                    if (options.ShowDocs && member.Documentation?.Summary != null)
                    {
                        sb.AppendLine($"  > {member.Documentation.Summary}");
                    }
                }
            }
        }
        else
        {
            foreach (var (kind, members) in grouped)
            {
                var names = members.Select(m => m.Name).Distinct().OrderBy(n => n);
                sb.AppendLine($"**{PluralizeKind(kind)}:** {string.Join(", ", names)}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderTypeMinimal(ApiType type, string? foundIn, ApiOptions options)
    {
        var sb = new StringBuilder();
        var allMembers = type.Members?.Where(m => !IsCompilerGenerated(m.Name)).ToList() ?? [];
        sb.Append(RenderTypeHeader(type, foundIn, options, allMembers.Count));

        var grouped = GroupMembersByKind(type, options.MemberFilter);
        if (grouped.Count == 0) return sb.ToString().TrimEnd();

        sb.AppendLine();

        // When using member filter with --docs, show detailed output with docs
        if (options.MemberFilter?.Count > 0 && options.ShowDocs)
        {
            foreach (var (kind, members) in grouped)
            {
                foreach (var member in members.OrderBy(m => m.Signature ?? m.Name))
                {
                    sb.AppendLine($"- `{member.Signature ?? member.Name}`");

                    if (member.Documentation?.Summary != null)
                    {
                        sb.AppendLine($"  > {member.Documentation.Summary}");
                    }
                }
            }
            return sb.ToString().TrimEnd();
        }

        foreach (var (kind, members) in grouped)
        {
            // Group by name to find overloads
            var byName = members.GroupBy(m => m.Name).OrderBy(g => g.Key).ToList();

            if (kind == "method" || kind == "constructor")
            {
                // Add section header for methods/constructors
                sb.AppendLine($"**{PluralizeKind(kind)}:**");
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

        // In signatures-only mode, skip the header entirely
        if (!options.SignaturesOnly)
        {
            sb.Append(RenderTypeHeader(type, foundIn, options));
        }

        if (type.Members is { Count: > 0 })
        {
            if (!options.SignaturesOnly)
            {
                sb.AppendLine();
                sb.AppendLine("| Member | Kind | Signature |");
                sb.AppendLine("|--------|------|-----------|");
            }

            // Filter out compiler-generated members and sort by kind for readability
            var members = type.Members
                .Where(m => !IsCompilerGenerated(m.Name))
                .OrderBy(m => GetMemberSortOrder(m.Kind))
                .ThenBy(m => m.Name)
                .ToList();

            // Apply member filter if specified
            if (options.MemberFilter?.Count > 0)
            {
                members = members.Where(m => options.MemberFilter.Contains(m.Name)).ToList();
            }

            var totalCount = members.Count;
            var displayMembers = members.AsEnumerable();

            if (options.Limit.HasValue && options.Limit.Value < totalCount)
            {
                displayMembers = displayMembers.Take(options.Limit.Value);
            }

            foreach (var member in displayMembers)
            {
                string sig = member.Signature ?? member.ReturnType ?? "";

                if (options.SignaturesOnly)
                {
                    // Plain signature output, one per line
                    sb.AppendLine(sig);
                }
                else
                {
                    // Escape pipes in signatures for markdown table
                    sig = sig.Replace("|", "\\|");
                    sb.AppendLine($"| {member.Name} | {member.Kind} | `{sig}` |");

                    // Show member documentation if available (when --docs is used with member filter)
                    if (member.Documentation?.Summary != null)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"> {member.Documentation.Summary}");
                        sb.AppendLine();
                    }
                }
            }

            if (!options.SignaturesOnly && options.Limit.HasValue && options.Limit.Value < totalCount)
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

    private static async Task<(string extractPath, string? tempDir)?> ExtractPackageAsync(string packageSource, VerboseLogger logger)
    {
        bool isLocalFile = packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

        if (isLocalFile)
        {
            if (!File.Exists(packageSource))
            {
                Console.Error.WriteLine($"Error: Package not found: {packageSource}");
                return null;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), $"inspect-api-{Guid.NewGuid():N}");
            string extractPath = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(tempDir);

            logger.Log($"Extracting package: {Path.GetFileName(packageSource)}");
            ZipFile.ExtractToDirectory(packageSource, extractPath);
            return (extractPath, tempDir);
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
                    return null;
                }
            }

            // Check NuGet cache first
            var cachedPath = NuGetCache.TryGetCachedPackage(packageName, version);
            if (cachedPath != null && NuGetCache.IsCachedPackageValid(cachedPath))
            {
                logger.Log($"Using cached package: {cachedPath}");
                return (cachedPath, null); // null tempDir means don't delete
            }

            string tempDir = Path.Combine(Path.GetTempPath(), $"inspect-api-{Guid.NewGuid():N}");
            string extractPath = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(tempDir);

            string nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/{version}/{packageName}.{version}.nupkg";
            logger.Log($"Downloading: {packageName} {version}");

            try
            {
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
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.Error.WriteLine($"Error: Package '{packageName}' version '{version}' not found on nuget.org.");
                Console.Error.WriteLine("Use 'dotnet-inspect package <name> --versions' to list available versions.");
                try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"Error: Failed to download package: {ex.Message}");
                try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }

            return (extractPath, tempDir);
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

}

/// <summary>
/// Options for the api command.
/// </summary>
public record ApiOptions
{
    public string? PackagePath { get; init; }
    public string? AssemblyPath { get; init; }
    public string? Tfm { get; init; }
    public bool JsonOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool Verbose { get; init; }
    public int? Limit { get; init; }
    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;
    public HashSet<string>? MemberFilter { get; init; }
    public bool ShowSourceUrl { get; init; }
    public bool ShowDocs { get; init; }
    public bool ShowInterfaces { get; init; }
    public bool IncludeAll { get; init; }
    public string? TypeFilter { get; init; }
    public bool SignaturesOnly { get; init; }
}
