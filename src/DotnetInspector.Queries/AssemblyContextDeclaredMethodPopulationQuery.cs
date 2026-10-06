using System.Runtime.CompilerServices;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public abstract class
    AssemblyContextDeclaredMethodPopulationPreparation
{
    private protected AssemblyContextDeclaredMethodPopulationPreparation(
        AssemblyContextSubject subject)
    {
        Subject = subject;
    }

    public AssemblyContextSubject Subject { get; }

    public sealed class Ready
        : AssemblyContextDeclaredMethodPopulationPreparation
    {
        private readonly AssemblyContextGroup.ParticipantResource<
            PreparedDeclaredMethodPopulationStore> _owner;

        internal Ready(
            AssemblyContextSubject subject,
            AssemblyContextGroup.ParticipantResource<
                PreparedDeclaredMethodPopulationStore> owner,
            AssemblyAcquisitionRegistration registration,
            AssemblyInspectionSession session,
            MetadataDeclaredMethodPopulationSource source,
            MetadataTypeDefinitionBinding binding)
            : base(subject)
        {
            _owner = owner;
            Registration = registration;
            Session = session;
            Source = source;
            Binding = binding;
        }

        internal AssemblyInspectionSession Session { get; }

        internal AssemblyAcquisitionRegistration Registration { get; }

        internal MetadataDeclaredMethodPopulationSource Source { get; }

        public MetadataTypeDefinitionBinding Binding { get; }

        /// <summary>
        /// Borrows the prepared source for repeated terminal execution.
        /// </summary>
        public AssemblyContextDeclaredMethodPopulationExecution
            OpenExecution()
        {
            AssemblyContextGroup.ParticipantResourceBorrow<
                PreparedDeclaredMethodPopulationStore> borrow =
                    _owner.Borrow(Registration);
            try
            {
                borrow.State.ValidateReady(this);
                return new(
                    Source,
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

        public MetadataDeclaredMethodPopulationResult Count()
        {
            using AssemblyContextDeclaredMethodPopulationExecution execution =
                OpenExecution();
            return execution.Count();
        }

        public MetadataDeclaredMethodPopulationResult Rows(
            int maximumRows = int.MaxValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);
            using AssemblyContextDeclaredMethodPopulationExecution execution =
                OpenExecution();
            return execution.Rows(maximumRows);
        }
    }

    public sealed class ParticipantRejected
        : AssemblyContextDeclaredMethodPopulationPreparation
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

    public sealed class BindingRejected
        : AssemblyContextDeclaredMethodPopulationPreparation
    {
        internal BindingRejected(
            AssemblyContextSubject subject,
            MetadataDeclaredMethodPopulationRejection reason)
            : base(subject)
        {
            Reason = reason;
        }

        public MetadataDeclaredMethodPopulationRejection Reason { get; }
    }

    public sealed class Failed
        : AssemblyContextDeclaredMethodPopulationPreparation
    {
        internal Failed(
            AssemblyContextSubject subject,
            string detail)
            : base(subject)
        {
            ArgumentException.ThrowIfNullOrEmpty(detail);
            Detail = detail;
        }

        public string Detail { get; }
    }
}

/// <summary>
/// A ready declared-method source whose group borrow is established before
/// terminal execution.
/// </summary>
public sealed class AssemblyContextDeclaredMethodPopulationExecution
    : IDisposable
{
    private readonly MetadataDeclaredMethodPopulationSource _source;
    private AssemblyContextGroup.ParticipantResourceBorrow<
        PreparedDeclaredMethodPopulationStore>? _borrow;

    internal AssemblyContextDeclaredMethodPopulationExecution(
        MetadataDeclaredMethodPopulationSource source,
        AssemblyContextGroup.ParticipantResourceBorrow<
            PreparedDeclaredMethodPopulationStore> borrow)
    {
        _source = source;
        _borrow = borrow;
    }

    public MetadataDeclaredMethodPopulationResult Count()
    {
        ObjectDisposedException.ThrowIf(_borrow is null, this);
        return _source.Count();
    }

    public MetadataDeclaredMethodPopulationResult Rows(
        int maximumRows = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);
        ObjectDisposedException.ThrowIf(_borrow is null, this);
        return _source.Rows(maximumRows);
    }

    public void Dispose()
    {
        AssemblyContextGroup.ParticipantResourceBorrow<
            PreparedDeclaredMethodPopulationStore>? borrow =
                Interlocked.Exchange(ref _borrow, null);
        borrow?.Dispose();
    }
}

/// <summary>
/// Executes one authenticated TypeDef's declared-MethodDef population while
/// the participant session is alive.
/// </summary>
public static class AssemblyContextDeclaredMethodPopulationQuery
{
    private static readonly Func<PreparedDeclaredMethodPopulationStore>
        s_createStore =
            static () => new PreparedDeclaredMethodPopulationStore();

    public static AssemblyContextDeclaredMethodPopulationPreparation
        PrepareParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataTypeDefinitionBinding type,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(type);
        cancellationToken.ThrowIfCancellationRequested();

        AssemblyContextGroup.ParticipantResource<
            PreparedDeclaredMethodPopulationStore> resource =
                group.GetOrCreateParticipantResource(s_createStore);
        return resource.Prepare(
            participant,
            cancellationToken,
            (
                Resource: resource,
                Registration: participant.Assembly.Registration,
                Subject: new AssemblyContextSubject(participant.Assembly),
                Type: type),
            static (store, access, state) =>
                store.Prepare(
                    state.Resource,
                    state.Registration,
                    state.Subject,
                    state.Type,
                    access));
    }

    public static AssemblyContextEntry<
        MetadataDeclaredMethodPopulationOutcome> ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataTypeDefinitionBinding type,
            MetadataDeclaredMethodPopulationTerminal terminal,
            int maximumRows = int.MaxValue,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);

        return AssemblyContextQueryExecutor.ExecuteParticipant<
            MetadataDeclaredMethodPopulationOutcome>(
            group,
            participant,
            session =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return session.DeclaredMethods(
                    new(
                        type,
                        terminal,
                        maximumRows));
            });
    }
}

internal sealed class PreparedDeclaredMethodPopulationStore
    : IDisposable,
        IAssemblyContextParticipantResourceState
{
    private readonly object _gate = new();
    private readonly Dictionary<
        PreparedDeclaredMethodPopulationKey,
        AssemblyContextDeclaredMethodPopulationPreparation> _preparations =
            [];
    private readonly Dictionary<
        PreparedDeclaredMethodPopulationKey,
        object> _preparationGates = [];
    private readonly Dictionary<
        AssemblyAcquisitionRegistration,
        PreparedParticipantSession> _sessionByRegistration =
            new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<
        AssemblyAcquisitionRegistration,
        object> _sessionGates =
            new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    internal AssemblyContextDeclaredMethodPopulationPreparation Prepare(
        AssemblyContextGroup.ParticipantResource<
            PreparedDeclaredMethodPopulationStore> resource,
        AssemblyAcquisitionRegistration registration,
        AssemblyContextSubject subject,
        MetadataTypeDefinitionBinding type,
        AssemblyContextParticipantPreparationAccess access)
    {
        var key =
            new PreparedDeclaredMethodPopulationKey(
                registration,
                type.ModuleVersionId,
                type.Definition.Value);
        object preparationGate;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_preparations.TryGetValue(
                    key,
                    out AssemblyContextDeclaredMethodPopulationPreparation?
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
                            out AssemblyContextDeclaredMethodPopulationPreparation?
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
                    type);
            }
        }
        finally
        {
            RemoveSettledPreparationGate(key, preparationGate);
        }
    }

    internal void ValidateReady(
        AssemblyContextDeclaredMethodPopulationPreparation.Ready ready)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessionByRegistration.TryGetValue(
                    ready.Registration,
                    out PreparedParticipantSession? participant)
                || !ReferenceEquals(
                    participant.Session,
                    ready.Session))
            {
                throw new ObjectDisposedException(
                    nameof(AssemblyContextParticipant));
            }
        }
    }

    private AssemblyContextDeclaredMethodPopulationPreparation
        PrepareAndPublish(
            AssemblyContextGroup.ParticipantResource<
                PreparedDeclaredMethodPopulationStore> resource,
            PreparedDeclaredMethodPopulationKey key,
            AssemblyContextSubject subject,
            AssemblyContextParticipantPreparationAccess access,
            MetadataTypeDefinitionBinding type)
    {
        AssemblyContextDeclaredMethodPopulationPreparation prepared =
            access switch
            {
                AssemblyContextParticipantPreparationAccess.Available
                    available =>
                        PrepareAvailable(
                            resource,
                            key,
                            subject,
                            available.Snapshot,
                            type),
                AssemblyContextParticipantPreparationAccess.Rejected
                    rejected =>
                        new AssemblyContextDeclaredMethodPopulationPreparation
                            .ParticipantRejected(
                                subject,
                                rejected.Failure),
                _ => throw new InvalidOperationException(
                    "Unknown participant preparation access."),
            };
        Publish(key, prepared);
        return prepared;
    }

    private AssemblyContextDeclaredMethodPopulationPreparation
        PrepareAvailable(
            AssemblyContextGroup.ParticipantResource<
                PreparedDeclaredMethodPopulationStore> resource,
            PreparedDeclaredMethodPopulationKey key,
            AssemblyContextSubject subject,
            AssemblyImageSnapshot snapshot,
            MetadataTypeDefinitionBinding type)
    {
        PreparedParticipantSession participantSession =
            GetOrCreateParticipantSession(
                key.Registration,
                snapshot);
        return participantSession.Session is { } session
            ? Prepare(
                resource,
                key.Registration,
                subject,
                session,
                type)
            : new AssemblyContextDeclaredMethodPopulationPreparation
                .Failed(
                    subject,
                    participantSession.Failure!);
    }

    private PreparedParticipantSession GetOrCreateParticipantSession(
        AssemblyAcquisitionRegistration registration,
        AssemblyImageSnapshot snapshot)
    {
        object sessionGate;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessionByRegistration.TryGetValue(
                    registration,
                    out PreparedParticipantSession? existing))
            {
                return existing;
            }
            if (!_sessionGates.TryGetValue(
                    registration,
                    out sessionGate!))
            {
                sessionGate = new();
                _sessionGates.Add(registration, sessionGate);
            }
        }

        lock (sessionGate)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_sessionByRegistration.TryGetValue(
                        registration,
                        out PreparedParticipantSession? existing))
                {
                    return existing;
                }
            }

            PreparedParticipantSession prepared;
            try
            {
                prepared = new(
                    AssemblyInspectionSession.Open(snapshot),
                    Failure: null);
            }
            catch (Exception ex)
                when (AssemblyContextQueryExecutor.IsArtifactFailure(ex))
            {
                prepared = new(
                    Session: null,
                    Failure: ex.Message);
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    prepared.Session?.Dispose();
                    throw new ObjectDisposedException(GetType().Name);
                }
                _sessionByRegistration.Add(
                    registration,
                    prepared);
            }
            return prepared;
        }
    }

    private AssemblyContextDeclaredMethodPopulationPreparation Prepare(
        AssemblyContextGroup.ParticipantResource<
            PreparedDeclaredMethodPopulationStore> resource,
        AssemblyAcquisitionRegistration registration,
        AssemblyContextSubject subject,
        AssemblyInspectionSession session,
        MetadataTypeDefinitionBinding type)
    {
        try
        {
            MetadataDeclaredMethodPopulationPreparation preparation =
                session.PrepareDeclaredMethods(type);
            switch (preparation)
            {
                case MetadataDeclaredMethodPopulationPreparation.Ready
                    ready:
                    var result =
                        new AssemblyContextDeclaredMethodPopulationPreparation
                            .Ready(
                                subject,
                                resource,
                                registration,
                                session,
                                ready.Source,
                                type);
                    return result;
                case MetadataDeclaredMethodPopulationPreparation.Rejected
                    rejected:
                    return new
                        AssemblyContextDeclaredMethodPopulationPreparation
                            .BindingRejected(
                                subject,
                                rejected.Reason);
                case MetadataDeclaredMethodPopulationPreparation.Failed
                    failed:
                    return new
                        AssemblyContextDeclaredMethodPopulationPreparation
                            .Failed(
                                subject,
                                failed.Detail);
                default:
                    throw new InvalidOperationException(
                        "Unknown Metadata declared-method preparation.");
            }
        }
        catch (Exception ex)
            when (AssemblyContextQueryExecutor.IsArtifactFailure(ex))
        {
            return new
                AssemblyContextDeclaredMethodPopulationPreparation.Failed(
                    subject,
                    ex.Message);
        }
    }

    private void Publish(
        PreparedDeclaredMethodPopulationKey key,
        AssemblyContextDeclaredMethodPopulationPreparation prepared)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _preparations.Add(key, prepared);
        }
    }

    private void RemoveSettledPreparationGate(
        PreparedDeclaredMethodPopulationKey key,
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
        AssemblyInspectionSession? session = null;
        lock (_gate)
        {
            if (_disposed)
                return;

            RemoveParticipantPreparations(registration);
            _sessionGates.Remove(registration);
            if (_sessionByRegistration.Remove(
                    registration,
                    out PreparedParticipantSession? participant))
            {
                session = participant.Session;
            }
        }

        session?.Dispose();
    }

    private void RemoveParticipantPreparations(
        AssemblyAcquisitionRegistration registration)
    {
        PreparedDeclaredMethodPopulationKey[] preparationKeys =
            [.. _preparations.Keys.Where(
                key => ReferenceEquals(
                    key.Registration,
                    registration))];
        foreach (PreparedDeclaredMethodPopulationKey key
            in preparationKeys)
        {
            _preparations.Remove(key);
        }

        PreparedDeclaredMethodPopulationKey[] gateKeys =
            [.. _preparationGates.Keys.Where(
                key => ReferenceEquals(
                    key.Registration,
                    registration))];
        foreach (PreparedDeclaredMethodPopulationKey key in gateKeys)
            _preparationGates.Remove(key);
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
                [.. _sessionByRegistration.Values
                    .Select(static prepared => prepared.Session)
                    .OfType<AssemblyInspectionSession>()];
            _preparations.Clear();
            _preparationGates.Clear();
            _sessionByRegistration.Clear();
            _sessionGates.Clear();
        }

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

    private sealed class PreparedParticipantSession
    {
        internal PreparedParticipantSession(
            AssemblyInspectionSession? Session,
            string? Failure)
        {
            this.Session = Session;
            this.Failure = Failure;
        }

        internal AssemblyInspectionSession? Session { get; }

        internal string? Failure { get; }
    }

    private readonly struct PreparedDeclaredMethodPopulationKey
        : IEquatable<PreparedDeclaredMethodPopulationKey>
    {
        private readonly AssemblyAcquisitionRegistration _registration;
        private readonly Guid _moduleVersionId;
        private readonly int _typeDefinitionToken;

        internal PreparedDeclaredMethodPopulationKey(
            AssemblyAcquisitionRegistration registration,
            Guid moduleVersionId,
            int typeDefinitionToken)
        {
            _registration = registration;
            _moduleVersionId = moduleVersionId;
            _typeDefinitionToken = typeDefinitionToken;
        }

        internal AssemblyAcquisitionRegistration Registration =>
            _registration;

        public bool Equals(
            PreparedDeclaredMethodPopulationKey other) =>
            ReferenceEquals(_registration, other._registration)
            && _moduleVersionId == other._moduleVersionId
            && _typeDefinitionToken == other._typeDefinitionToken;

        public override bool Equals(object? obj) =>
            obj is PreparedDeclaredMethodPopulationKey other
            && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                RuntimeHelpers.GetHashCode(_registration),
                _moduleVersionId,
                _typeDefinitionToken);
    }
}
