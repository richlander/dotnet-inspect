using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// The image contains Windows Metadata, which is outside the supported
/// ECMA-335 assembly-metadata format.
/// </summary>
public sealed class UnsupportedMetadataFormatException()
    : NotSupportedException(
        "Windows Metadata is not a supported metadata format.");

/// <summary>
/// The image has a malformed ECMA-335 metadata root.
/// </summary>
public sealed class MalformedMetadataRootException(
    MetadataRootMalformedReason reason)
    : BadImageFormatException(
        $"The assembly metadata root is malformed ({reason}).")
{
    public MetadataRootMalformedReason Reason { get; } = reason;
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
    public static bool AdmitImage(PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(peReader);

        return Admit(MetadataImageFormatClassifier.Classify(peReader));
    }

    internal static void AdmitRoot(BlobReader root) =>
        _ = Admit(MetadataImageFormatClassifier.Classify(root));

    static bool Admit(MetadataImageFormatResult result) =>
        result switch
        {
            MetadataImageFormatResult.SupportedEcma335 => true,
            MetadataImageFormatResult.NoMetadata => false,
            MetadataImageFormatResult.UnsupportedWindowsMetadata =>
                throw new UnsupportedMetadataFormatException(),
            MetadataImageFormatResult.MalformedRoot malformed =>
                throw new MalformedMetadataRootException(malformed.Reason),
            _ => throw new InvalidOperationException(
                "Unknown metadata image format result."),
        };

    public static MetadataReader GetMetadataReader(PEReader peReader)
    {
        EnsureMetadata(peReader);
        return peReader.GetMetadataReader();
    }

    public static MetadataReader GetMetadataReader(
        PEReader peReader,
        MetadataReaderOptions options)
    {
        EnsureMetadata(peReader);
        return peReader.GetMetadataReader(options);
    }

    static void EnsureMetadata(PEReader peReader)
    {
        if (!AdmitImage(peReader))
        {
            throw new BadImageFormatException(
                "The PE image contains no managed metadata.");
        }
    }
}
