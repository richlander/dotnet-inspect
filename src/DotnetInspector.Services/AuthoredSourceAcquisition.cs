using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(fetcher);

        var memberInspection = MetadataFindings.InspectMemberSources(
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

        var documentInspection = MetadataFindings.InspectSourceDocuments(
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
        if (verification is SourceChecksumVerification.Unavailable
            or SourceChecksumVerification.Unsupported
            or SourceChecksumVerification.Mismatch)
        {
            return Failed(
                subject,
                verification switch
                {
                    SourceChecksumVerification.Unavailable =>
                        "The portable PDB does not provide a usable source checksum.",
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
            string sourceText = Encoding.UTF8.GetString(content);
            if (sourceText.Length > 0 && sourceText[0] == '\uFEFF')
                sourceText = sourceText[1..];
            string memberText = SourceLinkResolver.ExtractMethodBody(
                sourceText,
                mapping.StartLine,
                mapping.EndLine,
                methodName);
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

        if (HashMatches(document.ChecksumAlgorithm, content, expected))
            return SourceChecksumVerification.Exact;
        if (HashMatchesAfterLineEndingNormalization(
            document.ChecksumAlgorithm,
            content,
            expected))
        {
            return SourceChecksumVerification.LineEndingNormalized;
        }

        return IsSupportedAlgorithm(document.ChecksumAlgorithm)
            ? SourceChecksumVerification.Mismatch
            : SourceChecksumVerification.Unsupported;
    }

    static AuthoredMemberSourceInspection Absent(string detail)
        => new(
            new FindingInspection<string>.Absent(detail),
            Text: null,
            Mapping: null,
            Document: null,
            ChecksumVerification: null);

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
