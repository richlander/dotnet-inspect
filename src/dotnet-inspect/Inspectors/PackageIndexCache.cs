using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using DotnetInspector.Core;
using DotnetInspector.Models;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using InertText;
using MarkdownTable.Formatting;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Caches the filesystem-derived fields of InspectionResult as markdown fields.
/// On cache hit, skips all directory scanning, nuspec parsing, and deps.json parsing.
/// Metadata (downloads, vulnerabilities) is cached separately by PackageMetadataService.
/// </summary>
internal static class PackageIndexCache
{
    internal const string Category = "pkg-index-v12";
    private static ReadOnlySpan<byte> DescriptionLengthPrefix => "description-bytes: "u8;
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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
            var (description, fieldsOffset) = ReadDescriptionEnvelope(bytes);
            using var doc = FieldDocument.Parse(bytes[fieldsOffset..]);
            var result = new InspectionResult
            {
                PackageName = doc.GetString("packageName") ?? packageName,
                ManifestVersion = doc.GetString("manifestVersion"),
                Version = doc.GetString("version") ?? version,
                Description = description,
                Authors = doc.GetString("authors"),
                License = doc.GetString("license"),
                LicenseUrl = doc.GetString("licenseUrl"),
                Repository = doc.GetString("repository"),
                RepositoryType = doc.GetString("repositoryType"),
                RepositoryCommit = doc.GetString("repositoryCommit"),
                ReadmeFile = doc.GetString("readmeFile"),
                PackageReadmeFile = doc.GetString("packageReadmeFile"),
                HasReadme = doc.GetBool("hasReadme"),
                HasAgentDocumentation = doc.GetBool("hasAgentDocumentation"),
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

            // Dependency groups are stored as JSON objects within the field array.
            var depGroupsRaw = doc.GetArrayList("dependencyGroups");
            if (depGroupsRaw != null)
            {
                result.DependencyGroups = depGroupsRaw.Select(DeserializeDependencyGroup).ToList();
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
    /// Stores the encoded description in a length-delimited UTF-8 envelope followed by plain
    /// fields (<c>key: value</c>).
    /// </summary>
    public static void Set(string packageName, string version, InspectionResult result)
    {
        string key = $"{packageName.ToLowerInvariant()}@{version}";

        var buf = new ArrayBufferWriter<byte>(1024);
        WriteDescriptionEnvelope(buf, result.Description);

        // Scalars first, ordered by access frequency and display priority
        WriteField(buf, "packageName"u8, result.PackageName);
        WriteField(buf, "manifestVersion"u8, result.ManifestVersion);
        WriteField(buf, "version"u8, result.Version);
        WriteField(buf, "authors"u8, result.Authors);
        WriteField(buf, "license"u8, result.License);
        WriteField(buf, "licenseUrl"u8, result.LicenseUrl);
        WriteField(buf, "repository"u8, result.Repository);
        WriteField(buf, "repositoryType"u8, result.RepositoryType);
        WriteField(buf, "repositoryCommit"u8, result.RepositoryCommit);
        WriteField(buf, "assemblyCount"u8, result.AssemblyCount);
        WriteField(buf, "hasReadme"u8, result.HasReadme);
        WriteField(buf, "hasAgentDocumentation"u8, result.HasAgentDocumentation);
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
        WriteField(buf, "packageReadmeFile"u8, result.PackageReadmeFile);
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

        // Each dependency group is one structured JSON value in the field array.
        if (result.DependencyGroups is { Count: > 0 })
        {
            var groupStrings = result.DependencyGroups.Select(SerializeDependencyGroup).ToList();
            WriteArray(buf, "dependencyGroups"u8, groupStrings);
        }

        CoreCache.SetBytes(Category, key, buf.WrittenSpan.ToArray(), extension: "md");
    }

    // ── Description envelope + field serialization ──

    private static void WriteField(ArrayBufferWriter<byte> buf, ReadOnlySpan<byte> key, string? value)
    {
        if (value is null) return;
        Write(buf, key);
        Write(buf, ": "u8);
        Write(buf, value);
        Write(buf, "\n"u8);
    }

    private static void WriteDescriptionEnvelope(
        ArrayBufferWriter<byte> buf,
        InertString? description)
    {
        Write(buf, DescriptionLengthPrefix);

        if (description is not { } inert)
        {
            WriteInteger(buf, -1);
            Write(buf, "\n\n"u8);
            return;
        }

        if (inert.IsTruncated)
        {
            throw new InvalidOperationException(
                "A truncated package description cannot be persisted without its provenance.");
        }

        string encoded = inert.ToString();
        WriteInteger(buf, Encoding.UTF8.GetByteCount(encoded));
        Write(buf, "\n"u8);
        Write(buf, encoded);
        Write(buf, "\n"u8);
    }

    private static (InertString? Description, int FieldsOffset) ReadDescriptionEnvelope(
        byte[] bytes)
    {
        ReadOnlySpan<byte> content = bytes;
        int headerEnd = content.IndexOf((byte)'\n');
        if (headerEnd < 0)
            throw new InvalidDataException("Cached package description header is missing.");

        ReadOnlySpan<byte> header = content[..headerEnd];
        if (!header.StartsWith(DescriptionLengthPrefix))
            throw new InvalidDataException("Cached package description header is invalid.");

        ReadOnlySpan<byte> lengthText = header[DescriptionLengthPrefix.Length..];
        if (!Utf8Parser.TryParse(
                lengthText,
                out int descriptionLength,
                out int consumed)
            || consumed != lengthText.Length
            || descriptionLength < -1)
        {
            throw new InvalidDataException("Cached package description length is invalid.");
        }

        int descriptionStart = headerEnd + 1;
        int storedLength = Math.Max(descriptionLength, 0);
        long descriptionEndLong = (long)descriptionStart + storedLength;
        if (descriptionEndLong >= bytes.Length)
            throw new InvalidDataException("Cached package description is truncated.");

        int descriptionEnd = (int)descriptionEndLong;
        if (bytes[descriptionEnd] != (byte)'\n')
            throw new InvalidDataException("Cached package description boundary is invalid.");

        InertString? description = null;
        if (descriptionLength >= 0)
        {
            string encoded;
            try
            {
                encoded = StrictUtf8.GetString(content.Slice(
                    descriptionStart,
                    descriptionLength));
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException(
                    "Cached package description is not valid UTF-8.",
                    ex);
            }

            try
            {
                description = InertString.FromEncoded(TextPolicy.Prose, encoded);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException(
                    "Cached package description is not valid encoded text.",
                    ex);
            }
        }

        return (description, descriptionEnd + 1);
    }

    private static void WriteInteger(ArrayBufferWriter<byte> buf, int value)
    {
        Span<byte> destination = buf.GetSpan(11);
        if (!Utf8Formatter.TryFormat(value, destination, out int written))
            throw new InvalidOperationException("Could not format a cache field length.");

        buf.Advance(written);
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

    internal static string SerializeDependencyGroup(DependencyGroup group)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("targetFramework", group.TargetFramework);
        writer.WritePropertyName("dependencies");
        writer.WriteStartArray();
        foreach (var dependency in group.Dependencies ?? [])
        {
            writer.WriteStartObject();
            writer.WriteString("id", dependency.Id);
            writer.WriteString("version", dependency.Version);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static DependencyGroup DeserializeDependencyGroup(string raw)
    {
        using var document = HardenedJson.Parse(raw);
        var root = document.RootElement;
        var dependencies = new List<PackageDependency>();
        foreach (var dependency in root.GetProperty("dependencies").EnumerateArray())
        {
            dependencies.Add(new PackageDependency
            {
                Id = dependency.GetProperty("id").GetString() ?? "",
                Version = dependency.GetProperty("version").GetString() ?? ""
            });
        }

        return new DependencyGroup
        {
            TargetFramework = root.GetProperty("targetFramework").GetString() ?? "",
            Dependencies = dependencies
        };
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
