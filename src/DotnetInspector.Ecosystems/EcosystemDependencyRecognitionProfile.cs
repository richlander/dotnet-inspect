using System.Collections.Immutable;

namespace DotnetInspector.Ecosystems;

/// <summary>The identity domain matched by one ecosystem association.</summary>
public enum EcosystemDependencyIdentityDomain
{
    PackageId,
    AssemblyName,
}

/// <summary>The matching behavior of one authored ecosystem association.</summary>
public enum EcosystemDependencyAssociationKind
{
    Exact,
    Family,
}

/// <summary>
/// One product-authored Package ID or assembly-name association.
/// </summary>
public sealed record EcosystemDependencyAssociation
{
    private EcosystemDependencyAssociation(
        EcosystemDependencyIdentityDomain domain,
        EcosystemDependencyAssociationKind kind,
        string value)
    {
        Domain = domain;
        Kind = kind;
        Value = value;
    }

    public EcosystemDependencyIdentityDomain Domain { get; }

    public EcosystemDependencyAssociationKind Kind { get; }

    public string Value { get; }

    internal static EcosystemDependencyAssociation ExactPackageId(
        string value) =>
        Create(
            EcosystemDependencyIdentityDomain.PackageId,
            EcosystemDependencyAssociationKind.Exact,
            value);

    internal static EcosystemDependencyAssociation PackageIdFamily(
        string value) =>
        Create(
            EcosystemDependencyIdentityDomain.PackageId,
            EcosystemDependencyAssociationKind.Family,
            value);

    internal static EcosystemDependencyAssociation ExactAssemblyName(
        string value) =>
        Create(
            EcosystemDependencyIdentityDomain.AssemblyName,
            EcosystemDependencyAssociationKind.Exact,
            value);

    internal static EcosystemDependencyAssociation AssemblyNameFamily(
        string value) =>
        Create(
            EcosystemDependencyIdentityDomain.AssemblyName,
            EcosystemDependencyAssociationKind.Family,
            value);

    internal bool Matches(
        EcosystemDependencyIdentityDomain domain,
        string candidate)
    {
        if (domain != Domain)
            return false;

        StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        return Kind switch
        {
            EcosystemDependencyAssociationKind.Exact =>
                candidate.Equals(Value, comparison),
            EcosystemDependencyAssociationKind.Family =>
                candidate.Equals(Value, comparison)
                || candidate.Length > Value.Length + 1
                    && candidate[Value.Length] == '.'
                    && candidate[Value.Length + 1] != '.'
                    && candidate.StartsWith(Value, comparison),
            _ => throw new InvalidOperationException(
                "Unknown ecosystem dependency association kind."),
        };
    }

    private static EcosystemDependencyAssociation Create(
        EcosystemDependencyIdentityDomain domain,
        EcosystemDependencyAssociationKind kind,
        string value)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain), domain, null);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "An ecosystem dependency association value is required.",
                nameof(value));
        }
        if (!value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An ecosystem dependency association cannot contain surrounding whitespace.",
                nameof(value));
        }
        if (kind == EcosystemDependencyAssociationKind.Family
            && value.Split('.').Any(static segment => segment.Length == 0))
        {
            throw new ArgumentException(
                "An ecosystem dependency family cannot contain an empty segment.",
                nameof(value));
        }

        return new EcosystemDependencyAssociation(domain, kind, value);
    }
}

/// <summary>
/// Product identity retained by ecosystem dependency recognition.
/// </summary>
public sealed record EcosystemDependencyDescriptor
{
    internal EcosystemDependencyDescriptor(
        EcosystemPackId id,
        string title,
        int order)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfLessThan(order, 1);
        Title = title;
        Order = order;
    }

    public EcosystemPackId Id { get; }

    public string Title { get; }

    public int Order { get; }
}

/// <summary>
/// One ecosystem and its independently authored dependency associations.
/// </summary>
public sealed class EcosystemDependencyProfileEntry
{
    internal EcosystemDependencyProfileEntry(
        EcosystemDependencyDescriptor ecosystem,
        ImmutableArray<EcosystemDependencyAssociation> associations)
    {
        Ecosystem = ecosystem;
        Associations = associations;
    }

    public EcosystemDependencyDescriptor Ecosystem { get; }

    public ImmutableArray<EcosystemDependencyAssociation> Associations { get; }
}

/// <summary>
/// A validated immutable product profile for direct-dependency recognition.
/// </summary>
public sealed class EcosystemDependencyRecognitionProfile
{
    private readonly ImmutableArray<EcosystemDependencyProfileEntry> _entries;

    internal EcosystemDependencyRecognitionProfile(
        IEnumerable<EcosystemPackDescriptor> knownEcosystems,
        IEnumerable<EcosystemDependencyProfileRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(knownEcosystems);
        ArgumentNullException.ThrowIfNull(registrations);

        EcosystemPackDescriptor[] known = [.. knownEcosystems];
        EcosystemDependencyProfileRegistration[] manifest = [.. registrations];
        if (known.Length == 0
            || known.Any(static descriptor => descriptor is null))
        {
            throw new ArgumentException(
                "A recognition profile requires a nonempty known-ecosystem catalog.",
                nameof(knownEcosystems));
        }
        if (manifest.Length == 0)
        {
            throw new ArgumentException(
                "An ecosystem dependency recognition profile must contain at least one entry.",
                nameof(registrations));
        }

        var knownById =
            new Dictionary<EcosystemPackId, EcosystemPackDescriptor>();
        foreach (EcosystemPackDescriptor descriptor in known)
        {
            if (!knownById.TryAdd(descriptor.Id, descriptor))
            {
                throw new ArgumentException(
                    $"Known ecosystem '{descriptor.Id}' occurs more than once.",
                    nameof(knownEcosystems));
            }
        }
        var registered = new HashSet<EcosystemPackId>();
        var entries =
            ImmutableArray.CreateBuilder<EcosystemDependencyProfileEntry>(
                manifest.Length);

        foreach (EcosystemDependencyProfileRegistration registration in
                 manifest)
        {
            if (registration is null)
            {
                throw new ArgumentException(
                    "An ecosystem dependency recognition profile cannot contain null entries.",
                    nameof(registrations));
            }
            if (!knownById.TryGetValue(
                    registration.Ecosystem,
                    out EcosystemPackDescriptor? descriptor))
            {
                throw new ArgumentException(
                    $"Ecosystem '{registration.Ecosystem}' is not registered.",
                    nameof(registrations));
            }
            if (!registered.Add(registration.Ecosystem))
            {
                throw new ArgumentException(
                    $"Ecosystem '{registration.Ecosystem}' is registered more than once.",
                    nameof(registrations));
            }
            if (descriptor.Order <= 0)
            {
                throw new ArgumentException(
                    $"Ecosystem '{registration.Ecosystem}' has no positive product order.",
                    nameof(knownEcosystems));
            }

            EcosystemDependencyAssociation[] associations =
            [
                .. registration.Associations
                    ?? throw new ArgumentException(
                        $"Ecosystem '{registration.Ecosystem}' has no association sequence.",
                        nameof(registrations)),
            ];
            if (associations.Any(static association => association is null))
            {
                throw new ArgumentException(
                    $"Ecosystem '{registration.Ecosystem}' contains a null association.",
                    nameof(registrations));
            }

            var associationKeys = new HashSet<AssociationKey>();
            foreach (EcosystemDependencyAssociation association in associations)
            {
                var key = new AssociationKey(
                    association.Domain,
                    association.Kind,
                    association.Value);
                if (!associationKeys.Add(key))
                {
                    throw new ArgumentException(
                        $"Ecosystem '{registration.Ecosystem}' contains a duplicate"
                        + $" {association.Domain} {association.Kind} association"
                        + $" for '{association.Value}'.",
                        nameof(registrations));
                }
            }

            entries.Add(new EcosystemDependencyProfileEntry(
                new EcosystemDependencyDescriptor(
                    descriptor.Id,
                    descriptor.Title,
                    descriptor.Order),
                [.. associations]));
        }

        _entries =
        [
            .. entries
                .OrderBy(static entry => entry.Ecosystem.Order),
        ];
        PackageAssociationCount = _entries.Sum(static entry =>
            entry.Associations.Count(static association =>
                association.Domain
                    == EcosystemDependencyIdentityDomain.PackageId));
        AssemblyAssociationCount = _entries.Sum(static entry =>
            entry.Associations.Count(static association =>
                association.Domain
                    == EcosystemDependencyIdentityDomain.AssemblyName));
    }

    public ImmutableArray<EcosystemDependencyProfileEntry> Entries => _entries;

    public int PackageAssociationCount { get; }

    public int AssemblyAssociationCount { get; }

    private readonly record struct AssociationKey(
        EcosystemDependencyIdentityDomain Domain,
        EcosystemDependencyAssociationKind Kind,
        string Value)
    {
        public bool Equals(AssociationKey other) =>
            Domain == other.Domain
            && Kind == other.Kind
            && Value.Equals(other.Value, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() =>
            HashCode.Combine(
                Domain,
                Kind,
                StringComparer.OrdinalIgnoreCase.GetHashCode(Value));
    }
}

internal sealed record EcosystemDependencyProfileRegistration(
    EcosystemPackId Ecosystem,
    IReadOnlyList<EcosystemDependencyAssociation> Associations);
