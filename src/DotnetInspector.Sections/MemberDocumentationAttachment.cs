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

public enum MemberDocumentationAttachmentFailure
{
    ResultSetMismatch,
    SubjectMismatch,
    ProviderFailed,
}

public abstract record MemberDocumentationAttachmentOutcome
{
    private MemberDocumentationAttachmentOutcome()
    {
    }

    public sealed record Available
        : MemberDocumentationAttachmentOutcome
    {
        public Available(
            ImmutableArray<MemberDocumentationAttachment> attachments)
        {
            if (attachments.IsDefault)
            {
                throw new ArgumentException(
                    "Available Member documentation attachments must be initialized.",
                    nameof(attachments));
            }

            Attachments = attachments;
        }

        public ImmutableArray<MemberDocumentationAttachment> Attachments
        {
            get;
        }
    }

    public sealed record Failed(
        MemberDocumentationAttachmentFailure Reason,
        string? Detail = null)
        : MemberDocumentationAttachmentOutcome;
}

internal static class MemberDocumentationAttachmentOperation
{
    internal static async ValueTask<MemberDocumentationAttachmentOutcome>
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
            return new MemberDocumentationAttachmentOutcome.Available([]);
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
            return new MemberDocumentationAttachmentOutcome.Failed(
                MemberDocumentationAttachmentFailure.ResultSetMismatch);
        }

        IReadOnlyDictionary<string, DocumentationQueryOutcome> outcomes;
        try
        {
            outcomes =
                await provider(
                        documentationIds,
                        request.Demand,
                        cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
        {
            return new MemberDocumentationAttachmentOutcome.Failed(
                MemberDocumentationAttachmentFailure.ProviderFailed,
                exception.Message);
        }
        if (outcomes.Count != documentationIds.Length)
        {
            return new MemberDocumentationAttachmentOutcome.Failed(
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
                return new MemberDocumentationAttachmentOutcome.Failed(
                    MemberDocumentationAttachmentFailure.ResultSetMismatch);
            }
            if (outcome.Subject
                != DocumentationSubject(subject, documentationId))
            {
                return new MemberDocumentationAttachmentOutcome.Failed(
                    MemberDocumentationAttachmentFailure.SubjectMismatch);
            }

            attachments.Add(new(subject, outcome));
        }

        return new MemberDocumentationAttachmentOutcome.Available(
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
