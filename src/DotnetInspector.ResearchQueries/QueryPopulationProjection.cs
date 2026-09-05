using System.Collections.Immutable;

using ILInspector.Research;

namespace DotnetInspector.Queries;

internal enum QueryPopulationProjectionRejection
{
    ProfileMismatch,
    OperationMismatch,
    QuestionMappingMismatch,
    InputMappingMismatch,
}

internal sealed record QueryResearchOperationCorrespondence(
    QueryComparisonOperationId Query,
    ResearchComparisonOperationId Research);

internal sealed record QueryResearchQuestionCorrespondence(
    QueryComparisonQuestionId Query,
    ResearchComparisonQuestionId Research);

internal sealed record QueryResearchInputCorrespondence(
    QueryComparisonInputId Query,
    ResearchComparisonInputId Research,
    QueryComparisonSide Side);

internal sealed record ProjectedQueryPopulation(
    ResearchAdmittedPopulation Admission,
    QueryToResearchPopulationReceipt Receipt);

internal abstract record QueryPopulationProjectionOutcome
{
    private protected QueryPopulationProjectionOutcome() { }

    internal sealed record Projected(ProjectedQueryPopulation Population)
        : QueryPopulationProjectionOutcome;

    internal sealed record Rejected(QueryPopulationProjectionRejection Reason)
        : QueryPopulationProjectionOutcome;

    internal sealed record AdmissionRejected(ResearchAdmissionRejection Rejection)
        : QueryPopulationProjectionOutcome;
}

/// <summary>
/// Ephemeral association between sealed inputs and the occurrences sent to Research.
/// Only the identity-only receipt survives outside the projected admission.
/// </summary>
internal sealed class QueryPopulationProjection
{
    QueryPopulationProjection(
        QueryComparisonPopulation population,
        ImmutableDictionary<QueryComparisonInputId, ResearchComparisonInputOccurrence> occurrences)
    {
        Population = population;
        Occurrences = occurrences;
        Request = new ResearchComparisonAdmissionRequest(
            ResearchProfile(population.Profile),
            [new ResearchComparisonAdmissionQuestion(
                population.InputIds
                    .Where(id => id.Side == QueryComparisonSide.Before)
                    .Select(id => occurrences[id]),
                population.InputIds
                    .Where(id => id.Side == QueryComparisonSide.After)
                    .Select(id => occurrences[id]))]);
    }

    internal QueryComparisonPopulation Population { get; }
    internal ResearchComparisonAdmissionRequest Request { get; }
    internal ImmutableDictionary<QueryComparisonInputId, ResearchComparisonInputOccurrence>
        Occurrences { get; }

    internal static QueryPopulationProjection Prepare(QueryComparisonPopulation population)
    {
        ArgumentNullException.ThrowIfNull(population);
        var occurrences = ImmutableDictionary.CreateBuilder<
            QueryComparisonInputId, ResearchComparisonInputOccurrence>(
                ReferenceEqualityComparer.Instance);
        switch (population)
        {
            case QueryComparisonPopulation<ImplementationComparisonBinding> implementation:
                foreach (var input in implementation.Inputs)
                {
                    occurrences.Add(input.Id, new ImplementationComparisonInputOccurrence(
                        input.Binding.Assembly, input.Binding.Resolver, input.Binding.BodyIndex));
                }
                break;
            case QueryComparisonPopulation<BodySignalComparisonBinding> bodySignal:
                foreach (var input in bodySignal.Inputs)
                {
                    occurrences.Add(input.Id,
                        new BodySignalComparisonInputOccurrence(input.Binding.BodyIndex));
                }
                break;
            default:
                throw new ArgumentException("Unsupported sealed comparison profile.", nameof(population));
        }
        return new(population, occurrences.ToImmutable());
    }

    internal static QueryPopulationProjectionOutcome Execute(QueryComparisonPopulation population)
    {
        QueryPopulationProjection projection = Prepare(population);
        return ResearchComparisonAdmission.Admit(projection.Request) switch
        {
            ResearchAdmissionOutcome.Admitted admitted =>
                projection.Complete(admitted.Population),
            ResearchAdmissionOutcome.Rejected rejected =>
                new QueryPopulationProjectionOutcome.AdmissionRejected(rejected.Rejection),
            _ => throw new InvalidOperationException("Unknown Research admission outcome."),
        };
    }

    internal QueryPopulationProjectionOutcome Complete(ResearchAdmittedPopulation admitted)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        if (admitted.Questions.Length != 1)
            return new QueryPopulationProjectionOutcome.Rejected(
                QueryPopulationProjectionRejection.QuestionMappingMismatch);

        // One sealing invocation is exactly one question, including empty sides.
        // This selects the unique question, not an ordinal correspondence.
        ResearchComparisonQuestionId question = admitted.Questions.Single().Id;
        var inputs = ImmutableArray.CreateBuilder<QueryResearchInputCorrespondence>();
        foreach (QueryComparisonInputId id in Population.InputIds)
        {
            if (!admitted.TryGetInput(Occurrences[id], out ResearchAdmittedInput? input))
                return new QueryPopulationProjectionOutcome.Rejected(
                    QueryPopulationProjectionRejection.InputMappingMismatch);
            inputs.Add(new(id, input.Id, id.Side));
        }

        return QueryToResearchPopulationReceipt.Create(
            this, admitted, new(Population.Operation, admitted.Operation),
            [new(Population.Question, question)], inputs.ToImmutable());
    }

    internal static ResearchComparisonProfile ResearchProfile(QueryComparisonProfile profile)
        => profile switch
        {
            QueryComparisonProfile.ImplementationComparison =>
                ResearchComparisonProfile.ImplementationComparison,
            QueryComparisonProfile.BodySignal => ResearchComparisonProfile.BodySignal,
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };

    internal static ResearchComparisonSide ResearchSide(QueryComparisonSide side)
        => side switch
        {
            QueryComparisonSide.Before => ResearchComparisonSide.Before,
            QueryComparisonSide.After => ResearchComparisonSide.After,
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };
}

/// <summary>
/// Immutable, identity-only maps for one complete query-to-Research projection.
/// Construction validates the maps against both populations and the original
/// occurrence association; equal borrowed values cannot substitute for that join.
/// </summary>
internal sealed class QueryToResearchPopulationReceipt
{
    QueryToResearchPopulationReceipt(
        QueryComparisonProfile profile,
        QueryResearchOperationCorrespondence operation,
        ImmutableArray<QueryResearchQuestionCorrespondence> questions,
        ImmutableArray<QueryResearchInputCorrespondence> inputs)
    {
        Profile = profile;
        Operation = operation;
        Questions = questions.ToImmutableDictionary(
            pair => pair.Query, pair => pair.Research,
            (IEqualityComparer<QueryComparisonQuestionId>)ReferenceEqualityComparer.Instance);
        Inputs = inputs.ToImmutableDictionary(
            pair => pair.Query, pair => pair,
            (IEqualityComparer<QueryComparisonInputId>)ReferenceEqualityComparer.Instance);
    }

    internal QueryComparisonProfile Profile { get; }
    internal QueryResearchOperationCorrespondence Operation { get; }
    internal ImmutableDictionary<QueryComparisonQuestionId, ResearchComparisonQuestionId>
        Questions { get; }
    internal ImmutableDictionary<QueryComparisonInputId, QueryResearchInputCorrespondence>
        Inputs { get; }

    internal static QueryPopulationProjectionOutcome Create(
        QueryPopulationProjection projection,
        ResearchAdmittedPopulation admitted,
        QueryResearchOperationCorrespondence operation,
        ImmutableArray<QueryResearchQuestionCorrespondence> questions,
        ImmutableArray<QueryResearchInputCorrespondence> inputs)
    {
        QueryComparisonPopulation population = projection.Population;
        if (admitted.Profile != QueryPopulationProjection.ResearchProfile(population.Profile))
            return Reject(QueryPopulationProjectionRejection.ProfileMismatch);
        if (!ReferenceEquals(operation.Query, population.Operation)
            || !ReferenceEquals(operation.Research, admitted.Operation))
            return Reject(QueryPopulationProjectionRejection.OperationMismatch);

        if (questions.IsDefault || questions.Length != 1 || admitted.Questions.Length != 1)
            return Reject(QueryPopulationProjectionRejection.QuestionMappingMismatch);
        QueryResearchQuestionCorrespondence question = questions.Single();
        ResearchAdmittedQuestion admittedQuestion = admitted.Questions.Single();
        if (!ReferenceEquals(question.Query, population.Question)
            || !ReferenceEquals(question.Research, admittedQuestion.Id)
            || !ReferenceEquals(question.Query.Operation, operation.Query)
            || !ReferenceEquals(question.Research.Operation, operation.Research))
            return Reject(QueryPopulationProjectionRejection.QuestionMappingMismatch);

        if (inputs.IsDefault || inputs.Length != population.InputIds.Length
            || inputs.Length != admitted.Inputs.Length
            || projection.Occurrences.Count != population.InputIds.Length)
            return Reject(QueryPopulationProjectionRejection.InputMappingMismatch);
        HashSet<QueryComparisonInputId> domain = new(ReferenceEqualityComparer.Instance);
        HashSet<ResearchComparisonInputId> range = new(ReferenceEqualityComparer.Instance);
        foreach (QueryResearchInputCorrespondence pair in inputs)
        {
            if (!domain.Add(pair.Query) || !range.Add(pair.Research)
                || !projection.Occurrences.TryGetValue(pair.Query, out var occurrence)
                || !admitted.TryGetInput(occurrence, out var ownerInput)
                || !ReferenceEquals(ownerInput.Id, pair.Research)
                || !ReferenceEquals(pair.Query.Operation, operation.Query)
                || !ReferenceEquals(pair.Query.Question, question.Query)
                || !ReferenceEquals(pair.Research.Operation, operation.Research)
                || !ReferenceEquals(pair.Research.Question, question.Research)
                || pair.Side != pair.Query.Side
                || pair.Research.Side != QueryPopulationProjection.ResearchSide(pair.Side))
                return Reject(QueryPopulationProjectionRejection.InputMappingMismatch);
        }
        if (!domain.SetEquals(population.InputIds)
            || !range.SetEquals(admitted.Inputs.Select(input => input.Id)))
            return Reject(QueryPopulationProjectionRejection.InputMappingMismatch);

        return new QueryPopulationProjectionOutcome.Projected(
            new(admitted, new QueryToResearchPopulationReceipt(
                population.Profile, operation, questions, inputs)));
    }

    static QueryPopulationProjectionOutcome.Rejected Reject(
        QueryPopulationProjectionRejection reason) => new(reason);
}
