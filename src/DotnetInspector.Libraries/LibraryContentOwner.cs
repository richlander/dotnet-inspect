using System.Collections.ObjectModel;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using Inspector.Resources;

namespace DotnetInspector.Libraries;

/// <summary>The observable lifetime state of one Library content owner.</summary>
public enum LibraryContentOwnerState
{
    Active,
    Retiring,
    Released,
    ReleaseFailed,
}

/// <summary>
/// Associates one failed child release with its exact Library content.
/// </summary>
public sealed class LibraryContentReleaseFailure
{
    internal LibraryContentReleaseFailure(
        LibraryContentReference content,
        Exception failure)
    {
        Content = content;
        Failure = failure;
    }

    public LibraryContentReference Content { get; }
    public Exception Failure { get; }
}

/// <summary>The closed result of requesting one Library operation lease.</summary>
public abstract class LibraryOperationLeaseIssueOutcome
{
    private protected LibraryOperationLeaseIssueOutcome()
    {
    }

    /// <summary>Ownership of one exact operation lease was transferred.</summary>
    public sealed class Issued : LibraryOperationLeaseIssueOutcome
    {
        internal Issued(LibraryOperationLease lease) => Lease = lease;

        public LibraryOperationLease Lease { get; }
    }

    /// <summary>The owner is draining previously issued operations.</summary>
    public sealed class OwnerRetiring : LibraryOperationLeaseIssueOutcome
    {
        internal OwnerRetiring()
        {
        }
    }

    /// <summary>The owner has reached a terminal released state.</summary>
    public sealed class OwnerReleased : LibraryOperationLeaseIssueOutcome
    {
        internal OwnerReleased(LibraryContentOwnerState state)
        {
            if (state is not LibraryContentOwnerState.Released
                and not LibraryContentOwnerState.ReleaseFailed)
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            State = state;
        }

        public LibraryContentOwnerState State { get; }
    }

    /// <summary>The requested Library is not owned by this resource.</summary>
    public sealed class ReferenceMismatch : LibraryOperationLeaseIssueOutcome
    {
        internal ReferenceMismatch(
            LibraryReference ownerReference,
            LibraryReference requestedReference)
        {
            OwnerReference = ownerReference;
            RequestedReference = requestedReference;
        }

        public LibraryReference OwnerReference { get; }
        public LibraryReference RequestedReference { get; }
    }
}

/// <summary>
/// Owns the Artifact content obligations for one exact realized Library.
/// </summary>
[ResourceOwnership]
public sealed class LibraryContentOwner : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<LibraryContentReference, ArtifactContentLease>
        _contentLeases;
    private ArtifactContentLease[] _releaseOrder;
    private readonly HashSet<LibraryOperationLease> _operations =
        new(ReferenceEqualityComparer.Instance);
    private TaskCompletionSource? _operationQuiescence;
    private Task? _retirementTask;
    private IReadOnlyList<Exception> _cleanupFailures = [];
    private IReadOnlyList<LibraryContentReleaseFailure>
        _releaseFailures = [];
    private LibraryContentOwnerState _state =
        LibraryContentOwnerState.Active;

    /// <summary>
    /// Atomically accepts one exact Artifact child for every Library content
    /// reference in Library order.
    /// </summary>
    public LibraryContentOwner(
        LibraryReference reference,
        IReadOnlyList<ArtifactContentLease> contentLeases)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(contentLeases);
        if (contentLeases.Count != reference.Contents.Count)
        {
            throw new ArgumentException(
                "Library ownership requires one Artifact content lease for every Library content reference.",
                nameof(contentLeases));
        }

        ArtifactContentLease[] leases = [.. contentLeases];
        var seen = new HashSet<ArtifactContentLease>(
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < leases.Length; index++)
        {
            ArtifactContentLease lease =
                leases[index]
                ?? throw new ArgumentException(
                    "Library content leases cannot contain null.",
                    nameof(contentLeases));
            if (!seen.Add(lease))
            {
                throw new ArgumentException(
                    "A Library cannot accept one Artifact content lease more than once.",
                    nameof(contentLeases));
            }

            LibraryContentReference content = reference.Contents[index];
            if (!ReferenceEquals(
                    lease.Reference,
                    content.ArtifactReference))
            {
                throw new ArgumentException(
                    "Every Artifact content lease must match the Library content reference at the same position.",
                    nameof(contentLeases));
            }

            _ = RequireArtifactAccess(
                lease.WithContent(
                    static (_, _) => true),
                nameof(contentLeases));
        }

        Reference = reference;
        _releaseOrder = leases;
        _contentLeases =
            new(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < leases.Length; index++)
        {
            _contentLeases.Add(
                reference.Contents[index],
                leases[index]);
        }
    }

    public LibraryReference Reference { get; }

    public LibraryContentOwnerState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public IReadOnlyList<Exception> CleanupFailures
    {
        get
        {
            lock (_gate)
                return _cleanupFailures;
        }
    }

    public IReadOnlyList<LibraryContentReleaseFailure> ReleaseFailures
    {
        get
        {
            lock (_gate)
                return _releaseFailures;
        }
    }

    /// <summary>
    /// Issues one operation over this exact Library while the owner is active.
    /// </summary>
    public LibraryOperationLeaseIssueOutcome IssueOperationLease(
        LibraryReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        lock (_gate)
        {
            if (!ReferenceEquals(Reference, reference))
            {
                return new LibraryOperationLeaseIssueOutcome
                    .ReferenceMismatch(Reference, reference);
            }

            switch (_state)
            {
                case LibraryContentOwnerState.Retiring:
                    return new LibraryOperationLeaseIssueOutcome
                        .OwnerRetiring();
                case LibraryContentOwnerState.Released:
                case LibraryContentOwnerState.ReleaseFailed:
                    return new LibraryOperationLeaseIssueOutcome
                        .OwnerReleased(_state);
                case LibraryContentOwnerState.Active:
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Library content owner state.");
            }

            if (_operations.Count == 0)
            {
                _operationQuiescence =
                    new(
                        TaskCreationOptions
                            .RunContinuationsAsynchronously);
            }

            var lease = new LibraryOperationLease(this, Reference);
            _operations.Add(lease);
            return new LibraryOperationLeaseIssueOutcome.Issued(lease);
        }
    }

    /// <summary>
    /// Rejects new operations, drains issued operations, and then releases all
    /// accepted Artifact children.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? starter = null;
        Task operationQuiescence = Task.CompletedTask;
        Task retirement;
        lock (_gate)
        {
            if (_retirementTask is null)
            {
                starter =
                    new(
                        TaskCreationOptions
                            .RunContinuationsAsynchronously);
                _retirementTask = starter.Task;
                _state = LibraryContentOwnerState.Retiring;
                operationQuiescence =
                    _operations.Count == 0
                        ? Task.CompletedTask
                        : _operationQuiescence!.Task;
            }

            retirement = _retirementTask;
        }

        if (starter is not null)
        {
            _ = CompleteRetirementAsync(
                starter,
                operationQuiescence);
        }

        return new ValueTask(retirement);
    }

    internal TResult Snapshot<TState, TResult>(
        LibraryOperationLease operation,
        LibraryContentReference content,
        scoped TState state,
        LibraryContentSnapshotCallback<TState, TResult> callback,
        CancellationToken cancellationToken)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();
        ArtifactContentLease child =
            BeginSnapshot(operation, content);
        try
        {
            var snapshot = new SingleSnapshotState<TState, TResult>(
                content,
                state,
                callback);
            return RequireArtifactAccess(
                child.WithContent(
                    snapshot,
                    ReadSingle<TState, TResult>,
                    cancellationToken),
                nameof(content));
        }
        finally
        {
            EndSnapshot(operation);
        }
    }

    internal TResult SnapshotPair<TState, TResult>(
        LibraryOperationLease operation,
        LibraryContentReference first,
        LibraryContentReference second,
        scoped TState state,
        LibraryContentPairSnapshotCallback<TState, TResult> callback,
        CancellationToken cancellationToken)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();
        (ArtifactContentLease First, ArtifactContentLease Second) children =
            BeginPairSnapshot(operation, first, second);
        try
        {
            var snapshot = new PairOuterSnapshotState<TState, TResult>(
                first,
                second,
                children.Second,
                state,
                callback);
            return RequireArtifactAccess(
                children.First.WithContent(
                    snapshot,
                    ReadPairFirst<TState, TResult>,
                    cancellationToken),
                nameof(first));
        }
        finally
        {
            EndSnapshot(operation);
        }
    }

    internal void ReleaseOperation(LibraryOperationLease operation)
    {
        TaskCompletionSource? completion = null;
        lock (_gate)
        {
            if (!operation.IsOwnedBy(this))
                return;
            if (!_operations.Contains(operation))
            {
                throw new InvalidOperationException(
                    "The Library operation lease is not registered with its owner.");
            }
            if (operation.ActiveSnapshots != 0)
            {
                throw new InvalidOperationException(
                    "A Library operation lease cannot be released while a snapshot is active.");
            }

            _operations.Remove(operation);
            operation.MarkReleased();
            if (_operations.Count == 0)
                completion = _operationQuiescence;
        }

        completion?.TrySetResult();
    }

    private ArtifactContentLease BeginSnapshot(
        LibraryOperationLease operation,
        LibraryContentReference content)
    {
        lock (_gate)
        {
            ValidateOperation(operation);
            if (!_contentLeases.TryGetValue(
                    content,
                    out ArtifactContentLease? child))
            {
                throw new ArgumentException(
                    "The content reference is not part of this Library.",
                    nameof(content));
            }

            operation.ActiveSnapshots =
                checked(operation.ActiveSnapshots + 1);
            return child;
        }
    }

    private (
        ArtifactContentLease First,
        ArtifactContentLease Second)
        BeginPairSnapshot(
            LibraryOperationLease operation,
            LibraryContentReference first,
            LibraryContentReference second)
    {
        lock (_gate)
        {
            ValidateOperation(operation);
            if (ReferenceEquals(first, second))
            {
                throw new ArgumentException(
                    "A Library pair snapshot requires two distinct content references.",
                    nameof(second));
            }
            if (!_contentLeases.TryGetValue(
                    first,
                    out ArtifactContentLease? firstChild))
            {
                throw new ArgumentException(
                    "The first content reference is not part of this Library.",
                    nameof(first));
            }
            if (!_contentLeases.TryGetValue(
                    second,
                    out ArtifactContentLease? secondChild))
            {
                throw new ArgumentException(
                    "The second content reference is not part of this Library.",
                    nameof(second));
            }

            operation.ActiveSnapshots =
                checked(operation.ActiveSnapshots + 1);
            return (firstChild, secondChild);
        }
    }

    private void ValidateOperation(
        LibraryOperationLease operation)
    {
        if (!operation.IsOwnedBy(this)
            || !_operations.Contains(operation))
        {
            throw new ObjectDisposedException(
                nameof(LibraryOperationLease));
        }
    }

    private void EndSnapshot(LibraryOperationLease operation)
    {
        lock (_gate)
        {
            if (!operation.IsOwnedBy(this)
                || !_operations.Contains(operation)
                || operation.ActiveSnapshots <= 0)
            {
                throw new InvalidOperationException(
                    "Library snapshot completion was unbalanced.");
            }

            operation.ActiveSnapshots--;
        }
    }

    private async Task CompleteRetirementAsync(
        TaskCompletionSource starter,
        Task operationQuiescence)
    {
        try
        {
            await operationQuiescence.ConfigureAwait(false);
            ArtifactContentLease[] children;
            lock (_gate)
                children = _releaseOrder;

            var releaseFailures =
                new List<LibraryContentReleaseFailure>();
            for (int index = children.Length - 1; index >= 0; index--)
            {
                try
                {
                    children[index].Dispose();
                }
                catch (Exception ex)
                {
                    releaseFailures.Add(
                        new(
                            Reference.Contents[index],
                            ex));
                }
            }

            List<Exception> failures =
                releaseFailures
                    .Select(static failure => failure.Failure)
                    .ToList();
            IReadOnlyList<Exception> cleanupFailures =
                failures.Count == 0
                    ? []
                    : new ReadOnlyCollection<Exception>(failures);
            IReadOnlyList<LibraryContentReleaseFailure>
                contentReleaseFailures =
                    releaseFailures.Count == 0
                        ? []
                        : new ReadOnlyCollection<
                            LibraryContentReleaseFailure>(
                                releaseFailures);
            lock (_gate)
            {
                _releaseOrder = [];
                _contentLeases.Clear();
                _cleanupFailures = cleanupFailures;
                _releaseFailures = contentReleaseFailures;
                _state =
                    failures.Count == 0
                        ? LibraryContentOwnerState.Released
                        : LibraryContentOwnerState.ReleaseFailed;
            }

            if (failures.Count == 0)
                starter.SetResult();
            else
                starter.SetException(new AggregateException(failures));
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _cleanupFailures =
                    new ReadOnlyCollection<Exception>([ex]);
                _releaseFailures = [];
                _state = LibraryContentOwnerState.ReleaseFailed;
            }
            starter.SetException(ex);
        }
    }

    private static TResult ReadSingle<TState, TResult>(
        scoped ArtifactContentView artifact,
        scoped SingleSnapshotState<TState, TResult> state,
        CancellationToken cancellationToken)
        where TState : allows ref struct
    {
        if (!ReferenceEquals(
                artifact.Reference,
                state.Content.ArtifactReference))
        {
            throw new InvalidOperationException(
                "Artifact supplied content for another Library reference.");
        }

        return state.Callback(
            new LibraryContentView(
                state.Content,
                artifact.Content),
            state.State,
            cancellationToken);
    }

    private static TResult ReadPairFirst<TState, TResult>(
        scoped ArtifactContentView first,
        scoped PairOuterSnapshotState<TState, TResult> state,
        CancellationToken cancellationToken)
        where TState : allows ref struct
    {
        if (!ReferenceEquals(
                first.Reference,
                state.First.ArtifactReference))
        {
            throw new InvalidOperationException(
                "Artifact supplied the first content for another Library reference.");
        }

        var inner = new PairInnerSnapshotState<TState, TResult>(
            first,
            state);
        return RequireArtifactAccess(
            state.SecondLease.WithContent(
                inner,
                ReadPairSecond<TState, TResult>,
                cancellationToken),
            nameof(state.Second));
    }

    private static TResult ReadPairSecond<TState, TResult>(
        scoped ArtifactContentView second,
        scoped PairInnerSnapshotState<TState, TResult> state,
        CancellationToken cancellationToken)
        where TState : allows ref struct
    {
        if (!ReferenceEquals(
                second.Reference,
                state.Second.ArtifactReference))
        {
            throw new InvalidOperationException(
                "Artifact supplied the second content for another Library reference.");
        }

        return state.Callback(
            new LibraryContentPairView(
                new(
                    state.First,
                    state.FirstArtifact.Content),
                new(
                    state.Second,
                    second.Content)),
            state.State,
            cancellationToken);
    }

    private static TResult RequireArtifactAccess<TResult>(
        ArtifactContentAccessOutcome<TResult> outcome,
        string parameterName) =>
        outcome switch
        {
            ArtifactContentAccessOutcome<TResult>.Accessed accessed =>
                accessed.Value,
            ArtifactContentAccessOutcome<TResult>.Unauthorized =>
                throw new UnauthorizedAccessException(
                    $"Artifact content authority was rejected for {parameterName}."),
            _ => throw new InvalidOperationException(
                "Unknown Artifact content access outcome."),
        };

    private readonly ref struct SingleSnapshotState<TState, TResult>
        where TState : allows ref struct
    {
        internal SingleSnapshotState(
            LibraryContentReference content,
            TState state,
            LibraryContentSnapshotCallback<TState, TResult> callback)
        {
            Content = content;
            State = state;
            Callback = callback;
        }

        internal LibraryContentReference Content { get; }
        internal TState State { get; }
        internal LibraryContentSnapshotCallback<TState, TResult>
            Callback { get; }
    }

    private readonly ref struct PairOuterSnapshotState<TState, TResult>
        where TState : allows ref struct
    {
        internal PairOuterSnapshotState(
            LibraryContentReference first,
            LibraryContentReference second,
            ArtifactContentLease secondLease,
            TState state,
            LibraryContentPairSnapshotCallback<TState, TResult> callback)
        {
            First = first;
            Second = second;
            SecondLease = secondLease;
            State = state;
            Callback = callback;
        }

        internal LibraryContentReference First { get; }
        internal LibraryContentReference Second { get; }
        internal ArtifactContentLease SecondLease { get; }
        internal TState State { get; }
        internal LibraryContentPairSnapshotCallback<TState, TResult>
            Callback { get; }
    }

    private readonly ref struct PairInnerSnapshotState<TState, TResult>
        where TState : allows ref struct
    {
        internal PairInnerSnapshotState(
            ArtifactContentView firstArtifact,
            PairOuterSnapshotState<TState, TResult> outer)
        {
            FirstArtifact = firstArtifact;
            First = outer.First;
            Second = outer.Second;
            State = outer.State;
            Callback = outer.Callback;
        }

        internal ArtifactContentView FirstArtifact { get; }
        internal LibraryContentReference First { get; }
        internal LibraryContentReference Second { get; }
        internal TState State { get; }
        internal LibraryContentPairSnapshotCallback<TState, TResult>
            Callback { get; }
    }
}

/// <summary>Owner-attested bytes for one exact Library content reference.</summary>
public readonly ref struct LibraryContentView
{
    internal LibraryContentView(
        LibraryContentReference reference,
        ReadOnlySpan<byte> content)
    {
        Reference = reference;
        Content = content;
    }

    public LibraryContentReference Reference { get; }
    public ReadOnlySpan<byte> Content { get; }
}

/// <summary>
/// Owner-attested bytes for two distinct exact Library content references.
/// </summary>
public readonly ref struct LibraryContentPairView
{
    internal LibraryContentPairView(
        LibraryContentView first,
        LibraryContentView second)
    {
        First = first;
        Second = second;
    }

    public LibraryContentView First { get; }
    public LibraryContentView Second { get; }
}

public delegate TResult LibraryContentSnapshotCallback<TResult>(
    scoped LibraryContentView view,
    CancellationToken cancellationToken);

public delegate TResult LibraryContentSnapshotCallback<TState, TResult>(
    scoped LibraryContentView view,
    scoped TState state,
    CancellationToken cancellationToken)
    where TState : allows ref struct;

public delegate TResult LibraryContentPairSnapshotCallback<TResult>(
    scoped LibraryContentPairView view,
    CancellationToken cancellationToken);

public delegate TResult LibraryContentPairSnapshotCallback<TState, TResult>(
    scoped LibraryContentPairView view,
    scoped TState state,
    CancellationToken cancellationToken)
    where TState : allows ref struct;

/// <summary>
/// Operation-scoped authority over one exact realized Library.
/// </summary>
[ResourceOwnership]
public sealed class LibraryOperationLease : IDisposable
{
    private LibraryContentOwner? _owner;

    internal LibraryOperationLease(
        LibraryContentOwner owner,
        LibraryReference reference)
    {
        _owner = owner;
        Reference = reference;
    }

    public LibraryReference Reference { get; }

    public TResult Snapshot<TResult>(
        LibraryContentReference content,
        LibraryContentSnapshotCallback<TResult> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Snapshot(
            content,
            callback,
            static (view, callback, token) =>
                callback(view, token),
            cancellationToken);
    }

    public TResult Snapshot<TState, TResult>(
        LibraryContentReference content,
        scoped TState state,
        LibraryContentSnapshotCallback<TState, TResult> callback,
        CancellationToken cancellationToken = default)
        where TState : allows ref struct
    {
        LibraryContentOwner owner =
            Volatile.Read(ref _owner)
            ?? throw new ObjectDisposedException(
                nameof(LibraryOperationLease));
        return owner.Snapshot(
            this,
            content,
            state,
            callback,
            cancellationToken);
    }

    public TResult SnapshotPair<TResult>(
        LibraryContentReference first,
        LibraryContentReference second,
        LibraryContentPairSnapshotCallback<TResult> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return SnapshotPair(
            first,
            second,
            callback,
            static (view, callback, token) =>
                callback(view, token),
            cancellationToken);
    }

    public TResult SnapshotPair<TState, TResult>(
        LibraryContentReference first,
        LibraryContentReference second,
        scoped TState state,
        LibraryContentPairSnapshotCallback<TState, TResult> callback,
        CancellationToken cancellationToken = default)
        where TState : allows ref struct
    {
        LibraryContentOwner owner =
            Volatile.Read(ref _owner)
            ?? throw new ObjectDisposedException(
                nameof(LibraryOperationLease));
        return owner.SnapshotPair(
            this,
            first,
            second,
            state,
            callback,
            cancellationToken);
    }

    public void Dispose()
    {
        LibraryContentOwner? owner = Volatile.Read(ref _owner);
        owner?.ReleaseOperation(this);
    }

    internal bool IsOwnedBy(LibraryContentOwner owner) =>
        ReferenceEquals(Volatile.Read(ref _owner), owner);

    internal int ActiveSnapshots { get; set; }

    internal void MarkReleased() =>
        Volatile.Write(ref _owner, null);
}
