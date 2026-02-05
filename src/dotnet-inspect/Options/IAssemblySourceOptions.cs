using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Common options for commands that search across multiple assembly sources.
/// </summary>
public interface IAssemblySourceOptions
{
    /// <summary>
    /// Packages to search (name or name@version).
    /// </summary>
    string[] Packages { get; }

    /// <summary>
    /// Direct assembly paths.
    /// </summary>
    string[] Assemblies { get; }

    /// <summary>
    /// Platform assemblies (e.g., System.Text.Json).
    /// </summary>
    string[] PlatformAssemblies { get; }

    /// <summary>
    /// Platform frameworks (e.g., runtime, aspnetcore).
    /// </summary>
    string[] PlatformFrameworks { get; }

    /// <summary>
    /// Target framework moniker filter.
    /// </summary>
    string? Tfm { get; }

    /// <summary>
    /// NuGet source configuration options.
    /// </summary>
    NuGetSourceOptions? SourceOptions { get; }
}
