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
    ResourcePath,
    ResourceName,
    Summary,
    RelatedRoute,
    ProductionBinding,
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

public sealed record CapabilityCatalogSearchResult(
    int Rank,
    double Similarity,
    string MatchedTerm,
    CapabilityCatalogSearchMatchSource MatchSource,
    bool IsSegment,
    InspectionCapabilityResourceIdentity ResourceIdentity,
    ResourceExplanationResourceKind ResourceKind,
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
        CapabilityCatalogSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(capabilityCatalog);
        ArgumentNullException.ThrowIfNull(explanationCatalog);
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyDictionary<
            InspectionCapabilityResourceIdentity,
            ResourceExplanationResource> resources =
                IndexCapabilityResources(explanationCatalog);
        ImmutableArray<Candidate> candidates =
            CreateCandidates(
                capabilityCatalog,
                explanationCatalog,
                resources);
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
                    static match => match.Candidate.Resource.Path.Value,
                    StringComparer.Ordinal)
                .ThenBy(
                    static match => match.Term.Value,
                    StringComparer.Ordinal),
        ];
        ImmutableArray<CapabilityCatalogSearchResult> results =
        [
            .. matches
                .Take(request.MaximumResults)
                .Select((match, index) =>
                    match.Candidate.ToResult(
                        index + 1,
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
            ResourceExplanationCatalog catalog)
    {
        var resources =
            new Dictionary<
                InspectionCapabilityResourceIdentity,
                ResourceExplanationResource>();
        foreach (ResourceExplanationResource resource in catalog.Resources)
        {
            if (resource.Identity
                is not ResourceExplanationIdentity.Capability capability)
            {
                continue;
            }
            if (!resources.TryAdd(capability.Resource, resource))
            {
                throw new ArgumentException(
                    $"Capability resource identity "
                    + $"'{capability.Resource.Identity}' has more than one "
                    + "Resource Explanation path.",
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
                            ? resource.Path.Value
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
                        explanationCatalog.Resolve(resource.Path.Value);
                    if (pathResolution
                        is not ResourcePathResolution.Resolved resolved
                        || resolved.Identity
                            is not ResourceExplanationIdentity.Capability
                                capability
                        || capability.Resource != identity)
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
                                    routeResource.Path.Value);
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
                                    bindingResource.Path.Value);
                            }),
                    ];
                    return Candidate.Create(
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
        ResourceExplanationResource Resource,
        string Name,
        ImmutableArray<string> CanonicalKeys,
        ImmutableArray<CapabilityCatalogSearchRoute> Routes,
        ImmutableArray<CapabilityCatalogSearchBinding> Bindings,
        ImmutableArray<SearchTerm> Terms)
    {
        public static Candidate Create(
            ResourceExplanationResource resource,
            IEnumerable<CapabilityCatalogSearchRoute> routes,
            IEnumerable<CapabilityCatalogSearchBinding> bindings)
        {
            var terms =
                new Dictionary<string, SearchTerm>(
                    StringComparer.OrdinalIgnoreCase);
            InspectionCapabilityResourceIdentity identity =
                ((ResourceExplanationIdentity.Capability)resource.Identity)
                    .Resource;
            AddTerms(
                terms,
                identity.Identity,
                CapabilityCatalogSearchMatchSource.OwnerIdentity);

            string name;
            string summary;
            ImmutableArray<string> canonicalKeys;
            switch (resource.Details)
            {
                case ResourceExplanationDetail.InspectionDocumentDetails
                    details:
                    name = details.Name;
                    summary = details.Summary;
                    canonicalKeys = [];
                    break;
                case ResourceExplanationDetail.HostNeutralRouteDetails
                    details:
                    name = details.Name;
                    summary = details.Summary;
                    canonicalKeys = [];
                    break;
                case ResourceExplanationDetail.QuerySpaceDetails details:
                    name = details.Name;
                    summary = details.Summary;
                    canonicalKeys = [];
                    break;
                case ResourceExplanationDetail.QueryFacetDetails details:
                    name = details.Name;
                    summary = details.Summary;
                    canonicalKeys = [details.Key];
                    AddTerms(
                        terms,
                        details.Key,
                        CapabilityCatalogSearchMatchSource.CanonicalKey);
                    break;
                case ResourceExplanationDetail.ConsumerBindingDetails
                    details:
                    name = details.Name;
                    summary = details.Summary;
                    canonicalKeys = [];
                    break;
                default:
                    throw new ArgumentException(
                        "Capability Catalog Search accepts only capability "
                        + "resources.",
                        nameof(resource));
            }

            AddTerms(
                terms,
                resource.Path.Value,
                CapabilityCatalogSearchMatchSource.ResourcePath);
            AddTerms(
                terms,
                name,
                CapabilityCatalogSearchMatchSource.ResourceName);
            AddTerms(
                terms,
                summary,
                CapabilityCatalogSearchMatchSource.Summary);

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
                resource,
                name,
                canonicalKeys,
                [.. routeArray],
                [.. bindingArray],
                [
                    .. terms.Values
                        .OrderBy(static term => Strength(term.Source))
                        .ThenBy(static term => term.IsSegment)
                        .ThenBy(static term => term.Source)
                        .ThenBy(
                            static term => term.Value,
                            StringComparer.Ordinal),
                ]);
        }

        public CapabilityCatalogSearchResult ToResult(
            int rank,
            double similarity,
            SearchTerm term) =>
            new(
                rank,
                similarity,
                term.Value,
                term.Source,
                term.IsSegment,
                ((ResourceExplanationIdentity.Capability)Resource.Identity)
                    .Resource,
                Resource.ResourceKind,
                Name,
                CanonicalKeys,
                Resource.Path.Value,
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
                if (!terms.TryGetValue(candidate.Value, out SearchTerm? prior)
                    || Strength(candidate.Source) < Strength(prior.Source)
                    || (Strength(candidate.Source) == Strength(prior.Source)
                        && candidate.Source < prior.Source)
                    || (candidate.Source == prior.Source
                        && prior.IsSegment
                        && !candidate.IsSegment))
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
            CapabilityCatalogSearchMatchSource.ResourcePath => 1,
            CapabilityCatalogSearchMatchSource.ResourceName => 2,
            CapabilityCatalogSearchMatchSource.Summary => 3,
            CapabilityCatalogSearchMatchSource.RelatedRoute
                or CapabilityCatalogSearchMatchSource.ProductionBinding => 4,
            _ => throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                "Unknown capability-search match source."),
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
