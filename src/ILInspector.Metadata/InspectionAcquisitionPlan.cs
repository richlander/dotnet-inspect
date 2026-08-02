using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

internal sealed record InspectionAcquisitionPlanOptions
{
    internal const int DefaultMaxCandidates = 4_096;
    internal const long DefaultMaxRetainedImageBytes = 512L * 1024 * 1024;
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
        internal Ready(AssemblyInspectionSession session) => Session = session;

        internal AssemblyInspectionSession Session { get; }
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
    readonly SemaphoreSlim _sourceOpenGate;
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
            new SemaphoreSlim(_options.MaxConcurrentSourceOpens);
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

    CandidateRegistrationResult ReadInventory(CandidateEntry entry)
    {
        _sourceOpenGate.Wait();
        try
        {
            using Stream stream = OpenSource(entry.Candidate.Assembly);
            long imageSize = ReadRemainingLength(stream);
            using var peReader = new PEReader(
                stream,
                PEStreamOptions.LeaveOpen | PEStreamOptions.PrefetchMetadata);
            if (!peReader.HasMetadata)
                return RejectInvalid(entry, "The selected image has no managed metadata.");

            MetadataReader reader = peReader.GetMetadataReader();
            AssemblyReferenceIdentity actual =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            if (!IdentityMatches(entry.Candidate.Assembly.Identity, actual))
            {
                return RejectInvalid(
                    entry,
                    "The selected image identity does not match its descriptor.");
            }

            var references =
                ImmutableArray.CreateBuilder<AssemblyReferenceIdentity>();
            var seenReferences = new HashSet<AssemblyReferenceIdentity>();
            var referencesByRow = new AssemblyReferenceIdentity[
                reader.GetTableRowCount(TableIndex.AssemblyRef)];
            foreach (AssemblyReferenceHandle handle in reader.AssemblyReferences)
            {
                AssemblyReferenceIdentity reference =
                    AssemblyReferenceIdentity.From(reader, handle);
                referencesByRow[MetadataTokens.GetRowNumber(handle) - 1] =
                    reference;
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

                    int targetIndex =
                        MetadataTokens.GetRowNumber(
                            (AssemblyReferenceHandle)terminal) - 1;
                    AssemblyReferenceIdentity target =
                        referencesByRow[targetIndex];
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
            _sourceOpenGate.Release();
        }
    }

    CandidateSessionResult OpenSessionCore(CandidateEntry entry)
    {
        CandidateRegistrationResult inventoryResult = entry.Inventory.Value;
        if (inventoryResult is CandidateRegistrationResult.Rejected rejected)
            return new CandidateSessionResult.Rejected(rejected.Failure);

        _sourceOpenGate.Wait();
        long reservedBytes = 0;
        try
        {
            Stream? stream = OpenSource(entry.Candidate.Assembly);
            AssemblyInspectionSession? session = null;
            try
            {
                long imageSize = ReadRemainingLength(stream);
                if (!TryReserveImage(imageSize))
                {
                    return new CandidateSessionResult.Rejected(
                        ResourceFailure(
                            "The retained-image budget was exhausted."));
                }

                reservedBytes = imageSize;
                Stream sessionSource = stream;
                stream = null;
                session = AssemblyInspectionSession.OpenPrefetched(sessionSource);
                var inventory =
                    (CandidateRegistrationResult.Ready)entry.Inventory.Value;
                if (!session.HasMetadata
                    || !IdentityMatches(
                        entry.Candidate.Assembly.Identity,
                        session.AssemblyIdentity())
                    || session.ModuleVersionId()
                        != inventory.Inventory.ModuleVersionId)
                {
                    ReleaseImage(reservedBytes);
                    return new CandidateSessionResult.Rejected(
                        new CandidateOpenFailure(
                            CandidateOpenFailureKind.InvalidImage,
                            "The opened image does not match the inventoried candidate."));
                }

                var ready = new CandidateSessionResult.Ready(session);
                session = null;
                return ready;
            }
            finally
            {
                session?.Dispose();
                stream?.Dispose();
            }
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ObjectDisposedException)
        {
            ReleaseImage(reservedBytes);
            return new CandidateSessionResult.Rejected(
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.Unreadable,
                    "The selected image could not be opened."));
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            ReleaseImage(reservedBytes);
            return new CandidateSessionResult.Rejected(
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.InvalidImage,
                    "The selected image contains invalid metadata."));
        }
        finally
        {
            _sourceOpenGate.Release();
        }
    }

    static Stream OpenSource(ResolvedAssemblyReference assembly)
    {
        Stream? stream = assembly.OpenRead();
        if (stream is null || !stream.CanRead)
        {
            stream?.Dispose();
            throw new IOException("The assembly opener did not return a readable stream.");
        }

        return stream;
    }

    static long ReadRemainingLength(Stream stream)
    {
        if (!stream.CanSeek)
            throw new NotSupportedException(
                "Assembly streams must support seeking for bounded inspection.");

        long length = checked(stream.Length - stream.Position);
        if (length <= 0)
            throw new BadImageFormatException("The selected image is empty.");
        return length;
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

    static bool IdentityMatches(
        AssemblyReferenceIdentity expected,
        AssemblyReferenceIdentity actual) =>
        StringComparer.OrdinalIgnoreCase.Equals(expected.Name, actual.Name)
        && expected.Version == actual.Version
        && CultureMatches(expected.Culture, actual.Culture)
        && StringComparer.OrdinalIgnoreCase.Equals(
            expected.PublicKeyToken ?? "",
            actual.PublicKeyToken ?? "");

    static bool CultureMatches(string? left, string? right)
    {
        static string Normalize(string? value) =>
            string.IsNullOrEmpty(value)
                || value.Equals("neutral", StringComparison.OrdinalIgnoreCase)
                    ? ""
                    : value;
        return StringComparer.OrdinalIgnoreCase.Equals(
            Normalize(left),
            Normalize(right));
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
        }

        foreach (AssemblyInspectionSession session in sessions)
            session.Dispose();
        _sourceOpenGate.Dispose();
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

        internal ResolvedAssemblyCandidate Candidate { get; }
        internal Lazy<CandidateRegistrationResult> Inventory { get; }
        internal Lazy<CandidateSessionResult> Session { get; }
    }
}
