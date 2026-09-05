using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

public sealed record AssemblyMemberSourcePairRequest
{
    public AssemblyMemberSourcePairRequest(
        MetadataTypeDefinitionName type,
        MemberAnchor member)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        Type = type;
        Member = member;
    }

    public MetadataTypeDefinitionName Type { get; }
    public MemberAnchor Member { get; }

    public static AssemblyMemberSourcePairRequest From(
        ApiType type,
        ApiMember member)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        return new(
            AssemblyTypeSourceRequest.GetDefinitionName(type),
            ApiMemberIdentity.GetMemberAnchor(type, member));
    }
}

public abstract record AssemblyMemberSourcePairEndpoint(
    AssemblyContextSubject Subject)
{
    public sealed record Resolved(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        AssemblyMemberPdbSourceAttempt Source)
        : AssemblyMemberSourcePairEndpoint(Subject);

    public sealed record NotFound(
        AssemblyContextSubject Subject,
        AssemblySourceFailure Failure)
        : AssemblyMemberSourcePairEndpoint(Subject);

    public sealed record Rejected(
        AssemblyContextSubject Subject,
        CandidateOpenFailure Failure)
        : AssemblyMemberSourcePairEndpoint(Subject);

    public sealed record Failed(
        AssemblyContextSubject Subject,
        AssemblySourceFailure Failure)
        : AssemblyMemberSourcePairEndpoint(Subject);
}

public enum AssemblyMemberSourcePairStatus
{
    Compared,
    Unavailable,
    Failed,
}

public sealed class AssemblyMemberSourcePairResult
{
    internal AssemblyMemberSourcePairResult(
        AssemblyMemberSourcePairRequest request,
        AssemblyMemberSourcePairEndpoint before,
        AssemblyMemberSourcePairEndpoint after,
        AssemblyMemberSourcePairStatus status,
        FindingComparison<string>? comparison = null,
        AssemblySourceFailure? failure = null)
    {
        Request = request;
        Before = before;
        After = after;
        Status = status;
        Comparison = comparison;
        Failure = failure;
    }

    public AssemblyMemberSourcePairRequest Request { get; }
    public AssemblyMemberSourcePairEndpoint Before { get; }
    public AssemblyMemberSourcePairEndpoint After { get; }
    public AssemblyMemberSourcePairStatus Status { get; }
    public FindingComparison<string>? Comparison { get; }
    public AssemblySourceFailure? Failure { get; }
    public bool IsExact =>
        Status == AssemblyMemberSourcePairStatus.Compared
        && Comparison is { IsExact: true };
}

/// <summary>
/// Compares verified PDB declarations for one metadata member selected in
/// two retained images, without running decompilation or IL comparison.
/// </summary>
public static class AssemblyContextMemberSourcePairQuery
{
    public static InspectionQuery<AssemblyMemberSourcePairResult> Definition
    { get; } = new(
        "Assembly context member source pair",
        InspectionCost.Moderated);

    public static async Task<AssemblyMemberSourcePairResult> ExecuteAsync(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant beforeParticipant,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant afterParticipant,
        AssemblyMemberSourcePairRequest request,
        AssemblyContextSourceQueryContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeGroup);
        ArgumentNullException.ThrowIfNull(beforeParticipant);
        ArgumentNullException.ThrowIfNull(afterGroup);
        ArgumentNullException.ThrowIfNull(afterParticipant);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        AssemblyBindingPolicyVersion beforeVersion =
            beforeGroup.BindingPolicyVersion;
        AssemblyBindingPolicyVersion afterVersion =
            afterGroup.BindingPolicyVersion;

        AssemblyMemberSourcePairEndpoint before =
            await AcquireEndpointAsync(
                beforeGroup,
                beforeParticipant,
                request,
                context,
                beforeVersion,
                cancellationToken).ConfigureAwait(false);
        AssemblyMemberSourcePairEndpoint after =
            await AcquireEndpointAsync(
                afterGroup,
                afterParticipant,
                request,
                context,
                afterVersion,
                cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyContextSourceQuery.EnsureBindingPolicyVersion(
                beforeParticipant,
                beforeVersion);
            AssemblyContextSourceQuery.EnsureBindingPolicyVersion(
                afterParticipant,
                afterVersion);

            if (before is AssemblyMemberSourcePairEndpoint.Failed beforeFailed)
            {
                return Failed(beforeFailed.Failure);
            }
            if (after is AssemblyMemberSourcePairEndpoint.Failed afterFailed)
            {
                return Failed(afterFailed.Failure);
            }

            if (before is not AssemblyMemberSourcePairEndpoint.Resolved
                {
                    Source: AssemblyMemberPdbSourceAttempt.Available beforeSource,
                }
                || after is not AssemblyMemberSourcePairEndpoint.Resolved
                {
                    Source: AssemblyMemberPdbSourceAttempt.Available afterSource,
                })
            {
                return new(
                    request,
                    before,
                    after,
                    AssemblyMemberSourcePairStatus.Unavailable);
            }

            FindingComparison<string> comparison = FindingComparison.Compare(
                beforeSource.Inspection.Lines,
                afterSource.Inspection.Lines);
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyContextSourceQuery.EnsureBindingPolicyVersion(
                beforeParticipant,
                beforeVersion);
            AssemblyContextSourceQuery.EnsureBindingPolicyVersion(
                afterParticipant,
                afterVersion);
            return new(
                request,
                before,
                after,
                comparison is FindingComparison<string>.Failed
                    ? AssemblyMemberSourcePairStatus.Failed
                    : AssemblyMemberSourcePairStatus.Compared,
                comparison);
        }
        catch (Exception ex) when (AssemblyContextSourceQuery.IsInspectionFailure(ex))
        {
            return Failed(AssemblyContextSourceQuery.InspectionFailure(ex));
        }

        AssemblyMemberSourcePairResult Failed(AssemblySourceFailure failure)
            => new(
                request,
                before,
                after,
                AssemblyMemberSourcePairStatus.Failed,
                failure: failure);
    }

    static async Task<AssemblyMemberSourcePairEndpoint> AcquireEndpointAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyMemberSourcePairRequest request,
        AssemblyContextSourceQueryContext context,
        AssemblyBindingPolicyVersion version,
        CancellationToken cancellationToken)
    {
        var subject = new AssemblyContextSubject(participant.Assembly);
        try
        {
            AssemblyImageAccessResult<MemberSeed> access =
                group.UseAssemblySession(
                    participant,
                    cancellationToken,
                    (session, retained) => new MemberSeed(
                        retained,
                        AssemblyContextSourceQuery.ResolveMember(
                            session,
                            request.Type,
                            request.Member)));
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyContextSourceQuery.EnsureBindingPolicyVersion(
                participant,
                version);

            if (access is AssemblyImageAccessResult<MemberSeed>.Rejected rejected)
            {
                return new AssemblyMemberSourcePairEndpoint.Rejected(
                    subject,
                    rejected.Failure);
            }
            if (access is not AssemblyImageAccessResult<MemberSeed>.Available available)
                throw new InvalidOperationException("Unknown assembly image access result.");
            if (available.Value.Target is not { } target)
            {
                return new AssemblyMemberSourcePairEndpoint.NotFound(
                    subject,
                    new(
                        AssemblySourceFailureKind.TargetNotFound,
                        "The selected participant does not declare one exact MethodDef for the requested member."));
            }

            AssemblyMemberSourceRequest exactRequest =
                AssemblyMemberSourceRequest.From(target.Type, target.Member);
            AssemblyContextSourceQuery.MemberPdbInspection pdb =
                await AssemblyContextSourceQuery.InspectMemberPdbAsync(
                    participant,
                    exactRequest,
                    context,
                    available.Value.Retained,
                    version,
                    cancellationToken).ConfigureAwait(false);
            AssemblyMemberPdbSourceAttempt source =
                pdb.Inspection.IsComplete
                && pdb.Inspection.Text is not null
                && pdb.Provenance is { } provenance
                    ? new AssemblyMemberPdbSourceAttempt.Available(
                        pdb.Inspection,
                        provenance)
                    : new AssemblyMemberPdbSourceAttempt.Unavailable(
                        pdb.Inspection);
            return new AssemblyMemberSourcePairEndpoint.Resolved(
                subject,
                exactRequest,
                source);
        }
        catch (Exception ex) when (AssemblyContextSourceQuery.IsInspectionFailure(ex))
        {
            return new AssemblyMemberSourcePairEndpoint.Failed(
                subject,
                AssemblyContextSourceQuery.InspectionFailure(ex));
        }
    }

    sealed record MemberSeed(
        ResolvedAssemblyReference Retained,
        (ApiType Type, ApiMember Member)? Target);
}
