namespace DotnetInspector.Services;

/// <summary>
/// Resolves search scopes based on explicit flags and applies curated defaults.
/// </summary>
public static class ScopeResolver
{
    /// <summary>
    /// Scope flags parsed from command line options.
    /// </summary>
    public record ScopeFlags(
        bool Platform = false,
        bool Extensions = false,
        bool AspNetCore = false,
        bool Curated = false);

    /// <summary>
    /// Resolved scope containing frameworks and packages to search.
    /// </summary>
    public record ResolvedScope(string[] Frameworks, string[] Packages);

    /// <summary>
    /// Resolves the search scope based on explicit flags and existing packages.
    /// When no explicit scope is specified, applies curated defaults (all platform frameworks + curated packages).
    /// </summary>
    /// <param name="flags">Scope flags from command line options.</param>
    /// <param name="existingPackages">Packages already specified via --package.</param>
    /// <param name="existingAssemblies">Assemblies already specified via --library.</param>
    /// <param name="packagePrefix">Package prefix if specified via --package-prefix.</param>
    /// <param name="hasOtherScopeIndicators">Additional scope indicators (e.g., projects, binPaths).</param>
    /// <returns>Resolved frameworks and packages arrays.</returns>
    public static ResolvedScope Resolve(
        ScopeFlags flags,
        string[] existingPackages,
        string[] existingAssemblies,
        string? packagePrefix = null,
        bool hasOtherScopeIndicators = false)
    {
        bool hasExplicitScope = flags.Platform || flags.Extensions || flags.AspNetCore || flags.Curated
            || existingPackages.Length > 0 || existingAssemblies.Length > 0 || packagePrefix != null
            || hasOtherScopeIndicators;

        string[] frameworks = [];
        string[] packages = existingPackages;

        if (!hasExplicitScope)
        {
            // Default scope: all platform frameworks + curated packages
            frameworks = ScopeConstants.PlatformFrameworks;
            packages = [.. packages, .. ScopeConstants.CuratedPackages];
        }
        else
        {
            if (flags.Platform)
                frameworks = ScopeConstants.PlatformFrameworks;

            if (flags.Extensions)
                packages = [.. packages, .. ScopeConstants.ExtensionsPackages];

            if (flags.AspNetCore)
                packages = [.. packages, .. ScopeConstants.AspNetCorePackages];

            if (flags.Curated)
            {
                frameworks = [.. frameworks, .. ScopeConstants.PlatformFrameworks];
                packages = [.. packages, .. ScopeConstants.CuratedPackages];
            }
        }

        return new ResolvedScope(frameworks, packages);
    }
}
