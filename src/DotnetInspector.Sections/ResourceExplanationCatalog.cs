using System.Collections.Immutable;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;

namespace DotnetInspector.Sections;

public sealed class ResourceExplanationCatalog
{
    private readonly ImmutableArray<ResourceExplanationResource> _resources;
    private readonly ImmutableArray<ResourceExplanationRelationship>
        _relationships;
    private readonly IReadOnlyDictionary<
        ResourceExplanationIdentity,
        ResourceExplanationResource> _resourcesByIdentity;
    private readonly IReadOnlyDictionary<
        string,
        ResourceExplanationResource> _resourcesByPath;
    private readonly IReadOnlyDictionary<
        ResourceExplanationIdentity,
        ImmutableArray<ResourceExplanationRelationship>>
        _relationshipsBySource;

    private ResourceExplanationCatalog(
        ImmutableArray<ResourceExplanationResource> resources,
        ImmutableArray<ResourceExplanationRelationship> relationships,
        IReadOnlyDictionary<
            ResourceExplanationIdentity,
            ResourceExplanationResource> resourcesByIdentity,
        IReadOnlyDictionary<
            string,
            ResourceExplanationResource> resourcesByPath,
        IReadOnlyDictionary<
            ResourceExplanationIdentity,
            ImmutableArray<ResourceExplanationRelationship>>
            relationshipsBySource)
    {
        _resources = resources;
        _relationships = relationships;
        _resourcesByIdentity = resourcesByIdentity;
        _resourcesByPath = resourcesByPath;
        _relationshipsBySource = relationshipsBySource;
    }

    public ImmutableArray<ResourceExplanationResource> Resources =>
        _resources;

    public ImmutableArray<ResourceExplanationRelationship> Relationships =>
        _relationships;

    public static ResourceExplanationCatalog Create(
        IEnumerable<ResourceExplanationResource> resources,
        IEnumerable<ResourceExplanationRelationship> relationships)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(relationships);

        ImmutableArray<ResourceExplanationResource> resourceArray =
            [.. resources];
        ImmutableArray<ResourceExplanationRelationship> relationshipArray =
            [.. relationships];
        if (resourceArray.IsEmpty)
        {
            throw new ArgumentException(
                "An explanation catalog must register at least one resource.",
                nameof(resources));
        }
        var resourcesByIdentity =
            new Dictionary<
                ResourceExplanationIdentity,
                ResourceExplanationResource>();
        var resourcesByPath =
            new Dictionary<
                string,
                ResourceExplanationResource>(
                StringComparer.OrdinalIgnoreCase);
        foreach (ResourceExplanationResource resource in resourceArray)
        {
            if (!resourcesByIdentity.TryAdd(
                    resource.Identity,
                    resource))
            {
                throw new ArgumentException(
                    $"Duplicate explanation identity "
                    + $"'{resource.Identity}'.",
                    nameof(resources));
            }
            if (!resourcesByPath.TryAdd(
                    resource.Path.Value,
                    resource))
            {
                throw new ArgumentException(
                    $"Duplicate explanation path "
                    + $"'{resource.Path.Value}'.",
                    nameof(resources));
            }
        }

        var relationshipsBySource =
            new Dictionary<
                ResourceExplanationIdentity,
                List<ResourceExplanationRelationship>>();
        var relationshipsSeen =
            new HashSet<ResourceExplanationRelationship>();
        foreach (ResourceExplanationRelationship relationship
                 in relationshipArray)
        {
            if (!resourcesByIdentity.ContainsKey(relationship.Source))
            {
                throw new ArgumentException(
                    "Explanation relationships must start at a registered "
                    + "resource.",
                    nameof(relationships));
            }
            bool targetRegistered = resourcesByIdentity.TryGetValue(
                relationship.Target,
                out ResourceExplanationResource? registeredTarget);
            if (targetRegistered
                && relationship.TargetPath != registeredTarget!.Path)
            {
                throw new ArgumentException(
                    "A registered relationship target must carry its "
                    + "registered canonical path.",
                    nameof(relationships));
            }
            if (!targetRegistered
                && relationship.TargetPath is not null)
            {
                throw new ArgumentException(
                    "A non-navigable external relationship target must not "
                    + "carry a resource path.",
                    nameof(relationships));
            }
            if (!relationshipsSeen.Add(relationship))
            {
                throw new ArgumentException(
                    "Duplicate explanation relationship.",
                    nameof(relationships));
            }

            if (!relationshipsBySource.TryGetValue(
                    relationship.Source,
                    out List<ResourceExplanationRelationship>? outgoing))
            {
                outgoing = [];
                relationshipsBySource.Add(
                    relationship.Source,
                    outgoing);
            }
            outgoing.Add(relationship);
        }

        return new ResourceExplanationCatalog(
            resourceArray,
            relationshipArray,
            resourcesByIdentity,
            resourcesByPath,
            relationshipsBySource.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToImmutableArray()));
    }

    public static ResourceExplanationCatalog CreateStructural(
        DiscoveryDocument document,
        IEnumerable<StructuralResourcePathRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(registrations);

        ImmutableArray<StructuralResourcePathRegistration>
            registrationArray = [.. registrations];
        var authoritative =
            document.Resources.ToDictionary(
                static resource => resource.Identity);
        var paths =
            new Dictionary<DiscoveryResourceIdentity, ResourcePath>();
        var identitiesByPath =
            new Dictionary<string, DiscoveryResourceIdentity>(
                StringComparer.OrdinalIgnoreCase);
        foreach (StructuralResourcePathRegistration registration
                 in registrationArray)
        {
            if (!authoritative.ContainsKey(registration.Identity))
            {
                throw new ArgumentException(
                    "A structural path registration has no authoritative "
                    + "Discovery resource.",
                    nameof(registrations));
            }
            if (!paths.TryAdd(
                    registration.Identity,
                    registration.Path))
            {
                throw new ArgumentException(
                    "A structural identity has more than one path "
                    + "registration.",
                    nameof(registrations));
            }
            if (!identitiesByPath.TryAdd(
                    registration.Path.Value,
                    registration.Identity))
            {
                throw new ArgumentException(
                    "Structural path registrations must be unique "
                    + "case-insensitively.",
                    nameof(registrations));
            }
        }
        if (paths.Count != authoritative.Count)
        {
            throw new ArgumentException(
                "Every authoritative Discovery resource must have exactly "
                + "one structural path registration.",
                nameof(registrations));
        }

        ResourcePath catalogPath = new(document.Catalog);
        ValidateStructuralPaths(document, catalogPath, paths);

        var resources = new List<ResourceExplanationResource>();
        var relationships =
            new List<ResourceExplanationRelationship>();
        var catalogIdentity =
            new ResourceExplanationIdentity.Catalog(document.Catalog);
        ResourcePath categoriesPath = catalogPath.Append("categories");
        ResourcePath sectionsPath = catalogPath.Append("sections");
        var categoriesIdentity =
            new ResourceExplanationIdentity.NavigationCollection(
                catalogIdentity,
                parentIdentity: null,
                ResourceExplanationNavigationCollectionKind
                    .CatalogCategories);
        var sectionsIdentity =
            new ResourceExplanationIdentity.NavigationCollection(
                catalogIdentity,
                parentIdentity: null,
                ResourceExplanationNavigationCollectionKind
                    .CatalogSections);

        resources.Add(
            new ResourceExplanationResource(
                catalogPath,
                catalogIdentity,
                ResourceExplanationResourceKind.Catalog,
                new ResourceExplanationDetail.CatalogDetails(
                    DisplayName(document.Catalog),
                    document.CatalogEntries.Length)));
        AddCollection(
            resources,
            categoriesPath,
            categoriesIdentity,
            "Categories",
            document.Resources.Count(static resource =>
                resource.Identity.Kind == DiscoveryResourceKind.Category));
        AddCollection(
            resources,
            sectionsPath,
            sectionsIdentity,
            "Sections",
            document.Resources.Count(static resource =>
                resource.Identity.Kind == DiscoveryResourceKind.Section));

        var explanationIdentities =
            new Dictionary<
                DiscoveryResourceIdentity,
                ResourceExplanationIdentity.Structural>();
        foreach (DiscoveryResource resource in document.Resources)
        {
            var identity =
                new ResourceExplanationIdentity.Structural(
                    resource.Identity);
            explanationIdentities.Add(resource.Identity, identity);
            resources.Add(
                new ResourceExplanationResource(
                    paths[resource.Identity],
                    identity,
                    ResourceKind(resource.Identity.Kind),
                    Details(resource)));
        }

        var itemCollections =
            new Dictionary<
                DiscoveryResourceIdentity,
                ResourceExplanationIdentity.NavigationCollection>();
        var kindCollections =
            new Dictionary<
                (DiscoveryResourceIdentity Section, string Kind),
                ResourceExplanationIdentity.NavigationCollection>();
        var itemGroups =
            new Dictionary<
                DiscoveryResourceIdentity,
                ImmutableArray<
                    IGrouping<string?, DiscoveryResourceIdentity>>>();
        foreach (DiscoveryResource section in document.Resources.Where(
                     static resource =>
                         resource.Identity.Kind
                         == DiscoveryResourceKind.Section
                         && !resource.Members.IsEmpty))
        {
            ImmutableArray<
                IGrouping<string?, DiscoveryResourceIdentity>> groups =
                [
                    .. section.Members.GroupBy(
                        static member => member.ItemKind,
                        StringComparer.Ordinal),
                ];
            itemGroups.Add(section.Identity, groups);
            ResourcePath sectionPath = paths[section.Identity];
            ResourcePath itemsPath = sectionPath.Append("items");
            var itemsIdentity =
                new ResourceExplanationIdentity.NavigationCollection(
                    catalogIdentity,
                    explanationIdentities[section.Identity],
                    ResourceExplanationNavigationCollectionKind
                        .StructuralItems);
            itemCollections.Add(section.Identity, itemsIdentity);
            AddCollection(
                resources,
                itemsPath,
                itemsIdentity,
                $"{section.Identity.Name} items",
                groups.Length);

            foreach (IGrouping<string?, DiscoveryResourceIdentity> group
                     in groups)
            {
                string itemKind = group.Key
                    ?? throw new InvalidOperationException(
                        "A section member must have an item kind.");
                ResourcePath kindPath =
                    itemsPath.Append(itemKind);
                var kindIdentity =
                    new ResourceExplanationIdentity.NavigationCollection(
                        catalogIdentity,
                        itemsIdentity,
                        ResourceExplanationNavigationCollectionKind
                            .StructuralItemKind,
                        itemKind);
                kindCollections.Add(
                    (section.Identity, itemKind),
                    kindIdentity);
                AddCollection(
                    resources,
                    kindPath,
                    kindIdentity,
                    $"{section.Identity.Name} {itemKind} items",
                    group.Count());
            }
        }

        AddRelationship(
            relationships,
            catalogIdentity,
            ResourceExplanationRelationshipKind.Navigation,
            categoriesIdentity,
            categoriesPath);
        AddRelationship(
            relationships,
            catalogIdentity,
            ResourceExplanationRelationshipKind.Navigation,
            sectionsIdentity,
            sectionsPath);
        foreach (DiscoveryResourceIdentity entry in document.CatalogEntries)
        {
            AddRelationship(
                relationships,
                catalogIdentity,
                ResourceExplanationRelationshipKind.CatalogEntry,
                explanationIdentities[entry],
                paths[entry]);
        }

        foreach (DiscoveryResource resource in document.Resources)
        {
            ResourceExplanationIdentity.Structural identity =
                explanationIdentities[resource.Identity];
            switch (resource.Identity.Kind)
            {
                case DiscoveryResourceKind.Category:
                    AddRelationship(
                        relationships,
                        categoriesIdentity,
                        ResourceExplanationRelationshipKind.CollectionMember,
                        identity,
                        paths[resource.Identity]);
                    foreach (DiscoveryResourceIdentity member
                             in resource.Members)
                    {
                        AddRelationship(
                            relationships,
                            identity,
                            ResourceExplanationRelationshipKind.CategoryMember,
                            explanationIdentities[member],
                            paths[member]);
                    }
                    break;

                case DiscoveryResourceKind.Section:
                    AddRelationship(
                        relationships,
                        sectionsIdentity,
                        ResourceExplanationRelationshipKind.CollectionMember,
                        identity,
                        paths[resource.Identity]);
                    if (!resource.Members.IsEmpty)
                    {
                        ResourceExplanationIdentity.NavigationCollection
                            itemsIdentity =
                                itemCollections[resource.Identity];
                        ResourcePath itemsPath =
                            paths[resource.Identity].Append("items");
                        AddRelationship(
                            relationships,
                            identity,
                            ResourceExplanationRelationshipKind.Navigation,
                            itemsIdentity,
                            itemsPath);
                        foreach (DiscoveryResourceIdentity member
                                 in resource.Members)
                        {
                            AddRelationship(
                                relationships,
                                identity,
                                ResourceExplanationRelationshipKind.StructuralItem,
                                explanationIdentities[member],
                                paths[member]);
                        }
                        foreach (IGrouping<
                                     string?,
                                     DiscoveryResourceIdentity> group
                                 in itemGroups[resource.Identity])
                        {
                            string itemKind = group.Key
                                ?? throw new InvalidOperationException(
                                    "A section member must have an item kind.");
                            var key = (resource.Identity, itemKind);
                            ResourceExplanationIdentity.NavigationCollection
                                kindIdentity = kindCollections[key];
                            ResourcePath kindPath =
                                paths[resource.Identity]
                                    .Append("items", itemKind);
                            AddRelationship(
                                relationships,
                                itemsIdentity,
                                ResourceExplanationRelationshipKind.CollectionMember,
                                kindIdentity,
                                kindPath);
                            foreach (DiscoveryResourceIdentity member
                                     in group)
                            {
                                AddRelationship(
                                    relationships,
                                    kindIdentity,
                                    ResourceExplanationRelationshipKind.CollectionMember,
                                    explanationIdentities[member],
                                    paths[member]);
                            }
                        }
                    }
                    break;

                case DiscoveryResourceKind.Item:
                    break;

                default:
                    throw new InvalidOperationException(
                        "Unknown Discovery resource kind.");
            }
        }

        return Create(resources, relationships);
    }

    public static ResourceExplanationCatalog CreateCapabilities(
        InspectionCapabilityCatalog catalog,
        IEnumerable<InspectionCapabilityResourcePathRegistration>
            registrations)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(registrations);

        ImmutableArray<InspectionCapabilityResourcePathRegistration>
            registrationArray = [.. registrations];
        IReadOnlyDictionary<
            InspectionCapabilityResourceIdentity,
            ResourcePath> paths =
                IndexCapabilityPaths(registrationArray);
        var resources = new List<ResourceExplanationResource>();
        var relationships =
            new List<ResourceExplanationRelationship>();
        var identities =
            new Dictionary<
                InspectionCapabilityResourceIdentity,
                ResourceExplanationIdentity.Capability>();

        foreach (InspectionDocumentRegistration document
                 in catalog.Documents)
        {
            InspectionCapabilityResourceIdentity resourceIdentity =
                DocumentIdentity(document);
            AddCapabilityResource(
                resources,
                identities,
                paths,
                resourceIdentity,
                ResourceExplanationResourceKind.InspectionDocument,
                new ResourceExplanationDetail.InspectionDocumentDetails(
                    document.Descriptor.Identity,
                    document.Descriptor.Name,
                    document.Descriptor.Summary,
                    document.Descriptor.ResultContract));
        }

        foreach (InspectionRouteRegistration route in catalog.Routes)
        {
            QuerySpaceOperationScopeDescriptor operation =
                route.QuerySpace.Descriptor.Operation;
            InspectionCapabilityResourceIdentity resourceIdentity =
                RouteIdentity(route);
            AddCapabilityResource(
                resources,
                identities,
                paths,
                resourceIdentity,
                ResourceExplanationResourceKind.HostNeutralRoute,
                new ResourceExplanationDetail.HostNeutralRouteDetails(
                    route.Descriptor.Identity,
                    route.Descriptor.Name,
                    route.Descriptor.Summary,
                    operation.SubjectRole,
                    operation.ResultGrain,
                    operation.Profile,
                    route.Document.Descriptor.ResultContract));
        }

        QuerySpaceBinding[] querySpaces =
        [
            .. catalog.Routes
                .Select(static route => route.QuerySpace)
                .DistinctBy(
                    static querySpace =>
                        querySpace.Descriptor.Identity,
                    StringComparer.Ordinal),
        ];
        foreach (QuerySpaceBinding querySpace in querySpaces)
        {
            InspectionCapabilityResourceIdentity resourceIdentity =
                QuerySpaceIdentity(querySpace);
            AddCapabilityResource(
                resources,
                identities,
                paths,
                resourceIdentity,
                ResourceExplanationResourceKind.QuerySpace,
                new ResourceExplanationDetail.QuerySpaceDetails(
                    querySpace.Descriptor.Identity,
                    querySpace.Descriptor.Operation.Operation,
                    "The effective executable Query Space for this route.",
                    querySpace.Descriptor.Operation.Terms.Count));

            foreach (QuerySpaceOperationTermDescriptor term
                     in querySpace.Descriptor.Operation.Terms)
            {
                InspectionCapabilityResourceIdentity termIdentity =
                    QueryFacetIdentity(querySpace, term);
                AddCapabilityResource(
                    resources,
                    identities,
                    paths,
                    termIdentity,
                    ResourceExplanationResourceKind.QueryFacet,
                    new ResourceExplanationDetail.QueryFacetDetails(
                        term.Identity,
                        term.Key,
                        term.Label,
                        term.Summary,
                        term.Operators.Select(
                            PortableQueryModel.TextOf),
                        term.ValueKind,
                        term.Values,
                        term.Effects.Select(static effect =>
                            $"{effect.Kind}: {effect.Identity}")));
            }
        }

        foreach (InspectionConsumerBinding binding in catalog.Bindings)
        {
            InspectionCapabilityResourceIdentity resourceIdentity =
                ConsumerBindingIdentity(binding);
            AddCapabilityResource(
                resources,
                identities,
                paths,
                resourceIdentity,
                ResourceExplanationResourceKind.ConsumerBinding,
                new ResourceExplanationDetail.ConsumerBindingDetails(
                    binding.Descriptor.Identity,
                    binding.Descriptor.Owner,
                    $"A production {binding.Descriptor.Kind} binding for "
                    + $"'{binding.Route.Descriptor.Name}'.",
                    binding.Descriptor.Kind,
                    binding.Descriptor.Gesture,
                    binding.ExposedQueryTerms.Length));
        }

        int expectedResourceCount =
            catalog.Documents.Length
            + catalog.Routes.Length
            + querySpaces.Length
            + querySpaces.Sum(static querySpace =>
                querySpace.Descriptor.Operation.Terms.Count)
            + catalog.Bindings.Length;
        if (paths.Count != expectedResourceCount)
        {
            throw new ArgumentException(
                "Every composed inspection capability resource must have "
                + "exactly one canonical Resource Explanation path.",
                nameof(registrations));
        }

        foreach (InspectionRouteRegistration route in catalog.Routes)
        {
            ResourceExplanationIdentity.Capability routeIdentity =
                identities[RouteIdentity(route)];
            ResourceExplanationIdentity.Capability documentIdentity =
                identities[DocumentIdentity(route.Document)];
            ResourceExplanationIdentity.Capability querySpaceIdentity =
                identities[QuerySpaceIdentity(route.QuerySpace)];
            AddRelationship(
                relationships,
                routeIdentity,
                ResourceExplanationRelationshipKind.Produces,
                documentIdentity,
                paths[DocumentIdentity(route.Document)]);
            AddRelationship(
                relationships,
                routeIdentity,
                ResourceExplanationRelationshipKind.QuerySurface,
                querySpaceIdentity,
                paths[QuerySpaceIdentity(route.QuerySpace)]);
            AddRelationship(
                relationships,
                documentIdentity,
                ResourceExplanationRelationshipKind.Route,
                routeIdentity,
                paths[RouteIdentity(route)]);

            foreach (QuerySpaceOperationTermDescriptor term
                     in route.QuerySpace.Descriptor.Operation.Terms)
            {
                ResourceExplanationIdentity.Capability facetIdentity =
                    identities[QueryFacetIdentity(
                        route.QuerySpace,
                        term)];
                AddRelationship(
                    relationships,
                    facetIdentity,
                    ResourceExplanationRelationshipKind.Route,
                    routeIdentity,
                    paths[RouteIdentity(route)]);
            }
        }

        foreach (QuerySpaceBinding querySpace in querySpaces)
        {
            ResourceExplanationIdentity.Capability querySpaceIdentity =
                identities[QuerySpaceIdentity(querySpace)];
            foreach (QuerySpaceOperationTermDescriptor term
                     in querySpace.Descriptor.Operation.Terms)
            {
                ResourceExplanationIdentity.Capability facetIdentity =
                    identities[QueryFacetIdentity(querySpace, term)];
                AddRelationship(
                    relationships,
                    querySpaceIdentity,
                    ResourceExplanationRelationshipKind.QueryFacet,
                    facetIdentity,
                    paths[QueryFacetIdentity(querySpace, term)]);
            }

            IEnumerable<InspectionQueryTermRelationship> termRelationships =
                catalog.Routes
                    .Where(route =>
                        ReferenceEquals(route.QuerySpace, querySpace))
                    .SelectMany(static route =>
                        route.QueryTermRelationships)
                    .Distinct();
            foreach (InspectionQueryTermRelationship termRelationship
                     in termRelationships)
            {
                QuerySpaceOperationTermDescriptor source =
                    querySpace.Descriptor.Operation.Terms.Single(
                        term => term.Identity
                            == termRelationship.SourceTerm);
                QuerySpaceOperationTermDescriptor target =
                    querySpace.Descriptor.Operation.Terms.Single(
                        term => term.Identity
                            == termRelationship.TargetTerm);
                AddRelationship(
                    relationships,
                    identities[QueryFacetIdentity(querySpace, source)],
                    termRelationship.Kind switch
                    {
                        InspectionQueryTermRelationshipKind
                            .RequiredContext =>
                            ResourceExplanationRelationshipKind
                                .RequiredContext,
                        _ => throw new InvalidOperationException(
                            "Unknown query-term relationship kind."),
                    },
                    identities[QueryFacetIdentity(querySpace, target)],
                    paths[QueryFacetIdentity(querySpace, target)]);
            }
        }

        foreach (InspectionConsumerBinding binding in catalog.Bindings)
        {
            ResourceExplanationIdentity.Capability bindingIdentity =
                identities[ConsumerBindingIdentity(binding)];
            ResourceExplanationIdentity.Capability routeIdentity =
                identities[RouteIdentity(binding.Route)];
            AddRelationship(
                relationships,
                bindingIdentity,
                ResourceExplanationRelationshipKind.Invokes,
                routeIdentity,
                paths[RouteIdentity(binding.Route)]);
            AddRelationship(
                relationships,
                routeIdentity,
                ResourceExplanationRelationshipKind.ConsumerBinding,
                bindingIdentity,
                paths[ConsumerBindingIdentity(binding)]);
            foreach (string exposedTerm in binding.ExposedQueryTerms)
            {
                QuerySpaceOperationTermDescriptor term =
                    binding.Route.QuerySpace.Descriptor.Operation.Terms
                        .Single(term =>
                            term.Identity == exposedTerm);
                ResourceExplanationIdentity.Capability facetIdentity =
                    identities[QueryFacetIdentity(
                        binding.Route.QuerySpace,
                        term)];
                ResourcePath facetPath =
                    paths[QueryFacetIdentity(
                        binding.Route.QuerySpace,
                        term)];
                AddRelationship(
                    relationships,
                    bindingIdentity,
                    ResourceExplanationRelationshipKind.Exposes,
                    facetIdentity,
                    facetPath);
                AddRelationship(
                    relationships,
                    facetIdentity,
                    ResourceExplanationRelationshipKind.ExposedBy,
                    bindingIdentity,
                    paths[ConsumerBindingIdentity(binding)]);
            }
        }

        return Create(resources, relationships);
    }

    public static ResourceExplanationCatalog Combine(
        params ResourceExplanationCatalog[] catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        if (catalogs.Length == 0)
        {
            throw new ArgumentException(
                "At least one Resource Explanation catalog is required.",
                nameof(catalogs));
        }
        return Create(
            catalogs.SelectMany(static catalog => catalog.Resources),
            catalogs.SelectMany(static catalog => catalog.Relationships));
    }

    public ResourcePathResolution Resolve(string requestedPath)
    {
        if (!ResourcePath.TryCreate(
                requestedPath,
                out ResourcePath? path,
                out string? error))
        {
            return new ResourcePathResolution.Invalid(
                requestedPath,
                error!);
        }

        if (_resourcesByPath.TryGetValue(
                path!.Value,
                out ResourceExplanationResource? resource))
        {
            return new ResourcePathResolution.Resolved(
                resource.Path,
                resource.Identity);
        }

        ImmutableArray<ResourcePath> suggestions =
        [
            .. _resources
                .OrderBy(resource =>
                    EditDistance(
                        path.Value,
                        resource.Path.Value))
                .ThenBy(
                    resource => resource.Path.Value,
                    StringComparer.Ordinal)
                .Take(5)
                .Select(static resource => resource.Path),
        ];
        return new ResourcePathResolution.Unknown(
            requestedPath,
            suggestions);
    }

    private static IReadOnlyDictionary<
        InspectionCapabilityResourceIdentity,
        ResourcePath> IndexCapabilityPaths(
            IEnumerable<InspectionCapabilityResourcePathRegistration>
                registrations)
    {
        var paths =
            new Dictionary<
                InspectionCapabilityResourceIdentity,
                ResourcePath>();
        var identitiesByPath =
            new Dictionary<
                string,
                InspectionCapabilityResourceIdentity>(
                StringComparer.OrdinalIgnoreCase);
        foreach (InspectionCapabilityResourcePathRegistration registration
                 in registrations)
        {
            if (!paths.TryAdd(
                    registration.Identity,
                    registration.Path))
            {
                throw new ArgumentException(
                    "An inspection capability resource has more than one "
                    + "path registration.",
                    nameof(registrations));
            }
            if (!identitiesByPath.TryAdd(
                    registration.Path.Value,
                    registration.Identity))
            {
                throw new ArgumentException(
                    "Inspection capability paths must be unique "
                    + "case-insensitively.",
                    nameof(registrations));
            }
        }
        return paths;
    }

    private static void AddCapabilityResource(
        ICollection<ResourceExplanationResource> resources,
        IDictionary<
            InspectionCapabilityResourceIdentity,
            ResourceExplanationIdentity.Capability> identities,
        IReadOnlyDictionary<
            InspectionCapabilityResourceIdentity,
            ResourcePath> paths,
        InspectionCapabilityResourceIdentity resourceIdentity,
        ResourceExplanationResourceKind resourceKind,
        ResourceExplanationDetail details)
    {
        if (!paths.TryGetValue(
                resourceIdentity,
                out ResourcePath? path))
        {
            throw new ArgumentException(
                $"Inspection capability resource "
                + $"'{resourceIdentity.Identity}' has no canonical path.",
                nameof(paths));
        }
        var identity =
            new ResourceExplanationIdentity.Capability(
                resourceIdentity);
        identities.Add(resourceIdentity, identity);
        resources.Add(
            new(
                path,
                identity,
                resourceKind,
                details));
    }

    internal static InspectionCapabilityResourceIdentity DocumentIdentity(
        InspectionDocumentRegistration document) =>
        new(
            InspectionCapabilityResourceKind.Document,
            document.Descriptor.Identity);

    internal static InspectionCapabilityResourceIdentity RouteIdentity(
        InspectionRouteRegistration route) =>
        new(
            InspectionCapabilityResourceKind.Route,
            route.Descriptor.Identity);

    internal static InspectionCapabilityResourceIdentity QuerySpaceIdentity(
        QuerySpaceBinding querySpace) =>
        new(
            InspectionCapabilityResourceKind.QuerySpace,
            querySpace.Descriptor.Identity);

    internal static InspectionCapabilityResourceIdentity QueryFacetIdentity(
        QuerySpaceBinding querySpace,
        QuerySpaceOperationTermDescriptor term) =>
        new(
            InspectionCapabilityResourceKind.QueryFacet,
            term.Identity,
            querySpace.Descriptor.Identity);

    internal static InspectionCapabilityResourceIdentity
        ConsumerBindingIdentity(
            InspectionConsumerBinding binding) =>
        new(
            InspectionCapabilityResourceKind.ConsumerBinding,
            binding.Descriptor.Identity);

    public InspectionEnvelope<ResourceExplanationDocument> Explain(
        ResourcePathResolution.Resolved resolved,
        ResourceExplanationRequest request)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(request);
        if (!_resourcesByPath.TryGetValue(
                resolved.Path.Value,
                out ResourceExplanationResource? root)
            || root.Identity != resolved.Identity)
        {
            throw new ArgumentException(
                "The resolved path does not belong to this catalog.",
                nameof(resolved));
        }

        var resources = new List<ResourceExplanationResource> { root };
        var relationships =
            new List<ResourceExplanationRelationship>();
        var visited =
            new HashSet<ResourceExplanationIdentity> { root.Identity };
        var queue =
            new Queue<(ResourceExplanationResource Resource, int Depth)>();
        queue.Enqueue((root, 0));
        var truncationReasons =
            new HashSet<ResourceExplanationTruncationReason>();
        int completedDepth = 0;
        bool relationshipLimitReached = false;

        while (queue.Count > 0 && !relationshipLimitReached)
        {
            (ResourceExplanationResource current, int depth) =
                queue.Dequeue();
            if (!_relationshipsBySource.TryGetValue(
                    current.Identity,
                    out ImmutableArray<ResourceExplanationRelationship>
                        outgoing))
            {
                continue;
            }

            foreach (ResourceExplanationRelationship relationship
                     in outgoing)
            {
                if (relationships.Count >= request.RelationshipLimit)
                {
                    truncationReasons.Add(
                        ResourceExplanationTruncationReason
                            .RelationshipLimit);
                    relationshipLimitReached = true;
                    break;
                }

                relationships.Add(relationship);
                if (!_resourcesByIdentity.TryGetValue(
                        relationship.Target,
                        out ResourceExplanationResource? target)
                    || visited.Contains(target.Identity))
                {
                    continue;
                }

                if (depth >= request.Depth)
                {
                    truncationReasons.Add(
                        ResourceExplanationTruncationReason.Depth);
                    continue;
                }
                if (resources.Count >= request.ResourceLimit)
                {
                    truncationReasons.Add(
                        ResourceExplanationTruncationReason.ResourceLimit);
                    continue;
                }

                visited.Add(target.Identity);
                resources.Add(target);
                int targetDepth = depth + 1;
                completedDepth = Math.Max(
                    completedDepth,
                    targetDepth);
                queue.Enqueue((target, targetDepth));
            }
        }

        var receipt =
            new ResourceExplanationTraversalReceipt(
                request.Depth,
                request.ResourceLimit,
                request.RelationshipLimit,
                completedDepth,
                resources.Count,
                relationships.Count,
                truncationReasons.Count == 0
                    ? ResourceExplanationCompleteness.Complete
                    : ResourceExplanationCompleteness.Truncated,
                truncationReasons.Order());
        var document =
            new ResourceExplanationDocument(
                root.Path,
                root.Identity,
                resources,
                relationships,
                receipt);
        return new InspectionEnvelope<ResourceExplanationDocument>(
            document,
            new InspectionShare.NonProjectable(
                root.Path.Value,
                "Resource Explanation does not yet have a portable "
                + "Workspace projection."));
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

    private static ResourceExplanationResourceKind ResourceKind(
        DiscoveryResourceKind kind) =>
        kind switch
        {
            DiscoveryResourceKind.Category =>
                ResourceExplanationResourceKind.StructuralCategory,
            DiscoveryResourceKind.Section =>
                ResourceExplanationResourceKind.StructuralSection,
            DiscoveryResourceKind.Item =>
                ResourceExplanationResourceKind.StructuralItem,
            _ => throw new InvalidOperationException(
                "Unknown Discovery resource kind."),
        };

    private static ResourceExplanationDetail Details(
        DiscoveryResource resource) =>
        resource.Identity.Kind switch
        {
            DiscoveryResourceKind.Category =>
                new ResourceExplanationDetail.StructuralCategoryDetails(
                    resource.Identity.Name,
                    resource.OutputModes,
                    resource.Members.Length),
            DiscoveryResourceKind.Section =>
                new ResourceExplanationDetail.StructuralSectionDetails(
                    resource.Identity.Name,
                    resource.OutputModes,
                    resource.Members.Length),
            DiscoveryResourceKind.Item =>
                new ResourceExplanationDetail.StructuralItemDetails(
                    resource.Identity.Name,
                    resource.Identity.ItemKind!),
            _ => throw new InvalidOperationException(
                "Unknown Discovery resource kind."),
        };

    private static void AddCollection(
        ICollection<ResourceExplanationResource> resources,
        ResourcePath path,
        ResourceExplanationIdentity.NavigationCollection identity,
        string name,
        int memberCount) =>
        resources.Add(
            new ResourceExplanationResource(
                path,
                identity,
                ResourceExplanationResourceKind.NavigationCollection,
                new ResourceExplanationDetail.NavigationCollectionDetails(
                    name,
                    memberCount)));

    private static void AddRelationship(
        ICollection<ResourceExplanationRelationship> relationships,
        ResourceExplanationIdentity source,
        ResourceExplanationRelationshipKind kind,
        ResourceExplanationIdentity target,
        ResourcePath targetPath) =>
        relationships.Add(
            new ResourceExplanationRelationship(
                source,
                kind,
                target,
                targetPath));

    private static string DisplayName(string catalog) =>
        char.ToUpperInvariant(catalog[0]) + catalog[1..];

    private static int EditDistance(string left, string right)
    {
        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];
        for (int column = 0; column <= right.Length; column++)
            previous[column] = column;

        for (int row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (int column = 1; column <= right.Length; column++)
            {
                int substitution =
                    left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(
                        current[column - 1] + 1,
                        previous[column] + 1),
                    previous[column - 1] + substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
