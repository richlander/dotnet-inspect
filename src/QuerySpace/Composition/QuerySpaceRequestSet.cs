using System.Collections.Immutable;

namespace QuerySpace.Composition;

/// <summary>
/// Opaque identity for one resource domain whose owner issues resource and
/// source-binding identities.
/// </summary>
public sealed class QuerySpaceResourceDomainIdentity
{
    QuerySpaceResourceDomainIdentity()
    {
    }

    /// <summary>Creates one owner-issued resource domain identity.</summary>
    public static QuerySpaceResourceDomainIdentity Create() => new();
}

/// <summary>Opaque owner-issued identity for one resource.</summary>
public sealed class QuerySpaceResourceIdentity
{
    QuerySpaceResourceIdentity(QuerySpaceResourceDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one resource identity in the supplied owner domain.</summary>
    public static QuerySpaceResourceIdentity Create(
        QuerySpaceResourceDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal QuerySpaceResourceDomainIdentity Domain { get; }
}

/// <summary>
/// Opaque owner-issued identity for one source binding within a resource
/// domain.
/// </summary>
public sealed class QuerySpaceSourceBindingIdentity
{
    QuerySpaceSourceBindingIdentity(QuerySpaceResourceDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one source-binding identity in the supplied owner domain.</summary>
    public static QuerySpaceSourceBindingIdentity Create(
        QuerySpaceResourceDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal QuerySpaceResourceDomainIdentity Domain { get; }
}

/// <summary>Opaque caller-issued identity for one request association.</summary>
public sealed class QuerySpaceRequestAssociationIdentity
{
    QuerySpaceRequestAssociationIdentity()
    {
    }

    /// <summary>Creates one caller-issued association identity.</summary>
    public static QuerySpaceRequestAssociationIdentity Create() => new();
}

/// <summary>
/// Owner-specific resolved request information used by request-set planning.
/// </summary>
public interface IQuerySpaceRequestSetRequest
{
    /// <summary>The validated structural QuerySpace request.</summary>
    QuerySpaceRequest StructuralRequest { get; }

    /// <summary>The result contract the owner-specific plan will publish.</summary>
    string? ResultContract { get; }
}

/// <summary>
/// One not-yet-validated request association supplied to the reference
/// request-set planner.
/// </summary>
public sealed record QuerySpaceRequestAssociationCandidate<TRequest>(
    QuerySpaceRequestAssociationIdentity? Association,
    QuerySpaceResourceIdentity? Resource,
    QuerySpaceSourceBindingIdentity? Source,
    TRequest? Request)
    where TRequest : class, IQuerySpaceRequestSetRequest;

/// <summary>One validated request association in an accepted plan.</summary>
public sealed record QuerySpaceRequestAssociation<TRequest>(
    QuerySpaceRequestAssociationIdentity Association,
    QuerySpaceResourceIdentity Resource,
    QuerySpaceSourceBindingIdentity Source,
    TRequest Request)
    where TRequest : class, IQuerySpaceRequestSetRequest;

/// <summary>Why the reference planner rejected a whole request set.</summary>
public enum QuerySpaceRequestSetRejectionReason
{
    /// <summary>The request set was empty.</summary>
    EmptySet,

    /// <summary>One candidate was absent.</summary>
    MissingAssociation,

    /// <summary>One candidate had no caller-issued association identity.</summary>
    MissingAssociationIdentity,

    /// <summary>One candidate had no owner-issued resource identity.</summary>
    MissingResourceIdentity,

    /// <summary>One candidate had no owner-issued source binding.</summary>
    MissingSourceBinding,

    /// <summary>One candidate had no owner-specific resolved request.</summary>
    MissingRequest,

    /// <summary>The same association identity appeared more than once.</summary>
    DuplicateAssociationIdentity,

    /// <summary>The source binding was issued by another resource domain.</summary>
    ResourceSourceDomainMismatch,

    /// <summary>The owner-specific plan and structural request disagree on result shape.</summary>
    ResultContractMismatch,
}

/// <summary>One typed reason a whole request set was rejected.</summary>
public sealed record QuerySpaceRequestSetRejection(
    int CandidateIndex,
    QuerySpaceRequestAssociationIdentity? Association,
    QuerySpaceRequestSetRejectionReason Reason);

/// <summary>
/// One execution group whose associations may share physical source work.
/// </summary>
public sealed record QuerySpaceRequestExecutionGroup<TRequest>(
    QuerySpaceResourceIdentity Resource,
    QuerySpaceSourceBindingIdentity Source,
    ImmutableArray<QuerySpaceRequestAssociation<TRequest>> Associations)
    where TRequest : class, IQuerySpaceRequestSetRequest;

/// <summary>
/// Immutable accepted request-set plan. Association order is publication
/// order only; it is not scheduling priority.
/// </summary>
public sealed record QuerySpaceRequestSetPlan<TRequest>(
    ImmutableArray<QuerySpaceRequestAssociation<TRequest>> Associations,
    ImmutableArray<QuerySpaceRequestExecutionGroup<TRequest>> Groups)
    where TRequest : class, IQuerySpaceRequestSetRequest;

/// <summary>The accepted or rejected result of request-set planning.</summary>
public abstract record QuerySpaceRequestSetPlanResult<TRequest>
    where TRequest : class, IQuerySpaceRequestSetRequest
{
    private QuerySpaceRequestSetPlanResult()
    {
    }

    /// <summary>The complete immutable request-set plan.</summary>
    public sealed record Accepted(QuerySpaceRequestSetPlan<TRequest> Plan)
        : QuerySpaceRequestSetPlanResult<TRequest>;

    /// <summary>The typed reasons the whole set was rejected.</summary>
    public sealed record Rejected(
        ImmutableArray<QuerySpaceRequestSetRejection> Reasons)
        : QuerySpaceRequestSetPlanResult<TRequest>;
}

/// <summary>
/// Pure reference planner for immutable request sets. It validates the whole
/// set before grouping by exact owner-issued resource and source identities.
/// </summary>
public static class QuerySpaceRequestSetPlanner
{
    /// <summary>Validates and groups one request set without acquiring resources.</summary>
    public static QuerySpaceRequestSetPlanResult<TRequest> Plan<TRequest>(
        IReadOnlyList<QuerySpaceRequestAssociationCandidate<TRequest>?>
            candidates)
        where TRequest : class, IQuerySpaceRequestSetRequest
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var rejections =
            ImmutableArray.CreateBuilder<QuerySpaceRequestSetRejection>();
        if (candidates.Count == 0)
        {
            rejections.Add(new(
                -1,
                null,
                QuerySpaceRequestSetRejectionReason.EmptySet));
            return new QuerySpaceRequestSetPlanResult<TRequest>.Rejected(
                rejections.ToImmutable());
        }

        var associations =
            ImmutableArray.CreateBuilder<
                QuerySpaceRequestAssociation<TRequest>>(candidates.Count);
        var seen = new HashSet<QuerySpaceRequestAssociationIdentity>(
            ReferenceEqualityComparer.Instance);
        for (int i = 0; i < candidates.Count; i++)
        {
            QuerySpaceRequestAssociationCandidate<TRequest>? candidate =
                candidates[i];
            if (candidate is null)
            {
                rejections.Add(new(
                    i,
                    null,
                    QuerySpaceRequestSetRejectionReason.MissingAssociation));
                continue;
            }

            QuerySpaceRequestAssociationIdentity? association =
                candidate.Association;
            QuerySpaceResourceIdentity? resource = candidate.Resource;
            QuerySpaceSourceBindingIdentity? source = candidate.Source;
            TRequest? request = candidate.Request;

            if (association is null)
            {
                rejections.Add(new(
                    i,
                    null,
                    QuerySpaceRequestSetRejectionReason
                        .MissingAssociationIdentity));
            }
            else if (!seen.Add(association))
            {
                rejections.Add(new(
                    i,
                    association,
                    QuerySpaceRequestSetRejectionReason
                        .DuplicateAssociationIdentity));
            }

            if (resource is null)
            {
                rejections.Add(new(
                    i,
                    association,
                    QuerySpaceRequestSetRejectionReason
                        .MissingResourceIdentity));
            }
            if (source is null)
            {
                rejections.Add(new(
                    i,
                    association,
                    QuerySpaceRequestSetRejectionReason.MissingSourceBinding));
            }
            if (request is null)
            {
                rejections.Add(new(
                    i,
                    association,
                    QuerySpaceRequestSetRejectionReason.MissingRequest));
            }

            if (resource is not null
                && source is not null
                && !ReferenceEquals(resource.Domain, source.Domain))
            {
                rejections.Add(new(
                    i,
                    association,
                    QuerySpaceRequestSetRejectionReason
                        .ResourceSourceDomainMismatch));
            }
            if (request is not null
                && !string.Equals(
                    request.StructuralRequest.ResultContract,
                    request.ResultContract,
                    StringComparison.Ordinal))
            {
                rejections.Add(new(
                    i,
                    association,
                    QuerySpaceRequestSetRejectionReason
                        .ResultContractMismatch));
            }

            if (association is not null
                && resource is not null
                && source is not null
                && request is not null)
            {
                associations.Add(new(
                    association,
                    resource,
                    source,
                    request));
            }
        }

        if (rejections.Count > 0)
        {
            return new QuerySpaceRequestSetPlanResult<TRequest>.Rejected(
                rejections.ToImmutable());
        }

        var groups = new List<GroupBuilder<TRequest>>();
        var groupIndices = new Dictionary<GroupKey, int>();
        foreach (QuerySpaceRequestAssociation<TRequest> association
            in associations)
        {
            var key = new GroupKey(
                association.Resource,
                association.Source);
            if (!groupIndices.TryGetValue(key, out int groupIndex))
            {
                groupIndex = groups.Count;
                groupIndices.Add(key, groupIndex);
                groups.Add(new(
                    association.Resource,
                    association.Source));
            }

            groups[groupIndex].Associations.Add(association);
        }

        var plannedGroups =
            ImmutableArray.CreateBuilder<
                QuerySpaceRequestExecutionGroup<TRequest>>(groups.Count);
        foreach (GroupBuilder<TRequest> group in groups)
        {
            plannedGroups.Add(new(
                group.Resource,
                group.Source,
                group.Associations.ToImmutable()));
        }

        return new QuerySpaceRequestSetPlanResult<TRequest>.Accepted(
            new(
                associations.ToImmutable(),
                plannedGroups.ToImmutable()));
    }

    sealed class GroupBuilder<TRequest>(
        QuerySpaceResourceIdentity resource,
        QuerySpaceSourceBindingIdentity source)
        where TRequest : class, IQuerySpaceRequestSetRequest
    {
        public QuerySpaceResourceIdentity Resource { get; } = resource;

        public QuerySpaceSourceBindingIdentity Source { get; } = source;

        public ImmutableArray<QuerySpaceRequestAssociation<TRequest>>.Builder
            Associations { get; } = ImmutableArray.CreateBuilder<
                QuerySpaceRequestAssociation<TRequest>>();
    }

    readonly record struct GroupKey(
        QuerySpaceResourceIdentity Resource,
        QuerySpaceSourceBindingIdentity Source);
}

/// <summary>How one request association was satisfied.</summary>
public enum QuerySpaceRequestSatisfaction
{
    /// <summary>The source answered the closed query without a covering read.</summary>
    SourceNative,

    /// <summary>The source ran one covering read and evaluated this request over it.</summary>
    CoveringRead,
}
