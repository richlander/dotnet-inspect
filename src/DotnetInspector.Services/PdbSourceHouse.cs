using System.IO;
using System.Text;

using Inspector.Findings;
using ILInspector.Metadata;
using DotnetInspector.SourceHouse;

namespace DotnetInspector.Services;

/// <summary>
/// Clearing house for PDB-mapped source acquisition: orders local and remote candidates,
/// verifies the portable-PDB checksum, and exposes one settled source result as evidence.
/// </summary>
public static class PdbSourceHouse
{
    /// <summary>
    /// Acquires the default PDB source document for one exact metadata
    /// type and verifies its portable-PDB checksum before exposing text.
    /// </summary>
    public static async Task<PdbTypeSourceInspection> AcquireTypeAsync(
        SourceLinkService source,
        MetadataTypeDefinitionName type,
        FindingSubject subject,
        SourceFetch fetcher,
        IReadOnlyList<string>? repositoryPaths = null,
        CancellationToken cancellationToken = default,
        bool allowLocalSource = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(fetcher);

        if (source.Context.NeedsPdb
            && !source.Context.WindowsPdbDetected)
        {
            return TypeFailed(
                subject,
                "A matching portable PDB remains unresolved after acquisition.",
                PdbTypeSourceOutcome.PortablePdbUnavailable);
        }

        SourceLinkResolver.TypeSourceInfo? mapping;
        try
        {
            mapping = source.ResolveTypeSource(type);
        }
        catch (Exception ex) when (IsPdbInspectionFailure(ex))
        {
            return TypeFailed(
                subject,
                $"Portable PDB type source mapping failed: {ex.Message}",
                PdbTypeSourceOutcome.InspectionFailed);
        }
        if (mapping is null)
        {
            return TypeAbsent(
                "The selected type has no portable-PDB source mapping.",
                PdbTypeSourceOutcome.SourceMappingUnavailable);
        }

        SourceLinkResolver.TypeSourceDocument? defaultDocument =
            TypeSourceDocumentSelection.SelectDefault(mapping);
        if (defaultDocument?.FilePath is not { Length: > 0 } sourcePath)
        {
            return TypeAbsent(
                "The selected type has no portable-PDB source mapping.",
                PdbTypeSourceOutcome.SourceMappingUnavailable);
        }

        FindingInspection<SourceDocumentObservation> documentInspection =
            SourceLinkFindings.InspectSourceDocuments(
                source,
                subject);
        if (documentInspection.Value
            is FindingInspection<SourceDocumentObservation>.Absent absent)
        {
            return TypeAbsent(
                absent.Detail
                    ?? "PDB source document is unavailable.",
                PdbTypeSourceOutcome.SourceDocumentUnavailable,
                mapping);
        }
        if (documentInspection.Value
            is FindingInspection<SourceDocumentObservation>.Failed failed)
        {
            return TypeFailed(
                failed.Error,
                PdbTypeSourceOutcome.InspectionFailed,
                mapping);
        }

        var complete =
            (FindingInspection<SourceDocumentObservation>.Complete)
                documentInspection.Value;
        SourceDocumentObservation? document =
            SelectTypeDocument(
                sourcePath,
                complete.Findings.Select(
                    static finding => finding.Payload));
        if (document is null)
        {
            return TypeAbsent(
                "The selected type's default source document is not uniquely identified in the portable PDB.",
                PdbTypeSourceOutcome.SourceDocumentUnavailable,
                mapping);
        }
        if (document.ChecksumAlgorithm is not { Length: > 0 }
            || document.Checksum is not { Length: > 0 })
        {
            return TypeAbsent(
                "The portable PDB does not provide a usable source checksum.",
                PdbTypeSourceOutcome.ChecksumUnavailable,
                mapping,
                document,
                SourceChecksumVerification.Unavailable);
        }

        if (allowLocalSource
            && TryReadVerifiedLocalSource(document) is { } localBytes)
        {
            return FromTypeContent(
                mapping,
                document,
                localBytes,
                subject);
        }

        if (repositoryPaths is { Count: > 0 }
            && LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                document,
                repositoryPaths) is { } repoBytes)
        {
            return FromTypeContent(
                mapping,
                document,
                repoBytes,
                subject);
        }

        string? url = document.ResolvedUrl ?? defaultDocument.SourceUrl;
        if (url is not { Length: > 0 })
        {
            if (document.Storage != SourceDocumentStorage.Embedded
                && document.ResolutionStatus
                    == SourceDocumentResolutionStatus.Rejected)
            {
                return TypeFailed(
                    subject,
                    "The SourceLink mapping for the selected source document was rejected.",
                    PdbTypeSourceOutcome.SourceAcquisitionFailed,
                    mapping,
                    document);
            }

            return TypeAbsent(
                document.Storage == SourceDocumentStorage.Embedded
                    ? "Embedded PDB-source retrieval is not available."
                    : "The selected source document has no fetchable SourceLink URL.",
                PdbTypeSourceOutcome.SourceAcquisitionUnavailable,
                mapping,
                document,
                SourceChecksumVerification.Unavailable);
        }

        FetchSourceResult fetch =
            await fetcher.FetchVerifiedSourceBytesAsync(
                url,
                content => SourceLinkService.VerifyChecksum(document, content.Span)
                    is SourceChecksumVerification.Exact
                        or SourceChecksumVerification
                            .LineEndingNormalized,
                cancellationToken).ConfigureAwait(false);
        if (fetch is FetchSourceResult.Failure failure)
        {
            if (failure.Error == SourceError.NotFound)
            {
                return TypeAbsent(
                    "The resolved SourceLink document was not found.",
                    PdbTypeSourceOutcome.SourceAcquisitionUnavailable,
                    mapping,
                    document,
                    SourceChecksumVerification.Unavailable);
            }

            return TypeFailed(
                subject,
                failure.Error switch
                {
                    SourceError.RequestNotAuthorized =>
                        "The host does not authorize this SourceLink destination.",
                    SourceError.ValidationFailed =>
                        "Fetched PDB source does not match the portable-PDB checksum.",
                    SourceError.StorageFailed =>
                        "The source-content store failed.",
                    _ => "Could not fetch PDB source.",
                },
                failure.Error == SourceError.ValidationFailed
                    ? PdbTypeSourceOutcome.ChecksumMismatch
                    : PdbTypeSourceOutcome.SourceAcquisitionFailed,
                mapping,
                document,
                failure.Error == SourceError.ValidationFailed
                    ? SourceChecksumVerification.Mismatch
                    : null);
        }

        return FromTypeContent(
            mapping,
            document,
            ((FetchSourceResult.Success)fetch).Content,
            subject);
    }

    public static async Task<PdbMemberSourceInspection> AcquireMemberAsync(
        SourceLinkService source,
        int metadataToken,
        string methodName,
        FindingSubject subject,
        SourceFetch fetcher,
        IReadOnlyList<string>? repositoryPaths = null,
        CancellationToken cancellationToken = default,
        bool allowLocalSource = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(fetcher);

        if (source.Context.NeedsPdb
            && !source.Context.WindowsPdbDetected)
        {
            return Failed(
                subject,
                "A matching portable PDB remains unresolved after acquisition.",
                PdbMemberSourceOutcome.PortablePdbUnavailable);
        }

        var memberInspection = SourceLinkFindings.InspectMemberSources(
            source,
            subject,
            new MemberSourceQuery(new HashSet<int> { metadataToken }));
        if (memberInspection.Value is FindingInspection<MemberSourceObservation>.Absent absent)
        {
            return Absent(
                absent.Detail ?? "PDB source mapping is unavailable.",
                PdbMemberSourceOutcome.SourceMappingUnavailable);
        }
        if (memberInspection.Value is FindingInspection<MemberSourceObservation>.Failed failed)
        {
            return Failed(
                failed.Error,
                PdbMemberSourceOutcome.InspectionFailed);
        }

        var memberComplete = (FindingInspection<MemberSourceObservation>.Complete)
            memberInspection.Value;
        var mapping = memberComplete.Findings
            .Select(static finding => finding.Payload)
            .OrderByDescending(static candidate => candidate.IsPrimaryDocument)
            .ThenBy(static candidate => candidate.DocumentRowId)
            .FirstOrDefault();
        if (mapping is null)
        {
            return Absent(
                "The selected member has no portable-PDB source mapping.",
                PdbMemberSourceOutcome.SourceMappingUnavailable);
        }

        var documentInspection = SourceLinkFindings.InspectSourceDocuments(
            source,
            subject,
            new SourceDocumentQuery(mapping.CanonicalPath));
        if (documentInspection.Value is FindingInspection<SourceDocumentObservation>.Absent documentAbsent)
        {
            return Absent(
                documentAbsent.Detail ?? "PDB source document is unavailable.",
                PdbMemberSourceOutcome.SourceDocumentUnavailable);
        }
        if (documentInspection.Value is FindingInspection<SourceDocumentObservation>.Failed documentFailed)
        {
            return Failed(
                documentFailed.Error,
                PdbMemberSourceOutcome.InspectionFailed);
        }

        var documentComplete = (FindingInspection<SourceDocumentObservation>.Complete)
            documentInspection.Value;
        var document = SelectMappedDocument(
            mapping,
            documentComplete.Findings.Select(static finding => finding.Payload));
        if (document is null)
        {
            return Absent(
                "The selected member's source document is not in the portable PDB.",
                PdbMemberSourceOutcome.SourceDocumentUnavailable);
        }
        if (document.ChecksumAlgorithm is not { Length: > 0 }
            || document.Checksum is not { Length: > 0 })
        {
            return Absent(
                "The portable PDB does not provide a usable source checksum.",
                PdbMemberSourceOutcome.ChecksumUnavailable,
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
        if (allowLocalSource
            && TryReadVerifiedLocalSource(document) is { } localBytes)
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
            if (document.Storage != SourceDocumentStorage.Embedded
                && document.ResolutionStatus
                    == SourceDocumentResolutionStatus.Rejected)
            {
                return Failed(
                    subject,
                    "The SourceLink mapping for the selected source document was rejected.",
                    PdbMemberSourceOutcome.SourceAcquisitionFailed,
                    mapping,
                    document);
            }

            if (document.Storage != SourceDocumentStorage.Embedded
                && source.SourceLinkMap.Status == SourceLinkMapStatus.Unusable)
            {
                return Failed(
                    subject,
                    "The SourceLink map is unusable: "
                        + (source.SourceLinkMap.Error
                            ?? "the map contains no usable document mappings"),
                    PdbMemberSourceOutcome.InspectionFailed,
                    mapping,
                    document);
            }

            return Absent(document.Storage == SourceDocumentStorage.Embedded
                ? "Embedded PDB-source retrieval is not available."
                : "The selected source document has no fetchable SourceLink URL.",
                PdbMemberSourceOutcome.SourceAcquisitionUnavailable);
        }

        FetchSourceResult fetch = await fetcher.FetchVerifiedSourceBytesAsync(
            url,
            content => SourceLinkService.VerifyChecksum(document, content.Span)
                is SourceChecksumVerification.Exact
                    or SourceChecksumVerification.LineEndingNormalized,
            cancellationToken).ConfigureAwait(false);
        if (fetch is FetchSourceResult.Failure failure)
        {
            if (failure.Error == SourceError.NotFound)
            {
                return Absent(
                    "The resolved SourceLink document was not found.",
                    PdbMemberSourceOutcome.SourceAcquisitionUnavailable,
                    mapping,
                    document,
                    SourceChecksumVerification.Unavailable);
            }

            if (failure.Error == SourceError.ValidationFailed)
            {
                return Failed(
                    subject,
                    "Fetched PDB source does not match the portable-PDB checksum.",
                    PdbMemberSourceOutcome.ChecksumMismatch,
                    mapping,
                    document,
                    SourceChecksumVerification.Mismatch);
            }

            return Failed(
                subject,
                failure.Error switch
                {
                    SourceError.RequestNotAuthorized =>
                        "The host does not authorize this SourceLink destination.",
                    SourceError.StorageFailed =>
                        "The source-content store failed.",
                    _ => "Could not fetch PDB source.",
                },
                PdbMemberSourceOutcome.SourceAcquisitionFailed);
        }

        return FromContent(
            mapping,
            document,
            ((FetchSourceResult.Success)fetch).Content,
            methodName,
            subject);
    }

    internal static SourceDocumentObservation? SelectMappedDocument(
        MemberSourceObservation mapping,
        IEnumerable<SourceDocumentObservation> documents)
    {
        SourceDocumentObservation? match = null;
        foreach (SourceDocumentObservation candidate in documents)
        {
            if (candidate.DocumentRowId != mapping.DocumentRowId
                || !string.Equals(
                    candidate.OriginalPath,
                    mapping.OriginalPath,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
                return null;

            match = candidate;
        }

        return match;
    }

    internal static SourceDocumentObservation? SelectTypeDocument(
        string sourcePath,
        IEnumerable<SourceDocumentObservation> documents)
    {
        SourceDocumentObservation? match = null;
        foreach (SourceDocumentObservation candidate in documents)
        {
            if (!string.Equals(
                    candidate.OriginalPath,
                    sourcePath,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
                return null;

            match = candidate;
        }

        return match;
    }

    public static async Task<VerifiedSourceTextResult> AcquireVerifiedSourceTextAsync(
        SourceFetch fetcher,
        string? localPath,
        string url,
        string? checksumAlgorithm,
        byte[]? checksum,
        IReadOnlyList<string>? repositoryPaths = null,
        CancellationToken cancellationToken = default,
        bool allowLocalSource = true)
    {
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        byte[]? content = allowLocalSource
            ? TryReadVerifiedLocalSource(localPath, checksumAlgorithm, checksum)
            : null;
        content ??= LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
            url,
            checksumAlgorithm,
            checksum,
            repositoryPaths ?? []);
        if (content is not null)
        {
            return SourceLinkService.VerifySourceContent(
                checksumAlgorithm, checksum, content);
        }

        return await VerifiedSourceTextFetch.FetchAsync(
            fetcher,
            url,
            checksumAlgorithm,
            checksum,
            cancellationToken).ConfigureAwait(false);
    }

    static PdbMemberSourceInspection FromContent(
        MemberSourceObservation mapping,
        SourceDocumentObservation document,
        byte[] content,
        string methodName,
        FindingSubject subject)
        => PdbSourceInspectionProjection.FromMemberContent(
            mapping,
            document,
            content,
            methodName,
            subject);

    static PdbTypeSourceInspection FromTypeContent(
        SourceLinkResolver.TypeSourceInfo mapping,
        SourceDocumentObservation document,
        byte[] content,
        FindingSubject subject)
        => PdbSourceInspectionProjection.FromTypeContent(
            mapping,
            document,
            content,
            subject);

    /// <summary>
    /// Reads PDB source from a local file, but only when its bytes authenticate against the
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

        return SourceLinkService.VerifyChecksum(checksumAlgorithm, checksum, content)
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

    // Upper bound on a local source file we are willing to read. PDB source files are small;
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

    static PdbMemberSourceInspection Absent(
        string detail,
        PdbMemberSourceOutcome outcome)
        => PdbSourceInspectionProjection.MemberAbsent(detail, outcome);

    static PdbMemberSourceInspection Absent(
        string detail,
        PdbMemberSourceOutcome outcome,
        MemberSourceObservation mapping,
        SourceDocumentObservation document,
        SourceChecksumVerification verification)
        => PdbSourceInspectionProjection.MemberAbsent(
            detail,
            outcome,
            mapping,
            document,
            verification);

    static PdbMemberSourceInspection Failed(
        InspectionError error,
        PdbMemberSourceOutcome outcome)
        => PdbSourceInspectionProjection.MemberFailed(error, outcome);

    static PdbMemberSourceInspection Failed(
        FindingSubject subject,
        string reason,
        PdbMemberSourceOutcome outcome,
        MemberSourceObservation? mapping = null,
        SourceDocumentObservation? document = null,
        SourceChecksumVerification? verification = null)
        => PdbSourceInspectionProjection.MemberFailed(
            subject,
            reason,
            outcome,
            mapping,
            document,
            verification);

    static PdbTypeSourceInspection TypeAbsent(
        string detail,
        PdbTypeSourceOutcome outcome,
        SourceLinkResolver.TypeSourceInfo? mapping = null,
        SourceDocumentObservation? document = null,
        SourceChecksumVerification? verification = null)
        => PdbSourceInspectionProjection.TypeAbsent(
            detail,
            outcome,
            mapping,
            document,
            verification);

    static PdbTypeSourceInspection TypeFailed(
        InspectionError error,
        PdbTypeSourceOutcome outcome,
        SourceLinkResolver.TypeSourceInfo? mapping = null)
        => PdbSourceInspectionProjection.TypeFailed(error, outcome, mapping);

    static PdbTypeSourceInspection TypeFailed(
        FindingSubject subject,
        string reason,
        PdbTypeSourceOutcome outcome,
        SourceLinkResolver.TypeSourceInfo? mapping = null,
        SourceDocumentObservation? document = null,
        SourceChecksumVerification? verification = null)
        => PdbSourceInspectionProjection.TypeFailed(
            subject,
            reason,
            outcome,
            mapping,
            document,
            verification);

    static bool IsPdbInspectionFailure(Exception exception)
        => exception is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException
            or DecoderFallbackException;
}
