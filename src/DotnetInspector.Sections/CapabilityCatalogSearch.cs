using System.Collections.Immutable;
using System.Text.Json.Serialization;
using ILInspector.MetadataPrimitives;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public sealed record CapabilityCatalogSearchRequest
{
    public const int DefaultMaximumResults = 20;
    public const int MaximumTextLength = 128;
    public const int MaximumResultLimit = 100;

    public CapabilityCatalogSearchRequest(
        string text,
        int maximumResults = DefaultMaximumResults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string normalized = text.Trim();
        if (normalized.Length > MaximumTextLength)
        {
            throw new ArgumentException(
                $"Capability search text must not exceed "
                + $"{MaximumTextLength} UTF-16 code units.",
                nameof(text));
        }
        if (maximumResults is < 1 or > MaximumResultLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumResults),
                $"Capability search results must be between 1 and "
                + $"{MaximumResultLimit}.");
        }

        Text = normalized;
        MaximumResults = maximumResults;
    }

    public string Text { get; }

    public int MaximumResults { get; }
}

[JsonConverter(
    typeof(JsonStringEnumConverter<CapabilityCatalogSearchMatchSource>))]
public enum CapabilityCatalogSearchMatchSource
{
    CanonicalKey,
    OwnerIdentity,
    ExampleValue,
    ResourcePath,
    ResourceName,
    Summary,
    RelatedRoute,
    ProductionBinding,
}

[JsonConverter(
    typeof(JsonStringEnumConverter<CapabilityCatalogSearchResourceKind>))]
public enum CapabilityCatalogSearchResourceKind
{
    InspectionDocument,
    HostNeutralRoute,
    QuerySpace,
    QueryFacet,
    ConsumerBinding,
    Section,
    Category,
}

public sealed record CapabilityCatalogSearchRoute(
    string Identity,
    string Name,
    string ResourcePath);

public sealed record CapabilityCatalogSearchBinding(
    string Identity,
    string Name,
    InspectionConsumerKind ConsumerKind,
    string Gesture,
    string ResourcePath);

/// <summary>
/// The typed identity of a structural Section or Category result: its
/// catalog root and its Discovery resource identity.
/// </summary>
public sealed record CapabilityCatalogSearchStructuralIdentity(
    string Catalog,
    DiscoveryResourceKind Kind,
    string Name);

/// <summary>
/// One section-selection binding a host admits for a structural catalog, and
/// the Sections and Categories that binding addresses. A catalog that more
/// than one command view projects admits one binding per view, so each
/// resource carries only the bindings that can select it.
/// </summary>
public sealed record CapabilityCatalogSearchStructuralBinding
{
    public CapabilityCatalogSearchStructuralBinding(
        CapabilityCatalogSearchBinding binding,
        IEnumerable<DiscoveryResourceIdentity> resources)
    {
        Binding = binding
            ?? throw new ArgumentNullException(nameof(binding));
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.Identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.Gesture);
        Resources = [.. resources];
    }

    public CapabilityCatalogSearchBinding Binding { get; }

    public ImmutableHashSet<DiscoveryResourceIdentity> Resources { get; }
}

/// <summary>
/// One structural catalog a host admits to search: the Discovery projection
/// and path registrations its structural Resource Explanation catalog is
/// created from, its registered structural route, and the host's
/// section-selection bindings. Route and Binding paths are the catalog root
/// path, and every admitted Section and Category has at least one binding.
/// </summary>
public sealed record CapabilityCatalogSearchStructuralCatalog
{
    public CapabilityCatalogSearchStructuralCatalog(
        DiscoveryDocument document,
        IEnumerable<StructuralResourcePathRegistration> paths,
        CapabilityCatalogSearchRoute route,
        IEnumerable<CapabilityCatalogSearchStructuralBinding> bindings)
    {
        Document = document
            ?? throw new ArgumentNullException(nameof(document));
        ArgumentNullException.ThrowIfNull(paths);
        Paths = [.. paths];
        Route = route ?? throw new ArgumentNullException(nameof(route));
        ArgumentNullException.ThrowIfNull(bindings);
        Bindings = [.. bindings];
        ArgumentException.ThrowIfNullOrWhiteSpace(route.Identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(route.Name);
        if (Bindings.IsEmpty)
        {
            throw new ArgumentException(
                "A structural catalog admits at least one binding.",
                nameof(bindings));
        }
        foreach (CapabilityCatalogSearchStructuralBinding binding in Bindings)
        {
            if (!string.Equals(
                    route.ResourcePath,
                    binding.Binding.ResourcePath,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A structural route and binding share the catalog root "
                    + "path.",
                    nameof(bindings));
            }
        }
        if (!string.Equals(
                route.ResourcePath,
                document.Catalog,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Structural route path '{route.ResourcePath}' is not the "
                + $"root of structural catalog '{document.Catalog}'.",
                nameof(route));
        }
    }

    public DiscoveryDocument Document { get; }

    public ImmutableArray<StructuralResourcePathRegistration> Paths { get; }

    public CapabilityCatalogSearchRoute Route { get; }

    public ImmutableArray<CapabilityCatalogSearchStructuralBinding> Bindings
    {
        get;
    }
}

/// <summary>
/// One ranked search result. Exactly one of <see cref="ResourceIdentity"/>
/// (a composed capability resource) and <see cref="StructuralIdentity"/>
/// (an admitted structural Section or Category) is present.
/// </summary>
public sealed record CapabilityCatalogSearchResult(
    double Similarity,
    string MatchedTerm,
    CapabilityCatalogSearchMatchSource MatchSource,
    bool IsSegment,
    InspectionCapabilityResourceIdentity? ResourceIdentity,
    CapabilityCatalogSearchStructuralIdentity? StructuralIdentity,
    CapabilityCatalogSearchResourceKind ResourceKind,
    string ResourceName,
    ImmutableArray<string> CanonicalKeys,
    string ResourcePath,
    ImmutableArray<CapabilityCatalogSearchRoute> OwningRoutes,
    ImmutableArray<CapabilityCatalogSearchBinding> ProductionBindings);

public sealed record CapabilityCatalogSearchDocument(
    string Query,
    double SimilarityThreshold,
    int CandidateResourceCount,
    int MatchCount,
    int ReturnedCount,
    bool IsTruncated,
    ImmutableArray<CapabilityCatalogSearchResult> Results);

public static class CapabilityCatalogSearch
{
    public const double SimilarityThreshold = 0.6;
    private static readonly char[] SegmentSeparators =
        [' ', '\t', '\r', '\n', '.', '/', ':', '-', '_'];

    public static InspectionEnvelope<CapabilityCatalogSearchDocument> Search(
        InspectionCapabilityCatalog capabilityCatalog,
        ResourceExplanationCatalog explanationCatalog,
        CapabilityCatalogSearchRequest request) =>
        Search(capabilityCatalog, explanationCatalog, [], request);

    /// <summary>
    /// Searches the composed capability family and the admitted structural
    /// catalogs. <paramref name="explanationCatalog"/> resolves the
    /// capability family's paths; structural candidates are read from each
    /// catalog's Discovery projection without building its explanation graph.
    /// </summary>
    public static InspectionEnvelope<CapabilityCatalogSearchDocument> Search(
        InspectionCapabilityCatalog capabilityCatalog,
        ResourceExplanationCatalog explanationCatalog,
        IEnumerable<CapabilityCatalogSearchStructuralCatalog>
            structuralCatalogs,
        CapabilityCatalogSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(capabilityCatalog);
        ArgumentNullException.ThrowIfNull(explanationCatalog);
        ArgumentNullException.ThrowIfNull(structuralCatalogs);
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyDictionary<
            InspectionCapabilityResourceIdentity,
            ResourceExplanationResource> resources =
                IndexCapabilityResources(
                    explanationCatalog,
                    capabilityCatalog);
        ImmutableArray<Candidate> candidates =
        [
            .. CreateCandidates(
                capabilityCatalog,
                explanationCatalog,
                resources),
            .. CreateStructuralCandidates([.. structuralCatalogs]),
        ];
        string normalizedQuery = request.Text.ToUpperInvariant();

        ScoredCandidate[] matches =
        [
            .. candidates
                .Select(candidate =>
                    Score(candidate, request.Text, normalizedQuery))
                .OfType<ScoredCandidate>()
                .OrderByDescending(static match => match.Similarity)
                .ThenBy(static match => Strength(match.Term.Source))
                .ThenBy(static match => match.Term.IsSegment)
                .ThenBy(
                    static match => match.Candidate.Path,
                    StringComparer.Ordinal)
                .ThenBy(
                    static match => match.Term.Value,
                    StringComparer.Ordinal),
        ];
        ImmutableArray<CapabilityCatalogSearchResult> results =
        [
            .. matches
                .Take(request.MaximumResults)
                .Select(match =>
                    match.Candidate.ToResult(
                        match.Similarity,
                        match.Term)),
        ];
        var document = new CapabilityCatalogSearchDocument(
            request.Text,
            SimilarityThreshold,
            candidates.Length,
            matches.Length,
            results.Length,
            matches.Length > results.Length,
            results);
        return new InspectionEnvelope<CapabilityCatalogSearchDocument>(
            document,
            new InspectionShare.NonProjectable(
                "capability-search",
                "Capability Catalog Search does not yet have a portable "
                + "Workspace projection."));
    }

    private static IReadOnlyDictionary<
        InspectionCapabilityResourceIdentity,
        ResourceExplanationResource> IndexCapabilityResources(
            ResourceExplanationCatalog catalog,
            InspectionCapabilityCatalog capabilityCatalog)
    {
        InspectionCapabilityResourceIdentity[] identities =
        [
            .. capabilityCatalog.Documents.Select(
                ResourceExplanationCatalog.DocumentIdentity),
            .. capabilityCatalog.Routes.Select(
                ResourceExplanationCatalog.RouteIdentity),
            .. capabilityCatalog.Routes
                .Select(static route => route.QuerySpace)
                .DistinctBy(
                    static querySpace => querySpace.Descriptor.Identity,
                    StringComparer.Ordinal)
                .SelectMany(querySpace =>
                    new[]
                    {
                        ResourceExplanationCatalog.QuerySpaceIdentity(
                            querySpace),
                    }.Concat(
                        querySpace.Descriptor.Operation.Terms.Select(term =>
                            ResourceExplanationCatalog.QueryFacetIdentity(
                                querySpace,
                                term)))),
            .. capabilityCatalog.Bindings.Select(
                ResourceExplanationCatalog.ConsumerBindingIdentity),
        ];
        IReadOnlyDictionary<
            QuerySpace.Explanation.ExplanationResourceKey,
            ResourceExplanationResource> resourcesByKey =
                catalog.Resources.ToDictionary(static resource =>
                    resource.Key);
        var resources =
            new Dictionary<
                InspectionCapabilityResourceIdentity,
                ResourceExplanationResource>();
        foreach (InspectionCapabilityResourceIdentity identity in identities)
        {
            if (!resourcesByKey.TryGetValue(
                    ResourceExplanationCatalog.CapabilityKey(identity),
                    out ResourceExplanationResource? resource))
            {
                throw new ArgumentException(
                    $"Capability resource identity '{identity.Identity}' "
                    + "has no Resource Explanation resource.",
                    nameof(catalog));
            }
            if (!resources.TryAdd(identity, resource))
            {
                throw new ArgumentException(
                    $"Capability resource identity "
                    + $"'{identity.Identity}' occurs more than once.",
                    nameof(catalog));
            }
        }
        return resources;
    }

    private static ImmutableArray<Candidate> CreateCandidates(
        InspectionCapabilityCatalog catalog,
        ResourceExplanationCatalog explanationCatalog,
        IReadOnlyDictionary<
            InspectionCapabilityResourceIdentity,
            ResourceExplanationResource> resources)
    {
        var bindingsByResource =
            new Dictionary<
                InspectionCapabilityResourceIdentity,
                HashSet<InspectionConsumerBinding>>();
        var routesByResource =
            new Dictionary<
                InspectionCapabilityResourceIdentity,
                HashSet<InspectionRouteRegistration>>();

        foreach (InspectionConsumerBinding binding in catalog.Bindings)
        {
            AddAvailableResource(
                ResourceExplanationCatalog.ConsumerBindingIdentity(binding),
                binding,
                binding.Route);
            AddAvailableResource(
                ResourceExplanationCatalog.RouteIdentity(binding.Route),
                binding,
                binding.Route);
            AddAvailableResource(
                ResourceExplanationCatalog.DocumentIdentity(
                    binding.Route.Document),
                binding,
                binding.Route);
            AddAvailableResource(
                ResourceExplanationCatalog.QuerySpaceIdentity(
                    binding.Route.QuerySpace),
                binding,
                binding.Route);

            foreach (string exposedTerm in binding.ExposedQueryTerms)
            {
                QuerySpaceOperationTermDescriptor term =
                    binding.Route.QuerySpace.Descriptor.Operation.Terms
                        .Single(candidate =>
                            candidate.Identity == exposedTerm);
                AddAvailableResource(
                    ResourceExplanationCatalog.QueryFacetIdentity(
                        binding.Route.QuerySpace,
                        term),
                    binding,
                    binding.Route);
            }
        }

        return
        [
            .. bindingsByResource.Keys
                .OrderBy(
                    identity => resources.TryGetValue(
                        identity,
                        out ResourceExplanationResource? resource)
                            ? resource.Path!.Value
                            : identity.Identity,
                    StringComparer.Ordinal)
                .Select(identity =>
                {
                    if (!resources.TryGetValue(identity, out var resource))
                    {
                        throw new ArgumentException(
                            $"Available capability resource "
                            + $"'{identity.Identity}' does not have one "
                            + "canonical Resource Explanation path.",
                            nameof(resources));
                    }
                    ResourcePathResolution pathResolution =
                        explanationCatalog.Resolve(resource.Path!.Value);
                    if (pathResolution
                        is not ResourcePathResolution.Resolved resolved
                        || resolved.Key
                            != ResourceExplanationCatalog.CapabilityKey(
                                identity))
                    {
                        throw new ArgumentException(
                            $"Capability resource '{identity.Identity}' "
                            + "does not resolve from its canonical Resource "
                            + "Explanation path to the same identity.",
                            nameof(resources));
                    }

                    CapabilityCatalogSearchRoute[] routes =
                    [
                        .. routesByResource[identity]
                            .OrderBy(
                                static route =>
                                    route.Descriptor.Identity,
                                StringComparer.Ordinal)
                            .Select(route =>
                            {
                                InspectionCapabilityResourceIdentity
                                    routeIdentity =
                                        ResourceExplanationCatalog
                                            .RouteIdentity(route);
                                ResourceExplanationResource routeResource =
                                    resources[routeIdentity];
                                return new CapabilityCatalogSearchRoute(
                                    route.Descriptor.Identity,
                                    route.Descriptor.Name,
                                    routeResource.Path!.Value);
                            }),
                    ];
                    CapabilityCatalogSearchBinding[] bindings =
                    [
                        .. bindingsByResource[identity]
                            .OrderBy(
                                static binding =>
                                    binding.Descriptor.Identity,
                                StringComparer.Ordinal)
                            .Select(binding =>
                            {
                                InspectionCapabilityResourceIdentity
                                    bindingIdentity =
                                        ResourceExplanationCatalog
                                            .ConsumerBindingIdentity(binding);
                                ResourceExplanationResource bindingResource =
                                    resources[bindingIdentity];
                                return new CapabilityCatalogSearchBinding(
                                    binding.Descriptor.Identity,
                                    binding.Descriptor.Owner,
                                    binding.Descriptor.Kind,
                                    binding.Descriptor.Gesture,
                                    bindingResource.Path!.Value);
                            }),
                    ];
                    return Candidate.Create(
                        identity,
                        resource,
                        routes,
                        bindings);
                }),
        ];

        void AddAvailableResource(
            InspectionCapabilityResourceIdentity identity,
            InspectionConsumerBinding binding,
            InspectionRouteRegistration route)
        {
            if (!bindingsByResource.TryGetValue(identity, out var bindings))
            {
                bindings = [];
                bindingsByResource.Add(identity, bindings);
                routesByResource.Add(identity, []);
            }
            bindings.Add(binding);
            routesByResource[identity].Add(route);
        }
    }

    // The admitted inputs are the ones the host's structural explanation
    // catalog is created from, so each candidate path is the path exact
    // explanation resolves. Hosts witness that wiring in tests; search checks
    // only what its own population needs.
    private static IEnumerable<Candidate> CreateStructuralCandidates(
        ImmutableArray<CapabilityCatalogSearchStructuralCatalog> catalogs)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<Candidate>();
        foreach (CapabilityCatalogSearchStructuralCatalog catalog in catalogs)
        {
            string root = catalog.Route.ResourcePath;
            if (!roots.Add(root))
            {
                throw new ArgumentException(
                    $"Structural catalog '{root}' is admitted more than "
                    + "once.",
                    nameof(catalogs));
            }

            var paths = new Dictionary<DiscoveryResourceIdentity, string>();
            foreach (StructuralResourcePathRegistration registration
                     in catalog.Paths)
            {
                if (registration.Identity.Kind
                    is not (DiscoveryResourceKind.Section
                        or DiscoveryResourceKind.Category))
                {
                    continue;
                }
                if (!registration.Path.Value.StartsWith(
                        $"{root}/",
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Structural path '{registration.Path}' is not "
                        + $"under catalog root '{root}'.",
                        nameof(catalogs));
                }
                if (!paths.TryAdd(
                        registration.Identity,
                        registration.Path.Value))
                {
                    throw new ArgumentException(
                        $"Structural resource '{registration.Identity.Name}' "
                        + "has more than one path registration.",
                        nameof(catalogs));
                }
            }

            int admitted = 0;
            var admittedResources = new HashSet<DiscoveryResourceIdentity>();
            foreach (DiscoveryResource resource in catalog.Document.Resources)
            {
                DiscoveryResourceIdentity identity = resource.Identity;
                if (identity.Kind
                    is not (DiscoveryResourceKind.Section
                        or DiscoveryResourceKind.Category))
                {
                    continue;
                }
                if (!paths.TryGetValue(identity, out string? path))
                {
                    throw new ArgumentException(
                        $"Structural resource '{identity.Name}' has no path "
                        + "registration.",
                        nameof(catalogs));
                }
                CapabilityCatalogSearchBinding[] bindings =
                [
                    .. catalog.Bindings
                        .Where(binding => binding.Resources.Contains(identity))
                        .Select(static binding => binding.Binding),
                ];
                if (bindings.Length == 0)
                {
                    throw new ArgumentException(
                        $"Structural resource '{identity.Name}' has no "
                        + "binding.",
                        nameof(catalogs));
                }
                admitted++;
                admittedResources.Add(identity);
                candidates.Add(Candidate.CreateStructural(
                    new CapabilityCatalogSearchStructuralIdentity(
                        root,
                        identity.Kind,
                        identity.Name),
                    path,
                    identity.Kind == DiscoveryResourceKind.Section
                        ? resource.Description?.Summary
                        : null,
                    catalog.Route,
                    bindings));
            }
            if (admitted != paths.Count)
            {
                throw new ArgumentException(
                    $"Catalog '{root}' registers a structural path with no "
                    + "Discovery resource.",
                    nameof(catalogs));
            }
            if (catalog.Bindings.Any(binding =>
                    !binding.Resources.IsSubsetOf(admittedResources)))
            {
                throw new ArgumentException(
                    $"Catalog '{root}' binds a structural resource it does "
                    + "not admit.",
                    nameof(catalogs));
            }
        }

        return candidates.OrderBy(
            static candidate => candidate.Path,
            StringComparer.Ordinal);
    }

    private static ScoredCandidate? Score(
        Candidate candidate,
        string query,
        string normalizedQuery)
    {
        return candidate.Terms
            .Select(term =>
            {
                double similarity = string.Equals(
                        query,
                        term.Value,
                        StringComparison.OrdinalIgnoreCase)
                    ? 1.0
                    : StringDistance.Similarity(
                        normalizedQuery,
                        term.Value.ToUpperInvariant());
                return new ScoredCandidate(
                    candidate,
                    term,
                    similarity);
            })
            .Where(static match =>
                match.Similarity >= SimilarityThreshold)
            .OrderByDescending(static match => match.Similarity)
            .ThenBy(static match => Strength(match.Term.Source))
            .ThenBy(static match => match.Term.IsSegment)
            .ThenBy(
                static match => match.Term.Value,
                StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private sealed record SearchTerm(
        string Value,
        CapabilityCatalogSearchMatchSource Source,
        bool IsSegment);

    private sealed record ScoredCandidate(
        Candidate Candidate,
        SearchTerm Term,
        double Similarity);

    private sealed record Candidate(
        InspectionCapabilityResourceIdentity? ResourceIdentity,
        CapabilityCatalogSearchStructuralIdentity? StructuralIdentity,
        CapabilityCatalogSearchResourceKind Kind,
        string Path,
        string Name,
        ImmutableArray<string> CanonicalKeys,
        ImmutableArray<CapabilityCatalogSearchRoute> Routes,
        ImmutableArray<CapabilityCatalogSearchBinding> Bindings,
        ImmutableArray<SearchTerm> Terms)
    {
        public static Candidate Create(
            InspectionCapabilityResourceIdentity identity,
            ResourceExplanationResource resource,
            IEnumerable<CapabilityCatalogSearchRoute> routes,
            IEnumerable<CapabilityCatalogSearchBinding> bindings)
        {
            var terms =
                new Dictionary<string, SearchTerm>(
                    StringComparer.OrdinalIgnoreCase);
            AddTerms(
                terms,
                identity.Identity,
                CapabilityCatalogSearchMatchSource.OwnerIdentity);

            string name =
                ResourceExplanationVocabulary.RequiredText(
                    resource,
                    "name");
            string? summary = identity.Kind is
                InspectionCapabilityResourceKind.Document
                or InspectionCapabilityResourceKind.Route
                or InspectionCapabilityResourceKind.QueryFacet
                    ? ResourceExplanationVocabulary.RequiredText(
                        resource,
                        "summary")
                    : null;
            ImmutableArray<string> canonicalKeys =
                identity.Kind == InspectionCapabilityResourceKind.QueryFacet
                    ? [
                        ResourceExplanationVocabulary.RequiredText(
                            resource,
                            "key"),
                    ]
                    : [];
            foreach (string key in canonicalKeys)
            {
                AddTerms(
                    terms,
                    key,
                    CapabilityCatalogSearchMatchSource.CanonicalKey);
            }
            if (identity.Kind
                == InspectionCapabilityResourceKind.QueryFacet)
            {
                foreach (string example
                         in ResourceExplanationVocabulary.Texts(
                             resource,
                             "examples"))
                {
                    AddTerms(
                        terms,
                        example,
                        CapabilityCatalogSearchMatchSource.ExampleValue);
                }
            }

            AddTerms(
                terms,
                resource.Path!.Value,
                CapabilityCatalogSearchMatchSource.ResourcePath);
            AddTerms(
                terms,
                name,
                CapabilityCatalogSearchMatchSource.ResourceName);
            if (summary is not null)
            {
                AddTerms(
                    terms,
                    summary,
                    CapabilityCatalogSearchMatchSource.Summary);
            }

            CapabilityCatalogSearchRoute[] routeArray = [.. routes];
            foreach (CapabilityCatalogSearchRoute route in routeArray)
            {
                AddTerms(
                    terms,
                    route.Identity,
                    CapabilityCatalogSearchMatchSource.RelatedRoute);
                AddTerms(
                    terms,
                    route.Name,
                    CapabilityCatalogSearchMatchSource.RelatedRoute);
            }

            CapabilityCatalogSearchBinding[] bindingArray = [.. bindings];
            foreach (CapabilityCatalogSearchBinding binding in bindingArray)
            {
                AddTerms(
                    terms,
                    binding.Identity,
                    CapabilityCatalogSearchMatchSource.ProductionBinding);
                AddTerms(
                    terms,
                    binding.Name,
                    CapabilityCatalogSearchMatchSource.ProductionBinding);
            }

            return new Candidate(
                identity,
                null,
                SearchKind(identity.Kind),
                resource.Path!.Value,
                name,
                canonicalKeys,
                [.. routeArray],
                [.. bindingArray],
                OrderTerms(terms));
        }

        // A structural resource projects its owner-issued name as its
        // identity and resource name, its path, its declared Summary, and its
        // admitted route and bindings. It has no canonical key or example.
        public static Candidate CreateStructural(
            CapabilityCatalogSearchStructuralIdentity identity,
            string path,
            string? summary,
            CapabilityCatalogSearchRoute route,
            IReadOnlyList<CapabilityCatalogSearchBinding> bindings)
        {
            var terms =
                new Dictionary<string, SearchTerm>(
                    StringComparer.OrdinalIgnoreCase);
            AddTerms(
                terms,
                identity.Name,
                CapabilityCatalogSearchMatchSource.OwnerIdentity);
            AddTerms(
                terms,
                path,
                CapabilityCatalogSearchMatchSource.ResourcePath);
            AddTerms(
                terms,
                identity.Name,
                CapabilityCatalogSearchMatchSource.ResourceName);
            if (summary is not null)
            {
                AddTerms(
                    terms,
                    summary,
                    CapabilityCatalogSearchMatchSource.Summary);
            }
            AddTerms(
                terms,
                route.Identity,
                CapabilityCatalogSearchMatchSource.RelatedRoute);
            AddTerms(
                terms,
                route.Name,
                CapabilityCatalogSearchMatchSource.RelatedRoute);
            foreach (CapabilityCatalogSearchBinding binding in bindings)
            {
                AddTerms(
                    terms,
                    binding.Identity,
                    CapabilityCatalogSearchMatchSource.ProductionBinding);
                AddTerms(
                    terms,
                    binding.Name,
                    CapabilityCatalogSearchMatchSource.ProductionBinding);
            }

            return new Candidate(
                null,
                identity,
                identity.Kind == DiscoveryResourceKind.Section
                    ? CapabilityCatalogSearchResourceKind.Section
                    : CapabilityCatalogSearchResourceKind.Category,
                path,
                identity.Name,
                [],
                [route],
                [.. bindings],
                OrderTerms(terms));
        }

        private static ImmutableArray<SearchTerm> OrderTerms(
            Dictionary<string, SearchTerm> terms) =>
        [
            .. terms.Values
                .OrderBy(static term => Strength(term.Source))
                .ThenBy(static term => term.IsSegment)
                .ThenBy(static term => term.Source)
                .ThenBy(
                    static term => term.Value,
                    StringComparer.Ordinal),
        ];

        public CapabilityCatalogSearchResult ToResult(
            double similarity,
            SearchTerm term) =>
            new(
                similarity,
                term.Value,
                term.Source,
                term.IsSegment,
                ResourceIdentity,
                StructuralIdentity,
                Kind,
                Name,
                CanonicalKeys,
                Path,
                Routes,
                Bindings);

        private static void AddTerms(
            Dictionary<string, SearchTerm> terms,
            string value,
            CapabilityCatalogSearchMatchSource source)
        {
            AddTerm(new SearchTerm(value, source, false));
            foreach (string segment in value.Split(
                         SegmentSeparators,
                         StringSplitOptions.RemoveEmptyEntries
                         | StringSplitOptions.TrimEntries))
            {
                AddTerm(new SearchTerm(segment, source, true));
            }

            void AddTerm(SearchTerm candidate)
            {
                if (!terms.TryGetValue(candidate.Value, out SearchTerm? prior))
                {
                    terms[candidate.Value] = candidate;
                    return;
                }

                int candidateStrength = Strength(candidate.Source);
                int priorStrength = Strength(prior.Source);
                if (candidateStrength < priorStrength
                    || (candidateStrength == priorStrength
                        && prior.IsSegment
                        && !candidate.IsSegment)
                    || (candidateStrength == priorStrength
                        && candidate.IsSegment == prior.IsSegment
                        && candidate.Source < prior.Source))
                {
                    terms[candidate.Value] = candidate;
                }
            }
        }
    }

    private static int Strength(
        CapabilityCatalogSearchMatchSource source) =>
        source switch
        {
            CapabilityCatalogSearchMatchSource.CanonicalKey
                or CapabilityCatalogSearchMatchSource.OwnerIdentity => 0,
            CapabilityCatalogSearchMatchSource.ExampleValue => 1,
            CapabilityCatalogSearchMatchSource.ResourcePath => 2,
            CapabilityCatalogSearchMatchSource.ResourceName => 3,
            CapabilityCatalogSearchMatchSource.Summary => 4,
            CapabilityCatalogSearchMatchSource.RelatedRoute
                or CapabilityCatalogSearchMatchSource.ProductionBinding => 5,
            _ => throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                "Unknown capability-search match source."),
        };

    private static CapabilityCatalogSearchResourceKind SearchKind(
        InspectionCapabilityResourceKind kind) =>
        kind switch
        {
            InspectionCapabilityResourceKind.Document =>
                CapabilityCatalogSearchResourceKind.InspectionDocument,
            InspectionCapabilityResourceKind.Route =>
                CapabilityCatalogSearchResourceKind.HostNeutralRoute,
            InspectionCapabilityResourceKind.QuerySpace =>
                CapabilityCatalogSearchResourceKind.QuerySpace,
            InspectionCapabilityResourceKind.QueryFacet =>
                CapabilityCatalogSearchResourceKind.QueryFacet,
            InspectionCapabilityResourceKind.ConsumerBinding =>
                CapabilityCatalogSearchResourceKind.ConsumerBinding,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Capability Catalog Search accepts only searchable "
                + "capability resources."),
        };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CapabilityCatalogSearchDocument))]
[JsonSerializable(
    typeof(InspectionEnvelope<CapabilityCatalogSearchDocument>))]
public partial class CapabilityCatalogSearchJsonContext :
    JsonSerializerContext;
