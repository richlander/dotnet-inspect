using DotnetInspector.Libraries;
using Inspector.Resources;

namespace DotnetInspector.EcosystemLoading;

/// <summary>The terminal status of one selected adjacent-owner operation.</summary>
public enum EcosystemPopulationChildSettlementKind
{
    Completed,
    Unavailable,
    Incomplete,
    Rejected,
    Failed,
}

/// <summary>How one completed child operation describes its Library members.</summary>
public enum EcosystemPopulationChildCompletionKind
{
    Members,
    NoMembers,
}

/// <summary>
/// Opaque identity for one exact adjacent-owner request.
/// </summary>
public sealed class EcosystemPopulationChildRequestIdentity
{
    EcosystemPopulationChildRequestIdentity(string name) => Name = name;

    public string Name { get; }

    public static EcosystemPopulationChildRequestIdentity Create(
        string name) =>
        new(EcosystemPopulationIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>
/// Opaque identity for one exact adjacent-owner receipt bound to its request.
/// </summary>
public sealed class EcosystemPopulationChildReceiptIdentity
{
    EcosystemPopulationChildReceiptIdentity(
        EcosystemPopulationChildRequestIdentity request,
        string name)
    {
        Request = request;
        Name = name;
    }

    public EcosystemPopulationChildRequestIdentity Request { get; }
    public string Name { get; }

    public static EcosystemPopulationChildReceiptIdentity Create(
        EcosystemPopulationChildRequestIdentity request,
        string name)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(
            request,
            EcosystemPopulationIdentityName.Validate(name));
    }

    public override string ToString() => Name;
}

/// <summary>
/// Resource-free exact adjacent-owner request and terminal receipt identities.
/// </summary>
public sealed class EcosystemPopulationChildSettlement
{
    internal EcosystemPopulationChildSettlement(
        EcosystemPopulationLoadRequestIdentity request,
        EcosystemPopulationChildRequestIdentity childRequest,
        EcosystemPopulationChildReceiptIdentity childReceipt,
        EcosystemPopulationChildSettlementKind kind,
        EcosystemPopulationChildCompletionKind? completionKind)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(childRequest);
        ArgumentNullException.ThrowIfNull(childReceipt);
        if (!ReferenceEquals(childReceipt.Request, childRequest))
        {
            throw new ArgumentException(
                "The child receipt must be bound to the exact child request.",
                nameof(childReceipt));
        }
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if ((kind == EcosystemPopulationChildSettlementKind.Completed)
            != completionKind.HasValue)
        {
            throw new ArgumentException(
                "Only a completed child settlement has a completion kind.",
                nameof(completionKind));
        }
        if (completionKind.HasValue
            && !Enum.IsDefined(completionKind.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(completionKind));
        }

        ParentRequest = request;
        Request = childRequest;
        Receipt = childReceipt;
        Kind = kind;
        CompletionKind = completionKind;
    }

    internal EcosystemPopulationLoadRequestIdentity ParentRequest { get; }
    public EcosystemPopulationChildRequestIdentity Request { get; }
    public EcosystemPopulationChildReceiptIdentity Receipt { get; }
    public EcosystemPopulationChildSettlementKind Kind { get; }
    public EcosystemPopulationChildCompletionKind? CompletionKind { get; }
}

/// <summary>Roles one loaded Library has in its Ecosystem population.</summary>
[Flags]
public enum EcosystemPopulationLibraryRole
{
    Focus = 1,
    BindingSupport = 2,
}

/// <summary>
/// One adjacent-owner Library authority supplied to a completed child token.
/// </summary>
[ResourceOwnership]
public sealed class EcosystemPopulationLibraryOwnership
{
    public EcosystemPopulationLibraryOwnership(
        LibraryContentOwner owner,
        EcosystemPopulationLibraryRole roles)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ValidateRoles(roles);
        Owner = owner;
        Roles = roles;
    }

    public LibraryContentOwner Owner { get; }
    public LibraryReference Reference => Owner.Reference;
    public EcosystemPopulationLibraryRole Roles { get; }

    internal static void ValidateRoles(EcosystemPopulationLibraryRole roles)
    {
        const EcosystemPopulationLibraryRole all =
            EcosystemPopulationLibraryRole.Focus
            | EcosystemPopulationLibraryRole.BindingSupport;
        if (roles == 0 || (roles & ~all) != 0)
            throw new ArgumentOutOfRangeException(nameof(roles));
    }
}

/// <summary>
/// One independently completed child operation and any Library owners it
/// transferred.
/// </summary>
[ResourceOwnership]
public sealed class EcosystemPopulationCompletedChild
{
    readonly EcosystemPopulationLibraryOwnership[] _ownerships;

    public EcosystemPopulationCompletedChild(
        EcosystemPopulationChildSettlement settlement,
        IEnumerable<EcosystemPopulationLibraryOwnership> libraries)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(libraries);
        if (settlement.Kind
            != EcosystemPopulationChildSettlementKind.Completed)
        {
            throw new ArgumentException(
                "Only a completed child settlement may transfer Library owners.",
                nameof(settlement));
        }

        _ownerships = EcosystemPopulationSnapshots.Ownerships(libraries);
        if ((settlement.CompletionKind
                == EcosystemPopulationChildCompletionKind.Members)
            != (_ownerships.Length > 0))
        {
            throw new ArgumentException(
                "A member completion must transfer at least one Library owner, while a no-members completion transfers none.",
                nameof(libraries));
        }

        Settlement = settlement;
        Libraries = Array.AsReadOnly(
            _ownerships
                .Select(
                    ownership =>
                        new EcosystemPopulationLoadedLibraryReference(
                            ownership.Reference,
                            ownership.Roles,
                            settlement))
                .ToArray());
    }

    public EcosystemPopulationChildSettlement Settlement { get; }
    public IReadOnlyList<EcosystemPopulationLoadedLibraryReference> Libraries
    {
        get;
    }

    internal IReadOnlyList<EcosystemPopulationLibraryOwnership> Ownerships =>
        _ownerships;
}

/// <summary>Resource-free projection of one loaded Library contribution.</summary>
public sealed class EcosystemPopulationLoadedLibraryReference
{
    internal EcosystemPopulationLoadedLibraryReference(
        LibraryReference reference,
        EcosystemPopulationLibraryRole roles,
        EcosystemPopulationChildSettlement childSettlement)
    {
        Reference = reference;
        Roles = roles;
        ChildSettlement = childSettlement;
    }

    public LibraryReference Reference { get; }
    public EcosystemPopulationLibraryRole Roles { get; }
    public EcosystemPopulationChildSettlement ChildSettlement { get; }
}
