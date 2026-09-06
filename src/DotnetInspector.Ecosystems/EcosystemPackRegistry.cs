using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems;

internal sealed record EcosystemDemoRegistration(
    string Title,
    string Summary,
    int Order,
    ProductDemoSourceBinding Source);

internal sealed record EcosystemPackRegistration(
    EcosystemPackId Id,
    string Title,
    string Summary,
    int Order,
    PackageSetId? PackageSet,
    IReadOnlyList<EcosystemDemoRegistration> Demos,
    EcosystemIntegrationScannerBinding? Scanner = null)
{
    public IReadOnlyList<string> NamespaceRoots { get; init; } = [];

    public IReadOnlyList<PackageCoordinate> CorePackages { get; init; } = [];

    public IReadOnlyList<PackageCoordinate> ToolPackages { get; init; } = [];
}

internal sealed class EcosystemPackRegistry
{
    private sealed record PackEntry(
        EcosystemPackDescriptor Descriptor,
        EcosystemIntegrationScannerBinding? Scanner);

    private sealed record DemoEntry(
        EcosystemDemoDescriptor Descriptor,
        ProductDemoSourceBinding Source);

    private readonly Dictionary<EcosystemPackId, PackEntry> _packsById;
    private readonly Dictionary<string, DemoEntry> _demosById;

    internal EcosystemPackRegistry(IEnumerable<EcosystemPackRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        EcosystemPackRegistration[] manifest = [.. registrations];
        if (manifest.Length == 0)
        {
            throw new ArgumentException(
                "An ecosystem-pack manifest must contain at least one registration.",
                nameof(registrations));
        }

        var packDescriptors =
            ImmutableArray.CreateBuilder<EcosystemPackDescriptor>(manifest.Length);
        var demoEntries = new List<DemoEntry>();
        _packsById = new Dictionary<EcosystemPackId, PackEntry>();
        _demosById = new Dictionary<string, DemoEntry>(StringComparer.Ordinal);
        var demoOrders = new HashSet<int>();
        int previousPackOrder = default;
        bool hasPreviousPackOrder = false;

        foreach (EcosystemPackRegistration registration in manifest)
        {
            if (registration is null)
            {
                throw new ArgumentException(
                    "An ecosystem-pack manifest cannot contain null registrations.",
                    nameof(registrations));
            }

            ValidateDisplayMetadata(
                registration.Id.ToString(),
                registration.Title,
                registration.Summary,
                nameof(registrations));
            if (hasPreviousPackOrder && registration.Order <= previousPackOrder)
            {
                throw new ArgumentException(
                    "Ecosystem-pack registrations must use strictly ascending order.",
                    nameof(registrations));
            }

            if (_packsById.ContainsKey(registration.Id))
            {
                throw new ArgumentException(
                    $"Ecosystem pack '{registration.Id}' is registered more than once.",
                    nameof(registrations));
            }

            ImmutableArray<string> namespaceRoots =
                SnapshotNamespaceRoots(registration, nameof(registrations));
            ImmutableArray<PackageCoordinate> corePackages =
                SnapshotPackageReferences(
                    registration.Id, registration.CorePackages, "core", nameof(registrations));
            ImmutableArray<PackageCoordinate> toolPackages =
                SnapshotPackageReferences(
                    registration.Id, registration.ToolPackages, "tool", nameof(registrations));
            EcosystemDemoRegistration[] demos =
            [
                .. registration.Demos
                    ?? throw new ArgumentException(
                        $"Ecosystem pack '{registration.Id}' has no demo sequence.",
                        nameof(registrations)),
            ];
            if (registration.PackageSet is null && demos.Length == 0
                && registration.Scanner is null)
            {
                throw new ArgumentException(
                    $"Ecosystem pack '{registration.Id}' must expose at least one capability.",
                    nameof(registrations));
            }

            var descriptors =
                ImmutableArray.CreateBuilder<EcosystemDemoDescriptor>(demos.Length);
            int previousDemoOrder = default;
            bool hasPreviousDemoOrder = false;
            foreach (EcosystemDemoRegistration demo in demos)
            {
                if (demo is null)
                {
                    throw new ArgumentException(
                        $"Ecosystem pack '{registration.Id}' contains a null demo.",
                        nameof(registrations));
                }

                ArgumentNullException.ThrowIfNull(demo.Source);
                ValidateDisplayMetadata(
                    demo.Source.ScenarioId,
                    demo.Title,
                    demo.Summary,
                    nameof(registrations));
                if (hasPreviousDemoOrder && demo.Order <= previousDemoOrder)
                {
                    throw new ArgumentException(
                        $"Ecosystem pack '{registration.Id}' demos must use strictly ascending order.",
                        nameof(registrations));
                }

                if (!demoOrders.Add(demo.Order))
                {
                    throw new ArgumentException(
                        $"Product demo order '{demo.Order}' is registered more than once.",
                        nameof(registrations));
                }

                var descriptor = new EcosystemDemoDescriptor(
                    registration.Id,
                    demo.Source.ScenarioId,
                    demo.Title,
                    demo.Summary,
                    demo.Order);
                var entry = new DemoEntry(descriptor, demo.Source);
                if (!_demosById.TryAdd(descriptor.ScenarioId, entry))
                {
                    throw new ArgumentException(
                        $"Product demo '{descriptor.ScenarioId}' is registered more than once.",
                        nameof(registrations));
                }

                descriptors.Add(descriptor);
                demoEntries.Add(entry);
                previousDemoOrder = demo.Order;
                hasPreviousDemoOrder = true;
            }

            var packDescriptor = new EcosystemPackDescriptor(
                registration.Id,
                registration.Title,
                registration.Summary,
                registration.Order,
                registration.PackageSet,
                descriptors.MoveToImmutable(),
                registration.Scanner is not null,
                namespaceRoots,
                corePackages,
                toolPackages);
            _packsById.Add(
                packDescriptor.Id,
                new PackEntry(packDescriptor, registration.Scanner));
            packDescriptors.Add(packDescriptor);
            previousPackOrder = registration.Order;
            hasPreviousPackOrder = true;
        }

        Packs = packDescriptors.MoveToImmutable();
        Demos =
        [
            .. demoEntries
                .OrderBy(entry => entry.Descriptor.Order)
                .Select(entry => entry.Descriptor),
        ];
    }

    internal ImmutableArray<EcosystemPackDescriptor> Packs { get; }

    internal ImmutableArray<EcosystemDemoDescriptor> Demos { get; }

    internal EcosystemPackLookupResult Lookup(EcosystemPackId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _packsById.TryGetValue(id, out PackEntry? entry)
            ? new EcosystemPackLookupResult.Known(entry.Descriptor)
            : new EcosystemPackLookupResult.Unknown(id);
    }

    internal EcosystemScannerSelectionResult SelectScanner(EcosystemPackId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!_packsById.TryGetValue(id, out PackEntry? entry))
            return new EcosystemScannerSelectionResult.Unknown(id);

        return entry.Scanner is { } binding
            ? new EcosystemScannerSelectionResult.Known(binding)
            : new EcosystemScannerSelectionResult.Unavailable(id);
    }

    internal EcosystemDemoSelectionResult SelectDemo(string scenarioId)
    {
        ArgumentNullException.ThrowIfNull(scenarioId);
        if (!_demosById.TryGetValue(scenarioId, out DemoEntry? entry))
            return new EcosystemDemoSelectionResult.Unknown(scenarioId);

        return new EcosystemDemoSelectionResult.Known(
            new EcosystemDemoSelection(
                entry.Descriptor,
                entry.Source.Resolve()));
    }

    private static ImmutableArray<string> SnapshotNamespaceRoots(
        EcosystemPackRegistration registration,
        string parameterName)
    {
        ImmutableArray<string> roots =
        [
            .. registration.NamespaceRoots
                ?? throw new ArgumentException(
                    $"Ecosystem pack '{registration.Id}' has no namespace-root sequence.",
                    parameterName),
        ];
        var uniqueRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)
                || root.Any(character => char.IsWhiteSpace(character) || character is '*' or '?')
                || root[0] == '.'
                || root[^1] == '.'
                || root.Contains("..", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Ecosystem pack '{registration.Id}' contains invalid namespace root '{root}'.",
                    parameterName);
            }

            if (!uniqueRoots.Add(root))
            {
                throw new ArgumentException(
                    $"Ecosystem pack '{registration.Id}' contains duplicate namespace root '{root}'.",
                    parameterName);
            }
        }

        return roots;
    }

    private static ImmutableArray<PackageCoordinate> SnapshotPackageReferences(
        EcosystemPackId packId,
        IReadOnlyList<PackageCoordinate> references,
        string role,
        string parameterName)
    {
        ImmutableArray<PackageCoordinate> packages =
        [
            .. references
                ?? throw new ArgumentException(
                    $"Ecosystem pack '{packId}' has no {role}-package sequence.",
                    parameterName),
        ];
        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PackageCoordinate package in packages)
        {
            if (package is null)
            {
                throw new ArgumentException(
                    $"Ecosystem pack '{packId}' contains a null {role} package.",
                    parameterName);
            }

            PackageCoordinateResolution.Invalid? invalid =
                PackageCoordinateResolver.Validate(package);
            if (invalid is not null)
            {
                throw new ArgumentException(
                    $"Ecosystem pack '{packId}' contains invalid {role} package"
                    + $" coordinate '{package.PackageId}': {invalid.Message}",
                    parameterName);
            }

            if (package.Version is not null
                || package.Framework is not null
                || package.RuntimeIdentifier is not null)
            {
                throw new ArgumentException(
                    $"Ecosystem pack '{packId}' contains a versioned or"
                    + $" target-specific {role}-package coordinate.",
                    parameterName);
            }

            if (!packageIds.Add(package.PackageId))
            {
                throw new ArgumentException(
                    $"Ecosystem pack '{packId}' contains duplicate {role}-package"
                    + $" ID '{package.PackageId}'.",
                    parameterName);
            }
        }

        return packages;
    }

    private static void ValidateDisplayMetadata(
        string id,
        string title,
        string summary,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException(
                $"Catalog entry '{id}' must have a title and summary.",
                parameterName);
        }
    }
}

/// <summary>Discovery and exact selection for shipped ecosystem content.</summary>
public static class EcosystemPackCatalog
{
    public static ImmutableArray<EcosystemPackDescriptor> Discover() =>
        ProductEcosystemPacks.Registry.Packs;

    public static ImmutableArray<EcosystemDemoDescriptor> DiscoverDemos() =>
        ProductEcosystemPacks.Registry.Demos;

    public static EcosystemPackLookupResult Lookup(EcosystemPackId id) =>
        ProductEcosystemPacks.Registry.Lookup(id);

    public static EcosystemScannerSelectionResult SelectScanner(EcosystemPackId id) =>
        ProductEcosystemPacks.Registry.SelectScanner(id);

    public static EcosystemDemoSelectionResult SelectDemo(string scenarioId) =>
        ProductEcosystemPacks.Registry.SelectDemo(scenarioId);
}
