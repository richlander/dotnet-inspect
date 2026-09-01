using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace ILInspector.Metadata;

internal sealed record InspectionAcquisitionPlanOptions
{
    internal const int DefaultMaxCandidates = 4_096;
    internal const long DefaultMaxRetainedImageBytes =
        AssemblyImageSnapshot.DefaultMaxRetainedImageBytes;
    internal const long DefaultMaxInventoryImageBytes =
        AssemblyImageSnapshot.DefaultMaxRetainedImageBytes;
    internal const int DefaultMaxConcurrentSourceOpens = 8;

    internal int MaxCandidates { get; init; } = DefaultMaxCandidates;
    internal long MaxRetainedImageBytes { get; init; } =
        DefaultMaxRetainedImageBytes;
    internal long MaxInventoryImageBytes { get; init; } =
        DefaultMaxInventoryImageBytes;
    internal int MaxConcurrentSourceOpens { get; init; } =
        DefaultMaxConcurrentSourceOpens;
    internal InspectionAcquisitionPlan.TestHooks? TestHooks { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCandidates);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRetainedImageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxInventoryImageBytes);
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
            AssemblyInventorySnapshot inventory,
            CandidateOpenFailure? inventoryFailure = null)
        {
            Candidate = candidate;
            Inventory = inventory;
            InventoryFailure = inventoryFailure;
        }

        internal ResolvedAssemblyCandidate Candidate { get; }
        internal AssemblyInventorySnapshot Inventory { get; }
        internal CandidateOpenFailure? InventoryFailure { get; }
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
    readonly Dictionary<AssemblyAcquisitionRegistration, CandidateEntry>
        _rootEntriesByRegistration =
            new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<AssemblyAcquisitionRegistration, CandidateEntry>
        _sharedEntriesByRegistration =
            new(ReferenceEqualityComparer.Instance);
    readonly HashSet<AssemblyAcquisitionRegistration> _registrations =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<AssemblyCandidateId, CandidateEntry> _entriesById = [];
    long _inventoryImageBytes;
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
            new SynchronousConcurrencyGate(
                _options.MaxConcurrentSourceOpens,
                _options.TestHooks?.SourceOpenWaitStarted);
    }

    internal AssemblyCatalogId CatalogId { get; }
    internal int CandidateCount
    {
        get
        {
            lock (_gate)
                return _registrations.Count;
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
        ResolvedAssemblyReference assembly) =>
        Register(assembly, allowRootAdjacencyDegradation: false);

    internal CandidateRegistrationResult RegisterRoot(
        ResolvedAssemblyReference assembly) =>
        Register(assembly, allowRootAdjacencyDegradation: true);

    CandidateRegistrationResult Register(
        ResolvedAssemblyReference assembly,
        bool allowRootAdjacencyDegradation)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        CandidateEntry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Dictionary<AssemblyAcquisitionRegistration, CandidateEntry>
                entries =
                    allowRootAdjacencyDegradation
                        ? _rootEntriesByRegistration
                        : _entriesByRegistration;
            if (entries.TryGetValue(
                    assembly.Registration,
                    out entry!))
            {
                // The single-flight value is evaluated after releasing the map lock.
            }
            else if (!_registrations.Contains(
                         assembly.Registration)
                && _registrations.Count >= _options.MaxCandidates)
            {
                return new CandidateRegistrationResult.Rejected(
                    assembly,
                    ResourceFailure("The inspection candidate budget was exhausted."));
            }
            else
            {
                if (!_sharedEntriesByRegistration.TryGetValue(
                        assembly.Registration,
                        out entry!))
                {
                    var id = new AssemblyCandidateId(Guid.NewGuid());
                    var candidate = new ResolvedAssemblyCandidate(
                        CatalogId,
                        id,
                        assembly);
                    entry = new CandidateEntry(this, candidate);
                    _sharedEntriesByRegistration.Add(
                        assembly.Registration,
                        entry);
                    _registrations.Add(assembly.Registration);
                    _entriesById.Add(id, entry);
                }
                entries.Add(assembly.Registration, entry);
            }

            _activeOperations++;
        }

        try
        {
            CandidateRegistrationResult result = entry.Inventory.Value;
            if (!allowRootAdjacencyDegradation
                && result is CandidateRegistrationResult.Ready
                {
                    InventoryFailure: { } failure,
                } ready)
            {
                return new CandidateRegistrationResult.Rejected(
                    ready.Candidate.Assembly,
                    failure);
            }
            return result;
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
            if (_sharedEntriesByRegistration.TryGetValue(
                    assembly.Registration,
                    out CandidateEntry? existing))
            {
                if (ReferenceEquals(
                        existing.RetainedSnapshot,
                        snapshot))
                {
                    _entriesByRegistration.TryAdd(
                        assembly.Registration,
                        existing);
                    return;
                }

                throw new InvalidOperationException(
                    "The acquisition registration already owns a different image source.");
            }
            if (_registrations.Count >= _options.MaxCandidates)
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
            _sharedEntriesByRegistration.Add(
                assembly.Registration,
                entry);
            _registrations.Add(assembly.Registration);
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
        long reservedBytes = 0;
        try
        {
            AssemblyImageSnapshot snapshot;
            if (entry.RetainedSnapshot is { } retainedSnapshot)
            {
                snapshot = retainedSnapshot;
            }
            else
            {
                AssemblyImageSnapshotResult snapshotResult =
                    AssemblyImageSnapshot.Open(
                        entry.Candidate.Assembly,
                        TryReserveInventoryImage,
                        ReleaseInventoryImage);
                if (snapshotResult
                    is AssemblyImageSnapshotResult.Rejected rejected)
                {
                    return new CandidateRegistrationResult.Rejected(
                        entry.Candidate.Assembly,
                        rejected.Failure);
                }

                snapshot =
                    ((AssemblyImageSnapshotResult.Ready)snapshotResult)
                        .Snapshot;
                reservedBytes = snapshot.Length;
            }
            ImmutableArray<byte> contentDigest =
                ImmutableArray.CreateRange(
                    SHA256.HashData(snapshot.Content.AsSpan()));
            using var peReader =
                new PEReader(snapshot.Content);

            MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(peReader);

            var references =
                ImmutableArray.CreateBuilder<AssemblyReferenceIdentity>();
            var seenReferences = new HashSet<AssemblyReferenceIdentity>();
            var referencesByHandle =
                new Dictionary<
                    AssemblyReferenceHandle,
                    AssemblyReferenceIdentity>();
            var referenceProjection =
                new AssemblyReferenceProjectionCache(reader);
            foreach (AssemblyReferenceHandle handle in reader.AssemblyReferences)
            {
                try
                {
                    AssemblyReferenceIdentity reference =
                        AssemblyReferenceIdentity.From(
                            handle,
                            referenceProjection);
                    referencesByHandle.Add(handle, reference);
                    if (seenReferences.Add(reference))
                        references.Add(reference);
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentOutOfRangeException)
                {
                    entry.RecordRootAdjacencyFailure(
                        "The selected image has an invalid AssemblyRef row.");
                }
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
                try
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
                        // Root extraction diagnoses this ExportedType row from
                        // the retained session; only adjacency discovery skips it.
                        entry.RecordRootAdjacencyFailure(
                            "The selected image has an invalid ExportedType relationship.");
                        continue;
                    }

                    if (terminal.Kind == HandleKind.AssemblyReference)
                    {
                        if (!reader.GetExportedType(rootToLeaf[0]).IsForwarder)
                        {
                            entry.RecordRootAdjacencyFailure(
                                ApiSurfaceInspectionFailure
                                    .UnmarkedAssemblyForwarderDetail);
                            continue;
                        }

                        var targetHandle = (AssemblyReferenceHandle)terminal;
                        if (!referencesByHandle.TryGetValue(
                                targetHandle,
                                out AssemblyReferenceIdentity? target))
                        {
                            target = AssemblyReferenceIdentity.From(
                                targetHandle,
                                referenceProjection);
                            referencesByHandle.Add(targetHandle, target);
                            if (seenReferences.Add(target))
                                references.Add(target);
                        }

                        if (seenForwarderTargets.Add(target))
                            forwarderTargets.Add(target);
                    }
                    else if (terminal.Kind != HandleKind.AssemblyFile)
                    {
                        // Root extraction diagnoses this ExportedType row from
                        // the retained session; only adjacency discovery skips it.
                        entry.RecordRootAdjacencyFailure(
                            "The selected image has an unsupported ExportedType terminal.");
                    }
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentOutOfRangeException)
                {
                    // Root extraction diagnoses this ExportedType row from the
                    // retained session; only adjacency discovery skips it.
                    entry.RecordRootAdjacencyFailure(
                        "The selected image has an invalid ExportedType row.");
                }
            }

            return new CandidateRegistrationResult.Ready(
                entry.Candidate,
                new AssemblyInventorySnapshot(
                    snapshot.Identity,
                    snapshot.ModuleVersionId,
                    contentDigest,
                    references.ToImmutable(),
                    forwarderTargets.ToImmutable(),
                    snapshot.Length),
                entry.RootAdjacencyFailure);
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
            ReleaseInventoryImage(reservedBytes);
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
            AssemblyImageSnapshotResult snapshotResult =
                AssemblyImageSnapshot.Open(
                    entry.Candidate.Assembly,
                    TryReserveImage,
                    ReleaseImage);
            if (snapshotResult
                is AssemblyImageSnapshotResult.Rejected snapshotRejected)
            {
                return new CandidateSessionResult.Rejected(
                    snapshotRejected.Failure);
            }

            AssemblyImageSnapshot snapshot =
                ((AssemblyImageSnapshotResult.Ready)snapshotResult)
                    .Snapshot;
            reservedBytes = snapshot.Length;
            var inventory =
                (CandidateRegistrationResult.Ready)entry.Inventory.Value;
            byte[] contentDigest =
                SHA256.HashData(snapshot.Content.AsSpan());
            if (snapshot.ModuleVersionId
                    != inventory.Inventory.ModuleVersionId
                || !CryptographicOperations.FixedTimeEquals(
                    contentDigest,
                    inventory.Inventory.ContentDigest.AsSpan()))
            {
                return new CandidateSessionResult.Rejected(
                    new CandidateOpenFailure(
                        CandidateOpenFailureKind.InvalidImage,
                        "The opened image does not match the inventoried candidate."));
            }

            AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(snapshot);
            retainReservation = true;
            return new CandidateSessionResult.Ready(
                session,
                snapshot);
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
            byte[] contentDigest =
                SHA256.HashData(snapshot.Content.AsSpan());
            if (!session.HasMetadata
                || !AssemblyImageSnapshot.IdentityMatches(
                    entry.Candidate.Assembly.Identity,
                    session.AssemblyIdentity())
                || session.ModuleVersionId()
                    != inventory.Inventory.ModuleVersionId
                || !CryptographicOperations.FixedTimeEquals(
                    contentDigest,
                    inventory.Inventory.ContentDigest.AsSpan()))
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
            if (imageSize
                > _options.MaxRetainedImageBytes
                    - _retainedImageBytes)
            {
                return false;
            }

            _retainedImageBytes += imageSize;
            return true;
        }
    }

    bool TryReserveInventoryImage(long imageSize)
    {
        lock (_gate)
        {
            if (imageSize
                > _options.MaxInventoryImageBytes
                    - _inventoryImageBytes)
            {
                return false;
            }

            _inventoryImageBytes += imageSize;
            return true;
        }
    }

    void ReleaseInventoryImage(long imageSize)
    {
        if (imageSize == 0)
            return;
        lock (_gate)
            _inventoryImageBytes -= imageSize;
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

            foreach (CandidateEntry entry
                in _sharedEntriesByRegistration.Values)
            {
                if (entry.Session.IsValueCreated
                    && entry.Session.Value
                        is CandidateSessionResult.Ready ready)
                {
                    sessions.Add(ready.Session);
                }
            }
            _entriesByRegistration.Clear();
            _rootEntriesByRegistration.Clear();
            _sharedEntriesByRegistration.Clear();
            _registrations.Clear();
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
        internal CandidateOpenFailure? RootAdjacencyFailure { get; private set; }
        internal Lazy<CandidateRegistrationResult> Inventory { get; }
        internal Lazy<CandidateSessionResult> Session { get; }

        internal void RecordRootAdjacencyFailure(string detail)
        {
            RootAdjacencyFailure ??= new CandidateOpenFailure(
                CandidateOpenFailureKind.InvalidImage,
                detail);
        }
    }

    internal sealed class TestHooks
    {
        internal Action? SourceOpenWaitStarted { get; init; }
    }
}
