// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using System.Text;

namespace DotnetInspector.Packages;

/// <summary>
/// Represents a NuGet package source.
/// </summary>
/// <param name="Name">Display name of the source</param>
/// <param name="Url">URL of the source (V3 feed URL)</param>
public record NuGetSource(string Name, string Url)
{
    /// <summary>
    /// Optional credentials for authenticated feeds.
    /// </summary>
    public NuGetSourceCredential? Credentials { get; init; }

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
        if (IsNuGetOrg())
        {
            return "https://api.nuget.org/v3-flatcontainer";
        }

        return null;
    }

    /// <summary>
    /// Returns true if this source points to nuget.org.
    /// </summary>
    public bool IsNuGetOrg() => Url.Contains("api.nuget.org", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an Authorization header value for this source, or null if no credentials.
    /// </summary>
    public AuthenticationHeaderValue? GetAuthHeader()
    {
        if (Credentials is null) return null;
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Credentials.Username}:{Credentials.Password}"));
        return new AuthenticationHeaderValue("Basic", encoded);
    }
}

/// <summary>
/// Credentials for an authenticated NuGet source.
/// </summary>
/// <param name="Username">User name</param>
/// <param name="Password">Password in clear text (decrypted if necessary)</param>
public record NuGetSourceCredential(string Username, string Password);
