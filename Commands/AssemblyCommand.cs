using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects a single .NET assembly.
/// </summary>
public class AssemblyCommand : ICommand
{
    public async Task<int> ExecuteAsync(string[] args)
    {
        var (options, assemblyPath, showHelp) = ParseOptions(args);

        if (showHelp)
        {
            return await new HelpCommand("assembly").ExecuteAsync([]);
        }

        // If --package is specified without assembly path, we'll auto-detect
        if (string.IsNullOrEmpty(assemblyPath) && string.IsNullOrEmpty(options.PackagePath))
        {
            Console.Error.WriteLine("Error: Assembly path required.");
            Console.Error.WriteLine("Run 'dotnet-inspect assembly --help' for usage.");
            return 1;
        }

        var logger = new VerboseLogger(options.Verbose);
        string? tempDir = null;

        try
        {
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                // Extract from package
                var extractResult = await ExtractFromPackageAsync(assemblyPath, options.PackagePath, logger);
                if (extractResult == null)
                {
                    return 1;
                }

                var (assemblyPaths, extractPath) = extractResult.Value;
                tempDir = Path.GetDirectoryName(extractPath);

                // Inspect all assemblies
                bool first = true;
                foreach (var targetPath in assemblyPaths)
                {
                    var audit = InspectAssembly(targetPath, options, logger);
                    if (audit == null)
                    {
                        logger.Log($"Warning: Could not read assembly: {Path.GetFileName(targetPath)}");
                        continue;
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

                var audit = InspectAssembly(assemblyPath!, options, logger);
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

    private static async Task<(List<string> assemblyPaths, string extractPath)?> ExtractFromPackageAsync(string? assemblyName, string packageSource, VerboseLogger logger)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"inspect-assembly-{Guid.NewGuid():N}");
        string extractPath = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(tempDir);

        // Check if it's a local file or a NuGet package name
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
            // Treat as NuGet package name - download from nuget.org
            // Support format: PackageName or PackageName@version
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

        // Find DLLs in the extracted package
        string[] allDlls = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);

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
                try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }

            // Return all assemblies, sorted by path
            var assemblyPaths = candidates.OrderBy(f => f).ToList();
            return (assemblyPaths, extractPath);
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
            try { Directory.Delete(tempDir, recursive: true); } catch { }
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
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }

        logger.Log($"Found: {Path.GetRelativePath(extractPath, matchingFiles[0])}");
        return ([matchingFiles[0]], extractPath);
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

    private static AssemblyAudit? InspectAssembly(string path, AssemblyOptions options, VerboseLogger logger)
    {
        logger.Log($"Inspecting: {Path.GetFileName(path)}");

        try
        {
            using FileStream stream = File.OpenRead(path);
            using PEReader peReader = new(stream);

            if (!peReader.HasMetadata)
            {
                // Native binary - still provide basic info
                return CreateNativeAudit(peReader, path);
            }

            var audit = new AssemblyAudit
            {
                FileName = Path.GetFileName(path),
                FileType = "dll"
            };

            // Always extract basic assembly info
            audit.AssemblyInfo = ExtractAssemblyInfo(peReader);

            // Audit if requested
            if (options.IncludeAudit)
            {
                AuditAssembly(peReader, audit);
            }

            return audit;
        }
        catch
        {
            return null;
        }
    }

    private static AssemblyAudit CreateNativeAudit(PEReader peReader, string path)
    {
        var audit = new AssemblyAudit
        {
            FileName = Path.GetFileName(path),
            FileType = "native"
        };

        var peHeaders = peReader.PEHeaders;
        var coffHeader = peHeaders.CoffHeader;

        audit.AssemblyInfo = new AssemblyInfo
        {
            HasCorHeader = false,
            HasManagedMetadata = false,
            HasILCode = false,
            IsExecutable = peHeaders.IsExe,
            IsDll = peHeaders.IsDll,
            Architecture = coffHeader.Machine switch
            {
                System.Reflection.PortableExecutable.Machine.I386 => "x86",
                System.Reflection.PortableExecutable.Machine.Amd64 => "x64",
                System.Reflection.PortableExecutable.Machine.Arm => "ARM",
                System.Reflection.PortableExecutable.Machine.Arm64 => "ARM64",
                _ => coffHeader.Machine.ToString()
            },
            CompilationType = "Native"
        };

        return audit;
    }

    private static void AuditAssembly(PEReader peReader, AssemblyAudit audit)
    {
        foreach (var entry in peReader.ReadDebugDirectory())
        {
            if (entry.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.Reproducible)
            {
                audit.HasReproducibleFlag = true;
            }

            if (entry.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.CodeView)
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

            if (entry.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                audit.HasEmbeddedPdb = true;
                using var provider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                var reader = provider.GetMetadataReader();

                string? sourceLink = ExtractSourceLink(reader);
                if (sourceLink != null)
                {
                    audit.HasSourceLink = true;
                    audit.SourceLinkJson = sourceLink;
                }
            }
        }

        audit.IsDeterministic = audit.HasReproducibleFlag && audit.HasNormalizedPaths != false;
    }

    private static readonly Guid SourceLinkGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    private static string? ExtractSourceLink(System.Reflection.Metadata.MetadataReader reader)
    {
        foreach (var handle in reader.CustomDebugInformation)
        {
            var info = reader.GetCustomDebugInformation(handle);
            Guid kind = reader.GetGuid(info.Kind);

            if (kind == SourceLinkGuid)
            {
                byte[] bytes = reader.GetBlobBytes(info.Value);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
        }
        return null;
    }

    private static AssemblyInfo ExtractAssemblyInfo(PEReader peReader)
    {
        var info = new AssemblyInfo();
        var peHeaders = peReader.PEHeaders;
        var coffHeader = peHeaders.CoffHeader;
        var corHeader = peHeaders.CorHeader;

        info.HasCorHeader = corHeader != null;
        info.HasManagedMetadata = peReader.HasMetadata;

        bool hasR2R = corHeader != null && corHeader.ManagedNativeHeaderDirectory.Size > 0;
        bool hasILCode = corHeader != null && peReader.HasMetadata;
        bool isILOnly = corHeader?.Flags.HasFlag(System.Reflection.PortableExecutable.CorFlags.ILOnly) == true;

        info.HasILCode = hasILCode;
        info.IsReadyToRun = hasR2R;

        if (corHeader == null)
        {
            info.CompilationType = "Native";
        }
        else if (hasR2R)
        {
            info.CompilationType = "ReadyToRun";
        }
        else if (isILOnly || hasILCode)
        {
            info.CompilationType = "CoreCLR";
        }
        else
        {
            info.CompilationType = "Unknown";
        }

        info.Architecture = coffHeader.Machine switch
        {
            System.Reflection.PortableExecutable.Machine.I386 =>
                corHeader?.Flags.HasFlag(System.Reflection.PortableExecutable.CorFlags.Requires32Bit) == true ? "x86" :
                corHeader?.Flags.HasFlag(System.Reflection.PortableExecutable.CorFlags.Prefers32Bit) == true ? "AnyCPU (32-bit preferred)" : "AnyCPU",
            System.Reflection.PortableExecutable.Machine.Amd64 => "x64",
            System.Reflection.PortableExecutable.Machine.Arm => "ARM",
            System.Reflection.PortableExecutable.Machine.Arm64 => "ARM64",
            _ => coffHeader.Machine.ToString()
        };

        info.IsAnyCpu = coffHeader.Machine == System.Reflection.PortableExecutable.Machine.I386 &&
                        corHeader?.Flags.HasFlag(System.Reflection.PortableExecutable.CorFlags.Requires32Bit) != true;
        info.Prefers32Bit = corHeader?.Flags.HasFlag(System.Reflection.PortableExecutable.CorFlags.Prefers32Bit) == true;
        info.IsSigned = corHeader?.Flags.HasFlag(System.Reflection.PortableExecutable.CorFlags.StrongNameSigned) == true;

        info.IsExecutable = peHeaders.IsExe;
        info.IsDll = peHeaders.IsDll;

        var metadataReader = peReader.GetMetadataReader();
        info.RuntimeVersion = metadataReader.MetadataVersion;

        if (metadataReader.IsAssembly)
        {
            var assemblyDef = metadataReader.GetAssemblyDefinition();
            info.AssemblyName = metadataReader.GetString(assemblyDef.Name);
            info.AssemblyVersion = assemblyDef.Version.ToString();
            info.Culture = metadataReader.GetString(assemblyDef.Culture);
            if (string.IsNullOrEmpty(info.Culture))
                info.Culture = "neutral";

            var publicKey = metadataReader.GetBlobBytes(assemblyDef.PublicKey);
            if (publicKey.Length > 0)
            {
                info.PublicKeyToken = Convert.ToHexString(publicKey.TakeLast(8).ToArray()).ToLowerInvariant();
            }
        }

        // Get target framework from custom attributes
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

    private static string? GetAttributeName(System.Reflection.Metadata.MetadataReader reader, System.Reflection.Metadata.CustomAttribute attr)
    {
        if (attr.Constructor.Kind == System.Reflection.Metadata.HandleKind.MemberReference)
        {
            var memberRef = reader.GetMemberReference((System.Reflection.Metadata.MemberReferenceHandle)attr.Constructor);
            if (memberRef.Parent.Kind == System.Reflection.Metadata.HandleKind.TypeReference)
            {
                var typeRef = reader.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)memberRef.Parent);
                string ns = reader.GetString(typeRef.Namespace);
                string name = reader.GetString(typeRef.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }
        }
        return null;
    }

    private static string? GetAttributeStringValue(System.Reflection.Metadata.MetadataReader reader, System.Reflection.Metadata.CustomAttribute attr)
    {
        try
        {
            var value = reader.GetBlobReader(attr.Value);
            value.ReadUInt16(); // Skip prolog
            return value.ReadSerializedString();
        }
        catch
        {
            return null;
        }
    }

    private static (AssemblyOptions options, string? assemblyPath, bool showHelp) ParseOptions(string[] args)
    {
        bool includeAudit = false;
        bool jsonOutput = false;
        bool verbose = false;
        bool showHelp = false;
        string? packagePath = null;
        var verbosity = Verbosity.Normal;
        HashSet<int>? includeSections = null;
        HashSet<int>? excludeSections = null;
        string? assemblyPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var lower = arg.ToLowerInvariant();

            switch (lower)
            {
                case "--audit":
                    includeAudit = true;
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
                case "--package":
                    if (i + 1 < args.Length)
                    {
                        packagePath = args[++i];
                    }
                    break;
                default:
                    if (lower.StartsWith("--package="))
                    {
                        packagePath = arg[10..];
                    }
                    else if (lower.StartsWith("-s:") || lower.StartsWith("-s="))
                    {
                        includeSections = ParseSectionList(arg[3..]);
                    }
                    else if (lower.StartsWith("-x:") || lower.StartsWith("-x="))
                    {
                        excludeSections = ParseSectionList(arg[3..]);
                    }
                    else if (!arg.StartsWith("-") && assemblyPath == null)
                    {
                        assemblyPath = arg;
                    }
                    break;
            }
        }

        var options = new AssemblyOptions
        {
            IncludeAudit = includeAudit,
            PackagePath = packagePath,
            JsonOutput = jsonOutput,
            Verbose = verbose,
            Verbosity = verbosity,
            IncludeSections = includeSections,
            ExcludeSections = excludeSections
        };

        return (options, assemblyPath, showHelp);
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
}
