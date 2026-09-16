using System.Collections.Immutable;
using DotnetInspector.Packages;

namespace DotnetInspector.Ecosystems;

internal sealed record PackageSetRegistration(
    PackageSetId Id,
    string Title,
    string Summary,
    int Order,
    IReadOnlyList<PackageCoordinate> Members);

internal sealed class PackageSetRegistry
{
    private readonly Dictionary<PackageSetId, PackageSetDescriptor> _byId;

    internal PackageSetRegistry(IEnumerable<PackageSetRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        PackageSetRegistration[] manifest = [.. registrations];
        if (manifest.Length == 0)
        {
            throw new ArgumentException(
                "A package-set manifest must contain at least one registration.",
                nameof(registrations));
        }

        var descriptors =
            ImmutableArray.CreateBuilder<PackageSetDescriptor>(manifest.Length);
        _byId = new Dictionary<PackageSetId, PackageSetDescriptor>();
        int previousOrder = default;
        bool hasPreviousOrder = false;

        for (int index = 0; index < manifest.Length; index++)
        {
            PackageSetRegistration registration = manifest[index]
                ?? throw new ArgumentException(
                    "A package-set manifest cannot contain null registrations.",
                    nameof(registrations));
            if (string.IsNullOrWhiteSpace(registration.Title)
                || string.IsNullOrWhiteSpace(registration.Summary))
            {
                throw new ArgumentException(
                    $"Package set '{registration.Id}' must have a title and summary.",
                    nameof(registrations));
            }

            if (hasPreviousOrder && registration.Order <= previousOrder)
            {
                throw new ArgumentException(
                    "Package-set registrations must use strictly ascending order.",
                    nameof(registrations));
            }

            if (_byId.ContainsKey(registration.Id))
            {
                throw new ArgumentException(
                    $"Package set '{registration.Id}' is registered more than once.",
                    nameof(registrations));
            }

            PackageCoordinate[] members =
            [
                .. registration.Members
                    ?? throw new ArgumentException(
                        $"Package set '{registration.Id}' has no member sequence.",
                        nameof(registrations)),
            ];
            var packageIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (PackageCoordinate member in members)
            {
                if (member is null)
                {
                    throw new ArgumentException(
                        $"Package set '{registration.Id}' contains a null member.",
                        nameof(registrations));
                }

                PackageCoordinateResolution.Invalid? invalid =
                    PackageCoordinateResolver.Validate(member);
                if (invalid is not null)
                {
                    throw new ArgumentException(
                        $"Package set '{registration.Id}' contains invalid package"
                        + $" coordinate '{member.PackageId}': {invalid.Message}",
                        nameof(registrations));
                }

                if (member.Version is not null
                    || member.Framework is not null
                    || member.RuntimeIdentifier is not null)
                {
                    throw new ArgumentException(
                        $"Package set '{registration.Id}' contains a versioned or"
                        + " target-specific package coordinate.",
                        nameof(registrations));
                }

                if (!packageIds.Add(member.PackageId))
                {
                    throw new ArgumentException(
                        $"Package set '{registration.Id}' contains duplicate package"
                        + $" ID '{member.PackageId}'.",
                        nameof(registrations));
                }
            }

            var descriptor = new PackageSetDescriptor(
                registration.Id,
                registration.Title,
                registration.Summary,
                registration.Order,
                members);
            descriptors.Add(descriptor);
            _byId.Add(descriptor.Id, descriptor);
            previousOrder = registration.Order;
            hasPreviousOrder = true;
        }

        Descriptors = descriptors.MoveToImmutable();
    }

    internal ImmutableArray<PackageSetDescriptor> Descriptors { get; }

    internal PackageSetLookupResult Lookup(PackageSetId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.TryGetValue(id, out PackageSetDescriptor? descriptor)
            ? new PackageSetLookupResult.Known(descriptor)
            : new PackageSetLookupResult.Unknown(id);
    }
}

/// <summary>Discovery and exact lookup for shipped package sets.</summary>
public static class PackageSetCatalog
{
    public static ImmutableArray<PackageSetDescriptor> Discover() =>
        ShippedPackageSets.Registry.Descriptors;

    public static PackageSetLookupResult Lookup(PackageSetId id) =>
        ShippedPackageSets.Registry.Lookup(id);
}
