using System.Runtime.InteropServices;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Resolves .NET platform/framework installation paths and discovers available assemblies.
/// </summary>
public static class PlatformResolver
{
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
    /// Discovers the .NET SDK packs directory.
    /// </summary>
    public static string? GetPacksDirectory()
    {
        // Try common locations in order of preference
        var candidates = GetPacksDirectoryCandidates();
        
        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
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

        // DOTNET_ROOT environment variable (works on all platforms)
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot, "shared");
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

        // DOTNET_ROOT environment variable (works on all platforms)
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot, "packs");
        }
    }

    /// <summary>
    /// Discovers all installed frameworks with their versions.
    /// </summary>
    public static List<FrameworkInfo> GetInstalledFrameworks(string? packsDirectory = null)
    {
        packsDirectory ??= GetPacksDirectory();
        if (packsDirectory == null || !Directory.Exists(packsDirectory))
        {
            return [];
        }

        var frameworks = new List<FrameworkInfo>();

        foreach (var (shortName, refPackName) in FrameworkMappings)
        {
            var refPackPath = Path.Combine(packsDirectory, refPackName);
            if (!Directory.Exists(refPackPath))
                continue;

            var versions = GetInstalledVersions(refPackPath);
            if (versions.Count == 0)
                continue;

            var latestVersion = versions[0]; // Already sorted descending
            var latestRefPath = GetRefAssemblyPath(refPackPath, latestVersion);
            var assemblyCount = latestRefPath != null ? CountAssemblies(latestRefPath) : 0;

            frameworks.Add(new FrameworkInfo
            {
                ShortName = shortName,
                RefPackName = refPackName,
                LatestVersion = latestVersion,
                AllVersions = versions,
                AssemblyCount = assemblyCount,
                Path = refPackPath
            });
        }

        return frameworks;
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
        packsDirectory ??= GetPacksDirectory();
        if (packsDirectory == null)
        {
            return (null, null, "Could not locate .NET SDK packs directory");
        }

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

        var refPackPath = Path.Combine(packsDirectory, refPackName);
        if (!Directory.Exists(refPackPath))
        {
            return (null, null, $"Framework '{frameworkName}' is not installed");
        }

        var versions = GetInstalledVersions(refPackPath);
        if (versions.Count == 0)
        {
            return (null, null, $"No versions found for framework '{frameworkName}'");
        }

        string version;
        if (requestedVersion != null)
        {
            // Find exact or closest match
            version = versions.FirstOrDefault(v => v.StartsWith(requestedVersion, StringComparison.OrdinalIgnoreCase))
                      ?? versions.FirstOrDefault(v => v.Contains(requestedVersion, StringComparison.OrdinalIgnoreCase))
                      ?? "";
            
            if (string.IsNullOrEmpty(version))
            {
                return (null, null, $"Version '{requestedVersion}' not found for '{frameworkName}'. Available: {string.Join(", ", versions.Take(5))}");
            }
        }
        else
        {
            version = versions[0]; // Latest
        }

        var refPath = GetRefAssemblyPath(refPackPath, version);
        if (refPath == null)
        {
            return (null, null, $"Could not find ref assemblies for '{frameworkName}' version {version}");
        }

        return (refPath, version, null);
    }

    /// <summary>
    /// Resolves an assembly name to a full path within a framework.
    /// </summary>
    /// <param name="assemblyName">The assembly name (with or without .dll extension)</param>
    /// <param name="frameworkSpec">Optional framework specifier (e.g., "runtime", "runtime@9.0.12")</param>
    /// <param name="packsDirectory">Optional override for packs directory</param>
    /// <param name="useRuntimeAssemblies">If true, resolve to runtime assemblies (with PDBs) instead of ref assemblies</param>
    public static (string? AssemblyPath, string? Framework, string? Version, string? Error) ResolveAssembly(
        string assemblyName,
        string? frameworkSpec = null,
        string? packsDirectory = null,
        bool useRuntimeAssemblies = false)
    {
        // Ensure .dll extension
        if (!assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            assemblyName += ".dll";
        }

        // For runtime assemblies, use the shared directory
        if (useRuntimeAssemblies)
        {
            return ResolveRuntimeAssembly(assemblyName, frameworkSpec);
        }

        packsDirectory ??= GetPacksDirectory();
        if (packsDirectory == null)
        {
            return (null, null, null, "Could not locate .NET SDK packs directory");
        }

        // If framework specified, search only that framework
        if (!string.IsNullOrEmpty(frameworkSpec))
        {
            var (refPath, version, error) = ResolveFramework(frameworkSpec, packsDirectory);
            if (error != null)
            {
                return (null, null, null, error);
            }

            var assemblyPath = Path.Combine(refPath!, assemblyName);
            if (!File.Exists(assemblyPath))
            {
                return (null, null, null, $"Assembly '{assemblyName}' not found in {frameworkSpec}");
            }

            // Get framework short name
            var frameworkName = frameworkSpec.Contains('@') 
                ? frameworkSpec[..frameworkSpec.LastIndexOf('@')] 
                : frameworkSpec;

            return (assemblyPath, frameworkName, version, null);
        }

        // Search all frameworks, prefer runtime
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

            var assemblyPath = Path.Combine(refPath, assemblyName);
            if (File.Exists(assemblyPath))
            {
                return (assemblyPath, framework.ShortName, framework.LatestVersion, null);
            }
        }

        return (null, null, null, $"Assembly '{assemblyName}' not found in any installed framework");
    }

    /// <summary>
    /// Resolves an assembly to the runtime (shared) directory instead of ref packs.
    /// Runtime assemblies have debug info for MSDL symbol lookup.
    /// </summary>
    private static (string? AssemblyPath, string? Framework, string? Version, string? Error) ResolveRuntimeAssembly(
        string assemblyName,
        string? frameworkSpec)
    {
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

        // Map short name to shared directory name
        if (!SharedFrameworkMappings.TryGetValue(frameworkName, out var sharedFrameworkName))
        {
            // netstandard doesn't have a runtime, fall back to runtime
            if (frameworkName.Equals("netstandard", StringComparison.OrdinalIgnoreCase))
            {
                return (null, null, null, "netstandard does not have runtime assemblies (ref-only)");
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

        var assemblyPath = Path.Combine(frameworkPath, version, assemblyName);
        if (!File.Exists(assemblyPath))
        {
            return (null, null, null, $"Assembly '{assemblyName}' not found in {frameworkName} runtime {version}");
        }

        return (assemblyPath, frameworkName, version, null);
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

    private static Version ParseVersion(string versionString)
    {
        // Handle versions like "9.0.12" and "10.0.0-preview.5.25277.114"
        var dashIndex = versionString.IndexOf('-');
        var cleanVersion = dashIndex > 0 ? versionString[..dashIndex] : versionString;
        
        // Pad to at least 3 parts
        var parts = cleanVersion.Split('.');
        while (parts.Length < 3)
        {
            cleanVersion += ".0";
            parts = cleanVersion.Split('.');
        }

        if (Version.TryParse(cleanVersion, out var version))
        {
            return version;
        }

        return new Version(0, 0, 0);
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
