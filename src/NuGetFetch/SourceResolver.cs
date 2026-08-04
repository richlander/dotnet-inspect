using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using InertText;

namespace NuGetFetch;

/// <summary>
/// Thrown when a resolved package source cannot be used as a NuGet source.
/// </summary>
/// <remarks>
/// Resolution is the only chokepoint every source passes through, whichever route it arrived
/// by, so it is the only place a rejection can be complete. That places the rejection well
/// after parsing, where returning a message is no longer an option, so it is raised and the
/// CLI converts it to an <c>Error:</c> line.
/// </remarks>
public sealed class UnsupportedSourceException(string message) : Exception(message)
{
    /// <summary>
    /// Throws when <paramref name="url"/> cannot be used as a NuGet source.
    /// </summary>
    /// <remarks>
    /// The throwing half of a pair, in the shape of <see cref="ArgumentNullException.ThrowIfNull"/>.
    /// A caller that would rather not be thrown at asks
    /// <see cref="SourceResolver.IsSupportedSource"/> first and handles the answer — which is what
    /// the CLI's option validators do, so a mistyped <c>--source</c> is an ordinary parse error.
    /// This guard is what remains for the paths that asked nothing, where a source that cannot
    /// work should stop the operation rather than quietly fail later as a 401.
    /// </remarks>
    public static void ThrowIfUnsupported(string url)
    {
        if (!SourceResolver.IsSupportedSource(url, out InertString? problem))
            throw new UnsupportedSourceException(problem.Value.ToString());
    }

    /// <summary>
    /// Throws when any of <paramref name="sources"/> cannot be used as a NuGet source.
    /// </summary>
    public static void ThrowIfUnsupported(IEnumerable<PackageSource> sources)
    {
        if (!SourceResolver.IsSupportedSource(sources, out InertString? problem))
            throw new UnsupportedSourceException(problem.Value.ToString());
    }
}

/// <summary>
/// Resolves NuGet package sources from nuget.config files.
/// </summary>
public static class SourceResolver
{
    private static readonly string NuGetOrgName = "nuget.org";
    private static readonly string NuGetOrgUrl = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// Reports whether <paramref name="url"/> can be used as a NuGet source.
    /// </summary>
    /// <remarks>
    /// Every source is checked, nuget.org included, because a check that exempts the sources it
    /// expects to be well-formed only holds while that expectation does.
    /// </remarks>
    public static bool IsSupportedSource(string url) => IsSupportedSource(url, out _);

    /// <summary>
    /// Reports whether <paramref name="url"/> can be used as a NuGet source, and why not when it
    /// cannot.
    /// </summary>
    /// <param name="url">The source URL to test.</param>
    /// <param name="problem">
    /// The reason the source is unusable, set only when this returns false.
    /// </param>
    /// <remarks>
    /// Credentials embedded in the URL are the case this exists to catch. NuGet has no support
    /// for them — the client never sends the userinfo component — so they authenticate against
    /// nothing, and the request that follows fails as an ordinary 401 that gives the operator no
    /// hint the credential they supplied was never sent. The form is a git and curl convention,
    /// which is exactly why it gets typed here.
    ///
    /// The reason carries the URL stripped of its userinfo, so reporting the problem does not
    /// itself put the credential on a terminal or in a log.
    /// </remarks>
    public static bool IsSupportedSource(string url, [NotNullWhen(false)] out InertString? problem)
    {
        problem = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrEmpty(uri.UserInfo))
        {
            return true;
        }

        string withoutCredentials = new UriBuilder(uri)
        {
            UserName = "",
            Password = "",
        }.Uri.ToString();

        problem = InertString.Format(
            TextPolicy.Field,
            $"Source URL '{withoutCredentials}' embeds <user>:<password>, which NuGet does not "
            + $"support. Configure the credentials in a nuget.config, or use a credential provider.");
        return false;
    }

    /// <summary>
    /// Reports whether every source in <paramref name="sources"/> can be used, and why not when
    /// one cannot.
    /// </summary>
    /// <param name="sources">The sources to test.</param>
    /// <param name="problem">
    /// The reason the first unusable source is unusable, set only when this returns false.
    /// </param>
    public static bool IsSupportedSource(
        IEnumerable<PackageSource> sources,
        [NotNullWhen(false)] out InertString? problem)
    {
        foreach (PackageSource source in sources)
        {
            if (!IsSupportedSource(source.Url, out problem))
            {
                return false;
            }
        }

        problem = null;
        return true;
    }

    private static IReadOnlyList<PackageSource> Validated(IReadOnlyList<PackageSource> sources)
    {
        UnsupportedSourceException.ThrowIfUnsupported(sources);
        return sources;
    }

    /// <summary>
    /// Resolves NuGet sources in priority order.
    /// Config files are processed most-distant first (machine → user → project-level),
    /// matching the official NuGet client semantics. A &lt;clear/&gt; in a project-level
    /// config clears sources accumulated from parent directories.
    /// </summary>
    /// <exception cref="UnsupportedSourceException">
    /// A resolved source cannot be used. Callers that would rather test than catch use
    /// <see cref="IsSupportedSource"/> on the sources they supply.
    /// </exception>
    public static IReadOnlyList<PackageSource> ResolveSources(
        string? explicitSource = null,
        string? configPath = null,
        IEnumerable<string>? additionalSources = null,
        string? workingDirectory = null)
        => Validated(BuildSources(explicitSource, configPath, additionalSources, workingDirectory));

    private static IReadOnlyList<PackageSource> BuildSources(
        string? explicitSource,
        string? configPath,
        IEnumerable<string>? additionalSources,
        string? workingDirectory)
    {
        // Explicit source overrides everything
        if (explicitSource is not null)
        {
            return [new PackageSource("explicit", explicitSource)];
        }

        List<PackageSource> sources = [.. BuildConfiguredSources(configPath, workingDirectory)];

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
    /// Resolves the sources declared by configuration, without substituting nuget.org when
    /// configuration declares none.
    /// </summary>
    /// <remarks>
    /// The fallback in <see cref="ResolveSources"/> is right for configs discovered by walking
    /// the directory tree — a machine with no nuget.config should still reach nuget.org. It is
    /// wrong for a config the caller named explicitly, where an empty result means the file could
    /// not supply what it was asked for, and silently searching nuget.org instead answers with
    /// packages from a feed the caller did not choose. Callers that need to tell those two cases
    /// apart use this method and decide for themselves.
    /// </remarks>
    public static IReadOnlyList<PackageSource> ResolveConfiguredSources(
        string? configPath = null,
        string? workingDirectory = null)
        => Validated(BuildConfiguredSources(configPath, workingDirectory));

    private static IReadOnlyList<PackageSource> BuildConfiguredSources(
        string? configPath = null,
        string? workingDirectory = null)
    {
        // Merge sources across all config files (most-distant first, so nearest wins)
        Dictionary<string, string> mergedSources = [];
        HashSet<string> disabled = [];
        Dictionary<string, PackageSourceCredential> credentials = [];

        IReadOnlyList<string> configFiles = configPath is not null
            ? [configPath]
            : FindConfigFiles(workingDirectory);

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

        return Validated(result);
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
