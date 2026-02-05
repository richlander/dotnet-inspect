// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace DotnetInspector.Packages;

/// <summary>
/// Options for configuring NuGet package sources.
/// </summary>
public record NuGetSourceOptions
{
    /// <summary>
    /// Explicit sources that replace all default sources.
    /// When set, only these sources are used (nuget.org is not included unless explicitly added).
    /// </summary>
    public string[] Sources { get; init; } = [];

    /// <summary>
    /// Additional sources to use alongside default/configured sources.
    /// </summary>
    public string[] AdditionalSources { get; init; } = [];

    /// <summary>
    /// Path to a specific nuget.config file to use.
    /// If not set, nuget.config files are discovered by walking up the directory tree.
    /// </summary>
    public string? ConfigFile { get; init; }

    /// <summary>
    /// Default options that use nuget.org only.
    /// </summary>
    public static NuGetSourceOptions Default { get; } = new();

    /// <summary>
    /// Returns true if any custom source configuration is specified.
    /// </summary>
    public bool HasCustomConfiguration =>
        Sources.Length > 0 || AdditionalSources.Length > 0 || ConfigFile != null;
}
