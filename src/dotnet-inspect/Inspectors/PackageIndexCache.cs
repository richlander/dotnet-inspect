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
        var content = CoreCache.TryGet(Category, key, extension: "md");
        if (content == null) return null;

        try
        {
            var dict = FieldParser.ParseToDictionary(content);
            var result = new InspectionResult
            {
                PackageName = GetString(dict, "packageName") ?? packageName,
                Version = GetString(dict, "version") ?? version,
                Description = GetString(dict, "description"),
                Authors = GetString(dict, "authors"),
                License = GetString(dict, "license"),
                Repository = GetString(dict, "repository"),
                ReadmeFile = GetString(dict, "readmeFile"),
                HasReadme = GetBool(dict, "hasReadme"),
                IsToolPackage = GetBool(dict, "isToolPackage"),
                AssemblyCount = GetInt(dict, "assemblyCount"),
                IsFrameworkDependent = GetBool(dict, "isFrameworkDependent"),
                HasRidSpecificAssets = GetBool(dict, "hasRidSpecificAssets"),
                HasNativeDependencies = GetBool(dict, "hasNativeDependencies"),
                IsRidSpecificPointerPackage = GetBool(dict, "isRidSpecificPointerPackage"),
                ToolFormat = GetString(dict, "toolFormat"),
                RuntimeTargetRid = GetString(dict, "runtimeTargetRid"),
                PackageTypes = GetArray(dict, "packageTypes"),
                ContentDirectories = GetArray(dict, "contentDirs"),
                TargetFrameworks = GetArray(dict, "targetFrameworks"),
                SupportedRids = GetArray(dict, "supportedRids"),
                ToolCommands = GetArray(dict, "toolCommands"),
                NativeFiles = GetArray(dict, "nativeFiles"),
            };

            // Dependency groups stored as compact strings: "tfm|name@ver,name@ver"
            var depGroupsRaw = GetArray(dict, "dependencyGroups");
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
    /// </summary>
    public static void Set(string packageName, string version, InspectionResult result)
    {
        string key = $"{packageName.ToLowerInvariant()}@{version}";

        var sb = new System.Text.StringBuilder(512);

        WriteField(sb, "packageName", result.PackageName);
        WriteField(sb, "version", result.Version);
        WriteField(sb, "description", result.Description);
        WriteField(sb, "authors", result.Authors);
        WriteField(sb, "license", result.License);
        WriteField(sb, "repository", result.Repository);
        WriteField(sb, "readmeFile", result.ReadmeFile);
        WriteField(sb, "hasReadme", result.HasReadme);
        WriteField(sb, "isToolPackage", result.IsToolPackage);
        WriteField(sb, "assemblyCount", result.AssemblyCount);
        WriteField(sb, "isFrameworkDependent", result.IsFrameworkDependent);
        WriteField(sb, "hasRidSpecificAssets", result.HasRidSpecificAssets);
        WriteField(sb, "hasNativeDependencies", result.HasNativeDependencies);
        WriteField(sb, "isRidSpecificPointerPackage", result.IsRidSpecificPointerPackage);
        WriteField(sb, "toolFormat", result.ToolFormat);
        WriteField(sb, "runtimeTargetRid", result.RuntimeTargetRid);

        WriteArray(sb, "packageTypes", result.PackageTypes);
        WriteArray(sb, "contentDirs", result.ContentDirectories);
        WriteArray(sb, "targetFrameworks", result.TargetFrameworks);
        WriteArray(sb, "supportedRids", result.SupportedRids);
        WriteArray(sb, "toolCommands", result.ToolCommands);
        WriteArray(sb, "nativeFiles", result.NativeFiles);

        // Serialize dependency groups compactly: "tfm|name@ver,name@ver"
        if (result.DependencyGroups is { Count: > 0 })
        {
            var groupStrings = result.DependencyGroups.Select(FormatDependencyGroup).ToList();
            WriteArray(sb, "dependencyGroups", groupStrings);
        }

        CoreCache.Set(Category, key, sb.ToString(), extension: "md");
    }

    // ── Field serialization ──

    private static void WriteField(System.Text.StringBuilder sb, string key, string? value)
    {
        if (value != null)
            sb.AppendLine($"**{key}:** {value}");
    }

    private static void WriteField(System.Text.StringBuilder sb, string key, bool value)
    {
        if (value)
            sb.AppendLine($"**{key}:** true");
    }

    private static void WriteField(System.Text.StringBuilder sb, string key, int value)
    {
        if (value != 0)
            sb.AppendLine($"**{key}:** {value}");
    }

    private static void WriteArray(System.Text.StringBuilder sb, string key, List<string>? items)
    {
        if (items is not { Count: > 0 }) return;
        sb.AppendLine();
        sb.AppendLine($"**{key}:**");
        foreach (var item in items)
            sb.AppendLine($"- {item}");
    }

    // ── Field deserialization helpers ──

    private static string? GetString(Dictionary<string, FieldValue> dict, string key)
        => dict.TryGetValue(key, out var v) ? v.Text : null;

    private static bool GetBool(Dictionary<string, FieldValue> dict, string key)
        => dict.TryGetValue(key, out var v) && v.Text.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static int GetInt(Dictionary<string, FieldValue> dict, string key)
        => dict.TryGetValue(key, out var v) && int.TryParse(v.Text, out var i) ? i : 0;

    private static List<string>? GetArray(Dictionary<string, FieldValue> dict, string key)
        => dict.TryGetValue(key, out var v) && v.IsArray ? [.. v.Items] : null;

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
