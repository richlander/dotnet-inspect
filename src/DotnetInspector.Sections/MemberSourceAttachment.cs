using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public enum MemberSourceAttachmentDemand
{
    Source,
    SourceWithAuthoredParts,
}

public sealed record MemberSourceAttachmentRequest
{
    public MemberSourceAttachmentRequest(
        MemberSourceAttachmentDemand demand =
            MemberSourceAttachmentDemand.Source,
        bool allowDecompiledFallback = true)
    {
        if (!Enum.IsDefined(demand))
            throw new ArgumentOutOfRangeException(nameof(demand));

        Demand = demand;
        AllowDecompiledFallback = allowDecompiledFallback;
    }

    public MemberSourceAttachmentDemand Demand { get; }
    public bool AllowDecompiledFallback { get; }
}

public sealed record MemberSourceAttachment(
    MemberSubject Subject,
    AssemblyMemberSourceEntry Outcome);

public delegate ValueTask<MemberSourceAttachment>
    MemberSourceAttachmentProvider(
        MemberSubject subject,
        MemberSourceAttachmentRequest request,
        CancellationToken cancellationToken);

internal enum MemberSourceAttachmentFailure
{
    SubjectMismatch,
}

internal abstract record MemberSourceAttachmentResult
{
    private MemberSourceAttachmentResult()
    {
    }

    internal sealed record Attached(MemberSourceAttachment Attachment)
        : MemberSourceAttachmentResult;

    internal sealed record Failed(MemberSourceAttachmentFailure Reason)
        : MemberSourceAttachmentResult;
}

internal static class MemberSourceAttachmentOperation
{
    internal static async ValueTask<MemberSourceAttachmentResult>
        ExecuteAsync(
            MemberSubject subject,
            MemberSourceAttachmentRequest request,
            MemberSourceAttachmentProvider provider,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);

        MemberSourceAttachment attachment =
            await provider(subject, request, cancellationToken)
                .ConfigureAwait(false);
        AssemblyMemberSourceRequest outcomeRequest =
            attachment.Outcome.Request;
        bool sourceRequestMatches =
            outcomeRequest.Type == subject.Group.DeclaringType
            && outcomeRequest.Member == subject.Anchor
            && outcomeRequest.MetadataToken == subject.MetadataToken
            && outcomeRequest.IncludeAuthoredParts
                == (request.Demand
                    is MemberSourceAttachmentDemand
                        .SourceWithAuthoredParts)
            && outcomeRequest.AllowDecompiledFallback
                == request.AllowDecompiledFallback;
        return attachment.Subject == subject
            && sourceRequestMatches
            ? new MemberSourceAttachmentResult.Attached(attachment)
            : new MemberSourceAttachmentResult.Failed(
                MemberSourceAttachmentFailure.SubjectMismatch);
    }
}
