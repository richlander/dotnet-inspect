using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;
using QuerySpace.Composition;

namespace ILInspector.Analysis.Planning;

/// <summary>An owner-issued Analysis source composed by an assembly operation.</summary>
public enum AssemblyAnalysisSourceKind
{
    /// <summary>Method definitions in metadata order.</summary>
    MethodDefinitions,
}

/// <summary>Opaque identity for one owner-issued Method-definition source request.</summary>
public sealed class MethodDefinitionSourceRequestIdentity
{
    internal MethodDefinitionSourceRequestIdentity()
    {
    }
}

static class MethodDefinitionSourceIdentities
{
    internal static QuerySpaceResourceDomainIdentity ResourceDomain { get; } =
        QuerySpaceResourceDomainIdentity.Create();
}

/// <summary>
/// Owner-issued identity for Method-source requests that are eligible to share
/// one physical execution group.
/// </summary>
public sealed class MethodDefinitionSourceBinding
{
    MethodDefinitionSourceBinding()
    {
        Identity = QuerySpaceSourceBindingIdentity.Create(
            MethodDefinitionSourceIdentities.ResourceDomain);
    }

    /// <summary>Creates one explicit Method-source sharing eligibility token.</summary>
    public static MethodDefinitionSourceBinding Create() => new();

    internal QuerySpaceSourceBindingIdentity Identity { get; }
}

/// <summary>
/// One resource-free request to run a closed producer description over Method
/// definitions.
/// </summary>
public abstract class MethodDefinitionSourceRequest
    : IQuerySpaceRequestSetRequest
{
    private protected MethodDefinitionSourceRequest(
        QuerySpaceRequest structuralRequest,
        MethodDefinitionSourceBinding binding,
        WorkDescription work,
        ProducerDeclaration producer)
    {
        StructuralRequest = structuralRequest;
        Binding = binding;
        Work = work;
        Declaration = producer;
        Identity = new();
        Association = QuerySpaceRequestAssociationIdentity.Create();
        Terminal = work.TerminalOf(producer);
        DeclaredLayers = MethodDefinitionExecution.FieldsRead(work);
    }

    /// <summary>This request's exact Method-source identity.</summary>
    public MethodDefinitionSourceRequestIdentity Identity { get; }

    /// <summary>This request's caller-issued QuerySpace association identity.</summary>
    public QuerySpaceRequestAssociationIdentity Association { get; }

    /// <summary>The owner-issued source binding for sharing eligibility.</summary>
    public MethodDefinitionSourceBinding Binding { get; }

    /// <inheritdoc />
    public QuerySpaceRequest StructuralRequest { get; }

    /// <inheritdoc />
    public string? ResultContract => StructuralRequest.ResultContract;

    /// <summary>The closing applied to the focused producer.</summary>
    public ProducerTerminal Terminal { get; }

    /// <summary>The source layers declared by this request's closed work.</summary>
    public MethodDefinitionLayers DeclaredLayers { get; }

    internal WorkDescription Work { get; }

    internal ProducerDeclaration Declaration { get; }

    internal ProducerRequest ProducerRequest => new(Declaration, Terminal);

    internal abstract MethodDefinitionSourceResult Capture(
        MethodDefinitionExecution execution,
        MethodDefinitionSourceReceipt receipt);
}

/// <summary>
/// One typed resource-free Method-source request. Supporting producer
/// dependencies may appear in its work description, but exactly one producer
/// is the requested result.
/// </summary>
public sealed class MethodDefinitionSourceRequest<TResult>
    : MethodDefinitionSourceRequest
{
    MethodDefinitionSourceRequest(
        QuerySpaceRequest structuralRequest,
        MethodDefinitionSourceBinding binding,
        WorkDescription work,
        ProducerDeclaration<TResult> producer)
        : base(structuralRequest, binding, work, producer)
    {
        Producer = producer;
    }

    /// <summary>Creates the Method-source request without reading a subject.</summary>
    public static MethodDefinitionSourceRequest<TResult> Create(
        QuerySpaceRequest structuralRequest,
        WorkDescription work,
        ProducerDeclaration<TResult> producer) =>
        Create(
            structuralRequest,
            MethodDefinitionSourceBinding.Create(),
            work,
            producer);

    /// <summary>
    /// Creates one Method-source request under an explicit owner-issued
    /// sharing binding without reading a subject.
    /// </summary>
    public static MethodDefinitionSourceRequest<TResult> Create(
        QuerySpaceRequest structuralRequest,
        MethodDefinitionSourceBinding binding,
        WorkDescription work,
        ProducerDeclaration<TResult> producer)
    {
        ArgumentNullException.ThrowIfNull(structuralRequest);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(producer);
        if (!work.Contains(producer) || !work.WasRequested(producer))
        {
            throw new ProducerContractException(
                $"Producer '{producer.Identity}' is not a requested producer "
                + "in this work description.");
        }

        int requested = 0;
        foreach (ProducerDeclaration planned in work.Producers)
        {
            if (work.WasRequested(planned))
            {
                requested++;
                if (!ReferenceEquals(planned, producer))
                {
                    throw new ProducerContractException(
                        "One Method-source association must have exactly one "
                        + "requested producer.");
                }
            }

            if (planned is not IMethodDefinitionProducer)
            {
                throw new ProducerContractException(
                    $"Producer '{planned.Identity}' is not a "
                    + "method-definition producer.");
            }
        }

        if (requested != 1)
        {
            throw new ProducerContractException(
                "One Method-source association must have exactly one "
                + "requested producer.");
        }

        return new(structuralRequest, binding, work, producer);
    }

    /// <summary>The focused producer result this request publishes.</summary>
    public ProducerDeclaration<TResult> Producer { get; }

    internal override MethodDefinitionSourceResult Capture(
        MethodDefinitionExecution execution,
        MethodDefinitionSourceReceipt receipt) =>
        new MethodDefinitionSourceResult<TResult>(
            this,
            receipt,
            execution.ResultOf(Producer));

    internal ProducerResult<TResult> ReadResult(
        MethodDefinitionSourceResult result)
    {
        if (result is not MethodDefinitionSourceResult<TResult> typed
            || !ReferenceEquals(typed.Request, this))
        {
            throw new ProducerContractException(
                "The detached Method-source result does not match its "
                + "request association.");
        }

        return typed.Result;
    }
}

/// <summary>
/// One QuerySpace-planned Method-source group and its Producer Planning work.
/// </summary>
public sealed record MethodDefinitionSourcePlan(
    QuerySpaceResourceIdentity Resource,
    QuerySpaceSourceBindingIdentity Source,
    ImmutableArray<MethodDefinitionSourceRequest> Requests,
    WorkDescription Work);

/// <summary>
/// Resource-free composition of one QuerySpace request set and its one
/// Method-source execution group.
/// </summary>
public sealed class AssemblyAnalysisOperation
{
    AssemblyAnalysisOperation(
        string sourceName,
        QuerySpaceRequestSetPlan<MethodDefinitionSourceRequest> requestSet,
        ImmutableArray<MethodDefinitionSourcePlan> methodDefinitions)
    {
        SourceName = sourceName;
        RequestSet = requestSet;
        MethodDefinitions = methodDefinitions;
    }

    /// <summary>Creates an operation without opening or reading its subject.</summary>
    public static AssemblyAnalysisOperation Create(
        string sourceName,
        params ReadOnlySpan<MethodDefinitionSourceRequest> requests)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (requests.IsEmpty)
        {
            throw new ProducerContractException(
                "An assembly analysis operation requires at least one "
                + "Method-source request.");
        }

        QuerySpaceResourceIdentity resource =
            QuerySpaceResourceIdentity.Create(
                MethodDefinitionSourceIdentities.ResourceDomain);
        var candidates = new QuerySpaceRequestAssociationCandidate<
            MethodDefinitionSourceRequest>?[requests.Length];
        for (int i = 0; i < requests.Length; i++)
        {
            MethodDefinitionSourceRequest request = requests[i]
                ?? throw new ArgumentNullException(
                    nameof(requests),
                    "Method-source requests cannot contain null.");
            candidates[i] = new(
                request.Association,
                resource,
                request.Binding.Identity,
                request);
        }

        QuerySpaceRequestSetPlanResult<MethodDefinitionSourceRequest> planned =
            QuerySpaceRequestSetPlanner.Plan(candidates);
        if (planned
            is not QuerySpaceRequestSetPlanResult<
                MethodDefinitionSourceRequest>.Accepted accepted)
        {
            var rejected = (QuerySpaceRequestSetPlanResult<
                MethodDefinitionSourceRequest>.Rejected)planned;
            throw new ProducerContractException(
                "The Method-source request set was rejected: "
                + string.Join(
                    ", ",
                    rejected.Reasons.Select(static reason => reason.Reason)));
        }
        var methodDefinitions =
            ImmutableArray.CreateBuilder<MethodDefinitionSourcePlan>(
                accepted.Plan.Groups.Length);
        foreach (QuerySpaceRequestExecutionGroup<
            MethodDefinitionSourceRequest> group in accepted.Plan.Groups)
        {
            methodDefinitions.Add(CreateSourcePlan(group));
        }

        return new(
            sourceName,
            accepted.Plan,
            methodDefinitions.ToImmutable());
    }

    static MethodDefinitionSourcePlan CreateSourcePlan(
        QuerySpaceRequestExecutionGroup<MethodDefinitionSourceRequest> group)
    {
        WorkDescription work;
        if (group.Associations.Length == 1)
        {
            work = group.Associations[0].Request.Work;
        }
        else
        {
            var producerRequests =
                new ProducerRequest[group.Associations.Length];
            for (int i = 0; i < group.Associations.Length; i++)
            {
                producerRequests[i] =
                    group.Associations[i].Request.ProducerRequest;
            }

            ProducerPlanResult producerPlan =
                ProducerPlanner.Plan(producerRequests);
            if (producerPlan
                is not ProducerPlanResult.Accepted producerAccepted)
            {
                var rejected = (ProducerPlanResult.Rejected)producerPlan;
                throw new ProducerContractException(
                    "The Method-source producer set was rejected: "
                    + string.Join(
                        ", ",
                        rejected.Reasons.Select(
                            static reason =>
                                $"{reason.Producer}:{reason.Reason}")));
            }

            work = producerAccepted.Description;
        }

        return new MethodDefinitionSourcePlan(
            group.Resource,
            group.Source,
            [.. group.Associations.Select(
                static association => association.Request)],
            work);
    }

    /// <summary>Content-free source name used in diagnostics.</summary>
    public string SourceName { get; }

    /// <summary>The immutable accepted QuerySpace request-set plan.</summary>
    public QuerySpaceRequestSetPlan<MethodDefinitionSourceRequest> RequestSet
    {
        get;
    }

    /// <summary>The exact owner-issued Method-source execution groups.</summary>
    public ImmutableArray<MethodDefinitionSourcePlan> MethodDefinitions
    {
        get;
    }

    /// <summary>Returns the exact source plan containing one request.</summary>
    public MethodDefinitionSourcePlan PlanOf(
        MethodDefinitionSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (MethodDefinitionSourcePlan plan in MethodDefinitions)
        {
            if (plan.Requests.Any(
                    candidate => ReferenceEquals(candidate, request)))
            {
                return plan;
            }
        }

        throw new ProducerContractException(
            "The Method-source request is not part of this assembly analysis "
            + "operation.");
    }

    /// <summary>The owner-issued source kinds composed by this operation.</summary>
    public ImmutableArray<AssemblyAnalysisSourceKind> SourceKinds =>
        [AssemblyAnalysisSourceKind.MethodDefinitions];
}

/// <summary>How the Method source settled one accepted request.</summary>
public enum MethodDefinitionSourceCompletion
{
    /// <summary>The source exhausted the selected Method-definition population.</summary>
    Exhausted,

    /// <summary>The request's terminal settled before source exhaustion.</summary>
    Satisfied,

    /// <summary>A producer failure prevented the source request from settling.</summary>
    ProducerFailed,

    /// <summary>A critical producer work bound aborted the source request.</summary>
    Aborted,
}

/// <summary>Detached evidence of the Method-source work actually performed.</summary>
public sealed record MethodDefinitionSourceReceipt(
    MethodDefinitionSourceRequestIdentity Request,
    QuerySpaceRequestAssociationIdentity Association,
    QuerySpaceResourceIdentity Resource,
    QuerySpaceSourceBindingIdentity Source,
    QuerySpaceRequestSatisfaction Satisfaction,
    ProducerTerminal Terminal,
    MethodDefinitionLayers DeclaredLayers,
    MethodDefinitionSourceCompletion Completion,
    int DefinitionsVisited,
    int BodiesAcquired,
    int ModuleLookups);

/// <summary>Detached physical-work evidence for one Method-source group.</summary>
public sealed record MethodDefinitionSourceGroupReceipt(
    QuerySpaceResourceIdentity Resource,
    QuerySpaceSourceBindingIdentity Source,
    WorkReceipt Work);

/// <summary>Why assembly-operation execution was rejected before producer work.</summary>
public enum AssemblyAnalysisRejectionKind
{
    /// <summary>The access was issued for a different operation.</summary>
    OperationAccessMismatch,

    /// <summary>Session-owned admission found no managed metadata source.</summary>
    ManagedMetadataUnavailable,
}

/// <summary>Result of binding and executing one assembly analysis operation.</summary>
public abstract record AssemblyAnalysisServiceResult
{
    private AssemblyAnalysisServiceResult()
    {
    }

    /// <summary>The source and producer execution completed with detached evidence.</summary>
    public sealed record Completed(AssemblyAnalysisExecution Execution)
        : AssemblyAnalysisServiceResult;

    /// <summary>Execution was rejected before producer work began.</summary>
    public sealed record Rejected(AssemblyAnalysisRejectionKind Kind)
        : AssemblyAnalysisServiceResult;
}

abstract class MethodDefinitionSourceResult(
    MethodDefinitionSourceRequest request,
    MethodDefinitionSourceReceipt receipt)
{
    internal MethodDefinitionSourceRequest Request { get; } = request;

    internal MethodDefinitionSourceReceipt Receipt { get; } = receipt;
}

sealed class MethodDefinitionSourceResult<TResult>(
    MethodDefinitionSourceRequest<TResult> request,
    MethodDefinitionSourceReceipt receipt,
    ProducerResult<TResult> result)
    : MethodDefinitionSourceResult(request, receipt)
{
    internal ProducerResult<TResult> Result { get; } = result;
}

/// <summary>Detached publication for one assembly analysis invocation.</summary>
public sealed class AssemblyAnalysisExecution
{
    readonly ImmutableArray<MethodDefinitionSourceResult> _results;

    internal AssemblyAnalysisExecution(
        AssemblyAnalysisOperation operation,
        AssemblyInspectionSubjectIdentity subject,
        ImmutableArray<MethodDefinitionSourceGroupReceipt> workReceipts,
        ImmutableArray<MethodDefinitionSourceResult> results)
    {
        Operation = operation;
        Subject = subject;
        WorkReceipts = workReceipts;
        _results = results;
        SourceReceipts =
            [.. results.Select(static result => result.Receipt)];
    }

    /// <summary>The exact resource-free operation that produced this execution.</summary>
    public AssemblyAnalysisOperation Operation { get; }

    /// <summary>The exact detached subject identity used for this invocation.</summary>
    public AssemblyInspectionSubjectIdentity Subject { get; }

    /// <summary>The physical Producer Planning receipt for each source group.</summary>
    public ImmutableArray<MethodDefinitionSourceGroupReceipt> WorkReceipts
    {
        get;
    }

    /// <summary>
    /// Every detached Method-source receipt in request-set publication order.
    /// </summary>
    public ImmutableArray<MethodDefinitionSourceReceipt> SourceReceipts
    {
        get;
    }

    /// <summary>Returns one request's detached Method-source receipt.</summary>
    public MethodDefinitionSourceReceipt SourceReceiptOf(
        MethodDefinitionSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        MethodDefinitionSourceResult result = Find(request);
        return result.Receipt;
    }

    /// <summary>Returns one focused result through its exact request association.</summary>
    public ProducerResult<TResult> ResultOf<TResult>(
        MethodDefinitionSourceRequest<TResult> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.ReadResult(Find(request));
    }

    /// <summary>Returns the physical work receipt for one request's source group.</summary>
    public WorkReceipt WorkReceiptOf(
        MethodDefinitionSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        MethodDefinitionSourcePlan plan = Operation.PlanOf(request);
        foreach (MethodDefinitionSourceGroupReceipt receipt in WorkReceipts)
        {
            if (ReferenceEquals(receipt.Resource, plan.Resource)
                && ReferenceEquals(receipt.Source, plan.Source))
            {
                return receipt.Work;
            }
        }

        throw new ProducerContractException(
            "The Method-source group has no physical work receipt.");
    }

    MethodDefinitionSourceResult Find(MethodDefinitionSourceRequest request)
    {
        foreach (MethodDefinitionSourceResult result in _results)
        {
            if (ReferenceEquals(result.Request, request))
                return result;
        }

        throw new ProducerContractException(
            "The Method-source request is not part of this assembly analysis "
            + "execution.");
    }
}

/// <summary>
/// Stateless executor binding one exact assembly operation to session-issued access.
/// </summary>
public sealed class AssemblyAnalysisService
{
    AssemblyAnalysisService()
    {
    }

    public static AssemblyAnalysisService Instance { get; } = new();

    /// <summary>Executes one operation through its exact stack-only access.</summary>
    public AssemblyAnalysisServiceResult Execute(
        AssemblyAnalysisOperation operation,
        scoped AssemblyInspectionOperationAccess<AssemblyAnalysisOperation>
            access)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!ReferenceEquals(operation, access.Operation))
        {
            return new AssemblyAnalysisServiceResult.Rejected(
                AssemblyAnalysisRejectionKind.OperationAccessMismatch);
        }

        if (!access.HasMetadata)
        {
            return new AssemblyAnalysisServiceResult.Rejected(
                AssemblyAnalysisRejectionKind.ManagedMetadataUnavailable);
        }

        AssemblyInspectionSubjectIdentity subject = access.Subject;
        return access.InspectImage(
            peReader => Execute(operation, subject, peReader));
    }

    static AssemblyAnalysisServiceResult Execute(
        AssemblyAnalysisOperation operation,
        AssemblyInspectionSubjectIdentity subject,
        PEReader peReader)
    {
        var byRequest =
            new Dictionary<
                MethodDefinitionSourceRequest,
                MethodDefinitionSourceResult>(
                    ReferenceEqualityComparer.Instance);
        var workReceipts =
            ImmutableArray.CreateBuilder<MethodDefinitionSourceGroupReceipt>(
                operation.MethodDefinitions.Length);
        foreach (MethodDefinitionSourcePlan plan
            in operation.MethodDefinitions)
        {
            MethodDefinitionExecution interim =
                MethodDefinitionExecution.Execute(
                    plan.Work,
                    operation.SourceName,
                    peReader);
            WorkReceipt workReceipt = interim.Receipt;
            workReceipts.Add(new(
                plan.Resource,
                plan.Source,
                workReceipt));
            foreach (MethodDefinitionSourceRequest request
                in plan.Requests)
            {
                ProducerParticipation participation =
                    workReceipt.For(request.Declaration);
                (int bodiesAcquired, int moduleLookups) =
                    LayerAcquisition(participation);
                ProducerOutcome outcome = participation.Outcome;
                MethodDefinitionSourceCompletion completion = outcome switch
                {
                    ProducerOutcome.Complete =>
                        MethodDefinitionSourceCompletion.Exhausted,
                    ProducerOutcome.Stopped =>
                        MethodDefinitionSourceCompletion.Satisfied,
                    ProducerOutcome.Failed
                        or ProducerOutcome.PrerequisiteFailed =>
                        MethodDefinitionSourceCompletion.ProducerFailed,
                    ProducerOutcome.Aborted =>
                        MethodDefinitionSourceCompletion.Aborted,
                    _ => throw new ProducerContractException(
                        $"Unknown producer outcome '{outcome}'."),
                };
                var sourceReceipt = new MethodDefinitionSourceReceipt(
                    request.Identity,
                    request.Association,
                    plan.Resource,
                    plan.Source,
                    QuerySpaceRequestSatisfaction.CoveringRead,
                    request.Terminal,
                    request.DeclaredLayers,
                    completion,
                    participation.UnitsAttempted,
                    bodiesAcquired,
                    moduleLookups);
                byRequest.Add(
                    request,
                    request.Capture(interim, sourceReceipt));
            }
        }

        var results =
            ImmutableArray.CreateBuilder<MethodDefinitionSourceResult>(
                operation.RequestSet.Associations.Length);
        foreach (QuerySpaceRequestAssociation<
            MethodDefinitionSourceRequest> association
            in operation.RequestSet.Associations)
        {
            results.Add(byRequest[association.Request]);
        }

        var execution = new AssemblyAnalysisExecution(
            operation,
            subject,
            workReceipts.ToImmutable(),
            results.ToImmutable());
        return new AssemblyAnalysisServiceResult.Completed(execution);
    }

    static (int BodiesAcquired, int ModuleLookups) LayerAcquisition(
        ProducerParticipation participation)
    {
        int bodiesAcquired = 0;
        int moduleLookups = 0;
        foreach (ProducerLayerParticipation layer in participation.Layers)
        {
            if (string.Equals(
                    layer.Layer,
                    nameof(MethodDefinitionLayers.Body),
                    StringComparison.Ordinal))
            {
                bodiesAcquired = layer.Acquired;
            }
            else if (string.Equals(
                    layer.Layer,
                    nameof(MethodDefinitionLayers.ModuleLookup),
                    StringComparison.Ordinal))
            {
                moduleLookups = layer.Acquired;
            }
        }

        return (bodiesAcquired, moduleLookups);
    }
}
