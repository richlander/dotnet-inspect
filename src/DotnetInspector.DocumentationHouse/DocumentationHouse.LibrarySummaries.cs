using System.Xml;

using CSharpText;
using DotnetInspector.Libraries;

namespace DotnetInspector.DocumentationHouse;

public static partial class DocumentationHouse
{
    /// <summary>
    /// Reads every compiled XML summary of the Library the borrowed lease
    /// names, in one bounded scan of its selected companion
    /// (docs/design/documentation-house.md#whole-library-compiled-summaries).
    /// The caller keeps ownership of the lease. Contributions must be the
    /// Library-level selection evidence for that Library: they name no
    /// subject, and their Library and API content are the lease's.
    /// </summary>
    public static DocumentationLibrarySummaryOutcome ReadLibrarySummaries(
        DocumentationLibrarySummaryRequest request,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operationLease);
        cancellationToken.ThrowIfCancellationRequested();

        if (DateTimeOffset.UtcNow >= request.Deadline)
        {
            return new DocumentationLibrarySummaryOutcome.Incomplete(
                DocumentationIncompleteBoundary.Deadline);
        }
        IReadOnlyList<CompiledXmlContribution> contributions =
            request.Contributions;
        if (contributions.Count
            > request.Limits.MaximumCompiledXmlContributions)
        {
            return new DocumentationLibrarySummaryOutcome.Incomplete(
                DocumentationIncompleteBoundary.ContributionLimit);
        }

        LibraryReference library = operationLease.Reference;
        var rejections = new List<DocumentationCompiledXmlRejection>();
        foreach (CompiledXmlContribution contribution in contributions)
        {
            DocumentationCompiledXmlRejectionKind? rejection =
                contribution.Subject is not null
                    ? DocumentationCompiledXmlRejectionKind.SubjectMismatch
                    : !ReferenceEquals(contribution.Library, library)
                        ? DocumentationCompiledXmlRejectionKind.LibraryMismatch
                        : !ReferenceEquals(
                            contribution.ApiContent,
                            library.ApiAssembly)
                            ? DocumentationCompiledXmlRejectionKind
                                .ApiContentMismatch
                            : ValidateCompanion(contribution);
            if (rejection is { } kind)
                rejections.Add(new(contribution, kind));
        }
        if (rejections.Count > 0)
        {
            return new DocumentationLibrarySummaryOutcome.Rejected(
                rejections.AsReadOnly());
        }

        if (contributions.Any(static contribution =>
                contribution.Kind == CompiledXmlContributionKind.Partial))
        {
            return new DocumentationLibrarySummaryOutcome.Incomplete(
                DocumentationIncompleteBoundary.CompanionSelectionPartial);
        }

        CompiledXmlContribution[] candidates =
        [
            .. contributions.Where(static contribution =>
                contribution.Kind == CompiledXmlContributionKind.Candidate),
        ];
        if (candidates.Length == 0)
        {
            return contributions.Any(static contribution =>
                    contribution.Kind == CompiledXmlContributionKind.Absent)
                ? new DocumentationLibrarySummaryOutcome.Absent(contributions)
                : new DocumentationLibrarySummaryOutcome.Unavailable(
                    contributions);
        }

        if (SelectCandidate(candidates) is not { } selected)
        {
            return new DocumentationLibrarySummaryOutcome.Ambiguous(
                Array.AsReadOnly(candidates));
        }

        (CompiledXmlReadStatus? stopped, XmlSnapshot? snapshot) =
            SnapshotCompiledXml(
                selected,
                operationLease,
                request.Limits,
                request.Deadline,
                cancellationToken);
        switch (stopped)
        {
            case null:
                break;
            case CompiledXmlReadStatus.ContentAccessFailed:
                return new DocumentationLibrarySummaryOutcome.Failed(
                    DocumentationCompiledXmlFailureKind.ContentAccessFailed);
            case CompiledXmlReadStatus.ByteLimit:
                return new DocumentationLibrarySummaryOutcome.Incomplete(
                    DocumentationIncompleteBoundary.CompiledXmlByteLimit);
            default:
                return new DocumentationLibrarySummaryOutcome.Incomplete(
                    DocumentationIncompleteBoundary.Deadline);
        }

        IReadOnlyDictionary<string, string> summaries;
        try
        {
            using var stream = new MemoryStream(
                snapshot!.Bytes!,
                writable: false);
            summaries = XmlDocumentationSummaryReader.ReadSummaries(
                stream,
                request.Limits.XmlReadLimits);
        }
        catch (Exception failure) when (
            failure is XmlException or IOException)
        {
            return new DocumentationLibrarySummaryOutcome.Failed(
                DocumentationCompiledXmlFailureKind
                    .MalformedOrUnreadableDocument);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (DateTimeOffset.UtcNow >= request.Deadline)
        {
            return new DocumentationLibrarySummaryOutcome.Incomplete(
                DocumentationIncompleteBoundary.Deadline);
        }
        return new DocumentationLibrarySummaryOutcome.Available(
            selected,
            summaries,
            snapshot.Length);
    }
}
