using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using DotnetInspector;

// Parse command-line arguments
bool jsonOutput = args.Contains("--json");
string[] filteredArgs = args.Where(a => a != "--json").ToArray();

if (filteredArgs.Length < 1)
{
    Console.WriteLine("Usage: dotnet-inspector <package-name> [version] [--json]");
    Console.WriteLine("   or: dotnet-inspector <path-to-nupkg> [--json]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --json    Output results as JSON");
    Console.WriteLine();
    Console.WriteLine("If version is omitted, the latest version will be used.");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet-inspector dotnet-ef");
    Console.WriteLine("  dotnet-inspector dotnet-ef 9.0.0");
    Console.WriteLine("  dotnet-inspector ./MyTool.1.0.0.nupkg --json");
    return 1;
}

// Check if first argument is a local file path
bool isLocalFile = filteredArgs.Length == 1 &&
    filteredArgs[0].EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

using HttpClient client = new();

string packageName;
string version;
string tempDir;

if (isLocalFile)
{
    string localPath = filteredArgs[0];
    if (!File.Exists(localPath))
    {
        Console.WriteLine($"Error: File not found: {localPath}");
        return 1;
    }

    string fileName = Path.GetFileNameWithoutExtension(localPath);
    packageName = fileName;
    version = "local";
    tempDir = Path.Combine(Path.GetTempPath(), $"inspect-local-{Path.GetFileName(localPath)}-{Guid.NewGuid():N}");
}
else
{
    packageName = filteredArgs[0].ToLowerInvariant();

    if (filteredArgs.Length >= 2)
    {
        version = filteredArgs[1].ToLowerInvariant();
    }
    else
    {
        // Auto-discover latest version
        string? latestVersion = await GetLatestVersionAsync(client, packageName, jsonOutput);
        if (latestVersion == null)
        {
            Console.WriteLine($"Failed to get latest version for package: {packageName}");
            return 1;
        }
        version = latestVersion;
    }

    tempDir = Path.Combine(Path.GetTempPath(), $"inspect-{packageName}-{version}-{Guid.NewGuid():N}");
}

// SourceLink GUID: CC110556-A091-4D38-9FEC-25AB9A351A6A
Guid sourceLinkGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

try
{
    Directory.CreateDirectory(tempDir);
    string extractPath = Path.Combine(tempDir, "extracted");

    if (isLocalFile)
    {
        string localPath = filteredArgs[0];
        if (!jsonOutput) Console.WriteLine($"Processing local package: {Path.GetFileName(localPath)}");
        ZipFile.ExtractToDirectory(localPath, extractPath);
    }
    else
    {
        string nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/{version}/{packageName}.{version}.nupkg";
        if (!jsonOutput) Console.WriteLine($"Downloading: {nupkgUrl}");

        byte[] packageBytes = await client.GetByteArrayAsync(nupkgUrl);
        string nupkgPath = Path.Combine(tempDir, $"{packageName}.{version}.nupkg");
        await File.WriteAllBytesAsync(nupkgPath, packageBytes);
        ZipFile.ExtractToDirectory(nupkgPath, extractPath);

        if (!jsonOutput) Console.WriteLine("Package downloaded successfully.");
    }

    // Parse package metadata
    var result = new InspectionResult
    {
        PackageName = packageName,
        Version = version
    };

    // Find and parse .nuspec file
    string[] nuspecFiles = Directory.GetFiles(extractPath, "*.nuspec", SearchOption.TopDirectoryOnly);
    if (nuspecFiles.Length > 0)
    {
        ParseNuspec(nuspecFiles[0], result);
    }

    // Analyze tools directory structure for RIDs
    string toolsDir = Path.Combine(extractPath, "tools");
    if (Directory.Exists(toolsDir))
    {
        result.IsToolPackage = true;
        AnalyzeToolsDirectory(toolsDir, result);
    }
    else
    {
        result.IsToolPackage = false;
        // Check lib directory for regular packages
        string libDir = Path.Combine(extractPath, "lib");
        if (Directory.Exists(libDir))
        {
            AnalyzeLibDirectory(libDir, result);
        }

        // Check runtimes directory for native dependencies
        string runtimesDir = Path.Combine(extractPath, "runtimes");
        if (Directory.Exists(runtimesDir))
        {
            AnalyzeRuntimesDirectory(runtimesDir, result);
        }
    }

    // Parse deps.json files for runtime dependencies
    string[] depsFiles = Directory.GetFiles(extractPath, "*.deps.json", SearchOption.AllDirectories);
    foreach (string depsFile in depsFiles)
    {
        ParseDepsJson(depsFile, result);
    }

    // Audit assemblies for SourceLink and deterministic builds
    AuditAssemblies(extractPath, result, sourceLinkGuid);

    // Output results
    if (jsonOutput)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, JsonContext.Default.InspectionResult));
    }
    else
    {
        PrintConsoleOutput(result);
    }

    return 0;
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"Failed to download package: {ex.Message}");
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

static async Task<string?> GetLatestVersionAsync(HttpClient client, string packageName, bool jsonOutput)
{
    try
    {
        string indexUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/index.json";
        if (!jsonOutput) Console.WriteLine($"Fetching versions from: {indexUrl}");

        string json = await client.GetStringAsync(indexUrl);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("versions", out var versions))
        {
            var versionList = versions.EnumerateArray().Select(v => v.GetString()).ToList();
            if (versionList.Count > 0)
            {
                string? latest = versionList[^1]; // Take the last (latest) version
                if (!jsonOutput) Console.WriteLine($"Latest version: {latest}");
                return latest;
            }
        }
    }
    catch (HttpRequestException ex)
    {
        if (!jsonOutput) Console.WriteLine($"Error fetching versions: {ex.Message}");
    }

    return null;
}

static void ParseNuspec(string nuspecPath, InspectionResult result)
{
    XDocument doc = XDocument.Load(nuspecPath);
    XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

    var metadata = doc.Root?.Element(ns + "metadata");
    if (metadata == null) return;

    result.PackageName = metadata.Element(ns + "id")?.Value ?? result.PackageName;
    result.Version = metadata.Element(ns + "version")?.Value ?? result.Version;
    result.Description = metadata.Element(ns + "description")?.Value;
    result.Authors = metadata.Element(ns + "authors")?.Value;
    result.Repository = metadata.Element(ns + "repository")?.Attribute("url")?.Value;

    // Check if it's marked as a tool
    var packageTypes = metadata.Element(ns + "packageTypes");
    if (packageTypes != null)
    {
        foreach (var pt in packageTypes.Elements(ns + "packageType"))
        {
            string? typeName = pt.Attribute("name")?.Value;
            if (typeName != null)
            {
                result.PackageTypes ??= [];
                result.PackageTypes.Add(typeName);
                if (typeName.Equals("DotnetTool", StringComparison.OrdinalIgnoreCase))
                {
                    result.IsToolPackage = true;
                }
            }
        }
    }

    // Parse dependencies
    var dependencies = metadata.Element(ns + "dependencies");
    if (dependencies != null)
    {
        foreach (var group in dependencies.Elements(ns + "group"))
        {
            string? tfm = group.Attribute("targetFramework")?.Value;
            var depGroup = new DependencyGroup { TargetFramework = tfm ?? "any" };

            foreach (var dep in group.Elements(ns + "dependency"))
            {
                depGroup.Dependencies.Add(new PackageDependency
                {
                    Id = dep.Attribute("id")?.Value ?? "",
                    Version = dep.Attribute("version")?.Value ?? ""
                });
            }

            if (depGroup.Dependencies.Count > 0)
            {
                result.DependencyGroups ??= [];
                result.DependencyGroups.Add(depGroup);
            }
        }

        // Handle dependencies without groups
        var ungroupedDeps = dependencies.Elements(ns + "dependency").ToList();
        if (ungroupedDeps.Count > 0)
        {
            var depGroup = new DependencyGroup { TargetFramework = "any" };
            foreach (var dep in ungroupedDeps)
            {
                depGroup.Dependencies.Add(new PackageDependency
                {
                    Id = dep.Attribute("id")?.Value ?? "",
                    Version = dep.Attribute("version")?.Value ?? ""
                });
            }
            result.DependencyGroups ??= [];
            result.DependencyGroups.Add(depGroup);
        }
    }
}

static void AnalyzeToolsDirectory(string toolsDir, InspectionResult result)
{
    // Tools directory structure: tools/{tfm}/{rid}/ or tools/{tfm}/any/
    foreach (string tfmDir in Directory.GetDirectories(toolsDir))
    {
        string tfm = Path.GetFileName(tfmDir);
        result.TargetFrameworks ??= [];
        if (!result.TargetFrameworks.Contains(tfm))
        {
            result.TargetFrameworks.Add(tfm);
        }

        foreach (string ridDir in Directory.GetDirectories(tfmDir))
        {
            string rid = Path.GetFileName(ridDir);
            result.SupportedRids ??= [];
            if (!result.SupportedRids.Contains(rid))
            {
                result.SupportedRids.Add(rid);
            }

            // Check if 'any' means framework-dependent
            if (rid.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                result.IsFrameworkDependent = true;
            }
            else
            {
                result.HasRidSpecificAssets = true;
            }

            // Look for executables and native files
            AnalyzeDirectoryContents(ridDir, result, rid);
        }
    }
}

static void AnalyzeLibDirectory(string libDir, InspectionResult result)
{
    foreach (string tfmDir in Directory.GetDirectories(libDir))
    {
        string tfm = Path.GetFileName(tfmDir);
        result.TargetFrameworks ??= [];
        if (!result.TargetFrameworks.Contains(tfm))
        {
            result.TargetFrameworks.Add(tfm);
        }
    }
}

static void AnalyzeRuntimesDirectory(string runtimesDir, InspectionResult result)
{
    // runtimes/{rid}/native/ or runtimes/{rid}/lib/{tfm}/
    foreach (string ridDir in Directory.GetDirectories(runtimesDir))
    {
        string rid = Path.GetFileName(ridDir);
        result.SupportedRids ??= [];
        if (!result.SupportedRids.Contains(rid))
        {
            result.SupportedRids.Add(rid);
        }
        result.HasRidSpecificAssets = true;

        // Check for native subdirectory
        string nativeDir = Path.Combine(ridDir, "native");
        if (Directory.Exists(nativeDir))
        {
            result.HasNativeDependencies = true;
            var nativeFiles = Directory.GetFiles(nativeDir);
            foreach (var file in nativeFiles)
            {
                result.NativeFiles ??= [];
                result.NativeFiles.Add($"{rid}: {Path.GetFileName(file)}");
            }
        }
    }
}

static void AnalyzeDirectoryContents(string dir, InspectionResult result, string rid)
{
    string[] files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);

    foreach (string file in files)
    {
        string fileName = Path.GetFileName(file);
        string ext = Path.GetExtension(file).ToLowerInvariant();

        // Detect native executables
        if (ext == ".exe" || ext == "" && !fileName.Contains('.'))
        {
            // Could be native executable (especially on non-Windows RIDs)
            if (!rid.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                result.HasNativeDependencies = true;
            }
        }

        // Detect native libraries
        if (ext is ".dll" or ".so" or ".dylib")
        {
            // Check if it's in a RID-specific folder (not 'any')
            if (!rid.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                // Could be native - would need PE inspection to be sure
            }
        }
    }
}

static void ParseDepsJson(string depsPath, InspectionResult result)
{
    try
    {
        string json = File.ReadAllText(depsPath);
        using var doc = JsonDocument.Parse(json);

        // Get runtime target
        if (doc.RootElement.TryGetProperty("runtimeTarget", out var runtimeTarget))
        {
            if (runtimeTarget.TryGetProperty("name", out var name))
            {
                string targetName = name.GetString() ?? "";
                // Format: .NETCoreApp,Version=v8.0/win-x64 or .NETCoreApp,Version=v8.0
                if (targetName.Contains('/'))
                {
                    string rid = targetName.Split('/')[1];
                    result.RuntimeTargetRid = rid;
                }
            }
        }

        // Get runtime dependencies
        if (doc.RootElement.TryGetProperty("libraries", out var libraries))
        {
            foreach (var lib in libraries.EnumerateObject())
            {
                string[] parts = lib.Name.Split('/');
                if (parts.Length == 2)
                {
                    if (lib.Value.TryGetProperty("type", out var typeElem))
                    {
                        string type = typeElem.GetString() ?? "";
                        if (type == "package")
                        {
                            result.RuntimeDependencies ??= [];
                            result.RuntimeDependencies.Add(new PackageDependency
                            {
                                Id = parts[0],
                                Version = parts[1]
                            });
                        }
                    }
                }
            }
        }
    }
    catch
    {
        // Ignore parse errors
    }
}

static void AuditAssemblies(string extractPath, InspectionResult result, Guid sourceLinkGuid)
{
    // Find all DLL files
    string[] dllFiles = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);

    // Find standalone PDB files
    string[] pdbFiles = Directory.GetFiles(extractPath, "*.pdb", SearchOption.AllDirectories);

    foreach (string dllFile in dllFiles)
    {
        try
        {
            var audit = AuditDll(dllFile, extractPath, sourceLinkGuid);
            if (audit != null)
            {
                result.AssemblyAudits ??= [];
                result.AssemblyAudits.Add(audit);
            }
        }
        catch
        {
            // Skip files that can't be read
        }
    }

    // Audit standalone PDBs
    foreach (string pdbFile in pdbFiles)
    {
        try
        {
            var audit = AuditStandalonePdb(pdbFile, extractPath, sourceLinkGuid);
            if (audit != null)
            {
                result.AssemblyAudits ??= [];
                result.AssemblyAudits.Add(audit);
            }
        }
        catch
        {
            // Skip files that can't be read
        }
    }

    // Calculate overall audit summary
    if (result.AssemblyAudits is { Count: > 0 })
    {
        int total = result.AssemblyAudits.Count;
        int deterministic = result.AssemblyAudits.Count(a => a.IsDeterministic);
        int hasSourceLink = result.AssemblyAudits.Count(a => a.HasSourceLink);
        int hasEmbeddedPdb = result.AssemblyAudits.Count(a => a.HasEmbeddedPdb);

        result.AuditSummary = new AuditSummary
        {
            TotalAssemblies = total,
            DeterministicCount = deterministic,
            SourceLinkCount = hasSourceLink,
            EmbeddedPdbCount = hasEmbeddedPdb,
            AllDeterministic = deterministic == total,
            AllHaveSourceLink = hasSourceLink == total
        };
    }
}

static AssemblyAudit? AuditDll(string dllPath, string extractPath, Guid sourceLinkGuid)
{
    using FileStream stream = File.OpenRead(dllPath);
    using PEReader peReader = new(stream);

    if (!peReader.HasMetadata)
        return null;

    string relativePath = Path.GetRelativePath(extractPath, dllPath);
    var audit = new AssemblyAudit
    {
        FileName = relativePath,
        FileType = "dll"
    };

    // Check debug directory entries
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

            // Check for normalized paths (deterministic builds use /_/ prefix or just filename)
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
            using MetadataReaderProvider provider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
            MetadataReader reader = provider.GetMetadataReader();

            string? sourceLink = ExtractSourceLinkFromReader(reader, sourceLinkGuid);
            if (sourceLink != null)
            {
                audit.HasSourceLink = true;
                audit.SourceLinkJson = sourceLink;

                var (pathsNormalized, nonNormalizedPaths) = CheckSourceLinkPaths(sourceLink);
                if (!pathsNormalized)
                {
                    audit.HasNormalizedPaths = false;
                    audit.NonNormalizedPaths ??= [];
                    foreach (var path in nonNormalizedPaths)
                    {
                        audit.NonNormalizedPaths.Add($"SourceLink: {path}");
                    }
                }

                // Extract repository URL from SourceLink
                audit.RepositoryUrl = ExtractRepositoryUrl(sourceLink);
            }
        }
    }

    // Determine overall deterministic status
    audit.IsDeterministic = audit.HasReproducibleFlag && audit.HasNormalizedPaths != false;

    // Extract assembly info
    audit.AssemblyInfo = ExtractAssemblyInfo(peReader);

    // Extract API surface
    audit.ApiSurface = ExtractApiSurface(peReader);

    return audit;
}

static AssemblyAudit? AuditStandalonePdb(string pdbPath, string extractPath, Guid sourceLinkGuid)
{
    using FileStream stream = File.OpenRead(pdbPath);

    byte[] header = new byte[4];
    stream.ReadExactly(header, 0, 4);
    stream.Position = 0;

    // Only handle Portable PDBs (BSJB header)
    if (header[0] != 'B' || header[1] != 'S' || header[2] != 'J' || header[3] != 'B')
    {
        return new AssemblyAudit
        {
            FileName = Path.GetRelativePath(extractPath, pdbPath),
            FileType = "pdb",
            PdbFormat = "Windows PDB (legacy)",
            HasSourceLink = false,
            IsDeterministic = false
        };
    }

    using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(stream);
    MetadataReader reader = provider.GetMetadataReader();

    string relativePath = Path.GetRelativePath(extractPath, pdbPath);
    var audit = new AssemblyAudit
    {
        FileName = relativePath,
        FileType = "pdb",
        PdbFormat = "Portable PDB"
    };

    string? sourceLink = ExtractSourceLinkFromReader(reader, sourceLinkGuid);
    if (sourceLink != null)
    {
        audit.HasSourceLink = true;
        audit.SourceLinkJson = sourceLink;

        var (pathsNormalized, nonNormalizedPaths) = CheckSourceLinkPaths(sourceLink);
        audit.HasNormalizedPaths = pathsNormalized;
        if (!pathsNormalized)
        {
            audit.NonNormalizedPaths = nonNormalizedPaths;
        }

        audit.RepositoryUrl = ExtractRepositoryUrl(sourceLink);
        audit.IsDeterministic = pathsNormalized;
    }

    return audit;
}

static string? ExtractSourceLinkFromReader(MetadataReader reader, Guid sourceLinkGuid)
{
    foreach (CustomDebugInformationHandle handle in reader.CustomDebugInformation)
    {
        CustomDebugInformation info = reader.GetCustomDebugInformation(handle);
        Guid kind = reader.GetGuid(info.Kind);

        if (kind == sourceLinkGuid)
        {
            byte[] bytes = reader.GetBlobBytes(info.Value);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }

    return null;
}

static (bool isNormalized, List<string> nonNormalizedPaths) CheckSourceLinkPaths(string sourceLink)
{
    var nonNormalizedPaths = new List<string>();
    try
    {
        using var doc = JsonDocument.Parse(sourceLink);
        if (doc.RootElement.TryGetProperty("documents", out var documents))
        {
            foreach (var prop in documents.EnumerateObject())
            {
                // Deterministic builds should have paths starting with /_
                if (!prop.Name.StartsWith("/_", StringComparison.Ordinal))
                {
                    nonNormalizedPaths.Add(prop.Name);
                }
            }
        }
        return (nonNormalizedPaths.Count == 0, nonNormalizedPaths);
    }
    catch
    {
        return (false, nonNormalizedPaths);
    }
}

static string? ExtractRepositoryUrl(string sourceLink)
{
    try
    {
        using var doc = JsonDocument.Parse(sourceLink);
        if (doc.RootElement.TryGetProperty("documents", out var documents))
        {
            foreach (var prop in documents.EnumerateObject())
            {
                string url = prop.Value.GetString() ?? "";
                // Extract base URL from SourceLink URL pattern
                // Example: https://raw.githubusercontent.com/org/repo/commit/*
                if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                {
                    // Convert raw.githubusercontent.com URL to github.com URL
                    var match = System.Text.RegularExpressions.Regex.Match(url,
                        @"https://raw\.githubusercontent\.com/([^/]+)/([^/]+)/([^/]+)/");
                    if (match.Success)
                    {
                        return $"https://github.com/{match.Groups[1].Value}/{match.Groups[2].Value}";
                    }
                }
                else if (url.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                         url.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase))
                {
                    return url.Split('_')[0].TrimEnd('/');
                }
                break; // Just use the first document URL
            }
        }
    }
    catch
    {
        // Ignore parse errors
    }
    return null;
}

static AssemblyInfo ExtractAssemblyInfo(PEReader peReader)
{
    var info = new AssemblyInfo();

    // PE Header information
    var peHeaders = peReader.PEHeaders;
    var coffHeader = peHeaders.CoffHeader;
    var corHeader = peHeaders.CorHeader;

    // Determine architecture
    info.Architecture = coffHeader.Machine switch
    {
        Machine.I386 => corHeader?.Flags.HasFlag(CorFlags.Requires32Bit) == true ? "x86" :
                        corHeader?.Flags.HasFlag(CorFlags.Prefers32Bit) == true ? "AnyCPU (32-bit preferred)" : "AnyCPU",
        Machine.Amd64 => "x64",
        Machine.Arm => "ARM",
        Machine.Arm64 => "ARM64",
        _ => coffHeader.Machine.ToString()
    };

    info.IsAnyCpu = coffHeader.Machine == Machine.I386 &&
                    corHeader?.Flags.HasFlag(CorFlags.Requires32Bit) != true;
    info.Prefers32Bit = corHeader?.Flags.HasFlag(CorFlags.Prefers32Bit) == true;
    info.IsSigned = corHeader?.Flags.HasFlag(CorFlags.StrongNameSigned) == true;

    // Determine if executable or DLL
    info.IsExecutable = peHeaders.IsExe;
    info.IsDll = peHeaders.IsDll;

    // Get metadata
    var metadataReader = peReader.GetMetadataReader();

    // Runtime version
    info.RuntimeVersion = metadataReader.MetadataVersion;
    info.MetadataVersion = metadataReader.GetTableRowCount(TableIndex.Module);

    // Check for unsafe code by looking for System.Security.UnverifiableCodeAttribute
    // or by checking if the assembly references pointers
    info.HasUnsafeCode = CheckForUnsafeCode(metadataReader);

    // Assembly definition
    if (metadataReader.IsAssembly)
    {
        var assemblyDef = metadataReader.GetAssemblyDefinition();
        info.AssemblyName = metadataReader.GetString(assemblyDef.Name);
        info.AssemblyVersion = assemblyDef.Version.ToString();
        info.Culture = metadataReader.GetString(assemblyDef.Culture);
        if (string.IsNullOrEmpty(info.Culture))
            info.Culture = "neutral";

        // Public key token
        var publicKey = metadataReader.GetBlobBytes(assemblyDef.PublicKey);
        if (publicKey.Length > 0)
        {
            info.PublicKeyToken = Convert.ToHexString(publicKey.TakeLast(8).ToArray()).ToLowerInvariant();
        }
    }

    // Get custom attributes for additional info
    foreach (var attrHandle in metadataReader.CustomAttributes)
    {
        var attr = metadataReader.GetCustomAttribute(attrHandle);
        string? attrName = GetAttributeName(metadataReader, attr);

        if (attrName == "System.Runtime.Versioning.TargetFrameworkAttribute")
        {
            info.TargetFramework = GetAttributeStringValue(metadataReader, attr);
        }
        else if (attrName == "System.Reflection.AssemblyFileVersionAttribute")
        {
            info.FileVersion = GetAttributeStringValue(metadataReader, attr);
        }
        else if (attrName == "System.Reflection.AssemblyInformationalVersionAttribute")
        {
            info.InformationalVersion = GetAttributeStringValue(metadataReader, attr);
        }
    }

    return info;
}

static bool CheckForUnsafeCode(MetadataReader reader)
{
    // Check for UnverifiableCodeAttribute which indicates unsafe code
    foreach (var attrHandle in reader.CustomAttributes)
    {
        var attr = reader.GetCustomAttribute(attrHandle);
        string? attrName = GetAttributeName(reader, attr);
        if (attrName == "System.Security.UnverifiableCodeAttribute")
        {
            return true;
        }
    }
    return false;
}

static string? GetAttributeName(MetadataReader reader, CustomAttribute attr)
{
    if (attr.Constructor.Kind == HandleKind.MemberReference)
    {
        var memberRef = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
        if (memberRef.Parent.Kind == HandleKind.TypeReference)
        {
            var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
            string ns = reader.GetString(typeRef.Namespace);
            string name = reader.GetString(typeRef.Name);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }
    }
    else if (attr.Constructor.Kind == HandleKind.MethodDefinition)
    {
        var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor);
        var typeDef = reader.GetTypeDefinition(methodDef.GetDeclaringType());
        string ns = reader.GetString(typeDef.Namespace);
        string name = reader.GetString(typeDef.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }
    return null;
}

static string? GetAttributeStringValue(MetadataReader reader, CustomAttribute attr)
{
    try
    {
        var value = reader.GetBlobReader(attr.Value);
        // Skip prolog (2 bytes)
        value.ReadUInt16();
        // Read the string value
        return value.ReadSerializedString();
    }
    catch
    {
        return null;
    }
}

static ApiSurface ExtractApiSurface(PEReader peReader)
{
    var surface = new ApiSurface();
    var reader = peReader.GetMetadataReader();

    foreach (var typeDefHandle in reader.TypeDefinitions)
    {
        var typeDef = reader.GetTypeDefinition(typeDefHandle);
        var attributes = typeDef.Attributes;

        // Only include public types
        bool isPublic = (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public ||
                        (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic;

        if (!isPublic)
            continue;

        string typeName = reader.GetString(typeDef.Name);

        // Skip compiler-generated types
        if (typeName.StartsWith("<") || typeName.StartsWith("__"))
            continue;

        var apiType = new ApiType
        {
            Namespace = reader.GetString(typeDef.Namespace),
            Name = typeName,
            IsSealed = (attributes & TypeAttributes.Sealed) != 0,
            IsAbstract = (attributes & TypeAttributes.Abstract) != 0,
        };

        // Determine kind
        if ((attributes & TypeAttributes.Interface) != 0)
        {
            apiType.Kind = "interface";
        }
        else if (!typeDef.BaseType.IsNil)
        {
            string? baseTypeName = GetTypeName(reader, typeDef.BaseType);
            apiType.BaseType = baseTypeName;

            apiType.Kind = baseTypeName switch
            {
                "System.Enum" => "enum",
                "System.ValueType" => "struct",
                "System.Delegate" or "System.MulticastDelegate" => "delegate",
                _ => "class"
            };
        }
        else
        {
            apiType.Kind = "class";
        }

        apiType.IsStatic = apiType.IsSealed && apiType.IsAbstract;

        // Get interfaces
        var interfaces = typeDef.GetInterfaceImplementations();
        if (interfaces.Count > 0)
        {
            apiType.Interfaces = [];
            foreach (var ifaceHandle in interfaces)
            {
                var iface = reader.GetInterfaceImplementation(ifaceHandle);
                string? ifaceName = GetTypeName(reader, iface.Interface);
                if (ifaceName != null)
                    apiType.Interfaces.Add(ifaceName);
            }
        }

        // Get public members
        apiType.Members = [];

        // Methods
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if ((method.Attributes & MethodAttributes.Public) == 0)
                continue;

            string methodName = reader.GetString(method.Name);

            // Skip property accessors and event accessors
            if (methodName.StartsWith("get_") || methodName.StartsWith("set_") ||
                methodName.StartsWith("add_") || methodName.StartsWith("remove_"))
                continue;

            var member = new ApiMember
            {
                Name = methodName,
                Kind = methodName == ".ctor" ? "constructor" : "method",
                IsStatic = (method.Attributes & MethodAttributes.Static) != 0,
                IsVirtual = (method.Attributes & MethodAttributes.Virtual) != 0,
                IsAbstract = (method.Attributes & MethodAttributes.Abstract) != 0,
                Signature = GetMethodSignature(reader, method)
            };

            apiType.Members.Add(member);
            surface.PublicMethodCount++;
        }

        // Properties
        foreach (var propHandle in typeDef.GetProperties())
        {
            var prop = reader.GetPropertyDefinition(propHandle);
            var accessors = prop.GetAccessors();

            // Check if any accessor is public
            bool isPublicProp = false;
            if (!accessors.Getter.IsNil)
            {
                var getter = reader.GetMethodDefinition(accessors.Getter);
                isPublicProp = (getter.Attributes & MethodAttributes.Public) != 0;
            }
            if (!isPublicProp && !accessors.Setter.IsNil)
            {
                var setter = reader.GetMethodDefinition(accessors.Setter);
                isPublicProp = (setter.Attributes & MethodAttributes.Public) != 0;
            }

            if (!isPublicProp)
                continue;

            var member = new ApiMember
            {
                Name = reader.GetString(prop.Name),
                Kind = "property",
                Signature = GetPropertySignature(reader, prop)
            };

            apiType.Members.Add(member);
            surface.PublicPropertyCount++;
        }

        // Fields (only public non-backing fields)
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.Public) == 0)
                continue;

            string fieldName = reader.GetString(field.Name);
            if (fieldName.StartsWith("<"))
                continue; // Skip backing fields

            var member = new ApiMember
            {
                Name = fieldName,
                Kind = "field",
                IsStatic = (field.Attributes & FieldAttributes.Static) != 0
            };

            apiType.Members.Add(member);
            surface.PublicFieldCount++;
        }

        // Events
        foreach (var eventHandle in typeDef.GetEvents())
        {
            var evt = reader.GetEventDefinition(eventHandle);
            var accessors = evt.GetAccessors();

            // Check if adder is public
            if (accessors.Adder.IsNil)
                continue;

            var adder = reader.GetMethodDefinition(accessors.Adder);
            if ((adder.Attributes & MethodAttributes.Public) == 0)
                continue;

            var member = new ApiMember
            {
                Name = reader.GetString(evt.Name),
                Kind = "event",
                IsStatic = (adder.Attributes & MethodAttributes.Static) != 0
            };

            apiType.Members.Add(member);
            surface.PublicEventCount++;
        }

        surface.Types.Add(apiType);
        surface.PublicTypeCount++;
    }

    return surface;
}

static string? GetTypeName(MetadataReader reader, EntityHandle handle)
{
    if (handle.Kind == HandleKind.TypeReference)
    {
        var typeRef = reader.GetTypeReference((TypeReferenceHandle)handle);
        string ns = reader.GetString(typeRef.Namespace);
        string name = reader.GetString(typeRef.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }
    else if (handle.Kind == HandleKind.TypeDefinition)
    {
        var typeDef = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
        string ns = reader.GetString(typeDef.Namespace);
        string name = reader.GetString(typeDef.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }
    else if (handle.Kind == HandleKind.TypeSpecification)
    {
        // Generic type - simplified handling
        return "(generic)";
    }
    return null;
}

static string GetMethodSignature(MetadataReader reader, MethodDefinition method)
{
    string name = reader.GetString(method.Name);
    var signature = method.DecodeSignature(new SignatureTypeProvider(), null);

    var parameters = signature.ParameterTypes.Select((p, i) => p).ToList();
    string paramStr = string.Join(", ", parameters);

    return $"{signature.ReturnType} {name}({paramStr})";
}

static string GetPropertySignature(MetadataReader reader, PropertyDefinition prop)
{
    string name = reader.GetString(prop.Name);
    var signature = prop.DecodeSignature(new SignatureTypeProvider(), null);
    return $"{signature.ReturnType} {name}";
}

static void PrintConsoleOutput(InspectionResult result)
{
    Console.WriteLine();
    Console.WriteLine($"Package: {result.PackageName}");
    Console.WriteLine($"Version: {result.Version}");

    if (result.Description != null)
    {
        Console.WriteLine($"Description: {result.Description}");
    }

    if (result.Authors != null)
    {
        Console.WriteLine($"Authors: {result.Authors}");
    }

    if (result.Repository != null)
    {
        Console.WriteLine($"Repository: {result.Repository}");
    }

    Console.WriteLine();
    Console.WriteLine($"Is Tool Package: {(result.IsToolPackage ? "Yes" : "No")}");

    if (result.PackageTypes is { Count: > 0 })
    {
        Console.WriteLine($"Package Types: {string.Join(", ", result.PackageTypes)}");
    }

    Console.WriteLine();
    Console.WriteLine("=== RID Analysis ===");

    if (result.TargetFrameworks is { Count: > 0 })
    {
        Console.WriteLine($"Target Frameworks: {string.Join(", ", result.TargetFrameworks)}");
    }

    if (result.SupportedRids is { Count: > 0 })
    {
        Console.WriteLine($"Supported RIDs: {string.Join(", ", result.SupportedRids)}");

        // Categorize RIDs
        var windowsRids = result.SupportedRids.Where(r => r.StartsWith("win", StringComparison.OrdinalIgnoreCase)).ToList();
        var linuxRids = result.SupportedRids.Where(r => r.StartsWith("linux", StringComparison.OrdinalIgnoreCase)).ToList();
        var osxRids = result.SupportedRids.Where(r => r.StartsWith("osx", StringComparison.OrdinalIgnoreCase)).ToList();
        var otherRids = result.SupportedRids.Where(r =>
            !r.StartsWith("win", StringComparison.OrdinalIgnoreCase) &&
            !r.StartsWith("linux", StringComparison.OrdinalIgnoreCase) &&
            !r.StartsWith("osx", StringComparison.OrdinalIgnoreCase) &&
            !r.Equals("any", StringComparison.OrdinalIgnoreCase)).ToList();

        if (windowsRids.Count > 0) Console.WriteLine($"  Windows: {string.Join(", ", windowsRids)}");
        if (linuxRids.Count > 0) Console.WriteLine($"  Linux: {string.Join(", ", linuxRids)}");
        if (osxRids.Count > 0) Console.WriteLine($"  macOS: {string.Join(", ", osxRids)}");
        if (otherRids.Count > 0) Console.WriteLine($"  Other: {string.Join(", ", otherRids)}");
        if (result.SupportedRids.Contains("any")) Console.WriteLine($"  Portable: any");
    }
    else
    {
        Console.WriteLine("Supported RIDs: (none - framework-dependent only)");
    }

    Console.WriteLine();
    Console.WriteLine($"Framework-Dependent: {(result.IsFrameworkDependent ? "Yes" : "No")}");
    Console.WriteLine($"Has RID-Specific Assets: {(result.HasRidSpecificAssets ? "Yes" : "No")}");
    Console.WriteLine($"Has Native Dependencies: {(result.HasNativeDependencies ? "Yes" : "No")}");

    if (result.RuntimeTargetRid != null)
    {
        Console.WriteLine($"Runtime Target RID (from deps.json): {result.RuntimeTargetRid}");
    }

    if (result.NativeFiles is { Count: > 0 })
    {
        Console.WriteLine();
        Console.WriteLine("Native Files:");
        foreach (var file in result.NativeFiles)
        {
            Console.WriteLine($"  {file}");
        }
    }

    // Assembly Audit Section
    if (result.AuditSummary != null)
    {
        Console.WriteLine();
        Console.WriteLine("=== Assembly Audit ===");
        Console.WriteLine($"Total Assemblies: {result.AuditSummary.TotalAssemblies}");
        Console.WriteLine($"Deterministic: {result.AuditSummary.DeterministicCount}/{result.AuditSummary.TotalAssemblies} {(result.AuditSummary.AllDeterministic ? "✓" : "✗")}");
        Console.WriteLine($"Has SourceLink: {result.AuditSummary.SourceLinkCount}/{result.AuditSummary.TotalAssemblies} {(result.AuditSummary.AllHaveSourceLink ? "✓" : "✗")}");
        Console.WriteLine($"Embedded PDB: {result.AuditSummary.EmbeddedPdbCount}/{result.AuditSummary.TotalAssemblies}");
    }

    if (result.AssemblyAudits is { Count: > 0 })
    {
        Console.WriteLine();
        foreach (var audit in result.AssemblyAudits)
        {
            string status = audit.IsDeterministic ? "✓" : "✗";
            string sourceLink = audit.HasSourceLink ? "SourceLink" : "No SourceLink";
            string pdbInfo = audit.HasEmbeddedPdb ? "Embedded PDB" : (audit.PdbFormat ?? "");

            Console.WriteLine($"  [{status}] {audit.FileName}");
            Console.WriteLine($"      Deterministic: {(audit.IsDeterministic ? "Yes" : "No")} | Reproducible Flag: {(audit.HasReproducibleFlag ? "Yes" : "No")} | {sourceLink}");

            if (audit.HasEmbeddedPdb)
            {
                Console.WriteLine($"      PDB: Embedded Portable PDB");
            }
            else if (audit.PdbFormat != null)
            {
                Console.WriteLine($"      PDB: {audit.PdbFormat}");
            }

            if (audit.RepositoryUrl != null)
            {
                Console.WriteLine($"      Repository: {audit.RepositoryUrl}");
            }

            // Assembly Info
            if (audit.AssemblyInfo != null)
            {
                var info = audit.AssemblyInfo;
                Console.WriteLine($"      Architecture: {info.Architecture}{(info.IsSigned ? " | Signed" : "")}{(info.HasUnsafeCode ? " | Unsafe" : "")}");
                if (info.TargetFramework != null)
                {
                    Console.WriteLine($"      Target Framework: {info.TargetFramework}");
                }
                if (info.AssemblyVersion != null)
                {
                    Console.WriteLine($"      Version: {info.AssemblyVersion}");
                }
            }

            // API Surface summary
            if (audit.ApiSurface != null)
            {
                var api = audit.ApiSurface;
                Console.WriteLine($"      API: {api.PublicTypeCount} types, {api.PublicMethodCount} methods, {api.PublicPropertyCount} properties");
            }

            if (audit.NonNormalizedPaths is { Count: > 0 })
            {
                Console.WriteLine($"      Non-normalized paths:");
                foreach (var path in audit.NonNormalizedPaths.Take(3))
                {
                    Console.WriteLine($"        {path}");
                }
                if (audit.NonNormalizedPaths.Count > 3)
                {
                    Console.WriteLine($"        ... and {audit.NonNormalizedPaths.Count - 3} more");
                }
            }
        }
    }

    if (result.DependencyGroups is { Count: > 0 })
    {
        Console.WriteLine();
        Console.WriteLine("=== Package Dependencies (from nuspec) ===");
        foreach (var group in result.DependencyGroups)
        {
            Console.WriteLine($"[{group.TargetFramework}]");
            foreach (var dep in group.Dependencies)
            {
                Console.WriteLine($"  {dep.Id} {dep.Version}");
            }
        }
    }

    if (result.RuntimeDependencies is { Count: > 0 })
    {
        Console.WriteLine();
        Console.WriteLine("=== Runtime Dependencies (from deps.json) ===");
        foreach (var dep in result.RuntimeDependencies.OrderBy(d => d.Id))
        {
            Console.WriteLine($"  {dep.Id} {dep.Version}");
        }
    }
}
