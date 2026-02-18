using DotnetInspector.Core;
using DotnetInspector.Models;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using MarkdownTable.Formatting;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Caches the filesystem-derived fields of InspectionResult as markdown fields.
/// On cache hit, skips all directory scanning, nuspec parsing, and deps.json parsing.
/// Metadata (downloads, vulnerabilities) is cached separately by PackageMetadataService.
/// </summary>
internal static class PackageIndexCache
{
    private const string Category = "pkg-index";

    /// <summary>
    /// Tries to load a cached InspectionResult for a package version.
    /// Returns null on cache miss.
    /// </summary>
    public static InspectionResult? TryGet(string packageName, string version)
    {
        string key = $"{packageName.ToLowerInvariant()}@{version}";
        var bytes = CoreCache.TryGetBytes(Category, key, extension: "md");
        if (bytes == null) return null;

        try
        {
            using var doc = FieldDocument.Parse(bytes);
            var result = new InspectionResult
            {
                PackageName = doc.GetString("packageName") ?? packageName,
                Version = doc.GetString("version") ?? version,
                Description = doc.GetString("description"),
                Authors = doc.GetString("authors"),
                License = doc.GetString("license"),
                Repository = doc.GetString("repository"),
                ReadmeFile = doc.GetString("readmeFile"),
                HasReadme = doc.GetBool("hasReadme"),
                IsToolPackage = doc.GetBool("isToolPackage"),
                AssemblyCount = doc.GetInt32("assemblyCount"),
                IsFrameworkDependent = doc.GetBool("isFrameworkDependent"),
                HasRidSpecificAssets = doc.GetBool("hasRidSpecificAssets"),
                HasNativeDependencies = doc.GetBool("hasNativeDependencies"),
                IsRidSpecificPointerPackage = doc.GetBool("isRidSpecificPointerPackage"),
                ToolFormat = doc.GetString("toolFormat"),
                RuntimeTargetRid = doc.GetString("runtimeTargetRid"),
                PackageTypes = doc.GetArrayList("packageTypes"),
                ContentDirectories = doc.GetArrayList("contentDirs"),
                TargetFrameworks = doc.GetArrayList("targetFrameworks"),
                SupportedRids = doc.GetArrayList("supportedRids"),
                ToolCommands = doc.GetArrayList("toolCommands"),
                NativeFiles = doc.GetArrayList("nativeFiles"),
            };

            // Built date (stored as ISO 8601)
            if (doc.GetString("builtDate") is string bd
                && DateTimeOffset.TryParse(bd, out var builtDate))
            {
                result.BuiltDate = builtDate;
            }

            // Dependency groups stored as compact strings: "tfm|name@ver,name@ver"
            var depGroupsRaw = doc.GetArrayList("dependencyGroups");
            if (depGroupsRaw != null)
            {
                result.DependencyGroups = depGroupsRaw.Select(ParseDependencyGroup).ToList();
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Caches an InspectionResult (filesystem-derived fields only).
    /// Uses plain field format (key: value) with fields ordered by access frequency.
    /// </summary>
    public static void Set(string packageName, string version, InspectionResult result)
    {
        string key = $"{packageName.ToLowerInvariant()}@{version}";

        var sb = new System.Text.StringBuilder(512);

        // Scalars first, ordered by access frequency and display priority
        WriteField(sb, "packageName", result.PackageName);
        WriteField(sb, "version", result.Version);
        WriteField(sb, "description", result.Description);
        WriteField(sb, "authors", result.Authors);
        WriteField(sb, "license", result.License);
        WriteField(sb, "assemblyCount", result.AssemblyCount);
        WriteField(sb, "hasReadme", result.HasReadme);
        WriteField(sb, "isToolPackage", result.IsToolPackage);
        WriteField(sb, "isFrameworkDependent", result.IsFrameworkDependent);
        WriteField(sb, "hasRidSpecificAssets", result.HasRidSpecificAssets);
        WriteField(sb, "hasNativeDependencies", result.HasNativeDependencies);
        WriteField(sb, "isRidSpecificPointerPackage", result.IsRidSpecificPointerPackage);
        WriteField(sb, "repository", result.Repository);
        WriteField(sb, "readmeFile", result.ReadmeFile);
        WriteField(sb, "toolFormat", result.ToolFormat);
        WriteField(sb, "runtimeTargetRid", result.RuntimeTargetRid);
        if (result.BuiltDate.HasValue)
            WriteField(sb, "builtDate", result.BuiltDate.Value.ToString("o"));

        // Arrays last
        WriteArray(sb, "packageTypes", result.PackageTypes);
        WriteArray(sb, "contentDirs", result.ContentDirectories);
        WriteArray(sb, "targetFrameworks", result.TargetFrameworks);
        WriteArray(sb, "supportedRids", result.SupportedRids);
        WriteArray(sb, "toolCommands", result.ToolCommands);
        WriteArray(sb, "nativeFiles", result.NativeFiles);

        // Dependency groups stored as compact strings: "tfm|name@ver,name@ver"
        if (result.DependencyGroups is { Count: > 0 })
        {
            var groupStrings = result.DependencyGroups.Select(FormatDependencyGroup).ToList();
            WriteArray(sb, "dependencyGroups", groupStrings);
        }

        CoreCache.Set(Category, key, sb.ToString(), extension: "md");
    }

    // ── Field serialization (plain format: "key: value") ──

    private static void WriteField(System.Text.StringBuilder sb, string key, string? value)
    {
        if (value != null)
            sb.AppendLine($"{key}: {value}");
    }

    private static void WriteField(System.Text.StringBuilder sb, string key, bool value)
    {
        if (value)
            sb.AppendLine($"{key}: true");
    }

    private static void WriteField(System.Text.StringBuilder sb, string key, int value)
    {
        if (value != 0)
            sb.AppendLine($"{key}: {value}");
    }

    private static void WriteArray(System.Text.StringBuilder sb, string key, List<string>? items)
    {
        if (items is not { Count: > 0 }) return;
        sb.AppendLine();
        sb.AppendLine($"{key}:");
        foreach (var item in items)
            sb.AppendLine($"- {item}");
    }

    // ── Dependency group serialization ──

    private static string FormatDependencyGroup(DependencyGroup group)
    {
        var deps = group.Dependencies?.Select(d =>
            d.Version.Length > 0 ? $"{d.Id}@{d.Version}" : d.Id) ?? [];
        return $"{group.TargetFramework ?? "any"}|{string.Join(",", deps)}";
    }

    private static DependencyGroup ParseDependencyGroup(string raw)
    {
        var parts = raw.Split('|', 2);
        var tfm = parts[0] == "any" ? null : parts[0];
        List<PackageDependency>? deps = null;

        if (parts.Length > 1 && parts[1].Length > 0)
        {
            deps = parts[1].Split(',').Select(d =>
            {
                var at = d.IndexOf('@');
                return at > 0
                    ? new PackageDependency { Id = d[..at], Version = d[(at + 1)..] }
                    : new PackageDependency { Id = d };
            }).ToList();
        }

        return new DependencyGroup { TargetFramework = tfm ?? "", Dependencies = deps ?? [] };
    }
}
