using System.Collections.Immutable;

using DotnetInspector.Libraries;

namespace DotnetInspector.Sections;

public sealed record MemberDocumentAttachmentCompositionRequest
{
    public MemberDocumentAttachmentCompositionRequest(
        LibraryReference library,
        MemberDocumentResolutionPlan plan,
        MemberDocumentationAttachmentRequest documentation)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
        Documentation = documentation
            ?? throw new ArgumentNullException(nameof(documentation));
    }

    public LibraryReference Library { get; }
    public MemberDocumentResolutionPlan Plan { get; }
    public MemberDocumentationAttachmentRequest Documentation { get; }
}

public sealed record MemberDocumentAttachmentCompositionResult
{
    public MemberDocumentAttachmentCompositionResult(
        MemberDocumentResolutionOutcome resolution,
        MemberDocumentationAttachmentOutcome? documentation)
    {
        Resolution = resolution
            ?? throw new ArgumentNullException(nameof(resolution));
        ImmutableArray<MemberSubject> subjects = Resolution switch
        {
            MemberDocumentResolutionOutcome.Exact exact =>
                [exact.Document.Subject],
            MemberDocumentResolutionOutcome.Overview overview =>
                [
                    .. overview.Document.Members.Select(
                        static member => member.Subject),
                ],
            _ => default,
        };
        if (subjects.IsDefault)
        {
            if (documentation is not null)
            {
                throw new ArgumentException(
                    "Member documentation cannot settle when final Member resolution did not complete.",
                    nameof(documentation));
            }
        }
        else
        {
            ArgumentNullException.ThrowIfNull(documentation);
            if (documentation
                    is MemberDocumentationAttachmentOutcome.Available
                        available
                && !subjects.SequenceEqual(
                    available.Attachments.Select(
                        static attachment => attachment.Subject)))
            {
                throw new ArgumentException(
                    "Member documentation attachments must correspond in order to every declaration in the resolved final document.",
                    nameof(documentation));
            }
        }

        Documentation = documentation;
    }

    public MemberDocumentResolutionOutcome Resolution { get; }
    public MemberDocumentationAttachmentOutcome? Documentation { get; }
}

public static class MemberDocumentAttachmentCompositionOperation
{
    public static async ValueTask<
        InspectionEnvelope<MemberDocumentAttachmentCompositionResult>>
        ExecuteAsync(
            MemberDocumentAttachmentCompositionRequest request,
            LibraryOperationLease lease,
            MemberDocumentationAttachmentProvider documentationProvider,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(documentationProvider);

        InspectionEnvelope<MemberDocumentResolutionOutcome> inspection =
            MemberDocumentResolutionOperation.Execute(
                new(request.Library, request.Plan),
                lease,
                cancellationToken);
        IReadOnlyList<MemberSubject>? subjects =
            inspection.Content switch
            {
                MemberDocumentResolutionOutcome.Exact exact =>
                    [
                        exact.Document.Subject,
                    ],
                MemberDocumentResolutionOutcome.Overview overview =>
                    [
                        .. overview.Document.Members.Select(
                            static member => member.Subject),
                    ],
                _ => null,
            };
        MemberDocumentationAttachmentOutcome? documentation =
            subjects is null
                ? null
                : await MemberDocumentationAttachmentOperation.ExecuteAsync(
                        subjects,
                        request.Documentation,
                        documentationProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
        return new(
            new(inspection.Content, documentation),
            inspection.Share,
            inspection.Diagnostics);
    }
}
