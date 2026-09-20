using DotnetInspector.SourceHouse;
using ILInspector.Decompiler;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Result of decompiling one exact member without attempting authored source.
/// </summary>
public abstract record AssemblyMemberDecompilationEntry(
    AssemblyContextSubject Subject,
    AssemblyMemberSourceRequest Request)
{
    public sealed record Settled(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        CSharpDecompilationAttempt Attempt,
        SourceHouseDecompilationOutcome HouseOutcome)
        : AssemblyMemberDecompilationEntry(Subject, Request);

    public sealed record Rejected(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        CandidateOpenFailure Failure)
        : AssemblyMemberDecompilationEntry(Subject, Request);

    public sealed record Unavailable(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        AssemblySourceFailure Failure)
        : AssemblyMemberDecompilationEntry(Subject, Request)
    {
        public AssemblyContextLibraryAdapterResult.Terminal? LibraryFailure
        {
            get;
            init;
        }
    }
}

public static partial class AssemblyContextSourceQuery
{
    public static InspectionQuery<AssemblyMemberDecompilationEntry>
        MemberDecompilation
    { get; } =
        new(
            "Assembly context member decompilation",
            InspectionCost.Moderated);

    public static async Task<AssemblyMemberDecompilationEntry>
        ExecuteMemberDecompilationAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyMemberSourceRequest request,
            AssemblyContextSourceQueryContext context,
            AssemblyContextLibraryPortablePdb? portablePdb = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var subject = new AssemblyContextSubject(participant.Assembly);
        AssemblyBindingPolicyVersion bindingPolicyVersion =
            group.BindingPolicyVersion;
        AssemblyImageAccessResult<MemberInspectionSeed> access;
        try
        {
            access = group.UseAssemblySession(
                participant,
                cancellationToken,
                (session, retained) => new MemberInspectionSeed(
                    retained,
                    ResolveMember(session, request)));
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
        }
        catch (Exception ex) when (IsInspectionFailure(ex))
        {
            return new AssemblyMemberDecompilationEntry.Unavailable(
                subject,
                request,
                InspectionFailure(ex));
        }

        if (access
            is AssemblyImageAccessResult<
                MemberInspectionSeed>.Rejected rejected)
        {
            return new AssemblyMemberDecompilationEntry.Rejected(
                subject,
                request,
                rejected.Failure);
        }
        if (access
            is not AssemblyImageAccessResult<
                MemberInspectionSeed>.Available available)
        {
            throw new InvalidOperationException(
                "Unknown assembly image access result.");
        }
        if (available.Value.Target is null)
        {
            return new AssemblyMemberDecompilationEntry.Unavailable(
                subject,
                request,
                TargetNotFound(
                    "The selected participant does not declare the requested method."));
        }

        try
        {
            SourceHouseDecompilationLimits limits =
                context.MemberDecompilationLimits;
            AssemblyContextLibraryAdapterResult admission =
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.Implementation,
                    new AssemblyContextLibraryMaterializationLimits(
                        Math.Max(
                            1,
                            Math.Max(
                                limits.MaximumAssemblyBytes,
                                limits.MaximumPortablePdbBytes)),
                        Math.Max(
                            1,
                            Math.Min(
                                int.MaxValue,
                                (long)limits.MaximumAssemblyBytes
                                    + limits.MaximumPortablePdbBytes))),
                    portablePdb,
                    cancellationToken)
                .ConfigureAwait(false);
            if (admission
                is AssemblyContextLibraryAdapterResult.Terminal terminal)
            {
                ThrowCleanupFailures(terminal.CleanupFailures);
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBindingPolicyVersion(
                    participant,
                    bindingPolicyVersion);
                return new AssemblyMemberDecompilationEntry.Unavailable(
                    subject,
                    request,
                    LibraryAdmissionUnavailable(terminal))
                {
                    LibraryFailure = terminal,
                };
            }
            if (admission
                is not AssemblyContextLibraryAdapterResult.Completed completed)
            {
                throw new InvalidOperationException(
                    "Unknown Library admission result.");
            }

            Exception? primaryFailure = null;
            AssemblyMemberDecompilationEntry.Settled settled;
            try
            {
                (CSharpDecompilationAttempt attempt,
                    SourceHouseDecompilationOutcome houseOutcome) =
                        await DecompileAsync(
                            participant,
                            new SourceHouseTarget.MemberTarget(
                                request.Type,
                                request.Member,
                                request.MetadataToken),
                            request.PrinterOptions,
                            completed,
                            bindingPolicyVersion,
                            limits,
                            context,
                            "member-decompilation",
                            cancellationToken)
                        .ConfigureAwait(false);
                settled = new AssemblyMemberDecompilationEntry.Settled(
                    subject,
                    request,
                    attempt,
                    houseOutcome);
            }
            catch (Exception failure)
            {
                primaryFailure = failure;
                throw;
            }
            finally
            {
                await RetireSourceHouseLibraryAsync(
                        completed,
                        primaryFailure)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
            return settled;
        }
        catch (Exception ex) when (IsInspectionFailure(ex))
        {
            return new AssemblyMemberDecompilationEntry.Unavailable(
                subject,
                request,
                InspectionFailure(ex));
        }
    }
}
