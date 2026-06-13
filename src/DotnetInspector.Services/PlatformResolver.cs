using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using NuGet.Versioning;

namespace DotnetInspector.Services;

/// <summary>
/// Resolves .NET platform/framework installation paths and discovers available assemblies.
/// </summary>
public static class PlatformResolver
{
    /// <summary>
    /// Process-lifetime cache for the parameterless GetInstalledFrameworks() overload.
    /// Framework installations don't change during a CLI invocation.
    /// </summary>
    private static List<FrameworkInfo>? _cachedFrameworks;

    /// <summary>
    /// Returns true if the name looks like a platform assembly.
    /// </summary>
    public static bool IsPlatformCandidate(string name)
        => name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
           name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tries to parse a fully qualified type name (e.g., "System.Text.Json.JsonSerializer")
    /// into a platform assembly name and type name by probing installed packs.
    /// Splits at each '.' from right to left, checking if the left part resolves as a platform assembly.
    /// </summary>
    public static bool TryParseQualifiedTypeName(string qualifiedName, out string assemblyName, out string typeName)
    {
        assemblyName = null!;
        typeName = null!;

        // Must have at least 3 dots: e.g., System.Text.Json.JsonSerializer
        // (System.X is an assembly, System.X.Y could be assembly or type — need at least System.X.Y.Z)
        int dotCount = 0;
        for (int i = 0; i < qualifiedName.Length; i++)
            if (qualifiedName[i] == '.') dotCount++;
        if (dotCount < 2) return false;

        // Try splitting from right to left
        for (int i = qualifiedName.Length - 1; i >= 0; i--)
        {
            if (qualifiedName[i] != '.') continue;

            var candidateAssembly = qualifiedName[..i];
            var candidateType = qualifiedName[(i + 1)..];

            if (string.IsNullOrEmpty(candidateAssembly) || string.IsNullOrEmpty(candidateType))
                continue;

            if (!IsPlatformCandidate(candidateAssembly))
                continue;

            // Check if this assembly exists in installed packs (pure disk, no network)
            var result = ResolveAssembly(candidateAssembly);
            if (result.AssemblyPath != null)
            {
                assemblyName = candidateAssembly;
                typeName = candidateType;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Framework short names mapped to ref pack directory names.
    /// </summary>
    public static readonly Dictionary<string, string> FrameworkMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["runtime"] = "Microsoft.NETCore.App.Ref",
        ["aspnetcore"] = "Microsoft.AspNetCore.App.Ref",
        ["netstandard"] = "NETStandard.Library.Ref"
    };

    /// <summary>
    /// Gets the reverse mapping from ref pack names to short names.
    /// </summary>
    public static readonly Dictionary<string, string> ReverseFrameworkMappings =
        FrameworkMappings.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Discovers the .NET SDK packs directory (first match).
    /// </summary>
    public static string? GetPacksDirectory()
    {
        var dirs = GetAllPacksDirectories();
        return dirs.Count > 0 ? dirs[0] : null;
    }

    /// <summary>
    /// Returns all valid packs directories in priority order.
    /// On Linux, the app cache is searched first (Microsoft packs preferred over distro builds).
    /// On Windows/macOS, the app cache is searched last (local SDK preferred).
    /// </summary>
    public static List<string> GetAllPacksDirectories()
    {
        List<string> result = [];
        foreach (var candidate in GetPacksDirectoryCandidates())
        {
            if (Directory.Exists(candidate) && !result.Contains(candidate))
            {
                result.Add(candidate);
            }
        }
        return result;
    }

    /// <summary>
    /// Discovers the .NET shared runtime directory.
    /// </summary>
    public static string? GetSharedDirectory()
    {
        var candidates = GetSharedDirectoryCandidates();
        
        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetSharedDirectoryCandidates()
    {
        // DOTNET_ROOT environment variable takes highest priority (works on all platforms)
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot, "shared");
        }

        var currentRuntimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        var currentFrameworkDirectory = string.IsNullOrEmpty(currentRuntimeDirectory)
            ? null
            : Path.GetDirectoryName(currentRuntimeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (currentFrameworkDirectory != null &&
            Path.GetFileName(currentFrameworkDirectory).Equals("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase))
        {
            var currentSharedDirectory = Path.GetDirectoryName(currentFrameworkDirectory);
            if (!string.IsNullOrEmpty(currentSharedDirectory))
                yield return currentSharedDirectory;
        }

        // Check /etc/dotnet/install_location on non-Windows (distro-configured path)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var installLocation = ReadInstallLocation();
            if (!string.IsNullOrEmpty(installLocation))
            {
                yield return Path.Combine(installLocation, "shared");
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return @"C:\Program Files\dotnet\shared";
            yield return @"C:\Program Files (x86)\dotnet\shared";
            
            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            if (!string.IsNullOrEmpty(programFiles))
            {
                yield return Path.Combine(programFiles, "dotnet", "shared");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/usr/local/share/dotnet/shared";
            yield return "/opt/homebrew/share/dotnet/shared";
            
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".dotnet", "shared");
            }
        }
        else // Linux and others
        {
            yield return "/usr/lib/dotnet/shared";
            yield return "/usr/share/dotnet/shared";
            yield return "/usr/local/share/dotnet/shared";
            yield return "/opt/dotnet/shared";
            
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".dotnet", "shared");
                yield return Path.Combine(home, "dotnet", "shared");
            }
        }
    }

    /// <summary>
    /// Framework short names mapped to shared runtime directory names.
    /// </summary>
    private static readonly Dictionary<string, string> SharedFrameworkMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["runtime"] = "Microsoft.NETCore.App",
        ["aspnetcore"] = "Microsoft.AspNetCore.App"
    };

    private static IEnumerable<string> GetPacksDirectoryCandidates()
    {
        // App cache packs first (always preferred)
        var appCachePacks = PlatformPackService.GetPacksCachePath();
        if (appCachePacks != null)
        {
            yield return appCachePacks;
        }

        // SDK-installed packs (same paths as shared, but "packs" instead of "shared")
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot, "packs");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var installLocation = ReadInstallLocation();
            if (!string.IsNullOrEmpty(installLocation))
            {
                yield return Path.Combine(installLocation, "packs");
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return @"C:\Program Files\dotnet\packs";
            yield return @"C:\Program Files (x86)\dotnet\packs";

            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            if (!string.IsNullOrEmpty(programFiles))
            {
                yield return Path.Combine(programFiles, "dotnet", "packs");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/usr/local/share/dotnet/packs";
            yield return "/opt/homebrew/share/dotnet/packs";

            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".dotnet", "packs");
            }
        }
        else // Linux and others
        {
            yield return "/usr/lib/dotnet/packs";
            yield return "/usr/share/dotnet/packs";
            yield return "/usr/local/share/dotnet/packs";
            yield return "/opt/dotnet/packs";

            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".dotnet", "packs");
                yield return Path.Combine(home, "dotnet", "packs");
            }
        }
    }

    /// <summary>
    /// Discovers all installed frameworks with their versions across all packs directories.
    /// </summary>
    public static List<FrameworkInfo> GetInstalledFrameworks(string? packsDirectory = null)
    {
        // Use process-lifetime cache for the common no-arg path
        if (packsDirectory == null)
        {
            var cached = Volatile.Read(ref _cachedFrameworks);
            if (cached != null)
                return cached;
        }

        var result = GetInstalledFrameworksCore(packsDirectory);

        if (packsDirectory == null)
            Volatile.Write(ref _cachedFrameworks, result);

        return result;
    }

    private static List<FrameworkInfo> GetInstalledFrameworksCore(string? packsDirectory)
    {
        List<string> packsDirs;
        if (packsDirectory != null)
        {
            packsDirs = Directory.Exists(packsDirectory) ? [packsDirectory] : [];
        }
        else
        {
            packsDirs = GetAllPacksDirectories();
        }

        if (packsDirs.Count == 0)
        {
            return [];
        }

        // Merge frameworks across all packs directories
        Dictionary<string, FrameworkInfo> frameworkMap = new(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in packsDirs)
        {
            foreach (var (shortName, refPackName) in FrameworkMappings)
            {
                var refPackPath = Path.Combine(dir, refPackName);
                if (!Directory.Exists(refPackPath))
                    continue;

                var versions = GetInstalledVersions(refPackPath);
                if (versions.Count == 0)
                    continue;

                if (frameworkMap.TryGetValue(shortName, out var existing))
                {
                    // Merge versions from additional packs directories
                    var allVersions = existing.AllVersions.Union(versions).Distinct()
                        .OrderByDescending(v => ParseVersion(v)).ToList();
                    existing.AllVersions = allVersions;
                    
                    // Update path if the newest version comes from this directory
                    if (allVersions[0] != existing.LatestVersion)
                    {
                        existing.LatestVersion = allVersions[0];
                        existing.Path = refPackPath;
                        var latestRefPath = GetRefAssemblyPath(refPackPath, existing.LatestVersion);
                        existing.AssemblyCount = latestRefPath != null ? CountAssemblies(latestRefPath) : 0;
                    }
                }
                else
                {
                    var latestVersion = versions[0];
                    var latestRefPath = GetRefAssemblyPath(refPackPath, latestVersion);
                    var assemblyCount = latestRefPath != null ? CountAssemblies(latestRefPath) : 0;

                    frameworkMap[shortName] = new FrameworkInfo
                    {
                        ShortName = shortName,
                        RefPackName = refPackName,
                        LatestVersion = latestVersion,
                        AllVersions = versions,
                        AssemblyCount = assemblyCount,
                        Path = refPackPath
                    };
                }
            }
        }

        return [.. frameworkMap.Values];
    }

    /// <summary>
    /// Gets all installed versions for a framework, sorted by version descending (latest first).
    /// </summary>
    public static List<string> GetInstalledVersions(string refPackPath)
    {
        if (!Directory.Exists(refPackPath))
            return [];

        var versions = Directory.GetDirectories(refPackPath)
            .Select(Path.GetFileName)
            .Where(v => v != null && char.IsDigit(v[0]))
            .Select(v => v!)
            .OrderByDescending(v => ParseVersion(v))
            .ToList();

        return versions;
    }

    /// <summary>
    /// Gets the ref assembly directory for a specific framework version.
    /// </summary>
    public static string? GetRefAssemblyPath(string refPackPath, string version)
    {
        var versionPath = Path.Combine(refPackPath, version, "ref");
        if (!Directory.Exists(versionPath))
            return null;

        // Find the TFM subdirectory (e.g., net8.0, net9.0)
        var tfmDirs = Directory.GetDirectories(versionPath)
            .Select(Path.GetFileName)
            .Where(d => d != null && d.StartsWith("net"))
            .OrderByDescending(d => d) // Latest TFM first
            .ToList();

        if (tfmDirs.Count == 0)
            return null;

        return Path.Combine(versionPath, tfmDirs[0]!);
    }

    /// <summary>
    /// Lists all assemblies in a ref assembly directory.
    /// </summary>
    public static List<AssemblyRefInfo> GetAssemblies(string refPath)
    {
        if (!Directory.Exists(refPath))
            return [];

        return Directory.GetFiles(refPath, "*.dll")
            .Select(f => new AssemblyRefInfo
            {
                Name = Path.GetFileName(f),
                Path = f
            })
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Resolves a framework short name (with optional version) to a ref pack path.
    /// </summary>
    public static (string? RefPath, string? Version, string? Error) ResolveFramework(
        string frameworkSpec, 
        string? packsDirectory = null)
    {
        // Parse framework@version syntax
        string frameworkName;
        string? requestedVersion = null;
        
        var atIndex = frameworkSpec.LastIndexOf('@');
        if (atIndex > 0)
        {
            frameworkName = frameworkSpec[..atIndex];
            requestedVersion = frameworkSpec[(atIndex + 1)..];
        }
        else
        {
            frameworkName = frameworkSpec;
        }

        // Resolve short name to ref pack name
        if (!FrameworkMappings.TryGetValue(frameworkName, out var refPackName))
        {
            return (null, null, $"Unknown framework '{frameworkName}'. Valid names: {string.Join(", ", FrameworkMappings.Keys)}");
        }

        // Search across all packs directories
        List<string> packsDirs;
        if (packsDirectory != null)
        {
            packsDirs = [packsDirectory];
        }
        else
        {
            packsDirs = GetAllPacksDirectories();
        }

        // Collect all versions across all directories, tracking which dir has each version
        List<(string Version, string RefPackPath)> allVersions = [];

        foreach (var dir in packsDirs)
        {
            var refPackPath = Path.Combine(dir, refPackName);
            if (!Directory.Exists(refPackPath))
                continue;

            foreach (var v in GetInstalledVersions(refPackPath))
            {
                if (!allVersions.Any(x => x.Version == v))
                {
                    allVersions.Add((v, refPackPath));
                }
            }
        }

        if (allVersions.Count == 0)
        {
            return (null, null, $"Framework '{frameworkName}' is not installed");
        }

        // Sort by version descending
        allVersions = allVersions.OrderByDescending(x => ParseVersion(x.Version)).ToList();

        string version;
        string matchedRefPackPath;

        if (requestedVersion != null)
        {
            var match = allVersions.FirstOrDefault(x => x.Version.StartsWith(requestedVersion, StringComparison.OrdinalIgnoreCase));
            if (match == default)
                match = allVersions.FirstOrDefault(x => x.Version.Contains(requestedVersion, StringComparison.OrdinalIgnoreCase));

            if (match == default)
            {
                return (null, null, $"Version '{requestedVersion}' not found for '{frameworkName}'. Available: {string.Join(", ", allVersions.Take(5).Select(x => x.Version))}");
            }

            version = match.Version;
            matchedRefPackPath = match.RefPackPath;
        }
        else
        {
            version = allVersions[0].Version;
            matchedRefPackPath = allVersions[0].RefPackPath;
        }

        var refPath = GetRefAssemblyPath(matchedRefPackPath, version);
        if (refPath == null)
        {
            return (null, null, $"Could not find ref libraries for '{frameworkName}' version {version}");
        }

        return (refPath, version, null);
    }

    /// <summary>
    /// Downloads ref packs if needed, then resolves an assembly name to a full path.
    /// Prefer this over <see cref="ResolveAssembly"/> to avoid cache-miss races.
    /// </summary>
    public static async Task<(string? AssemblyPath, string? Framework, string? Version, string? Error)> ResolveAssemblyAsync(
        string assemblyName,
        HttpClient httpClient,
        Action<string>? log = null,
        string? frameworkSpec = null,
        bool useRuntimeAssemblies = false,
        string? platformVersion = null)
    {
        // Try resolving from already-installed packs first (SDK + app cache, no network)
        var local = ResolveAssembly(assemblyName, frameworkSpec, useRuntimeAssemblies: useRuntimeAssemblies, platformVersion: platformVersion);
        if (local.AssemblyPath != null)
        {
            log?.Invoke($"Resolved from installed packs: {local.Framework} {local.Version}");
            return local;
        }

        // Only download ref packs when a specific framework is requested.
        // Bare-name resolution (null frameworkSpec) relies on installed SDK packs;
        // if the assembly isn't found locally, it's likely a NuGet package.
        if (!useRuntimeAssemblies && !string.IsNullOrEmpty(frameworkSpec))
        {
            // Parse explicit version from frameworkSpec (e.g., "runtime@9.0.12")
            string? explicitVersion = null;
            if (frameworkSpec.Contains('@'))
                explicitVersion = frameworkSpec[(frameworkSpec.LastIndexOf('@') + 1)..];

            var requests = PlatformPackService.BuildPackRequests(assemblyName, explicitVersion);
            await foreach (var _ in PlatformPackService.EnsurePacksAsync(requests, httpClient, log).ConfigureAwait(false))
            {
            }

            return ResolveAssembly(assemblyName, frameworkSpec, useRuntimeAssemblies: useRuntimeAssemblies, platformVersion: platformVersion);
        }

        return local;
    }

    /// <summary>
    /// Resolves an assembly name to a full path within a framework.
    /// Callers must ensure ref packs are already downloaded, or use <see cref="ResolveAssemblyAsync"/>.
    /// </summary>
    /// <param name="assemblyName">The assembly name (with or without .dll extension)</param>
    /// <param name="frameworkSpec">Optional framework family specifier (e.g., "runtime", "aspnetcore")</param>
    /// <param name="packsDirectory">Optional override for packs directory</param>
    /// <param name="useRuntimeAssemblies">If true, resolve to runtime assemblies (with PDBs) instead of ref assemblies</param>
    /// <param name="platformVersion">Optional shared runtime version. With runtime assemblies and no framework, searches runtime then aspnetcore.</param>
    public static (string? AssemblyPath, string? Framework, string? Version, string? Error) ResolveAssembly(
        string assemblyName,
        string? frameworkSpec = null,
        string? packsDirectory = null,
        bool useRuntimeAssemblies = false,
        string? platformVersion = null)
    {
        // Detect framework names passed as assembly names (e.g., --platform Microsoft.AspNetCore.App)
        // and provide a helpful error message
        var frameworkMatch = SharedFrameworkMappings
            .FirstOrDefault(kv => string.Equals(kv.Value, assemblyName, StringComparison.OrdinalIgnoreCase));
        if (frameworkMatch.Key != null)
        {
            return (null, null, null, $"'{assemblyName}' is a framework, not a library. Use '--framework {frameworkMatch.Key}' only to restrict a library lookup to that framework family, or 'find <pattern> --platform' to search across platform libraries.");
        }

        // Ensure .dll extension
        if (!assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            assemblyName += ".dll";
        }

        // For runtime assemblies, use the shared directory
        if (useRuntimeAssemblies)
        {
            return ResolveRuntimeAssembly(assemblyName, frameworkSpec, platformVersion);
        }

        // If framework specified, search only that framework (across all packs dirs)
        if (!string.IsNullOrEmpty(frameworkSpec))
        {
            var (refPath, version, error) = ResolveFramework(frameworkSpec, packsDirectory);
            if (error != null)
            {
                return (null, null, null, error);
            }

            var assemblyPath = FindAssemblyCaseInsensitive(refPath!, assemblyName);

            // Get framework short name
            var frameworkName = frameworkSpec.Contains('@')
                ? frameworkSpec[..frameworkSpec.LastIndexOf('@')]
                : frameworkSpec;

            if (assemblyPath == null)
            {
                // Runtime-only implementation assemblies (e.g. System.Private.CoreLib)
                // live in the shared runtime but have no ref-pack counterpart.
                var rtOnly = ResolveRuntimeAssembly(assemblyName, frameworkSpec);
                if (rtOnly.AssemblyPath != null)
                    return rtOnly;

                return (null, null, null, $"Library '{assemblyName}' not found in {frameworkSpec}");
            }

            // For unversioned specs, prefer runtime when it has an equal or newer version
            if (!frameworkSpec.Contains('@'))
            {
                var rt = ResolveRuntimeAssembly(assemblyName, frameworkName);
                if (rt.AssemblyPath != null && rt.Version != null)
                {
                    var rtVer = ParseVersion(rt.Version);
                    var refVer = ParseVersion(version!);
                    if (rtVer >= refVer)
                        return (rt.AssemblyPath, rt.Framework, rt.Version, null);
                }
            }

            return (assemblyPath, frameworkName, version, null);
        }

        // Search all frameworks across all packs dirs and runtime,
        // returning the assembly from whichever source has the newest version.
        // When versions are equal, runtime wins (has debug info for SourceLink).
        var frameworks = GetInstalledFrameworks(packsDirectory);
        var searchOrder = new[] { "runtime", "aspnetcore", "netstandard" };

        foreach (var shortName in searchOrder)
        {
            var framework = frameworks.FirstOrDefault(f => f.ShortName == shortName);
            if (framework == null)
                continue;

            var refPath = GetRefAssemblyPath(framework.Path, framework.LatestVersion);
            if (refPath == null)
                continue;

            var assemblyPath = FindAssemblyCaseInsensitive(refPath, assemblyName);
            if (assemblyPath == null)
                continue;

            // Check if the runtime has a newer (or equal) version
            var rt = ResolveRuntimeAssembly(assemblyName, shortName);
            if (rt.AssemblyPath != null && rt.Version != null)
            {
                var rtVer = ParseVersion(rt.Version);
                var refVer = ParseVersion(framework.LatestVersion);
                if (rtVer >= refVer)
                    return (rt.AssemblyPath, rt.Framework, rt.Version, null);
            }

            return (assemblyPath, framework.ShortName, framework.LatestVersion, null);
        }

        // Fallback: runtime-only implementation assemblies (e.g. System.Private.CoreLib)
        // live in the shared runtime but have no ref-pack counterpart.
        foreach (var shortName in new[] { "runtime", "aspnetcore" })
        {
            var rt = ResolveRuntimeAssembly(assemblyName, shortName);
            if (rt.AssemblyPath != null)
                return rt;
        }

        return (null, null, null, $"Library '{assemblyName}' not found in any installed framework");
    }

    /// <summary>
    /// Resolves an assembly to the runtime (shared) directory instead of ref packs.
    /// Runtime assemblies have debug info for MSDL symbol lookup.
    /// </summary>
    private static (string? AssemblyPath, string? Framework, string? Version, string? Error) ResolveRuntimeAssembly(
        string assemblyName,
        string? frameworkSpec,
        string? platformVersion = null)
    {
        if (string.IsNullOrEmpty(frameworkSpec) && !string.IsNullOrEmpty(platformVersion))
            return ResolveRuntimeAssemblyAcrossFrameworks(assemblyName, platformVersion);

        var sharedDir = GetSharedDirectory();
        if (sharedDir == null)
        {
            return (null, null, null, "Could not locate .NET shared runtime directory");
        }

        // Parse framework and version
        string frameworkName = "runtime";
        string? requestedVersion = null;
        
        if (!string.IsNullOrEmpty(frameworkSpec))
        {
            var atIndex = frameworkSpec.LastIndexOf('@');
            if (atIndex > 0)
            {
                frameworkName = frameworkSpec[..atIndex];
                requestedVersion = frameworkSpec[(atIndex + 1)..];
            }
            else
            {
                frameworkName = frameworkSpec;
            }
        }

        if (!string.IsNullOrEmpty(platformVersion))
        {
            if (requestedVersion != null && !requestedVersion.Equals(platformVersion, StringComparison.OrdinalIgnoreCase))
                return (null, null, null, "Specify the platform version either with --version or as --framework name@version, not both.");
            requestedVersion ??= platformVersion;
        }

        // Map short name to shared directory name
        if (!SharedFrameworkMappings.TryGetValue(frameworkName, out var sharedFrameworkName))
        {
            // netstandard doesn't have a runtime, fall back to runtime
            if (frameworkName.Equals("netstandard", StringComparison.OrdinalIgnoreCase))
            {
                return (null, null, null, "netstandard does not have runtime libraries (ref-only)");
            }
            return (null, null, null, $"Unknown framework '{frameworkName}'. Valid names for runtime: {string.Join(", ", SharedFrameworkMappings.Keys)}");
        }

        var frameworkPath = Path.Combine(sharedDir, sharedFrameworkName);
        if (!Directory.Exists(frameworkPath))
        {
            return (null, null, null, $"Framework '{frameworkName}' runtime is not installed");
        }

        // Get installed versions
        var versions = Directory.GetDirectories(frameworkPath)
            .Select(Path.GetFileName)
            .Where(v => v != null && char.IsDigit(v[0]))
            .Select(v => v!)
            .OrderByDescending(v => ParseVersion(v))
            .ToList();

        if (versions.Count == 0)
        {
            return (null, null, null, $"No versions found for '{frameworkName}' runtime");
        }

        string version;
        if (requestedVersion != null)
        {
            version = versions.FirstOrDefault(v => v.StartsWith(requestedVersion, StringComparison.OrdinalIgnoreCase))
                      ?? versions.FirstOrDefault(v => v.Contains(requestedVersion, StringComparison.OrdinalIgnoreCase))
                      ?? "";
            
            if (string.IsNullOrEmpty(version))
            {
                return (null, null, null, $"Version '{requestedVersion}' not found for '{frameworkName}' runtime. Available: {string.Join(", ", versions.Take(5))}");
            }
        }
        else
        {
            version = versions[0]; // Latest
        }

        var assemblyPath = FindAssemblyCaseInsensitive(Path.Combine(frameworkPath, version), assemblyName);
        if (assemblyPath == null)
        {
            return (null, null, null, $"Library '{assemblyName}' not found in {frameworkName} runtime {version}");
        }

        return (assemblyPath, frameworkName, version, null);
    }

    private static (string? AssemblyPath, string? Framework, string? Version, string? Error) ResolveRuntimeAssemblyAcrossFrameworks(
        string assemblyName,
        string platformVersion)
    {
        List<string> errors = [];
        foreach (var frameworkName in new[] { "runtime", "aspnetcore" })
        {
            var result = ResolveRuntimeAssembly(assemblyName, frameworkName, platformVersion);
            if (result.AssemblyPath != null)
                return result;
            if (!string.IsNullOrWhiteSpace(result.Error))
                errors.Add(result.Error);
        }

        return (null, null, null,
            $"Library '{assemblyName}' not found in installed runtime frameworks for version '{platformVersion}'. Checked runtime, aspnetcore.");
    }

    /// <summary>
    /// Resolves the runtime framework directory for a given framework short name.
    /// Returns the directory path containing runtime assemblies (e.g., shared/Microsoft.NETCore.App/9.0.0/).
    /// </summary>
    public static (string? DirectoryPath, string? Version, string? Error) ResolveRuntimeFramework(
        string frameworkName)
    {
        var sharedDir = GetSharedDirectory();
        if (sharedDir == null)
            return (null, null, "Could not locate .NET shared runtime directory");

        if (!SharedFrameworkMappings.TryGetValue(frameworkName, out var sharedFrameworkName))
            return (null, null, frameworkName.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
                ? "netstandard does not have runtime libraries (ref-only)"
                : $"Unknown framework '{frameworkName}'");

        var frameworkPath = Path.Combine(sharedDir, sharedFrameworkName);
        if (!Directory.Exists(frameworkPath))
            return (null, null, $"Framework '{frameworkName}' runtime is not installed");

        var version = Directory.GetDirectories(frameworkPath)
            .Select(Path.GetFileName)
            .Where(v => v != null && char.IsDigit(v[0]))
            .Select(v => v!)
            .OrderByDescending(v => ParseVersion(v))
            .FirstOrDefault();

        if (version == null)
            return (null, null, $"No versions found for '{frameworkName}' runtime");

        return (Path.Combine(frameworkPath, version), version, null);
    }

    /// <summary>
    /// Finds a DLL in a directory using case-insensitive matching.
    /// Returns the actual path if found, null otherwise.
    /// Tries exact match first for performance, then falls back to directory scan on Linux.
    /// </summary>
    private static string? FindAssemblyCaseInsensitive(string directory, string assemblyFileName)
    {
        var exactPath = Path.Combine(directory, assemblyFileName);
        if (File.Exists(exactPath))
            return exactPath;

        // On case-sensitive filesystems, try a directory scan
        if (!Directory.Exists(directory))
            return null;

        try
        {
            var match = Directory.EnumerateFiles(directory, "*.dll")
                .FirstOrDefault(f => Path.GetFileName(f).Equals(assemblyFileName, StringComparison.OrdinalIgnoreCase));
            return match;
        }
        catch
        {
            return null;
        }
    }

    private static int CountAssemblies(string refPath)
    {
        try
        {
            return Directory.GetFiles(refPath, "*.dll").Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Reads the .NET install location from /etc/dotnet/install_location (Linux/macOS distro packages).
    /// </summary>
    private static string? ReadInstallLocation()
    {
        const string installLocationFile = "/etc/dotnet/install_location";
        try
        {
            if (File.Exists(installLocationFile))
            {
                return File.ReadAllText(installLocationFile).Trim();
            }
        }
        catch
        {
            // Ignore errors reading the file
        }
        return null;
    }

    private static NuGetVersion ParseVersion(string versionString)
    {
        // Use full SemVer parsing so prerelease labels are ordered correctly.
        // Stripping the prerelease suffix would collapse builds that share a
        // base version (e.g. "11.0.0-preview.3.x" and "11.0.0-preview.4.x")
        // into the same value, making "latest" selection arbitrary.
        return NuGetVersion.TryParse(versionString, out var version)
            ? version
            : new NuGetVersion(0, 0, 0);
    }

    /// <summary>
    /// Scans all platform ref assemblies in the runtime framework to find which
    /// library contains a given type. Returns the library name (without .dll), or null.
    /// </summary>
    public static string? FindLibraryContainingType(string typeName)
    {
        var frameworks = GetInstalledFrameworks();
        var runtime = frameworks.FirstOrDefault(f => f.ShortName == "runtime");
        if (runtime == null) return null;

        var refPath = GetRefAssemblyPath(runtime.Path, runtime.LatestVersion);
        if (refPath == null) return null;

        var normalized = ILInspector.Metadata.TypeMatcher.Normalize(typeName);
        string? forwarderMatch = null;

        // Prefer assemblies with actual type definitions over type forwarders.
        // e.g., Dictionary`2 is defined in System.Collections, not mscorlib (which only forwards).
        foreach (var dll in Directory.GetFiles(refPath, "*.dll"))
        {
            if (ILInspector.Metadata.TypeForwardResolver.DefinesType(dll, normalized))
                return System.IO.Path.GetFileNameWithoutExtension(dll);
            if (forwarderMatch == null && ILInspector.Metadata.TypeForwardResolver.ForwardsType(dll, normalized))
                forwarderMatch = System.IO.Path.GetFileNameWithoutExtension(dll);
        }

        return forwarderMatch;
    }

    /// <summary>
    /// Lightweight type existence check using raw metadata.
    /// </summary>
    public static bool HasType(string assemblyPath, string typeName)
    {
        var normalized = ILInspector.Metadata.TypeMatcher.Normalize(typeName);
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
            if (!peReader.HasMetadata) return false;

            var mdReader = peReader.GetMetadataReader();
            foreach (var handle in mdReader.TypeDefinitions)
            {
                var typeDef = mdReader.GetTypeDefinition(handle);
                var name = mdReader.GetString(typeDef.Name);
                var ns = mdReader.GetString(typeDef.Namespace);
                var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                if (ILInspector.Metadata.TypeMatcher.Matches(fullName, normalized))
                    return true;
            }

            foreach (var handle in mdReader.ExportedTypes)
            {
                var exported = mdReader.GetExportedType(handle);
                if (!exported.IsForwarder) continue;
                var name = mdReader.GetString(exported.Name);
                var ns = mdReader.GetString(exported.Namespace);
                var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                if (ILInspector.Metadata.TypeMatcher.Matches(fullName, normalized))
                    return true;
            }
        }
        catch { }
        return false;
    }
}

/// <summary>
/// Information about an installed framework.
/// </summary>
public class FrameworkInfo
{
    public string ShortName { get; set; } = "";
    public string RefPackName { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public List<string> AllVersions { get; set; } = [];
    public int AssemblyCount { get; set; }
    public string Path { get; set; } = "";
}

/// <summary>
/// Information about a reference assembly.
/// </summary>
public class AssemblyRefInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}
