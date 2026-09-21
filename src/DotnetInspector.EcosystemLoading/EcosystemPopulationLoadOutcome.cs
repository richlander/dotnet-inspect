using DotnetInspector.Libraries;
using Inspector.Artifacts.Workspaces;
using Inspector.Resources;

namespace DotnetInspector.EcosystemLoading;

/// <summary>Resource-free retained evidence for one bound loader invocation.</summary>
public sealed class EcosystemPopulationLoadReceipt
{
    internal EcosystemPopulationLoadReceipt(
        EcosystemPopulationLoadRequestSnapshot request,
        EcosystemPopulationLoadSettlementKind settlementKind,
        EcosystemPopulationCompletionWitness? completion,
        IReadOnlyList<EcosystemPopulationChildSettlement> children,
        IReadOnlyList<EcosystemPopulationLoadDiagnostic> diagnostics)
    {
        Request = request;
        SettlementKind = settlementKind;
        Completion = completion;
        Children = Array.AsReadOnly([.. children]);
        Diagnostics = Array.AsReadOnly([.. diagnostics]);
    }

    public EcosystemPopulationLoadRequestSnapshot Request { get; }
    public EcosystemPopulationLoadSettlementKind SettlementKind { get; }
    public EcosystemPopulationCompletionWitness? Completion { get; }
    public IReadOnlyList<EcosystemPopulationChildSettlement> Children { get; }
    public IReadOnlyList<EcosystemPopulationLoadDiagnostic> Diagnostics
    {
        get;
    }
}

/// <summary>Observable state of one owner-preserving load result.</summary>
public enum EcosystemPopulationOwnerBatchState
{
    Active,
    Retiring,
    Retired,
    RetirementFailed,
}

/// <summary>The result of taking one exact Library authority from a batch.</summary>
public abstract class EcosystemPopulationOwnerTakeOutcome
{
    private protected EcosystemPopulationOwnerTakeOutcome()
    {
    }

    [ResourceOwnership]
    public sealed class Transferred : EcosystemPopulationOwnerTakeOutcome
    {
        internal Transferred(
            LibraryContentOwner owner,
            EcosystemPopulationLoadedLibraryReference library)
        {
            Owner = owner;
            Library = library;
        }

        public LibraryContentOwner Owner { get; }
        public EcosystemPopulationLoadedLibraryReference Library { get; }
    }

    public sealed class NotFound : EcosystemPopulationOwnerTakeOutcome
    {
        internal NotFound()
        {
        }
    }

    public sealed class AlreadyTransferred :
        EcosystemPopulationOwnerTakeOutcome
    {
        internal AlreadyTransferred()
        {
        }
    }

    public sealed class Retired : EcosystemPopulationOwnerTakeOutcome
    {
        internal Retired()
        {
        }
    }
}

/// <summary>
/// The result of taking one exact adjacent Artifact session from a batch.
/// </summary>
public abstract class EcosystemPopulationArtifactSessionTakeOutcome
{
    private protected EcosystemPopulationArtifactSessionTakeOutcome()
    {
    }

    [ResourceOwnership]
    public sealed class Transferred :
        EcosystemPopulationArtifactSessionTakeOutcome
    {
        internal Transferred(
            ArtifactSetSession session,
            EcosystemPopulationChildSettlement childSettlement)
        {
            Session = session;
            ChildSettlement = childSettlement;
        }

        public ArtifactSetSession Session { get; }
        public EcosystemPopulationChildSettlement ChildSettlement { get; }
    }

    public sealed class NotFound :
        EcosystemPopulationArtifactSessionTakeOutcome
    {
        internal NotFound()
        {
        }
    }

    public sealed class AlreadyTransferred :
        EcosystemPopulationArtifactSessionTakeOutcome
    {
        internal AlreadyTransferred()
        {
        }
    }

    public sealed class Retired :
        EcosystemPopulationArtifactSessionTakeOutcome
    {
        internal Retired()
        {
        }
    }
}

/// <summary>
/// One-shot transfer and retirement boundary for returned Library owners and
/// adjacent Artifact sessions.
/// </summary>
[ResourceOwnership]
public sealed class EcosystemPopulationOwnerBatch : IAsyncDisposable
{
    readonly object _gate = new();
    readonly Entry[] _entries;
    readonly ArtifactSessionEntry[] _artifactSessions;
    Task? _retirementTask;
    EcosystemPopulationOwnerBatchState _state =
        EcosystemPopulationOwnerBatchState.Active;

    internal EcosystemPopulationOwnerBatch(
        IReadOnlyList<EcosystemPopulationOwnedLibraryContribution> ownerships,
        IReadOnlyList<EcosystemPopulationOwnedArtifactSessionContribution>
            artifactSessions)
    {
        _entries = ownerships
            .Select(
                contribution =>
                {
                    EcosystemPopulationLibraryOwnership ownership =
                        contribution.Ownership;
                    var reference =
                        new EcosystemPopulationLoadedLibraryReference(
                            ownership.Reference,
                            ownership.Roles,
                            contribution.ChildSettlement);
                    return new Entry(ownership.Owner, reference);
                })
            .ToArray();
        Libraries = Array.AsReadOnly(
            _entries.Select(entry => entry.Library).ToArray());
        _artifactSessions = artifactSessions
            .Select(
                contribution =>
                    new ArtifactSessionEntry(
                        contribution.Session,
                        contribution.ChildSettlement))
            .ToArray();
        ArtifactSessionChildren = Array.AsReadOnly(
            _artifactSessions
                .Select(entry => entry.ChildSettlement)
                .ToArray());
    }

    public IReadOnlyList<EcosystemPopulationLoadedLibraryReference> Libraries
    {
        get;
    }
    public IReadOnlyList<EcosystemPopulationChildSettlement>
        ArtifactSessionChildren
    {
        get;
    }

    public EcosystemPopulationOwnerBatchState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public EcosystemPopulationOwnerTakeOutcome Take(
        LibraryReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        lock (_gate)
        {
            Entry? entry = _entries.SingleOrDefault(
                item => ReferenceEquals(item.Library.Reference, reference));
            if (entry is null)
                return new EcosystemPopulationOwnerTakeOutcome.NotFound();
            if (entry.Transferred)
            {
                return new EcosystemPopulationOwnerTakeOutcome
                    .AlreadyTransferred();
            }
            if (entry.Retired || _state != EcosystemPopulationOwnerBatchState.Active)
                return new EcosystemPopulationOwnerTakeOutcome.Retired();

            LibraryContentOwner owner = entry.Owner!;
            entry.Owner = null;
            entry.Transferred = true;
            return new EcosystemPopulationOwnerTakeOutcome.Transferred(
                owner,
                entry.Library);
        }
    }

    public EcosystemPopulationArtifactSessionTakeOutcome TakeArtifactSession(
        EcosystemPopulationChildSettlement childSettlement)
    {
        ArgumentNullException.ThrowIfNull(childSettlement);
        lock (_gate)
        {
            ArtifactSessionEntry? entry =
                _artifactSessions.SingleOrDefault(
                    item => ReferenceEquals(
                        item.ChildSettlement,
                        childSettlement));
            if (entry is null)
            {
                return new EcosystemPopulationArtifactSessionTakeOutcome
                    .NotFound();
            }
            if (entry.Transferred)
            {
                return new EcosystemPopulationArtifactSessionTakeOutcome
                    .AlreadyTransferred();
            }
            if (entry.Retired
                || _state != EcosystemPopulationOwnerBatchState.Active)
            {
                return new EcosystemPopulationArtifactSessionTakeOutcome
                    .Retired();
            }

            ArtifactSetSession session = entry.Session!;
            entry.Session = null;
            entry.Transferred = true;
            return new EcosystemPopulationArtifactSessionTakeOutcome
                .Transferred(session, entry.ChildSettlement);
        }
    }

    internal EcosystemPopulationChildAuthorityTransfer
        TakeChildAuthorities(
            EcosystemPopulationChildSettlement childSettlement)
    {
        ArgumentNullException.ThrowIfNull(childSettlement);
        lock (_gate)
        {
            if (_state != EcosystemPopulationOwnerBatchState.Active)
            {
                throw new InvalidOperationException(
                    "The Ecosystem population owner batch is not active.");
            }

            Entry[] libraryEntries =
            [
                .. _entries.Where(
                    entry => ReferenceEquals(
                        entry.Library.ChildSettlement,
                        childSettlement)),
            ];
            ArtifactSessionEntry? artifactSessionEntry =
                _artifactSessions.SingleOrDefault(
                    entry => ReferenceEquals(
                        entry.ChildSettlement,
                        childSettlement));
            if (libraryEntries.Length == 0
                && artifactSessionEntry is null)
            {
                throw new InvalidOperationException(
                    "The child has no transferable authorities.");
            }
            if (libraryEntries.Any(
                    entry => entry.Transferred || entry.Retired)
                || artifactSessionEntry?.Transferred == true
                || artifactSessionEntry?.Retired == true)
            {
                throw new InvalidOperationException(
                    "One or more child authorities were already settled.");
            }

            LibraryContentOwner[] owners =
            [
                .. libraryEntries.Select(
                    entry =>
                    {
                        LibraryContentOwner owner = entry.Owner!;
                        entry.Owner = null;
                        entry.Transferred = true;
                        return owner;
                    }),
            ];
            ArtifactSetSession? artifactSession = null;
            if (artifactSessionEntry is not null)
            {
                artifactSession = artifactSessionEntry.Session!;
                artifactSessionEntry.Session = null;
                artifactSessionEntry.Transferred = true;
            }

            return new(
                owners,
                libraryEntries.Select(entry => entry.Library).ToArray(),
                artifactSession);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_retirementTask is not null)
                return new ValueTask(_retirementTask);

            _state = EcosystemPopulationOwnerBatchState.Retiring;
            LibraryContentOwner[] owners =
            [
                .. _entries
                    .Where(entry => entry.Owner is not null)
                    .Select(
                        entry =>
                        {
                            LibraryContentOwner owner = entry.Owner!;
                            entry.Owner = null;
                            entry.Retired = true;
                            return owner;
                        }),
            ];
            ArtifactSetSession[] artifactSessions =
            [
                .. _artifactSessions
                    .Where(entry => entry.Session is not null)
                    .Select(
                        entry =>
                        {
                            ArtifactSetSession session = entry.Session!;
                            entry.Session = null;
                            entry.Retired = true;
                            return session;
                        }),
            ];
            _retirementTask = RetireAsync(owners, artifactSessions);
            return new ValueTask(_retirementTask);
        }
    }

    async Task RetireAsync(
        IReadOnlyList<LibraryContentOwner> owners,
        IReadOnlyList<ArtifactSetSession> artifactSessions)
    {
        List<Exception>? failures = null;
        foreach (LibraryContentOwner owner in owners)
        {
            try
            {
                await owner.DisposeAsync();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }
        foreach (ArtifactSetSession artifactSession in artifactSessions)
        {
            try
            {
                await artifactSession.DisposeAsync();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
            if (artifactSession.CleanupFailures.Count != 0)
            {
                (failures ??= []).AddRange(
                    artifactSession.CleanupFailures);
            }
        }

        lock (_gate)
        {
            _state = failures is null
                ? EcosystemPopulationOwnerBatchState.Retired
                : EcosystemPopulationOwnerBatchState.RetirementFailed;
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more Ecosystem population authorities failed to retire.",
                failures);
        }
    }

    sealed class Entry(
        LibraryContentOwner owner,
        EcosystemPopulationLoadedLibraryReference library)
    {
        public LibraryContentOwner? Owner { get; set; } = owner;
        public EcosystemPopulationLoadedLibraryReference Library { get; } =
            library;
        public bool Transferred { get; set; }
        public bool Retired { get; set; }
    }

    sealed class ArtifactSessionEntry(
        ArtifactSetSession session,
        EcosystemPopulationChildSettlement childSettlement)
    {
        public ArtifactSetSession? Session { get; set; } = session;
        public EcosystemPopulationChildSettlement ChildSettlement { get; } =
            childSettlement;
        public bool Transferred { get; set; }
        public bool Retired { get; set; }
    }
}

[ResourceOwnership]
internal sealed class EcosystemPopulationChildAuthorityTransfer :
    IAsyncDisposable
{
    readonly LibraryContentOwner[] _owners;
    readonly ArtifactSetSession? _artifactSession;
    bool _consumed;

    internal EcosystemPopulationChildAuthorityTransfer(
        LibraryContentOwner[] owners,
        EcosystemPopulationLoadedLibraryReference[] libraries,
        ArtifactSetSession? artifactSession)
    {
        _owners = owners;
        Libraries = Array.AsReadOnly(libraries);
        _artifactSession = artifactSession;
    }

    internal IReadOnlyList<LibraryContentOwner> Owners => _owners;

    internal IReadOnlyList<EcosystemPopulationLoadedLibraryReference>
        Libraries
    {
        get;
    }

    internal ArtifactSetSession? ArtifactSession => _artifactSession;

    internal void MarkConsumed() => _consumed = true;

    public async ValueTask DisposeAsync()
    {
        if (_consumed)
            return;

        List<Exception>? failures = null;
        foreach (LibraryContentOwner owner in _owners)
        {
            try
            {
                await owner.DisposeAsync();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }
        if (_artifactSession is not null)
        {
            try
            {
                await _artifactSession.DisposeAsync();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
            if (_artifactSession.CleanupFailures.Count != 0)
            {
                (failures ??= []).AddRange(
                    _artifactSession.CleanupFailures);
            }
        }

        _consumed = true;
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more Ecosystem population child authorities failed to retire.",
                failures);
        }
    }
}

/// <summary>Closed owning result of one bound loader invocation.</summary>
public abstract class EcosystemPopulationLoadOutcome
{
    private protected EcosystemPopulationLoadOutcome(
        EcosystemPopulationLoadReceipt receipt,
        EcosystemPopulationLoadSettlementKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.SettlementKind != expectedKind)
        {
            throw new ArgumentException(
                $"The receipt must represent a {expectedKind} settlement.",
                nameof(receipt));
        }

        Receipt = receipt;
    }

    public EcosystemPopulationLoadReceipt Receipt { get; }

    [ResourceOwnership]
    public sealed class Completed : EcosystemPopulationLoadOutcome
    {
        internal Completed(
            EcosystemPopulationLoadReceipt receipt,
            EcosystemPopulationOwnerBatch owners)
            : base(receipt, EcosystemPopulationLoadSettlementKind.Completed) =>
            Owners = owners;

        public EcosystemPopulationOwnerBatch Owners { get; }
    }

    public sealed class Unavailable : EcosystemPopulationLoadOutcome
    {
        internal Unavailable(EcosystemPopulationLoadReceipt receipt)
            : base(
                receipt,
                EcosystemPopulationLoadSettlementKind.Unavailable)
        {
        }
    }

    public sealed class Ambiguous : EcosystemPopulationLoadOutcome
    {
        internal Ambiguous(EcosystemPopulationLoadReceipt receipt)
            : base(receipt, EcosystemPopulationLoadSettlementKind.Ambiguous)
        {
        }
    }

    [ResourceOwnership]
    public sealed class Incomplete : EcosystemPopulationLoadOutcome
    {
        internal Incomplete(
            EcosystemPopulationLoadReceipt receipt,
            EcosystemPopulationOwnerBatch owners)
            : base(receipt, EcosystemPopulationLoadSettlementKind.Incomplete) =>
            Owners = owners;

        public EcosystemPopulationOwnerBatch Owners { get; }
    }

    public sealed class Rejected : EcosystemPopulationLoadOutcome
    {
        internal Rejected(EcosystemPopulationLoadReceipt receipt)
            : base(receipt, EcosystemPopulationLoadSettlementKind.Rejected)
        {
        }
    }

    public sealed class Failed : EcosystemPopulationLoadOutcome
    {
        internal Failed(EcosystemPopulationLoadReceipt receipt)
            : base(receipt, EcosystemPopulationLoadSettlementKind.Failed)
        {
        }
    }
}
