// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace DotnetInspector.Packages;

/// <summary>
/// A published package version together with the feed that served it and its listing status.
/// </summary>
/// <param name="Version">The published version string (as reported by the feed).</param>
/// <param name="Feed">
/// A short label for the feed that carried this version. The source's configured name when it has
/// a meaningful one, otherwise the host, falling back to the full URL when neither distinguishes
/// two sources.
/// </param>
/// <param name="Listed">
/// <c>true</c> when the version is listed on the feed that served it. Only nuget.org publishes a
/// listing status; versions from other feeds are reported as listed because those feeds have no
/// such concept.
/// </param>
public sealed record PackageVersionSourceInfo(string Version, string Feed, bool Listed);
