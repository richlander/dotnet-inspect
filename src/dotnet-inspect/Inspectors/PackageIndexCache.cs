using System.Buffers;
using System.Text;
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
    internal const string Category = "pkg-index-v8";

    static PackageIndexCache()
    {
        CoreCache.RegisterVersionedCategory("pkg-index-v", Category);
    }

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
                ManifestVersion = doc.GetString("manifestVersion"),
                Version = doc.GetString("version") ?? version,
                Description = doc.GetString("description"),
                Authors = doc.GetString("authors"),
                License = doc.GetString("license"),
                LicenseUrl = doc.GetString("licenseUrl"),
                Repository = doc.GetString("repository"),
                RepositoryType = doc.GetString("repositoryType"),
                RepositoryCommit = doc.GetString("repositoryCommit"),
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
                LibraryFiles = doc.GetArrayList("libraryFiles"),
            };
            var totalBinaries = doc.GetInt32("totalBinaries");
            if (totalBinaries > 0)
            {
                result.BinarySignals = new PackageBinarySignals
                {
                    TotalBinaries = totalBinaries,
                    SymbolsAvailable = doc.GetInt32("symbolsAvailable"),
                    SourceLinkAvailable = doc.GetInt32("sourceLinkAvailable"),
                    EmbeddedPdbs = doc.GetInt32("embeddedPdbs"),
                    InPackagePdbs = doc.GetInt32("inPackagePdbs"),
                    SnupkgPdbs = doc.GetInt32("snupkgPdbs"),
                    MsdlPdbs = doc.GetInt32("msdlPdbs"),
                    OtherPdbs = doc.GetInt32("otherPdbs"),
                    EmbeddedSourceLinkPdbs = doc.GetInt32("embeddedSourceLinkPdbs"),
                    InPackageSourceLinkPdbs = doc.GetInt32("inPackageSourceLinkPdbs"),
                    SnupkgSourceLinkPdbs = doc.GetInt32("snupkgSourceLinkPdbs"),
                    MsdlSourceLinkPdbs = doc.GetInt32("msdlSourceLinkPdbs"),
                    OtherSourceLinkPdbs = doc.GetInt32("otherSourceLinkPdbs")
                };
            }

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

            var ridPackagesRaw = doc.GetArrayList("runtimeIdentifierPackages");
            if (ridPackagesRaw != null)
            {
                result.RuntimeIdentifierPackages = ridPackagesRaw.Select(ParseRidPackageReference).ToList();
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
    /// Uses plain field format (key: value) written as UTF-8 bytes directly.
    /// </summary>
    public static void Set(string packageName, string version, InspectionResult result)
    {
        string key = $"{packageName.ToLowerInvariant()}@{version}";

        var buf = new ArrayBufferWriter<byte>(1024);

        // Scalars first, ordered by access frequency and display priority
        WriteField(buf, "packageName"u8, result.PackageName);
        WriteField(buf, "manifestVersion"u8, result.ManifestVersion);
        WriteField(buf, "version"u8, result.Version);
        WriteField(buf, "description"u8, result.Description);
        WriteField(buf, "authors"u8, result.Authors);
        WriteField(buf, "license"u8, result.License);
        WriteField(buf, "licenseUrl"u8, result.LicenseUrl);
        WriteField(buf, "repository"u8, result.Repository);
        WriteField(buf, "repositoryType"u8, result.RepositoryType);
        WriteField(buf, "repositoryCommit"u8, result.RepositoryCommit);
        WriteField(buf, "assemblyCount"u8, result.AssemblyCount);
        WriteField(buf, "hasReadme"u8, result.HasReadme);
        WriteField(buf, "isToolPackage"u8, result.IsToolPackage);
        WriteField(buf, "isFrameworkDependent"u8, result.IsFrameworkDependent);
        WriteField(buf, "hasRidSpecificAssets"u8, result.HasRidSpecificAssets);
        WriteField(buf, "hasNativeDependencies"u8, result.HasNativeDependencies);
        WriteField(buf, "isRidSpecificPointerPackage"u8, result.IsRidSpecificPointerPackage);
        if (result.BinarySignals is { } binarySignals)
        {
            WriteField(buf, "totalBinaries"u8, binarySignals.TotalBinaries);
            WriteField(buf, "symbolsAvailable"u8, binarySignals.SymbolsAvailable);
            WriteField(buf, "sourceLinkAvailable"u8, binarySignals.SourceLinkAvailable);
            WriteField(buf, "embeddedPdbs"u8, binarySignals.EmbeddedPdbs);
            WriteField(buf, "inPackagePdbs"u8, binarySignals.InPackagePdbs);
            WriteField(buf, "snupkgPdbs"u8, binarySignals.SnupkgPdbs);
            WriteField(buf, "msdlPdbs"u8, binarySignals.MsdlPdbs);
            WriteField(buf, "otherPdbs"u8, binarySignals.OtherPdbs);
            WriteField(buf, "embeddedSourceLinkPdbs"u8, binarySignals.EmbeddedSourceLinkPdbs);
            WriteField(buf, "inPackageSourceLinkPdbs"u8, binarySignals.InPackageSourceLinkPdbs);
            WriteField(buf, "snupkgSourceLinkPdbs"u8, binarySignals.SnupkgSourceLinkPdbs);
            WriteField(buf, "msdlSourceLinkPdbs"u8, binarySignals.MsdlSourceLinkPdbs);
            WriteField(buf, "otherSourceLinkPdbs"u8, binarySignals.OtherSourceLinkPdbs);
        }
        WriteField(buf, "readmeFile"u8, result.ReadmeFile);
        WriteField(buf, "toolFormat"u8, result.ToolFormat);
        WriteField(buf, "runtimeTargetRid"u8, result.RuntimeTargetRid);
        if (result.BuiltDate.HasValue)
            WriteField(buf, "builtDate"u8, result.BuiltDate.Value.ToString("o"));

        // Arrays last
        WriteArray(buf, "packageTypes"u8, result.PackageTypes);
        WriteArray(buf, "contentDirs"u8, result.ContentDirectories);
        WriteArray(buf, "targetFrameworks"u8, result.TargetFrameworks);
        WriteArray(buf, "supportedRids"u8, result.SupportedRids);
        WriteArray(buf, "toolCommands"u8, result.ToolCommands);
        WriteArray(buf, "nativeFiles"u8, result.NativeFiles);
        WriteArray(buf, "libraryFiles"u8, result.LibraryFiles);
        if (result.RuntimeIdentifierPackages is { Count: > 0 })
            WriteArray(buf, "runtimeIdentifierPackages"u8,
                result.RuntimeIdentifierPackages.Select(FormatRidPackageReference).ToList());

        // Dependency groups stored as compact strings: "tfm|name@ver,name@ver"
        if (result.DependencyGroups is { Count: > 0 })
        {
            var groupStrings = result.DependencyGroups.Select(FormatDependencyGroup).ToList();
            WriteArray(buf, "dependencyGroups"u8, groupStrings);
        }

        CoreCache.SetBytes(Category, key, buf.WrittenSpan.ToArray(), extension: "md");
    }

    // ── Field serialization (plain format: "key: value", UTF-8 bytes) ──

    private static void WriteField(ArrayBufferWriter<byte> buf, ReadOnlySpan<byte> key, string? value)
    {
        if (value is null) return;
        Write(buf, key);
        Write(buf, ": "u8);
        Write(buf, value);
        Write(buf, "\n"u8);
    }

    private static void WriteField(ArrayBufferWriter<byte> buf, ReadOnlySpan<byte> key, bool value)
    {
        if (!value) return;
        Write(buf, key);
        Write(buf, ": true\n"u8);
    }

    private static void WriteField(ArrayBufferWriter<byte> buf, ReadOnlySpan<byte> key, int value)
    {
        if (value == 0) return;
        Write(buf, key);
        Write(buf, ": "u8);
        Write(buf, value.ToString());
        Write(buf, "\n"u8);
    }

    private static void WriteArray(ArrayBufferWriter<byte> buf, ReadOnlySpan<byte> key, List<string>? items)
    {
        if (items is not { Count: > 0 }) return;
        Write(buf, "\n"u8);
        Write(buf, key);
        Write(buf, ":\n"u8);
        foreach (var item in items)
        {
            Write(buf, "- "u8);
            Write(buf, item);
            Write(buf, "\n"u8);
        }
    }

    private static void Write(ArrayBufferWriter<byte> buf, ReadOnlySpan<byte> utf8)
    {
        utf8.CopyTo(buf.GetSpan(utf8.Length));
        buf.Advance(utf8.Length);
    }

    private static void Write(ArrayBufferWriter<byte> buf, string text)
    {
        var maxBytes = Encoding.UTF8.GetMaxByteCount(text.Length);
        var span = buf.GetSpan(maxBytes);
        int written = Encoding.UTF8.GetBytes(text, span);
        buf.Advance(written);
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

    private static string FormatRidPackageReference(RidPackageReference reference)
        => $"{reference.RuntimeIdentifier}|{reference.PackageId}|{reference.AvailableDisplay}";

    private static RidPackageReference ParseRidPackageReference(string raw)
    {
        var parts = raw.Split('|', 3);
        return new RidPackageReference
        {
            RuntimeIdentifier = parts.ElementAtOrDefault(0) ?? "",
            PackageId = parts.ElementAtOrDefault(1) ?? "",
            Exists = parts.ElementAtOrDefault(2) switch
            {
                "yes" => true,
                "no" => false,
                _ => null
            }
        };
    }
}
