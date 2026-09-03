// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace DotnetInspector.Packages;

/// <summary>
/// Options for configuring NuGet package sources.
/// </summary>
public record NuGetSourceOptions
{
    public string[] Sources { get; init; } = [];
    public string[] AdditionalSources { get; init; } = [];
    public string? ConfigFile { get; init; }
    public string? ConfigDirectory { get; init; }
    internal string[]? AuthorizedSourceKeys { get; init; }
    internal NuGetFetch.PackageSource[]? ResolvedSources { get; init; }
    public static NuGetSourceOptions Default { get; } = new();
}
