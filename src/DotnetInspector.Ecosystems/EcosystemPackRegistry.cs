using System.Collections.Immutable;
using DotnetInspector.Queries.Definitions;

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
    IReadOnlyList<EcosystemDemoRegistration> Demos);

internal sealed class EcosystemPackRegistry
{
    private sealed record DemoEntry(
        EcosystemDemoDescriptor Descriptor,
        ProductDemoSourceBinding Source);

    private readonly Dictionary<EcosystemPackId, EcosystemPackDescriptor> _packsById;
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
        _packsById = new Dictionary<EcosystemPackId, EcosystemPackDescriptor>();
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

            EcosystemDemoRegistration[] demos =
            [
                .. registration.Demos
                    ?? throw new ArgumentException(
                        $"Ecosystem pack '{registration.Id}' has no demo sequence.",
                        nameof(registrations)),
            ];
            if (registration.PackageSet is null && demos.Length == 0)
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
                descriptors.MoveToImmutable());
            _packsById.Add(packDescriptor.Id, packDescriptor);
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
        return _packsById.TryGetValue(id, out EcosystemPackDescriptor? descriptor)
            ? new EcosystemPackLookupResult.Known(descriptor)
            : new EcosystemPackLookupResult.Unknown(id);
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

    public static EcosystemDemoSelectionResult SelectDemo(string scenarioId) =>
        ProductEcosystemPacks.Registry.SelectDemo(scenarioId);
}
