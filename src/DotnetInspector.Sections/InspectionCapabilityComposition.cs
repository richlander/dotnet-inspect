using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(JsonStringEnumConverter<InspectionConsumerKind>))]
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

/// <summary>
/// How a host-neutral route executes. A Query Space route executes through its
/// Query Space binding and carries the Query Space gates; an inspection route
/// executes its typed entry point directly.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<InspectionRouteExecution>))]
public enum InspectionRouteExecution
{
    QuerySpace,
    Inspection,
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

public sealed record InspectionQueryTermRelatedOperation
{
    public InspectionQueryTermRelatedOperation(
        string sourceTerm,
        RelatedOperationAffordance operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTerm);
        SourceTerm = sourceTerm;
        Operation = operation
            ?? throw new ArgumentNullException(nameof(operation));
    }

    public string SourceTerm { get; }

    public RelatedOperationAffordance Operation { get; }
}

public sealed record InspectionQueryTermOption
{
    public InspectionQueryTermOption(
        string value,
        string label,
        string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Value = value;
        Label = label;
        Summary = summary;
    }

    public string Value { get; }

    public string Label { get; }

    public string Summary { get; }
}

public sealed record InspectionQueryTermPresentation
{
    public InspectionQueryTermPresentation(
        string sourceTerm,
        int order,
        string controlKind,
        string presentationKind,
        string executionClass,
        IEnumerable<InspectionQueryTermOption>? options = null,
        string? selectionGroup = null,
        bool combinesWithinSelectionGroup = false,
        string? replacementGroup = null,
        string? optionSource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTerm);
        if (order < 0)
            throw new ArgumentOutOfRangeException(nameof(order));
        ArgumentException.ThrowIfNullOrWhiteSpace(controlKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionClass);
        if (selectionGroup is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(selectionGroup);
        if (replacementGroup is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(replacementGroup);
        if (optionSource is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(optionSource);

        SourceTerm = sourceTerm;
        Order = order;
        ControlKind = controlKind;
        PresentationKind = presentationKind;
        ExecutionClass = executionClass;
        Options = [.. options ?? []];
        SelectionGroup = selectionGroup;
        CombinesWithinSelectionGroup = combinesWithinSelectionGroup;
        ReplacementGroup = replacementGroup;
        OptionSource = optionSource;
        if (Options.Select(static option => option.Value)
                .Distinct(StringComparer.Ordinal)
                .Count()
            != Options.Length)
        {
            throw new ArgumentException(
                "Query-term presentation option values must be unique.",
                nameof(options));
        }
        if (combinesWithinSelectionGroup && selectionGroup is null)
        {
            throw new ArgumentException(
                "A combining query-term presentation requires a selection "
                + "group.",
                nameof(combinesWithinSelectionGroup));
        }
        if (optionSource is not null && Options.Length > 0)
        {
            throw new ArgumentException(
                "A query-term presentation cannot declare both inline options "
                + "and an external option source.",
                nameof(optionSource));
        }
    }

    public string SourceTerm { get; }

    public int Order { get; }

    public string ControlKind { get; }

    public string PresentationKind { get; }

    public string ExecutionClass { get; }

    public ImmutableArray<InspectionQueryTermOption> Options { get; }

    public string? SelectionGroup { get; }

    public bool CombinesWithinSelectionGroup { get; }

    public string? ReplacementGroup { get; }

    public string? OptionSource { get; }
}

public abstract class InspectionRouteRegistration
{
    private protected InspectionRouteRegistration(
        InspectionRouteDescriptor descriptor,
        InspectionDocumentRegistration document,
        QuerySpaceBinding? querySpace,
        IEnumerable<InspectionQueryTermRelationship>?
            queryTermRelationships,
        IEnumerable<InspectionQueryTermRelatedOperation>?
            queryTermRelatedOperations,
        IEnumerable<InspectionQueryTermPresentation>?
            queryTermPresentations,
        AnalysisOperationDefinition? analysisOperation = null)
    {
        Descriptor = descriptor
            ?? throw new ArgumentNullException(nameof(descriptor));
        Document = document
            ?? throw new ArgumentNullException(nameof(document));
        QuerySpace = querySpace;
        AnalysisOperation = analysisOperation;
        QueryTermRelationships =
        [
            .. queryTermRelationships ?? [],
        ];
        QueryTermRelatedOperations =
        [
            .. queryTermRelatedOperations ?? [],
        ];
        QueryTermPresentations =
        [
            .. queryTermPresentations ?? [],
        ];
        if (querySpace is null)
        {
            if (!QueryTermRelationships.IsEmpty
                || !QueryTermRelatedOperations.IsEmpty
                || !QueryTermPresentations.IsEmpty)
            {
                throw new ArgumentException(
                    $"Inspection route '{descriptor.Identity}' has no Query "
                    + "Space, so it cannot declare query-term relationships, "
                    + "related operations, or presentations.",
                    nameof(querySpace));
            }
            return;
        }

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
        foreach (InspectionQueryTermRelatedOperation relatedOperation
                 in QueryTermRelatedOperations)
        {
            if (!queryTerms.Contains(relatedOperation.SourceTerm))
            {
                throw new ArgumentException(
                    $"Inspection route '{descriptor.Identity}' declares a "
                    + "query-term related operation outside its effective "
                    + "Query Space.",
                    nameof(queryTermRelatedOperations));
            }
        }
        foreach (InspectionQueryTermPresentation presentation
                 in QueryTermPresentations)
        {
            if (!queryTerms.Contains(presentation.SourceTerm))
            {
                throw new ArgumentException(
                    $"Inspection route '{descriptor.Identity}' declares "
                    + "query-term presentation outside its effective Query "
                    + "Space.",
                    nameof(queryTermPresentations));
            }
            QuerySpaceOperationTermDescriptor term =
                querySpace.Descriptor.Operation.Terms.Single(candidate =>
                    candidate.Identity == presentation.SourceTerm);
            if (!presentation.Options.Select(static option => option.Value)
                    .SequenceEqual(term.Values, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Inspection route '{descriptor.Identity}' query-term "
                    + $"presentation for '{term.Key}' disagrees with the "
                    + "effective Query Space values.",
                    nameof(queryTermPresentations));
            }
        }
        if (QueryTermRelatedOperations
                .Select(static relationship =>
                    (
                        relationship.SourceTerm,
                        relationship.Operation.Id.Value))
                .Distinct()
                .Count()
            != QueryTermRelatedOperations.Length)
        {
            throw new ArgumentException(
                "Inspection route query-term related-operation identities "
                + "must be unique.",
                nameof(queryTermRelatedOperations));
        }
        if (QueryTermRelationships.Distinct().Count()
            != QueryTermRelationships.Length)
        {
            throw new ArgumentException(
                "Inspection route query-term relationships must be unique.",
                nameof(queryTermRelationships));
        }
        if (QueryTermPresentations
                .Select(static presentation => presentation.SourceTerm)
                .Distinct(StringComparer.Ordinal)
                .Count()
            != QueryTermPresentations.Length)
        {
            throw new ArgumentException(
                "Inspection route query-term presentation identities must "
                + "be unique.",
                nameof(queryTermPresentations));
        }
    }

    public InspectionRouteDescriptor Descriptor { get; }

    public InspectionDocumentRegistration Document { get; }

    /// <summary>The route's Query Space binding; null for an inspection route.</summary>
    public QuerySpaceBinding? QuerySpace { get; }

    /// <summary>
    /// The analysis operation this route runs, whose registered analyses are
    /// the route's resources; null when the route runs no analysis set.
    /// </summary>
    public AnalysisOperationDefinition? AnalysisOperation { get; }

    public InspectionRouteExecution Execution =>
        QuerySpace is null
            ? InspectionRouteExecution.Inspection
            : InspectionRouteExecution.QuerySpace;

    public ImmutableArray<InspectionQueryTermRelationship>
        QueryTermRelationships { get; }

    public ImmutableArray<InspectionQueryTermRelatedOperation>
        QueryTermRelatedOperations { get; }

    public ImmutableArray<InspectionQueryTermPresentation>
        QueryTermPresentations { get; }
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
            queryTermRelationships = null,
        IEnumerable<InspectionQueryTermRelatedOperation>?
            queryTermRelatedOperations = null,
        IEnumerable<InspectionQueryTermPresentation>?
            queryTermPresentations = null)
        : base(
            descriptor,
            document,
            querySpace
                ?? throw new ArgumentNullException(nameof(querySpace)),
            queryTermRelationships,
            queryTermRelatedOperations,
            queryTermPresentations)
    {
        _execute = execute
            ?? throw new ArgumentNullException(nameof(execute));
    }

    /// <summary>
    /// An inspection route: typed execution without a Query Space, optionally
    /// running one analysis operation.
    /// </summary>
    public InspectionRouteRegistration(
        InspectionRouteDescriptor descriptor,
        InspectionDocumentRegistration<TContent> document,
        Func<
            TRequest,
            CancellationToken,
            ValueTask<InspectionEnvelope<TContent>>> execute,
        AnalysisOperationDefinition? analysisOperation = null)
        : base(descriptor, document, null, null, null, null, analysisOperation)
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
        IEnumerable<string> exposedQueryTerms,
        IEnumerable<string>? inputRules = null,
        IEnumerable<string>? exposedAnalyses = null)
    {
        Descriptor = descriptor
            ?? throw new ArgumentNullException(nameof(descriptor));
        Route = route
            ?? throw new ArgumentNullException(nameof(route));
        InputRules = [.. (inputRules ?? []).Select(rule =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rule);
            return rule;
        })];
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
        ExposedAnalyses =
        [
            .. (exposedAnalyses ?? [])
                .Select(static identity =>
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(identity);
                    return identity;
                }),
        ];
        if (ExposedAnalyses.Distinct(StringComparer.Ordinal).Count()
            != ExposedAnalyses.Length)
        {
            throw new ArgumentException(
                "A consumer binding cannot expose the same analysis more "
                + "than once.",
                nameof(exposedAnalyses));
        }
    }

    public InspectionConsumerDescriptor Descriptor { get; }

    public InspectionRouteRegistration Route { get; }

    public ImmutableArray<string> ExposedQueryTerms { get; }

    /// <summary>
    /// The analysis identities this binding selects for its route's analysis
    /// operation, in product order; empty when it selects none.
    /// </summary>
    public ImmutableArray<string> ExposedAnalyses { get; }

    /// <summary>Consumer-owned input lowering rules; empty means unregistered.</summary>
    public ImmutableArray<string> InputRules { get; }
}

public sealed class InspectionConsumerBinding<TRequest, TContent> :
    InspectionConsumerBinding
    where TRequest : class
{
    public InspectionConsumerBinding(
        InspectionConsumerDescriptor descriptor,
        InspectionRouteRegistration<TRequest, TContent> route,
        IEnumerable<string> exposedQueryTerms,
        IEnumerable<string>? inputRules = null,
        IEnumerable<string>? exposedAnalyses = null)
        : base(descriptor, route, exposedQueryTerms, inputRules, exposedAnalyses)
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

/// <summary>
/// One typed producer bound to one report surface of one operation
/// participation. The operation dispatches this binding; it is never a
/// delivery path.
/// </summary>
public abstract class InspectionAnalysisProducerBinding
{
    private protected InspectionAnalysisProducerBinding(
        AnalysisOperationKind operation,
        AnalysisSurfaceParticipation participation)
    {
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        Operation = operation;
        Participation = participation
            ?? throw new ArgumentNullException(nameof(participation));
    }

    public AnalysisOperationKind Operation { get; }

    public AnalysisSurfaceParticipation Participation { get; }

    /// <summary>The producer's declared input type.</summary>
    public abstract Type InputType { get; }

    /// <summary>The producer's declared result type.</summary>
    public abstract Type ResultType { get; }
}

public sealed class InspectionAnalysisProducerBinding<TInput, TResult> :
    InspectionAnalysisProducerBinding
{
    private readonly Func<TInput, TResult> _produce;

    public InspectionAnalysisProducerBinding(
        AnalysisOperationKind operation,
        AnalysisSurfaceParticipation participation,
        Func<TInput, TResult> produce)
        : base(operation, participation)
    {
        _produce = produce ?? throw new ArgumentNullException(nameof(produce));
    }

    public override Type InputType => typeof(TInput);

    public override Type ResultType => typeof(TResult);

    public TResult Produce(TInput input) => _produce(input);
}

/// <summary>
/// One analysis participation registration: the owner-issued analysis
/// descriptor and exactly one typed producer binding for every report
/// surface of every operation it declares. Discovery and dispatch read the
/// same registration.
/// </summary>
public sealed class InspectionAnalysisRegistration
{
    public InspectionAnalysisRegistration(
        AnalysisDescriptor analysis,
        IEnumerable<InspectionAnalysisProducerBinding> producers)
    {
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        ArgumentNullException.ThrowIfNull(producers);
        Producers = [.. producers];
        if (Producers.Any(static producer => producer is null))
            throw new ArgumentException("The collection cannot contain null.", nameof(producers));
        if (analysis.Participations.IsEmpty)
        {
            throw new ArgumentException(
                $"Analysis '{analysis.Id.Value}' declares no operation participation.",
                nameof(analysis));
        }

        var declared = analysis.Participations
            .SelectMany(static participation => participation.Surfaces
                .Select(surface => (participation.Operation, Surface: surface)))
            .ToArray();
        foreach (var (operation, surface) in declared)
        {
            if (Producers.Count(producer =>
                    producer.Operation == operation
                    && ReferenceEquals(producer.Participation, surface)) != 1)
            {
                throw new ArgumentException(
                    $"Analysis '{analysis.Id.Value}' must bind exactly one producer "
                    + $"for {operation} at the {surface.Surface} surface.",
                    nameof(producers));
            }
        }
        if (Producers.Length != declared.Length)
        {
            throw new ArgumentException(
                $"Analysis '{analysis.Id.Value}' binds a producer for an "
                + "undeclared participation.",
                nameof(producers));
        }
    }

    public AnalysisDescriptor Analysis { get; }

    public ImmutableArray<InspectionAnalysisProducerBinding> Producers { get; }

    public InspectionAnalysisProducerBinding? ProducerFor(
        AnalysisOperationKind operation,
        AnalysisReportSurfaceKind surface)
        => Producers.FirstOrDefault(producer =>
            producer.Operation == operation
            && producer.Participation.Surface == surface);
}

public sealed record InspectionCapabilityModule
{
    public InspectionCapabilityModule(
        string identity,
        IEnumerable<InspectionDocumentRegistration>? documents = null,
        IEnumerable<InspectionRouteRegistration>? routes = null,
        IEnumerable<InspectionConsumerBinding>? bindings = null,
        IEnumerable<InspectionProductionAdoptionRequirement>?
            adoptionRequirements = null,
        IEnumerable<InspectionAnalysisRegistration>? analyses = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Identity = identity;
        Documents = [.. documents ?? []];
        Routes = [.. routes ?? []];
        Bindings = [.. bindings ?? []];
        AdoptionRequirements = [.. adoptionRequirements ?? []];
        Analyses = [.. analyses ?? []];
    }

    public string Identity { get; }

    public ImmutableArray<InspectionDocumentRegistration> Documents { get; }

    public ImmutableArray<InspectionRouteRegistration> Routes { get; }

    public ImmutableArray<InspectionConsumerBinding> Bindings { get; }

    public ImmutableArray<InspectionProductionAdoptionRequirement>
        AdoptionRequirements { get; }

    /// <summary>Analysis participation registrations, in product order.</summary>
    public ImmutableArray<InspectionAnalysisRegistration> Analyses { get; }
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
        ImmutableArray<InspectionProductionAdoptionGap> adoptionGaps,
        ImmutableArray<InspectionAnalysisRegistration> analyses,
        AnalysisCapabilityCatalog analysisCapabilities)
    {
        Modules = modules;
        Documents = documents;
        Routes = routes;
        Bindings = bindings;
        AdoptionGaps = adoptionGaps;
        Analyses = analyses;
        AnalysisCapabilities = analysisCapabilities;
    }

    public ImmutableArray<InspectionCapabilityModule> Modules { get; }

    public ImmutableArray<InspectionDocumentRegistration> Documents { get; }

    public ImmutableArray<InspectionRouteRegistration> Routes { get; }

    public ImmutableArray<InspectionConsumerBinding> Bindings { get; }

    public ImmutableArray<InspectionProductionAdoptionGap> AdoptionGaps
    {
        get;
    }

    /// <summary>
    /// Registered analyses in product order. The same registrations serve
    /// explanation and operation dispatch.
    /// </summary>
    public ImmutableArray<InspectionAnalysisRegistration> Analyses { get; }

    /// <summary>
    /// The registered analyses a route's analysis operation can dispatch, in
    /// product order: the route's analysis resources.
    /// </summary>
    public ImmutableArray<InspectionAnalysisRegistration> AnalysesOf(
        InspectionRouteRegistration route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route.AnalysisOperation is { } operation
            ? [
                .. Analyses.Where(registration =>
                    operation.CanDispatch(registration.Analysis)),
            ]
            : [];
    }

    /// <summary>
    /// The host-neutral analysis capability catalog over exactly the
    /// registered descriptors; it owns identity and set validation.
    /// </summary>
    public AnalysisCapabilityCatalog AnalysisCapabilities { get; }

    public InspectionAnalysisRegistration? FindAnalysis(
        AnalysisDescriptor analysis)
        => Analyses.FirstOrDefault(registration =>
            ReferenceEquals(registration.Analysis, analysis));

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
                 in routes
                     .Where(static route => route.QuerySpace is not null)
                     .GroupBy(
                         static route =>
                             route.QuerySpace!.Descriptor.Identity,
                         StringComparer.Ordinal))
        {
            QuerySpaceBinding first = group.First().QuerySpace!;
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
                binding.Route.QuerySpace?.Descriptor.Operation.Terms
                    .Select(static term => term.Identity)
                    .ToHashSet(StringComparer.Ordinal)
                ?? [];
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

        ImmutableArray<InspectionAnalysisRegistration> analyses =
        [
            .. moduleArray.SelectMany(static module => module.Analyses),
        ];
        EnsureUnique(
            analyses,
            static analysis => analysis.Analysis.Id.Value,
            "analysis");
        var analysisCapabilities = new AnalysisCapabilityCatalog(
            analyses.Select(static analysis => analysis.Analysis));
        foreach (InspectionRouteRegistration route in routes)
        {
            if (route.AnalysisOperation is not { } operation)
                continue;
            HashSet<string> dispatchable =
                analyses
                    .Where(analysis =>
                        operation.CanDispatch(analysis.Analysis))
                    .Select(static analysis => analysis.Analysis.Id.Value)
                    .ToHashSet(StringComparer.Ordinal);
            if (dispatchable.Count == 0)
            {
                throw new ArgumentException(
                    $"Inspection route '{route.Descriptor.Identity}' runs "
                    + $"{operation.Kind} analyses, but no registered analysis "
                    + "can be dispatched by that operation.",
                    nameof(modules));
            }
            foreach (string identity in operation.DefaultSet)
            {
                if (!dispatchable.Contains(identity))
                {
                    throw new ArgumentException(
                        $"Inspection route '{route.Descriptor.Identity}' "
                        + $"defaults to analysis '{identity}', which its "
                        + "operation cannot dispatch.",
                        nameof(modules));
                }
            }
        }
        foreach (InspectionConsumerBinding binding in bindings)
        {
            if (binding.ExposedAnalyses.IsEmpty)
                continue;
            AnalysisOperationDefinition operation =
                binding.Route.AnalysisOperation
                ?? throw new ArgumentException(
                    $"Inspection consumer binding "
                    + $"'{binding.Descriptor.Identity}' exposes analyses, but "
                    + $"route '{binding.Route.Descriptor.Identity}' runs no "
                    + "analysis operation.",
                    nameof(modules));
            foreach (string identity in binding.ExposedAnalyses)
            {
                if (!analyses.Any(analysis =>
                        analysis.Analysis.Id.Value == identity
                        && operation.CanDispatch(analysis.Analysis)))
                {
                    throw new ArgumentException(
                        $"Inspection consumer binding "
                        + $"'{binding.Descriptor.Identity}' exposes analysis "
                        + $"'{identity}' outside route "
                        + $"'{binding.Route.Descriptor.Identity}'.",
                        nameof(modules));
                }
            }
        }

        return new(
            moduleArray,
            documents,
            routes,
            bindings,
            gaps,
            analyses,
            analysisCapabilities);
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
