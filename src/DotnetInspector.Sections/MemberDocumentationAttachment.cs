using System.Collections.Immutable;

using DotnetInspector.DocumentationHouse;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public sealed record MemberDocumentationAttachmentRequest
{
    public MemberDocumentationAttachmentRequest(
        DocumentationDemand demand)
    {
        if (!Enum.IsDefined(demand))
            throw new ArgumentOutOfRangeException(nameof(demand));

        Demand = demand;
    }

    public DocumentationDemand Demand { get; }
}

public sealed record MemberDocumentationAttachment(
    MemberSubject Subject,
    DocumentationQueryOutcome Outcome);

public delegate ValueTask<
    IReadOnlyDictionary<string, DocumentationQueryOutcome>>
    MemberDocumentationAttachmentProvider(
        IReadOnlyCollection<string> documentationIds,
        DocumentationDemand demand,
        CancellationToken cancellationToken);

internal enum MemberDocumentationAttachmentFailure
{
    ResultSetMismatch,
    SubjectMismatch,
}

internal abstract record MemberDocumentationAttachmentResult
{
    private MemberDocumentationAttachmentResult()
    {
    }

    internal sealed record Attached(
        ImmutableArray<MemberDocumentationAttachment> Attachments)
        : MemberDocumentationAttachmentResult;

    internal sealed record Failed(
        MemberDocumentationAttachmentFailure Reason)
        : MemberDocumentationAttachmentResult;
}

internal static class MemberDocumentationAttachmentOperation
{
    internal static async ValueTask<MemberDocumentationAttachmentResult>
        ExecuteAsync(
            IReadOnlyList<MemberSubject> subjects,
            MemberDocumentationAttachmentRequest request,
            MemberDocumentationAttachmentProvider provider,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);

        if (subjects.Count == 0)
        {
            return new MemberDocumentationAttachmentResult.Attached([]);
        }

        string[] documentationIds =
        [
            .. subjects.Select(static subject =>
                subject.DocumentationId.ToString()),
        ];
        if (documentationIds
                .Distinct(StringComparer.Ordinal)
                .Count() != documentationIds.Length)
        {
            return new MemberDocumentationAttachmentResult.Failed(
                MemberDocumentationAttachmentFailure.ResultSetMismatch);
        }

        IReadOnlyDictionary<string, DocumentationQueryOutcome> outcomes =
            await provider(
                    documentationIds,
                    request.Demand,
                    cancellationToken)
                .ConfigureAwait(false);
        if (outcomes.Count != documentationIds.Length)
        {
            return new MemberDocumentationAttachmentResult.Failed(
                MemberDocumentationAttachmentFailure.ResultSetMismatch);
        }

        var attachments =
            ImmutableArray.CreateBuilder<MemberDocumentationAttachment>(
                subjects.Count);
        foreach (MemberSubject subject in subjects)
        {
            string documentationId = subject.DocumentationId.ToString();
            if (!outcomes.TryGetValue(
                    documentationId,
                    out DocumentationQueryOutcome? outcome))
            {
                return new MemberDocumentationAttachmentResult.Failed(
                    MemberDocumentationAttachmentFailure.ResultSetMismatch);
            }
            if (outcome.Subject
                != DocumentationSubject(subject, documentationId))
            {
                return new MemberDocumentationAttachmentResult.Failed(
                    MemberDocumentationAttachmentFailure.SubjectMismatch);
            }

            attachments.Add(new(subject, outcome));
        }

        return new MemberDocumentationAttachmentResult.Attached(
            attachments.MoveToImmutable());
    }

    private static CompiledDocumentationSubject DocumentationSubject(
        MemberSubject subject,
        string documentationId)
    {
        LibraryAssemblyIdentity assembly = subject.Population.Assembly;
        return new(
            new(
                assembly.Name.ToString(),
                assembly.Version.ToString(),
                assembly.Culture?.ToString(),
                assembly.PublicKeyToken?.ToString()),
            documentationId);
    }
}
