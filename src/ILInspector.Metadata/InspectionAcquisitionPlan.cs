using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

internal sealed record InspectionAcquisitionPlanOptions
{
    internal const int DefaultMaxCandidates = 4_096;
    internal const long DefaultMaxRetainedImageBytes =
        AssemblyImageSnapshot.DefaultMaxRetainedImageBytes;
    internal const int DefaultMaxConcurrentSourceOpens = 8;

    internal int MaxCandidates { get; init; } = DefaultMaxCandidates;
    internal long MaxRetainedImageBytes { get; init; } =
        DefaultMaxRetainedImageBytes;
    internal int MaxConcurrentSourceOpens { get; init; } =
        DefaultMaxConcurrentSourceOpens;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCandidates);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRetainedImageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxConcurrentSourceOpens);
    }
}

internal abstract class CandidateRegistrationResult
{
    private protected CandidateRegistrationResult()
    {
    }

    internal sealed class Ready : CandidateRegistrationResult
    {
        internal Ready(
            ResolvedAssemblyCandidate candidate,
            AssemblyInventorySnapshot inventory)
        {
            Candidate = candidate;
            Inventory = inventory;
        }

        internal ResolvedAssemblyCandidate Candidate { get; }
        internal AssemblyInventorySnapshot Inventory { get; }
    }

    internal sealed class Rejected : CandidateRegistrationResult
    {
        internal Rejected(
            ResolvedAssemblyReference assembly,
            CandidateOpenFailure failure)
        {
            Assembly = assembly;
            Failure = failure;
        }

        internal ResolvedAssemblyReference Assembly { get; }
        internal CandidateOpenFailure Failure { get; }
    }
}

internal abstract class CandidateSessionResult
{
    private protected CandidateSessionResult()
    {
    }

    internal sealed class Ready : CandidateSessionResult
    {
        internal Ready(
            AssemblyInspectionSession session,
            AssemblyImageSnapshot snapshot)
        {
            Session = session;
            Snapshot = snapshot;
        }

        internal AssemblyInspectionSession Session { get; }
        internal AssemblyImageSnapshot Snapshot { get; }
    }

    internal sealed class Rejected : CandidateSessionResult
    {
        internal Rejected(CandidateOpenFailure failure) => Failure = failure;

        internal CandidateOpenFailure Failure { get; }
    }
}

internal sealed class InspectionAcquisitionPlan : IDisposable
{
    readonly object _gate = new();
    readonly InspectionAcquisitionPlanOptions _options;
    readonly SynchronousConcurrencyGate _sourceOpenGate;
    readonly Dictionary<AssemblyAcquisitionRegistration, CandidateEntry>
        _entriesByRegistration =
            new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<AssemblyCandidateId, CandidateEntry> _entriesById = [];
    long _retainedImageBytes;
    int _activeOperations;
    bool _disposed;

    internal InspectionAcquisitionPlan(
        InspectionAcquisitionPlanOptions? options = null)
    {
        _options = options ?? new InspectionAcquisitionPlanOptions();
        _options.Validate();
        CatalogId = new AssemblyCatalogId(Guid.NewGuid());
        _sourceOpenGate =
            new SynchronousConcurrencyGate(_options.MaxConcurrentSourceOpens);
    }

    internal AssemblyCatalogId CatalogId { get; }
    internal int CandidateCount
    {
        get
        {
            lock (_gate)
                return _entriesByRegistration.Count;
        }
    }

    internal long RetainedImageBytes
    {
        get
        {
            lock (_gate)
                return _retainedImageBytes;
        }
    }

    internal CandidateRegistrationResult Register(
        ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        CandidateEntry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entriesByRegistration.TryGetValue(
                    assembly.Registration,
                    out entry!))
            {
                // The single-flight value is evaluated after releasing the map lock.
            }
            else if (_entriesByRegistration.Count >= _options.MaxCandidates)
            {
                return new CandidateRegistrationResult.Rejected(
                    assembly,
                    ResourceFailure("The inspection candidate budget was exhausted."));
            }
            else
            {
                var id = new AssemblyCandidateId(Guid.NewGuid());
                var candidate = new ResolvedAssemblyCandidate(
                    CatalogId,
                    id,
                    assembly);
                entry = new CandidateEntry(this, candidate);
                _entriesByRegistration.Add(assembly.Registration, entry);
                _entriesById.Add(id, entry);
            }

            _activeOperations++;
        }

        try
        {
            return entry.Inventory.Value;
        }
        finally
        {
            EndOperation();
        }
    }

    internal void RegisterRetainedSnapshot(
        ResolvedAssemblyReference assembly,
        AssemblyImageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(snapshot);

        ResolvedAssemblyReference retained =
            snapshot.RetainAssemblyReference(assembly);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entriesByRegistration.TryGetValue(
                    assembly.Registration,
                    out CandidateEntry? existing))
            {
                if (ReferenceEquals(
                        existing.RetainedSnapshot,
                        snapshot))
                {
                    return;
                }

                throw new InvalidOperationException(
                    "The acquisition registration already owns a different image source.");
            }
            if (_entriesByRegistration.Count >= _options.MaxCandidates)
            {
                throw new InvalidOperationException(
                    "The inspection candidate budget was exhausted.");
            }
            if (snapshot.Length
                > _options.MaxRetainedImageBytes - _retainedImageBytes)
            {
                throw new InvalidOperationException(
                    "The retained-image budget was exhausted.");
            }

            var id = new AssemblyCandidateId(Guid.NewGuid());
            var candidate = new ResolvedAssemblyCandidate(
                CatalogId,
                id,
                retained);
            var entry = new CandidateEntry(this, candidate, snapshot);
            _entriesByRegistration.Add(assembly.Registration, entry);
            _entriesById.Add(id, entry);
            _retainedImageBytes += snapshot.Length;
        }
    }

    internal CandidateSessionResult OpenSession(
        ResolvedAssemblyCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        CandidateEntry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (candidate.Catalog != CatalogId
                || !_entriesById.TryGetValue(candidate.Id, out entry!)
                || !ReferenceEquals(entry.Candidate, candidate))
            {
                throw new ArgumentException(
                    "The candidate does not belong to this acquisition plan.",
                    nameof(candidate));
            }

            _activeOperations++;
        }

        try
        {
            return entry.Session.Value;
        }
        finally
        {
            EndOperation();
        }
    }

    internal ResolvedAssemblyReference? RetainAssemblyReference(
        ResolvedAssemblyCandidate candidate)
        => OpenSession(candidate)
            is CandidateSessionResult.Ready ready
                ? ready.Snapshot.RetainAssemblyReference(
                    candidate.Assembly)
                : null;

    CandidateRegistrationResult ReadInventory(CandidateEntry entry)
    {
        _sourceOpenGate.Enter();
        try
        {
            using Stream stream =
                AssemblyImageSnapshot.OpenSource(
                    entry.Candidate.Assembly);
            long imageSize =
                AssemblyImageSnapshot.ReadRemainingLength(stream);
            using var peReader = new PEReader(
                stream,
                PEStreamOptions.LeaveOpen | PEStreamOptions.PrefetchMetadata);
            if (!peReader.HasMetadata)
                return RejectInvalid(entry, "The selected image has no managed metadata.");

            MetadataReader reader = peReader.GetMetadataReader();
            AssemblyReferenceIdentity actual =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            if (!AssemblyImageSnapshot.IdentityMatches(
                    entry.Candidate.Assembly.Identity,
                    actual))
            {
                return RejectInvalid(
                    entry,
                    "The selected image identity does not match its descriptor.");
            }

            var references =
                ImmutableArray.CreateBuilder<AssemblyReferenceIdentity>();
            var seenReferences = new HashSet<AssemblyReferenceIdentity>();
            var referencesByHandle =
                new Dictionary<
                    AssemblyReferenceHandle,
                    AssemblyReferenceIdentity>();
            foreach (AssemblyReferenceHandle handle in reader.AssemblyReferences)
            {
                AssemblyReferenceIdentity reference =
                    AssemblyReferenceIdentity.From(reader, handle);
                referencesByHandle.Add(handle, reference);
                if (seenReferences.Add(reference))
                    references.Add(reference);
            }

            var forwarderTargets =
                ImmutableArray.CreateBuilder<AssemblyReferenceIdentity>();
            var seenForwarderTargets =
                new HashSet<AssemblyReferenceIdentity>();
            Span<ExportedTypeHandle> rootToLeaf =
                stackalloc ExportedTypeHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            foreach (ExportedTypeHandle handle in reader.ExportedTypes)
            {
                if (!MetadataRelationshipTraversal
                        .TryWalkExportedTypeImplementationChain(
                            reader,
                            handle,
                            rootToLeaf,
                            out _,
                            out EntityHandle terminal,
                            out _))
                {
                    return RejectInvalid(
                        entry,
                        "The selected image has an invalid ExportedType relationship.");
                }

                if (terminal.Kind == HandleKind.AssemblyReference)
                {
                    if (!reader.GetExportedType(rootToLeaf[0]).IsForwarder)
                    {
                        return RejectInvalid(
                            entry,
                            "An AssemblyRef-terminated ExportedType chain is not a forwarder.");
                    }

                    var targetHandle = (AssemblyReferenceHandle)terminal;
                    if (!referencesByHandle.TryGetValue(
                            targetHandle,
                            out AssemblyReferenceIdentity? target))
                    {
                        target = AssemblyReferenceIdentity.From(
                            reader,
                            targetHandle);
                        referencesByHandle.Add(targetHandle, target);
                        if (seenReferences.Add(target))
                            references.Add(target);
                    }

                    if (target is null)
                    {
                        return RejectInvalid(
                            entry,
                            "The selected image has an invalid AssemblyRef target.");
                    }

                    if (seenForwarderTargets.Add(target))
                        forwarderTargets.Add(target);
                }
                else if (terminal.Kind != HandleKind.AssemblyFile)
                {
                    return RejectInvalid(
                        entry,
                        "The selected image has an unsupported ExportedType terminal.");
                }
            }

            return new CandidateRegistrationResult.Ready(
                entry.Candidate,
                new AssemblyInventorySnapshot(
                    actual,
                    reader.GetGuid(reader.GetModuleDefinition().Mvid),
                    references.ToImmutable(),
                    forwarderTargets.ToImmutable(),
                    imageSize));
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ObjectDisposedException)
        {
            return new CandidateRegistrationResult.Rejected(
                entry.Candidate.Assembly,
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.Unreadable,
                    "The selected image could not be read."));
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return RejectInvalid(
                entry,
                "The selected image contains invalid metadata.");
        }
        finally
        {
            _sourceOpenGate.Exit();
        }
    }

    CandidateSessionResult OpenSessionCore(CandidateEntry entry)
    {
        CandidateRegistrationResult inventoryResult = entry.Inventory.Value;
        if (inventoryResult is CandidateRegistrationResult.Rejected rejected)
            return new CandidateSessionResult.Rejected(rejected.Failure);

        _sourceOpenGate.Enter();
        long reservedBytes = 0;
        bool retainReservation = false;
        try
        {
            AssemblyInspectionSession? session = null;
            try
            {
                AssemblyImageSnapshotResult snapshotResult =
                    AssemblyImageSnapshot.Open(
                        entry.Candidate.Assembly,
                        TryReserveImage,
                        ReleaseImage);
                if (snapshotResult
                    is AssemblyImageSnapshotResult.Rejected
                        snapshotRejected)
                {
                    return new CandidateSessionResult.Rejected(
                        snapshotRejected.Failure);
                }

                AssemblyImageSnapshot snapshot =
                    ((AssemblyImageSnapshotResult.Ready)snapshotResult)
                    .Snapshot;
                reservedBytes = snapshot.Length;
                session = AssemblyInspectionSession.Open(snapshot);
                var inventory =
                    (CandidateRegistrationResult.Ready)entry.Inventory.Value;
                if (!session.HasMetadata
                    || !AssemblyImageSnapshot.IdentityMatches(
                        entry.Candidate.Assembly.Identity,
                        session.AssemblyIdentity())
                    || session.ModuleVersionId()
                        != inventory.Inventory.ModuleVersionId)
                {
                    return new CandidateSessionResult.Rejected(
                        new CandidateOpenFailure(
                            CandidateOpenFailureKind.InvalidImage,
                            "The opened image does not match the inventoried candidate."));
                }

                var ready = new CandidateSessionResult.Ready(
                    session,
                    snapshot);
                session = null;
                retainReservation = true;
                return ready;
            }
            finally
            {
                session?.Dispose();
            }
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ObjectDisposedException)
        {
            return new CandidateSessionResult.Rejected(
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.Unreadable,
                    "The selected image could not be opened."));
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return new CandidateSessionResult.Rejected(
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.InvalidImage,
                    "The selected image contains invalid metadata."));
        }
        finally
        {
            if (!retainReservation)
                ReleaseImage(reservedBytes);
            _sourceOpenGate.Exit();
        }
    }

    CandidateSessionResult OpenRetainedSession(
        CandidateEntry entry,
        AssemblyImageSnapshot snapshot)
    {
        CandidateRegistrationResult inventoryResult =
            entry.Inventory.Value;
        if (inventoryResult is CandidateRegistrationResult.Rejected rejected)
            return new CandidateSessionResult.Rejected(rejected.Failure);

        AssemblyInspectionSession? session = null;
        try
        {
            session = AssemblyInspectionSession.Open(snapshot);
            var inventory =
                (CandidateRegistrationResult.Ready)inventoryResult;
            if (!session.HasMetadata
                || !AssemblyImageSnapshot.IdentityMatches(
                    entry.Candidate.Assembly.Identity,
                    session.AssemblyIdentity())
                || session.ModuleVersionId()
                    != inventory.Inventory.ModuleVersionId)
            {
                return new CandidateSessionResult.Rejected(
                    new CandidateOpenFailure(
                        CandidateOpenFailureKind.InvalidImage,
                        "The retained image does not match the inventoried candidate."));
            }

            var ready = new CandidateSessionResult.Ready(
                session,
                snapshot);
            session = null;
            return ready;
        }
        finally
        {
            session?.Dispose();
        }
    }

    bool TryReserveImage(long imageSize)
    {
        lock (_gate)
        {
            if (imageSize > _options.MaxRetainedImageBytes - _retainedImageBytes)
                return false;
            _retainedImageBytes += imageSize;
            return true;
        }
    }

    void ReleaseImage(long imageSize)
    {
        if (imageSize == 0)
            return;
        lock (_gate)
            _retainedImageBytes -= imageSize;
    }

    void EndOperation()
    {
        lock (_gate)
        {
            _activeOperations--;
            if (_activeOperations == 0)
                Monitor.PulseAll(_gate);
        }
    }

    static CandidateRegistrationResult.Rejected RejectInvalid(
        CandidateEntry entry,
        string detail) =>
        new(
            entry.Candidate.Assembly,
            new CandidateOpenFailure(
                CandidateOpenFailureKind.InvalidImage,
                detail));

    static CandidateOpenFailure ResourceFailure(string detail) =>
        new(CandidateOpenFailureKind.ResourceBudget, detail);

    public void Dispose()
    {
        List<AssemblyInspectionSession> sessions = [];
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            while (_activeOperations != 0)
                Monitor.Wait(_gate);

            foreach (CandidateEntry entry in _entriesByRegistration.Values)
            {
                if (entry.Session.IsValueCreated
                    && entry.Session.Value is CandidateSessionResult.Ready ready)
                {
                    sessions.Add(ready.Session);
                }
            }
            _entriesByRegistration.Clear();
            _entriesById.Clear();
            _retainedImageBytes = 0;
        }

        foreach (AssemblyInspectionSession session in sessions)
            session.Dispose();
    }

    sealed class CandidateEntry
    {
        internal CandidateEntry(
            InspectionAcquisitionPlan owner,
            ResolvedAssemblyCandidate candidate)
        {
            Candidate = candidate;
            Inventory = new Lazy<CandidateRegistrationResult>(
                () => owner.ReadInventory(this),
                LazyThreadSafetyMode.ExecutionAndPublication);
            Session = new Lazy<CandidateSessionResult>(
                () => owner.OpenSessionCore(this),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        internal CandidateEntry(
            InspectionAcquisitionPlan owner,
            ResolvedAssemblyCandidate candidate,
            AssemblyImageSnapshot snapshot)
        {
            Candidate = candidate;
            RetainedSnapshot = snapshot;
            Inventory = new Lazy<CandidateRegistrationResult>(
                () => owner.ReadInventory(this),
                LazyThreadSafetyMode.ExecutionAndPublication);
            Session = new Lazy<CandidateSessionResult>(
                () => owner.OpenRetainedSession(this, snapshot),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        internal ResolvedAssemblyCandidate Candidate { get; }
        internal AssemblyImageSnapshot? RetainedSnapshot { get; }
        internal Lazy<CandidateRegistrationResult> Inventory { get; }
        internal Lazy<CandidateSessionResult> Session { get; }
    }
}
