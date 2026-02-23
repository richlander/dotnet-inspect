namespace DotnetInspector.Services;

/// <summary>
/// Constants for search scope definitions (platforms, packages, frameworks).
/// </summary>
public static class ScopeConstants
{
    /// <summary>
    /// Platform framework names for --platform scope.
    /// </summary>
    public static readonly string[] PlatformFrameworks = ["runtime", "aspnetcore", "netstandard"];

    /// <summary>
    /// Curated Microsoft.Extensions.* packages for --extensions scope.
    /// </summary>
    public static readonly string[] ExtensionsPackages =
    [
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Hosting.Abstractions",
        "Microsoft.Extensions.FileProviders.Abstractions",
        "Microsoft.Extensions.Http",
        "Microsoft.Extensions.Caching.Memory",
        "Microsoft.Extensions.Caching.Abstractions",
        "Microsoft.Extensions.Telemetry.Abstractions",
        "Microsoft.Extensions.AI",
        "Microsoft.Extensions.AI.Abstractions",
    ];

    /// <summary>
    /// Curated Microsoft.AspNetCore.* packages for --aspnetcore scope.
    /// </summary>
    public static readonly string[] AspNetCorePackages =
    [
        "Microsoft.AspNetCore.Authentication",
        "Microsoft.AspNetCore.Authorization",
        "Microsoft.AspNetCore.Components",
        "Microsoft.AspNetCore.Mvc.Core",
        "Microsoft.AspNetCore.SignalR",
    ];

    /// <summary>
    /// Previously used for implicit default scope. Now empty - all frameworks are the default.
    /// Retained for --curated flag compatibility.
    /// </summary>
    public static readonly string[] CuratedPackages = [];
}
