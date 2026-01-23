using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using DotnetInspector;
using MarkdownData;

// Parse command-line arguments
bool jsonOutput = args.Contains("--json");
bool verbose = args.Contains("--verbose") || args.Contains("-v");
bool showHelp = args.Contains("--help") || args.Contains("help");
string[] filteredArgs = args.Where(a => a != "--mdf" && a != "--json" && a != "--verbose" && a != "-v" && a != "--help" && a != "help").ToArray();

if (showHelp || filteredArgs.Length < 1)
{
    Console.WriteLine("Usage: dotnet-inspect <package-name> [version] [--mdf|--json]");
    Console.WriteLine("   or: dotnet-inspect <path-to-nupkg> [--mdf|--json]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --mdf       Output results as Markdown Data Format (default)");
    Console.WriteLine("  --json      Output results as JSON");
    Console.WriteLine("  --verbose   Show progress messages on stderr");
    Console.WriteLine("  --help      Show this help message");
    Console.WriteLine();
    Console.WriteLine("If version is omitted, the latest version will be used.");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet-inspect dotnet-ef");
    Console.WriteLine("  dotnet-inspect dotnet-ef 9.0.0");
    Console.WriteLine("  dotnet-inspect ./MyTool.1.0.0.nupkg --json");
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
    packageName = filteredArgs[0].ToLowerInvariant();

    if (filteredArgs.Length >= 2)
    {
        version = filteredArgs[1].ToLowerInvariant();
    }
    else
    {
        // Auto-discover latest version
        string? latestVersion = await GetLatestVersionAsync(client, packageName, verbose);
        if (latestVersion == null)
        {
            Console.Error.WriteLine($"Failed to get latest version for package: {packageName}");
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
        if (verbose) Console.Error.WriteLine($"Processing local package: {Path.GetFileName(localPath)}");
        ZipFile.ExtractToDirectory(localPath, extractPath);
    }
    else
    {
        string nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/{version}/{packageName}.{version}.nupkg";
        if (verbose) Console.Error.WriteLine($"Downloading: {nupkgUrl}");

        byte[] packageBytes = await client.GetByteArrayAsync(nupkgUrl);
        string nupkgPath = Path.Combine(tempDir, $"{packageName}.{version}.nupkg");
        await File.WriteAllBytesAsync(nupkgPath, packageBytes);
        ZipFile.ExtractToDirectory(nupkgPath, extractPath);

        if (verbose) Console.Error.WriteLine("Package downloaded successfully.");
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

    // Verify RID-specific packages exist (for RID-specific pointer packages)
    if (result.IsRidSpecificPointerPackage && result.RuntimeIdentifierPackages is { Count: > 0 })
    {
        string? localDir = isLocalFile ? Path.GetDirectoryName(Path.GetFullPath(filteredArgs[0])) : null;
        await VerifyRidPackagesAsync(client, result, result.Version, localDir, verbose);
    }

    // Output results
    if (jsonOutput)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, JsonContext.Default.InspectionResult));
    }
    else
    {
        // Default: MDF output
        Console.WriteLine(MdfSerializer.Serialize(result, new MdfContext { BoldFieldNames = true }));
    }

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

static async Task<string?> GetLatestVersionAsync(HttpClient client, string packageName, bool verbose)
{
    try
    {
        string indexUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/index.json";
        if (verbose) Console.Error.WriteLine($"Fetching versions from: {indexUrl}");

        string json = await client.GetStringAsync(indexUrl);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("versions", out var versions))
        {
            var versionList = versions.EnumerateArray().Select(v => v.GetString()).ToList();
            if (versionList.Count > 0)
            {
                string? latest = versionList[^1]; // Take the last (latest) version
                if (verbose) Console.Error.WriteLine($"Latest version: {latest}");
                return latest;
            }
        }
    }
    catch (HttpRequestException ex)
    {
        if (verbose) Console.Error.WriteLine($"Error fetching versions: {ex.Message}");
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
    // First, check for DotnetToolSettings.xml to detect RID-specific tool format
    string[] settingsFiles = Directory.GetFiles(toolsDir, "DotnetToolSettings.xml", SearchOption.AllDirectories);

    foreach (string settingsFile in settingsFiles)
    {
        try
        {
            var doc = XDocument.Load(settingsFile);
            var root = doc.Root;

            // Check for Version="2" (RID-specific tool format)
            string? version = root?.Attribute("Version")?.Value;
            if (version == "2")
            {
                result.ToolFormat = "DotNetCliTool Version=\"2\" (RID-specific)";
                result.IsRidSpecificPointerPackage = true;
                result.IsFrameworkDependent = false; // RID packages are self-contained
                result.HasRidSpecificAssets = true;

                // Extract commands
                var commands = root?.Element("Commands")?.Elements("Command");
                if (commands != null)
                {
                    result.ToolCommands = commands
                        .Select(c => c.Attribute("Name")?.Value)
                        .Where(n => n != null)
                        .Cast<string>()
                        .ToList();
                }

                // Extract RuntimeIdentifierPackages
                var ridPackages = root?.Element("RuntimeIdentifierPackages")?.Elements("RuntimeIdentifierPackage");
                if (ridPackages != null)
                {
                    result.RuntimeIdentifierPackages = ridPackages
                        .Select(r => new RidPackageReference
                        {
                            RuntimeIdentifier = r.Attribute("RuntimeIdentifier")?.Value ?? "",
                            PackageId = r.Attribute("Id")?.Value ?? ""
                        })
                        .ToList();

                    // Also populate SupportedRids from the RuntimeIdentifierPackages
                    result.SupportedRids = result.RuntimeIdentifierPackages
                        .Select(r => r.RuntimeIdentifier)
                        .ToList();
                }
            }
            else if (version == "1" || version == null)
            {
                result.ToolFormat = "DotNetCliTool Version=\"1\" (portable)";

                // Extract commands for v1 format too
                var commands = root?.Element("Commands")?.Elements("Command");
                if (commands != null)
                {
                    result.ToolCommands = commands
                        .Select(c => c.Attribute("Name")?.Value)
                        .Where(n => n != null)
                        .Cast<string>()
                        .ToList();
                }
            }
        }
        catch
        {
            // Ignore parse errors for settings files
        }
    }

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
            // But don't override if we already detected this is a RID-specific pointer package
            if (rid.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                if (!result.IsRidSpecificPointerPackage)
                {
                    result.IsFrameworkDependent = true;
                }
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

    string relativePath = Path.GetRelativePath(extractPath, dllPath);

    // Handle native binaries (no managed metadata)
    if (!peReader.HasMetadata)
    {
        return AuditNativeBinary(peReader, relativePath);
    }

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

static AssemblyAudit AuditNativeBinary(PEReader peReader, string relativePath)
{
    var audit = new AssemblyAudit
    {
        FileName = relativePath,
        FileType = "native"
    };

    var peHeaders = peReader.PEHeaders;
    var coffHeader = peHeaders.CoffHeader;

    // Create AssemblyInfo for native binaries
    var info = new AssemblyInfo
    {
        HasCorHeader = false,
        HasManagedMetadata = false,
        HasILCode = false,
        IsExecutable = peHeaders.IsExe,
        IsDll = peHeaders.IsDll
    };

    // Determine architecture
    info.Architecture = coffHeader.Machine switch
    {
        Machine.I386 => "x86",
        Machine.Amd64 => "x64",
        Machine.Arm => "ARM",
        Machine.Arm64 => "ARM64",
        _ => coffHeader.Machine.ToString()
    };

    // Detect if this is a NativeAOT binary by checking for specific indicators
    // NativeAOT binaries have characteristic import patterns and may have
    // specific sections or symbols
    bool isNativeAot = DetectNativeAot(peReader);

    info.IsNativeAot = isNativeAot;
    info.CompilationType = isNativeAot ? "NativeAOT" : "Native";

    audit.AssemblyInfo = info;
    return audit;
}

static bool DetectNativeAot(PEReader peReader)
{
    // NativeAOT detection heuristics:
    // 1. Check for RhpNewFast or other CoreRT/NativeAOT runtime symbols in exports
    // 2. Check for .NET AOT-specific sections
    // 3. Check import table for patterns

    try
    {
        var peHeaders = peReader.PEHeaders;

        // Check the PE sections for NativeAOT-specific patterns
        foreach (var section in peHeaders.SectionHeaders)
        {
            string sectionName = section.Name;

            // NativeAOT uses specific section names like ".managed" or keeps ".text"
            // but the key indicator is the absence of .NET metadata combined with
            // characteristic runtime patterns

            // Look for hydrated/frozen object sections (NativeAOT specific)
            if (sectionName == ".data" || sectionName == ".rdata")
            {
                // NativeAOT typically has frozen objects in data sections
                // This is a soft indicator
            }
        }

        // Check the import directory for NativeAOT patterns
        // NativeAOT links against OS APIs directly rather than mscoree.dll or coreclr.dll
        var importDir = peHeaders.PEHeader?.ImportTableDirectory;
        if (importDir is { Size: > 0 })
        {
            // If we find imports, check they're not CLR-related
            // NativeAOT won't import from mscoree.dll, coreclr.dll, or clrjit.dll
            // This would require reading the import table which is more complex

            // For now, use a simpler heuristic: if no COR header and no metadata,
            // but the binary size is substantial, it could be NativeAOT
        }

        // Check for debug directory entries that might indicate NativeAOT
        foreach (var entry in peReader.ReadDebugDirectory())
        {
            // NativeAOT binaries may have reproducible/deterministic markers
            if (entry.Type == DebugDirectoryEntryType.Reproducible)
            {
                // Having reproducible flag without metadata suggests NativeAOT
                // (since regular native builds rarely have this)
                return true;
            }
        }
    }
    catch
    {
        // If we can't analyze, default to unknown
    }

    // Without more specific indicators, we can't definitively say it's NativeAOT
    // A pure native binary (C/C++) would look similar
    return false;
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

    // Check for COR header presence (managed code indicator)
    info.HasCorHeader = corHeader != null;
    info.HasManagedMetadata = peReader.HasMetadata;

    // Check for ReadyToRun (R2R) compilation - has both IL and native code
    // R2R binaries have a ManagedNativeHeader directory in the COR header
    bool hasR2R = false;
    if (corHeader != null)
    {
        // R2R assemblies have a non-empty ManagedNativeHeaderDirectory
        var managedNativeHeader = corHeader.ManagedNativeHeaderDirectory;
        hasR2R = managedNativeHeader.Size > 0;
    }

    // Check for IL code - CoreCLR has IL, NativeAOT strips it
    // The ILOnly flag indicates pure IL with no native code
    bool hasILCode = corHeader != null && peReader.HasMetadata;
    bool isILOnly = corHeader?.Flags.HasFlag(CorFlags.ILOnly) == true;

    info.HasILCode = hasILCode;
    info.IsReadyToRun = hasR2R;

    // Determine compilation type
    if (corHeader == null)
    {
        // No COR header = native code
        // Could be NativeAOT or pure native (C/C++)
        // NativeAOT binaries are native but were compiled from .NET
        info.CompilationType = "Native";
        info.IsNativeAot = false; // Can't definitively say it's NAOT without more info
    }
    else if (hasR2R)
    {
        info.CompilationType = "ReadyToRun";
        info.IsNativeAot = false;
    }
    else if (isILOnly)
    {
        info.CompilationType = "CoreCLR";
        info.IsNativeAot = false;
    }
    else if (hasILCode)
    {
        // Has COR header and metadata but not IL-only
        // This could be mixed-mode or potentially NativeAOT with stubs
        info.CompilationType = "CoreCLR";
        info.IsNativeAot = false;
    }
    else
    {
        // Has COR header but no metadata = unusual, likely corrupted
        info.CompilationType = "Unknown";
        info.IsNativeAot = false;
    }

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

static async Task VerifyRidPackagesAsync(HttpClient client, InspectionResult result, string version, string? localDir, bool verbose)
{
    if (result.RuntimeIdentifierPackages == null)
        return;

    foreach (var ridPkg in result.RuntimeIdentifierPackages)
    {
        if (localDir != null)
        {
            // Local verification: check if sibling .nupkg file exists
            string expectedFileName = $"{ridPkg.PackageId}.{version}.nupkg";
            string expectedPath = Path.Combine(localDir, expectedFileName);
            ridPkg.Exists = File.Exists(expectedPath);

            if (verbose)
            {
                string status = ridPkg.Exists == true ? "found" : "NOT FOUND";
                Console.Error.WriteLine($"  {ridPkg.RuntimeIdentifier}: {status} ({expectedFileName})");
            }
        }
        else
        {
            // Remote verification: check NuGet API
            string packageId = ridPkg.PackageId.ToLowerInvariant();
            string checkVersion = version.ToLowerInvariant();
            string url = $"https://api.nuget.org/v3-flatcontainer/{packageId}/{checkVersion}/{packageId}.nuspec";

            try
            {
                var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                ridPkg.Exists = response.IsSuccessStatusCode;

                if (verbose)
                {
                    string status = ridPkg.Exists == true ? "available" : "NOT FOUND";
                    Console.Error.WriteLine($"  {ridPkg.RuntimeIdentifier}: {status} ({ridPkg.PackageId} {version})");
                }
            }
            catch
            {
                ridPkg.Exists = false;
                if (verbose)
                {
                    Console.Error.WriteLine($"  {ridPkg.RuntimeIdentifier}: ERROR checking ({ridPkg.PackageId} {version})");
                }
            }
        }
    }
}
