using System.Xml.Linq;

namespace NuGetFetch;

/// <summary>
/// Resolves NuGet package sources from nuget.config files.
/// </summary>
public static class SourceResolver
{
    private static readonly string NuGetOrgName = "nuget.org";
    private static readonly string NuGetOrgUrl = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// Resolves NuGet sources in priority order.
    /// Config files are processed most-distant first (machine → user → project-level),
    /// matching the official NuGet client semantics. A &lt;clear/&gt; in a project-level
    /// config clears sources accumulated from parent directories.
    /// </summary>
    public static IReadOnlyList<PackageSource> ResolveSources(
        string? explicitSource = null,
        string? configPath = null,
        IEnumerable<string>? additionalSources = null)
    {
        // Explicit source overrides everything
        if (explicitSource is not null)
        {
            return [new PackageSource("explicit", explicitSource)];
        }

        // Merge sources across all config files (most-distant first, so nearest wins)
        Dictionary<string, string> mergedSources = [];
        HashSet<string> disabled = [];
        Dictionary<string, PackageSourceCredential> credentials = [];

        IReadOnlyList<string> configFiles = configPath is not null
            ? [configPath]
            : FindConfigFiles();

        // FindConfigFiles returns nearest-first; reverse to process most-distant first
        // so that <clear/> in a nearer config properly resets distant sources
        for (int i = configFiles.Count - 1; i >= 0; i--)
        {
            MergeConfigFile(configFiles[i], mergedSources, disabled, credentials);
        }

        // Build result (skip disabled sources)
        List<PackageSource> sources = [];

        foreach ((string name, string url) in mergedSources)
        {
            if (disabled.Contains(name))
            {
                continue;
            }

            credentials.TryGetValue(name, out PackageSourceCredential? credential);
            sources.Add(new PackageSource(name, url, credential));
        }

        // Default to nuget.org if no config sources found
        if (sources.Count == 0)
        {
            sources.Add(new PackageSource(NuGetOrgName, NuGetOrgUrl));
        }

        // Append additional sources
        if (additionalSources is not null)
        {
            foreach (string url in additionalSources)
            {
                sources.Add(new PackageSource("additional", url));
            }
        }

        return sources;
    }

    /// <summary>
    /// Finds nuget.config files by walking up the directory tree from the current directory.
    /// Uses the canonical name "NuGet.Config" matching the official NuGet client.
    /// </summary>
    public static IReadOnlyList<string> FindConfigFiles(string? startDir = null)
    {
        List<string> configs = [];
        string? dir = startDir ?? Directory.GetCurrentDirectory();

        while (dir is not null)
        {
            string configFile = Path.Combine(dir, "NuGet.Config");

            if (File.Exists(configFile))
            {
                configs.Add(configFile);
            }
            else
            {
                // Fallback: check lowercase variant (common on Linux)
                string lowerConfigFile = Path.Combine(dir, "nuget.config");

                if (File.Exists(lowerConfigFile))
                {
                    configs.Add(lowerConfigFile);
                }
            }

            dir = Path.GetDirectoryName(dir);
        }

        // User-level config
        string? userConfig = GetUserConfigPath();

        if (userConfig is not null && File.Exists(userConfig))
        {
            configs.Add(userConfig);
        }

        return configs;
    }

    /// <summary>
    /// Loads package sources from a nuget.config file.
    /// </summary>
    public static IReadOnlyList<PackageSource> LoadSourcesFromConfig(string configPath)
    {
        Dictionary<string, string> sources = [];
        HashSet<string> disabled = [];
        Dictionary<string, PackageSourceCredential> credentials = [];

        MergeConfigFile(configPath, sources, disabled, credentials);

        List<PackageSource> result = [];

        foreach ((string name, string url) in sources)
        {
            if (disabled.Contains(name))
            {
                continue;
            }

            credentials.TryGetValue(name, out PackageSourceCredential? credential);
            result.Add(new PackageSource(name, url, credential));
        }

        return result;
    }

    /// <summary>
    /// Merges a single nuget.config file into the accumulated sources, disabled set, and credentials.
    /// A &lt;clear/&gt; element clears all previously accumulated sources.
    /// </summary>
    private static void MergeConfigFile(
        string configPath,
        Dictionary<string, string> sources,
        HashSet<string> disabled,
        Dictionary<string, PackageSourceCredential> credentials)
    {
        if (!File.Exists(configPath))
        {
            return;
        }

        try
        {
            XDocument doc = XDocument.Load(configPath);
            XElement? root = doc.Root;

            if (root is null)
            {
                return;
            }

            // Parse <packageSources>
            XElement? packageSources = root.Element("packageSources");

            if (packageSources is not null)
            {
                foreach (XElement element in packageSources.Elements())
                {
                    if (element.Name == "clear")
                    {
                        sources.Clear();
                        continue;
                    }

                    if (element.Name == "add")
                    {
                        string? key = element.Attribute("key")?.Value;
                        string? value = element.Attribute("value")?.Value;

                        if (key is not null && value is not null)
                        {
                            sources[key] = value;
                        }
                    }
                }
            }

            // Parse <disabledPackageSources>
            XElement? disabledSources = root.Element("disabledPackageSources");

            if (disabledSources is not null)
            {
                foreach (XElement element in disabledSources.Elements("add"))
                {
                    string? key = element.Attribute("key")?.Value;
                    string? value = element.Attribute("value")?.Value;

                    if (key is not null && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        disabled.Add(key);
                    }
                }
            }

            // Parse <packageSourceCredentials>
            XElement? credentialsElement = root.Element("packageSourceCredentials");

            if (credentialsElement is not null)
            {
                foreach (XElement sourceElement in credentialsElement.Elements())
                {
                    // Source name may be XML-encoded (spaces → _x0020_)
                    string sourceName = sourceElement.Name.LocalName.Replace("_x0020_", " ");
                    string? username = null;
                    string? password = null;

                    foreach (XElement add in sourceElement.Elements("add"))
                    {
                        string? key = add.Attribute("key")?.Value;
                        string? value = add.Attribute("value")?.Value;

                        if (string.Equals(key, "Username", StringComparison.OrdinalIgnoreCase))
                        {
                            username = value;
                        }
                        else if (string.Equals(key, "ClearTextPassword", StringComparison.OrdinalIgnoreCase))
                        {
                            password = value;
                        }
                    }

                    if (username is not null && password is not null)
                    {
                        credentials[sourceName] = new PackageSourceCredential(username, password);
                    }
                }
            }
        }
        catch
        {
            // Best-effort config parsing
        }
    }

    private static string? GetUserConfigPath()
    {
        // Match the official NuGet client: SpecialFolder.ApplicationData + "NuGet/NuGet.Config"
        // Windows: %APPDATA%\NuGet\NuGet.Config
        // Linux:   ~/.config/NuGet/NuGet.Config (via XDG_CONFIG_HOME)
        // macOS:   ~/.config/NuGet/NuGet.Config
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrEmpty(appData))
        {
            return null;
        }

        return Path.Combine(appData, "NuGet", "NuGet.Config");
    }
}
