using System.Reflection;
using System.Reflection.Metadata;

namespace DotnetInspector;

/// <summary>
/// Provides version information for the CLI tool.
/// </summary>
public static class VersionInfo
{
    public const string ToolName = "dotnet-inspect";

    private static string? _version;

    /// <summary>
    /// Gets the version string with short commit hash (e.g., "0.1.11+abc1234").
    /// </summary>
    public static string Version => _version ??= GetVersion();

    /// <summary>
    /// Gets the runtime flavor: "NativeAOT" or "CoreCLR".
    /// </summary>
    // CoreCLR retains CoreLib metadata even in single-file builds; NativeAOT does not.
    public static unsafe string Flavor =>
        typeof(object).Assembly.TryGetRawMetadata(out _, out _) ? "CoreCLR" : "NativeAOT";

    /// <summary>
    /// Gets the runtime flavor and .NET version (e.g., "CoreCLR; .NET 10.0").
    /// </summary>
    public static string FlavorVersion =>
        $"{Flavor}; .NET {Environment.Version.Major}.{Environment.Version.Minor}";

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
