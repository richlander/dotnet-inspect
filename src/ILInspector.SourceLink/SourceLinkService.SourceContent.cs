using System.Security.Cryptography;
using System.Text;
using ILInspector.Metadata;

namespace ILInspector.SourceLink;

public enum SourceChecksumVerification
{
    Exact,
    LineEndingNormalized,
    Unavailable,
    Unsupported,
    Mismatch,
}

public sealed record VerifiedSourceTextResult(
    string? Text,
    string? Failure,
    SourceChecksumVerification ChecksumVerification = SourceChecksumVerification.Unavailable)
{
    public bool IsVerified => Text is not null;
}

public sealed partial class SourceLinkService
{
    /// <summary>Interprets supplied source bytes only after their PDB checksum verifies.</summary>
    public static VerifiedSourceTextResult VerifySourceContent(
        string? checksumAlgorithm,
        byte[]? checksum,
        byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        SourceChecksumVerification verification =
            VerifyChecksum(checksumAlgorithm, checksum, content);
        return verification switch
        {
            SourceChecksumVerification.Exact or SourceChecksumVerification.LineEndingNormalized =>
                new(DecodeSourceText(content), null, verification),
            SourceChecksumVerification.Unavailable =>
                new(null, "The portable PDB does not provide a usable source checksum.", verification),
            SourceChecksumVerification.Unsupported =>
                new(null, "The portable-PDB source checksum algorithm is unsupported.", verification),
            _ => new(null, "Fetched source does not match the portable-PDB checksum.", verification),
        };
    }

    public static SourceChecksumVerification VerifyChecksum(
        SourceDocumentObservation document,
        ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.ChecksumAlgorithm is not { Length: > 0 }
            || document.Checksum is not { Length: > 0 })
        {
            return SourceChecksumVerification.Unavailable;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(document.Checksum);
        }
        catch (FormatException)
        {
            return SourceChecksumVerification.Unsupported;
        }

        return VerifyChecksum(document.ChecksumAlgorithm, expected, content);
    }

    /// <summary>
    /// Verifies supplied source bytes against a portable-PDB document hash,
    /// preserving exact and accepted CR/LF-normalized correspondence.
    /// </summary>
    public static SourceChecksumVerification VerifyChecksum(
        string? algorithm,
        byte[]? expectedChecksum,
        ReadOnlySpan<byte> content)
    {
        if (algorithm is not { Length: > 0 } || expectedChecksum is not { Length: > 0 })
            return SourceChecksumVerification.Unavailable;

        if (HashMatches(algorithm, content, expectedChecksum))
            return SourceChecksumVerification.Exact;
        if (HashMatchesAfterLineEndingNormalization(algorithm, content, expectedChecksum))
            return SourceChecksumVerification.LineEndingNormalized;

        return IsSupportedAlgorithm(algorithm)
            ? SourceChecksumVerification.Mismatch
            : SourceChecksumVerification.Unsupported;
    }

    /// <summary>
    /// Decodes source using its UTF-8/UTF-16/UTF-32 BOM, or UTF-8 when absent,
    /// and removes the BOM. Decoding alone does not establish checksum evidence.
    /// </summary>
    public static string DecodeSourceText(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var stream = new MemoryStream(content, writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    static bool HashMatches(
        string algorithm,
        ReadOnlySpan<byte> content,
        ReadOnlySpan<byte> expected)
        => ComputeHash(algorithm, content).AsSpan().SequenceEqual(expected);

    static bool HashMatchesAfterLineEndingNormalization(
        string algorithm,
        ReadOnlySpan<byte> content,
        ReadOnlySpan<byte> expected)
    {
        if (!content.Contains((byte)'\n') && !content.Contains((byte)'\r'))
            return false;

        return HashMatches(algorithm, NormalizeLineEndings(content, crlf: false), expected)
            || HashMatches(algorithm, NormalizeLineEndings(content, crlf: true), expected);
    }

    static byte[] NormalizeLineEndings(ReadOnlySpan<byte> content, bool crlf)
    {
        List<byte> lf = new(content.Length);
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r')
            {
                if (i + 1 < content.Length && content[i + 1] == '\n')
                    continue;

                lf.Add((byte)'\n');
                continue;
            }

            lf.Add(content[i]);
        }

        if (!crlf)
            return [.. lf];

        List<byte> result = new(lf.Count);
        foreach (byte value in lf)
        {
            if (value == '\n')
            {
                result.Add((byte)'\r');
                result.Add((byte)'\n');
            }
            else
            {
                result.Add(value);
            }
        }

        return [.. result];
    }

    static byte[] ComputeHash(string algorithm, ReadOnlySpan<byte> content)
        => algorithm switch
        {
            "SHA256" => SHA256.HashData(content),
            "SHA1" => SHA1.HashData(content),
            _ => [],
        };

    static bool IsSupportedAlgorithm(string algorithm)
        => algorithm is "SHA256" or "SHA1";
}
