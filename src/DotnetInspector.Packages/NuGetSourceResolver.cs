// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml;
using System.Xml.Linq;
using NuGetFetch;
using DotnetInspector.Core;
using InertText;
using NuGetSource = NuGetFetch.PackageSource;

namespace DotnetInspector.Packages;

/// <summary>
/// Identifies why package source mapping could not authorize a producer.
/// </summary>
public enum PackageSourceMappingFailure
{
    /// <summary>The package id matched no configured pattern.</summary>
    NoPattern,

    /// <summary>No active source carries a configured name selected by mapping.</summary>
    InactiveSource,

    /// <summary>Eligible aliases for one producer use different credentials.</summary>
    ConflictingCredentials,
}

/// <summary>
/// Thrown when package source mapping cannot authorize a producer for a package id.
/// </summary>
public sealed class PackageSourceMappingException(
    PackageSourceMappingFailure failure,
    string message) : InvalidOperationException(message)
{
    /// <summary>
    /// Gets the mapping failure category.
    /// </summary>
    public PackageSourceMappingFailure Failure { get; } = failure;
}

internal sealed record PackageSourceResolutionFailure(
    string Name,
    InertString Authority,
    string Message);

internal sealed record PackageSourceResolution(
    List<NuGetSource> Sources,
    List<PackageSourceResolutionFailure> Failures);

/// <summary>
/// Resolves NuGet sources by delegating to NuGetFetch.SourceResolver.
/// </summary>
public static class NuGetSourceResolver
{
    /// <summary>
    /// Restricts payload fulfillment for one discovered coordinate to its
    /// reporting producers while retaining the ambient source set and config.
    /// Follow-on coordinates, such as tool-wrapper redirects, independently
    /// recalculate their authorization.
    /// </summary>
    public static NuGetSourceOptions? RestrictToSources(
        NuGetSourceOptions? original,
        IReadOnlyList<string> sourceUrls)
    {
        ArgumentNullException.ThrowIfNull(sourceUrls);
        return RestrictToSourceKeys(
            original,
            [.. sourceUrls.Select(NuGetCache.GetSourceKey)]);
    }

    /// <summary>
    /// Restricts follow-on metadata or payload acquisition to an already
    /// resolved producer set without reselecting configured aliases.
    /// </summary>
    public static NuGetSourceOptions RestrictToResolvedSources(
        NuGetSourceOptions? original,
        IReadOnlyList<NuGetSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return (original ?? NuGetSourceOptions.Default) with
        {
            AuthorizedSourceKeys = [.. SourceKeys(sources)],
            ResolvedSources = [.. sources],
        };
    }

    /// <summary>
    /// Restricts payload or metadata fulfillment to canonical producer identities established
    /// by an earlier package acquisition.
    /// </summary>
    public static NuGetSourceOptions? RestrictToSourceKeys(
        NuGetSourceOptions? original,
        IReadOnlyList<string> sourceKeys)
    {
        ArgumentNullException.ThrowIfNull(sourceKeys);
        return (original ?? NuGetSourceOptions.Default) with
        {
            AuthorizedSourceKeys = [.. sourceKeys],
        };
    }

    /// <summary>
    /// Applies a producer restriction established by an earlier coordinate resolution to an
    /// already package-mapped source set.
    /// </summary>
    public static IReadOnlyList<NuGetSource> ResolveAuthorizedSources(
        NuGetSourceOptions? options,
        IReadOnlyList<NuGetSource> activeSources)
    {
        if (options?.AuthorizedSourceKeys is not { } authorizedKeys)
            return activeSources;

        HashSet<string> authorizedKeySet = [.. authorizedKeys];
        return
        [
            .. activeSources.Where(source =>
                authorizedKeySet.Contains(NuGetCache.GetSourceKey(source.Url))),
        ];
    }

    internal static NuGetSourceOptions? WithoutSourceRestriction(
        NuGetSourceOptions? options)
        => options is null
            || options.AuthorizedSourceKeys is null
                && options.ResolvedSources is null
            ? options
            : options with
            {
                AuthorizedSourceKeys = null,
                ResolvedSources = null,
            };

    /// <summary>
    /// Resolves sources and reduces them to the identities the package content
    /// cache records, so a caller can ask the cache for content this
    /// configuration is actually entitled to. Configured order is preserved.
    /// </summary>
    public static IReadOnlyList<string> ResolveSourceKeys(
        NuGetSourceOptions? options,
        string? workingDirectory = null)
        => SourceKeys(ResolveSources(options, workingDirectory));

    /// <summary>
    /// Resolves the producers eligible to serve <paramref name="packageId"/> and reduces them to
    /// the identities recorded by the package-content cache.
    /// </summary>
    public static IReadOnlyList<string> ResolveSourceKeysForPackage(
        NuGetSourceOptions? options,
        string packageId,
        string? workingDirectory = null)
        => SourceKeys(ResolveSourcesForPackage(options, packageId, workingDirectory));

    /// <summary>
    /// Reduces already-resolved sources to their cache identities, preserving
    /// configured order.
    /// </summary>
    /// <remarks>
    /// Order is part of the contract. Sources are consulted in configured order
    /// on a miss, so a cache read that consults slots in some other order could
    /// answer from a lower-precedence feed than the one a cold run would have
    /// used. Returning a set rather than an ordered list would leave that
    /// precedence undefined.
    /// </remarks>
    public static IReadOnlyList<string> SourceKeys(IEnumerable<NuGetSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<string>();
        foreach (var source in sources)
        {
            var key = NuGetCache.GetSourceKey(source.Url);
            if (seen.Add(key))
                keys.Add(key);
        }

        return keys;
    }

    public static List<NuGetSource> ResolveSources(NuGetSourceOptions? options, string? workingDirectory = null)
    {
        options ??= NuGetSourceOptions.Default;

        if (options.ConfigFile is not null)
        {
            ValidateExplicitConfig(options.ConfigFile);
        }
        if (options.ResolvedSources is { } resolvedSources)
            return [.. resolvedSources];

        IReadOnlyList<PackageSourceDeclaration> activeDeclarations =
            SourceResolver.GetEffectiveSourceDeclarations(
                options.ConfigFile,
                workingDirectory);
        IReadOnlyList<PackageSourceDeclaration> configuredAliases =
            options.Sources.Length > 0 || options.AdditionalSources.Length > 0
                ? SourceResolver.GetConfiguredSourceAliasDeclarations(
                    options.ConfigFile,
                    workingDirectory)
                : activeDeclarations;

        List<NuGetSource> selected = options.Sources.Length > 0
            ? SelectExplicitSources(
                options.Sources,
                configuredAliases,
                workingDirectory)
            :
            [
                .. activeDeclarations.Select(
                    static declaration => declaration.Resolve()),
            ];
        AddExplicitSources(
            selected,
            options.AdditionalSources,
            configuredAliases,
            workingDirectory);
        return selected;
    }

    /// <summary>
    /// Resolves active source aliases, applies package source mapping for
    /// <paramref name="packageId"/>, and collapses eligible aliases to canonical producers.
    /// </summary>
    /// <remarks>
    /// Mapping names configured aliases, while package payloads and caches name canonical
    /// producer endpoints. Aliases therefore remain distinct until mapping has selected the
    /// package-specific set. Eligible aliases for one producer must agree on credentials.
    /// </remarks>
    /// <exception cref="PackageSourceMappingException">
    /// Mapping is enabled but the package id matches no pattern, none of the mapped names is
    /// active, or eligible aliases for one producer disagree on credentials.
    /// </exception>
    public static List<NuGetSource> ResolveSourcesForPackage(
        NuGetSourceOptions? options,
        string packageId,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        options ??= NuGetSourceOptions.Default;
        PackageSourceMapping mapping =
            ResolvePackageSourceMapping(options, workingDirectory);
        if (!mapping.IsEnabled)
        {
            return CollapseLegacyAliases(
                ResolveSources(options, workingDirectory),
                packageId);
        }

        IReadOnlyList<string> mappedNames =
            mapping.GetConfiguredPackageSources(packageId);
        if (mappedNames.Count == 0)
        {
            throw new PackageSourceMappingException(
                PackageSourceMappingFailure.NoPattern,
                $"Package source mapping has no pattern for package '{packageId}'.");
        }

        if (options.ConfigFile is not null)
            ValidateExplicitConfig(options.ConfigFile);

        var allowedNames = new HashSet<string>(
            mappedNames,
            StringComparer.OrdinalIgnoreCase);
        List<NuGetSource> selected;
        if (options.ResolvedSources is { } resolvedSources)
        {
            selected = [.. resolvedSources];
        }
        else
        {
            IReadOnlyList<PackageSourceDeclaration> activeDeclarations =
                SourceResolver.GetEffectiveSourceDeclarations(
                    options.ConfigFile,
                    workingDirectory);
            IReadOnlyList<PackageSourceDeclaration> configuredAliases =
                options.Sources.Length > 0
                    || options.AdditionalSources.Length > 0
                    ? SourceResolver.GetConfiguredSourceAliasDeclarations(
                        options.ConfigFile,
                        workingDirectory)
                    : activeDeclarations;

            selected = options.Sources.Length > 0
                ? SelectExplicitSources(
                    options.Sources,
                    configuredAliases,
                    workingDirectory)
                :
                [
                    .. activeDeclarations
                        .Where(declaration =>
                            allowedNames.Contains(declaration.Name))
                        .Select(static declaration => declaration.Resolve()),
                ];
            AddExplicitSources(
                selected,
                options.AdditionalSources,
                configuredAliases,
                workingDirectory);
        }

        IReadOnlyList<NuGetSource> eligibleAliases =
        [
            .. selected.Where(source => allowedNames.Contains(source.Name)),
        ];
        if (eligibleAliases.Count == 0)
        {
            throw new PackageSourceMappingException(
                PackageSourceMappingFailure.InactiveSource,
                $"Package '{packageId}' maps to source"
                + $"{(mappedNames.Count == 1 ? "" : "s")} "
                + $"'{string.Join("', '", mappedNames)}', but "
                + $"{(mappedNames.Count == 1 ? "it is not" : "none are")} active.");
        }

        return CollapseLegacyAliases(eligibleAliases, packageId);
    }

    internal static PackageSourceResolution ResolveSourcesForPackageWithFailures(
        NuGetSourceOptions? options,
        string packageId,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        options ??= NuGetSourceOptions.Default;
        PackageSourceMapping mapping = ResolvePackageSourceMapping(options, workingDirectory);
        if (!mapping.IsEnabled)
        {
            PackageSourceResolution resolution =
                ResolveSourcesWithFailures(options, workingDirectory);
            return resolution with
            {
                Sources = CollapseAliases(
                    resolution.Sources,
                    packageId),
            };
        }

        IReadOnlyList<string> mappedNames =
            mapping.GetConfiguredPackageSources(packageId);
        if (mappedNames.Count == 0)
        {
            throw new PackageSourceMappingException(
                PackageSourceMappingFailure.NoPattern,
                $"Package source mapping has no pattern for package '{packageId}'.");
        }

        if (options.ConfigFile is not null)
        {
            ValidateExplicitConfig(options.ConfigFile);
        }

        var allowedNames = new HashSet<string>(
            mappedNames,
            StringComparer.OrdinalIgnoreCase);
        PackageSourceResolution selected;
        if (options.ResolvedSources is { } resolvedSources)
        {
            selected = ClassifySources(resolvedSources);
        }
        else
        {
            IReadOnlyList<PackageSourceDeclaration> activeDeclarations =
                SourceResolver.GetEffectiveSourceDeclarations(
                    options.ConfigFile,
                    workingDirectory);
            IReadOnlyList<PackageSourceDeclaration> configuredAliases =
                options.Sources.Length > 0
                    || options.AdditionalSources.Length > 0
                    ? SourceResolver.GetConfiguredSourceAliasDeclarations(
                        options.ConfigFile,
                        workingDirectory)
                    : activeDeclarations;

            selected = options.Sources.Length > 0
                ? ResolveExplicitSources(
                    options.Sources,
                    configuredAliases,
                    workingDirectory)
                : ResolveDeclarations(
                    activeDeclarations.Where(declaration =>
                        allowedNames.Contains(declaration.Name)));
            AddExplicitSourcesWithFailures(
                selected,
                options.AdditionalSources,
                configuredAliases,
                workingDirectory);
        }

        List<NuGetSource> eligibleAliases =
        [
            .. selected.Sources.Where(source =>
                allowedNames.Contains(source.Name)),
        ];
        List<PackageSourceResolutionFailure> eligibleFailures =
        [
            .. selected.Failures.Where(failure =>
                allowedNames.Contains(failure.Name)),
        ];
        if (eligibleAliases.Count == 0 && eligibleFailures.Count == 0)
        {
            throw new PackageSourceMappingException(
                PackageSourceMappingFailure.InactiveSource,
                $"Package '{packageId}' maps to source"
                + $"{(mappedNames.Count == 1 ? "" : "s")} "
                + $"'{string.Join("', '", mappedNames)}', but "
                + $"{(mappedNames.Count == 1 ? "it is not" : "none are")} active.");
        }

        return new PackageSourceResolution(
            CollapseAliases(eligibleAliases, packageId),
            eligibleFailures);
    }

    internal static PackageSourceMapping ResolvePackageSourceMapping(
        NuGetSourceOptions? options,
        string? workingDirectory = null)
        => SourceResolver.ResolvePackageSourceMapping(
            options?.ConfigFile,
            workingDirectory);

    internal static bool IsAliasEligibleForPackage(
        NuGetSource source,
        IReadOnlyList<NuGetSource> activeAliases,
        PackageSourceMapping mapping,
        string packageId)
    {
        if (!mapping.IsEnabled)
        {
            return true;
        }

        IReadOnlyList<string> mappedNames =
            mapping.GetConfiguredPackageSources(packageId);
        if (mappedNames.Count == 0)
        {
            return false;
        }

        var allowedNames = new HashSet<string>(
            mappedNames,
            StringComparer.OrdinalIgnoreCase);
        List<NuGetSource> eligibleAliases =
        [
            .. activeAliases.Where(alias => allowedNames.Contains(alias.Name)),
        ];
        if (eligibleAliases.Count == 0)
        {
            return false;
        }

        _ = CollapseLegacyAliases(eligibleAliases, packageId);
        return allowedNames.Contains(source.Name);
    }

    /// <summary>
    /// Returns a description of why <paramref name="configFile"/> cannot be used as a NuGet
    /// config, or null when it can. Exposed so the CLI can report the same problem at parse
    /// time rather than letting it surface as an exception from whichever service resolves
    /// sources first.
    /// </summary>
    /// <remarks>
    /// This method reports problems; it does not raise them. Its caller is a parse-time option
    /// validator, and an exception thrown there escapes before any command runs — outside every
    /// handler in Program.cs, which wrap invocation rather than parsing — and terminates the
    /// process with a raw stack trace. Every reason a config cannot be read is therefore a
    /// returned string, including the ones that arrive as exceptions.
    /// </remarks>
    public static string? DescribeConfigProblem(string configFile)
    {
        if (!File.Exists(configFile))
        {
            return $"NuGet config file not found: '{configFile}'.";
        }

        try
        {
            XDocument.Load(configFile);
        }
        catch (XmlException ex)
        {
            return $"NuGet config file '{configFile}' is not valid XML: {ex.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Exists but cannot be opened: locked by another process, denied by ACL, or not a
            // regular file. Unusable for the same reason a missing file is unusable.
            return $"NuGet config file '{configFile}' could not be read: {ex.Message}";
        }

        // Well-formed XML is not enough. Any XML file parses — a .csproj passed
        // by mistake reaches this point. Source values remain unresolved here:
        // package mapping and explicit selection have not selected aliases yet.
        if (SourceResolver.GetConfiguredSourceAliasDeclarations(
                configFile).Count == 0)
        {
            return $"NuGet config file '{configFile}' declares no package sources.";
        }

        return null;
    }

    /// <summary>
    /// Validates a user-supplied <c>--nugetconfig</c> path before it is used.
    /// </summary>
    /// <remarks>
    /// Ambient resolution starts with the default NuGet.org source layer before merging discovered
    /// configuration. An explicitly selected config starts empty instead: a mistyped path or
    /// malformed file must not search unrelated feeds and exit 0, reporting someone else's
    /// packages as the answer. An explicit config that cannot be used is a failure, not a reason
    /// to pick a default.
    /// </remarks>
    private static void ValidateExplicitConfig(string configFile)
    {
        if (DescribeConfigProblem(configFile) is not string problem)
        {
            return;
        }

        throw File.Exists(configFile)
            ? new InvalidOperationException(problem)
            : new FileNotFoundException(problem, configFile);
    }

    /// <summary>
    /// Builds the source list for an explicit <c>--source</c> selection.
    /// </summary>
    /// <remarks>
    /// <c>--source</c> replaces the configured defaults. SourceResolver's explicit-source fast
    /// path takes a single value, so more than one had been forwarded as *additional* sources,
    /// which re-entered config resolution and silently searched feeds the user never named — and
    /// a single <c>--source</c> combined with <c>--add-source</c> dropped the added source
    /// entirely. Selection is resolved here instead.
    ///
    /// Credentials still come from configuration: a user who names an authenticated feed on the
    /// command line has already declared that feed's credentials in nuget.config, keyed by the
    /// same URL, and NuGet's own client matches them the same way.
    /// </remarks>
    private static List<NuGetSource> SelectExplicitSources(
        IEnumerable<string> urls,
        IReadOnlyList<PackageSourceDeclaration> configured,
        string? workingDirectory)
    {
        List<NuGetSource> selected = [];
        AddExplicitSources(selected, urls, configured, workingDirectory);
        return selected;
    }

    private static PackageSourceResolution ResolveSourcesWithFailures(
        NuGetSourceOptions options,
        string? workingDirectory)
    {
        if (options.ConfigFile is not null)
            ValidateExplicitConfig(options.ConfigFile);

        if (options.ResolvedSources is { } resolvedSources)
            return ClassifySources(resolvedSources);

        IReadOnlyList<PackageSourceDeclaration> activeDeclarations =
            SourceResolver.GetEffectiveSourceDeclarations(
                options.ConfigFile,
                workingDirectory);
        IReadOnlyList<PackageSourceDeclaration> configuredAliases =
            options.Sources.Length > 0 || options.AdditionalSources.Length > 0
                ? SourceResolver.GetConfiguredSourceAliasDeclarations(
                    options.ConfigFile,
                    workingDirectory)
                : activeDeclarations;

        PackageSourceResolution selected = options.Sources.Length > 0
            ? ResolveExplicitSources(
                options.Sources,
                configuredAliases,
                workingDirectory)
            : ResolveDeclarations(activeDeclarations);
        AddExplicitSourcesWithFailures(
            selected,
            options.AdditionalSources,
            configuredAliases,
            workingDirectory);
        return selected;
    }

    private static PackageSourceResolution ResolveDeclarations(
        IEnumerable<PackageSourceDeclaration> declarations)
    {
        var sources = new List<NuGetSource>();
        var failures = new List<PackageSourceResolutionFailure>();
        foreach (PackageSourceDeclaration declaration in declarations)
        {
            try
            {
                AddClassifiedSource(
                    declaration.Resolve(),
                    sources,
                    failures);
            }
            catch (UnsupportedSourceException exception)
            {
                AddResolutionFailure(
                    declaration.Name,
                    url: null,
                    exception.Message,
                    failures);
            }
        }

        return new PackageSourceResolution(sources, failures);
    }

    private static PackageSourceResolution ResolveExplicitSources(
        IEnumerable<string> urls,
        IReadOnlyList<PackageSourceDeclaration> configured,
        string? workingDirectory)
    {
        var resolution = new PackageSourceResolution(
            new List<NuGetSource>(),
            new List<PackageSourceResolutionFailure>());
        AddExplicitSourcesWithFailures(
            resolution,
            urls,
            configured,
            workingDirectory);
        return resolution;
    }

    private static PackageSourceResolution ClassifySources(
        IEnumerable<NuGetSource> sources)
    {
        var classified = new List<NuGetSource>();
        var failures = new List<PackageSourceResolutionFailure>();
        foreach (NuGetSource source in sources)
            AddClassifiedSource(source, classified, failures);

        return new PackageSourceResolution(classified, failures);
    }

    private static void AddClassifiedSource(
        NuGetSource source,
        List<NuGetSource> sources,
        List<PackageSourceResolutionFailure> failures)
    {
        if (ConfiguredPackageAuthorityKey.TryCreate(
                source,
                out _,
                out string? problem))
        {
            sources.Add(source);
            return;
        }

        AddResolutionFailure(
            source.Name,
            source.Url,
            problem,
            failures);
    }

    private static void AddResolutionFailure(
        string name,
        string? url,
        string message,
        List<PackageSourceResolutionFailure> failures)
    {
        InertString authority = PackageSourceDisplay.ForDiagnostics(name, url);
        failures.Add(new PackageSourceResolutionFailure(
            name,
            authority,
            $"Package source {authority} is unusable. {message}"));
    }

    private static void AddExplicitSourcesWithFailures(
        PackageSourceResolution resolution,
        IEnumerable<string> urls,
        IReadOnlyList<PackageSourceDeclaration> configured,
        string? workingDirectory)
    {
        foreach (string url in urls)
        {
            string resolved;
            try
            {
                resolved = SourceResolver.ResolveSourceValue(
                    url,
                    workingDirectory);
            }
            catch (UnsupportedSourceException exception)
            {
                PackageSourceDeclaration[] matchingDeclarations =
                [
                    .. configured.Where(declaration =>
                        declaration.MatchesUnclassifiedValue(url)),
                ];
                if (matchingDeclarations.Length == 0)
                {
                    AddResolutionFailure(
                        url,
                        url,
                        exception.Message,
                        resolution.Failures);
                }
                else
                {
                    foreach (PackageSourceDeclaration declaration in
                             matchingDeclarations)
                    {
                        AddResolutionFailure(
                            declaration.Name,
                            url,
                            exception.Message,
                            resolution.Failures);
                    }
                }
                continue;
            }

            foreach (NuGetSource match in Match(resolved, configured))
            {
                if (!resolution.Sources.Contains(match))
                {
                    AddClassifiedSource(
                        match,
                        resolution.Sources,
                        resolution.Failures);
                }
            }
        }
    }

    private static void AddExplicitSources(
        List<NuGetSource> selected,
        IEnumerable<string> urls,
        IReadOnlyList<PackageSourceDeclaration> configured,
        string? workingDirectory)
    {
        foreach (string url in urls)
        {
            string resolved = SourceResolver.ResolveSourceValue(
                url,
                workingDirectory);
            foreach (NuGetSource match in Match(resolved, configured))
            {
                if (!selected.Contains(match))
                {
                    selected.Add(match);
                }
            }
        }
    }

    /// <summary>
    /// Finds the configured source that names the same endpoint as <paramref name="url"/>, so an
    /// explicitly requested feed can use the credentials configured for it.
    /// </summary>
    /// <remarks>
    /// The match is deliberately narrow. Comparing whole URLs case-insensitively would alias
    /// <c>/FeedA</c> and <c>/feeda</c>, which are different feeds on servers with case-sensitive
    /// paths, and would hand one feed's credentials to the other. Origin is compared
    /// case-insensitively because scheme and host are case-insensitive by definition; path and
    /// query are compared ordinally on their raw form, normalizing only
    /// percent-escape hex casing and one trailing path slash. This preserves
    /// encoded-unreserved and dot-segment distinctions that
    /// <see cref="Uri"/> otherwise collapses.
    ///
    /// Every configured alias for the endpoint is retained. Package source mapping names those
    /// aliases, so selecting one before the package id is known would either bypass mapping or
    /// discard the credential attached to the alias mapping later selects.
    ///
    /// On a match only the credentials are adopted. The URL stays exactly as the user spelled it,
    /// so a request never silently goes somewhere other than where it was pointed.
    /// </remarks>
    private static IReadOnlyList<NuGetSource> Match(
        string url,
        IReadOnlyList<PackageSourceDeclaration> configured)
    {
        List<NuGetSource> matches = [];
        foreach (PackageSourceDeclaration declaration in configured)
        {
            NuGetSource source;
            try
            {
                source = declaration.Resolve();
            }
            catch (UnsupportedSourceException)
            {
                continue;
            }

            if (string.Equals(
                    source.Url,
                    url,
                    StringComparison.Ordinal)
                || IsSameSource(source.Url, url))
            {
                matches.Add(source with { Url = url });
            }
        }

        return matches.Count == 0
            ? [new NuGetSource(url, url)]
            : matches;
    }

    private static bool IsSameSource(string left, string right)
    {
        bool leftIsLocal = LocalPackageSourceIdentity.IsLocalSource(left);
        bool rightIsLocal = LocalPackageSourceIdentity.IsLocalSource(right);
        if (leftIsLocal || rightIsLocal)
        {
            return leftIsLocal
                && rightIsLocal
                && LocalPackageSourceIdentity.CreateAbsolute(left).Equals(
                    LocalPackageSourceIdentity.CreateAbsolute(right));
        }

        var leftSource = new NuGetSource("left", left);
        var rightSource = new NuGetSource("right", right);
        return ConfiguredPackageAuthorityKey.TryCreate(
                leftSource,
                out ConfiguredPackageAuthorityKey? leftKey,
                out _)
            && ConfiguredPackageAuthorityKey.TryCreate(
                rightSource,
                out ConfiguredPackageAuthorityKey? rightKey,
                out _)
            && leftKey.Equals(rightKey);
    }

    private static List<NuGetSource> CollapseAliases(
        IReadOnlyList<NuGetSource> eligibleAliases,
        string packageId) =>
        CollapseAliases(
            eligibleAliases,
            packageId,
            ConfiguredPackageAuthorityKey.Create);

    private static List<NuGetSource> CollapseLegacyAliases(
        IReadOnlyList<NuGetSource> eligibleAliases,
        string packageId) =>
        CollapseAliases(
            eligibleAliases,
            packageId,
            LegacyAuthorityKey);

    private static List<NuGetSource> CollapseAliases<TKey>(
        IReadOnlyList<NuGetSource> eligibleAliases,
        string packageId,
        Func<NuGetSource, TKey> keySelector)
        where TKey : notnull
    {
        List<NuGetSource> authorities = [];
        foreach (IGrouping<TKey, NuGetSource> aliases
                 in eligibleAliases.GroupBy(keySelector))
        {
            NuGetSource first = aliases.First();
            if (aliases.Any(alias => alias.Credential != first.Credential))
            {
                throw new PackageSourceMappingException(
                    PackageSourceMappingFailure.ConflictingCredentials,
                    $"Package '{packageId}' is eligible from multiple configured names for "
                    + $"'{UrlRedaction.ForDiagnostics(first.Url)}', but those names use conflicting credentials.");
            }

            authorities.Add(first);
        }

        return authorities;
    }

    private static string LegacyAuthorityKey(NuGetSource source)
    {
        if (LocalPackageSourceIdentity.IsLocalSource(source.Url))
        {
            return
                $"local\n{LocalPackageSourceIdentity.CreateAbsolute(source.Url).PersistentValue}";
        }

        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? endpoint))
        {
            throw new ArgumentException(
                "The package source is unusable.",
                nameof(source));
        }

        return NuGetCredentialScope.CanonicalizeEndpoint(endpoint);
    }
}
