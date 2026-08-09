using System.Collections.Immutable;

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
/// <c>WorkspaceDisposal_DisposesOwnedGraphBeforeSnapshots</c>.
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
    readonly long _maxRetainedImageBytes;
    long _retainedImageBytes;
    int _activeCallbacks;
    bool _disposed;
    bool _released;

    internal AssemblyContextGroup(
        IEnumerable<AssemblyContextParticipant> participants,
        AssemblyContextGroupOptions? options,
        Action<AssemblyContextGroup> onDisposed)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(onDisposed);
        options ??= new AssemblyContextGroupOptions();
        options.Validate();
        _onDisposed = onDisposed;
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

    internal AssemblyImageAccessResult<TResult> UseSnapshot<TResult>(
        ResolvedAssemblyReference assembly,
        Func<AssemblyImageSnapshot, TResult> callback)
    {
        BeginCallback();
        try
        {
            ParticipantState participant = FindParticipant(assembly);
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
        finally
        {
            EndCallback();
        }
    }

    internal TResult UseContext<TResult>(Func<TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        BeginCallback();
        try
        {
            return callback();
        }
        finally
        {
            EndCallback();
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
        finally
        {
            EndCallback();
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

    SnapshotAccess GetSnapshot(ParticipantState participant)
    {
        lock (participant.ImageLoadGate)
        {
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

    void EndCallback()
    {
        bool release;
        lock (_lifetimeGate)
        {
            _activeCallbacks--;
            release =
                _disposed && _activeCallbacks == 0 && !_released;
            if (release)
                _released = true;
        }

        if (release)
            ReleaseOwnedState();
    }

    public void Dispose()
    {
        bool release;
        lock (_lifetimeGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            release = _activeCallbacks == 0 && !_released;
            if (release)
                _released = true;
        }

        _onDisposed(this);

        if (release)
            ReleaseOwnedState();
    }

    void ReleaseOwnedState()
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
            long imageSize = participant.Release();
            if (imageSize != 0)
                ReleaseImage(imageSize);
        }

        if (failures is not null)
            throw new AggregateException(failures);
    }

    sealed class ParticipantState(
        AssemblyContextParticipant participant)
    {
        internal AssemblyContextParticipant Participant { get; } =
            participant;
        internal object ImageLoadGate { get; } = new();
        internal bool Initialized { get; set; }
        internal SnapshotAccess Access { get; set; }

        internal long Release()
        {
            long imageSize = Access.Snapshot?.Length ?? 0;
            Access = default;
            Initialized = false;
            return imageSize;
        }
    }

    readonly record struct SnapshotAccess(
        AssemblyImageSnapshot? Snapshot,
        CandidateOpenFailure? Failure);
}

/// <summary>
/// Shared owner for one or more assembly context groups.
/// </summary>
public sealed class InspectionWorkspace : IDisposable
{
    readonly object _gate = new();
    readonly List<AssemblyContextGroup> _groups = [];
    bool _disposed;

    public AssemblyContextGroup CreateAssemblyContextGroup(
        IEnumerable<AssemblyContextParticipant> participants,
        AssemblyContextGroupOptions? options = null)
    {
        lock (_gate)
            ObjectDisposedException.ThrowIf(_disposed, this);

        var group = new AssemblyContextGroup(
            participants,
            options,
            RemoveGroup);

        lock (_gate)
        {
            if (!_disposed)
            {
                _groups.Add(group);
                return group;
            }
        }

        group.Dispose();
        throw new ObjectDisposedException(nameof(InspectionWorkspace));
    }

    public void Dispose()
    {
        List<AssemblyContextGroup> groups;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            groups = [.. _groups];
            _groups.Clear();
        }

        foreach (AssemblyContextGroup group in groups)
            group.Dispose();
    }

    void RemoveGroup(AssemblyContextGroup group)
    {
        lock (_gate)
            _groups.Remove(group);
    }
}
