namespace NuGetFetch.Plugins;

/// <summary>A discovered plugin entry point and how it must be launched.</summary>
/// <param name="Path">Absolute path to the entry-point file.</param>
/// <param name="RequiresDotnetHost">
/// True for managed <c>.dll</c> entry points, which run as <c>dotnet &lt;path&gt; -Plugin</c>.
/// False for native or self-contained executables, which run directly.
/// </param>
internal readonly record struct PluginExecutable(string Path, bool RequiresDotnetHost);

/// <summary>
/// Finds installed NuGet credential plugins.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors NuGet/NuGet.Client, src/NuGet.Core/NuGet.Protocol/Plugins/PluginDiscoverer.cs and
/// PluginDiscoveryUtility.cs. Credential providers are never registered in nuget.config —
/// there is no registration step at all — so discovery is entirely by convention, over three
/// routes tried in strict precedence order:
/// </para>
/// <list type="number">
///   <item><description><c>NUGET_NETCORE_PLUGIN_PATHS</c>, an explicit list of entry-point files.</description></item>
///   <item><description><c>NUGET_PLUGIN_PATHS</c>, the framework-agnostic equivalent, consulted only if the first is unset. Entries may be files or directories.</description></item>
///   <item><description>The convention directory <c>~/.nuget/plugins/netcore/</c> together with a scan of <c>PATH</c> for executables named <c>nuget-plugin-*</c>.</description></item>
/// </list>
/// <para>
/// Either environment variable <em>replaces</em> route 3 rather than adding to it, which is why
/// they are checked first and return immediately.
/// </para>
/// <para>
/// Route 3's PATH scan is the one most easily overlooked, because it looks like an ordinary tool
/// install: <c>dotnet tool install --global Microsoft.Artifacts.CredentialProvider.NuGet.Tool</c>
/// puts <c>nuget-plugin-microsoft-artifacts-credential-provider</c> on PATH and creates no
/// convention directory at all.
/// </para>
/// </remarks>
internal static class PluginDiscovery
{
    private const string NetCorePluginPathsVariable = "NUGET_NETCORE_PLUGIN_PATHS";
    private const string PluginPathsVariable = "NUGET_PLUGIN_PATHS";
    private const string PathPrefix = "nuget-plugin-";

    /// <summary>
    /// Returns the plugin entry points visible to this process, in discovery order.
    /// </summary>
    /// <param name="getEnvironmentVariable">Environment lookup; defaults to the process environment.</param>
    /// <param name="nuGetHome">Overrides the <c>~/.nuget</c> root.</param>
    public static IReadOnlyList<PluginExecutable> Discover(
        Func<string, string?>? getEnvironmentVariable = null,
        string? nuGetHome = null)
    {
        Func<string, string?> environment = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

        string? netCorePaths = environment(NetCorePluginPathsVariable);

        if (!string.IsNullOrEmpty(netCorePaths))
        {
            return ExpandExplicitPaths(netCorePaths);
        }

        string? pluginPaths = environment(PluginPathsVariable);

        if (!string.IsNullOrEmpty(pluginPaths))
        {
            return ExpandExplicitPaths(pluginPaths);
        }

        List<PluginExecutable> plugins = [];
        AddConventionPlugins(plugins, nuGetHome ?? GetDefaultNuGetHome());
        AddPathPlugins(plugins, environment("PATH"));
        return plugins;
    }

    /// <summary>
    /// Expands an explicit list of plugin paths. Entries may name a file directly or a directory
    /// to scan, and are separated by the platform path separator.
    /// </summary>
    private static IReadOnlyList<PluginExecutable> ExpandExplicitPaths(string value)
    {
        List<PluginExecutable> plugins = [];

        foreach (string entry in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (File.Exists(entry))
            {
                plugins.Add(Describe(entry));
            }
            else if (Directory.Exists(entry))
            {
                foreach (string file in SafeEnumerateFiles(entry))
                {
                    if (IsPathPluginName(file))
                    {
                        plugins.Add(Describe(file));
                    }
                }
            }
        }

        return plugins;
    }

    /// <summary>
    /// Adds plugins under <c>~/.nuget/plugins/netcore/</c>, where the entry point is a
    /// <c>.dll</c> named after its containing directory.
    /// </summary>
    private static void AddConventionPlugins(List<PluginExecutable> plugins, string? nuGetHome)
    {
        if (string.IsNullOrEmpty(nuGetHome))
        {
            return;
        }

        string root = Path.Combine(nuGetHome, "plugins", "netcore");

        if (!Directory.Exists(root))
        {
            return;
        }

        string[] directories;

        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string directory in directories)
        {
            string entryPoint = Path.Combine(directory, Path.GetFileName(directory) + ".dll");

            if (File.Exists(entryPoint))
            {
                plugins.Add(new PluginExecutable(entryPoint, RequiresDotnetHost: true));
            }
        }
    }

    /// <summary>
    /// Adds executables named <c>nuget-plugin-*</c> found on PATH. These are always launched
    /// directly, never through the dotnet host, because they are expected to be apphosts.
    /// </summary>
    private static void AddPathPlugins(List<PluginExecutable> plugins, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string file in SafeEnumerateFiles(directory))
            {
                if (IsPathPluginName(file))
                {
                    plugins.Add(new PluginExecutable(file, RequiresDotnetHost: false));
                }
            }
        }
    }

    /// <summary>
    /// Whether a file qualifies as a PATH-discovered plugin. The prefix is matched
    /// case-sensitively, matching NuGet, and the file must be executable: an extension check on
    /// Windows, the owner execute bit elsewhere.
    /// </summary>
    private static bool IsPathPluginName(string file)
    {
        string name = Path.GetFileName(file);

        if (!name.StartsWith(PathPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            string extension = Path.GetExtension(name);
            return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            return (File.GetUnixFileMode(file) & UnixFileMode.UserExecute) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Describes an explicitly named entry point, inferring the launch mode from its extension.</summary>
    private static PluginExecutable Describe(string file) =>
        new(file, RequiresDotnetHost: Path.GetExtension(file).Equals(".dll", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Enumerates a directory, treating an unreadable or missing one as empty. PATH routinely
    /// contains directories that do not exist or cannot be listed, and none of that is fatal.
    /// </summary>
    private static IEnumerable<string> SafeEnumerateFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static string? GetDefaultNuGetHome()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile) ? null : Path.Combine(profile, ".nuget");
    }
}
