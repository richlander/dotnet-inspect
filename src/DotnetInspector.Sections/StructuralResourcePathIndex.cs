namespace DotnetInspector.Sections;

/// <summary>
/// Resource Explanation's validated structural paths. Registration and
/// explanation share this index instead of rebuilding and revalidating it.
/// </summary>
internal sealed class StructuralResourcePathIndex
{
    private StructuralResourcePathIndex(
        ResourcePath catalogPath,
        Dictionary<DiscoveryResourceIdentity, ResourcePath> paths)
    {
        CatalogPath = catalogPath;
        Paths = paths;
    }

    public ResourcePath CatalogPath { get; }

    public IReadOnlyDictionary<DiscoveryResourceIdentity, ResourcePath> Paths { get; }

    public static StructuralResourcePathIndex Create(
        DiscoveryDocument document,
        IEnumerable<StructuralResourcePathRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var catalogPath = new ResourcePath(document.Catalog);
        string prefix = $"{catalogPath.Value}/";
        var paths = new Dictionary<DiscoveryResourceIdentity, ResourcePath>();
        var spellings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StructuralResourcePathRegistration registration in registrations)
        {
            if (!registration.Path.Value.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Structural path '{registration.Path}' is not under catalog root '{document.Catalog}'.",
                    nameof(registrations));
            }
            if (!paths.TryAdd(registration.Identity, registration.Path))
            {
                throw new ArgumentException(
                    $"Structural resource '{registration.Identity.Name}' has more than one path registration.",
                    nameof(registrations));
            }
            if (!spellings.Add(registration.Path.Value))
                throw new ArgumentException("Structural paths must be unique case-insensitively.", nameof(registrations));
        }
        foreach (DiscoveryResource resource in document.Resources)
        {
            if (!paths.ContainsKey(resource.Identity))
            {
                throw new ArgumentException(
                    $"Structural resource '{resource.Identity.Name}' has no path registration.",
                    nameof(registrations));
            }
        }
        if (paths.Count != document.Resources.Length)
        {
            throw new ArgumentException(
                $"Catalog '{document.Catalog}' registers a path with no Discovery resource.",
                nameof(registrations));
        }
        ValidateStructuralPaths(document, catalogPath, paths);
        return new(catalogPath, paths);
    }

    private static void ValidateStructuralPaths(
        DiscoveryDocument document,
        ResourcePath catalogPath,
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            ResourcePath> paths)
    {
        foreach (DiscoveryResource resource in document.Resources)
        {
            string expectedPrefix = resource.Identity.Kind switch
            {
                DiscoveryResourceKind.Category =>
                    $"{catalogPath.Value}/categories/",
                DiscoveryResourceKind.Section =>
                    $"{catalogPath.Value}/sections/",
                DiscoveryResourceKind.Item =>
                    ItemPrefix(resource.Identity, paths),
                _ => throw new InvalidOperationException(
                    "Unknown Discovery resource kind."),
            };
            if (!paths[resource.Identity].Value.StartsWith(
                    expectedPrefix,
                    StringComparison.Ordinal)
                || !ResourcePath.IsCanonicalSegment(
                    paths[resource.Identity].Value[
                        expectedPrefix.Length..]))
            {
                throw new ArgumentException(
                    $"Structural path '{paths[resource.Identity]}' does not "
                    + $"match {resource.Identity.Kind} identity "
                    + $"'{resource.Identity.Name}'.",
                    nameof(paths));
            }
        }
    }

    private static string ItemPrefix(
        DiscoveryResourceIdentity identity,
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            ResourcePath> paths)
    {
        var sectionIdentity =
            new DiscoveryResourceIdentity(
                DiscoveryResourceKind.Section,
                identity.Section!);
        if (!paths.TryGetValue(
                sectionIdentity,
                out ResourcePath? sectionPath))
        {
            throw new ArgumentException(
                $"Item '{identity.Name}' has no registered owning section.",
                nameof(paths));
        }
        if (!ResourcePath.IsCanonicalSegment(identity.ItemKind!))
        {
            throw new ArgumentException(
                $"Item kind '{identity.ItemKind}' is not a canonical path "
                + "segment.",
                nameof(paths));
        }

        return $"{sectionPath.Value}/items/{identity.ItemKind}/";
    }

}
