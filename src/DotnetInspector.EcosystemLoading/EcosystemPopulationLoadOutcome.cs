using DotnetInspector.Libraries;
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
/// One-shot transfer and retirement boundary for returned Library owners.
/// </summary>
[ResourceOwnership]
public sealed class EcosystemPopulationOwnerBatch : IAsyncDisposable
{
    readonly object _gate = new();
    readonly Entry[] _entries;
    Task? _retirementTask;
    EcosystemPopulationOwnerBatchState _state =
        EcosystemPopulationOwnerBatchState.Active;

    internal EcosystemPopulationOwnerBatch(
        IReadOnlyList<EcosystemPopulationOwnedLibraryContribution> ownerships)
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
    }

    public IReadOnlyList<EcosystemPopulationLoadedLibraryReference> Libraries
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
            _retirementTask = RetireAsync(owners);
            return new ValueTask(_retirementTask);
        }
    }

    async Task RetireAsync(IReadOnlyList<LibraryContentOwner> owners)
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

        lock (_gate)
        {
            _state = failures is null
                ? EcosystemPopulationOwnerBatchState.Retired
                : EcosystemPopulationOwnerBatchState.RetirementFailed;
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more Ecosystem population Library owners failed to retire.",
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
