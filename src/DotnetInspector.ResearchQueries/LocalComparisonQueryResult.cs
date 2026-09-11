using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>The exact side-local identities issued for one local comparison invocation.</summary>
public sealed class LocalComparisonQueryIdentity
{
    internal LocalComparisonQueryIdentity(
        QueryComparisonPopulation<ImplementationComparisonBinding> population)
    {
        Operation = population.Operation;
        Question = population.Question;
        Before = population.Before.Single().Id;
        After = population.After.Single().Id;
    }

    public QueryComparisonOperationId Operation { get; }
    public QueryComparisonQuestionId Question { get; }
    public QueryComparisonInputId Before { get; }
    public QueryComparisonInputId After { get; }
}

public enum DirectMemberDesignationFailureKind
{
    MissingAddress,
    MissingMethod,
    MetadataSelectionUnavailable,
}

/// <summary>Query-origin non-success, distinct from a Research terminal outcome.</summary>
public abstract record LocalComparisonQueryFailure
{
    private protected LocalComparisonQueryFailure() { }

    public sealed record InvalidDesignation(
        DirectMemberDesignationFailureKind Kind,
        ImmutableArray<ApiSurfaceInspectionFailure> MetadataFailures)
        : LocalComparisonQueryFailure;

    public sealed record AccessRejected(CandidateOpenFailure Cause)
        : LocalComparisonQueryFailure;

    public sealed record PopulationRejected(QueryPopulationRejection Cause)
        : LocalComparisonQueryFailure;

    public sealed record AdmissionRejected(ResearchAdmissionRejection Cause)
        : LocalComparisonQueryFailure;

    public sealed record PlanningRejected(ResearchTargetPlanningRejection Cause)
        : LocalComparisonQueryFailure;

    public sealed record DesignationRejected(ResearchDesignatedPairOutcome.Rejected Cause)
        : LocalComparisonQueryFailure;

    public sealed record DesignationUnavailable(ResearchDesignatedPairOutcome.Unavailable Cause)
        : LocalComparisonQueryFailure;

    public sealed record Failed(Exception Cause) : LocalComparisonQueryFailure;

    public sealed record Cancelled(OperationCanceledException Cause)
        : LocalComparisonQueryFailure;
}

/// <summary>
/// Retained identity and original native evidence, published after borrowed access ends.
/// Research completion accounts for work; it does not imply implementation equality.
/// </summary>
public abstract class LocalComparisonQueryResult
{
    private protected LocalComparisonQueryResult(
        LocalComparisonQueryIdentity? identity,
        QueryToResearchPopulationReceipt? receipt)
    {
        Identity = identity;
        Receipt = receipt;
    }

    public LocalComparisonQueryIdentity? Identity { get; }
    internal QueryToResearchPopulationReceipt? Receipt { get; }

    public sealed class Published : LocalComparisonQueryResult
    {
        internal Published(
            LocalComparisonQueryIdentity identity,
            QueryToResearchPopulationReceipt receipt,
            ResearchProducerSessionOutcome outcome)
            : base(identity, receipt) => Outcome = outcome;

        public ResearchProducerSessionOutcome Outcome { get; }
    }

    public sealed class NonSuccess : LocalComparisonQueryResult
    {
        internal NonSuccess(
            LocalComparisonQueryIdentity? identity,
            QueryToResearchPopulationReceipt? receipt,
            QueryComparisonSide? side,
            LocalComparisonQueryFailure failure)
            : base(identity, receipt)
        {
            Side = side;
            Failure = failure;
        }

        public QueryComparisonSide? Side { get; }
        public LocalComparisonQueryFailure Failure { get; }
    }
}

/// <summary>Captures association before calling Research, never by inspecting its outcome.</summary>
internal sealed class LocalComparisonPublication(
    LocalComparisonQueryIdentity identity,
    ProjectedQueryPopulation population)
{
    internal LocalComparisonQueryResult.Published Run(
        ResearchProducerSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(request.Population, population.Admission)
            || !ReferenceEquals(identity.Operation, population.Receipt.Operation.Query)
            || !population.Receipt.Questions.ContainsKey(identity.Question)
            || !population.Receipt.Inputs.ContainsKey(identity.Before)
            || !population.Receipt.Inputs.ContainsKey(identity.After))
        {
            throw new ArgumentException(
                "The session must consume this invocation's exact projected population.",
                nameof(request));
        }

        ResearchProducerSessionOutcome outcome =
            ResearchProducerSession.Run(request, cancellationToken);
        return new(identity, population.Receipt, outcome);
    }
}
