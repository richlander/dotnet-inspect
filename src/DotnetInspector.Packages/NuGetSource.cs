// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace DotnetInspector.Packages;

/// <summary>
/// Represents a NuGet package source.
/// </summary>
/// <param name="Name">Display name of the source</param>
/// <param name="Url">URL of the source (V3 feed URL)</param>
public record NuGetSource(string Name, string Url)
{
    /// <summary>
    /// The default nuget.org source.
    /// </summary>
    public static NuGetSource NuGetOrg { get; } = new("nuget.org", "https://api.nuget.org/v3/index.json");

    /// <summary>
    /// Returns the flat-container base URL for this source.
    /// For nuget.org, this is the well-known URL. For other sources, returns null
    /// (caller should use V3 service index discovery).
    /// </summary>
    public string? GetFlatContainerUrl()
    {
        // nuget.org has a well-known flat-container URL
        if (Url.Contains("api.nuget.org", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.nuget.org/v3-flatcontainer";
        }

        return null;
    }
}
