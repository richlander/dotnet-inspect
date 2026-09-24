using System.Collections.Immutable;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public enum InspectionConsumerKind
{
    Cli,
    Browser,
    OperationBackedSection,
}

public sealed record InspectionDocumentDescriptor
{
    public InspectionDocumentDescriptor(
        string identity,
        string name,
        string summary,
        string resultContract)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultContract);
        Identity = identity;
        Name = name;
        Summary = summary;
        ResultContract = resultContract;
    }

    public string Identity { get; }

    public string Name { get; }

    public string Summary { get; }

    public string ResultContract { get; }
}

public abstract class InspectionDocumentRegistration
{
    private protected InspectionDocumentRegistration(
        InspectionDocumentDescriptor descriptor)
    {
        Descriptor = descriptor
            ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public InspectionDocumentDescriptor Descriptor { get; }
}

public sealed class InspectionDocumentRegistration<TContent> :
    InspectionDocumentRegistration
{
    public InspectionDocumentRegistration(
        InspectionDocumentDescriptor descriptor)
        : base(descriptor)
    {
    }
}

public sealed record InspectionRouteDescriptor
{
    public InspectionRouteDescriptor(
        string identity,
        string name,
        string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Identity = identity;
        Name = name;
        Summary = summary;
    }

    public string Identity { get; }

    public string Name { get; }

    public string Summary { get; }
}

public enum InspectionQueryTermRelationshipKind
{
    RequiredContext,
}

public sealed record InspectionQueryTermRelationship
{
    public InspectionQueryTermRelationship(
        InspectionQueryTermRelationshipKind kind,
        string sourceTerm,
        string targetTerm)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTerm);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTerm);
        if (string.Equals(
                sourceTerm,
                targetTerm,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A query-term relationship must target another term.",
                nameof(targetTerm));
        }
        Kind = kind;
        SourceTerm = sourceTerm;
        TargetTerm = targetTerm;
    }

    public InspectionQueryTermRelationshipKind Kind { get; }

    public string SourceTerm { get; }

    public string TargetTerm { get; }
}

public abstract class InspectionRouteRegistration
{
    private protected InspectionRouteRegistration(
        InspectionRouteDescriptor descriptor,
        InspectionDocumentRegistration document,
        QuerySpaceBinding querySpace,
        IEnumerable<InspectionQueryTermRelationship>?
            queryTermRelationships)
    {
        Descriptor = descriptor
            ?? throw new ArgumentNullException(nameof(descriptor));
        Document = document
            ?? throw new ArgumentNullException(nameof(document));
        QuerySpace = querySpace
            ?? throw new ArgumentNullException(nameof(querySpace));

        QuerySpaceResultContractDescriptor? rowsContract =
            querySpace.Descriptor.ResultContracts.SingleOrDefault(
                static contract =>
                    contract.Terminal
                    == QuerySpaceTerminalRequirement.Rows);
        if (rowsContract is null
            || !string.Equals(
                rowsContract.Identity,
                document.Descriptor.ResultContract,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Inspection route '{descriptor.Identity}' must expose the "
                + $"document result contract "
                + $"'{document.Descriptor.ResultContract}' for Rows.",
                nameof(querySpace));
        }

        QueryTermRelationships =
        [
            .. queryTermRelationships ?? [],
        ];
        HashSet<string> queryTerms =
            querySpace.Descriptor.Operation.Terms
                .Select(static term => term.Identity)
                .ToHashSet(StringComparer.Ordinal);
        foreach (InspectionQueryTermRelationship relationship
                 in QueryTermRelationships)
        {
            if (!queryTerms.Contains(relationship.SourceTerm)
                || !queryTerms.Contains(relationship.TargetTerm))
            {
                throw new ArgumentException(
                    $"Inspection route '{descriptor.Identity}' declares a "
                    + "query-term relationship outside its effective Query "
                    + "Space.",
                    nameof(queryTermRelationships));
            }
        }
        if (QueryTermRelationships.Distinct().Count()
            != QueryTermRelationships.Length)
        {
            throw new ArgumentException(
                "Inspection route query-term relationships must be unique.",
                nameof(queryTermRelationships));
        }
    }

    public InspectionRouteDescriptor Descriptor { get; }

    public InspectionDocumentRegistration Document { get; }

    public QuerySpaceBinding QuerySpace { get; }

    public ImmutableArray<InspectionQueryTermRelationship>
        QueryTermRelationships { get; }
}

public sealed class InspectionRouteRegistration<TRequest, TContent> :
    InspectionRouteRegistration
    where TRequest : class
{
    private readonly Func<
        TRequest,
        CancellationToken,
        ValueTask<InspectionEnvelope<TContent>>> _execute;

    public InspectionRouteRegistration(
        InspectionRouteDescriptor descriptor,
        InspectionDocumentRegistration<TContent> document,
        QuerySpaceBinding querySpace,
        Func<
            TRequest,
            CancellationToken,
            ValueTask<InspectionEnvelope<TContent>>> execute,
        IEnumerable<InspectionQueryTermRelationship>?
            queryTermRelationships = null)
        : base(
            descriptor,
            document,
            querySpace,
            queryTermRelationships)
    {
        _execute = execute
            ?? throw new ArgumentNullException(nameof(execute));
    }

    public new InspectionDocumentRegistration<TContent> Document =>
        (InspectionDocumentRegistration<TContent>)base.Document;

    public ValueTask<InspectionEnvelope<TContent>> ExecuteAsync(
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _execute(request, cancellationToken);
    }
}

public sealed record InspectionConsumerDescriptor
{
    public InspectionConsumerDescriptor(
        string identity,
        InspectionConsumerKind kind,
        string owner,
        string gesture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(gesture);
        Identity = identity;
        Kind = kind;
        Owner = owner;
        Gesture = gesture;
    }

    public string Identity { get; }

    public InspectionConsumerKind Kind { get; }

    public string Owner { get; }

    public string Gesture { get; }
}

public abstract class InspectionConsumerBinding
{
    private protected InspectionConsumerBinding(
        InspectionConsumerDescriptor descriptor,
        InspectionRouteRegistration route,
        IEnumerable<string> exposedQueryTerms)
    {
        Descriptor = descriptor
            ?? throw new ArgumentNullException(nameof(descriptor));
        Route = route
            ?? throw new ArgumentNullException(nameof(route));
        ArgumentNullException.ThrowIfNull(exposedQueryTerms);
        ExposedQueryTerms =
        [
            .. exposedQueryTerms
                .Select(static identity =>
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(identity);
                    return identity;
                }),
        ];
        if (ExposedQueryTerms.Distinct(StringComparer.Ordinal).Count()
            != ExposedQueryTerms.Length)
        {
            throw new ArgumentException(
                "A consumer binding cannot expose the same query term more "
                + "than once.",
                nameof(exposedQueryTerms));
        }
    }

    public InspectionConsumerDescriptor Descriptor { get; }

    public InspectionRouteRegistration Route { get; }

    public ImmutableArray<string> ExposedQueryTerms { get; }
}

public sealed class InspectionConsumerBinding<TRequest, TContent> :
    InspectionConsumerBinding
    where TRequest : class
{
    public InspectionConsumerBinding(
        InspectionConsumerDescriptor descriptor,
        InspectionRouteRegistration<TRequest, TContent> route,
        IEnumerable<string> exposedQueryTerms)
        : base(descriptor, route, exposedQueryTerms)
    {
    }

    public new InspectionRouteRegistration<TRequest, TContent> Route =>
        (InspectionRouteRegistration<TRequest, TContent>)base.Route;

    public ValueTask<InspectionEnvelope<TContent>> ExecuteAsync(
        TRequest request,
        CancellationToken cancellationToken = default) =>
        Route.ExecuteAsync(request, cancellationToken);
}

public sealed record InspectionProductionAdoptionRequirement
{
    public InspectionProductionAdoptionRequirement(
        InspectionRouteRegistration route,
        InspectionConsumerKind consumerKind)
    {
        Route = route ?? throw new ArgumentNullException(nameof(route));
        if (!Enum.IsDefined(consumerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(consumerKind));
        }
        ConsumerKind = consumerKind;
    }

    public InspectionRouteRegistration Route { get; }

    public InspectionConsumerKind ConsumerKind { get; }
}

public sealed record InspectionCapabilityModule
{
    public InspectionCapabilityModule(
        string identity,
        IEnumerable<InspectionDocumentRegistration>? documents = null,
        IEnumerable<InspectionRouteRegistration>? routes = null,
        IEnumerable<InspectionConsumerBinding>? bindings = null,
        IEnumerable<InspectionProductionAdoptionRequirement>?
            adoptionRequirements = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Identity = identity;
        Documents = [.. documents ?? []];
        Routes = [.. routes ?? []];
        Bindings = [.. bindings ?? []];
        AdoptionRequirements = [.. adoptionRequirements ?? []];
    }

    public string Identity { get; }

    public ImmutableArray<InspectionDocumentRegistration> Documents { get; }

    public ImmutableArray<InspectionRouteRegistration> Routes { get; }

    public ImmutableArray<InspectionConsumerBinding> Bindings { get; }

    public ImmutableArray<InspectionProductionAdoptionRequirement>
        AdoptionRequirements { get; }
}

public sealed record InspectionProductionAdoptionGap(
    InspectionRouteRegistration Route,
    InspectionConsumerKind ConsumerKind);

public sealed class InspectionCapabilityCatalog
{
    private InspectionCapabilityCatalog(
        ImmutableArray<InspectionCapabilityModule> modules,
        ImmutableArray<InspectionDocumentRegistration> documents,
        ImmutableArray<InspectionRouteRegistration> routes,
        ImmutableArray<InspectionConsumerBinding> bindings,
        ImmutableArray<InspectionProductionAdoptionGap> adoptionGaps)
    {
        Modules = modules;
        Documents = documents;
        Routes = routes;
        Bindings = bindings;
        AdoptionGaps = adoptionGaps;
    }

    public ImmutableArray<InspectionCapabilityModule> Modules { get; }

    public ImmutableArray<InspectionDocumentRegistration> Documents { get; }

    public ImmutableArray<InspectionRouteRegistration> Routes { get; }

    public ImmutableArray<InspectionConsumerBinding> Bindings { get; }

    public ImmutableArray<InspectionProductionAdoptionGap> AdoptionGaps
    {
        get;
    }

    public static InspectionCapabilityCatalog Create(
        IEnumerable<InspectionCapabilityModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ImmutableArray<InspectionCapabilityModule> moduleArray =
        [
            .. modules.OrderBy(
                static module => module.Identity,
                StringComparer.Ordinal),
        ];
        EnsureUnique(
            moduleArray,
            static module => module.Identity,
            "module");

        ImmutableArray<InspectionDocumentRegistration> documents =
        [
            .. moduleArray
                .SelectMany(static module => module.Documents)
                .OrderBy(
                    static document => document.Descriptor.Identity,
                    StringComparer.Ordinal),
        ];
        EnsureUnique(
            documents,
            static document => document.Descriptor.Identity,
            "document");

        ImmutableArray<InspectionRouteRegistration> routes =
        [
            .. moduleArray
                .SelectMany(static module => module.Routes)
                .OrderBy(
                    static route => route.Descriptor.Identity,
                    StringComparer.Ordinal),
        ];
        EnsureUnique(
            routes,
            static route => route.Descriptor.Identity,
            "route");
        var documentSet =
            documents.ToHashSet(
                ReferenceEqualityComparer.Instance);
        foreach (InspectionRouteRegistration route in routes)
        {
            if (!documentSet.Contains(route.Document))
            {
                throw new ArgumentException(
                    $"Inspection route '{route.Descriptor.Identity}' "
                    + "references a document registration that is not "
                    + "present in the selected modules.",
                    nameof(modules));
            }
        }
        foreach (IGrouping<string, InspectionRouteRegistration> group
                 in routes.GroupBy(
                     static route =>
                         route.QuerySpace.Descriptor.Identity,
                     StringComparer.Ordinal))
        {
            QuerySpaceBinding first = group.First().QuerySpace;
            if (group.Any(route =>
                    !ReferenceEquals(route.QuerySpace, first)))
            {
                throw new ArgumentException(
                    $"Query Space identity '{group.Key}' is registered by "
                    + "more than one executable binding.",
                    nameof(modules));
            }
        }

        ImmutableArray<InspectionConsumerBinding> bindings =
        [
            .. moduleArray
                .SelectMany(static module => module.Bindings)
                .OrderBy(
                    static binding => binding.Descriptor.Identity,
                    StringComparer.Ordinal),
        ];
        EnsureUnique(
            bindings,
            static binding => binding.Descriptor.Identity,
            "consumer binding");
        var routeSet =
            routes.ToHashSet(
                ReferenceEqualityComparer.Instance);
        foreach (InspectionConsumerBinding binding in bindings)
        {
            if (!routeSet.Contains(binding.Route))
            {
                throw new ArgumentException(
                    $"Inspection consumer binding "
                    + $"'{binding.Descriptor.Identity}' references a route "
                    + "registration that is not present in the selected "
                    + "modules.",
                    nameof(modules));
            }

            HashSet<string> routeTerms =
                binding.Route.QuerySpace.Descriptor.Operation.Terms
                    .Select(static term => term.Identity)
                    .ToHashSet(StringComparer.Ordinal);
            foreach (string exposedTerm in binding.ExposedQueryTerms)
            {
                if (!routeTerms.Contains(exposedTerm))
                {
                    throw new ArgumentException(
                        $"Inspection consumer binding "
                        + $"'{binding.Descriptor.Identity}' exposes query "
                        + $"term '{exposedTerm}' outside route "
                        + $"'{binding.Route.Descriptor.Identity}'.",
                        nameof(modules));
                }
            }
        }

        ImmutableArray<InspectionProductionAdoptionRequirement>
            requirements =
        [
            .. moduleArray.SelectMany(
                static module => module.AdoptionRequirements),
        ];
        foreach (InspectionProductionAdoptionRequirement requirement
                 in requirements)
        {
            if (!routeSet.Contains(requirement.Route))
            {
                throw new ArgumentException(
                    $"The production-adoption requirement for route "
                    + $"'{requirement.Route.Descriptor.Identity}' references "
                    + "a route registration that is not present in the "
                    + "selected modules.",
                    nameof(modules));
            }
        }

        ImmutableArray<InspectionProductionAdoptionGap> gaps =
        [
            .. requirements
                .Where(requirement =>
                    !bindings.Any(binding =>
                        ReferenceEquals(
                            binding.Route,
                            requirement.Route)
                        && binding.Descriptor.Kind
                            == requirement.ConsumerKind))
                .OrderBy(
                    static requirement =>
                        requirement.Route.Descriptor.Identity,
                    StringComparer.Ordinal)
                .ThenBy(
                    static requirement => requirement.ConsumerKind)
                .Select(static requirement =>
                    new InspectionProductionAdoptionGap(
                        requirement.Route,
                        requirement.ConsumerKind)),
        ];

        return new(
            moduleArray,
            documents,
            routes,
            bindings,
            gaps);
    }

    private static void EnsureUnique<T>(
        IEnumerable<T> values,
        Func<T, string> identity,
        string kind)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (T value in values)
        {
            string candidate = identity(value);
            if (!identities.Add(candidate))
            {
                throw new ArgumentException(
                    $"Duplicate inspection capability {kind} identity "
                    + $"'{candidate}'.",
                    nameof(values));
            }
        }
    }
}
