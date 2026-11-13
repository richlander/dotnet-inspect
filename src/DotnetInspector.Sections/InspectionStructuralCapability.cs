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
    internal StructuralResourcePathIndex ResourcePaths { get; }

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
        ResourcePaths = StructuralResourcePathIndex.Create(Document, projection.ResourcePaths);
    }

    public string Identity { get; }

    public string Name { get; }

    public StructuralDiscoveryProjection Projection { get; }

    public DiscoveryDocument Document => Projection.Document;

    public ResourcePath PathOf(DiscoveryResourceIdentity resource) => ResourcePaths.Paths[resource];

    internal bool ContainsSelectableResource(DiscoveryResourceIdentity resource) =>
        resource.Kind is DiscoveryResourceKind.Section or DiscoveryResourceKind.Category
        && ResourcePaths.Paths.ContainsKey(resource);
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
