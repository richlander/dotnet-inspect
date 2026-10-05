using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using DotnetInspector.Queries;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Explanation;
using QuerySpace.Operations;

namespace DotnetInspector.Sections;

public sealed class ResourceExplanationCatalog
{
    private readonly ImmutableArray<ExplanationSchema> _schemas;
    private readonly ImmutableArray<CatalogResource> _catalogResources;
    private readonly ImmutableArray<ResourceExplanationResource> _resources;
    private readonly ImmutableArray<ResourceExplanationRelationship>
        _relationships;
    private readonly IReadOnlyDictionary<
        ExplanationResourceKey,
        CatalogResource> _resourcesByKey;
    private readonly IReadOnlyDictionary<string, CatalogResource>
        _resourcesByPath;

    private ResourceExplanationCatalog(
        ImmutableArray<ExplanationSchema> schemas,
        ImmutableArray<CatalogResource> catalogResources,
        ImmutableArray<ResourceExplanationResource> resources,
        ImmutableArray<ResourceExplanationRelationship> relationships,
        IReadOnlyDictionary<
            ExplanationResourceKey,
            CatalogResource> resourcesByKey,
        IReadOnlyDictionary<string, CatalogResource> resourcesByPath)
    {
        _schemas = schemas;
        _catalogResources = catalogResources;
        _resources = resources;
        _relationships = relationships;
        _resourcesByKey = resourcesByKey;
        _resourcesByPath = resourcesByPath;
    }

    public ImmutableArray<ExplanationSchema> Schemas => _schemas;

    public ImmutableArray<ResourceExplanationResource> Resources =>
        _resources;

    public ImmutableArray<ResourceExplanationRelationship> Relationships =>
        _relationships;

    public static ResourceExplanationCatalog Create(
        IEnumerable<ExplanationSchema> schemas,
        IEnumerable<ExplanationResourceSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(snapshots);
        ImmutableArray<ExplanationSchema> schemaArray = [.. schemas];
        ImmutableArray<ExplanationResourceSnapshot> snapshotArray =
            [.. snapshots];
        if (schemaArray.IsEmpty)
        {
            throw new ArgumentException(
                "An explanation catalog requires a schema closure.",
                nameof(schemas));
        }
        if (snapshotArray.IsEmpty)
        {
            throw new ArgumentException(
                "An explanation catalog must register at least one snapshot.",
                nameof(snapshots));
        }

        var resourcesByKey =
            new Dictionary<ExplanationResourceKey, CatalogResource>();
        var resourcesByPath =
            new Dictionary<string, CatalogResource>(
                StringComparer.OrdinalIgnoreCase);
        var catalogResources =
            ImmutableArray.CreateBuilder<CatalogResource>(
                snapshotArray.Length);
        foreach (ExplanationResourceSnapshot snapshot in snapshotArray)
        {
            ExplanationConformance.ValidateSnapshot(schemaArray, snapshot);
            ResourcePath path = Path(snapshot);
            var resource = new CatalogResource(path, snapshot);
            if (!resourcesByKey.TryAdd(snapshot.Key, resource))
            {
                throw new ArgumentException(
                    $"Duplicate explanation resource key '{snapshot.Key}'.",
                    nameof(snapshots));
            }
            if (!resourcesByPath.TryAdd(path.Value, resource))
            {
                throw new ArgumentException(
                    $"Duplicate explanation path '{path.Value}'.",
                    nameof(snapshots));
            }
            catalogResources.Add(resource);
        }

        foreach (CatalogResource resource in catalogResources)
        {
            foreach (ExplanationRelationshipObservation observation
                     in resource.Snapshot.Relationships)
            {
                if (observation.Targets
                    .Select(static target => target.Resource)
                    .Distinct()
                    .Count()
                    != observation.Targets.Length)
                {
                    throw new ArgumentException(
                        $"Resource '{resource.Snapshot.Key}' relationship "
                        + $"'{observation.Relationship}' repeats a target.",
                        nameof(snapshots));
                }
            }
        }

        ImmutableArray<ResourceExplanationResource> projections =
        [
            .. catalogResources.Select(static resource =>
                resource.Projection),
        ];
        ImmutableArray<ResourceExplanationRelationship> relationships =
        [
            .. catalogResources.SelectMany(resource =>
                ProjectRelationships(
                    resource,
                    resourcesByKey,
                    targetLimit: int.MaxValue).Relationships),
        ];
        return new(
            schemaArray,
            catalogResources.MoveToImmutable(),
            projections,
            relationships,
            resourcesByKey,
            resourcesByPath);
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
        ExplanationResourceKey catalogKey =
            ResourceExplanationVocabulary.Key(
                ResourceExplanationVocabulary.CatalogType,
                Pack(document.Catalog));
        ExplanationResourceKey categoriesKey =
            NavigationKey(document.Catalog, "catalog-categories");
        ExplanationResourceKey sectionsKey =
            NavigationKey(document.Catalog, "catalog-sections");
        ResourcePath categoriesPath = catalogPath.Append("categories");
        ResourcePath sectionsPath = catalogPath.Append("sections");

        var keys =
            document.Resources.ToDictionary(
                static resource => resource.Identity,
                resource => ResourceExplanationVocabulary.Key(
                    StructuralType(resource.Identity.Kind),
                    StructuralIdentity(document.Catalog, resource.Identity)));
        var itemCollectionKeys =
            new Dictionary<
                DiscoveryResourceIdentity,
                ExplanationResourceKey>();
        var kindCollectionKeys =
            new Dictionary<
                (DiscoveryResourceIdentity Section, string Kind),
                ExplanationResourceKey>();
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
            itemCollectionKeys.Add(
                section.Identity,
                NavigationKey(
                    document.Catalog,
                    "structural-items",
                    StructuralIdentity(document.Catalog, section.Identity)));
            foreach (IGrouping<string?, DiscoveryResourceIdentity> group
                     in groups)
            {
                string itemKind = group.Key
                    ?? throw new InvalidOperationException(
                        "A section member must have an item kind.");
                kindCollectionKeys.Add(
                    (section.Identity, itemKind),
                    NavigationKey(
                        document.Catalog,
                        "structural-item-kind",
                        StructuralIdentity(
                            document.Catalog,
                            section.Identity),
                        itemKind));
            }
        }

        var snapshots = new List<ExplanationResourceSnapshot>();
        snapshots.Add(
            Snapshot(
                catalogPath,
                catalogKey,
                [
                    ResourceExplanationVocabulary.TextFact(
                        ResourceExplanationVocabulary.CatalogType,
                        "name",
                        DisplayName(document.Catalog)),
                    ResourceExplanationVocabulary.IntegerFact(
                        ResourceExplanationVocabulary.CatalogType,
                        "members",
                        document.CatalogEntries.Length),
                ],
                RelationshipObservations(
                    ResourceExplanationVocabulary.CatalogType,
                    new Dictionary<string, IEnumerable<ExplanationResourceKey>>
                    {
                        ["navigation"] = [categoriesKey, sectionsKey],
                        ["catalog-category-entry"] = document.CatalogEntries
                            .Where(static identity =>
                                identity.Kind
                                    == DiscoveryResourceKind.Category)
                            .Select(identity => keys[identity]),
                        ["catalog-section-entry"] = document.CatalogEntries
                            .Where(static identity =>
                                identity.Kind
                                    == DiscoveryResourceKind.Section)
                            .Select(identity => keys[identity]),
                    })));
        snapshots.Add(
            NavigationSnapshot(
                categoriesPath,
                categoriesKey,
                "Categories",
                document.Resources.Count(static resource =>
                    resource.Identity.Kind
                        == DiscoveryResourceKind.Category),
                new Dictionary<string, IEnumerable<ExplanationResourceKey>>
                {
                    ["collection-category"] = document.Resources
                        .Where(static resource =>
                            resource.Identity.Kind
                                == DiscoveryResourceKind.Category)
                        .Select(resource => keys[resource.Identity]),
                }));
        snapshots.Add(
            NavigationSnapshot(
                sectionsPath,
                sectionsKey,
                "Sections",
                document.Resources.Count(static resource =>
                    resource.Identity.Kind
                        == DiscoveryResourceKind.Section),
                new Dictionary<string, IEnumerable<ExplanationResourceKey>>
                {
                    ["collection-section"] = document.Resources
                        .Where(static resource =>
                            resource.Identity.Kind
                                == DiscoveryResourceKind.Section)
                        .Select(resource => keys[resource.Identity]),
                }));

        foreach (DiscoveryResource resource in document.Resources)
        {
            ExplanationResourceTypeIdentity type =
                StructuralType(resource.Identity.Kind);
            snapshots.Add(
                Snapshot(
                    paths[resource.Identity],
                    keys[resource.Identity],
                    StructuralFacts(resource, type),
                    StructuralRelationships(
                        resource,
                        type,
                        keys,
                        itemCollectionKeys)));
        }

        foreach (DiscoveryResource section in document.Resources.Where(
                     resource => itemCollectionKeys.ContainsKey(
                         resource.Identity)))
        {
            ResourcePath itemsPath =
                paths[section.Identity].Append("items");
            ImmutableArray<
                IGrouping<string?, DiscoveryResourceIdentity>> groups =
                    itemGroups[section.Identity];
            snapshots.Add(
                NavigationSnapshot(
                    itemsPath,
                    itemCollectionKeys[section.Identity],
                    $"{section.Identity.Name} items",
                    groups.Length,
                    new Dictionary<
                        string,
                        IEnumerable<ExplanationResourceKey>>
                    {
                        ["navigation-collection"] = groups.Select(group =>
                            kindCollectionKeys[
                                (
                                    section.Identity,
                                    group.Key
                                    ?? throw new InvalidOperationException(
                                        "A structural item kind is required.")
                                )]),
                    }));

            foreach (IGrouping<string?, DiscoveryResourceIdentity> group
                     in groups)
            {
                string itemKind = group.Key
                    ?? throw new InvalidOperationException(
                        "A structural item kind is required.");
                ResourcePath kindPath = itemsPath.Append(itemKind);
                snapshots.Add(
                    NavigationSnapshot(
                        kindPath,
                        kindCollectionKeys[(section.Identity, itemKind)],
                        $"{section.Identity.Name} {itemKind} items",
                        group.Count(),
                        new Dictionary<
                            string,
                            IEnumerable<ExplanationResourceKey>>
                        {
                            ["collection-item"] =
                                group.Select(identity => keys[identity]),
                        }));
            }
        }

        return Create(ResourceExplanationVocabulary.Schemas, snapshots);
    }

    public static ResourceExplanationCatalog CreateCapabilities(
        InspectionCapabilityCatalog catalog,
        IEnumerable<InspectionCapabilityResourcePathRegistration>
            registrations)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(registrations);
        IReadOnlyDictionary<
            InspectionCapabilityResourceIdentity,
            ResourcePath> paths = IndexCapabilityPaths(registrations);

        QuerySpaceBinding[] querySpaces =
        [
            .. catalog.Routes
                .Select(static route => route.QuerySpace)
                .DistinctBy(
                    static querySpace =>
                        querySpace.Descriptor.Identity,
                    StringComparer.Ordinal),
        ];
        InspectionCapabilityResourceIdentity[] identities =
        [
            .. catalog.Documents.Select(DocumentIdentity),
            .. catalog.Routes.Select(RouteIdentity),
            .. querySpaces.Select(QuerySpaceIdentity),
            .. querySpaces.SelectMany(querySpace =>
                querySpace.Descriptor.Operation.Terms.Select(term =>
                    QueryFacetIdentity(querySpace, term))),
            .. catalog.Bindings.Select(ConsumerBindingIdentity),
        ];
        if (paths.Count != identities.Length)
        {
            throw new ArgumentException(
                "Every composed inspection capability resource must have "
                + "exactly one canonical Resource Explanation path.",
                nameof(registrations));
        }

        var keys = identities.ToDictionary(
            static identity => identity,
            identity => CapabilityKey(identity));
        var snapshots = new List<ExplanationResourceSnapshot>();

        foreach (InspectionDocumentRegistration document
                 in catalog.Documents)
        {
            InspectionCapabilityResourceIdentity identity =
                DocumentIdentity(document);
            snapshots.Add(
                Snapshot(
                    paths[identity],
                    keys[identity],
                    [
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .InspectionDocumentType,
                            "identity",
                            document.Descriptor.Identity),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .InspectionDocumentType,
                            "name",
                            document.Descriptor.Name),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .InspectionDocumentType,
                            "summary",
                            document.Descriptor.Summary),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .InspectionDocumentType,
                            "result-contract",
                            document.Descriptor.ResultContract),
                    ],
                    RelationshipObservations(
                        ResourceExplanationVocabulary
                            .InspectionDocumentType,
                        new Dictionary<
                            string,
                            IEnumerable<ExplanationResourceKey>>
                        {
                            ["route"] = catalog.Routes
                                .Where(route =>
                                    ReferenceEquals(
                                        route.Document,
                                        document))
                                .Select(route => keys[RouteIdentity(route)]),
                        })));
        }

        foreach (InspectionRouteRegistration route in catalog.Routes)
        {
            InspectionCapabilityResourceIdentity identity =
                RouteIdentity(route);
            QuerySpaceOperationScopeDescriptor operation =
                route.QuerySpace.Descriptor.Operation;
            snapshots.Add(
                Snapshot(
                    paths[identity],
                    keys[identity],
                    [
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .HostNeutralRouteType,
                            "identity",
                            route.Descriptor.Identity),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .HostNeutralRouteType,
                            "name",
                            route.Descriptor.Name),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .HostNeutralRouteType,
                            "summary",
                            route.Descriptor.Summary),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .HostNeutralRouteType,
                            "subject-role",
                            operation.SubjectRole),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .HostNeutralRouteType,
                            "result-grain",
                            operation.ResultGrain),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .HostNeutralRouteType,
                            "profile",
                            operation.Profile),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .HostNeutralRouteType,
                            "result-contract",
                            route.Document.Descriptor.ResultContract),
                    ],
                    RelationshipObservations(
                        ResourceExplanationVocabulary.HostNeutralRouteType,
                        new Dictionary<
                            string,
                            IEnumerable<ExplanationResourceKey>>
                        {
                            ["produces"] =
                                [keys[DocumentIdentity(route.Document)]],
                            ["query-surface"] =
                                [keys[QuerySpaceIdentity(route.QuerySpace)]],
                            ["consumer-binding"] = catalog.Bindings
                                .Where(binding =>
                                    ReferenceEquals(binding.Route, route))
                                .Select(binding =>
                                    keys[ConsumerBindingIdentity(binding)]),
                        })));
        }

        foreach (QuerySpaceBinding querySpace in querySpaces)
        {
            InspectionCapabilityResourceIdentity identity =
                QuerySpaceIdentity(querySpace);
            snapshots.Add(
                Snapshot(
                    paths[identity],
                    keys[identity],
                    [
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary.QuerySpaceType,
                            "identity",
                            querySpace.Descriptor.Identity),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary.QuerySpaceType,
                            "name",
                            querySpace.Descriptor.Operation.Operation),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary.QuerySpaceType,
                            "summary",
                            "The effective executable Query Space for this "
                            + "route."),
                        ResourceExplanationVocabulary.IntegerFact(
                            ResourceExplanationVocabulary.QuerySpaceType,
                            "members",
                            querySpace.Descriptor.Operation.Terms.Count),
                    ],
                    RelationshipObservations(
                        ResourceExplanationVocabulary.QuerySpaceType,
                        new Dictionary<
                            string,
                            IEnumerable<ExplanationResourceKey>>
                        {
                            ["query-facet"] =
                                querySpace.Descriptor.Operation.Terms.Select(
                                    term => keys[
                                        QueryFacetIdentity(
                                            querySpace,
                                            term)]),
                        })));

            IEnumerable<InspectionQueryTermRelationship> termRelationships =
                catalog.Routes
                    .Where(route =>
                        ReferenceEquals(route.QuerySpace, querySpace))
                    .SelectMany(static route =>
                        route.QueryTermRelationships)
                    .Distinct();
            foreach (QuerySpaceOperationTermDescriptor term
                     in querySpace.Descriptor.Operation.Terms)
            {
                InspectionCapabilityResourceIdentity termIdentity =
                    QueryFacetIdentity(querySpace, term);
                snapshots.Add(
                    Snapshot(
                        paths[termIdentity],
                        keys[termIdentity],
                        [
                            ResourceExplanationVocabulary.TextFact(
                                ResourceExplanationVocabulary
                                    .QueryFacetType,
                                "identity",
                                term.Identity),
                            ResourceExplanationVocabulary.TextFact(
                                ResourceExplanationVocabulary
                                    .QueryFacetType,
                                "key",
                                term.Key),
                            ResourceExplanationVocabulary.TextFact(
                                ResourceExplanationVocabulary
                                    .QueryFacetType,
                                "name",
                                term.Label),
                            ResourceExplanationVocabulary.TextFact(
                                ResourceExplanationVocabulary
                                    .QueryFacetType,
                                "summary",
                                term.Summary),
                            ResourceExplanationVocabulary.TextsFact(
                                ResourceExplanationVocabulary
                                    .QueryFacetType,
                                "operators",
                                term.Operators.Select(
                                    PortableQueryModel.TextOf)),
                            ResourceExplanationVocabulary.TextFact(
                                ResourceExplanationVocabulary
                                    .QueryFacetType,
                                "value-kind",
                                term.ValueKind),
                            ResourceExplanationVocabulary.TextsFact(
                                ResourceExplanationVocabulary
                                    .QueryFacetType,
                                "values",
                                term.Values),
                            ResourceExplanationVocabulary.TextsFact(
                                ResourceExplanationVocabulary
                                    .QueryFacetType,
                                "examples",
                                term.Examples),
                            ResourceExplanationVocabulary.TextsFact(
                                ResourceExplanationVocabulary
                                    .QueryFacetType,
                                "effects",
                                term.Effects.Select(static effect =>
                                    $"{effect.Kind}: {effect.Identity}")),
                        ],
                        RelationshipObservations(
                            ResourceExplanationVocabulary.QueryFacetType,
                            new Dictionary<
                                string,
                                IEnumerable<ExplanationResourceKey>>
                            {
                                ["route"] = catalog.Routes
                                    .Where(route =>
                                        ReferenceEquals(
                                            route.QuerySpace,
                                            querySpace))
                                    .Select(route =>
                                        keys[RouteIdentity(route)]),
                                ["required-context"] = termRelationships
                                    .Where(relationship =>
                                        relationship.SourceTerm
                                            == term.Identity
                                        && relationship.Kind
                                            == InspectionQueryTermRelationshipKind
                                                .RequiredContext)
                                    .Select(relationship =>
                                    {
                                        QuerySpaceOperationTermDescriptor
                                            target =
                                                querySpace.Descriptor.Operation
                                                    .Terms.Single(candidate =>
                                                        candidate.Identity
                                                        == relationship
                                                            .TargetTerm);
                                        return keys[
                                            QueryFacetIdentity(
                                                querySpace,
                                                target)];
                                    }),
                                ["exposed-by"] = catalog.Bindings
                                    .Where(binding =>
                                        ReferenceEquals(
                                            binding.Route.QuerySpace,
                                            querySpace)
                                        && binding.ExposedQueryTerms.Contains(
                                            term.Identity))
                                    .Select(binding =>
                                        keys[
                                            ConsumerBindingIdentity(
                                                binding)]),
                            })));
            }
        }

        foreach (InspectionConsumerBinding binding in catalog.Bindings)
        {
            InspectionCapabilityResourceIdentity identity =
                ConsumerBindingIdentity(binding);
            snapshots.Add(
                Snapshot(
                    paths[identity],
                    keys[identity],
                    [
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .ConsumerBindingType,
                            "identity",
                            binding.Descriptor.Identity),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .ConsumerBindingType,
                            "name",
                            binding.Descriptor.Owner),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .ConsumerBindingType,
                            "summary",
                            $"A production {binding.Descriptor.Kind} binding "
                            + $"for '{binding.Route.Descriptor.Name}'."),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .ConsumerBindingType,
                            "consumer-kind",
                            binding.Descriptor.Kind.ToString()),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary
                                .ConsumerBindingType,
                            "gesture",
                            binding.Descriptor.Gesture),
                        ResourceExplanationVocabulary.IntegerFact(
                            ResourceExplanationVocabulary
                                .ConsumerBindingType,
                            "members",
                            binding.ExposedQueryTerms.Length),
                    ],
                    RelationshipObservations(
                        ResourceExplanationVocabulary.ConsumerBindingType,
                        new Dictionary<
                            string,
                            IEnumerable<ExplanationResourceKey>>
                        {
                            ["invokes"] =
                                [keys[RouteIdentity(binding.Route)]],
                            ["exposes"] = binding.ExposedQueryTerms.Select(
                                exposed =>
                                {
                                    QuerySpaceOperationTermDescriptor term =
                                        binding.Route.QuerySpace.Descriptor
                                            .Operation.Terms.Single(
                                                candidate =>
                                                    candidate.Identity
                                                    == exposed);
                                    return keys[
                                        QueryFacetIdentity(
                                            binding.Route.QuerySpace,
                                            term)];
                                }),
                        })));
        }

        return Create(ResourceExplanationVocabulary.Schemas, snapshots);
    }

    public static ResourceExplanationCatalog CreateAnalyses(
        InspectionCapabilityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Analyses.IsEmpty)
        {
            throw new ArgumentException(
                "The capability catalog registers no analyses.",
                nameof(catalog));
        }

        var collectionPath = new ResourcePath(AnalysesCollectionSegment);
        ExplanationResourceKey collectionKey =
            ResourceExplanationVocabulary.Key(
                ResourceExplanationVocabulary.NavigationCollectionType,
                Pack("analysis-collection"));
        var analysisKeys = catalog.Analyses.ToDictionary(
            static registration => registration,
            registration => ResourceExplanationVocabulary.Key(
                ResourceExplanationVocabulary.AnalysisType,
                Pack(registration.Analysis.Id.Value)));
        var snapshots = new List<ExplanationResourceSnapshot>
        {
            NavigationSnapshot(
                collectionPath,
                collectionKey,
                "Analyses",
                catalog.Analyses.Length,
                new Dictionary<string, IEnumerable<ExplanationResourceKey>>
                {
                    ["collection-analysis"] =
                        catalog.Analyses.Select(registration =>
                            analysisKeys[registration]),
                }),
        };

        foreach (InspectionAnalysisRegistration registration
                 in catalog.Analyses)
        {
            AnalysisDescriptor analysis = registration.Analysis;
            var operationSurfaces = new List<ExplanationResourceKey>();
            var findings = new List<ExplanationResourceKey>();
            foreach (AnalysisOperationParticipation participation
                     in analysis.Participations)
            {
                foreach (AnalysisSurfaceParticipation surface
                         in participation.Surfaces)
                {
                    string operation = participation.Operation.ToString();
                    string surfaceKind = surface.Surface.ToString();
                    operationSurfaces.Add(
                        ResourceExplanationVocabulary.Key(
                            ResourceExplanationVocabulary
                                .OperationSurfaceType,
                            $"{operation} / {surfaceKind}"));
                    findings.AddRange(
                        surface.Descriptors.Select(descriptor =>
                            ResourceExplanationVocabulary.Key(
                                ResourceExplanationVocabulary
                                    .IssuedFindingType,
                                $"{operation} / {surfaceKind} / "
                                + descriptor.Id)));
                }
            }

            snapshots.Add(
                Snapshot(
                    AnalysisPath(analysis.Id.Value),
                    analysisKeys[registration],
                    [
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary.AnalysisType,
                            "identity",
                            analysis.Id.Value),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary.AnalysisType,
                            "name",
                            analysis.Id.Value),
                        ResourceExplanationVocabulary.IntegerFact(
                            ResourceExplanationVocabulary.AnalysisType,
                            "revision",
                            analysis.Revision),
                        ResourceExplanationVocabulary.TextFact(
                            ResourceExplanationVocabulary.AnalysisType,
                            "cost",
                            analysis.Cost.ToString()),
                        ResourceExplanationVocabulary.TextsFact(
                            ResourceExplanationVocabulary.AnalysisType,
                            "participations",
                            analysis.Participations.SelectMany(
                                static participation =>
                                    participation.Surfaces.Select(surface =>
                                        $"{participation.Operation} "
                                        + $"{surface.Surface}: "
                                        + string.Join(
                                            ", ",
                                            surface.Descriptors.Select(
                                                static descriptor =>
                                                    descriptor.Id))))),
                    ],
                    RelationshipObservations(
                        ResourceExplanationVocabulary.AnalysisType,
                        new Dictionary<
                            string,
                            IEnumerable<ExplanationResourceKey>>
                        {
                            ["participates"] = operationSurfaces,
                            ["issues"] = findings,
                        })));
        }

        return Create(ResourceExplanationVocabulary.Schemas, snapshots);
    }

    public const string AnalysesCollectionSegment = "analyses";

    public static ResourcePath AnalysisPath(string analysisIdentity) =>
        new ResourcePath(AnalysesCollectionSegment).Append(analysisIdentity);

    internal static InspectionCapabilityResourceIdentity AnalysisIdentity(
        InspectionAnalysisRegistration registration) =>
        new(
            InspectionCapabilityResourceKind.Analysis,
            registration.Analysis.Id.Value);

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

        ExplanationSchema[] schemas =
        [
            .. catalogs
                .SelectMany(static catalog => catalog.Schemas)
                .GroupBy(static schema =>
                    (schema.Identity, schema.Version))
                .Select(static group => group.First()),
        ];
        return Create(
            schemas,
            catalogs.SelectMany(static catalog =>
                catalog._catalogResources.Select(
                    static resource => resource.Snapshot)));
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

        ResourcePath canonicalPath = path!;
        if (TryResolveExact(canonicalPath, out var resolved))
            return resolved;

        ImmutableArray<ResourcePath> suggestions =
        [
            .. _catalogResources
                .OrderBy(resource =>
                    EditDistance(
                        canonicalPath.Value,
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

    public bool TryResolveExact(
        ResourcePath path,
        [NotNullWhen(true)]
        out ResourcePathResolution.Resolved? resolved)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!_resourcesByPath.TryGetValue(
                path.Value,
                out CatalogResource? resource))
        {
            resolved = null;
            return false;
        }

        resolved = new(resource.Path, resource.Snapshot.Key);
        return true;
    }

    public InspectionEnvelope<ResourceExplanationDocument> Explain(
        ResourcePathResolution.Resolved resolved,
        ResourceExplanationRequest request)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(request);
        if (!_resourcesByPath.TryGetValue(
                resolved.Path.Value,
                out CatalogResource? root)
            || root.Snapshot.Key != resolved.Key)
        {
            throw new ArgumentException(
                "The resolved path does not belong to this catalog.",
                nameof(resolved));
        }

        var resources = new List<CatalogResource> { root };
        var resourceDepth =
            new Dictionary<ExplanationResourceKey, int>
            {
                [root.Snapshot.Key] = 0,
            };
        var relationships =
            new List<ResourceExplanationRelationship>();
        var visited =
            new HashSet<ExplanationResourceKey> { root.Snapshot.Key };
        var queue =
            new Queue<(CatalogResource Resource, int Depth)>();
        queue.Enqueue((root, 0));
        var truncationReasons =
            new HashSet<ResourceExplanationTruncationReason>();
        int emittedTargets = 0;
        bool relationshipLimitReached = false;

        while (queue.Count > 0 && !relationshipLimitReached)
        {
            (CatalogResource current, int depth) = queue.Dequeue();
            foreach (ExplanationRelationshipObservation observation
                     in current.Snapshot.Relationships)
            {
                if (relationships.Count >= request.RelationshipLimit)
                {
                    truncationReasons.Add(
                        ResourceExplanationTruncationReason
                            .RelationshipLimit);
                    relationshipLimitReached = true;
                    break;
                }

                int remainingTargets =
                    request.RelationshipTargetLimit - emittedTargets;
                ProjectedRelationships projected =
                    ProjectRelationship(
                        current,
                        observation,
                        _resourcesByKey,
                        remainingTargets);
                relationships.Add(projected.Relationship);
                emittedTargets += projected.Relationship.Targets.Length;
                if (projected.WasTruncated)
                {
                    truncationReasons.Add(
                        ResourceExplanationTruncationReason
                            .RelationshipTargetLimit);
                }

                foreach (ResourceExplanationRelationshipTarget projectedTarget
                         in projected.Relationship.Targets)
                {
                    if (!_resourcesByKey.TryGetValue(
                            projectedTarget.Resource,
                            out CatalogResource? target)
                        || visited.Contains(target.Snapshot.Key))
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
                            ResourceExplanationTruncationReason
                                .ResourceLimit);
                        continue;
                    }

                    visited.Add(target.Snapshot.Key);
                    resources.Add(target);
                    int targetDepth = depth + 1;
                    resourceDepth.Add(target.Snapshot.Key, targetDepth);
                    queue.Enqueue((target, targetDepth));
                }
            }
        }

        ImmutableArray<ExplanationSchema> schemaSlice =
            CreateSchemaSlice(resources, relationships);
        while (ResourceExplanationVocabulary.DeclarationCount(schemaSlice)
            > request.SchemaDeclarationLimit)
        {
            truncationReasons.Add(
                ResourceExplanationTruncationReason
                    .SchemaDeclarationLimit);
            if (relationships.Count > 0)
            {
                relationships.RemoveAt(relationships.Count - 1);
            }
            else if (resources.Count > 1)
            {
                CatalogResource removed = resources[^1];
                resources.RemoveAt(resources.Count - 1);
                resourceDepth.Remove(removed.Snapshot.Key);
                relationships.RemoveAll(relationship =>
                    relationship.Source == removed.Snapshot.Key);
            }
            else
            {
                throw new InvalidOperationException(
                    "The schema declaration limit is too small to explain "
                    + "the requested root resource.");
            }
            schemaSlice = CreateSchemaSlice(resources, relationships);
        }

        emittedTargets = relationships.Sum(static relationship =>
            relationship.Targets.Length);
        int completedDepth = resourceDepth.Count == 0
            ? 0
            : resourceDepth.Values.Max();
        int declarationCount =
            ResourceExplanationVocabulary.DeclarationCount(schemaSlice);
        var receipt =
            new ResourceExplanationTraversalReceipt(
                request.Depth,
                request.ResourceLimit,
                request.RelationshipLimit,
                request.RelationshipTargetLimit,
                request.SchemaDeclarationLimit,
                completedDepth,
                resources.Count,
                relationships.Count,
                emittedTargets,
                declarationCount,
                truncationReasons.Count == 0
                    ? ResourceExplanationCompleteness.Complete
                    : ResourceExplanationCompleteness.Truncated,
                truncationReasons.Order());
        var document =
            new ResourceExplanationDocument(
                root.Path,
                root.Snapshot.Key,
                schemaSlice,
                resources.Select(static resource =>
                    resource.Projection),
                relationships,
                receipt);
        return new InspectionEnvelope<ResourceExplanationDocument>(
            document,
            new InspectionShare.NonProjectable(
                root.Path.Value,
                "Resource Explanation does not yet have a portable "
                + "Workspace projection."));
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

    internal static ExplanationResourceKey CapabilityKey(
        InspectionCapabilityResourceIdentity identity) =>
        ResourceExplanationVocabulary.Key(
            identity.Kind switch
            {
                InspectionCapabilityResourceKind.Document =>
                    ResourceExplanationVocabulary.InspectionDocumentType,
                InspectionCapabilityResourceKind.Route =>
                    ResourceExplanationVocabulary.HostNeutralRouteType,
                InspectionCapabilityResourceKind.QuerySpace =>
                    ResourceExplanationVocabulary.QuerySpaceType,
                InspectionCapabilityResourceKind.QueryFacet =>
                    ResourceExplanationVocabulary.QueryFacetType,
                InspectionCapabilityResourceKind.ConsumerBinding =>
                    ResourceExplanationVocabulary.ConsumerBindingType,
                InspectionCapabilityResourceKind.Analysis =>
                    ResourceExplanationVocabulary.AnalysisType,
                InspectionCapabilityResourceKind.AnalysisCollection =>
                    ResourceExplanationVocabulary.NavigationCollectionType,
                _ => throw new InvalidOperationException(
                    "Unknown inspection capability resource kind."),
            },
            CapabilityIdentity(identity));

    private static ImmutableArray<ExplanationFactObservation> StructuralFacts(
        DiscoveryResource resource,
        ExplanationResourceTypeIdentity type) =>
        resource.Identity.Kind switch
        {
            DiscoveryResourceKind.Category =>
            [
                ResourceExplanationVocabulary.TextFact(
                    type,
                    "name",
                    resource.Identity.Name),
                ResourceExplanationVocabulary.TextsFact(
                    type,
                    "formats",
                    resource.OutputModes.Select(static mode =>
                        mode.ToString())),
                ResourceExplanationVocabulary.IntegerFact(
                    type,
                    "members",
                    resource.Members.Length),
            ],
            DiscoveryResourceKind.Section =>
            [
                ResourceExplanationVocabulary.TextFact(
                    type,
                    "name",
                    resource.Identity.Name),
                ResourceExplanationVocabulary.TextsFact(
                    type,
                    "formats",
                    resource.OutputModes.Select(static mode =>
                        mode.ToString())),
                ResourceExplanationVocabulary.IntegerFact(
                    type,
                    "members",
                    resource.Members.Length),
                resource.Shape is { } shape
                    ? ResourceExplanationVocabulary.TextFact(
                        type,
                        "shape",
                        shape.ToString().ToLowerInvariant())
                    : ResourceExplanationVocabulary.AbsentFact(
                        type,
                        "shape"),
                resource.Cardinality is { } cardinality
                    ? ResourceExplanationVocabulary.TextFact(
                        type,
                        "cardinality",
                        cardinality.Kind.ToString().ToLowerInvariant())
                    : ResourceExplanationVocabulary.AbsentFact(
                        type,
                        "cardinality"),
            ],
            DiscoveryResourceKind.Item =>
            [
                ResourceExplanationVocabulary.TextFact(
                    type,
                    "name",
                    resource.Identity.Name),
                ResourceExplanationVocabulary.TextFact(
                    type,
                    "item-kind",
                    resource.Identity.ItemKind
                    ?? throw new InvalidOperationException(
                        "A structural item kind is required.")),
            ],
            _ => throw new InvalidOperationException(
                "Unknown Discovery resource kind."),
        };

    private static ImmutableArray<ExplanationRelationshipObservation>
        StructuralRelationships(
            DiscoveryResource resource,
            ExplanationResourceTypeIdentity type,
            IReadOnlyDictionary<
                DiscoveryResourceIdentity,
                ExplanationResourceKey> keys,
            IReadOnlyDictionary<
                DiscoveryResourceIdentity,
                ExplanationResourceKey> itemCollections)
    {
        var targets =
            new Dictionary<string, IEnumerable<ExplanationResourceKey>>();
        switch (resource.Identity.Kind)
        {
            case DiscoveryResourceKind.Category:
                targets["category-member"] =
                    resource.Members.Select(member => keys[member]);
                break;
            case DiscoveryResourceKind.Section:
                targets["navigation"] =
                    itemCollections.TryGetValue(
                        resource.Identity,
                        out ExplanationResourceKey? collection)
                        ? [collection]
                        : [];
                targets["structural-item"] =
                    resource.Members.Select(member => keys[member]);
                break;
        }
        return RelationshipObservations(type, targets);
    }

    private static ExplanationResourceSnapshot NavigationSnapshot(
        ResourcePath path,
        ExplanationResourceKey key,
        string name,
        int members,
        IReadOnlyDictionary<
            string,
            IEnumerable<ExplanationResourceKey>> targets) =>
        Snapshot(
            path,
            key,
            [
                ResourceExplanationVocabulary.TextFact(
                    ResourceExplanationVocabulary.NavigationCollectionType,
                    "name",
                    name),
                ResourceExplanationVocabulary.IntegerFact(
                    ResourceExplanationVocabulary.NavigationCollectionType,
                    "members",
                    members),
            ],
            RelationshipObservations(
                ResourceExplanationVocabulary.NavigationCollectionType,
                targets));

    private static ExplanationResourceSnapshot Snapshot(
        ResourcePath path,
        ExplanationResourceKey key,
        IEnumerable<ExplanationFactObservation> facts,
        IEnumerable<ExplanationRelationshipObservation> relationships) =>
        ExplanationConformance.CreateSnapshot(
            ResourceExplanationVocabulary.Schemas,
            key,
            ResourceExplanationVocabulary.Version,
            ExplanationSnapshotScope.Installed,
            [ResourceExplanationVocabulary.Address(path)],
            facts,
            relationships);

    private static ImmutableArray<ExplanationRelationshipObservation>
        RelationshipObservations(
            ExplanationResourceTypeIdentity sourceType,
            IReadOnlyDictionary<
                string,
                IEnumerable<ExplanationResourceKey>> targets)
    {
        ExplanationResourceTypeDeclaration declaration =
            ResourceExplanationVocabulary.ResourceType(sourceType);
        return
        [
            .. declaration.Relationships.Select(relationship =>
                ResourceExplanationVocabulary.Targets(
                    sourceType,
                    relationship.Identity.Value,
                    targets.TryGetValue(
                        relationship.Identity.Value,
                        out IEnumerable<ExplanationResourceKey>? values)
                        ? values
                        : [])),
        ];
    }

    private static ResourcePath Path(
        ExplanationResourceSnapshot snapshot)
    {
        ExplanationPublicAddress address = snapshot.Addresses.Single(
            candidate =>
                candidate.Kind
                    == ResourceExplanationVocabulary.ResourcePathAddressKind);
        if (address.Value is not ExplanationValue.Scalar
            {
                Value:
                {
                    Kind: ExplanationScalarKind.Text,
                    Text: { } text,
                },
            })
        {
            throw new ArgumentException(
                $"Resource '{snapshot.Key}' has a non-text Resource "
                + "Explanation path.",
                nameof(snapshot));
        }
        return new ResourcePath(text);
    }

    private static ExplanationResourceKey NavigationKey(
        string catalog,
        string kind,
        params string[] components) =>
        ResourceExplanationVocabulary.Key(
            ResourceExplanationVocabulary.NavigationCollectionType,
            Pack([catalog, kind, .. components]));

    private static ExplanationResourceTypeIdentity StructuralType(
        DiscoveryResourceKind kind) =>
        kind switch
        {
            DiscoveryResourceKind.Category =>
                ResourceExplanationVocabulary.StructuralCategoryType,
            DiscoveryResourceKind.Section =>
                ResourceExplanationVocabulary.StructuralSectionType,
            DiscoveryResourceKind.Item =>
                ResourceExplanationVocabulary.StructuralItemType,
            _ => throw new InvalidOperationException(
                "Unknown Discovery resource kind."),
        };

    private static string StructuralIdentity(
        string catalog,
        DiscoveryResourceIdentity identity) =>
        Pack(
            catalog,
            identity.Kind.ToString(),
            identity.Name,
            identity.Section,
            identity.ItemKind);

    private static string CapabilityIdentity(
        InspectionCapabilityResourceIdentity identity) =>
        Pack(
            identity.Kind.ToString(),
            identity.Identity,
            identity.ParentIdentity);

    private static string Pack(params string?[] values) =>
        string.Concat(values.Select(static value =>
            value is null
                ? "-1:"
                : $"{value.Length}:{value}"));

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

    private static ImmutableArray<ExplanationSchema> CreateSchemaSlice(
        IEnumerable<CatalogResource> resources,
        IEnumerable<ResourceExplanationRelationship> relationships)
    {
        CatalogResource[] resourceArray = [.. resources];
        ResourceExplanationRelationship[] relationshipArray =
            [.. relationships];
        var resourceTypes =
            new HashSet<ExplanationResourceTypeIdentity>(
                resourceArray.Select(static resource =>
                    resource.Snapshot.Key.ResourceType));
        foreach (ResourceExplanationRelationship relationship
                 in relationshipArray)
        {
            resourceTypes.Add(relationship.Source.ResourceType);
            ExplanationRelationshipDeclaration declaration =
                ResourceExplanationVocabulary.ResourceType(
                    relationship.Source.ResourceType).Relationships.Single(
                        candidate =>
                            candidate.Identity
                                == relationship.Relationship);
            resourceTypes.Add(declaration.TargetResourceType);
            foreach (ResourceExplanationRelationshipTarget target
                     in relationship.Targets)
            {
                resourceTypes.Add(target.Resource.ResourceType);
            }
        }

        var relationshipIds =
            relationshipArray.Select(static relationship =>
                    relationship.Relationship)
                .ToHashSet();
        var addressKinds =
            resourceArray.SelectMany(static resource =>
                    resource.Snapshot.Addresses)
                .Concat(relationshipArray.SelectMany(static relationship =>
                    relationship.Targets.SelectMany(static target =>
                        target.Addresses)))
                .Select(static address => address.Kind)
                .ToHashSet();
        var shapes = new HashSet<ExplanationDataShapeIdentity>();
        var selectedResources =
            new Dictionary<
                ExplanationResourceTypeIdentity,
                ExplanationResourceTypeDeclaration>();

        foreach (ExplanationResourceTypeIdentity type in resourceTypes)
        {
            ExplanationResourceTypeDeclaration original =
                ResourceExplanationVocabulary.ResourceType(type);
            bool emitted = resourceArray.Any(resource =>
                resource.Snapshot.Key.ResourceType == type);
            ImmutableArray<ExplanationFactDeclaration> facts =
                emitted ? original.Facts : [];
            ImmutableArray<ExplanationRelationshipDeclaration>
                selectedRelationships =
            [
                .. original.Relationships.Where(relationship =>
                    relationshipIds.Contains(relationship.Identity)),
            ];
            ImmutableArray<ExplanationPublicAddressKindIdentity>
                selectedAddressKinds =
            [
                .. original.AddressKinds.Where(addressKinds.Contains),
            ];
            var selected =
                new ExplanationResourceTypeDeclaration(
                    original.Identity,
                    original.DisplayName,
                    original.Meaning,
                    original.IdentityShape,
                    facts,
                    selectedRelationships,
                    selectedAddressKinds);
            selectedResources.Add(type, selected);
            AddShape(original.IdentityShape);
            foreach (ExplanationFactDeclaration fact in facts)
            {
                AddShape(fact.ValueShape);
                if (fact.UnavailableDataShape is { } unavailable)
                    AddShape(unavailable);
                if (fact.FailureDataShape is { } failure)
                    AddShape(failure);
            }
            foreach (ExplanationRelationshipDeclaration relationship
                     in selectedRelationships)
            {
                if (relationship.UnavailableDataShape is { } unavailable)
                    AddShape(unavailable);
                if (relationship.FailureDataShape is { } failure)
                    AddShape(failure);
            }
        }

        foreach (ExplanationPublicAddressKindIdentity kind in addressKinds)
        {
            ExplanationPublicAddressKindDeclaration declaration =
                ResourceExplanationVocabulary.Schemas
                    .SelectMany(static schema => schema.AddressKinds)
                    .Single(candidate => candidate.Identity == kind);
            AddShape(declaration.ValueShape);
        }

        var result = ImmutableArray.CreateBuilder<ExplanationSchema>();
        foreach (ExplanationSchema schema
                 in ResourceExplanationVocabulary.Schemas)
        {
            ExplanationDataShapeDeclaration[] selectedShapes =
            [
                .. schema.DataShapes.Where(shape =>
                    shapes.Contains(shape.Identity)),
            ];
            ExplanationResourceTypeDeclaration[] types =
            [
                .. schema.ResourceTypes
                    .Where(resource =>
                        selectedResources.ContainsKey(resource.Identity))
                    .Select(resource =>
                        selectedResources[resource.Identity]),
            ];
            ExplanationPublicAddressKindDeclaration[] addresses =
            [
                .. schema.AddressKinds.Where(address =>
                    addressKinds.Contains(address.Identity)),
            ];
            if (selectedShapes.Length == 0
                && types.Length == 0
                && addresses.Length == 0)
            {
                continue;
            }
            result.Add(
                new(
                    schema.Identity,
                    schema.Version,
                    selectedShapes,
                    types,
                    addresses));
        }
        return result.ToImmutable();

        void AddShape(ExplanationDataShapeIdentity identity)
        {
            if (!shapes.Add(identity))
                return;
            ExplanationDataShapeDeclaration shape =
                ResourceExplanationVocabulary.Schemas
                    .SelectMany(static schema => schema.DataShapes)
                    .Single(candidate => candidate.Identity == identity);
            switch (shape)
            {
                case ExplanationDataShapeDeclaration.Record record:
                    foreach (ExplanationRecordFieldDeclaration field
                             in record.Fields)
                    {
                        AddShape(field.ValueShape);
                    }
                    break;
                case ExplanationDataShapeDeclaration.Choice choice:
                    foreach (ExplanationChoiceCaseDeclaration @case
                             in choice.Cases)
                    {
                        if (@case.ValueShape is { } valueShape)
                            AddShape(valueShape);
                    }
                    break;
                case ExplanationDataShapeDeclaration.Reference reference:
                    AddShape(reference.Target);
                    break;
            }
        }
    }

    private static ProjectedRelationships ProjectRelationships(
        CatalogResource resource,
        IReadOnlyDictionary<
            ExplanationResourceKey,
            CatalogResource> resourcesByKey,
        int targetLimit)
    {
        var relationships =
            ImmutableArray.CreateBuilder<ResourceExplanationRelationship>();
        int remaining = targetLimit;
        bool truncated = false;
        foreach (ExplanationRelationshipObservation observation
                 in resource.Snapshot.Relationships)
        {
            ProjectedRelationships projected =
                ProjectRelationship(
                    resource,
                    observation,
                    resourcesByKey,
                    remaining);
            relationships.Add(projected.Relationship);
            remaining -= projected.Relationship.Targets.Length;
            truncated |= projected.WasTruncated;
        }
        return new(relationships.ToImmutable(), truncated);
    }

    private static ProjectedRelationships ProjectRelationship(
        CatalogResource resource,
        ExplanationRelationshipObservation observation,
        IReadOnlyDictionary<
            ExplanationResourceKey,
            CatalogResource> resourcesByKey,
        int targetLimit)
    {
        int count = Math.Min(
            Math.Max(targetLimit, 0),
            observation.Targets.Length);
        ResourceExplanationRelationshipTarget[] targets =
        [
            .. observation.Targets
                .Take(count)
                .Select(target =>
                    new ResourceExplanationRelationshipTarget(
                        target.Resource,
                        resourcesByKey.TryGetValue(
                            target.Resource,
                            out CatalogResource? registered)
                            ? registered.Snapshot.Addresses
                            : [])),
        ];
        bool truncated = count < observation.Targets.Length;
        return new(
            new ResourceExplanationRelationship(
                resource.Snapshot.Key,
                observation.Relationship,
                observation.State,
                targets,
                observation.OutcomeData,
                truncated
                    ? ResourceExplanationTargetProjectionCompleteness
                        .Truncated
                    : ResourceExplanationTargetProjectionCompleteness
                        .Complete),
            truncated);
    }

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

    private sealed record CatalogResource(
        ResourcePath Path,
        ExplanationResourceSnapshot Snapshot)
    {
        public ResourceExplanationResource Projection { get; } =
            ResourceExplanationResource.FromSnapshot(Path, Snapshot);
    }

    private sealed record ProjectedRelationships(
        ImmutableArray<ResourceExplanationRelationship> Relationships,
        bool WasTruncated)
    {
        public ResourceExplanationRelationship Relationship =>
            Relationships.Single();

        public ProjectedRelationships(
            ResourceExplanationRelationship relationship,
            bool wasTruncated)
            : this(
                ImmutableArray.Create(relationship),
                wasTruncated)
        {
        }
    }
}
