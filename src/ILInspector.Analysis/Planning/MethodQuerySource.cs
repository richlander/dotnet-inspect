using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;
using QuerySpace.Composition;

namespace ILInspector.Analysis.Planning;

/// <summary>The direct MethodDef breadth selected by a source plan.</summary>
public enum MethodDefinitionSourceBreadthKind
{
    /// <summary>Every MethodDef in metadata order.</summary>
    AllDefinitions,

    /// <summary>An exact normalized set of MethodDef handles.</summary>
    ExactMethods,

    /// <summary>Every MethodDef declared by an exact normalized set of TypeDefs.</summary>
    ExactTypes,
}

/// <summary>
/// The immutable direct MethodDef population one source request may read.
/// Exact coordinates are normalized into metadata order without duplicates.
/// </summary>
public sealed class MethodDefinitionSourceBreadth
{
    MethodDefinitionSourceBreadth(
        MethodDefinitionSourceBreadthKind kind,
        ImmutableArray<MethodDefinitionHandle> methods,
        ImmutableArray<TypeDefinitionHandle> types)
    {
        Kind = kind;
        Methods = methods;
        Types = types;
    }

    public static MethodDefinitionSourceBreadth AllDefinitions { get; } =
        new(
            MethodDefinitionSourceBreadthKind.AllDefinitions,
            [],
            []);

    public MethodDefinitionSourceBreadthKind Kind { get; }

    public ImmutableArray<MethodDefinitionHandle> Methods { get; }

    public ImmutableArray<TypeDefinitionHandle> Types { get; }

    public bool IsEmpty =>
        Kind switch
        {
            MethodDefinitionSourceBreadthKind.ExactMethods => Methods.IsEmpty,
            MethodDefinitionSourceBreadthKind.ExactTypes => Types.IsEmpty,
            _ => false,
        };

    public static MethodDefinitionSourceBreadth ExactMethods(
        params MethodDefinitionHandle[] methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        return CreateExactMethods(methods);
    }

    public static MethodDefinitionSourceBreadth ExactMethods(
        ImmutableArray<MethodDefinitionHandle> methods)
    {
        if (methods.IsDefault)
        {
            throw new ArgumentException(
                "Exact MethodDef handles must be initialized.",
                nameof(methods));
        }

        return CreateExactMethods(methods.AsSpan());
    }

    static MethodDefinitionSourceBreadth CreateExactMethods(
        ReadOnlySpan<MethodDefinitionHandle> methods)
    {
        var rows = new int[methods.Length];
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].IsNil)
            {
                throw new ArgumentException(
                    "An exact MethodDef handle cannot be nil.",
                    nameof(methods));
            }

            rows[i] = MetadataTokens.GetRowNumber(methods[i]);
        }

        Array.Sort(rows);
        var normalized =
            ImmutableArray.CreateBuilder<MethodDefinitionHandle>(rows.Length);
        int previous = 0;
        foreach (int row in rows)
        {
            if (row == previous)
                continue;
            normalized.Add(MetadataTokens.MethodDefinitionHandle(row));
            previous = row;
        }

        return new(
            MethodDefinitionSourceBreadthKind.ExactMethods,
            normalized.ToImmutable(),
            []);
    }

    public static MethodDefinitionSourceBreadth ExactTypes(
        params TypeDefinitionHandle[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return CreateExactTypes(types);
    }

    public static MethodDefinitionSourceBreadth ExactTypes(
        ImmutableArray<TypeDefinitionHandle> types)
    {
        if (types.IsDefault)
        {
            throw new ArgumentException(
                "Exact TypeDef handles must be initialized.",
                nameof(types));
        }

        return CreateExactTypes(types.AsSpan());
    }

    static MethodDefinitionSourceBreadth CreateExactTypes(
        ReadOnlySpan<TypeDefinitionHandle> types)
    {
        var rows = new int[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            if (types[i].IsNil)
            {
                throw new ArgumentException(
                    "An exact TypeDef handle cannot be nil.",
                    nameof(types));
            }

            rows[i] = MetadataTokens.GetRowNumber(types[i]);
        }

        Array.Sort(rows);
        var normalized =
            ImmutableArray.CreateBuilder<TypeDefinitionHandle>(rows.Length);
        int previous = 0;
        foreach (int row in rows)
        {
            if (row == previous)
                continue;
            normalized.Add(MetadataTokens.TypeDefinitionHandle(row));
            previous = row;
        }

        return new(
            MethodDefinitionSourceBreadthKind.ExactTypes,
            [],
            normalized.ToImmutable());
    }
}

/// <summary>Opaque identity for one owner-issued Method-definition source request.</summary>
public sealed class MethodDefinitionSourceRequestIdentity
{
    internal MethodDefinitionSourceRequestIdentity()
    {
    }
}

/// <summary>
/// Opaque identity for one Method-definition population known to one
/// request-set operation.
/// </summary>
public sealed class MethodDefinitionSourceResourceIdentity
{
    MethodDefinitionSourceResourceIdentity()
    {
        QuerySpaceIdentity =
            QuerySpaceResourceIdentity.Create(
                MethodDefinitionSourceIdentityAuthority.Domain);
    }

    internal QuerySpaceResourceIdentity QuerySpaceIdentity { get; }

    public static MethodDefinitionSourceResourceIdentity Create() => new();
}

static class MethodDefinitionSourceIdentityAuthority
{
    internal static QuerySpaceResourceDomainIdentity Domain { get; } =
        QuerySpaceResourceDomainIdentity.Create();
}

/// <summary>
/// One non-generic Method-source request that can participate in a QuerySpace
/// request set.
/// </summary>
public abstract class MethodDefinitionSourceRequest
    : IQuerySpaceRequestSetRequest
{
    private protected MethodDefinitionSourceRequest(
        QuerySpaceRequest? structuralRequest,
        WorkDescription work,
        ProducerDeclaration producer,
        MethodDefinitionSourceBreadth breadth)
    {
        StructuralRequestOrNull = structuralRequest;
        Work = work;
        FocusedProducer = producer;
        Identity = new();
        Breadth = breadth;
        Terminal = work.TerminalOf(producer);
        RowLimit = work.RowLimitOf(producer);
        DeclaredLayers = MethodDefinitionExecution.FieldsRead(work);
        if (structuralRequest is not null
            && structuralRequest.Terminal != QuerySpaceTerminal(Terminal))
        {
            throw new ProducerContractException(
                "The QuerySpace terminal does not match the resolved "
                + "Method-source terminal.");
        }
    }

    QuerySpaceRequest? StructuralRequestOrNull { get; }

    public QuerySpaceRequest StructuralRequest =>
        StructuralRequestOrNull
        ?? throw new ProducerContractException(
            "A direct Method-source request has no QuerySpace structural "
            + "request and cannot enter a request set.");

    public string? ResultContract =>
        StructuralRequestOrNull?.ResultContract;

    public MethodDefinitionSourceRequestIdentity Identity { get; }

    public MethodDefinitionSourceBreadth Breadth { get; }

    public ProducerTerminal Terminal { get; }

    public int? RowLimit { get; }

    public MethodDefinitionLayers DeclaredLayers { get; }

    internal bool HasStructuralRequest =>
        StructuralRequestOrNull is not null;

    internal WorkDescription Work { get; }

    internal ProducerDeclaration FocusedProducer { get; }

    internal abstract object BoxResult(
        MethodDefinitionExecution execution);

    static QuerySpaceTerminalRequirement QuerySpaceTerminal(
        ProducerTerminal terminal) =>
        terminal switch
        {
            ProducerTerminal.Rows =>
                QuerySpaceTerminalRequirement.Rows,
            ProducerTerminal.Count =>
                QuerySpaceTerminalRequirement.Count,
            ProducerTerminal.Exists =>
                QuerySpaceTerminalRequirement.Exists,
            ProducerTerminal.Complete =>
                throw new ProducerContractException(
                    "A complete owner-defined fold has no QuerySpace "
                    + "terminal and cannot enter a request set."),
            _ => throw new ArgumentOutOfRangeException(nameof(terminal)),
        };
}

/// <summary>
/// One resource-free request to run a closed, single-producer
/// description over Method definitions.
/// </summary>
public sealed class MethodDefinitionSourceRequest<TResult>
    : MethodDefinitionSourceRequest
{
    internal MethodDefinitionSourceRequest(
        QuerySpaceRequest? structuralRequest,
        WorkDescription work,
        ProducerDeclaration<TResult> producer,
        MethodDefinitionSourceBreadth breadth)
        : base(
            structuralRequest,
            work,
            producer,
            breadth)
    {
        Producer = producer;
    }

    /// <summary>Creates the reference Method-source request without reading a subject.</summary>
    public static MethodDefinitionSourceRequest<TResult> Create(
        WorkDescription work,
        ProducerDeclaration<TResult> producer,
        MethodDefinitionSourceBreadth? breadth = null) =>
        MethodQuerySource.Plan(
            structuralRequest: null,
            work,
            producer,
            breadth ?? MethodDefinitionSourceBreadth.AllDefinitions);

    /// <summary>
    /// Creates a QuerySpace-associated Method-source request without reading
    /// a subject.
    /// </summary>
    public static MethodDefinitionSourceRequest<TResult> Create(
        QuerySpaceRequest structuralRequest,
        WorkDescription work,
        ProducerDeclaration<TResult> producer,
        MethodDefinitionSourceBreadth? breadth = null)
    {
        ArgumentNullException.ThrowIfNull(structuralRequest);
        return MethodQuerySource.Plan(
            structuralRequest,
            work,
            producer,
            breadth ?? MethodDefinitionSourceBreadth.AllDefinitions);
    }

    /// <summary>The focused producer result this request publishes.</summary>
    public ProducerDeclaration<TResult> Producer { get; }

    internal override object BoxResult(
        MethodDefinitionExecution execution) =>
        execution.ResultOf(Producer);
}

/// <summary>One caller association with one resolved Method-source request.</summary>
public sealed class MethodDefinitionSourceAssociation
{
    MethodDefinitionSourceAssociation(
        QuerySpaceRequestAssociationIdentity identity,
        MethodDefinitionSourceRequest request)
    {
        Identity = identity;
        Request = request;
    }

    public QuerySpaceRequestAssociationIdentity Identity { get; }

    public MethodDefinitionSourceRequest Request { get; }

    public static MethodDefinitionSourceAssociation Create(
        MethodDefinitionSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(
            QuerySpaceRequestAssociationIdentity.Create(),
            request);
    }
}

/// <summary>One terminal-specialized lane within a shared Method-source group.</summary>
public sealed class MethodDefinitionSourceLanePlan
{
    internal MethodDefinitionSourceLanePlan(
        WorkDescription work,
        ImmutableArray<MethodDefinitionSourceAssociation> associations)
    {
        Work = work;
        Associations = associations;
    }

    public WorkDescription Work { get; }

    public ImmutableArray<MethodDefinitionSourceAssociation> Associations
    {
        get;
    }
}

/// <summary>One physical Method-source group and its independent lanes.</summary>
public sealed class MethodDefinitionSourceGroupPlan
{
    internal MethodDefinitionSourceGroupPlan(
        QuerySpaceResourceIdentity resource,
        QuerySpaceSourceBindingIdentity source,
        MethodDefinitionSourceBreadth breadth,
        ImmutableArray<MethodDefinitionSourceLanePlan> lanes)
    {
        Resource = resource;
        Source = source;
        Breadth = breadth;
        Lanes = lanes;
    }

    public QuerySpaceResourceIdentity Resource { get; }

    public QuerySpaceSourceBindingIdentity Source { get; }

    public MethodDefinitionSourceBreadth Breadth { get; }

    public ImmutableArray<MethodDefinitionSourceLanePlan> Lanes { get; }
}

/// <summary>Immutable QuerySpace and source plans for one Method request set.</summary>
public sealed class MethodDefinitionSourceRequestSetPlan
{
    internal MethodDefinitionSourceRequestSetPlan(
        QuerySpaceRequestSetPlan<MethodDefinitionSourceRequest> requests,
        ImmutableArray<MethodDefinitionSourceGroupPlan> groups)
    {
        Requests = requests;
        Groups = groups;
    }

    internal QuerySpaceRequestSetPlan<MethodDefinitionSourceRequest> Requests
    {
        get;
    }

    public ImmutableArray<MethodDefinitionSourceGroupPlan> Groups { get; }
}

/// <summary>The accepted or rejected result of Method request-set planning.</summary>
public abstract record MethodDefinitionSourceRequestSetPlanResult
{
    private MethodDefinitionSourceRequestSetPlanResult()
    {
    }

    public sealed record Accepted(
        MethodDefinitionSourceRequestSetPlan Plan)
        : MethodDefinitionSourceRequestSetPlanResult;

    public sealed record Rejected(
        ImmutableArray<QuerySpaceRequestSetRejection> Reasons)
        : MethodDefinitionSourceRequestSetPlanResult;
}

/// <summary>
/// Forms QuerySpace groups from exact Method resource and source identities,
/// then lowers compatible all-definition requests into independent
/// terminal-specialized lanes.
/// </summary>
public static class MethodDefinitionSourceRequestSet
{
    public static MethodDefinitionSourceRequestSetPlanResult Plan(
        MethodDefinitionSourceResourceIdentity resource,
        IReadOnlyList<MethodDefinitionSourceAssociation?> associations)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(associations);

        var groups = new List<SourceGroupBuilder>();
        var groupByAssociation =
            new Dictionary<QuerySpaceRequestAssociationIdentity, int>(
                ReferenceEqualityComparer.Instance);
        foreach (MethodDefinitionSourceAssociation? association
            in associations)
        {
            if (association is null
                || !association.Request.HasStructuralRequest)
            {
                continue;
            }
            if (groupByAssociation.ContainsKey(association.Identity))
                continue;

            int groupIndex = -1;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].CanAdd(association))
                {
                    groupIndex = i;
                    break;
                }
            }

            if (groupIndex < 0)
            {
                groupIndex = groups.Count;
                groups.Add(new());
            }

            groups[groupIndex].Add(association);
            groupByAssociation.Add(
                association.Identity,
                groupIndex);
        }

        var candidates = new QuerySpaceRequestAssociationCandidate<
            MethodDefinitionSourceRequest>?[associations.Count];
        for (int i = 0; i < associations.Count; i++)
        {
            MethodDefinitionSourceAssociation? association =
                associations[i];
            if (association is null)
            {
                candidates[i] = null;
                continue;
            }

            MethodDefinitionSourceRequest? request =
                association.Request.HasStructuralRequest
                    ? association.Request
                    : null;
            QuerySpaceSourceBindingIdentity? source =
                request is null
                    ? null
                    : groups[groupByAssociation[association.Identity]].Source;
            candidates[i] = new(
                association.Identity,
                resource.QuerySpaceIdentity,
                source,
                request);
        }

        QuerySpaceRequestSetPlanResult<MethodDefinitionSourceRequest>
            structural = QuerySpaceRequestSetPlanner.Plan(candidates);
        if (structural
            is QuerySpaceRequestSetPlanResult<
                MethodDefinitionSourceRequest>.Rejected rejected)
        {
            return new MethodDefinitionSourceRequestSetPlanResult.Rejected(
                rejected.Reasons);
        }

        QuerySpaceRequestSetPlan<MethodDefinitionSourceRequest> requestPlan =
            ((QuerySpaceRequestSetPlanResult<
                MethodDefinitionSourceRequest>.Accepted)structural).Plan;
        var byIdentity =
            new Dictionary<
                QuerySpaceRequestAssociationIdentity,
                MethodDefinitionSourceAssociation>(
                    ReferenceEqualityComparer.Instance);
        foreach (MethodDefinitionSourceAssociation? association
            in associations)
        {
            if (association is not null)
                byIdentity.Add(association.Identity, association);
        }

        var sourceGroups =
            ImmutableArray.CreateBuilder<MethodDefinitionSourceGroupPlan>(
                requestPlan.Groups.Length);
        foreach (QuerySpaceRequestExecutionGroup<
            MethodDefinitionSourceRequest> group in requestPlan.Groups)
        {
            var groupedAssociations =
                ImmutableArray.CreateBuilder<
                    MethodDefinitionSourceAssociation>(
                        group.Associations.Length);
            foreach (QuerySpaceRequestAssociation<
                MethodDefinitionSourceRequest> association
                in group.Associations)
            {
                groupedAssociations.Add(
                    byIdentity[association.Association]);
            }

            ImmutableArray<MethodDefinitionSourceAssociation> members =
                groupedAssociations.MoveToImmutable();

            sourceGroups.Add(new(
                group.Resource,
                group.Source,
                members[0].Request.Breadth,
                BuildLanes(members)));
        }

        return new MethodDefinitionSourceRequestSetPlanResult.Accepted(
            new(
                requestPlan,
                sourceGroups.MoveToImmutable()));
    }

    static ImmutableArray<MethodDefinitionSourceLanePlan> BuildLanes(
        ImmutableArray<MethodDefinitionSourceAssociation> associations)
    {
        var planned =
            ImmutableArray.CreateBuilder<MethodDefinitionSourceLanePlan>(
                associations.Length);
        foreach (MethodDefinitionSourceAssociation association
            in associations)
        {
            planned.Add(new(
                association.Request.Work,
                [association]));
        }

        return planned.MoveToImmutable();
    }

    static bool SameBreadth(
        MethodDefinitionSourceBreadth first,
        MethodDefinitionSourceBreadth second) =>
        first.Kind == second.Kind
        && first.Methods.SequenceEqual(second.Methods)
        && first.Types.SequenceEqual(second.Types);

    sealed class SourceGroupBuilder
    {
        readonly List<MethodDefinitionSourceAssociation> _associations = [];

        public QuerySpaceSourceBindingIdentity Source { get; } =
            QuerySpaceSourceBindingIdentity.Create(
                MethodDefinitionSourceIdentityAuthority.Domain);

        public bool CanAdd(
            MethodDefinitionSourceAssociation association)
        {
            if (_associations.Count == 0)
                return true;

            MethodDefinitionSourceRequest first =
                _associations[0].Request;
            MethodDefinitionSourceRequest request =
                association.Request;
            if (first.Breadth.Kind
                    != MethodDefinitionSourceBreadthKind.AllDefinitions
                || !SameBreadth(first.Breadth, request.Breadth)
                || first.Work.PassCount != 1
                || request.Work.PassCount != 1)
            {
                return false;
            }

            return true;
        }

        public void Add(
            MethodDefinitionSourceAssociation association) =>
            _associations.Add(association);
    }
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

/// <summary>One inclusive contiguous range of MethodDef identities.</summary>
public readonly record struct MethodDefinitionHandleRange
{
    internal MethodDefinitionHandleRange(
        MethodDefinitionHandle first,
        MethodDefinitionHandle last)
    {
        First = first;
        Last = last;
    }

    public MethodDefinitionHandle First { get; }

    public MethodDefinitionHandle Last { get; }

    public int Count =>
        MetadataTokens.GetRowNumber(Last)
        - MetadataTokens.GetRowNumber(First)
        + 1;

    public bool Contains(MethodDefinitionHandle handle)
    {
        if (handle.IsNil)
            return false;

        int row = MetadataTokens.GetRowNumber(handle);
        return row >= MetadataTokens.GetRowNumber(First)
            && row <= MetadataTokens.GetRowNumber(Last);
    }
}

/// <summary>Exact detached MethodDef coverage compacted into row ranges.</summary>
public sealed class MethodDefinitionHandleCoverage
{
    internal MethodDefinitionHandleCoverage(
        int count,
        ImmutableArray<MethodDefinitionHandleRange> ranges)
    {
        Count = count;
        Ranges = ranges;
    }

    public static MethodDefinitionHandleCoverage Empty { get; } =
        new(0, []);

    public int Count { get; }

    public ImmutableArray<MethodDefinitionHandleRange> Ranges { get; }

    public bool Contains(MethodDefinitionHandle handle)
    {
        foreach (MethodDefinitionHandleRange range in Ranges)
        {
            if (range.Contains(handle))
                return true;
        }

        return false;
    }
}

/// <summary>Exact source work performed for one Method query request.</summary>
public sealed record MethodDefinitionSourceCoverage(
    MethodDefinitionHandleCoverage DefinitionsExamined,
    MethodDefinitionHandleCoverage MethodsSelected,
    MethodDefinitionHandleCoverage BodiesAcquired);

/// <summary>Detached evidence of the Method-source work actually performed.</summary>
public sealed record MethodDefinitionSourceReceipt(
    MethodDefinitionSourceRequestIdentity Request,
    AssemblyInspectionSubjectIdentity Subject,
    MethodDefinitionSourceBreadth Breadth,
    ProducerTerminal Terminal,
    MethodDefinitionLayers DeclaredLayers,
    MethodDefinitionSourceCompletion Completion,
    MethodDefinitionSourceCoverage Coverage,
    int ModuleLookups)
{
    public int DefinitionsVisited => Coverage.MethodsSelected.Count;

    public int BodiesAcquired => Coverage.BodiesAcquired.Count;
}

/// <summary>One request-associated result and its independent source evidence.</summary>
public sealed class MethodDefinitionSourceRequestResult
{
    internal MethodDefinitionSourceRequestResult(
        MethodDefinitionSourceAssociation association,
        QuerySpaceResourceIdentity resource,
        QuerySpaceSourceBindingIdentity source,
        MethodDefinitionSourceReceipt sourceReceipt,
        WorkReceipt workReceipt,
        object result)
    {
        Association = association;
        Resource = resource;
        Source = source;
        SourceReceipt = sourceReceipt;
        WorkReceipt = workReceipt;
        BoxedResult = result;
    }

    public MethodDefinitionSourceAssociation Association { get; }

    public QuerySpaceResourceIdentity Resource { get; }

    public QuerySpaceSourceBindingIdentity Source { get; }

    public QuerySpaceRequestSatisfaction Satisfaction =>
        QuerySpaceRequestSatisfaction.SourceNative;

    public MethodDefinitionSourceReceipt SourceReceipt { get; }

    public WorkReceipt WorkReceipt { get; }

    internal object BoxedResult { get; }
}

/// <summary>Physical source work recorded once for one execution group.</summary>
public sealed record MethodDefinitionSourceGroupReceipt(
    QuerySpaceResourceIdentity Resource,
    QuerySpaceSourceBindingIdentity Source,
    MethodDefinitionSourceCoverage PhysicalCoverage,
    ImmutableArray<WorkReceipt> LaneReceipts);

/// <summary>Detached result set preserving request order and group work.</summary>
public sealed class MethodDefinitionSourceRequestSetExecution
{
    readonly Dictionary<
        QuerySpaceRequestAssociationIdentity,
        MethodDefinitionSourceRequestResult> _byAssociation;

    internal MethodDefinitionSourceRequestSetExecution(
        MethodDefinitionSourceRequestSetPlan plan,
        ImmutableArray<MethodDefinitionSourceRequestResult> results,
        ImmutableArray<MethodDefinitionSourceGroupReceipt> groupReceipts)
    {
        Plan = plan;
        Results = results;
        GroupReceipts = groupReceipts;
        _byAssociation = new(
            results.Length,
            ReferenceEqualityComparer.Instance);
        foreach (MethodDefinitionSourceRequestResult result in results)
        {
            _byAssociation.Add(
                result.Association.Identity,
                result);
        }
    }

    public MethodDefinitionSourceRequestSetPlan Plan { get; }

    public ImmutableArray<MethodDefinitionSourceRequestResult> Results
    {
        get;
    }

    public ImmutableArray<MethodDefinitionSourceGroupReceipt> GroupReceipts
    {
        get;
    }

    public ProducerResult<TResult> ResultOf<TResult>(
        MethodDefinitionSourceAssociation association,
        MethodDefinitionSourceRequest<TResult> request)
    {
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(association.Request, request)
            || !_byAssociation.TryGetValue(
                association.Identity,
                out MethodDefinitionSourceRequestResult? result))
        {
            throw new ProducerContractException(
                "The Method-source association and request do not identify "
                + "one result in this execution.");
        }

        return (ProducerResult<TResult>)result.BoxedResult;
    }

    public MethodDefinitionSourceRequestResult ResultOf(
        MethodDefinitionSourceAssociation association)
    {
        ArgumentNullException.ThrowIfNull(association);
        if (!_byAssociation.TryGetValue(
                association.Identity,
                out MethodDefinitionSourceRequestResult? result))
        {
            throw new ProducerContractException(
                "The Method-source association is not in this execution.");
        }

        return result;
    }
}

internal readonly struct MethodQuerySourceExecution<TResult>
{
    internal MethodQuerySourceExecution(
        MethodDefinitionSourceReceipt receipt,
        WorkReceipt workReceipt,
        ProducerResult<TResult> result)
    {
        Receipt = receipt;
        WorkReceipt = workReceipt;
        Result = result;
    }

    internal MethodDefinitionSourceReceipt Receipt { get; }

    internal WorkReceipt WorkReceipt { get; }

    internal ProducerResult<TResult> Result { get; }
}

internal static class MethodQuerySource
{
    internal static MethodDefinitionSourceRequest<TResult> Plan<TResult>(
        QuerySpaceRequest? structuralRequest,
        WorkDescription work,
        ProducerDeclaration<TResult> producer,
        MethodDefinitionSourceBreadth breadth)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(breadth);
        if (!work.Contains(producer) || !work.WasRequested(producer))
        {
            throw new ProducerContractException(
                $"Producer '{producer.Identity}' is not a requested producer "
                + "in this work description.");
        }

        if (work.Producers.Length != 1
            || !ReferenceEquals(work.Producers[0], producer))
        {
            throw new ProducerContractException(
                "A direct Method-source request accepts exactly one planned "
                + "producer; request sets compose independent requests.");
        }

        foreach (ProducerDeclaration planned in work.Producers)
        {
            if (planned is not IMethodDefinitionProducer)
            {
                throw new ProducerContractException(
                    $"Producer '{planned.Identity}' is not a "
                    + "method-definition producer.");
            }
        }

        return new(structuralRequest, work, producer, breadth);
    }

    internal static MethodQuerySourceExecution<TResult> Execute<TResult>(
        MethodDefinitionSourceRequest<TResult> request,
        AssemblyInspectionSubjectIdentity subject,
        string sourceName,
        PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(peReader);

        MethodDefinitionExecution interim =
            MethodDefinitionExecution.Execute(
                request.Work,
                sourceName,
                peReader,
                request.Breadth);
        ProducerResult<TResult> result =
            interim.ResultOf(request.Producer);
        WorkReceipt workReceipt = interim.Receipt;
        ProducerParticipation participation =
            workReceipt.For(request.Producer);

        int moduleLookups = 0;
        foreach (ProducerLayerParticipation layer in participation.Layers)
        {
            if (string.Equals(
                    layer.Layer,
                    nameof(MethodDefinitionLayers.ModuleLookup),
                    StringComparison.Ordinal))
            {
                moduleLookups = layer.Acquired;
            }
        }

        MethodDefinitionSourceCompletion completion = result.Outcome switch
        {
            ProducerOutcome.Complete =>
                MethodDefinitionSourceCompletion.Exhausted,
            ProducerOutcome.Stopped =>
                MethodDefinitionSourceCompletion.Satisfied,
            ProducerOutcome.Failed or ProducerOutcome.PrerequisiteFailed =>
                MethodDefinitionSourceCompletion.ProducerFailed,
            ProducerOutcome.Aborted =>
                MethodDefinitionSourceCompletion.Aborted,
            _ => throw new ProducerContractException(
                $"Unknown producer outcome '{result.Outcome}'."),
        };
        var receipt = new MethodDefinitionSourceReceipt(
            request.Identity,
            subject,
            request.Breadth,
            request.Terminal,
            request.DeclaredLayers,
            completion,
            interim.SourceCoverage,
            moduleLookups);
        return new(receipt, workReceipt, result);
    }

    internal static MethodDefinitionSourceRequestSetExecution Execute(
        MethodDefinitionSourceRequestSetPlan plan,
        AssemblyInspectionSubjectIdentity subject,
        string sourceName,
        PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(peReader);

        var byAssociation = new Dictionary<
            QuerySpaceRequestAssociationIdentity,
            MethodDefinitionSourceRequestResult>(
                ReferenceEqualityComparer.Instance);
        var groupReceipts =
            ImmutableArray.CreateBuilder<
                MethodDefinitionSourceGroupReceipt>(
                    plan.Groups.Length);
        foreach (MethodDefinitionSourceGroupPlan group in plan.Groups)
        {
            ImmutableArray<MethodDefinitionExecution> laneExecutions;
            MethodDefinitionSourceCoverage physicalCoverage;
            if (group.Lanes.Length == 1)
            {
                MethodDefinitionExecution execution =
                    MethodDefinitionExecution.Execute(
                        group.Lanes[0].Work,
                        sourceName,
                        peReader,
                        group.Breadth);
                laneExecutions = [execution];
                physicalCoverage = execution.SourceCoverage;
            }
            else
            {
                if (group.Breadth.Kind
                    != MethodDefinitionSourceBreadthKind.AllDefinitions)
                {
                    throw new ProducerContractException(
                        "Shared Method-source groups currently require "
                        + "all-definition breadth.");
                }

                MethodDefinitionSharedExecution shared =
                    MethodDefinitionExecution.ExecuteShared(
                        group.Lanes
                            .Select(static lane => lane.Work)
                            .ToArray(),
                        sourceName,
                        peReader);
                laneExecutions = shared.Lanes;
                physicalCoverage = shared.PhysicalCoverage;
            }

            var laneReceipts =
                ImmutableArray.CreateBuilder<WorkReceipt>(
                    laneExecutions.Length);
            for (int laneIndex = 0;
                laneIndex < group.Lanes.Length;
                laneIndex++)
            {
                MethodDefinitionSourceLanePlan lane =
                    group.Lanes[laneIndex];
                MethodDefinitionExecution execution =
                    laneExecutions[laneIndex];
                laneReceipts.Add(execution.Receipt);

                foreach (MethodDefinitionSourceAssociation association
                    in lane.Associations)
                {
                    MethodDefinitionSourceRequest request =
                        association.Request;
                    ProducerParticipation participation =
                        execution.Receipt.For(
                            request.FocusedProducer);
                    int moduleLookups = 0;
                    foreach (ProducerLayerParticipation layer
                        in participation.Layers)
                    {
                        if (string.Equals(
                                layer.Layer,
                                nameof(
                                    MethodDefinitionLayers.ModuleLookup),
                                StringComparison.Ordinal))
                        {
                            moduleLookups = layer.Acquired;
                        }
                    }

                    MethodDefinitionSourceCompletion completion =
                        participation.Outcome switch
                        {
                            ProducerOutcome.Complete =>
                                MethodDefinitionSourceCompletion.Exhausted,
                            ProducerOutcome.Stopped =>
                                MethodDefinitionSourceCompletion.Satisfied,
                            ProducerOutcome.Failed
                                or ProducerOutcome.PrerequisiteFailed =>
                                    MethodDefinitionSourceCompletion
                                        .ProducerFailed,
                            ProducerOutcome.Aborted =>
                                MethodDefinitionSourceCompletion.Aborted,
                            _ => throw new ProducerContractException(
                                $"Unknown producer outcome "
                                + $"'{participation.Outcome}'."),
                        };
                    var sourceReceipt =
                        new MethodDefinitionSourceReceipt(
                            request.Identity,
                            subject,
                            request.Breadth,
                            request.Terminal,
                            request.DeclaredLayers,
                            completion,
                            execution.SourceCoverage,
                            moduleLookups);
                    byAssociation.Add(
                        association.Identity,
                        new(
                            association,
                            group.Resource,
                            group.Source,
                            sourceReceipt,
                            execution.Receipt,
                            request.BoxResult(execution)));
                }
            }

            groupReceipts.Add(new(
                group.Resource,
                group.Source,
                physicalCoverage,
                laneReceipts.MoveToImmutable()));
        }

        var ordered =
            ImmutableArray.CreateBuilder<
                MethodDefinitionSourceRequestResult>(
                    plan.Requests.Associations.Length);
        foreach (QuerySpaceRequestAssociation<
            MethodDefinitionSourceRequest> association
            in plan.Requests.Associations)
        {
            ordered.Add(
                byAssociation[association.Association]);
        }

        return new(
            plan,
            ordered.MoveToImmutable(),
            groupReceipts.MoveToImmutable());
    }
}

internal sealed class MethodDefinitionSourceCoverageBuilder
{
    readonly bool _enabled;
    readonly MethodDefinitionHandleCoverageBuilder _definitionsExamined = new();
    readonly MethodDefinitionHandleCoverageBuilder _methodsSelected = new();
    readonly MethodDefinitionHandleCoverageBuilder _bodiesAcquired = new();

    public MethodDefinitionSourceCoverageBuilder(bool enabled) =>
        _enabled = enabled;

    public void RecordDefinitionExamined(MethodDefinitionHandle handle)
    {
        if (_enabled)
            _definitionsExamined.Add(handle);
    }

    public void RecordMethodSelected(MethodDefinitionHandle handle)
    {
        if (_enabled)
            _methodsSelected.Add(handle);
    }

    public void RecordBodyAcquired(MethodDefinitionHandle handle)
    {
        if (_enabled)
            _bodiesAcquired.Add(handle);
    }

    public MethodDefinitionSourceCoverage Build() =>
        new(
            _definitionsExamined.Build(),
            _methodsSelected.Build(),
            _bodiesAcquired.Build());
}

internal sealed class MethodDefinitionHandleCoverageBuilder
{
    List<MethodDefinitionHandleRange>? _completedRanges;
    HashSet<int>? _unorderedRows;
    int _firstRow;
    int _lastRow;
    int _count;

    public void Add(MethodDefinitionHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        if (_unorderedRows is not null)
        {
            if (_unorderedRows.Add(row))
                _count++;
            return;
        }

        if (_count == 0)
        {
            _firstRow = row;
            _lastRow = row;
            _count = 1;
            return;
        }

        if (row == _lastRow)
            return;
        if (row < _lastRow)
        {
            MoveToUnordered(row);
            return;
        }

        if (row == _lastRow + 1)
        {
            _lastRow = row;
            _count++;
            return;
        }

        (_completedRanges ??= []).Add(CurrentRange());
        _firstRow = row;
        _lastRow = row;
        _count++;
    }

    public MethodDefinitionHandleCoverage Build()
    {
        if (_count == 0)
            return MethodDefinitionHandleCoverage.Empty;
        if (_unorderedRows is not null)
        {
            int[] rows = [.. _unorderedRows];
            Array.Sort(rows);
            var ordered = new MethodDefinitionHandleCoverageBuilder();
            foreach (int row in rows)
            {
                ordered.Add(
                    MetadataTokens.MethodDefinitionHandle(row));
            }
            return ordered.Build();
        }

        MethodDefinitionHandleRange current = CurrentRange();
        if (_completedRanges is null)
        {
            return new MethodDefinitionHandleCoverage(
                _count,
                [current]);
        }

        var ranges =
            ImmutableArray.CreateBuilder<MethodDefinitionHandleRange>(
                _completedRanges.Count + 1);
        ranges.AddRange(_completedRanges);
        ranges.Add(current);
        return new MethodDefinitionHandleCoverage(
            _count,
            ranges.ToImmutable());
    }

    void MoveToUnordered(int row)
    {
        var rows = new HashSet<int>(_count + 1);
        if (_completedRanges is not null)
        {
            foreach (MethodDefinitionHandleRange range
                in _completedRanges)
            {
                AddRange(rows, range);
            }
        }
        AddRange(rows, CurrentRange());
        rows.Add(row);
        _unorderedRows = rows;
        _count = rows.Count;
    }

    static void AddRange(
        HashSet<int> rows,
        MethodDefinitionHandleRange range)
    {
        int last = MetadataTokens.GetRowNumber(range.Last);
        for (int row = MetadataTokens.GetRowNumber(range.First);
            row <= last;
            row++)
        {
            rows.Add(row);
        }
    }

    MethodDefinitionHandleRange CurrentRange() =>
        new(
            MetadataTokens.MethodDefinitionHandle(_firstRow),
            MetadataTokens.MethodDefinitionHandle(_lastRow));
}
