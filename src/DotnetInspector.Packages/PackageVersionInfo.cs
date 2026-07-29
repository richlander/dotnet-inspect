// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace DotnetInspector.Packages;

/// <summary>
/// A single published package version together with its NuGet listing status.
/// </summary>
/// <param name="Version">The published version string (as reported by the feed).</param>
/// <param name="Listed">
/// <c>true</c> when the version is listed (visible in discovery on nuget.org). <c>false</c> when it
/// is explicitly unlisted. When listing status cannot be determined (a non-nuget.org feed, or the
/// registration index was unavailable), the version is reported as listed so discovery fails open
/// rather than hiding real versions.
/// </param>
public sealed record PackageVersionInfo(string Version, bool Listed);
