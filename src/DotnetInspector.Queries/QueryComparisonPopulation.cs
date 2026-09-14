using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public enum QueryComparisonProfile
{
    ImplementationComparison,
}

public enum QueryComparisonSide
{
    Before,
    After,
}

/// <summary>Opaque identity of one comparison-population sealing operation.</summary>
public sealed class QueryComparisonOperationId
{
    internal QueryComparisonOperationId() { }
}

public sealed class QueryComparisonQuestionId
{
    internal QueryComparisonQuestionId(QueryComparisonOperationId operation)
        => Operation = operation;

    public QueryComparisonOperationId Operation { get; }
}

public sealed class QueryComparisonInputId
{
    internal QueryComparisonInputId(
        QueryComparisonQuestionId question,
        QueryComparisonSide side)
    {
        Question = question;
        Side = side;
    }

    public QueryComparisonOperationId Operation => Question.Operation;
    public QueryComparisonQuestionId Question { get; }
    public QueryComparisonSide Side { get; }
}

/// <summary>Idless borrowed evidence, validated by population sealing.</summary>
public sealed record ImplementationComparisonBinding(
    ResolvedAssemblyReference Assembly,
    IAssemblyReferenceResolver Resolver,
    LibraryBodyIndex BodyIndex);

public sealed record ImplementationComparisonPopulationRequest(
    IReadOnlyList<ImplementationComparisonBinding?>? Before,
    IReadOnlyList<ImplementationComparisonBinding?>? After,
    IReadOnlySet<string>? TypeFilters = null,
    IReadOnlySet<string>? MemberTargetIdentities = null);

public sealed class QueryComparisonInput<TBinding> where TBinding : class
{
    internal QueryComparisonInput(QueryComparisonInputId id, TBinding binding)
    {
        Id = id;
        Binding = binding;
    }

    public QueryComparisonInputId Id { get; }
    public TBinding Binding { get; }
}

/// <summary>One immutable question and its complete side-local input population.</summary>
public abstract class QueryComparisonPopulation
{
    private protected QueryComparisonPopulation(
        QueryComparisonProfile profile,
        QueryComparisonQuestionId question,
        ImmutableArray<QueryComparisonInputId> inputIds,
        ImmutableHashSet<string>? typeFilters,
        ImmutableHashSet<string>? memberTargetIdentities)
    {
        Profile = profile;
        Question = question;
        InputIds = inputIds;
        TypeFilters = typeFilters;
        MemberTargetIdentities = memberTargetIdentities;
    }

    public QueryComparisonProfile Profile { get; }
    public QueryComparisonOperationId Operation => Question.Operation;
    public QueryComparisonQuestionId Question { get; }
    public ImmutableArray<QueryComparisonInputId> InputIds { get; }
    public ImmutableHashSet<string>? TypeFilters { get; }
    public ImmutableHashSet<string>? MemberTargetIdentities { get; }
}

public sealed class QueryComparisonPopulation<TBinding> :
    QueryComparisonPopulation where TBinding : class
{
    internal QueryComparisonPopulation(
        QueryComparisonProfile profile,
        QueryComparisonQuestionId question,
        ImmutableArray<QueryComparisonInput<TBinding>> before,
        ImmutableArray<QueryComparisonInput<TBinding>> after,
        ImmutableHashSet<string>? typeFilters,
        ImmutableHashSet<string>? memberTargetIdentities)
        : base(profile, question,
            [.. before.Select(input => input.Id), .. after.Select(input => input.Id)],
            typeFilters, memberTargetIdentities)
    {
        Before = before;
        After = after;
        Inputs = [.. before, .. after];
    }

    public ImmutableArray<QueryComparisonInput<TBinding>> Before { get; }
    public ImmutableArray<QueryComparisonInput<TBinding>> After { get; }
    public ImmutableArray<QueryComparisonInput<TBinding>> Inputs { get; }
}

public enum QueryPopulationRejectionKind
{
    MissingSide,
    MissingBinding,
    MissingAssembly,
    MissingResolver,
    MissingBodyIndex,
    MissingTypeFilter,
    MissingMemberTarget,
}

/// <summary>Coordinates locate invalid input; they are never population identity.</summary>
public sealed record QueryPopulationRejection(
    QueryPopulationRejectionKind Kind,
    QueryComparisonProfile Profile,
    QueryComparisonSide? Side = null,
    int? Index = null);

public abstract class QueryPopulationSealingOutcome
{
    private protected QueryPopulationSealingOutcome() { }

    public sealed class Sealed : QueryPopulationSealingOutcome
    {
        internal Sealed(QueryComparisonPopulation population)
            => Population = population;

        public QueryComparisonPopulation Population { get; }
    }

    public sealed class Rejected : QueryPopulationSealingOutcome
    {
        internal Rejected(QueryPopulationRejection rejection)
            => Rejection = rejection;

        public QueryPopulationRejection Rejection { get; }
    }
}
