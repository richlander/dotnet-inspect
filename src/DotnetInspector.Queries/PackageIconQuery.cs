using System.Buffers.Binary;
using System.Collections.Immutable;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>An embedded package icon admitted for presentation.</summary>
public sealed record PackageIcon(string MediaType, ImmutableArray<byte> Bytes);

/// <summary>The stable reason a declared package icon was not admitted.</summary>
public enum PackageIconUnavailableReason
{
    InvalidManifest,
    InvalidPath,
    MissingEntry,
    ConfiguredLimitExceeded,
    UnsupportedFormat,
    InvalidImage,
}

/// <summary>The typed outcome of projecting one package's embedded icon.</summary>
public abstract record PackageIconResult
{
    private PackageIconResult()
    {
    }

    /// <summary>The manifest does not declare an embedded icon.</summary>
    public sealed record Missing : PackageIconResult;

    /// <summary>The manifest-declared icon was admitted.</summary>
    public sealed record Available(PackageIcon Value) : PackageIconResult;

    /// <summary>The manifest declares an icon that cannot be presented.</summary>
    public sealed record Unavailable(
        PackageIconUnavailableReason Reason) : PackageIconResult;
}

/// <summary>
/// Projects a bounded JPEG or PNG package icon from exact host-neutral package content.
/// </summary>
/// <remarks>
/// NuGet owns the 1 MB encoded-byte limit and JPEG/PNG format allow list. This query adds a
/// Browser-safe decoded-dimension ceiling because the icon is rendered at 20 CSS pixels and
/// untrusted image headers must not authorize unbounded decoder work. The deprecated manifest
/// icon URL is metadata only and never grants this query network authority.
/// </remarks>
public static class PackageIconQuery
{
    public const int MaxIconBytes = 1024 * 1024;
    public const int MaxDimension = 2048;
    public const long MaxPixels = (long)MaxDimension * MaxDimension;

    static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static InspectionQuery<PackageIconResult> Definition { get; } =
        new("Package icon", InspectionCost.NetworkFree);

    public static PackageIconResult Execute(
        IPackageContent content,
        string packageId,
        string packageVersion)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        try
        {
            string? manifestPath;
            try
            {
                manifestPath =
                    PackageManifestContent.FindRootManifest(content);
            }
            catch (InvalidDataException)
            {
                return new PackageIconResult.Unavailable(
                    PackageIconUnavailableReason.InvalidManifest);
            }
            if (manifestPath is null)
                return new PackageIconResult.Missing();

            if (!TryReadEntry(
                    content,
                    manifestPath,
                    PackageManifestFactsQuery.MaxManifestBytes,
                    out byte[] manifestBytes))
            {
                return new PackageIconResult.Unavailable(
                    PackageIconUnavailableReason.InvalidManifest);
            }

            PackageManifestFactsResult facts =
                PackageManifestFactsQuery.Execute(
                    manifestBytes,
                    PackageSourceCoordinate.Create(
                        packageId,
                        packageVersion));
            if (facts is not PackageManifestFactsResult.Available available)
            {
                return new PackageIconResult.Unavailable(
                    PackageIconUnavailableReason.InvalidManifest);
            }

            string? declaredPath = available.Value.IconFile;
            if (string.IsNullOrEmpty(declaredPath))
                return new PackageIconResult.Missing();
            if (!TryNormalizePath(declaredPath, out string iconPath))
            {
                return new PackageIconResult.Unavailable(
                    PackageIconUnavailableReason.InvalidPath);
            }

            if (!TryReadEntry(
                    content,
                    iconPath,
                    MaxIconBytes,
                    out byte[] iconBytes))
            {
                return new PackageIconResult.Unavailable(
                    PackageIconUnavailableReason.MissingEntry);
            }

            return TryReadImage(iconBytes, out string mediaType)
                ? new PackageIconResult.Available(
                    new PackageIcon(mediaType, [.. iconBytes]))
                : new PackageIconResult.Unavailable(
                    SniffSupportedFormat(iconBytes)
                        ? PackageIconUnavailableReason.InvalidImage
                        : PackageIconUnavailableReason.UnsupportedFormat);
        }
        catch (InvalidDataException)
        {
            return new PackageIconResult.Unavailable(
                PackageIconUnavailableReason.ConfiguredLimitExceeded);
        }
        catch (IOException)
        {
            return new PackageIconResult.Unavailable(
                PackageIconUnavailableReason.MissingEntry);
        }
        catch (UnauthorizedAccessException)
        {
            return new PackageIconResult.Unavailable(
                PackageIconUnavailableReason.MissingEntry);
        }
    }

    static bool TryNormalizePath(
        string declaredPath,
        out string normalizedPath)
    {
        normalizedPath = declaredPath.Replace('\\', '/');
        string[] segments = normalizedPath.Split('/');
        if (segments.Length == 0
            || segments.Any(segment =>
                !PackageEntryPath.IsSafeSegment(segment)))
        {
            normalizedPath = "";
            return false;
        }

        return true;
    }

    static bool TryReadEntry(
        IPackageContent content,
        string path,
        long maxBytes,
        out byte[] bytes)
    {
        if (!content.TryOpenEntry(path, maxBytes, out Stream? stream))
        {
            bytes = [];
            return false;
        }

        using (stream)
        using (var output = new MemoryStream())
        {
            byte[] buffer = new byte[81920];
            while (true)
            {
                int read = stream.Read(buffer);
                if (read == 0)
                    break;
                if (output.Length > maxBytes - read)
                {
                    throw new InvalidDataException(
                        "Package entry exceeds the configured byte limit.");
                }
                output.Write(buffer, 0, read);
            }
            bytes = output.ToArray();
            return true;
        }
    }

    static bool TryReadImage(byte[] bytes, out string mediaType)
    {
        if (TryReadPngDimensions(bytes, out int width, out int height))
        {
            mediaType = "image/png";
            return DimensionsAreBounded(width, height);
        }
        if (TryReadJpegDimensions(bytes, out width, out height))
        {
            mediaType = "image/jpeg";
            return DimensionsAreBounded(width, height);
        }

        mediaType = "";
        return false;
    }

    static bool SniffSupportedFormat(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(PngSignature)
        || bytes is [0xff, 0xd8, ..];

    static bool TryReadPngDimensions(
        ReadOnlySpan<byte> bytes,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 24
            || !bytes.StartsWith(PngSignature)
            || BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) != 13
            || !bytes[12..16].SequenceEqual("IHDR"u8))
        {
            return false;
        }

        uint encodedWidth =
            BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
        uint encodedHeight =
            BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
        if (encodedWidth > int.MaxValue || encodedHeight > int.MaxValue)
            return false;
        width = (int)encodedWidth;
        height = (int)encodedHeight;
        return width > 0 && height > 0;
    }

    static bool TryReadJpegDimensions(
        ReadOnlySpan<byte> bytes,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;
        if (bytes is not [0xff, 0xd8, ..])
            return false;

        int offset = 2;
        while (offset < bytes.Length)
        {
            while (offset < bytes.Length && bytes[offset] == 0xff)
                offset++;
            if (offset >= bytes.Length)
                return false;

            byte marker = bytes[offset++];
            if (marker is 0x00 or 0x01
                || marker is >= 0xd0 and <= 0xd9)
            {
                continue;
            }
            if (offset > bytes.Length - 2)
                return false;

            int segmentLength =
                BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
            if (segmentLength < 2 || segmentLength > bytes.Length - offset)
                return false;
            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7)
                    return false;
                height =
                    BinaryPrimitives.ReadUInt16BigEndian(
                        bytes[(offset + 3)..]);
                width =
                    BinaryPrimitives.ReadUInt16BigEndian(
                        bytes[(offset + 5)..]);
                return width > 0 && height > 0;
            }

            offset += segmentLength;
        }

        return false;
    }

    static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xc0 and <= 0xcf
        && marker is not 0xc4 and not 0xc8 and not 0xcc;

    static bool DimensionsAreBounded(int width, int height) =>
        width <= MaxDimension
        && height <= MaxDimension
        && (long)width * height <= MaxPixels;
}
