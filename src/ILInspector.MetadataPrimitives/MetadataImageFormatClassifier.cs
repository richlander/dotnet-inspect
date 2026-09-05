using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>Why an assembly metadata root could not be classified.</summary>
public enum MetadataRootMalformedReason
{
    /// <summary>The metadata directory could not be mapped from the PE image.</summary>
    UnmappableMetadataDirectory,

    /// <summary>The selected metadata extent is not fully available in its containing block.</summary>
    UnmappableMetadataExtent,

    /// <summary>The metadata block does not contain the complete fixed root prefix.</summary>
    TruncatedFixedPrefix,

    /// <summary>The metadata root does not begin with the ECMA-335 signature.</summary>
    InvalidSignature,

    /// <summary>The padded version-field length is negative, unaligned, or exceeds 256 bytes.</summary>
    InvalidVersionLength,

    /// <summary>The declared version field extends beyond the metadata block.</summary>
    TruncatedVersionField,

    /// <summary>The declared version field contains no null terminator.</summary>
    MissingVersionTerminator,
}

/// <summary>
/// A bounded mechanical classification of one owner-bound assembly metadata root.
/// The primitive-local portion of MDP017 is enforced by
/// <c>MetadataImageFormatClassifierTests</c> and
/// <c>LayeringTests.MetadataPrimitives_MetadataRootClassifierIsIsolated</c>;
/// product adoption remains a separate gate.
/// </summary>
public abstract record MetadataImageFormatResult
{
    private protected MetadataImageFormatResult()
    {
    }

    /// <summary>The image contains ordinary ECMA-335 assembly metadata.</summary>
    public sealed record SupportedEcma335 : MetadataImageFormatResult
    {
        internal SupportedEcma335()
        {
        }
    }

    /// <summary>The image contains unsupported Windows Metadata.</summary>
    public sealed record UnsupportedWindowsMetadata : MetadataImageFormatResult
    {
        internal UnsupportedWindowsMetadata()
        {
        }
    }

    /// <summary>The PE image has no managed metadata directory.</summary>
    public sealed record NoMetadata : MetadataImageFormatResult
    {
        internal NoMetadata()
        {
        }
    }

    /// <summary>The metadata root is malformed before SRM reader construction.</summary>
    public sealed record MalformedRoot : MetadataImageFormatResult
    {
        internal MalformedRoot(MetadataRootMalformedReason reason)
        {
            Reason = reason;
        }

        public MetadataRootMalformedReason Reason { get; }
    }
}

/// <summary>
/// Classifies the bounded ECMA-335 metadata-root prefix without constructing a
/// <see cref="MetadataReader"/> or inspecting stream, heap, table, or row data.
/// </summary>
public static class MetadataImageFormatClassifier
{
    internal const int FixedPrefixLength = 16;
    internal const int MaximumVersionStringLength = 255;
    internal const int MaximumPaddedVersionLength = 256;

    const uint MetadataRootSignature = 0x424A5342;

    static readonly MetadataImageFormatResult Supported =
        new MetadataImageFormatResult.SupportedEcma335();
    static readonly MetadataImageFormatResult Unsupported =
        new MetadataImageFormatResult.UnsupportedWindowsMetadata();
    static readonly MetadataImageFormatResult Missing =
        new MetadataImageFormatResult.NoMetadata();

    /// <summary>
    /// Classifies the metadata root owned by <paramref name="peReader"/>.
    /// Acquisition failures other than an unmappable PE metadata directory are
    /// preserved for the acquisition owner.
    /// </summary>
    public static MetadataImageFormatResult Classify(PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(peReader);

        bool hasMetadata;
        try
        {
            hasMetadata = peReader.HasMetadata;
        }
        catch (BadImageFormatException)
        {
            return Malformed(
                MetadataRootMalformedReason.UnmappableMetadataDirectory);
        }

        if (!hasMetadata)
            return Missing;

        PEMemoryBlock metadata;
        try
        {
            metadata = peReader.GetMetadata();
        }
        catch (BadImageFormatException)
        {
            return Malformed(
                MetadataRootMalformedReason.UnmappableMetadataDirectory);
        }

        return Classify(metadata, metadata.Length);
    }

    /// <summary>
    /// Classifies one exact metadata-root extent within an already acquired PE
    /// memory block. Bytes after <paramref name="length"/> are not examined.
    /// </summary>
    public static MetadataImageFormatResult Classify(
        PEMemoryBlock metadata,
        int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (length > metadata.Length)
            return Malformed(MetadataRootMalformedReason.UnmappableMetadataExtent);

        if (length < FixedPrefixLength)
        {
            return Malformed(
                MetadataRootMalformedReason.TruncatedFixedPrefix);
        }

        int boundedLength = Math.Min(
            length,
            FixedPrefixLength + MaximumPaddedVersionLength);
        BlobReader reader = metadata.GetReader(0, boundedLength);

        if (reader.ReadUInt32() != MetadataRootSignature)
            return Malformed(MetadataRootMalformedReason.InvalidSignature);

        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt32();

        int versionLength = reader.ReadInt32();
        if (versionLength < 0
            || versionLength > MaximumPaddedVersionLength
            || (versionLength & 3) != 0)
        {
            return Malformed(
                MetadataRootMalformedReason.InvalidVersionLength);
        }

        if (reader.RemainingBytes < versionLength)
        {
            return Malformed(
                MetadataRootMalformedReason.TruncatedVersionField);
        }

        bool containsWindowsRuntime = false;
        bool foundTerminator = false;
        int markerOffset = 0;
        ReadOnlySpan<byte> marker = "WindowsRuntime"u8;
        int versionStringLength = Math.Min(
            versionLength,
            MaximumVersionStringLength);
        for (int i = 0; i < versionStringLength; i++)
        {
            byte value = reader.ReadByte();
            if (value == 0)
            {
                foundTerminator = true;
                break;
            }

            if (value == marker[markerOffset])
            {
                markerOffset++;
                if (markerOffset == marker.Length)
                {
                    containsWindowsRuntime = true;
                    markerOffset = 0;
                }
            }
            else
            {
                markerOffset = value == marker[0] ? 1 : 0;
            }
        }

        if (!foundTerminator)
        {
            return Malformed(
                MetadataRootMalformedReason.MissingVersionTerminator);
        }

        return containsWindowsRuntime ? Unsupported : Supported;
    }

    static MetadataImageFormatResult Malformed(
        MetadataRootMalformedReason reason)
        => new MetadataImageFormatResult.MalformedRoot(reason);
}
