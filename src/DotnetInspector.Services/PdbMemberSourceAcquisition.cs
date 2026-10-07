using Inspector.Findings;
using ILInspector.SourceLink;

namespace DotnetInspector.Services;

/// <summary>
/// Acquires checksum-verified authored member source for certification and
/// diagnostic consumers that already own an open SourceLink context.
/// Product member and type Source settlement uses SourceHouse.
/// </summary>
public static class PdbMemberSourceAcquisition
{
    public static async Task<PdbMemberSourceInspection> AcquireAsync(
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

        FindingInspection<MemberSourceObservation> memberInspection =
            SourceLinkFindings.InspectMemberSources(
                source,
                subject,
                new MemberSourceQuery(new HashSet<int> { metadataToken }));
        if (memberInspection.Value
            is FindingInspection<MemberSourceObservation>.Absent absent)
        {
            return Absent(
                absent.Detail ?? "PDB source mapping is unavailable.",
                PdbMemberSourceOutcome.SourceMappingUnavailable);
        }
        if (memberInspection.Value
            is FindingInspection<MemberSourceObservation>.Failed failed)
        {
            return Failed(
                failed.Error,
                PdbMemberSourceOutcome.InspectionFailed);
        }

        var memberComplete =
            (FindingInspection<MemberSourceObservation>.Complete)
                memberInspection.Value;
        MemberSourceObservation? mapping = memberComplete.Findings
            .Select(static finding => finding.Payload)
            .OrderByDescending(
                static candidate => candidate.IsPrimaryDocument)
            .ThenBy(static candidate => candidate.DocumentRowId)
            .FirstOrDefault();
        if (mapping is null)
        {
            return Absent(
                "The selected member has no portable-PDB source mapping.",
                PdbMemberSourceOutcome.SourceMappingUnavailable);
        }

        FindingInspection<SourceDocumentObservation> documentInspection =
            SourceLinkFindings.InspectSourceDocuments(
                source,
                subject,
                new SourceDocumentQuery(mapping.CanonicalPath));
        if (documentInspection.Value
            is FindingInspection<SourceDocumentObservation>.Absent
                documentAbsent)
        {
            return Absent(
                documentAbsent.Detail
                    ?? "PDB source document is unavailable.",
                PdbMemberSourceOutcome.SourceDocumentUnavailable);
        }
        if (documentInspection.Value
            is FindingInspection<SourceDocumentObservation>.Failed
                documentFailed)
        {
            return Failed(
                documentFailed.Error,
                PdbMemberSourceOutcome.InspectionFailed);
        }

        var documentComplete =
            (FindingInspection<SourceDocumentObservation>.Complete)
                documentInspection.Value;
        SourceDocumentObservation? document = SelectMappedDocument(
            mapping,
            documentComplete.Findings.Select(
                static finding => finding.Payload));
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

        if (allowLocalSource
            && VerifiedLocalSourceRead.TryRead(document) is { } localBytes)
        {
            return PdbSourceInspectionProjection.FromMemberContent(
                mapping,
                document,
                localBytes,
                methodName,
                subject);
        }

        if (repositoryPaths is { Count: > 0 }
            && LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                document,
                repositoryPaths) is { } repoBytes)
        {
            return PdbSourceInspectionProjection.FromMemberContent(
                mapping,
                document,
                repoBytes,
                methodName,
                subject);
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

            return Absent(
                document.Storage == SourceDocumentStorage.Embedded
                    ? "Embedded PDB-source retrieval is not available."
                    : "The selected source document has no fetchable SourceLink URL.",
                PdbMemberSourceOutcome.SourceAcquisitionUnavailable);
        }

        FetchSourceResult fetch =
            await fetcher.FetchVerifiedSourceBytesAsync(
                url,
                content =>
                    SourceLinkService.VerifyChecksum(
                        document,
                        content.Span)
                    is SourceChecksumVerification.Exact
                        or SourceChecksumVerification
                            .LineEndingNormalized,
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

        return PdbSourceInspectionProjection.FromMemberContent(
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
}
