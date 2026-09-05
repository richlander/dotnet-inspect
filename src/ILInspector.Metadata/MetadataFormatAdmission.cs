using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// The image contains Windows Metadata, which is outside the supported
/// ECMA-335 assembly-metadata format.
/// </summary>
public sealed class UnsupportedMetadataFormatException(
    MetadataRootSource? source = null)
    : NotSupportedException(
        source is null
            ? "Windows Metadata is not a supported metadata format."
            : $"{source} metadata is Windows Metadata, which is not a supported metadata format.")
{
    public MetadataRootSource? RootSource { get; } = source;
}

/// <summary>
/// The image has a malformed ECMA-335 metadata root.
/// </summary>
public sealed class MalformedMetadataRootException(
    MetadataRootMalformedReason reason,
    MetadataRootSource? source = null)
    : BadImageFormatException(
        source is null
            ? $"The assembly metadata root is malformed ({reason})."
            : $"The {source} metadata root is malformed ({reason}).")
{
    public MetadataRootMalformedReason Reason { get; } = reason;
    public MetadataRootSource? RootSource { get; } = source;
}

/// <summary>
/// Maps the MetadataPrimitives-owned root classification to Metadata's direct
/// API contract before any SRM metadata reader is constructed.
/// Gate: <c>MetadataImageFormatClassifierTests</c> exercises every result arm.
/// That no other entry point in this assembly constructs a reader outside this
/// type is <c>unverified</c> — no gate enforces that closure.
/// </summary>
public static class MetadataFormatAdmission
{
    /// <summary>
    /// Returns <see langword="true"/> for supported ECMA-335 metadata and
    /// <see langword="false"/> when the image has no metadata. Unsupported or
    /// malformed metadata throws its bounded typed admission exception.
    /// </summary>
    public static bool AdmitImage(
        PEReader peReader,
        MetadataRootSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(peReader);

        return MetadataImageFormatClassifier.Classify(peReader) switch
        {
            MetadataImageFormatResult.SupportedEcma335 => true,
            MetadataImageFormatResult.NoMetadata => false,
            MetadataImageFormatResult.UnsupportedWindowsMetadata =>
                throw new UnsupportedMetadataFormatException(source),
            MetadataImageFormatResult.MalformedRoot malformed =>
                throw new MalformedMetadataRootException(malformed.Reason, source),
            _ => throw new InvalidOperationException(
                "Unknown metadata image format result."),
        };
    }

    public static MetadataReader GetMetadataReader(
        PEReader peReader,
        MetadataRootSource? source = null)
    {
        EnsureMetadata(peReader, source);
        return peReader.GetMetadataReader();
    }

    public static MetadataReader GetMetadataReader(
        PEReader peReader,
        MetadataReaderOptions options,
        MetadataRootSource? source = null)
    {
        EnsureMetadata(peReader, source);
        return peReader.GetMetadataReader(options);
    }

    internal static void AdmitRoot(
        PEMemoryBlock metadata,
        int length,
        MetadataRootSource source)
    {
        switch (MetadataImageFormatClassifier.Classify(metadata, length))
        {
            case MetadataImageFormatResult.SupportedEcma335:
                return;
            case MetadataImageFormatResult.UnsupportedWindowsMetadata:
                throw new UnsupportedMetadataFormatException(source);
            case MetadataImageFormatResult.MalformedRoot malformed:
                throw new MalformedMetadataRootException(
                    malformed.Reason,
                    source);
            case MetadataImageFormatResult.NoMetadata:
                throw new InvalidOperationException(
                    "An exact metadata-root extent cannot report no metadata.");
            default:
                throw new InvalidOperationException(
                    "Unknown metadata image format result.");
        }
    }

    static void EnsureMetadata(
        PEReader peReader,
        MetadataRootSource? source)
    {
        if (!AdmitImage(peReader, source))
        {
            throw new BadImageFormatException(
                "The PE image contains no managed metadata.");
        }
    }
}
