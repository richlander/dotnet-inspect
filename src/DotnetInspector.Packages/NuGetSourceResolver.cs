// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml.Linq;

namespace DotnetInspector.Packages;

/// <summary>
/// Resolves NuGet package sources from configuration files and CLI options.
/// </summary>
public static class NuGetSourceResolver
{
    // Process-lifetime cache: config parsing is the expensive part and configs don't change mid-run.
    private static readonly Dictionary<string, List<NuGetSource>> s_cache = new();

    /// <summary>
    /// Resolves the list of NuGet sources to use based on options and configuration.
    /// </summary>
    /// <param name="options">Source options from CLI</param>
    /// <param name="workingDirectory">Directory to start searching for nuget.config (defaults to current directory)</param>
    /// <returns>Ordered list of sources to try</returns>
    public static List<NuGetSource> ResolveSources(NuGetSourceOptions? options, string? workingDirectory = null)
    {
        options ??= NuGetSourceOptions.Default;
        workingDirectory ??= Directory.GetCurrentDirectory();

        // If explicit --source options provided, use only those (no I/O, no caching needed)
        if (options.Sources.Length > 0)
        {
            var sources = options.Sources
                .Select((url, i) => new NuGetSource($"source{i + 1}", url))
                .ToList();

            // Add any --add-source values
            foreach (var additional in options.AdditionalSources)
            {
                sources.Add(new NuGetSource($"additional{sources.Count + 1}", additional));
            }

            return sources;
        }

        // Cache key: explicit config file (if set) or working directory, plus additional sources
        var cacheKey = string.Concat(
            options.ConfigFile ?? workingDirectory,
            "|",
            string.Join(",", options.AdditionalSources));

        if (s_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        // Try to load from config file(s)
        var configSources = LoadSourcesFromConfig(options.ConfigFile, workingDirectory);

        List<NuGetSource> result;
        if (configSources.Count > 0)
        {
            // Add any --add-source values
            foreach (var additional in options.AdditionalSources)
            {
                configSources.Add(new NuGetSource($"additional{configSources.Count + 1}", additional));
            }

            result = configSources;
        }
        else
        {
            // Default: nuget.org plus any additional sources
            result = [NuGetSource.NuGetOrg];

            foreach (var additional in options.AdditionalSources)
            {
                result.Add(new NuGetSource($"additional{result.Count}", additional));
            }
        }

        s_cache[cacheKey] = result;
        return result;
    }

    /// <summary>
    /// Loads sources from nuget.config file(s).
    /// </summary>
    private static List<NuGetSource> LoadSourcesFromConfig(string? explicitConfigPath, string workingDirectory)
    {
        List<string> configFiles = [];

        if (!string.IsNullOrEmpty(explicitConfigPath))
        {
            // Use only the explicitly specified config file
            if (File.Exists(explicitConfigPath))
            {
                configFiles.Add(explicitConfigPath);
            }
        }
        else
        {
            // Walk up directory tree to find nuget.config files
            configFiles.AddRange(FindConfigFiles(workingDirectory));

            // Also check user-level config
            var userConfig = GetUserConfigPath();
            if (userConfig != null && File.Exists(userConfig))
            {
                configFiles.Add(userConfig);
            }
        }

        // Parse and merge configs (later files have lower priority)
        var sources = new Dictionary<string, NuGetSource>(StringComparer.OrdinalIgnoreCase);
        var disabledSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configFile in configFiles)
        {
            try
            {
                ParseConfigFile(configFile, sources, disabledSources);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Skipping invalid NuGet config {configFile}: {ex.Message}");
            }
        }

        // Remove disabled sources and return
        return sources
            .Where(kvp => !disabledSources.Contains(kvp.Key))
            .Select(kvp => kvp.Value)
            .ToList();
    }

    /// <summary>
    /// Finds nuget.config files by walking up the directory tree.
    /// </summary>
    private static IEnumerable<string> FindConfigFiles(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);

        while (dir != null)
        {
            var configPath = Path.Combine(dir.FullName, "nuget.config");
            if (File.Exists(configPath))
            {
                yield return configPath;
            }

            // Also check NuGet.config (case-sensitive on Linux)
            var altConfigPath = Path.Combine(dir.FullName, "NuGet.config");
            if (File.Exists(altConfigPath) && !configPath.Equals(altConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                yield return altConfigPath;
            }

            // Also check NuGet.Config
            var alt2ConfigPath = Path.Combine(dir.FullName, "NuGet.Config");
            if (File.Exists(alt2ConfigPath) && 
                !configPath.Equals(alt2ConfigPath, StringComparison.OrdinalIgnoreCase) &&
                !altConfigPath.Equals(alt2ConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                yield return alt2ConfigPath;
            }

            dir = dir.Parent;
        }
    }

    /// <summary>
    /// Gets the path to the user-level nuget.config.
    /// </summary>
    private static string? GetUserConfigPath()
    {
        // Windows: %APPDATA%\NuGet\NuGet.Config
        // Linux/macOS: ~/.nuget/NuGet/NuGet.Config
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "NuGet", "NuGet.Config");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".nuget", "NuGet", "NuGet.Config");
        }
    }

    /// <summary>
    /// Parses a nuget.config file and adds sources to the dictionary.
    /// </summary>
    private static void ParseConfigFile(
        string configPath,
        Dictionary<string, NuGetSource> sources,
        HashSet<string> disabledSources)
    {
        var doc = XDocument.Load(configPath);
        var configuration = doc.Root;
        if (configuration == null || configuration.Name.LocalName != "configuration")
            return;

        // Parse packageSources
        var packageSources = configuration.Element("packageSources");
        if (packageSources != null)
        {
            // Handle <clear /> - removes all previously defined sources
            if (packageSources.Elements("clear").Any())
            {
                sources.Clear();
            }

            foreach (var add in packageSources.Elements("add"))
            {
                var key = add.Attribute("key")?.Value;
                var value = add.Attribute("value")?.Value;

                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    // Only add if not already present (first definition wins)
                    if (!sources.ContainsKey(key))
                    {
                        sources[key] = new NuGetSource(key, value);
                    }
                }
            }
        }

        // Parse disabledPackageSources
        var disabled = configuration.Element("disabledPackageSources");
        if (disabled != null)
        {
            foreach (var add in disabled.Elements("add"))
            {
                var key = add.Attribute("key")?.Value;
                var value = add.Attribute("value")?.Value;

                if (!string.IsNullOrEmpty(key) && 
                    value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                {
                    disabledSources.Add(key);
                }
            }
        }

        // Parse packageSourceCredentials
        var credentialsSection = configuration.Element("packageSourceCredentials");
        if (credentialsSection != null)
        {
            foreach (var sourceElement in credentialsSection.Elements())
            {
                // Element name is the source name (XML-encoded: spaces become _x0020_, etc.)
                var sourceName = System.Xml.XmlConvert.DecodeName(sourceElement.Name.LocalName);
                if (sourceName == null || !sources.ContainsKey(sourceName))
                    continue;

                string? username = null;
                string? password = null;
                bool isClearText = false;

                foreach (var add in sourceElement.Elements("add"))
                {
                    var key = add.Attribute("key")?.Value;
                    var value = add.Attribute("value")?.Value;

                    if (string.Equals(key, "Username", StringComparison.OrdinalIgnoreCase))
                        username = value;
                    else if (string.Equals(key, "ClearTextPassword", StringComparison.OrdinalIgnoreCase))
                        { password = value; isClearText = true; }
                    else if (string.Equals(key, "Password", StringComparison.OrdinalIgnoreCase))
                        { password = value; isClearText = false; }
                }

                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    string clearPassword;
                    if (isClearText)
                    {
                        clearPassword = password!;
                    }
                    else
                    {
                        try { clearPassword = EncryptionUtility.DecryptString(password!); }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Warning: Could not decrypt password for source '{sourceName}': {ex.Message}");
                            continue;
                        }
                    }

                    sources[sourceName] = sources[sourceName] with
                    {
                        Credentials = new NuGetSourceCredential(username!, clearPassword)
                    };
                }
            }
        }
    }
}
