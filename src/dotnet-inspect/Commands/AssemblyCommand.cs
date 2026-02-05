using System.IO.Compression;
using DotnetInspector.Packages;
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
                (packageName, packageVersion) = ParsePackageReference(options.PackagePath);
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
            var tfmAssembly = ApiCommand.FindAssemblyByTfm(extractPath, tfm);
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
                await AuditAssemblyAsync(peReader, audit, path, packageName, packageVersion, logger, httpClient, isPlatformAssembly);
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

    private static async Task AuditAssemblyAsync(
        PEReader peReader,
        AssemblyAudit audit,
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        VerboseLogger logger,
        HttpClient httpClient,
        bool isPlatformAssembly = false)
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
                audit.PdbFormat = "Portable";
                audit.PdbLocation = "Embedded";
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

        // Check for standalone PDB file (if no embedded PDB)
        if (!audit.HasEmbeddedPdb)
        {
            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (File.Exists(pdbPath))
            {
                if (IsWindowsPdb(pdbPath))
                {
                    audit.WindowsPdbDetected = true;
                    audit.PdbFormat = "Windows";
                    audit.PdbLocation = "Standalone";
                }
                else if (IsPortablePdb(pdbPath))
                {
                    audit.PdbFormat = "Portable";
                    audit.PdbLocation = "Standalone";
                    // Try to extract SourceLink from standalone PDB
                    audit.SourceLinkJson = ExtractSourceLinkFromFile(pdbPath);
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
                    using var _ = pdbResult.Provider;
                    audit.PdbFormat = "Portable";
                    audit.PdbLocation = "Symbol Package";
                    audit.SymbolServer = pdbResult.SymbolServer;

                    string? sourceLink = ExtractSourceLink(pdbResult.Reader);
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

    /// <summary>
    /// Extracts SourceLink JSON from a standalone Portable PDB file.
    /// </summary>
    private static string? ExtractSourceLinkFromFile(string pdbPath)
    {
        try
        {
            using var stream = File.OpenRead(pdbPath);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var reader = provider.GetMetadataReader();
            return ExtractSourceLink(reader);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if a PDB file is a Windows PDB (MSF format) rather than a Portable PDB.
    /// </summary>
    private static bool IsWindowsPdb(string pdbPath)
    {
        try
        {
            using var stream = File.OpenRead(pdbPath);
            byte[] header = new byte[4];
            if (stream.Read(header, 0, 4) < 4)
                return false;

            // Windows PDB starts with "Microsoft C/C++ MSF 7.00" - first 4 bytes are "Micr"
            return header[0] == 'M' && header[1] == 'i' && header[2] == 'c' && header[3] == 'r';
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a PDB file is a Portable PDB.
    /// </summary>
    private static bool IsPortablePdb(string pdbPath)
    {
        try
        {
            using var stream = File.OpenRead(pdbPath);
            byte[] header = new byte[4];
            if (stream.Read(header, 0, 4) < 4)
                return false;

            // Portable PDB starts with "BSJB"
            return header[0] == 'B' && header[1] == 'S' && header[2] == 'J' && header[3] == 'B';
        }
        catch
        {
            return false;
        }
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
            else if (attrName == "System.Reflection.AssemblyProductAttribute")
            {
                info.Product = GetAttributeStringValue(metadataReader, attr);
            }
            else if (attrName == "System.Reflection.AssemblyCompanyAttribute")
            {
                info.Company = GetAttributeStringValue(metadataReader, attr);
            }
            else if (attrName == "System.Reflection.AssemblyCopyrightAttribute")
            {
                info.Copyright = GetAttributeStringValue(metadataReader, attr);
            }
            else if (attrName == "System.Reflection.AssemblyDescriptionAttribute")
            {
                info.Description = GetAttributeStringValue(metadataReader, attr);
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
    /// Parses a package reference like "PackageName@1.0.0" or "PackageName" or "path/to/package.nupkg"
    /// </summary>
    private static (string? name, string? version) ParsePackageReference(string packageSource)
    {
        // Local file - try to extract from filename
        if (packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileNameWithoutExtension(packageSource);
            return ParsePackageFileName(fileName);
        }

        // Package name with optional version: "PackageName@1.0.0" or "PackageName"
        int atIndex = packageSource.IndexOf('@');
        if (atIndex > 0)
        {
            return (packageSource[..atIndex], packageSource[(atIndex + 1)..]);
        }

        // Just package name - version will need to be resolved
        return (packageSource, null);
    }

    /// <summary>
    /// Attempts to parse a package file name like "Microsoft.Extensions.Logging.8.0.0"
    /// </summary>
    private static (string? name, string? version) ParsePackageFileName(string fileName)
    {
        // Find the version part (first segment that starts with a digit)
        var parts = fileName.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0 && char.IsDigit(parts[i][0]))
            {
                var name = string.Join(".", parts.Take(i));
                var version = string.Join(".", parts.Skip(i));
                return (name, version);
            }
        }
        return (fileName, null);
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
