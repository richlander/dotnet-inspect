using System.Buffers;
using System.Text;
using DotnetInspector.Core;
using MarkdownTable.Formatting;

namespace DotnetInspector.Services;

/// <summary>
/// Reads and writes <see cref="PackageMetadata"/> as markdown field documents.
/// Replaces the previous JSON-based metadata cache with zero-string byte-level parsing.
/// </summary>
internal static class MetadataFieldCache
{
    internal readonly record struct Entry(
        PackageMetadata Metadata,
        bool IsAbsent);

    private const string Category = "metadata";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
    private static ReadOnlySpan<byte> PresentPrefix => "metadata-v6:present\n"u8;
    private static ReadOnlySpan<byte> AbsentEntry => "metadata-v6:absent\n"u8;

    /// <summary>
    /// Tries to load cached metadata. Returns null on cache miss or expiry.
    /// </summary>
    public static PackageMetadata? TryGet(string cacheKey)
    {
        Entry? entry = TryGetEntry(cacheKey);
        return entry is { IsAbsent: false }
            ? entry.Value.Metadata
            : null;
    }

    /// <summary>
    /// Tries to load cached metadata or an authoritative source-absence marker.
    /// </summary>
    public static Entry? TryGetEntry(string cacheKey)
    {
        var bytes = CoreCache.TryGetBytes(Category, cacheKey, Ttl, extension: "md");
        if (bytes is null) return null;

        try
        {
            if (bytes.AsSpan().SequenceEqual(AbsentEntry))
            {
                return new Entry(new PackageMetadata(), IsAbsent: true);
            }
            if (!bytes.AsSpan().StartsWith(PresentPrefix))
            {
                return null;
            }

            using var doc = FieldDocument.Parse(bytes[PresentPrefix.Length..]);
            var metadata = new PackageMetadata
            {
                DeprecationMetadataSupported =
                    doc.GetBool("deprecationMetadataSupported"),
                DeprecationMetadataAvailable =
                    doc.GetBool("deprecationMetadataAvailable"),
                IsVerified = doc.GetBool("isVerified") ? true : null,
                VersionCount = doc.GetInt32("versionCount") is > 0 and var vc ? vc : null,
            };

            if (doc.GetString("published") is string pub
                && DateTimeOffset.TryParse(pub, out var published))
                metadata.Published = published;

            if (doc.GetString("totalDownloads") is string td
                && long.TryParse(td, out var totalDl))
                metadata.TotalDownloads = totalDl;

            if (doc.GetString("versionDownloads") is string vd
                && long.TryParse(vd, out var verDl))
                metadata.VersionDownloads = verDl;

            if (doc.GetString("packageSize") is string ps
                && long.TryParse(ps, out var size))
                metadata.PackageSize = size;

            var owners = doc.GetArrayList("owners");
            if (owners is { Count: > 0 })
                metadata.Owners = owners.Select(DecodeText).ToList();

            // Deprecation: flattened fields
            var reasons = doc.GetArrayList("deprecationReasons");
            var message = DecodeOptionalText(
                doc.GetString("deprecationMessage"));
            var altPkg = DecodeOptionalText(
                doc.GetString("deprecationAlternate"));
            if (reasons is { Count: > 0 }
                || message is not null
                || altPkg is not null)
            {
                metadata.Deprecation = new PackageDeprecation
                {
                    Reasons = reasons?.Select(DecodeText).ToList(),
                    Message = message,
                    AlternatePackageId = altPkg,
                };
            }

            // Vulnerabilities: pipe-delimited compact strings
            var vulnItems = doc.GetArrayList("vulnerabilities");
            if (vulnItems is { Count: > 0 })
            {
                metadata.Vulnerabilities = vulnItems.Select(ParseVulnerability).ToList();
            }
            else if (doc.GetBool("vulnerabilitiesChecked"))
            {
                metadata.Vulnerabilities = [];
            }

            return new Entry(metadata, IsAbsent: false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Caches metadata as a markdown field document.
    /// </summary>
    public static void Set(string cacheKey, PackageMetadata metadata)
        => SetEntry(cacheKey, metadata, isAbsent: false);

    /// <summary>
    /// Caches that one producer authoritatively reported the package version absent.
    /// </summary>
    public static void SetAbsent(string cacheKey)
        => SetEntry(cacheKey, new PackageMetadata(), isAbsent: true);

    private static void SetEntry(
        string cacheKey,
        PackageMetadata metadata,
        bool isAbsent)
    {
        try
        {
            var buf = new ArrayBufferWriter<byte>(512);

            if (isAbsent)
            {
                Write(buf, AbsentEntry);
                CoreCache.SetBytes(
                    Category,
                    cacheKey,
                    buf.WrittenSpan.ToArray(),
                    extension: "md");
                return;
            }

            Write(buf, PresentPrefix);

            // Keep even an otherwise empty metadata result parseable. A feed may advertise a
            // package without any optional aggregate metadata fields.
            WriteField(buf, "formatVersion"u8, "6");

            // Scalars
            if (metadata.Published.HasValue)
                WriteField(buf, "published"u8, metadata.Published.Value.ToString("o"));
            if (metadata.TotalDownloads.HasValue)
                WriteField(buf, "totalDownloads"u8, metadata.TotalDownloads.Value.ToString());
            if (metadata.VersionDownloads.HasValue)
                WriteField(buf, "versionDownloads"u8, metadata.VersionDownloads.Value.ToString());
            if (metadata.VersionCount.HasValue)
                WriteField(buf, "versionCount"u8, metadata.VersionCount.Value.ToString());
            if (metadata.PackageSize.HasValue)
                WriteField(buf, "packageSize"u8, metadata.PackageSize.Value.ToString());
            if (metadata.IsVerified == true)
                WriteField(buf, "isVerified"u8, "true");
            if (metadata.DeprecationMetadataAvailable)
            {
                WriteField(
                    buf,
                    "deprecationMetadataAvailable"u8,
                    "true");
            }
            if (metadata.DeprecationMetadataSupported)
            {
                WriteField(
                    buf,
                    "deprecationMetadataSupported"u8,
                    "true");
            }

            // Deprecation: flattened
            if (metadata.Deprecation is { } dep)
            {
                if (dep.Reasons is { Count: > 0 })
                    WriteArray(
                        buf,
                        "deprecationReasons"u8,
                        dep.Reasons.Select(EncodeText).ToList());
                if (!string.IsNullOrEmpty(dep.Message))
                    WriteField(
                        buf,
                        "deprecationMessage"u8,
                        EncodeText(dep.Message));
                if (!string.IsNullOrEmpty(dep.AlternatePackageId))
                    WriteField(
                        buf,
                        "deprecationAlternate"u8,
                        EncodeText(dep.AlternatePackageId));
            }

            // Arrays
            WriteArray(
                buf,
                "owners"u8,
                metadata.Owners?.Select(EncodeText).ToList());

            // Vulnerabilities: compact pipe-delimited
            if (metadata.Vulnerabilities is { } vulnerabilities)
            {
                WriteField(buf, "vulnerabilitiesChecked"u8, "true");
                if (vulnerabilities.Count > 0)
                {
                    var items = vulnerabilities.Select(FormatVulnerability).ToList();
                    WriteArray(buf, "vulnerabilities"u8, items);
                }
            }

            CoreCache.SetBytes(Category, cacheKey, buf.WrittenSpan.ToArray(), extension: "md");
        }
        catch
        {
            // Best-effort caching
        }
    }

    // ── Vulnerability compact format: "Severity|CveId|GhsaId|Summary|AdvisoryUrl" ──

    private static string FormatVulnerability(PackageVulnerability v)
    {
        return string.Join(
            '|',
            EncodeText(v.Severity),
            EncodeText(v.CveId ?? ""),
            EncodeText(v.GhsaId ?? ""),
            EncodeText(v.Summary ?? ""),
            EncodeText(v.AdvisoryUrl ?? ""));
    }

    private static PackageVulnerability ParseVulnerability(string raw)
    {
        var parts = raw.Split('|', 5);
        return new PackageVulnerability
        {
            Severity = parts.Length > 0 ? DecodeText(parts[0]) : "",
            CveId = parts.Length > 1
                ? DecodeOptionalText(parts[1])
                : null,
            GhsaId = parts.Length > 2
                ? DecodeOptionalText(parts[2])
                : null,
            Summary = parts.Length > 3
                ? DecodeOptionalText(parts[3])
                : null,
            AdvisoryUrl = parts.Length > 4
                ? DecodeOptionalText(parts[4])
                : null,
        };
    }

    private static string EncodeText(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string DecodeText(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static string? DecodeOptionalText(string? value)
    {
        if (value is null)
            return null;

        string decoded = DecodeText(value);
        return decoded.Length > 0 ? decoded : null;
    }

    // ── Field serialization (UTF-8 bytes) ──

    private static void WriteField(ArrayBufferWriter<byte> buf, ReadOnlySpan<byte> key, string? value)
    {
        if (value is null) return;
        Write(buf, key);
        Write(buf, ": "u8);
        Write(buf, value);
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
}
