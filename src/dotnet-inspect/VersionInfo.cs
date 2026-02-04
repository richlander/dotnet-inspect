using System.Reflection;

namespace DotnetInspector;

/// <summary>
/// Provides version information for the CLI tool.
/// </summary>
public static class VersionInfo
{
    private static string? _version;

    /// <summary>
    /// Gets the version string with short commit hash (e.g., "0.1.11+abc1234").
    /// </summary>
    public static string Version => _version ??= GetVersion();

    private static string GetVersion()
    {
        var assembly = typeof(VersionInfo).Assembly;
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        
        if (string.IsNullOrEmpty(infoVersion))
        {
            // Fallback to assembly version
            return assembly.GetName().Version?.ToString() ?? "0.0.0";
        }

        // Shorten commit hash to 7 characters (GitHub style)
        // Format: "0.1.11+1934fa7036f721a2c0aa41a0ea4dac0d5f8684d9" -> "0.1.11+1934fa7"
        var plusIndex = infoVersion.IndexOf('+');
        if (plusIndex > 0 && infoVersion.Length > plusIndex + 8)
        {
            return infoVersion[..(plusIndex + 8)];
        }

        return infoVersion;
    }
}
