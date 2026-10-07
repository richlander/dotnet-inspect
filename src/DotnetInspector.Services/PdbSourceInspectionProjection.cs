using System.Collections.Immutable;

using CSharpText;
using CSharpText.MemberSlicing;
using ILInspector.Metadata;
using Inspector.Findings;
using Inspector.Text;

namespace DotnetInspector.Services;

public enum PdbMemberSourceOutcome
{
    Complete,
    PortablePdbUnavailable,
    PortablePdbAcquisitionFailed,
    SourceMappingUnavailable,
    SourceDocumentUnavailable,
    ChecksumUnavailable,
    ChecksumUnsupported,
    ChecksumMismatch,
    SourceAcquisitionUnavailable,
    SourceAcquisitionFailed,
    NoVouchedDeclaration,
    SourceTooComplex,
    InvalidSequencePointCoordinates,
    SourceExtractionFailed,
    InspectionFailed,
    SourceDeadlineExceeded,
    SourceLimitExceeded,
}

public sealed record PdbMemberSourceInspection(
    FindingInspection<string> Lines,
    string? Text,
    MemberSourceObservation? Mapping,
    SourceDocumentObservation? Document,
    SourceChecksumVerification? ChecksumVerification)
{
    public bool IsComplete =>
        Lines.Value is FindingInspection<string>.Complete;

    public PdbMemberSourceOutcome Outcome { get; init; } =
        Lines.Value is FindingInspection<string>.Complete
            ? PdbMemberSourceOutcome.Complete
            : PdbMemberSourceOutcome.InspectionFailed;
}

public enum PdbTypeSourceOutcome
{
    Unspecified,
    Complete,
    PortablePdbUnavailable,
    PortablePdbAcquisitionFailed,
    SourceMappingUnavailable,
    SourceDocumentUnavailable,
    ChecksumUnavailable,
    ChecksumUnsupported,
    ChecksumMismatch,
    SourceAcquisitionUnavailable,
    SourceAcquisitionFailed,
    SourceTooComplex,
    SourceExtractionFailed,
    InspectionFailed,
    SourceDeadlineExceeded,
    SourceLimitExceeded,
    PortablePdbPreferenceWindowElapsed,
}

public enum PdbTypeSourceUnitScope
{
    PrimaryTypeDocument,
    AdditionalTypeDocument,
}

public enum PdbTypeSourceMappingStrength
{
    CorrelatedTypeDocument,
    InferredTypeDocument,
}

public sealed record PdbTypeSourceAdditionalDocument(
    string OriginalPath,
    string? ResolvedUrl);

public sealed record PdbTypeSourceInspection(
    FindingInspection<string> Lines,
    string? Text,
    SourceLinkResolver.TypeSourceInfo? Mapping,
    SourceDocumentObservation? Document,
    SourceChecksumVerification? ChecksumVerification)
{
    public bool IsComplete =>
        Lines.Value is FindingInspection<string>.Complete;

    public PdbTypeSourceOutcome Outcome { get; init; } =
        Lines.Value is FindingInspection<string>.Complete
            ? PdbTypeSourceOutcome.Complete
            : PdbTypeSourceOutcome.Unspecified;

    public bool? PortablePdbAvailable { get; init; }

    public PdbTypeSourceUnitScope? Scope { get; init; }
    public PdbTypeSourceMappingStrength? Strength { get; init; }
    public bool IsPartial { get; init; }
    public IReadOnlyList<PdbTypeSourceAdditionalDocument> AdditionalDocuments { get; init; } =
        Array.Empty<PdbTypeSourceAdditionalDocument>();
}

/// <summary>
/// Projects PDB acquisition, checksum, mapping, and decoded-text evidence into
/// the compatibility member and type source inspection models.
/// </summary>
public static class PdbSourceInspectionProjection
{
    internal const int MaxPdbSourceLineCount = 500_000;

    public static PdbMemberSourceInspection MemberAcquisitionFailed(
        FindingSubject subject,
        Exception error)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(error);
        return MemberFailed(
            subject,
            $"Portable PDB acquisition failed: {error.Message}",
            PdbMemberSourceOutcome.PortablePdbAcquisitionFailed);
    }

    public static PdbTypeSourceInspection TypeAcquisitionFailed(
        FindingSubject subject,
        Exception error)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(error);
        return TypeFailed(
            subject,
            $"Portable PDB acquisition failed: {error.Message}",
            PdbTypeSourceOutcome.PortablePdbAcquisitionFailed);
    }

    public static PdbMemberSourceInspection FromMemberContent(
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

        var verification = SourceLinkService.VerifyChecksum(document, content);
        if (verification == SourceChecksumVerification.Unavailable)
        {
            return MemberAbsent(
                "The portable PDB does not provide a usable source checksum.",
                PdbMemberSourceOutcome.ChecksumUnavailable,
                mapping,
                document,
                verification);
        }
        if (verification is SourceChecksumVerification.Unsupported
            or SourceChecksumVerification.Mismatch)
        {
            return MemberFailed(
                subject,
                verification switch
                {
                    SourceChecksumVerification.Unsupported =>
                        $"The source checksum algorithm '{document.ChecksumAlgorithm}' is unsupported.",
                    _ => "Fetched PDB source does not match the portable-PDB checksum.",
                },
                verification == SourceChecksumVerification.Unsupported
                    ? PdbMemberSourceOutcome.ChecksumUnsupported
                    : PdbMemberSourceOutcome.ChecksumMismatch,
                mapping,
                document,
                verification);
        }

        try
        {
            string sourceText = SourceLinkService.DecodeSourceText(content);
            string? memberText = MemberTextSlicer.ExtractMemberText(
                sourceText,
                mapping.StartLine,
                mapping.EndLine,
                methodName,
                mapping.SequencePointStartLines);
            if (memberText is null)
            {
                return MemberAbsent(
                    "The selected member's PDB source range does not identify one declaration that can be shown.",
                    PdbMemberSourceOutcome.NoVouchedDeclaration,
                    mapping,
                    document,
                    verification);
            }

            var lines = TextFindings.Inspect(memberText, subject).ToImmutableArray();
            return new PdbMemberSourceInspection(
                new FindingInspection<string>.Complete(lines),
                memberText,
                mapping,
                document,
                verification)
            {
                Outcome = PdbMemberSourceOutcome.Complete,
            };
        }
        catch (Exception ex) when (ex is CSharpTextComplexityException
            or TextFindingComplexityException)
        {
            return MemberFailed(
                subject,
                $"Could not extract the PDB member source: {ex.Message}",
                PdbMemberSourceOutcome.SourceTooComplex,
                mapping,
                document,
                verification);
        }
        catch (InvalidMemberTextCoordinatesException ex)
        {
            return MemberFailed(
                subject,
                $"Could not extract the PDB member source: {ex.Message}",
                PdbMemberSourceOutcome.InvalidSequencePointCoordinates,
                mapping,
                document,
                verification);
        }
        catch (Exception ex) when (ex is ArgumentException
            or IndexOutOfRangeException
            or InvalidOperationException)
        {
            return MemberFailed(
                subject,
                $"Could not extract the PDB member source: {ex.Message}",
                PdbMemberSourceOutcome.SourceExtractionFailed,
                mapping,
                document,
                verification);
        }
    }

    public static PdbTypeSourceInspection FromTypeContent(
        SourceLinkResolver.TypeSourceInfo mapping,
        SourceDocumentObservation document,
        byte[] content,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(subject);

        SourceChecksumVerification verification =
            SourceLinkService.VerifyChecksum(document, content);
        if (verification == SourceChecksumVerification.Unavailable)
        {
            return TypeAbsent(
                "The portable PDB does not provide a usable source checksum.",
                PdbTypeSourceOutcome.ChecksumUnavailable,
                mapping,
                document,
                verification);
        }
        if (verification is SourceChecksumVerification.Unsupported
            or SourceChecksumVerification.Mismatch)
        {
            return TypeFailed(
                subject,
                verification == SourceChecksumVerification.Unsupported
                    ? $"The source checksum algorithm '{document.ChecksumAlgorithm}' is unsupported."
                    : "Fetched PDB source does not match the portable-PDB checksum.",
                verification == SourceChecksumVerification.Unsupported
                    ? PdbTypeSourceOutcome.ChecksumUnsupported
                    : PdbTypeSourceOutcome.ChecksumMismatch,
                mapping,
                document,
                verification);
        }

        string text;
        try
        {
            text = SourceLinkService.DecodeSourceText(content);
        }
        catch (ArgumentException ex)
        {
            return TypeFailed(
                subject,
                $"Could not decode the PDB type source: {ex.Message}",
                PdbTypeSourceOutcome.SourceExtractionFailed,
                mapping,
                document,
                verification);
        }

        return FromVerifiedTypeContent(
            mapping,
            document,
            text,
            verification,
            subject);
    }

    public static PdbTypeSourceInspection FromVerifiedTypeContent(
        SourceLinkResolver.TypeSourceInfo mapping,
        SourceDocumentObservation document,
        string text,
        SourceChecksumVerification verification,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(subject);
        if (verification is not SourceChecksumVerification.Exact
            and not SourceChecksumVerification.LineEndingNormalized)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verification),
                "Verified type content requires an accepted checksum result.");
        }

        try
        {
            return new PdbTypeSourceInspection(
                new FindingInspection<string>.Complete(
                    TextFindings.Inspect(
                            text,
                            subject,
                            MaxPdbSourceLineCount)
                        .ToImmutableArray()),
                text,
                mapping,
                document,
                verification)
            {
                Outcome = PdbTypeSourceOutcome.Complete,
            };
        }
        catch (TextFindingComplexityException ex)
        {
            return TypeFailed(
                subject,
                $"Could not decode the PDB type source: {ex.Message}",
                PdbTypeSourceOutcome.SourceTooComplex,
                mapping,
                document,
                verification);
        }
    }

    internal static PdbMemberSourceInspection MemberAbsent(
        string detail,
        PdbMemberSourceOutcome outcome)
        => new(
            new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput,
                detail),
            Text: null,
            Mapping: null,
            Document: null,
            ChecksumVerification: null)
        {
            Outcome = outcome,
        };

    internal static PdbMemberSourceInspection MemberAbsent(
        string detail,
        PdbMemberSourceOutcome outcome,
        MemberSourceObservation mapping,
        SourceDocumentObservation document,
        SourceChecksumVerification verification)
        => new(
            new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput,
                detail),
            Text: null,
            mapping,
            document,
            verification)
        {
            Outcome = outcome,
        };

    internal static PdbMemberSourceInspection MemberFailed(
        InspectionError error,
        PdbMemberSourceOutcome outcome)
        => new(
            new FindingInspection<string>.Failed(error),
            Text: null,
            Mapping: null,
            Document: null,
            ChecksumVerification: null)
        {
            Outcome = outcome,
        };

    internal static PdbMemberSourceInspection MemberFailed(
        FindingSubject subject,
        string reason,
        PdbMemberSourceOutcome outcome,
        MemberSourceObservation? mapping = null,
        SourceDocumentObservation? document = null,
        SourceChecksumVerification? verification = null)
        => new(
            new FindingInspection<string>.Failed(
                new InspectionError(subject, TextFindings.LineDescriptor, reason)),
            Text: null,
            mapping,
            document,
            verification)
        {
            Outcome = outcome,
        };

    internal static PdbTypeSourceInspection TypeAbsent(
        string detail,
        PdbTypeSourceOutcome outcome,
        SourceLinkResolver.TypeSourceInfo? mapping = null,
        SourceDocumentObservation? document = null,
        SourceChecksumVerification? verification = null)
        => new(
            new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput,
                detail),
            Text: null,
            mapping,
            document,
            verification)
        {
            Outcome = outcome,
        };

    internal static PdbTypeSourceInspection TypeFailed(
        InspectionError error,
        PdbTypeSourceOutcome outcome,
        SourceLinkResolver.TypeSourceInfo? mapping = null)
        => new(
            new FindingInspection<string>.Failed(error),
            Text: null,
            mapping,
            Document: null,
            ChecksumVerification: null)
        {
            Outcome = outcome,
        };

    internal static PdbTypeSourceInspection TypeFailed(
        FindingSubject subject,
        string reason,
        PdbTypeSourceOutcome outcome,
        SourceLinkResolver.TypeSourceInfo? mapping = null,
        SourceDocumentObservation? document = null,
        SourceChecksumVerification? verification = null)
        => new(
            new FindingInspection<string>.Failed(
                new InspectionError(
                    subject,
                    TextFindings.LineDescriptor,
                    reason)),
            Text: null,
            mapping,
            document,
            verification)
        {
            Outcome = outcome,
        };
}
