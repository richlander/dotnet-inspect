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
        private readonly PreparedDeclaredMethodPopulationStore _owner;

        internal Ready(
            AssemblyContextSubject subject,
            PreparedDeclaredMethodPopulationStore owner,
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
            OpenExecution() =>
            _owner.OpenExecution(this);

        public MetadataDeclaredMethodPopulationResult Count() =>
            _owner.Count(this);

        public MetadataDeclaredMethodPopulationResult Rows(
            int maximumRows = int.MaxValue) =>
            _owner.Rows(this, maximumRows);
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
    private readonly PreparedDeclaredMethodPopulationStore _owner;
    private readonly AssemblyAcquisitionRegistration _registration;
    private AssemblyContextGroup.AssemblyContextGroupResourceBorrow?
        _borrow;

    internal AssemblyContextDeclaredMethodPopulationExecution(
        PreparedDeclaredMethodPopulationStore owner,
        AssemblyAcquisitionRegistration registration,
        MetadataDeclaredMethodPopulationSource source,
        AssemblyContextGroup.AssemblyContextGroupResourceBorrow borrow)
    {
        _owner = owner;
        _registration = registration;
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
        AssemblyContextGroup.AssemblyContextGroupResourceBorrow? borrow =
            Interlocked.Exchange(ref _borrow, null);
        if (borrow is null)
            return;

        try
        {
            _owner.EndExecution(_registration);
        }
        finally
        {
            borrow.Dispose();
        }
    }
}

/// <summary>
/// Executes one authenticated TypeDef's declared-MethodDef population while
/// the participant session is alive.
/// </summary>
public static class AssemblyContextDeclaredMethodPopulationQuery
{
    private static readonly Func<
        AssemblyContextGroup,
        PreparedDeclaredMethodPopulationStore> s_createStore =
            static owner =>
                new PreparedDeclaredMethodPopulationStore(owner);

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

        PreparedDeclaredMethodPopulationStore store =
            group.GetOrCreateOwnedResource<
                PreparedDeclaredMethodPopulationStore,
                AssemblyContextGroup>(
                group,
                s_createStore);
        return store.Prepare(
            participant,
            type,
            cancellationToken);
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
        IAssemblyContextParticipantOwnedResource
{
    private readonly object _gate = new();
    private readonly AssemblyContextGroup _group;
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

    internal PreparedDeclaredMethodPopulationStore(
        AssemblyContextGroup group)
    {
        _group = group;
    }

    internal AssemblyContextDeclaredMethodPopulationPreparation Prepare(
        AssemblyContextParticipant participant,
        MetadataTypeDefinitionBinding type,
        CancellationToken cancellationToken)
    {
        var key =
            new PreparedDeclaredMethodPopulationKey(
                participant.Assembly.Registration,
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
        }

        _group.ValidateParticipant(participant);
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
                cancellationToken.ThrowIfCancellationRequested();
                var subject =
                    new AssemblyContextSubject(participant.Assembly);
                AssemblyImageAccessResult<
                    AssemblyContextDeclaredMethodPopulationPreparation>
                    access =
                        _group.UseSnapshot(
                            participant,
                            cancellationToken,
                            (
                                Owner: this,
                                Key: key,
                                Subject: subject,
                                Type: type),
                            static (snapshot, state) =>
                                state.Owner.PrepareAndPublish(
                                    state.Key,
                                    state.Subject,
                                    snapshot,
                                    state.Type));
                AssemblyContextDeclaredMethodPopulationPreparation prepared =
                    access switch
                    {
                        AssemblyImageAccessResult<
                            AssemblyContextDeclaredMethodPopulationPreparation>
                            .Available available =>
                                available.Value,
                        AssemblyImageAccessResult<
                            AssemblyContextDeclaredMethodPopulationPreparation>
                            .Rejected rejected =>
                                new AssemblyContextDeclaredMethodPopulationPreparation
                                    .ParticipantRejected(
                                        subject,
                                        rejected.Failure),
                        _ => throw new InvalidOperationException(
                            "Unknown assembly image access result."),
                    };
                if (access
                    is AssemblyImageAccessResult<
                        AssemblyContextDeclaredMethodPopulationPreparation>
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

    internal MetadataDeclaredMethodPopulationResult Count(
        AssemblyContextDeclaredMethodPopulationPreparation.Ready ready)
    {
        using AssemblyContextDeclaredMethodPopulationExecution execution =
            OpenExecution(ready);
        return execution.Count();
    }

    internal MetadataDeclaredMethodPopulationResult Rows(
        AssemblyContextDeclaredMethodPopulationPreparation.Ready ready,
        int maximumRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);
        using AssemblyContextDeclaredMethodPopulationExecution execution =
            OpenExecution(ready);
        return execution.Rows(maximumRows);
    }

    internal AssemblyContextDeclaredMethodPopulationExecution OpenExecution(
        AssemblyContextDeclaredMethodPopulationPreparation.Ready ready)
    {
        AssemblyContextGroup.AssemblyContextGroupResourceBorrow borrow =
            _group.BorrowOwnedResource(this);
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_sessionByRegistration.TryGetValue(
                        ready.Registration,
                        out PreparedParticipantSession? participant)
                    || participant.ReleaseRequested
                    || !ReferenceEquals(
                        participant.Session,
                        ready.Session))
                {
                    throw new ObjectDisposedException(
                        nameof(AssemblyContextParticipant));
                }

                participant.ActiveExecutions++;
            }

            return new(
                this,
                ready.Registration,
                ready.Source,
                borrow);
        }
        catch
        {
            borrow.Dispose();
            throw;
        }
    }

    private AssemblyContextDeclaredMethodPopulationPreparation
        PrepareAndPublish(
            PreparedDeclaredMethodPopulationKey key,
            AssemblyContextSubject subject,
            AssemblyImageSnapshot snapshot,
            MetadataTypeDefinitionBinding type)
    {
        PreparedParticipantSession participantSession =
            GetOrCreateParticipantSession(
                key.Registration,
                snapshot);
        AssemblyContextDeclaredMethodPopulationPreparation prepared =
            participantSession.Session is { } session
                ? Prepare(
                    key.Registration,
                    subject,
                    session,
                    type)
                : new AssemblyContextDeclaredMethodPopulationPreparation
                    .Failed(
                        subject,
                        participantSession.Failure!);
        Publish(key, prepared);
        return prepared;
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
                                this,
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

    private void PublishIfAlive(
        PreparedDeclaredMethodPopulationKey key,
        AssemblyContextDeclaredMethodPopulationPreparation prepared)
    {
        lock (_gate)
        {
            if (!_disposed)
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

    bool IAssemblyContextParticipantOwnedResource
        .TryReleaseParticipant(
            AssemblyAcquisitionRegistration registration)
    {
        AssemblyInspectionSession? session = null;
        lock (_gate)
        {
            if (_disposed)
                return true;

            RemoveParticipantPreparations(registration);
            _sessionGates.Remove(registration);
            if (!_sessionByRegistration.TryGetValue(
                    registration,
                    out PreparedParticipantSession? participant))
            {
                return true;
            }

            participant.ReleaseRequested = true;
            if (participant.ActiveExecutions != 0)
                return false;

            _sessionByRegistration.Remove(registration);
            session = participant.Session;
        }

        session?.Dispose();
        return true;
    }

    internal void EndExecution(
        AssemblyAcquisitionRegistration registration)
    {
        AssemblyInspectionSession? session = null;
        bool completeParticipantRelease = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            PreparedParticipantSession participant =
                _sessionByRegistration[registration];
            participant.ActiveExecutions--;
            if (participant.ActiveExecutions < 0)
            {
                throw new InvalidOperationException(
                    "Prepared execution accounting became negative.");
            }
            if (participant.ActiveExecutions == 0
                && participant.ReleaseRequested)
            {
                _sessionByRegistration.Remove(registration);
                session = participant.Session;
                completeParticipantRelease = true;
            }
        }

        try
        {
            session?.Dispose();
        }
        finally
        {
            if (completeParticipantRelease)
                _group.TryCompleteParticipantRelease(registration);
        }
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

        internal int ActiveExecutions { get; set; }

        internal bool ReleaseRequested { get; set; }
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
