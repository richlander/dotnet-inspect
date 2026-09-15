using System.Diagnostics.CodeAnalysis;
using System.Xml;
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
/// One effective configured alias whose source value has not yet been
/// classified or canonicalized.
/// </summary>
/// <remarks>
/// Holding a declaration is not source authority. Consumers select alias names
/// first and call <see cref="Resolve"/> only for declarations included in the
/// requested effective view.
/// </remarks>
public sealed class PackageSourceDeclaration
{
    private readonly string _value;
    private readonly string? _baseDirectory;
    private readonly PackageSourceCredential? _credential;

    internal PackageSourceDeclaration(
        string name,
        string value,
        string? baseDirectory,
        PackageSourceCredential? credential)
    {
        Name = name;
        _value = value;
        _baseDirectory = baseDirectory;
        _credential = credential;
    }

    /// <summary>Gets the configured alias name.</summary>
    public string Name { get; }

    /// <summary>
    /// Classifies and canonicalizes this selected declaration.
    /// </summary>
    /// <exception cref="UnsupportedSourceException">
    /// The selected source value is unusable.
    /// </exception>
    public PackageSource Resolve() =>
        new(
            Name,
            SourceResolver.ResolveSourceValue(_value, _baseDirectory),
            _credential);

    internal bool MatchesUnclassifiedValue(string value) =>
        string.Equals(_value, value, StringComparison.Ordinal);
}

/// <summary>
/// Resolves NuGet package sources from nuget.config files.
/// </summary>
public static class SourceResolver
{
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

        if (LocalPackageSourceIdentity.IsLocalSource(url))
        {
            try
            {
                _ = LocalPackageSourceIdentity.Create(
                    url,
                    Directory.GetCurrentDirectory());
                return true;
            }
            catch (Exception ex) when (ex is
                ArgumentException
                or IOException
                or NotSupportedException)
            {
                problem = new InertString(
                    TextPolicy.Field,
                    "The local package source path is unusable.");
                return false;
            }
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps))
        {
            problem = new InertString(
                TextPolicy.Field,
                "The package source must be an HTTP(S) URL, local path, or file URI.");
            return false;
        }

        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            return true;
        }

        // Same redaction leaf as Core/package diagnostics (InertText.UrlRedaction):
        // strip user-info, query, fragment, and path auth tokens so the rejection
        // message never prints the credential that made the source unusable.
        problem = InertString.Format(
            TextPolicy.Field,
            $"Source URL '{UrlRedaction.ForDiagnostics(url)}' embeds <user>:<password>, which NuGet does not "
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
    /// <remarks>
    /// Ambient discovery starts with <see cref="PackageSources.Default"/>. Supplying
    /// <paramref name="configPath"/> selects only that file and starts with
    /// <see cref="PackageSources.Empty"/>.
    /// </remarks>
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
            return
            [
                new PackageSource(
                    "explicit",
                    ResolveSourceValue(explicitSource, workingDirectory)),
            ];
        }

        IReadOnlyList<PackageSource> initialSources = configPath is null
            ? PackageSources.Default
            : PackageSources.Empty;
        List<PackageSource> sources =
            [.. BuildConfiguredSources(configPath, workingDirectory, initialSources)];

        // Append additional sources
        if (additionalSources is not null)
        {
            foreach (string url in additionalSources)
            {
                sources.Add(
                    new PackageSource(
                        "additional",
                        ResolveSourceValue(url, workingDirectory)));
            }
        }

        return sources;
    }

    /// <summary>
    /// Resolves only the sources declared by configuration.
    /// </summary>
    /// <remarks>
    /// Ambient discovery starts with <see cref="PackageSources.Default"/>, while an explicitly
    /// selected config starts with <see cref="PackageSources.Empty"/>. This method exposes the
    /// latter behavior so callers can inspect configuration without inheriting the ambient
    /// default.
    /// </remarks>
    public static IReadOnlyList<PackageSource> ResolveConfiguredSources(
        string? configPath = null,
        string? workingDirectory = null)
        => Validated(BuildConfiguredSources(
            configPath,
            workingDirectory,
            PackageSources.Empty,
            includeDisabled: false));

    /// <summary>
    /// Resolves every configured source alias, including aliases currently disabled for ambient
    /// use.
    /// </summary>
    /// <remarks>
    /// Explicit command-line source selection can reactivate a disabled endpoint. Callers use
    /// this view only to retain its configured name and credentials; ordinary source resolution
    /// continues to exclude disabled entries.
    /// </remarks>
    public static IReadOnlyList<PackageSource> ResolveConfiguredSourceAliases(
        string? configPath = null,
        string? workingDirectory = null)
        => Validated(
        [
            .. GetConfiguredSourceAliasDeclarations(
                configPath,
                workingDirectory)
                .Select(static declaration => declaration.Resolve()),
        ]);

    /// <summary>
    /// Reads active source declarations after configuration hierarchy merge
    /// without classifying their values.
    /// </summary>
    public static IReadOnlyList<PackageSourceDeclaration>
        GetEffectiveSourceDeclarations(
            string? configPath = null,
            string? workingDirectory = null)
        => BuildConfiguredSourceDeclarations(
            configPath,
            workingDirectory,
            configPath is null ? PackageSources.Default : PackageSources.Empty,
            includeDisabled: false);

    /// <summary>
    /// Reads every effective configured alias, including disabled aliases,
    /// without classifying source values.
    /// </summary>
    /// <remarks>
    /// Explicit source selection uses this view to find a configured alias and
    /// credentials for the endpoint the user selected. Ordinary resolution
    /// uses <see cref="GetEffectiveSourceDeclarations"/>.
    /// </remarks>
    public static IReadOnlyList<PackageSourceDeclaration>
        GetConfiguredSourceAliasDeclarations(
            string? configPath = null,
            string? workingDirectory = null)
        => BuildConfiguredSourceDeclarations(
            configPath,
            workingDirectory,
            configPath is null ? PackageSources.Default : PackageSources.Empty,
            includeDisabled: true);

    /// <summary>
    /// Resolves package source mapping from the same configuration hierarchy as package sources.
    /// </summary>
    /// <remarks>
    /// Mapping is independent of source replacement. Supplying <paramref name="configPath"/>
    /// selects only that file; otherwise, configurations are merged most-distant first and a
    /// nearer source key replaces the complete pattern list inherited for that key.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// A mapping source has no key or patterns, or a pattern is not an exact package id or a
    /// prefix ending in <c>*</c>.
    /// </exception>
    public static PackageSourceMapping ResolvePackageSourceMapping(
        string? configPath = null,
        string? workingDirectory = null)
    {
        IReadOnlyList<string> configFiles = configPath is not null
            ? [configPath]
            : FindConfigFiles(workingDirectory);
        return MergePackageSourceMappings(configFiles);
    }

    internal static PackageSourceMapping MergePackageSourceMappings(
        IReadOnlyList<string> configFiles)
    {
        ArgumentNullException.ThrowIfNull(configFiles);

        var mappings =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        for (int i = configFiles.Count - 1; i >= 0; i--)
        {
            MergePackageSourceMappingFile(configFiles[i], mappings);
        }

        return new PackageSourceMapping(mappings);
    }

    private static IReadOnlyList<PackageSource> BuildConfiguredSources(
        string? configPath,
        string? workingDirectory,
        IReadOnlyList<PackageSource> initialSources,
        bool includeDisabled = false)
        =>
        [
            .. BuildConfiguredSourceDeclarations(
                configPath,
                workingDirectory,
                initialSources,
                includeDisabled)
                .Select(static declaration => declaration.Resolve()),
        ];

    private static IReadOnlyList<PackageSourceDeclaration>
        BuildConfiguredSourceDeclarations(
            string? configPath,
            string? workingDirectory,
            IReadOnlyList<PackageSource> initialSources,
            bool includeDisabled)
    {
        IReadOnlyList<string> configFiles = configPath is not null
            ? [configPath]
            : FindConfigFiles(workingDirectory);

        return MergeConfigDeclarations(
            configFiles,
            initialSources,
            includeDisabled);
    }

    internal static IReadOnlyList<PackageSource> MergeConfigFiles(
        IReadOnlyList<string> configFiles,
        IReadOnlyList<PackageSource> initialSources,
        bool includeDisabled = false)
        =>
        [
            .. MergeConfigDeclarations(
                configFiles,
                initialSources,
                includeDisabled)
                .Select(static declaration => declaration.Resolve()),
        ];

    private static IReadOnlyList<PackageSourceDeclaration>
        MergeConfigDeclarations(
            IReadOnlyList<string> configFiles,
            IReadOnlyList<PackageSource> initialSources,
            bool includeDisabled)
    {
        ArgumentNullException.ThrowIfNull(configFiles);
        ArgumentNullException.ThrowIfNull(initialSources);

        var mergedSources =
            new Dictionary<string, SourceDeclaration>(
                StringComparer.OrdinalIgnoreCase);
        List<string> sourceOrder = [];
        var inheritedSourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var credentials =
            new Dictionary<string, PackageSourceCredential>(StringComparer.OrdinalIgnoreCase);

        foreach (PackageSource source in initialSources)
        {
            SetSource(
                mergedSources,
                sourceOrder,
                source.Name,
                new SourceDeclaration(source.Url, BaseDirectory: null));
            inheritedSourceNames.Add(source.Name);

            if (source.Credential is not null)
            {
                credentials[source.Name] = source.Credential;
            }
        }

        // FindConfigFiles returns nearest-first; reverse to process most-distant first
        // so that <clear/> in a nearer config properly resets distant sources
        for (int i = configFiles.Count - 1; i >= 0; i--)
        {
            MergeConfigFile(
                configFiles[i],
                mergedSources,
                sourceOrder,
                inheritedSourceNames,
                disabled,
                credentials);
        }

        List<PackageSourceDeclaration> declarations = [];
        IEnumerable<string> configuredSources = sourceOrder
            .Where(name => !inheritedSourceNames.Contains(name));
        IEnumerable<string> inheritedSources = sourceOrder
            .Where(inheritedSourceNames.Contains);

        // Explicitly configured sources are consulted before surviving defaults.
        foreach (string name in configuredSources.Concat(inheritedSources))
        {
            if (!includeDisabled && disabled.Contains(name))
            {
                continue;
            }

            credentials.TryGetValue(name, out PackageSourceCredential? credential);
            SourceDeclaration declaration = mergedSources[name];
            declarations.Add(
                new PackageSourceDeclaration(
                    name,
                    declaration.Value,
                    declaration.BaseDirectory,
                    credential));
        }

        return declarations;
    }

    private static void MergePackageSourceMappingFile(
        string configPath,
        Dictionary<string, IReadOnlyList<string>> mappings)
    {
        if (!File.Exists(configPath))
        {
            return;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(configPath);
        }
        catch (Exception ex) when (ex is
            XmlException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            // Source parsing treats unreadable ambient configuration as absent.
            return;
        }

        XElement? mapping = document.Root?.Element("packageSourceMapping");
        if (mapping is null)
        {
            return;
        }

        foreach (XElement element in mapping.Elements())
        {
            if (element.Name == "clear")
            {
                mappings.Clear();
                continue;
            }

            if (element.Name != "packageSource")
            {
                continue;
            }

            string? sourceName = element.Attribute("key")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                throw new InvalidDataException(
                    $"Package source mapping in '{configPath}' contains a source without a key.");
            }

            List<string> patterns = [];
            foreach (XElement package in element.Elements("package"))
            {
                string? pattern = package.Attribute("pattern")?.Value.Trim();
                if (pattern is null)
                {
                    throw new InvalidDataException(
                        $"Package source mapping for '{sourceName}' in '{configPath}' "
                        + "contains a package without a pattern.");
                }

                patterns.Add(pattern);
            }
            if (patterns.Count == 0)
            {
                throw new InvalidDataException(
                    $"Package source mapping for '{sourceName}' in '{configPath}' "
                    + "must contain at least one package pattern.");
            }

            foreach (string pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)
                    || (pattern.Contains('*')
                        && (pattern[^1] != '*'
                            || pattern[..^1].Contains('*'))))
                {
                    throw new InvalidDataException(
                        $"Package source mapping pattern '{pattern}' for '{sourceName}' "
                        + $"in '{configPath}' must be an exact package id or a prefix ending in '*'.");
                }
            }

            mappings.Remove(sourceName);
            mappings.Add(sourceName, patterns);
        }
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
        => Validated(BuildConfiguredSources(
            configPath,
            workingDirectory: null,
            initialSources: PackageSources.Empty,
            includeDisabled: false));

    /// <summary>
    /// Merges a single nuget.config file into the accumulated sources, disabled set, and credentials.
    /// A &lt;clear/&gt; element clears all previously accumulated sources.
    /// </summary>
    private static void MergeConfigFile(
        string configPath,
        Dictionary<string, SourceDeclaration> sources,
        List<string> sourceOrder,
        HashSet<string> inheritedSourceNames,
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
                        sourceOrder.Clear();
                        inheritedSourceNames.Clear();
                        continue;
                    }

                    if (element.Name == "add")
                    {
                        string? key = element.Attribute("key")?.Value;
                        string? value = element.Attribute("value")?.Value;

                        if (key is not null && value is not null)
                        {
                            inheritedSourceNames.Remove(key);
                            SetSource(
                                sources,
                                sourceOrder,
                                key,
                                new SourceDeclaration(
                                    Environment.ExpandEnvironmentVariables(
                                        value),
                                    Path.GetDirectoryName(
                                        Path.GetFullPath(configPath))));
                        }
                    }
                }
            }

            // Parse <disabledPackageSources>
            XElement? disabledSources = root.Element("disabledPackageSources");

            if (disabledSources is not null)
            {
                foreach (XElement element in disabledSources.Elements())
                {
                    if (element.Name == "clear")
                    {
                        disabled.Clear();
                        continue;
                    }

                    if (element.Name != "add")
                    {
                        continue;
                    }

                    string? key = element.Attribute("key")?.Value;
                    string? value = element.Attribute("value")?.Value;

                    if (key is not null && bool.TryParse(value, out bool isDisabled))
                    {
                        if (isDisabled)
                        {
                            disabled.Add(key);
                        }
                        else
                        {
                            disabled.Remove(key);
                        }
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
        catch (Exception ex) when (ex is not UnsupportedSourceException)
        {
            // Best-effort config parsing
        }
    }

    private static void SetSource(
        Dictionary<string, SourceDeclaration> sources,
        List<string> sourceOrder,
        string name,
        SourceDeclaration declaration)
    {
        int existingIndex = sourceOrder.FindIndex(
            existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            sourceOrder.RemoveAt(existingIndex);
        }

        sources.Remove(name);
        sources[name] = declaration;
        sourceOrder.Add(name);
    }

    private readonly record struct SourceDeclaration(
        string Value,
        string? BaseDirectory);

    internal static string ResolveSourceValue(
        string source,
        string? baseDirectory)
    {
        if (!LocalPackageSourceIdentity.IsLocalSource(source))
        {
            UnsupportedSourceException.ThrowIfUnsupported(source);
            return source;
        }

        try
        {
            return LocalPackageSourceIdentity.Create(
                source,
                baseDirectory ?? Directory.GetCurrentDirectory()).CanonicalPath;
        }
        catch (Exception ex) when (ex is
            ArgumentException
            or IOException
            or NotSupportedException)
        {
            throw new UnsupportedSourceException(
                "The local package source path is unusable.");
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
