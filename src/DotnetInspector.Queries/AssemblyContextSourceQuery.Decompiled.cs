using DotnetInspector.SourceHouse;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
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

/// <summary>
/// Result of decompiling one exact type without attempting authored source.
/// </summary>
public abstract record AssemblyTypeDecompilationEntry(
    AssemblyContextSubject Subject,
    AssemblyTypeSourceRequest Request)
{
    public sealed record Settled(
        AssemblyContextSubject Subject,
        AssemblyTypeSourceRequest Request,
        CSharpDecompilationAttempt Attempt,
        SourceHouseDecompilationOutcome HouseOutcome)
        : AssemblyTypeDecompilationEntry(Subject, Request);

    public sealed record Rejected(
        AssemblyContextSubject Subject,
        AssemblyTypeSourceRequest Request,
        CandidateOpenFailure Failure)
        : AssemblyTypeDecompilationEntry(Subject, Request);

    public sealed record Unavailable(
        AssemblyContextSubject Subject,
        AssemblyTypeSourceRequest Request,
        AssemblySourceFailure Failure)
        : AssemblyTypeDecompilationEntry(Subject, Request)
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

    public static InspectionQuery<AssemblyTypeDecompilationEntry>
        TypeDecompilation
    { get; } =
        new(
            "Assembly context type decompilation",
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
            DecompilationOperationResult operation =
                await ExecuteDecompilationOperationAsync(
                        group,
                        participant,
                        new SourceHouseTarget.MemberTarget(
                            request.Type,
                            request.Member,
                            request.MetadataToken),
                        request.PrinterOptions,
                        context,
                        context.MemberDecompilationLimits,
                        portablePdb,
                        bindingPolicyVersion,
                        "member-decompilation",
                        cancellationToken)
                    .ConfigureAwait(false);
            return operation switch
            {
                DecompilationOperationResult.Settled settled =>
                    new AssemblyMemberDecompilationEntry.Settled(
                        subject,
                        request,
                        settled.Attempt,
                        settled.HouseOutcome),
                DecompilationOperationResult.LibraryUnavailable unavailable =>
                    new AssemblyMemberDecompilationEntry.Unavailable(
                        subject,
                        request,
                        LibraryAdmissionUnavailable(
                            unavailable.Terminal))
                    {
                        LibraryFailure = unavailable.Terminal,
                    },
                _ => throw new InvalidOperationException(
                    "Unknown exact decompilation operation result."),
            };
        }
        catch (Exception ex) when (IsInspectionFailure(ex))
        {
            return new AssemblyMemberDecompilationEntry.Unavailable(
                subject,
                request,
                InspectionFailure(ex));
        }
    }

    public static async Task<AssemblyTypeDecompilationEntry>
        ExecuteTypeDecompilationAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyTypeSourceRequest request,
            AssemblyContextSourceQueryContext context,
            AssemblyContextLibraryPortablePdb? portablePdb = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (request.OriginalDocumentPath is not null)
        {
            throw new ArgumentException(
                "A decompiled-only type request cannot select an authored document.",
                nameof(request));
        }

        var subject = new AssemblyContextSubject(participant.Assembly);
        AssemblyBindingPolicyVersion bindingPolicyVersion =
            group.BindingPolicyVersion;
        AssemblyImageAccessResult<TypeInspectionSeed> access;
        try
        {
            access = group.UseAssemblySession(
                participant,
                cancellationToken,
                (session, retained) => new TypeInspectionSeed(
                    retained,
                    ResolveType(session, request.Type)));
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
        }
        catch (Exception ex) when (IsInspectionFailure(ex))
        {
            return new AssemblyTypeDecompilationEntry.Unavailable(
                subject,
                request,
                InspectionFailure(ex));
        }

        if (access
            is AssemblyImageAccessResult<
                TypeInspectionSeed>.Rejected rejected)
        {
            return new AssemblyTypeDecompilationEntry.Rejected(
                subject,
                request,
                rejected.Failure);
        }
        if (access
            is not AssemblyImageAccessResult<
                TypeInspectionSeed>.Available available)
        {
            throw new InvalidOperationException(
                "Unknown assembly image access result.");
        }
        if (available.Value.Target is null)
        {
            return new AssemblyTypeDecompilationEntry.Unavailable(
                subject,
                request,
                TargetNotFound(
                    "The selected participant does not declare the requested type."));
        }

        try
        {
            DecompilationOperationResult operation =
                await ExecuteDecompilationOperationAsync(
                        group,
                        participant,
                        new SourceHouseTarget.TypeTarget(
                            request.Type),
                        request.PrinterOptions,
                        context,
                        context.TypeDecompilationLimits,
                        portablePdb,
                        bindingPolicyVersion,
                        "type-decompilation",
                        cancellationToken)
                    .ConfigureAwait(false);
            return operation switch
            {
                DecompilationOperationResult.Settled settled =>
                    new AssemblyTypeDecompilationEntry.Settled(
                        subject,
                        request,
                        settled.Attempt,
                        settled.HouseOutcome),
                DecompilationOperationResult.LibraryUnavailable unavailable =>
                    new AssemblyTypeDecompilationEntry.Unavailable(
                        subject,
                        request,
                        LibraryAdmissionUnavailable(
                            unavailable.Terminal))
                    {
                        LibraryFailure = unavailable.Terminal,
                    },
                _ => throw new InvalidOperationException(
                    "Unknown exact decompilation operation result."),
            };
        }
        catch (Exception ex) when (IsInspectionFailure(ex))
        {
            return new AssemblyTypeDecompilationEntry.Unavailable(
                subject,
                request,
                InspectionFailure(ex));
        }
    }

    static async Task<DecompilationOperationResult>
        ExecuteDecompilationOperationAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            SourceHouseTarget target,
            PrinterOptions? printerOptions,
            AssemblyContextSourceQueryContext context,
            SourceHouseDecompilationLimits limits,
            AssemblyContextLibraryPortablePdb? portablePdb,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            string operationName,
            CancellationToken cancellationToken)
    {
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
            return new DecompilationOperationResult.LibraryUnavailable(
                terminal);
        }
        if (admission
            is not AssemblyContextLibraryAdapterResult.Completed completed)
        {
            throw new InvalidOperationException(
                "Unknown Library admission result.");
        }

        Exception? primaryFailure = null;
        DecompilationOperationResult.Settled settled;
        try
        {
            (CSharpDecompilationAttempt attempt,
                SourceHouseDecompilationOutcome houseOutcome) =
                    await DecompileAsync(
                            participant,
                            target,
                            printerOptions,
                            completed,
                            bindingPolicyVersion,
                            limits,
                            context,
                            operationName,
                            cancellationToken)
                        .ConfigureAwait(false);
            settled = new(
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

    abstract record DecompilationOperationResult
    {
        internal sealed record Settled(
            CSharpDecompilationAttempt Attempt,
            SourceHouseDecompilationOutcome HouseOutcome)
            : DecompilationOperationResult;

        internal sealed record LibraryUnavailable(
            AssemblyContextLibraryAdapterResult.Terminal Terminal)
            : DecompilationOperationResult;
    }
}
