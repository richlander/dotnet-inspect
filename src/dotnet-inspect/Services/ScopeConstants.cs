namespace DotnetInspector.Services;

/// <summary>
/// Constants for search scope definitions (platforms, packages, frameworks).
/// </summary>
public static class ScopeConstants
{
    /// <summary>
    /// Maximum package coordinates contributed by a package-prefix expansion.
    /// </summary>
    public const int PackagePrefixExpansionLimit = 500;

    /// <summary>
    /// Platform framework names for --platform scope.
    /// </summary>
    public static readonly string[] PlatformFrameworks = ["runtime", "aspnetcore", "netstandard"];
}
