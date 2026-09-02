using System.Collections.Immutable;

using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Workspaces;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// One assembly and the binding-policy snapshot that selected dependencies
/// relative to it.
/// </summary>
public sealed class AssemblyContextParticipant
{
    public AssemblyContextParticipant(
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        Assembly = assembly;
        BindingPolicy = bindingPolicy;
    }

    public ResolvedAssemblyReference Assembly { get; }
    public IAssemblyBindingPolicy BindingPolicy { get; }
}

/// <summary>Resource limits for one binding-consistent assembly context group.</summary>
public sealed record AssemblyContextGroupOptions
{
    public const long DefaultMaxRetainedImageBytes =
        AssemblyImageSnapshot.DefaultMaxRetainedImageBytes;

    public long MaxRetainedImageBytes { get; init; } =
        DefaultMaxRetainedImageBytes;

    internal void Validate() =>
        ArgumentOutOfRangeException.ThrowIfNegative(
            MaxRetainedImageBytes);
}

/// <summary>
/// A callback-scoped view of one immutable, non-pooled assembly image.
/// </summary>
/// <remarks>
/// The view is stack-only and its <see cref="Content"/> cannot be captured by
/// ordinary managed code. The callback may return only materialized values;
/// it cannot return this view or its span as <typeparamref name="TResult"/>.
/// Enforced by the C# <c>ref struct</c> and <c>scoped</c> escape rules and
/// gated by <c>ImageViewAndSpanResult_AreStackOnly</c>.
/// </remarks>
public readonly ref struct AssemblyImageView
{
    internal AssemblyImageView(
        ResolvedAssemblyReference assembly,
        ReadOnlySpan<byte> content)
    {
        Assembly = assembly;
        Content = content;
    }

    public ResolvedAssemblyReference Assembly { get; }
    public ReadOnlySpan<byte> Content { get; }
}

/// <summary>Runs bounded work while one assembly image is available.</summary>
public delegate TResult AssemblyImageCallback<TResult>(
    scoped AssemblyImageView image);

/// <summary>Typed result of callback-scoped assembly-image access.</summary>
public abstract class AssemblyImageAccessResult<TResult>
{
    private protected AssemblyImageAccessResult()
    {
    }

    public sealed class Available : AssemblyImageAccessResult<TResult>
    {
        internal Available(TResult value) => Value = value;

        public TResult Value { get; }
    }

    public sealed class Rejected : AssemblyImageAccessResult<TResult>
    {
        internal Rejected(
            ResolvedAssemblyReference assembly,
            CandidateOpenFailure failure)
        {
            Assembly = assembly;
            Failure = failure;
        }

        public ResolvedAssemblyReference Assembly { get; }
        public CandidateOpenFailure Failure { get; }
    }
}

/// <summary>
/// Stack-only result carrying either an immutable image span or a typed
/// acquisition failure.
/// </summary>
/// <remarks>
/// A successful span remains memory-safe after its group is disposed. The
/// backing array is immutable and never returned to a pool; the span itself
/// keeps that array alive for its stack lifetime. Disposal only prevents new
/// access and releases the group's retained reference. Gated by
/// <c>ReturnedSpan_RemainsSafeAfterWorkspaceDisposal</c> and
/// <c>Snapshot_IsIsolatedFromTheMutableSource</c>.
/// </remarks>
public readonly ref struct AssemblyImageSpanResult
{
    internal AssemblyImageSpanResult(
        ResolvedAssemblyReference assembly,
        ReadOnlySpan<byte> content,
        CandidateOpenFailure? failure)
    {
        Assembly = assembly;
        Content = content;
        Failure = failure;
    }

    public ResolvedAssemblyReference Assembly { get; }
    public ReadOnlySpan<byte> Content { get; }
    public CandidateOpenFailure? Failure { get; }
    public bool IsAvailable => Failure is null;
}

/// <summary>
/// One binding-consistent assembly universe owned by an
/// <see cref="InspectionWorkspace"/>.
/// </summary>
/// <remarks>
/// Images are acquired lazily, validated against their typed descriptors, and
/// retained as immutable, non-pooled snapshots. Disposal closes the group to
/// new access immediately. Active callbacks and derived-resource operations
/// keep their local snapshot and owned resources alive; the group disposes
/// derived resources before releasing retained snapshots after the final
/// operation exits.
/// Gated by <c>ConcurrentDisposal_DoesNotRevokeActiveView</c>,
/// <c>DisposalInsideCallback_DoesNotRevokeActiveView</c>, and
/// <c>GroupRejectsMixedBindingPolicySnapshots</c>. Derived-resource ordering
/// is gated by
/// <c>OwnedResources_AreDisposedBeforeSnapshots</c>.
/// </remarks>
public sealed class AssemblyContextGroup : IDisposable
{
    readonly object _lifetimeGate = new();
    readonly ImmutableArray<AssemblyContextParticipant> _participants;
    readonly Dictionary<
        AssemblyAcquisitionRegistration,
        ParticipantState> _participantByRegistration =
            new(ReferenceEqualityComparer.Instance);
    readonly HashSet<IDisposable> _ownedResources =
        new(ReferenceEqualityComparer.Instance);
    readonly Action<AssemblyContextGroup> _onDisposed;
    readonly TaskCompletionSource<AssemblyContextGroupReleaseResult>
        _releaseCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly bool _captureReleaseFailuresByDefault;
    readonly long _maxRetainedImageBytes;
    long _retainedImageBytes;
    int _activeCallbacks;
    bool _disposed;
    bool _captureReleaseFailure;
    bool _releaseRequested;
    bool _released;

    internal AssemblyContextGroup(
        IEnumerable<AssemblyContextParticipant> participants,
        AssemblyContextGroupOptions? options,
        Action<AssemblyContextGroup> onDisposed,
        bool captureReleaseFailuresByDefault)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(onDisposed);
        options ??= new AssemblyContextGroupOptions();
        options.Validate();
        _onDisposed = onDisposed;
        _captureReleaseFailuresByDefault =
            captureReleaseFailuresByDefault;
        _maxRetainedImageBytes = options.MaxRetainedImageBytes;

        var builder =
            ImmutableArray.CreateBuilder<AssemblyContextParticipant>();
        AssemblyBindingPolicyVersion? bindingPolicyVersion = null;
        foreach (AssemblyContextParticipant participant in participants)
        {
            ArgumentNullException.ThrowIfNull(participant);
            AssemblyBindingPolicyVersion participantVersion =
                participant.BindingPolicy.Version
                ?? throw new ArgumentException(
                    "A participant binding policy must expose its snapshot identity.",
                    nameof(participants));
            if (bindingPolicyVersion is null)
            {
                bindingPolicyVersion = participantVersion;
            }
            else if (!ReferenceEquals(
                         bindingPolicyVersion,
                         participantVersion))
            {
                throw new ArgumentException(
                    "Every participant in an assembly context group must use the same binding-policy snapshot.",
                    nameof(participants));
            }

            if (!_participantByRegistration.TryAdd(
                    participant.Assembly.Registration,
                    new ParticipantState(participant)))
            {
                throw new ArgumentException(
                    "An acquisition registration may appear only once in an assembly context group.",
                    nameof(participants));
            }

            builder.Add(participant);
        }

        if (builder.Count == 0)
        {
            throw new ArgumentException(
                "An assembly context group requires at least one participant.",
                nameof(participants));
        }

        _participants = builder.ToImmutable();
        BindingPolicyVersion = bindingPolicyVersion!;
    }

    public ImmutableArray<AssemblyContextParticipant> Participants =>
        _participants;

    public AssemblyBindingPolicyVersion BindingPolicyVersion { get; }

    public long RetainedImageBytes
    {
        get
        {
            lock (_lifetimeGate)
                return _retainedImageBytes;
        }
    }

    public AssemblyImageAccessResult<TResult> UseAssemblyImage<TResult>(
        ResolvedAssemblyReference assembly,
        AssemblyImageCallback<TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(callback);

        return UseSnapshot(
            assembly,
            snapshot => callback(
                new AssemblyImageView(
                    assembly,
                    snapshot.Content.AsSpan())));
    }

    /// <summary>
    /// Retains the participant's authoritative immutable image behind a fresh stream factory
    /// while preserving its acquisition registration, identity, path hint, and provenance.
    /// </summary>
    /// <remarks>
    /// The returned descriptor remains valid after the group is disposed because its stream
    /// factory retains the immutable, non-pooled snapshot. The source path is not reopened.
    /// Gated by
    /// <c>RetainedReference_RemainsSnapshotBackedAfterWorkspaceDisposal</c>.
    /// </remarks>
    public AssemblyImageAccessResult<ResolvedAssemblyReference>
        RetainAssemblyReference(
            ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return UseSnapshot(
            assembly,
            snapshot => snapshot.RetainAssemblyReference(assembly));
    }

    internal AssemblyImageAccessResult<TResult> UseAssemblySession<TResult>(
        ResolvedAssemblyReference assembly,
        Func<AssemblyInspectionSession, TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(callback);

        return UseSnapshot(
            assembly,
            snapshot =>
            {
                using AssemblyInspectionSession session =
                    AssemblyInspectionSession.Open(snapshot);
                return callback(session);
            });
    }

    internal AssemblyImageAccessResult<TResult> UseAssemblySession<TResult>(
        ResolvedAssemblyReference assembly,
        Func<
            AssemblyInspectionSession,
            ResolvedAssemblyReference,
            TResult> callback)
        => UseAssemblySession(
            assembly,
            CancellationToken.None,
            callback);

    internal AssemblyImageAccessResult<TResult> UseAssemblySession<TResult>(
        ResolvedAssemblyReference assembly,
        CancellationToken cancellationToken,
        Func<
            AssemblyInspectionSession,
            ResolvedAssemblyReference,
            TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(callback);

        return UseSnapshot(
            assembly,
            cancellationToken,
            snapshot =>
            {
                using AssemblyInspectionSession session =
                    AssemblyInspectionSession.Open(snapshot);
                return callback(
                    session,
                    snapshot.RetainAssemblyReference(assembly));
            });
    }

    internal AssemblyImageAccessResult<TResult> UseAssemblySession<TResult>(
        AssemblyContextParticipant participant,
        CancellationToken cancellationToken,
        Func<
            AssemblyInspectionSession,
            ResolvedAssemblyReference,
            TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(callback);

        return UseSnapshot(
            participant,
            cancellationToken,
            snapshot =>
            {
                using AssemblyInspectionSession session =
                    AssemblyInspectionSession.Open(snapshot);
                return callback(
                    session,
                    snapshot.RetainAssemblyReference(
                        participant.Assembly));
            });
    }

    internal async Task<AssemblyImageAccessResult<TResult>>
        UseAndReleaseAssemblySessionAsync<TResult>(
            ResolvedAssemblyReference assembly,
            Func<
                AssemblyInspectionSession,
                ResolvedAssemblyReference,
                Task<TResult>> callback)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(callback);

        BeginCallback();
        Exception? operationFailure = null;
        ParticipantState? participant = null;
        try
        {
            participant = FindParticipant(assembly);
            SnapshotAccess access = GetSnapshot(participant);
            if (access.Failure is { } failure)
            {
                return new AssemblyImageAccessResult<TResult>.Rejected(
                    assembly,
                    failure);
            }

            AssemblyImageSnapshot snapshot = access.Snapshot!;
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(snapshot);
            TResult value = await callback(
                    session,
                    snapshot.RetainAssemblyReference(assembly))
                .ConfigureAwait(false);
            return new AssemblyImageAccessResult<TResult>.Available(value);
        }
        catch (Exception ex)
        {
            operationFailure = ex;
            throw;
        }
        finally
        {
            if (participant is null)
                EndCallback(operationFailure);
            else
                ReleaseSnapshotAndEndCallback(
                    participant,
                    operationFailure);
        }
    }

    internal AssemblyImageAccessResult<TResult> UseSnapshot<TResult>(
        ResolvedAssemblyReference assembly,
        Func<AssemblyImageSnapshot, TResult> callback)
        => UseSnapshot(
            assembly,
            CancellationToken.None,
            callback);

    internal AssemblyImageAccessResult<TResult> UseSnapshot<TResult>(
        ResolvedAssemblyReference assembly,
        CancellationToken cancellationToken,
        Func<AssemblyImageSnapshot, TResult> callback)
        => UseSnapshot(
            assembly,
            expectedParticipant: null,
            cancellationToken,
            callback);

    internal AssemblyImageAccessResult<TResult> UseSnapshot<TResult>(
        AssemblyContextParticipant participant,
        CancellationToken cancellationToken,
        Func<AssemblyImageSnapshot, TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(participant);
        return UseSnapshot(
            participant.Assembly,
            participant,
            cancellationToken,
            callback);
    }

    AssemblyImageAccessResult<TResult> UseSnapshot<TResult>(
        ResolvedAssemblyReference assembly,
        AssemblyContextParticipant? expectedParticipant,
        CancellationToken cancellationToken,
        Func<AssemblyImageSnapshot, TResult> callback)
    {
        BeginCallback();
        Exception? operationFailure = null;
        try
        {
            ParticipantState participant =
                expectedParticipant is null
                    ? FindParticipant(assembly)
                    : FindExactParticipant(expectedParticipant);
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotAccess access = GetSnapshot(participant);
            if (access.Failure is { } failure)
            {
                return new AssemblyImageAccessResult<TResult>.Rejected(
                    assembly,
                    failure);
            }

            TResult value = callback(access.Snapshot!);
            return new AssemblyImageAccessResult<TResult>.Available(value);
        }
        catch (Exception ex)
        {
            operationFailure = ex;
            throw;
        }
        finally
        {
            EndCallback(operationFailure);
        }
    }

    internal TResult UseContext<TResult>(Func<TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        BeginCallback();
        Exception? operationFailure = null;
        try
        {
            return callback();
        }
        catch (Exception ex)
        {
            operationFailure = ex;
            throw;
        }
        finally
        {
            EndCallback(operationFailure);
        }
    }

    internal void RegisterOwnedResource(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_ownedResources.Add(resource))
            {
                throw new ArgumentException(
                    "A resource may be registered with an assembly context group only once.",
                    nameof(resource));
            }
        }
    }

    internal void UnregisterOwnedResource(IDisposable resource)
    {
        lock (_lifetimeGate)
            _ownedResources.Remove(resource);
    }

    public AssemblyImageSpanResult GetAssemblyImageSpan(
        ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        BeginCallback();
        Exception? operationFailure = null;
        try
        {
            ParticipantState participant = FindParticipant(assembly);
            SnapshotAccess access = GetSnapshot(participant);
            return access.Failure is { } failure
                ? new AssemblyImageSpanResult(
                    assembly,
                    default,
                    failure)
                : new AssemblyImageSpanResult(
                    assembly,
                    access.Snapshot!.Content.AsSpan(),
                    failure: null);
        }
        catch (Exception ex)
        {
            operationFailure = ex;
            throw;
        }
        finally
        {
            EndCallback(operationFailure);
        }
    }

    ParticipantState FindParticipant(
        ResolvedAssemblyReference assembly)
    {
        if (!_participantByRegistration.TryGetValue(
                assembly.Registration,
                out ParticipantState? participant)
            || !ReferenceEquals(participant.Participant.Assembly, assembly))
        {
            throw new ArgumentException(
                "The assembly does not belong to this context group.",
                nameof(assembly));
        }

        return participant;
    }

    ParticipantState FindExactParticipant(
        AssemblyContextParticipant participant)
    {
        ParticipantState registered =
            FindParticipant(participant.Assembly);
        if (!ReferenceEquals(
                registered.Participant,
                participant))
        {
            throw new ArgumentException(
                "The participant does not belong to this context group.",
                nameof(participant));
        }
        if (!ReferenceEquals(
                participant.BindingPolicy.Version,
                BindingPolicyVersion))
        {
            throw new InvalidOperationException(
                "The participant binding-policy snapshot changed after the assembly context group was created.");
        }

        return registered;
    }

    SnapshotAccess GetSnapshot(ParticipantState participant)
    {
        lock (participant.ImageLoadGate)
        {
            ObjectDisposedException.ThrowIf(
                participant.Released,
                participant);
            if (participant.Initialized)
                return participant.Access;

            AssemblyImageSnapshotResult result =
                AssemblyImageSnapshot.Open(
                    participant.Participant.Assembly,
                    TryReserveImage,
                    ReleaseImage);
            SnapshotAccess access = result switch
            {
                AssemblyImageSnapshotResult.Ready ready =>
                    new SnapshotAccess(ready.Snapshot, Failure: null),
                AssemblyImageSnapshotResult.Rejected rejected =>
                    new SnapshotAccess(
                        Snapshot: null,
                        rejected.Failure),
                _ => throw new InvalidOperationException(
                    "Unknown assembly image acquisition result."),
            };

            participant.Access = access;
            participant.Initialized = true;
            return access;
        }
    }

    void ReleaseSnapshotAndEndCallback(
        ParticipantState participant,
        Exception? operationFailure)
    {
        bool release;
        bool captureReleaseFailure;
        lock (participant.ImageLoadGate)
        {
            lock (_lifetimeGate)
            {
                _activeCallbacks--;
                release =
                    _releaseRequested
                    && _activeCallbacks == 0
                    && !_released;
                if (release)
                {
                    _released = true;
                }
                else if (!_disposed)
                {
                    _retainedImageBytes -= participant.Release();
                }

                captureReleaseFailure = _captureReleaseFailure;
            }
        }

        CompleteCallbackRelease(
            release,
            captureReleaseFailure,
            operationFailure);
    }

    void ReleaseSnapshotCore(ParticipantState participant)
    {
        long imageSize;
        lock (participant.ImageLoadGate)
            imageSize = participant.Release();
        if (imageSize != 0)
            ReleaseImage(imageSize);
    }

    bool TryReserveImage(long imageSize)
    {
        lock (_lifetimeGate)
        {
            if (imageSize
                > _maxRetainedImageBytes - _retainedImageBytes)
            {
                return false;
            }

            _retainedImageBytes += imageSize;
            return true;
        }
    }

    void ReleaseImage(long imageSize)
    {
        lock (_lifetimeGate)
            _retainedImageBytes -= imageSize;
    }

    void BeginCallback()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeCallbacks++;
        }
    }

    void EndCallback(Exception? operationFailure)
    {
        bool release;
        bool captureReleaseFailure;
        lock (_lifetimeGate)
        {
            _activeCallbacks--;
            release =
                _releaseRequested
                && _activeCallbacks == 0
                && !_released;
            if (release)
                _released = true;
            captureReleaseFailure = _captureReleaseFailure;
        }

        CompleteCallbackRelease(
            release,
            captureReleaseFailure,
            operationFailure);
    }

    void CompleteCallbackRelease(
        bool release,
        bool captureReleaseFailure,
        Exception? operationFailure)
    {
        if (!release)
            return;

        Exception? releaseFailure = ReleaseOwnedState();
        _releaseCompletion.TrySetResult(
            new AssemblyContextGroupReleaseResult(releaseFailure));
        if (releaseFailure is null || captureReleaseFailure)
            return;

        if (operationFailure is not null)
        {
            throw new AggregateException(
                operationFailure,
                releaseFailure);
        }

        throw releaseFailure;
    }

    public void Dispose()
    {
        RequestRelease(captureFailure: false);
    }

    internal Task<AssemblyContextGroupReleaseResult> RequestReleaseAsync()
    {
        RequestRelease(captureFailure: true);
        return _releaseCompletion.Task;
    }

    internal Task<AssemblyContextGroupReleaseResult> ReleaseCompletion =>
        _releaseCompletion.Task;

    internal void CloseAdmissionFromWorkspace(
        bool captureFailure)
    {
        lock (_lifetimeGate)
        {
            _captureReleaseFailure |=
                captureFailure
                || _captureReleaseFailuresByDefault;
            _disposed = true;
        }
    }

    void RequestRelease(bool captureFailure)
    {
        bool release;
        bool captureReleaseFailure;
        bool notifyOwner;
        lock (_lifetimeGate)
        {
            _captureReleaseFailure |=
                captureFailure
                || _captureReleaseFailuresByDefault;
            captureReleaseFailure = _captureReleaseFailure;
            if (_releaseRequested)
                return;

            notifyOwner = !_disposed;
            _disposed = true;
            _releaseRequested = true;
            release = _activeCallbacks == 0 && !_released;
            if (release)
                _released = true;
        }

        if (notifyOwner)
            _onDisposed(this);

        if (release)
        {
            Exception? releaseFailure = ReleaseOwnedState();
            _releaseCompletion.TrySetResult(
                new AssemblyContextGroupReleaseResult(releaseFailure));
            if (releaseFailure is not null
                && !captureReleaseFailure)
            {
                throw releaseFailure;
            }
        }
    }

    Exception? ReleaseOwnedState()
    {
        IDisposable[] resources;
        lock (_lifetimeGate)
        {
            resources = [.. _ownedResources];
            _ownedResources.Clear();
        }

        List<Exception>? failures = null;
        foreach (IDisposable resource in resources)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        foreach (ParticipantState participant
            in _participantByRegistration.Values)
        {
            ReleaseSnapshotCore(participant);
        }

        return failures is null
            ? null
            : new AggregateException(failures);
    }

    sealed class ParticipantState(
        AssemblyContextParticipant participant)
    {
        internal AssemblyContextParticipant Participant { get; } =
            participant;
        internal object ImageLoadGate { get; } = new();
        internal bool Initialized { get; set; }
        internal bool Released { get; private set; }
        internal SnapshotAccess Access { get; set; }

        internal long Release()
        {
            if (Released)
                return 0;

            long imageSize = Access.Snapshot?.Length ?? 0;
            Access = default;
            Initialized = false;
            Released = true;
            return imageSize;
        }
    }

    readonly record struct SnapshotAccess(
        AssemblyImageSnapshot? Snapshot,
        CandidateOpenFailure? Failure);
}

sealed record AssemblyContextGroupReleaseResult(Exception? Failure);

/// <summary>
/// Terminal release result for one group admitted by an asynchronous workspace.
/// </summary>
public abstract class InspectionWorkspaceGroupCloseResult
{
    internal InspectionWorkspaceGroupCloseResult(
        int registrationIndex)
    {
        RegistrationIndex = registrationIndex;
    }

    public int RegistrationIndex { get; }
}

/// <summary>
/// Terminal result for one workspace-owned direct group release.
/// </summary>
public sealed class InspectionWorkspaceDirectGroupCloseResult
    : InspectionWorkspaceGroupCloseResult
{
    internal InspectionWorkspaceDirectGroupCloseResult(
        int registrationIndex,
        Exception? failure)
        : base(registrationIndex)
    {
        Failure = failure;
    }

    public bool Succeeded => Failure is null;

    public Exception? Failure { get; }
}

/// <summary>
/// Terminal result retained from an adjacent coordinated release owner.
/// </summary>
public sealed class InspectionWorkspaceCoordinatedGroupCloseResult<TResult>
    : InspectionWorkspaceGroupCloseResult
{
    internal InspectionWorkspaceCoordinatedGroupCloseResult(
        int registrationIndex,
        TResult result)
        : base(registrationIndex)
    {
        Result = result;
    }

    public TResult Result { get; }
}

internal interface IWorkspaceCoordinatedGroupParticipation
{
    WorkspaceCoordinatedAdmissionGate WorkspaceAdmission { get; }

    void RequestRelease();

    Task<InspectionWorkspaceGroupCloseResult> GetCloseResultAsync(
        int registrationIndex);
}

internal sealed class WorkspaceCoordinatedAdmissionGate
{
    readonly object _gate = new();
    bool _closed;

    internal TResult Admit<TResult>(Func<TResult> create)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _closed,
                nameof(PackageAssemblyContextCompletion));
            return create();
        }
    }

    internal void Close()
    {
        lock (_gate)
            _closed = true;
    }
}

/// <summary>
/// Immutable terminal report produced by an asynchronous workspace close.
/// </summary>
public sealed class InspectionWorkspaceCloseReport
{
    internal InspectionWorkspaceCloseReport(
        ImmutableArray<InspectionWorkspaceGroupCloseResult> groups,
        ImmutableArray<Exception> artifactSessionCleanupFailures)
    {
        Groups = groups;
        ArtifactSessionCleanupFailures =
            artifactSessionCleanupFailures;
    }

    public ImmutableArray<InspectionWorkspaceGroupCloseResult> Groups
    {
        get;
    }

    /// <summary>
    /// Cleanup failures retained from artifact sessions after their exact
    /// dependent groups reached terminal release.
    /// </summary>
    public ImmutableArray<Exception> ArtifactSessionCleanupFailures
    {
        get;
    }
}

/// <summary>
/// Shared owner for one or more assembly context groups.
/// </summary>
public sealed partial class InspectionWorkspace :
    IDisposable,
    IAsyncDisposable
{
    readonly object _gate = new();
    readonly List<AssemblyContextGroup> _groups = [];
    readonly List<WorkspaceGroupAdmission> _admissions = [];
    readonly List<WorkspaceArtifactSessionRegistration>
        _artifactSessions = [];
    readonly InspectionWorkspaceLifetimeMode _lifetimeMode;
    readonly TaskCompletionSource<
        WorkspaceClosePlan>? _closeStart;
    readonly Task<InspectionWorkspaceCloseReport>? _closeTask;
    InspectionWorkspaceCloseReport? _closeReport;
    InspectionWorkspaceState _state;
    int _nextRegistrationIndex;

    public InspectionWorkspace()
    {
        _lifetimeMode = InspectionWorkspaceLifetimeMode.Synchronous;
    }

    InspectionWorkspace(
        InspectionWorkspaceLifetimeMode lifetimeMode)
    {
        _lifetimeMode = lifetimeMode;
        _closeStart = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _closeTask = CloseCoreAsync(_closeStart.Task);
    }

    /// <summary>
    /// Creates a workspace whose terminal lifetime is observed through
    /// <see cref="CloseAsync"/>.
    /// </summary>
    public static InspectionWorkspace CreateAsynchronous() =>
        new(InspectionWorkspaceLifetimeMode.Asynchronous);

    /// <summary>
    /// Gets the terminal report after asynchronous close completes.
    /// </summary>
    public InspectionWorkspaceCloseReport? CloseReport
    {
        get
        {
            lock (_gate)
                return _closeReport;
        }
    }

    public AssemblyContextGroup CreateAssemblyContextGroup(
        IEnumerable<AssemblyContextParticipant> participants,
        AssemblyContextGroupOptions? options = null)
    {
        WorkspaceGroupAdmission? admission = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            if (_lifetimeMode
                == InspectionWorkspaceLifetimeMode.Asynchronous)
            {
                admission = new WorkspaceGroupAdmission(
                    _nextRegistrationIndex++,
                    coordinatedParticipation: null);
                _admissions.Add(admission);
            }
        }

        AssemblyContextGroup group;
        try
        {
            group = new AssemblyContextGroup(
                participants,
                options,
                RemoveGroup,
                captureReleaseFailuresByDefault:
                    _lifetimeMode
                    == InspectionWorkspaceLifetimeMode.Asynchronous);
        }
        catch
        {
            admission?.Complete(registration: null);
            throw;
        }

        WorkspaceGroupRegistration? registration =
            admission is null
                ? null
                : new WorkspaceGroupRegistration(
                    admission.RegistrationIndex,
                    group);
        bool published;
        bool artifactOwnershipConflict;
        lock (_gate)
        {
            artifactOwnershipConflict =
                _state == InspectionWorkspaceState.Open
                && _artifactSessions.Any(
                    registration =>
                        registration.DependsOn(group));
            published =
                _state == InspectionWorkspaceState.Open
                && !artifactOwnershipConflict;
            if (published)
            {
                _groups.Add(group);
                admission?.Complete(registration);
            }
        }

        if (published)
            return group;

        admission?.Complete(
            artifactOwnershipConflict
                ? null
                : registration);
        if (artifactOwnershipConflict)
        {
            group.Dispose();
            throw new InvalidOperationException(
                "A group projected from a transferred artifact session cannot be admitted later.");
        }
        if (_lifetimeMode
            == InspectionWorkspaceLifetimeMode.Synchronous)
        {
            group.Dispose();
        }

        throw new ObjectDisposedException(nameof(InspectionWorkspace));
    }

    /// <summary>
    /// Transfers one published artifact session and query lease to this
    /// workspace and binds their release to exact dependent groups.
    /// </summary>
    /// <remarks>
    /// The supplied set must contain every current workspace group with at
    /// least one participant projected from this session, and no other group.
    /// A later group projected from a transferred session is rejected. The
    /// workspace disposes the query lease and session only after all stored
    /// groups complete release. These properties are gated by
    /// <c>RegisterArtifactSession_RejectsForeignOrIncompleteGroupSet</c> and
    /// <c>WorkspaceClose_ReleasesArtifactSessionAfterExactDependentGroupQuiesces</c>.
    /// </remarks>
    internal void RegisterArtifactSession(
        ArtifactSetSession session,
        ArtifactQueryLease queryLease,
        IEnumerable<AssemblyContextGroup> dependentGroups)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(queryLease);
        ArgumentNullException.ThrowIfNull(dependentGroups);
        if (_lifetimeMode
            != InspectionWorkspaceLifetimeMode.Asynchronous)
        {
            throw new InvalidOperationException(
                "Artifact sessions require a workspace created by CreateAsynchronous.");
        }

        ImmutableArray<AssemblyContextGroup> groups =
            [.. dependentGroups];
        if (groups.IsDefaultOrEmpty
            || groups.Any(static group => group is null))
        {
            throw new ArgumentException(
                "An artifact session requires at least one dependent group.",
                nameof(dependentGroups));
        }
        if (groups.Distinct(ReferenceEqualityComparer.Instance).Count()
            != groups.Length)
        {
            throw new ArgumentException(
                "An artifact session cannot depend on the same group more than once.",
                nameof(dependentGroups));
        }

        IReadOnlyList<ArtifactDescriptor> catalog =
            session.GetCatalog(queryLease);
        var registrations =
            new HashSet<ArtifactAcquisitionRegistration>(
                ReferenceEqualityComparer.Instance);
        foreach (ArtifactDescriptor descriptor in catalog)
        {
            registrations.Add(
                session.GetContentReference(
                    descriptor.Identity,
                    queryLease).Registration);
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            var expectedGroups =
                new HashSet<AssemblyContextGroup>(
                    ReferenceEqualityComparer.Instance);
            foreach (WorkspaceGroupAdmission admission in _admissions)
            {
                if (!admission.TryGetCompletedGroup(out AssemblyContextGroup? group))
                {
                    throw new InvalidOperationException(
                        "Artifact session ownership cannot transfer while a workspace group admission is incomplete.");
                }
                if (group is not null
                    && group.Participants.Any(participant =>
                        participant.Assembly.Registration
                            .ArtifactRegistration is
                                ArtifactAcquisitionRegistration registration
                        && registrations.Contains(registration)))
                {
                    expectedGroups.Add(group);
                }
            }
            if (expectedGroups.Count == 0
                || !expectedGroups.SetEquals(groups))
            {
                throw new ArgumentException(
                    "The dependent groups must be the complete exact set of current workspace groups projected from this artifact session.",
                    nameof(dependentGroups));
            }
            if (_artifactSessions.Any(registration =>
                    ReferenceEquals(registration.Session, session)))
            {
                throw new InvalidOperationException(
                    "The artifact session is already registered with this workspace.");
            }

            _artifactSessions.Add(
                new WorkspaceArtifactSessionRegistration(
                    session,
                    queryLease,
                    [.. registrations],
                    groups));
        }
    }

    public void Dispose()
    {
        if (_lifetimeMode
            == InspectionWorkspaceLifetimeMode.Asynchronous)
        {
            throw new InvalidOperationException(
                "An asynchronous inspection workspace must be closed with CloseAsync or DisposeAsync.");
        }

        List<AssemblyContextGroup> groups;
        lock (_gate)
        {
            if (_state != InspectionWorkspaceState.Open)
                return;
            _state = InspectionWorkspaceState.Closing;
            groups = [.. _groups];
            foreach (AssemblyContextGroup group in groups)
            {
                group.CloseAdmissionFromWorkspace(
                    captureFailure: false);
            }

            _groups.Clear();
        }

        List<Exception>? failures = null;
        foreach (AssemblyContextGroup group in groups)
        {
            try
            {
                group.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        lock (_gate)
            _state = InspectionWorkspaceState.Closed;

        if (failures is not null)
            throw new AggregateException(failures);
    }

    /// <summary>
    /// Closes an asynchronous workspace and returns its shared terminal report.
    /// </summary>
    public Task<InspectionWorkspaceCloseReport> CloseAsync()
    {
        if (_lifetimeMode
            != InspectionWorkspaceLifetimeMode.Asynchronous)
        {
            throw new InvalidOperationException(
                "CloseAsync requires a workspace created by CreateAsynchronous.");
        }

        WorkspaceClosePlan plan = default;
        bool startClose = false;
        lock (_gate)
        {
            if (_state == InspectionWorkspaceState.Open)
            {
                ImmutableArray<WorkspaceGroupAdmission> admissions =
                    [.. _admissions];
                foreach (WorkspaceGroupAdmission admission in admissions)
                    admission.CloseWorkspaceAdmission();
                plan = new WorkspaceClosePlan(
                    admissions,
                    [.. _artifactSessions]);
                _state = InspectionWorkspaceState.Closing;
                foreach (AssemblyContextGroup group in _groups)
                {
                    group.CloseAdmissionFromWorkspace(
                        captureFailure: true);
                }

                _groups.Clear();
                startClose = true;
            }
        }

        if (startClose)
            _closeStart!.SetResult(plan);

        return _closeTask!;
    }

    public ValueTask DisposeAsync()
    {
        if (_lifetimeMode
            == InspectionWorkspaceLifetimeMode.Asynchronous)
        {
            return new ValueTask(CloseAsync());
        }

        Dispose();
        return ValueTask.CompletedTask;
    }

    void RemoveGroup(AssemblyContextGroup group)
    {
        lock (_gate)
            _groups.Remove(group);
    }

    internal ImmutableArray<WorkspaceCoordinatedGroupAdmission>
        BeginCoordinatedGroupAdmissions(
            ImmutableArray<IWorkspaceCoordinatedGroupParticipation>
                participations)
    {
        if (_lifetimeMode
            != InspectionWorkspaceLifetimeMode.Asynchronous)
        {
            throw new InvalidOperationException(
                "Coordinated package-role completion requires a workspace created by CreateAsynchronous.");
        }
        if (participations.IsDefaultOrEmpty
            || participations.Any(
                static participation => participation is null))
        {
            throw new ArgumentException(
                "Coordinated group admission requires one participation handle per planned group.",
                nameof(participations));
        }

        var admissions =
            ImmutableArray.CreateBuilder<
                WorkspaceCoordinatedGroupAdmission>(
                participations.Length);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            foreach (IWorkspaceCoordinatedGroupParticipation participation
                in participations)
            {
                var admission = new WorkspaceGroupAdmission(
                    _nextRegistrationIndex++,
                    participation);
                _admissions.Add(admission);
                admissions.Add(
                    new WorkspaceCoordinatedGroupAdmission(
                        this,
                        admission));
            }
        }

        return admissions.MoveToImmutable();
    }

    internal bool CompleteCoordinatedGroupAdmissions(
        ImmutableArray<WorkspaceCoordinatedGroupAdmission> admissions,
        ImmutableArray<AssemblyContextGroup> groups)
    {
        if (admissions.IsDefaultOrEmpty
            || admissions.Length != groups.Length)
        {
            throw new ArgumentException(
                "Every coordinated admission must complete with one exact physical group.",
                nameof(groups));
        }
        for (int index = 0; index < admissions.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(admissions[index]);
            ArgumentNullException.ThrowIfNull(groups[index]);
            if (!ReferenceEquals(admissions[index]._workspace, this))
            {
                throw new InvalidOperationException(
                    "A coordinated admission belongs to a different inspection workspace.");
            }
        }

        bool published;
        lock (_gate)
        {
            published =
                _state == InspectionWorkspaceState.Open
                && !groups.Any(group =>
                    _artifactSessions.Any(
                        registration =>
                            registration.DependsOn(group)));
            for (int index = 0; index < admissions.Length; index++)
            {
                WorkspaceCoordinatedGroupAdmission admission =
                    admissions[index];
                admission._admission.SetRegistration(
                    new WorkspaceGroupRegistration(
                        admission._admission.RegistrationIndex,
                        groups[index],
                        admission._admission.CoordinatedParticipation));
            }
        }

        foreach (WorkspaceCoordinatedGroupAdmission admission in admissions)
            admission._admission.FinishRegistration();

        return published;
    }

    internal void CompleteCoordinatedGroupAdmissionsWithoutGroups(
        ImmutableArray<WorkspaceCoordinatedGroupAdmission> admissions)
    {
        foreach (WorkspaceCoordinatedGroupAdmission admission in admissions)
        {
            if (!ReferenceEquals(admission._workspace, this))
            {
                throw new InvalidOperationException(
                    "A coordinated admission belongs to a different inspection workspace.");
            }
            admission._admission.Complete(registration: null);
        }
    }

    async Task<InspectionWorkspaceCloseReport> CloseCoreAsync(
        Task<WorkspaceClosePlan> start)
    {
        WorkspaceClosePlan plan =
            await start.ConfigureAwait(false);
        var completionTasks =
            new Task<InspectionWorkspaceGroupCloseResult?>[
                plan.GroupAdmissions.Length];
        for (int index = 0;
            index < plan.GroupAdmissions.Length;
            index++)
        {
            completionTasks[index] =
                plan.GroupAdmissions[index]
                    .RequestReleaseAndGetResultAsync();
        }

        var completed =
            new InspectionWorkspaceGroupCloseResult?[
                completionTasks.Length];
        Exception? groupCloseFailure = null;
        try
        {
            completed =
                await Task.WhenAll(completionTasks).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            groupCloseFailure = exception;
            for (int index = 0;
                index < completionTasks.Length;
                index++)
            {
                if (completionTasks[index].IsCompletedSuccessfully)
                    completed[index] = completionTasks[index].Result;
            }
        }
        ImmutableArray<Exception>.Builder artifactCleanupFailures =
            ImmutableArray.CreateBuilder<Exception>();
        foreach (WorkspaceArtifactSessionRegistration registration
            in plan.ArtifactSessions)
        {
            artifactCleanupFailures.AddRange(
                await registration.ReleaseAsync().ConfigureAwait(false));
        }
        var reportGroups =
            ImmutableArray.CreateBuilder<
                InspectionWorkspaceGroupCloseResult>();
        foreach (InspectionWorkspaceGroupCloseResult? result in completed)
        {
            if (result is not null)
                reportGroups.Add(result);
        }

        var report = new InspectionWorkspaceCloseReport(
            reportGroups.ToImmutable(),
            artifactCleanupFailures.ToImmutable());
        lock (_gate)
        {
            _closeReport = report;
            _state = InspectionWorkspaceState.Closed;
        }
        if (groupCloseFailure is not null)
        {
            return await Task.FromException<
                    InspectionWorkspaceCloseReport>(
                    groupCloseFailure)
                .ConfigureAwait(false);
        }
        return report;
    }

    internal sealed class WorkspaceArtifactSessionRegistration
    {
        internal WorkspaceArtifactSessionRegistration(
            ArtifactSetSession session,
            ArtifactQueryLease queryLease,
            ImmutableArray<ArtifactAcquisitionRegistration>
                artifactRegistrations,
            ImmutableArray<AssemblyContextGroup> dependentGroups)
        {
            Session = session;
            QueryLease = queryLease;
            ArtifactRegistrations = artifactRegistrations;
            DependentGroups = dependentGroups;
        }

        internal ArtifactSetSession Session { get; }

        ArtifactQueryLease QueryLease { get; }

        ImmutableArray<ArtifactAcquisitionRegistration>
            ArtifactRegistrations { get; }

        ImmutableArray<AssemblyContextGroup> DependentGroups { get; }

        internal bool DependsOn(AssemblyContextGroup group) =>
            group.Participants.Any(participant =>
                participant.Assembly.Registration.ArtifactRegistration
                    is ArtifactAcquisitionRegistration registration
                && ArtifactRegistrations.Contains(
                    registration,
                    ReferenceEqualityComparer.Instance));

        internal async Task<IReadOnlyList<Exception>> ReleaseAsync()
        {
            await Task.WhenAll(
                    DependentGroups.Select(
                        static group => group.RequestReleaseAsync()))
                .ConfigureAwait(false);
            var failures = new List<Exception>();
            try
            {
                QueryLease.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                await Session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            failures.AddRange(Session.CleanupFailures);
            return failures;
        }
    }

    readonly record struct WorkspaceClosePlan(
        ImmutableArray<WorkspaceGroupAdmission> GroupAdmissions,
        ImmutableArray<WorkspaceArtifactSessionRegistration>
            ArtifactSessions);

    internal sealed class WorkspaceCoordinatedGroupAdmission
    {
        internal readonly InspectionWorkspace _workspace;
        internal readonly WorkspaceGroupAdmission _admission;

        internal WorkspaceCoordinatedGroupAdmission(
            InspectionWorkspace workspace,
            WorkspaceGroupAdmission admission)
        {
            _workspace = workspace;
            _admission = admission;
        }

        internal AssemblyContextGroup CreateGroup(
            IEnumerable<AssemblyContextParticipant> participants,
            AssemblyContextGroupOptions? options) =>
            new(
                participants,
                options,
                _workspace.RemoveGroup,
                captureReleaseFailuresByDefault: false);
    }

    internal sealed class WorkspaceGroupAdmission(
        int registrationIndex,
        IWorkspaceCoordinatedGroupParticipation? coordinatedParticipation)
    {
        readonly object _gate = new();
        readonly TaskCompletionSource _constructionCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource<
            InspectionWorkspaceGroupCloseResult?> _terminalCompletion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkspaceGroupRegistration? _registration;
        AssemblyContextGroup? _completedGroup;

        internal int RegistrationIndex { get; } = registrationIndex;

        internal IWorkspaceCoordinatedGroupParticipation?
            CoordinatedParticipation { get; } =
                coordinatedParticipation;

        internal void CloseWorkspaceAdmission() =>
            CoordinatedParticipation?.WorkspaceAdmission.Close();

        internal void TryRequestRelease()
        {
            WorkspaceGroupRegistration? registration;
            lock (_gate)
                registration = _registration;

            if (CoordinatedParticipation is not null)
            {
                CoordinatedParticipation.RequestRelease();
            }
            else if (registration is not null)
            {
                _ = registration.Group.RequestReleaseAsync();
            }
        }

        internal async Task<InspectionWorkspaceGroupCloseResult?>
            RequestReleaseAndGetResultAsync()
        {
            await _constructionCompletion.Task.ConfigureAwait(false);
            TryRequestRelease();
            return await _terminalCompletion.Task.ConfigureAwait(false);
        }

        internal void Complete(
            WorkspaceGroupRegistration? registration)
        {
            if (registration is null)
            {
                _terminalCompletion.SetResult(result: null);
                _constructionCompletion.SetResult();
                return;
            }

            lock (_gate)
            {
                _registration = registration;
                _completedGroup = registration.Group;
            }

            ObserveRelease(registration);

            _constructionCompletion.SetResult();
        }

        internal bool TryGetCompletedGroup(
            out AssemblyContextGroup? group)
        {
            if (!_constructionCompletion.Task.IsCompleted)
            {
                group = null;
                return false;
            }

            lock (_gate)
            {
                group = _completedGroup;
                return true;
            }
        }

        internal void SetRegistration(
            WorkspaceGroupRegistration registration)
        {
            ArgumentNullException.ThrowIfNull(registration);
            lock (_gate)
            {
                if (_registration is not null)
                {
                    throw new InvalidOperationException(
                        "A workspace group admission completed more than once.");
                }
                _registration = registration;
                _completedGroup = registration.Group;
            }
        }

        internal void FinishRegistration()
        {
            WorkspaceGroupRegistration registration;
            lock (_gate)
            {
                registration =
                    _registration
                    ?? throw new InvalidOperationException(
                        "A workspace group admission has no registration to finish.");
            }

            ObserveRelease(registration);
            _constructionCompletion.SetResult();
        }

        void ObserveRelease(
            WorkspaceGroupRegistration registration)
        {
            if (registration.CoordinatedParticipation is not null)
            {
                ObserveCoordinatedRelease(registration);
                return;
            }

            Task<AssemblyContextGroupReleaseResult> completion =
                registration.Group.ReleaseCompletion;
            var awaiter =
                completion.ConfigureAwait(false).GetAwaiter();
            if (awaiter.IsCompleted)
            {
                CompleteRelease(
                    registration,
                    awaiter.GetResult());
                return;
            }

            awaiter.OnCompleted(
                () => CompleteRelease(
                    registration,
                    awaiter.GetResult()));
        }

        void CompleteRelease(
            WorkspaceGroupRegistration registration,
            AssemblyContextGroupReleaseResult release)
        {
            var result = new InspectionWorkspaceDirectGroupCloseResult(
                registration.RegistrationIndex,
                release.Failure);
            CompleteRelease(registration, result);
        }

        void ObserveCoordinatedRelease(
            WorkspaceGroupRegistration registration)
        {
            Task<InspectionWorkspaceGroupCloseResult> completion =
                registration.CoordinatedParticipation!
                    .GetCloseResultAsync(
                        registration.RegistrationIndex);
            _ = CompleteCoordinatedReleaseAsync(
                registration,
                completion);
        }

        async Task CompleteCoordinatedReleaseAsync(
            WorkspaceGroupRegistration registration,
            Task<InspectionWorkspaceGroupCloseResult> completion)
        {
            InspectionWorkspaceGroupCloseResult result;
            try
            {
                result =
                    await completion.ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                lock (_gate)
                {
                    if (ReferenceEquals(
                            _registration,
                            registration))
                    {
                        _registration = null;
                    }
                }

                _terminalCompletion.SetException(failure);
                return;
            }

            CompleteRelease(registration, result);
        }

        void CompleteRelease(
            WorkspaceGroupRegistration registration,
            InspectionWorkspaceGroupCloseResult result)
        {
            lock (_gate)
            {
                if (ReferenceEquals(
                        _registration,
                        registration))
                {
                    _registration = null;
                }
            }

            _terminalCompletion.SetResult(result);
        }
    }

    internal sealed record WorkspaceGroupRegistration(
        int RegistrationIndex,
        AssemblyContextGroup Group,
        IWorkspaceCoordinatedGroupParticipation?
            CoordinatedParticipation = null);

    enum InspectionWorkspaceLifetimeMode
    {
        Synchronous,
        Asynchronous
    }

    enum InspectionWorkspaceState
    {
        Open,
        Closing,
        Closed
    }
}
