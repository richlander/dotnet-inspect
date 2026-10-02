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
            AssemblyInspectionSession session,
            MetadataDeclaredMethodPopulationSource source,
            MetadataTypeDefinitionBinding binding)
            : base(subject)
        {
            _owner = owner;
            Session = session;
            Source = source;
            Binding = binding;
        }

        internal AssemblyInspectionSession Session { get; }

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
    private AssemblyContextGroup.AssemblyContextGroupResourceBorrow?
        _borrow;

    internal AssemblyContextDeclaredMethodPopulationExecution(
        MetadataDeclaredMethodPopulationSource source,
        AssemblyContextGroup.AssemblyContextGroupResourceBorrow borrow)
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
        AssemblyContextGroup.AssemblyContextGroupResourceBorrow? borrow =
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
    : IDisposable
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
        bool belongsToGroup = false;
        foreach (AssemblyContextParticipant candidate
            in _group.Participants)
        {
            if (!ReferenceEquals(
                    candidate.Assembly.Registration,
                    participant.Assembly.Registration))
            {
                continue;
            }
            belongsToGroup = true;
            break;
        }
        if (!belongsToGroup)
        {
            throw new ArgumentException(
                "The requested participant is not a member of the "
                    + "assembly context group.",
                nameof(participant));
        }

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
            if (!_preparationGates.TryGetValue(
                    key,
                    out preparationGate!))
            {
                preparationGate = new();
                _preparationGates.Add(key, preparationGate);
            }
        }

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
                AssemblyContextDeclaredMethodPopulationPreparation> access =
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
                lock (_gate)
                {
                    if (!_disposed)
                        _preparations.Add(key, prepared);
                }
            }
            return prepared;
        }
    }

    internal MetadataDeclaredMethodPopulationResult Count(
        AssemblyContextDeclaredMethodPopulationPreparation.Ready ready) =>
        _group.UseOwnedResource(
            this,
            ready,
            static (_, prepared) =>
                prepared.Source.Count());

    internal MetadataDeclaredMethodPopulationResult Rows(
        AssemblyContextDeclaredMethodPopulationPreparation.Ready ready,
        int maximumRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);
        return _group.UseOwnedResource(
            this,
            (Ready: ready, MaximumRows: maximumRows),
            static (_, state) =>
                state.Ready.Source.Rows(state.MaximumRows));
    }

    internal AssemblyContextDeclaredMethodPopulationExecution OpenExecution(
        AssemblyContextDeclaredMethodPopulationPreparation.Ready ready)
    {
        AssemblyContextGroup.AssemblyContextGroupResourceBorrow borrow =
            _group.BorrowOwnedResource(this);
        return new(
            ready.Source,
            borrow);
    }

    private AssemblyContextDeclaredMethodPopulationPreparation
        PrepareAndPublish(
            PreparedDeclaredMethodPopulationKey key,
            AssemblyContextSubject subject,
            AssemblyImageSnapshot snapshot,
            MetadataTypeDefinitionBinding type)
    {
        AssemblyContextDeclaredMethodPopulationPreparation prepared =
            Prepare(subject, snapshot, type);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _preparations.Add(key, prepared);
        }
        return prepared;
    }

    private AssemblyContextDeclaredMethodPopulationPreparation Prepare(
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        MetadataTypeDefinitionBinding type)
    {
        AssemblyInspectionSession? session = null;
        try
        {
            session = AssemblyInspectionSession.Open(snapshot);
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
                                session,
                                ready.Source,
                                type);
                    session = null;
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
        finally
        {
            session?.Dispose();
        }
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
                        AssemblyContextDeclaredMethodPopulationPreparation
                            .Ready>()
                    .Select(static ready => ready.Session)];
            _preparations.Clear();
            _preparationGates.Clear();
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
