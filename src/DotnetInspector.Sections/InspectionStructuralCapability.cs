using System.Collections.Frozen;
using System.Collections.Immutable;

namespace DotnetInspector.Sections;

/// <summary>Host-neutral registration of owner-issued structural declarations.</summary>
public static class StructuralCatalogCapability
{
    public static InspectionCapabilityModule CreateModule(StructuralDiscoveryProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        string root = projection.Document.Catalog;
        return new InspectionCapabilityModule(
            $"dotnet-inspect/{root}-structural",
            structuralCatalogs:
            [
                // Preserve the installed identity issued before graph adoption.
                // Its historical namespace does not declare a consumer or route.
                new InspectionStructuralCatalogRegistration(
                    $"dotnet-inspect.cli/{root}-catalog", root, projection),
            ]);
    }
}

/// <summary>
/// One owner-issued structural catalog. Registration is independent of a host
/// binding and does not imply a host-neutral execution route.
/// </summary>
public sealed class InspectionStructuralCatalogRegistration
{
    private readonly FrozenDictionary<DiscoveryResourceIdentity, ResourcePath> _paths;

    public InspectionStructuralCatalogRegistration(
        string identity,
        string name,
        StructuralDiscoveryProjection projection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(projection);
        Identity = identity;
        Name = name;
        Projection = projection;
        var paths = new Dictionary<DiscoveryResourceIdentity, ResourcePath>();
        var spellings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string root = Document.Catalog;
        _ = new ResourcePath(root);
        foreach (StructuralResourcePathRegistration registration in projection.ResourcePaths)
        {
            if (!registration.Path.Value.StartsWith($"{root}/", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Structural path '{registration.Path}' is not under catalog root '{root}'.",
                    nameof(projection));
            }
            if (!paths.TryAdd(registration.Identity, registration.Path))
            {
                throw new ArgumentException(
                    $"Structural resource '{registration.Identity.Name}' has more than one path registration.",
                    nameof(projection));
            }
            if (!spellings.Add(registration.Path.Value))
                throw new ArgumentException("Structural paths must be unique.", nameof(projection));
        }
        foreach (DiscoveryResource resource in Document.Resources)
        {
            if (!paths.ContainsKey(resource.Identity))
            {
                throw new ArgumentException(
                    $"Structural resource '{resource.Identity.Name}' has no path registration.",
                    nameof(projection));
            }
        }
        if (paths.Count != Document.Resources.Length)
        {
            throw new ArgumentException(
                $"Catalog '{root}' registers a path with no Discovery resource.",
                nameof(projection));
        }
        _paths = paths.ToFrozenDictionary();
    }

    public string Identity { get; }

    public string Name { get; }

    public StructuralDiscoveryProjection Projection { get; }

    public DiscoveryDocument Document => Projection.Document;

    public ResourcePath PathOf(DiscoveryResourceIdentity resource) => _paths[resource];

    internal bool ContainsSelectableResource(DiscoveryResourceIdentity resource) =>
        resource.Kind is DiscoveryResourceKind.Section or DiscoveryResourceKind.Category
        && _paths.ContainsKey(resource);
}

/// <summary>One host gesture selecting resources of an exact registered catalog.</summary>
public sealed class InspectionStructuralViewBinding
{
    public InspectionStructuralViewBinding(
        InspectionConsumerDescriptor descriptor,
        InspectionStructuralCatalogRegistration catalog,
        IEnumerable<DiscoveryResourceIdentity> resources)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        ArgumentNullException.ThrowIfNull(resources);
        Resources = [.. resources];
    }

    public InspectionConsumerDescriptor Descriptor { get; }

    public InspectionStructuralCatalogRegistration Catalog { get; }

    public ImmutableHashSet<DiscoveryResourceIdentity> Resources { get; }
}
