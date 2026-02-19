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
    private const string Category = "metadata";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    /// <summary>
    /// Tries to load cached metadata. Returns null on cache miss or expiry.
    /// </summary>
    public static PackageMetadata? TryGet(string cacheKey)
    {
        var bytes = CoreCache.TryGetBytes(Category, cacheKey, Ttl, extension: "md");
        if (bytes is null) return null;

        try
        {
            using var doc = FieldDocument.Parse(bytes);
            var metadata = new PackageMetadata
            {
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
                metadata.Owners = owners;

            // Deprecation: flattened fields
            var reasons = doc.GetString("deprecationReasons");
            var message = doc.GetString("deprecationMessage");
            var altPkg = doc.GetString("deprecationAlternate");
            if (reasons is not null || message is not null)
            {
                metadata.Deprecation = new PackageDeprecation
                {
                    Reasons = reasons?.Split(", ").ToList(),
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

            return metadata;
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
    {
        try
        {
            var buf = new ArrayBufferWriter<byte>(512);

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

            // Deprecation: flattened
            if (metadata.Deprecation is { } dep)
            {
                if (dep.Reasons is { Count: > 0 })
                    WriteField(buf, "deprecationReasons"u8, string.Join(", ", dep.Reasons));
                if (!string.IsNullOrEmpty(dep.Message))
                    WriteField(buf, "deprecationMessage"u8, dep.Message);
                if (!string.IsNullOrEmpty(dep.AlternatePackageId))
                    WriteField(buf, "deprecationAlternate"u8, dep.AlternatePackageId);
            }

            // Arrays
            WriteArray(buf, "owners"u8, metadata.Owners);

            // Vulnerabilities: compact pipe-delimited
            if (metadata.Vulnerabilities is { Count: > 0 })
            {
                var items = metadata.Vulnerabilities.Select(FormatVulnerability).ToList();
                WriteArray(buf, "vulnerabilities"u8, items);
            }

            CoreCache.SetBytes(Category, cacheKey, buf.WrittenSpan.ToArray(), extension: "md");
        }
        catch
        {
            // Best-effort caching
        }
    }

    // ── Vulnerability compact format: "Severity|CveId|GhsaId|Summary" ──

    private static string FormatVulnerability(PackageVulnerability v)
    {
        return $"{v.Severity}|{v.CveId ?? ""}|{v.GhsaId ?? ""}|{v.Summary ?? ""}";
    }

    private static PackageVulnerability ParseVulnerability(string raw)
    {
        var parts = raw.Split('|', 4);
        return new PackageVulnerability
        {
            Severity = parts.Length > 0 ? parts[0] : "",
            CveId = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null,
            GhsaId = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null,
            Summary = parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null,
        };
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
