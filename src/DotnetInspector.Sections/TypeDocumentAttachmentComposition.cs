using DotnetInspector.Libraries;

namespace DotnetInspector.Sections;

public sealed record TypeDocumentAttachmentCompositionRequest
{
    public TypeDocumentAttachmentCompositionRequest(
        LibraryReference library,
        TypeDocumentInspectionPlan plan,
        MemberDocumentationAttachmentRequest documentation)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Documentation = documentation
            ?? throw new ArgumentNullException(nameof(documentation));
    }

    public LibraryReference Library { get; }
    public TypeDocumentInspectionPlan Plan { get; }
    public MemberDocumentationAttachmentRequest Documentation { get; }
}

public sealed record TypeDocumentAttachmentCompositionResult
{
    public TypeDocumentAttachmentCompositionResult(
        TypeDocumentInspectionOutcome inspection,
        MemberDocumentationAttachmentOutcome? documentation)
    {
        Inspection = inspection
            ?? throw new ArgumentNullException(nameof(inspection));
        if (inspection
            is TypeDocumentInspectionOutcome.Available available)
        {
            ArgumentNullException.ThrowIfNull(documentation);
            if (documentation
                    is MemberDocumentationAttachmentOutcome.Available attachments
                && !available.Document.Members
                    .Select(static member => member.Subject)
                    .SequenceEqual(attachments.Attachments.Select(
                        static attachment => attachment.Subject)))
            {
                throw new ArgumentException(
                    "Member documentation attachments must correspond in order to every declaration in the Type document.",
                    nameof(documentation));
            }
        }
        else if (documentation is not null)
        {
            throw new ArgumentException(
                "Member documentation cannot settle when Type inspection did not complete.",
                nameof(documentation));
        }

        Documentation = documentation;
    }

    public TypeDocumentInspectionOutcome Inspection { get; }
    public MemberDocumentationAttachmentOutcome? Documentation { get; }
}

public static class TypeDocumentAttachmentCompositionOperation
{
    public static async ValueTask<
        InspectionEnvelope<TypeDocumentAttachmentCompositionResult>>
        ExecuteAsync(
            TypeDocumentAttachmentCompositionRequest request,
            LibraryOperationLease lease,
            MemberDocumentationAttachmentProvider documentationProvider,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(documentationProvider);

        InspectionEnvelope<TypeDocumentInspectionOutcome> inspection =
            TypeDocumentInspectionOperation.Execute(
                new(request.Library, request.Plan),
                lease,
                cancellationToken);
        MemberDocumentationAttachmentOutcome? documentation =
            inspection.Content
                is TypeDocumentInspectionOutcome.Available available
                ? await MemberDocumentationAttachmentOperation.ExecuteAsync(
                    [
                        .. available.Document.Members.Select(
                            static member => member.Subject),
                    ],
                    request.Documentation,
                    documentationProvider,
                    cancellationToken).ConfigureAwait(false)
                : null;
        return new(
            new(inspection.Content, documentation),
            inspection.Share,
            inspection.Diagnostics);
    }
}
