using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using DotnetInspector.CSharpBodySlicer;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.Text;

namespace DotnetInspector.Services;

public enum SourceChecksumVerification
{
    Exact,
    LineEndingNormalized,
    Unavailable,
    Unsupported,
    Mismatch,
}

public sealed record AuthoredMemberSourceInspection(
    FindingInspection<string> Lines,
    string? Text,
    MemberSourceObservation? Mapping,
    SourceDocumentObservation? Document,
    SourceChecksumVerification? ChecksumVerification)
{
    public bool IsComplete => Lines.Value is FindingInspection<string>.Complete;
}

/// <summary>
/// Acquires one authored member from SourceLink and verifies the portable-PDB checksum before
/// exposing its text as evidence.
/// </summary>
public static class AuthoredSourceAcquisition
{
    public static async Task<AuthoredMemberSourceInspection> AcquireMemberAsync(
        SourceLinkService source,
        int metadataToken,
        string methodName,
        FindingSubject subject,
        SourceFetcher fetcher,
        IReadOnlyList<string>? repositoryPaths = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(fetcher);

        var memberInspection = SourceLinkFindings.InspectMemberSources(
            source,
            subject,
            new MemberSourceQuery(new HashSet<int> { metadataToken }));
        if (memberInspection.Value is FindingInspection<MemberSourceObservation>.Absent absent)
            return Absent(absent.Detail ?? "Authored source mapping is unavailable.");
        if (memberInspection.Value is FindingInspection<MemberSourceObservation>.Failed failed)
            return Failed(failed.Error);

        var memberComplete = (FindingInspection<MemberSourceObservation>.Complete)
            memberInspection.Value;
        var mapping = memberComplete.Findings
            .Select(static finding => finding.Payload)
            .OrderByDescending(static candidate => candidate.IsPrimaryDocument)
            .ThenBy(static candidate => candidate.DocumentRowId)
            .FirstOrDefault();
        if (mapping is null)
            return Absent("The selected member has no portable-PDB source mapping.");

        var documentInspection = SourceLinkFindings.InspectSourceDocuments(
            source,
            subject,
            new SourceDocumentQuery(mapping.CanonicalPath));
        if (documentInspection.Value is FindingInspection<SourceDocumentObservation>.Absent documentAbsent)
            return Absent(documentAbsent.Detail ?? "Authored source document is unavailable.");
        if (documentInspection.Value is FindingInspection<SourceDocumentObservation>.Failed documentFailed)
            return Failed(documentFailed.Error);

        var documentComplete = (FindingInspection<SourceDocumentObservation>.Complete)
            documentInspection.Value;
        var document = documentComplete.Findings
            .Select(static finding => finding.Payload)
            .FirstOrDefault(candidate => string.Equals(
                candidate.CanonicalPath,
                mapping.CanonicalPath,
                StringComparison.OrdinalIgnoreCase));
        if (document is null)
            return Absent("The selected member's source document is not in the portable PDB.");
        if (document.ChecksumAlgorithm is not { Length: > 0 }
            || document.Checksum is not { Length: > 0 })
        {
            return Absent(
                "The portable PDB does not provide a usable source checksum.",
                mapping,
                document,
                SourceChecksumVerification.Unavailable);
        }

        // Honor the source the portable PDB points at when it is present locally. A non-reproducible
        // (local dev) build records a real local path, and the exact bytes that produced this binary
        // may exist only here, so the remote SourceLink URL would 404 or serve different bytes. The
        // checksum gate authenticates the on-disk bytes against the portable PDB, so this cannot
        // surface unrelated content; remote SourceLink stays the fallback for reproducible (published)
        // builds, whose normalized document paths are not local reads in the first place.
        if (TryReadVerifiedLocalSource(document) is { } localBytes)
            return FromContent(mapping, document, localBytes, methodName, subject);

        // Opt-in (--repo): read the committed blob at the SourceLink commit from a user-named local
        // clone, authenticated by the same portable-PDB checksum, before touching the network. This
        // is the path that matters for reproducible (published) builds, whose normalized document
        // paths are not local reads, yet whose exact source lives in a clone the user already has.
        if (repositoryPaths is { Count: > 0 }
            && LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(document, repositoryPaths)
                is { } repoBytes)
        {
            return FromContent(mapping, document, repoBytes, methodName, subject);
        }

        if (document.ResolvedUrl is not { Length: > 0 } url)
        {
            return Absent(document.Storage == SourceDocumentStorage.Embedded
                ? "Embedded authored-source retrieval is not available."
                : "The selected source document has no fetchable SourceLink URL.");
        }

        var bytes = await fetcher.FetchValidatedSourceBytesAsync(
            url,
            content => VerifyChecksum(document, content.Span)
                is SourceChecksumVerification.Exact
                    or SourceChecksumVerification.LineEndingNormalized,
            cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return Failed(
                subject,
                $"Could not fetch authored source from '{url}'.");
        }

        return FromContent(mapping, document, bytes, methodName, subject);
    }

    public static AuthoredMemberSourceInspection FromContent(
        MemberSourceObservation mapping,
        SourceDocumentObservation document,
        byte[] content,
        string methodName,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(subject);

        var verification = VerifyChecksum(document, content);
        if (verification == SourceChecksumVerification.Unavailable)
        {
            return Absent(
                "The portable PDB does not provide a usable source checksum.",
                mapping,
                document,
                verification);
        }
        if (verification is SourceChecksumVerification.Unsupported
            or SourceChecksumVerification.Mismatch)
        {
            return Failed(
                subject,
                verification switch
                {
                    SourceChecksumVerification.Unsupported =>
                        $"The source checksum algorithm '{document.ChecksumAlgorithm}' is unsupported.",
                    _ => "Fetched authored source does not match the portable-PDB checksum.",
                },
                mapping,
                document,
                verification);
        }

        try
        {
            string sourceText = DecodeSourceText(content);
            // A C# destructor's source line is "~Type()", which carries no
            // accessibility keyword and whose metadata name ("Finalize") does not
            // appear in the text; without an explicit signal the backward signature
            // scan would walk past it into the preceding member. Use the mapping's
            // authoritative object.Finalize-override identity (computed from metadata
            // by the source-mapping producer), NOT a "Finalize" name match, so an
            // ordinary parameterized method named "Finalize" is never truncated.
            bool isDestructor = mapping.IsFinalizer;
            string? memberText = BodySlicer.ExtractMethodBody(
                sourceText,
                mapping.StartLine,
                mapping.EndLine,
                methodName,
                isDestructor,
                isDestructor ? mapping.Anchor.TypeFullName : null);
            if (memberText is null)
            {
                // The member's sequence points map to its type's header, not to a declaration
                // of its own. Absent is the honest answer; the header is not this member's
                // source.
                return Absent(
                    "The selected member has no authored declaration of its own; its source range is the declaring type's header.",
                    mapping,
                    document,
                    verification);
            }

            var lines = TextFindings.Inspect(memberText, subject).ToImmutableArray();
            return new AuthoredMemberSourceInspection(
                new FindingInspection<string>.Complete(lines),
                memberText,
                mapping,
                document,
                verification);
        }
        catch (Exception ex) when (ex is ArgumentException
            or IndexOutOfRangeException
            or InvalidOperationException)
        {
            return Failed(
                subject,
                $"Could not extract the authored member source: {ex.Message}",
                mapping,
                document,
                verification);
        }
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
    /// Verifies fetched or on-disk source <paramref name="content"/> against a portable-PDB
    /// document hash. Accepts an exact match or one recovered after CR/LF normalization. Returns
    /// <see cref="SourceChecksumVerification.Unavailable"/> when no usable checksum is supplied.
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
    /// Reads authored source from a local file, but only when its bytes authenticate against the
    /// portable-PDB document checksum. The document path originates in an untrusted PDB, so the
    /// checksum — not the path — authorizes the read: an attacker cannot precompute a matching hash
    /// for an unknown local file, so a mismatched or absent checksum yields null. Returns null (the
    /// caller falls back to the remote SourceLink URL) when the path is not a compiler source file,
    /// is absent or unreadable, carries no usable checksum, or the content does not verify.
    /// </summary>
    public static byte[]? TryReadVerifiedLocalSource(
        string? localPath,
        string? checksumAlgorithm,
        byte[]? checksum)
    {
        if (string.IsNullOrEmpty(localPath)
            || checksumAlgorithm is not { Length: > 0 }
            || checksum is not { Length: > 0 }
            || !IsCompilerLanguageSourcePath(localPath)
            || !IsLocalFileSystemPath(localPath))
        {
            return null;
        }

        byte[] content;
        try
        {
            // The document path is attacker-influenced, so bound the I/O even though the checksum
            // authenticates the content: skip missing files, reparse points (symlinks that could
            // redirect the read), and oversized files.
            var info = new FileInfo(localPath);
            if (!info.Exists
                || (info.Attributes & FileAttributes.ReparsePoint) != 0
                || info.Length > MaxLocalSourceBytes)
            {
                return null;
            }
            content = File.ReadAllBytes(localPath);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            return null;
        }

        return VerifyChecksum(checksumAlgorithm, checksum, content)
            is SourceChecksumVerification.Exact
                or SourceChecksumVerification.LineEndingNormalized
            ? content
            : null;
    }

    /// <summary>
    /// Convenience overload that authenticates a local source file against a resolved
    /// <see cref="SourceDocumentObservation"/> (its original PDB document path and checksum).
    /// </summary>
    public static byte[]? TryReadVerifiedLocalSource(SourceDocumentObservation document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Checksum is not { Length: > 0 })
            return null;

        byte[] checksum;
        try
        {
            checksum = Convert.FromHexString(document.Checksum);
        }
        catch (FormatException)
        {
            return null;
        }

        return TryReadVerifiedLocalSource(
            document.OriginalPath,
            document.ChecksumAlgorithm,
            checksum);
    }

    static bool IsCompilerLanguageSourcePath(string path)
        => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".fs", StringComparison.OrdinalIgnoreCase);

    // Upper bound on a local source file we are willing to read. Authored source files are small;
    // this only guards against an attacker-directed path pointing at a pathologically large file.
    const long MaxLocalSourceBytes = 64L * 1024 * 1024;

    /// <summary>
    /// True only for a plain, fully-qualified local filesystem path. Rejects relative paths, UNC
    /// shares (<c>\\server\share</c>), Win32 device paths (<c>\\?\</c>, <c>\\.\</c>), and paths on a
    /// network-mapped drive so an untrusted PDB document name cannot trigger outbound SMB/network
    /// I/O before the checksum is even evaluated.
    /// </summary>
    internal static bool IsLocalFileSystemPath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException)
        {
            return false;
        }

        if (full.StartsWith(@"\\", StringComparison.Ordinal)
            || full.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        // Reject paths on a network-mapped drive (e.g. Z: -> \\server\share): reading one reaches
        // the network even though the leading form is a local drive letter. GetDriveType is a local
        // lookup and does not connect to the share.
        try
        {
            var root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network)
                return false;
        }
        catch (Exception ex) when (ex is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            // Indeterminate drive type: fall through to the reparse/size gates and the checksum.
        }

        return true;
    }

    /// <summary>
    /// Decodes source bytes to text using the byte-order mark to select the encoding
    /// (UTF-8/UTF-16/UTF-32), defaulting to UTF-8 when no BOM is present, and strips the BOM.
    /// This mirrors the remote path's <see cref="System.Net.Http.HttpContent.ReadAsStringAsync()"/>
    /// decoding so a checksum-verified local file in any of those encodings renders identically.
    /// </summary>
    public static string DecodeSourceText(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var stream = new MemoryStream(content, writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    static AuthoredMemberSourceInspection Absent(string detail)
        => new(
            new FindingInspection<string>.Absent(detail),
            Text: null,
            Mapping: null,
            Document: null,
            ChecksumVerification: null);

    static AuthoredMemberSourceInspection Absent(
        string detail,
        MemberSourceObservation mapping,
        SourceDocumentObservation document,
        SourceChecksumVerification verification)
        => new(
            new FindingInspection<string>.Absent(detail),
            Text: null,
            mapping,
            document,
            verification);

    static AuthoredMemberSourceInspection Failed(
        InspectionError error)
        => new(
            new FindingInspection<string>.Failed(error),
            Text: null,
            Mapping: null,
            Document: null,
            ChecksumVerification: null);

    static AuthoredMemberSourceInspection Failed(
        FindingSubject subject,
        string reason,
        MemberSourceObservation? mapping = null,
        SourceDocumentObservation? document = null,
        SourceChecksumVerification? verification = null)
        => new(
            new FindingInspection<string>.Failed(
                new InspectionError(subject, TextFindings.LineDescriptor, reason)),
            Text: null,
            mapping,
            document,
            verification);

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
