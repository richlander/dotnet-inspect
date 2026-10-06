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
        private readonly AssemblyContextGroup.ParticipantResource<
            PreparedHierarchyRelationIndexStore> _owner;

        internal Ready(
            AssemblyContextSubject subject,
            AssemblyContextGroup.ParticipantResource<
                PreparedHierarchyRelationIndexStore> owner,
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
            OpenExecution()
        {
            AssemblyContextGroup.ParticipantResourceBorrow<
                PreparedHierarchyRelationIndexStore> borrow =
                    _owner.Borrow(Registration);
            try
            {
                borrow.State.ValidateReady(this);
                return new(
                    Index,
                    borrow);
            }
            catch (Exception operationFailure)
            {
                try
                {
                    borrow.Dispose();
                }
                catch (Exception releaseFailure)
                {
                    throw new AggregateException(
                        operationFailure,
                        releaseFailure);
                }
                throw;
            }
        }

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
    private AssemblyContextGroup.ParticipantResourceBorrow<
        PreparedHierarchyRelationIndexStore>? _borrow;

    internal AssemblyContextHierarchyRelationIndexExecution(
        MetadataHierarchyRelationIndex index,
        AssemblyContextGroup.ParticipantResourceBorrow<
            PreparedHierarchyRelationIndexStore> borrow)
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
        AssemblyContextGroup.ParticipantResourceBorrow<
            PreparedHierarchyRelationIndexStore>? borrow =
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
    private static readonly Func<PreparedHierarchyRelationIndexStore>
        s_createStore =
            static () => new PreparedHierarchyRelationIndexStore();

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

        AssemblyContextGroup.ParticipantResource<
            PreparedHierarchyRelationIndexStore> resource =
                group.GetOrCreateParticipantResource(s_createStore);
        return resource.Prepare(
            participant,
            cancellationToken,
            (
                Resource: resource,
                Registration: participant.Assembly.Registration,
                Subject: new AssemblyContextSubject(participant.Assembly),
                Policy: preparationPolicy,
                CancellationToken: cancellationToken),
            static (store, access, state) =>
                store.Prepare(
                    state.Resource,
                    state.Registration,
                    state.Subject,
                    state.Policy,
                    access,
                    state.CancellationToken));
    }
}

internal sealed class PreparedHierarchyRelationIndexStore
    : IDisposable,
        IAssemblyContextParticipantResourceState
{
    private readonly object _gate = new();
    private readonly Dictionary<
        PreparedHierarchyRelationIndexKey,
        AssemblyContextHierarchyRelationIndexPreparation> _preparations =
            [];
    private readonly Dictionary<
        PreparedHierarchyRelationIndexKey,
        object> _preparationGates = [];
    private bool _disposed;

    internal AssemblyContextHierarchyRelationIndexPreparation Prepare(
        AssemblyContextGroup.ParticipantResource<
            PreparedHierarchyRelationIndexStore> resource,
        AssemblyAcquisitionRegistration registration,
        AssemblyContextSubject subject,
        MetadataOperationPolicy preparationPolicy,
        AssemblyContextParticipantPreparationAccess access,
        CancellationToken cancellationToken)
    {
        var key =
            new PreparedHierarchyRelationIndexKey(
                registration,
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

                return PrepareAndPublish(
                    resource,
                    key,
                    subject,
                    access,
                    preparationPolicy,
                    cancellationToken);
            }
        }
        finally
        {
            RemoveSettledPreparationGate(key, preparationGate);
        }
    }

    internal void ValidateReady(
        AssemblyContextHierarchyRelationIndexPreparation.Ready ready)
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
    }

    private AssemblyContextHierarchyRelationIndexPreparation
        PrepareAndPublish(
            AssemblyContextGroup.ParticipantResource<
                PreparedHierarchyRelationIndexStore> resource,
            PreparedHierarchyRelationIndexKey key,
            AssemblyContextSubject subject,
            AssemblyContextParticipantPreparationAccess access,
            MetadataOperationPolicy preparationPolicy,
            CancellationToken cancellationToken)
    {
        AssemblyContextHierarchyRelationIndexPreparation prepared =
            access switch
            {
                AssemblyContextParticipantPreparationAccess.Available
                    available =>
                        PrepareAvailable(
                            resource,
                            key,
                            subject,
                            available.Snapshot,
                            preparationPolicy,
                            cancellationToken),
                AssemblyContextParticipantPreparationAccess.Rejected
                    rejected =>
                        new AssemblyContextHierarchyRelationIndexPreparation
                            .ParticipantRejected(
                                subject,
                                rejected.Failure),
                _ => throw new InvalidOperationException(
                    "Unknown participant preparation access."),
            };
        Publish(key, prepared);
        return prepared;
    }

    private AssemblyContextHierarchyRelationIndexPreparation
        PrepareAvailable(
            AssemblyContextGroup.ParticipantResource<
                PreparedHierarchyRelationIndexStore> resource,
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
            return failed;
        }

        try
        {
            AssemblyContextHierarchyRelationIndexPreparation prepared =
                PrepareIndex(
                    resource,
                    key.Registration,
                    subject,
                    session,
                    preparationPolicy,
                    cancellationToken);
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
            AssemblyContextGroup.ParticipantResource<
                PreparedHierarchyRelationIndexStore> resource,
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
                            resource,
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

    void IAssemblyContextParticipantResourceState
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
