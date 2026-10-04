using System.Runtime.CompilerServices;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public abstract class AssemblyContextHierarchyRelationIndexPreparation
{
    private protected AssemblyContextHierarchyRelationIndexPreparation(
        AssemblyContextSubject subject)
    {
        Subject = subject;
    }

    public AssemblyContextSubject Subject { get; }

    public sealed class Ready
        : AssemblyContextHierarchyRelationIndexPreparation
    {
        private readonly PreparedHierarchyRelationIndexStore _owner;

        internal Ready(
            AssemblyContextSubject subject,
            PreparedHierarchyRelationIndexStore owner,
            AssemblyAcquisitionRegistration registration,
            AssemblyInspectionSession session,
            MetadataHierarchyRelationIndex index,
            MetadataOperationPolicy preparationPolicy)
            : base(subject)
        {
            _owner = owner;
            Registration = registration;
            Session = session;
            Index = index;
            PreparationPolicy = preparationPolicy;
        }

        internal AssemblyAcquisitionRegistration Registration { get; }

        internal AssemblyInspectionSession Session { get; }

        internal MetadataHierarchyRelationIndex Index { get; }

        public MetadataOperationPolicy PreparationPolicy { get; }

        public MetadataHierarchyRelationIndexReceipt Receipt =>
            Index.Receipt;

        /// <summary>
        /// Borrows the prepared reverse index for repeated target analysis.
        /// </summary>
        public AssemblyContextHierarchyRelationIndexExecution
            OpenExecution() =>
            _owner.OpenExecution(this);

        public MetadataHierarchyRelationAnalysisOutcome Analyze(
            MetadataHierarchyRelationAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            using AssemblyContextHierarchyRelationIndexExecution execution =
                OpenExecution();
            return execution.Analyze(request, cancellationToken);
        }
    }

    public sealed class ParticipantRejected
        : AssemblyContextHierarchyRelationIndexPreparation
    {
        internal ParticipantRejected(
            AssemblyContextSubject subject,
            CandidateOpenFailure failure)
            : base(subject)
        {
            Failure = failure;
        }

        public CandidateOpenFailure Failure { get; }
    }

    public sealed class InspectionFailed
        : AssemblyContextHierarchyRelationIndexPreparation
    {
        internal InspectionFailed(
            AssemblyContextSubject subject,
            string detail)
            : base(subject)
        {
            ArgumentException.ThrowIfNullOrEmpty(detail);
            Detail = detail;
        }

        public string Detail { get; }
    }

    public sealed class IndexFailed
        : AssemblyContextHierarchyRelationIndexPreparation
    {
        internal IndexFailed(
            AssemblyContextSubject subject,
            MetadataHierarchyRelationIndexReceipt receipt)
            : base(subject)
        {
            Receipt = receipt
                ?? throw new ArgumentNullException(nameof(receipt));
        }

        public MetadataHierarchyRelationIndexReceipt Receipt { get; }
    }

    public sealed class ImageRejected
        : AssemblyContextHierarchyRelationIndexPreparation
    {
        internal ImageRejected(
            AssemblyContextSubject subject,
            MetadataImageFormatResult format,
            string detail)
            : base(subject)
        {
            Format = format
                ?? throw new ArgumentNullException(nameof(format));
            ArgumentException.ThrowIfNullOrEmpty(detail);
            Detail = detail;
        }

        public MetadataImageFormatResult Format { get; }

        public string Detail { get; }
    }
}

/// <summary>
/// A ready hierarchy reverse index whose group borrow is established before
/// target analysis.
/// </summary>
public sealed class AssemblyContextHierarchyRelationIndexExecution
    : IDisposable
{
    private readonly MetadataHierarchyRelationIndex _index;
    private AssemblyContextGroup.AssemblyContextGroupResourceBorrow?
        _borrow;

    internal AssemblyContextHierarchyRelationIndexExecution(
        MetadataHierarchyRelationIndex index,
        AssemblyContextGroup.AssemblyContextGroupResourceBorrow borrow)
    {
        _index = index;
        _borrow = borrow;
    }

    public MetadataHierarchyRelationAnalysisOutcome Analyze(
        MetadataHierarchyRelationAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_borrow is null, this);
        return _index.Analyze(request, cancellationToken);
    }

    public void Dispose()
    {
        AssemblyContextGroup.AssemblyContextGroupResourceBorrow? borrow =
            Interlocked.Exchange(ref _borrow, null);
        borrow?.Dispose();
    }
}

/// <summary>
/// Prepares one target-independent hierarchy reverse index over an assembly
/// context participant.
/// </summary>
public static class AssemblyContextHierarchyRelationIndexQuery
{
    private static readonly Func<
        AssemblyContextGroup,
        PreparedHierarchyRelationIndexStore> s_createStore =
            static owner =>
                new PreparedHierarchyRelationIndexStore(owner);

    public static AssemblyContextHierarchyRelationIndexPreparation
        PrepareParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataOperationPolicy preparationPolicy,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(preparationPolicy);
        cancellationToken.ThrowIfCancellationRequested();

        PreparedHierarchyRelationIndexStore store =
            group.GetOrCreateOwnedResource<
                PreparedHierarchyRelationIndexStore,
                AssemblyContextGroup>(
                    group,
                    s_createStore);
        return store.Prepare(
            participant,
            preparationPolicy,
            cancellationToken);
    }
}

internal sealed class PreparedHierarchyRelationIndexStore
    : IDisposable,
        IAssemblyContextParticipantOwnedResource
{
    private readonly object _gate = new();
    private readonly AssemblyContextGroup _group;
    private readonly Dictionary<
        PreparedHierarchyRelationIndexKey,
        AssemblyContextHierarchyRelationIndexPreparation> _preparations =
            [];
    private readonly Dictionary<
        PreparedHierarchyRelationIndexKey,
        object> _preparationGates = [];
    private bool _disposed;

    internal PreparedHierarchyRelationIndexStore(
        AssemblyContextGroup group)
    {
        _group = group;
    }

    internal AssemblyContextHierarchyRelationIndexPreparation Prepare(
        AssemblyContextParticipant participant,
        MetadataOperationPolicy preparationPolicy,
        CancellationToken cancellationToken)
    {
        var key =
            new PreparedHierarchyRelationIndexKey(
                participant.Assembly.Registration,
                preparationPolicy);
        object preparationGate;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_preparations.TryGetValue(
                    key,
                    out AssemblyContextHierarchyRelationIndexPreparation?
                        existing))
            {
                return existing;
            }
        }

        _group.ValidateParticipant(participant);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_preparations.TryGetValue(
                    key,
                    out AssemblyContextHierarchyRelationIndexPreparation?
                        existing))
            {
                return existing;
            }
            if (!_preparationGates.TryGetValue(
                    key,
                    out preparationGate!))
            {
                preparationGate = new();
                _preparationGates.Add(key, preparationGate);
            }
        }

        try
        {
            lock (preparationGate)
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (_preparations.TryGetValue(
                            key,
                            out AssemblyContextHierarchyRelationIndexPreparation?
                                existing))
                    {
                        return existing;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                using AssemblyContextGroup
                    .AssemblyContextGroupResourceBorrow preparationBorrow =
                        _group.BorrowOwnedResource(
                            this,
                            key.Registration);
                var subject =
                    new AssemblyContextSubject(participant.Assembly);
                AssemblyImageAccessResult<
                    AssemblyContextHierarchyRelationIndexPreparation>
                    access =
                        _group.UseSnapshot(
                            participant,
                            cancellationToken,
                            (
                                Owner: this,
                                Key: key,
                                Subject: subject,
                                Policy: preparationPolicy,
                                CancellationToken: cancellationToken),
                            static (snapshot, state) =>
                                state.Owner.PrepareAndPublish(
                                    state.Key,
                                    state.Subject,
                                    snapshot,
                                    state.Policy,
                                    state.CancellationToken));
                AssemblyContextHierarchyRelationIndexPreparation prepared =
                    access switch
                    {
                        AssemblyImageAccessResult<
                            AssemblyContextHierarchyRelationIndexPreparation>
                            .Available available =>
                                available.Value,
                        AssemblyImageAccessResult<
                            AssemblyContextHierarchyRelationIndexPreparation>
                            .Rejected rejected =>
                                new AssemblyContextHierarchyRelationIndexPreparation
                                    .ParticipantRejected(
                                        subject,
                                        rejected.Failure),
                        _ => throw new InvalidOperationException(
                            "Unknown assembly image access result."),
                    };
                if (access
                    is AssemblyImageAccessResult<
                        AssemblyContextHierarchyRelationIndexPreparation>
                        .Rejected)
                {
                    PublishIfAlive(key, prepared);
                }
                return prepared;
            }
        }
        finally
        {
            RemoveSettledPreparationGate(key, preparationGate);
        }
    }

    internal AssemblyContextHierarchyRelationIndexExecution OpenExecution(
        AssemblyContextHierarchyRelationIndexPreparation.Ready ready)
    {
        AssemblyContextGroup.AssemblyContextGroupResourceBorrow borrow =
            _group.BorrowOwnedResource(
                this,
                ready.Registration);
        try
        {
            var key =
                new PreparedHierarchyRelationIndexKey(
                    ready.Registration,
                    ready.PreparationPolicy);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_preparations.TryGetValue(
                        key,
                        out AssemblyContextHierarchyRelationIndexPreparation?
                            preparation)
                    || !ReferenceEquals(preparation, ready))
                {
                    throw new ObjectDisposedException(
                        nameof(AssemblyContextParticipant));
                }
            }

            return new(
                ready.Index,
                borrow);
        }
        catch
        {
            borrow.Dispose();
            throw;
        }
    }

    private AssemblyContextHierarchyRelationIndexPreparation
        PrepareAndPublish(
            PreparedHierarchyRelationIndexKey key,
            AssemblyContextSubject subject,
            AssemblyImageSnapshot snapshot,
            MetadataOperationPolicy preparationPolicy,
            CancellationToken cancellationToken)
    {
        AssemblyInspectionSession? session;
        try
        {
            session = AssemblyInspectionSession.Open(snapshot);
        }
        catch (Exception ex)
            when (AssemblyContextQueryExecutor.IsArtifactFailure(ex))
        {
            var failed =
                new AssemblyContextHierarchyRelationIndexPreparation
                    .InspectionFailed(
                        subject,
                        ex.Message);
            Publish(key, failed);
            return failed;
        }

        try
        {
            AssemblyContextHierarchyRelationIndexPreparation prepared =
                PrepareIndex(
                    key.Registration,
                    subject,
                    session,
                    preparationPolicy,
                    cancellationToken);
            Publish(key, prepared);
            if (prepared
                is AssemblyContextHierarchyRelationIndexPreparation.Ready)
            {
                session = null;
            }
            return prepared;
        }
        finally
        {
            session?.Dispose();
        }
    }

    private AssemblyContextHierarchyRelationIndexPreparation
        PrepareIndex(
            AssemblyAcquisitionRegistration registration,
            AssemblyContextSubject subject,
            AssemblyInspectionSession session,
            MetadataOperationPolicy preparationPolicy,
            CancellationToken cancellationToken)
    {
        try
        {
            MetadataHierarchyRelationIndexPreparation preparation =
                session.PrepareHierarchyRelationIndex(
                    preparationPolicy,
                    cancellationToken);
            return preparation switch
            {
                MetadataHierarchyRelationIndexPreparation.Ready ready =>
                    new AssemblyContextHierarchyRelationIndexPreparation
                        .Ready(
                            subject,
                            this,
                            registration,
                            session,
                            ready.Index,
                            preparationPolicy),
                MetadataHierarchyRelationIndexPreparation.Failed failed =>
                    new AssemblyContextHierarchyRelationIndexPreparation
                        .IndexFailed(
                            subject,
                            failed.Receipt),
                MetadataHierarchyRelationIndexPreparation.Rejected
                    rejected =>
                    new AssemblyContextHierarchyRelationIndexPreparation
                        .ImageRejected(
                            subject,
                            rejected.Format,
                            rejected.Detail),
                _ => throw new InvalidOperationException(
                    "Unknown Metadata hierarchy-index preparation."),
            };
        }
        catch (Exception ex)
            when (AssemblyContextQueryExecutor.IsArtifactFailure(ex))
        {
            return new
                AssemblyContextHierarchyRelationIndexPreparation
                    .InspectionFailed(
                        subject,
                        ex.Message);
        }
    }

    private void Publish(
        PreparedHierarchyRelationIndexKey key,
        AssemblyContextHierarchyRelationIndexPreparation prepared)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _preparations.Add(key, prepared);
        }
    }

    private void PublishIfAlive(
        PreparedHierarchyRelationIndexKey key,
        AssemblyContextHierarchyRelationIndexPreparation prepared)
    {
        lock (_gate)
        {
            if (!_disposed)
                _preparations.Add(key, prepared);
        }
    }

    private void RemoveSettledPreparationGate(
        PreparedHierarchyRelationIndexKey key,
        object preparationGate)
    {
        lock (_gate)
        {
            if (_preparations.ContainsKey(key)
                && _preparationGates.TryGetValue(
                    key,
                    out object? current)
                && ReferenceEquals(current, preparationGate))
            {
                _preparationGates.Remove(key);
            }
        }
    }

    void IAssemblyContextParticipantOwnedResource
        .ReleaseParticipant(
            AssemblyAcquisitionRegistration registration)
    {
        AssemblyInspectionSession[] sessions;
        lock (_gate)
        {
            if (_disposed)
                return;

            sessions =
                RemoveParticipantPreparations(registration);
        }

        DisposeSessions(sessions);
    }

    private AssemblyInspectionSession[] RemoveParticipantPreparations(
        AssemblyAcquisitionRegistration registration)
    {
        var sessions = new List<AssemblyInspectionSession>();
        PreparedHierarchyRelationIndexKey[] preparationKeys =
            [.. _preparations.Keys.Where(
                key => ReferenceEquals(
                    key.Registration,
                    registration))];
        foreach (PreparedHierarchyRelationIndexKey key
            in preparationKeys)
        {
            if (_preparations[key]
                is AssemblyContextHierarchyRelationIndexPreparation.Ready
                    ready)
            {
                sessions.Add(ready.Session);
            }
            _preparations.Remove(key);
        }

        PreparedHierarchyRelationIndexKey[] gateKeys =
            [.. _preparationGates.Keys.Where(
                key => ReferenceEquals(
                    key.Registration,
                    registration))];
        foreach (PreparedHierarchyRelationIndexKey key in gateKeys)
            _preparationGates.Remove(key);
        return [.. sessions];
    }

    public void Dispose()
    {
        AssemblyInspectionSession[] sessions;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            sessions =
                [.. _preparations.Values
                    .OfType<
                        AssemblyContextHierarchyRelationIndexPreparation
                            .Ready>()
                    .Select(static ready => ready.Session)];
            _preparations.Clear();
            _preparationGates.Clear();
        }

        DisposeSessions(sessions);
    }

    private static void DisposeSessions(
        IEnumerable<AssemblyInspectionSession> sessions)
    {
        List<Exception>? failures = null;
        foreach (AssemblyInspectionSession session in sessions)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }
        if (failures is not null)
            throw new AggregateException(failures);
    }

    private readonly struct PreparedHierarchyRelationIndexKey
        : IEquatable<PreparedHierarchyRelationIndexKey>
    {
        private readonly AssemblyAcquisitionRegistration _registration;
        private readonly MetadataOperationPolicy _preparationPolicy;

        internal PreparedHierarchyRelationIndexKey(
            AssemblyAcquisitionRegistration registration,
            MetadataOperationPolicy preparationPolicy)
        {
            _registration = registration;
            _preparationPolicy = preparationPolicy;
        }

        internal AssemblyAcquisitionRegistration Registration =>
            _registration;

        public bool Equals(
            PreparedHierarchyRelationIndexKey other) =>
            ReferenceEquals(_registration, other._registration)
            && _preparationPolicy == other._preparationPolicy;

        public override bool Equals(object? obj) =>
            obj is PreparedHierarchyRelationIndexKey other
            && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                RuntimeHelpers.GetHashCode(_registration),
                _preparationPolicy);
    }
}
