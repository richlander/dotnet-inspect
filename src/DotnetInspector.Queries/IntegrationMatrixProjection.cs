using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Whether one incident participant/context/concept cell is complete.
/// </summary>
public abstract class IntegrationMatrixCellState
{
    private protected IntegrationMatrixCellState()
    {
    }

    public sealed class Complete : IntegrationMatrixCellState
    {
        internal Complete()
        {
        }
    }

    public sealed class Incomplete : IntegrationMatrixCellState
    {
        internal Incomplete(
            IntegrationSourceParticipantAttempt? sourceAttempt,
            ImmutableArray<IntegrationProducerPolicyAttempt>
                producerPolicyAttempts,
            ImmutableArray<IntegrationCandidateAttempt.Failed>
                candidateAttempts)
        {
            if (sourceAttempt
                is IntegrationSourceParticipantAttempt.Available)
            {
                throw new ArgumentException(
                    "An available source participant is not an incomplete-cell cause.",
                    nameof(sourceAttempt));
            }
            if (producerPolicyAttempts.Any(
                    static attempt =>
                        attempt is IntegrationProducerPolicyAttempt.Completed))
            {
                throw new ArgumentException(
                    "A completed producer policy is not an incomplete-cell cause.",
                    nameof(producerPolicyAttempts));
            }
            if (sourceAttempt is null
                && producerPolicyAttempts.IsEmpty
                && candidateAttempts.IsEmpty)
            {
                throw new ArgumentException(
                    "An incomplete matrix cell requires at least one retained cause.");
            }

            SourceAttempt = sourceAttempt;
            ProducerPolicyAttempts = producerPolicyAttempts;
            CandidateAttempts = candidateAttempts;
        }

        public IntegrationSourceParticipantAttempt? SourceAttempt { get; }
        public ImmutableArray<IntegrationProducerPolicyAttempt>
            ProducerPolicyAttempts { get; }
        public ImmutableArray<IntegrationCandidateAttempt.Failed>
            CandidateAttempts { get; }
    }
}

/// <summary>
/// One concept cell in an incident Integration matrix row.
/// </summary>
public sealed class IntegrationMatrixCell
{
    internal IntegrationMatrixCell(
        IntegrationSourceParticipantIdentity participant,
        IIntegrationBindingContextIdentity bindingContext,
        IntegrationConceptDescriptor concept,
        ImmutableArray<IntegrationCandidateAttempt.Classified>
            classifiedAttempts,
        IntegrationMatrixCellState state)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(bindingContext);
        ArgumentNullException.ThrowIfNull(concept);
        ArgumentNullException.ThrowIfNull(state);
        if (classifiedAttempts.Any(attempt =>
                !Matches(
                    attempt.Address,
                    participant,
                    bindingContext,
                    concept)))
        {
            throw new ArgumentException(
                "Classified attempts must match the matrix cell coordinates.",
                nameof(classifiedAttempts));
        }
        if (state is IntegrationMatrixCellState.Incomplete incomplete)
        {
            ValidateIncompleteState(
                incomplete,
                participant,
                bindingContext,
                concept);
        }

        Concept = concept;
        ClassifiedAttempts = classifiedAttempts;
        State = state;
        InCount = classifiedAttempts.Count(
            static attempt =>
                attempt.Disposition is IntegrationCandidateDisposition.In);
        OutCount = classifiedAttempts.Length - InCount;
    }

    public IntegrationConceptDescriptor Concept { get; }
    public ImmutableArray<IntegrationCandidateAttempt.Classified>
        ClassifiedAttempts { get; }
    public int InCount { get; }
    public int OutCount { get; }
    public int TotalCount => ClassifiedAttempts.Length;
    public IntegrationMatrixCellState State { get; }
    public bool IsComplete => State is IntegrationMatrixCellState.Complete;

    static void ValidateIncompleteState(
        IntegrationMatrixCellState.Incomplete state,
        IntegrationSourceParticipantIdentity participant,
        IIntegrationBindingContextIdentity bindingContext,
        IntegrationConceptDescriptor concept)
    {
        if (state.SourceAttempt is { } sourceAttempt
            && !sourceAttempt.Participant.Equals(participant))
        {
            throw new ArgumentException(
                "The source failure must match the matrix row participant.",
                nameof(state));
        }
        if (state.ProducerPolicyAttempts.Any(attempt =>
                !attempt.Address.Participant.Equals(participant)
                || !attempt.Address.Policy.Policy.Concepts.Contains(
                    concept,
                    ReferenceEqualityComparer.Instance)))
        {
            throw new ArgumentException(
                "Producer-policy failures must match the matrix participant and concept.",
                nameof(state));
        }
        if (state.CandidateAttempts.Any(attempt =>
                !Matches(
                    attempt.Address,
                    participant,
                    bindingContext,
                    concept)))
        {
            throw new ArgumentException(
                "Candidate failures must match the matrix cell coordinates.",
                nameof(state));
        }
    }

    static bool Matches(
        IntegrationCandidateAttemptAddress address,
        IntegrationSourceParticipantIdentity participant,
        IIntegrationBindingContextIdentity bindingContext,
        IntegrationConceptDescriptor concept) =>
        address.Candidate.Source.Participant.Equals(participant)
        && EqualityComparer<IIntegrationBindingContextIdentity>.Default.Equals(
            address.BindingContext,
            bindingContext)
        && ReferenceEquals(address.Candidate.Concept, concept);
}

/// <summary>
/// One source-participant and binding-context row in the sparse Integration
/// matrix.
/// </summary>
public sealed class IntegrationMatrixRow
{
    internal IntegrationMatrixRow(
        IntegrationSourceParticipantIdentity participant,
        IIntegrationBindingContextIdentity bindingContext,
        ImmutableArray<IntegrationMatrixCell> cells)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(bindingContext);
        Participant = participant;
        BindingContext = bindingContext;
        Cells = cells;
    }

    public IntegrationSourceParticipantIdentity Participant { get; }
    public IIntegrationBindingContextIdentity BindingContext { get; }
    public ImmutableArray<IntegrationMatrixCell> Cells { get; }
    public bool IsComplete => Cells.All(static cell => cell.IsComplete);
}

/// <summary>
/// The typed sparse-matrix payload for one independently validated Integration
/// projection.
/// </summary>
public sealed class IntegrationMatrixProjectionResult :
    IntegrationCensusProjectionResult
{
    internal IntegrationMatrixProjectionResult(
        AnalysisRequestPlan plan,
        IntegrationCensusSnapshot snapshot,
        ImmutableArray<IntegrationMatrixRow> rows)
        : base(RequireMatrixPlan(plan), snapshot)
    {
        Rows = rows;
    }

    public ImmutableArray<IntegrationMatrixRow> Rows { get; }
    public IAnalysisUniverseCompleteness UniverseCompleteness =>
        Snapshot.UniverseCompleteness;
    public ImmutableArray<IAnalysisUniverseFailure> UniverseFailures =>
        Snapshot.UniverseFailures;

    internal static AnalysisRequestPlan RequireMatrixPlan(
        AnalysisRequestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReferenceEquals(
                plan.Projection,
                IntegrationAnalysisCatalog.Matrix))
        {
            throw new ArgumentException(
                "Integration matrix projection requires the configured matrix projection.",
                nameof(plan));
        }

        return plan;
    }
}

/// <summary>
/// Projects one compatible Census snapshot into incident
/// participant/context rows and catalog-ordered concept cells.
/// </summary>
public static class IntegrationMatrixProjection
{
    public static IntegrationMatrixProjectionResult Project(
        AnalysisRequestPlan plan,
        IntegrationCensusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);
        IntegrationMatrixProjectionResult.RequireMatrixPlan(plan);
        if (!snapshot.IsCompatibleWith(plan))
        {
            throw new ArgumentException(
                "The Integration matrix request is not compatible with the Census snapshot.",
                nameof(plan));
        }

        Dictionary<
            IntegrationSourceParticipantIdentity,
            IntegrationSourceParticipantAttempt> sourceAttempts =
            snapshot.SourceAttempts.ToDictionary(
                static attempt => attempt.Participant);
        Dictionary<ParticipantConceptKey, List<IntegrationProducerPolicyAttempt>>
            producerFailures = IndexProducerFailures(snapshot);
        Dictionary<CellKey, List<IntegrationCandidateAttempt.Classified>>
            classifiedAttempts = [];
        Dictionary<CellKey, List<IntegrationCandidateAttempt.Failed>>
            candidateFailures = [];
        foreach (IntegrationCandidateAttempt attempt
            in snapshot.CandidateAttempts)
        {
            CellKey key = CellKey.For(attempt.Address);
            switch (attempt)
            {
                case IntegrationCandidateAttempt.Classified classified:
                    Add(classifiedAttempts, key, classified);
                    break;
                case IntegrationCandidateAttempt.Failed failed:
                    Add(candidateFailures, key, failed);
                    break;
            }
        }

        var rows = ImmutableArray.CreateBuilder<IntegrationMatrixRow>();
        foreach (IntegrationSourceBindingContextIncidence incidence
            in snapshot.SourceContextIncidence)
        {
            IntegrationSourceParticipantAttempt sourceAttempt =
                sourceAttempts[incidence.Participant];
            foreach (IIntegrationBindingContextIdentity bindingContext
                in incidence.BindingContexts)
            {
                var cells =
                    ImmutableArray.CreateBuilder<IntegrationMatrixCell>(
                        IntegrationAnalysisCatalog.Concepts.Length);
                foreach (IntegrationConceptDescriptor concept
                    in IntegrationAnalysisCatalog.Concepts)
                {
                    CellKey cellKey = new(
                        incidence.Participant,
                        bindingContext,
                        concept);
                    ImmutableArray<
                        IntegrationCandidateAttempt.Classified> classified =
                        Get(classifiedAttempts, cellKey);
                    ImmutableArray<
                        IntegrationCandidateAttempt.Failed> failed =
                        Get(candidateFailures, cellKey);
                    ImmutableArray<IntegrationProducerPolicyAttempt>
                        policyFailures = Get(
                            producerFailures,
                            new ParticipantConceptKey(
                                incidence.Participant,
                                concept));
                    IntegrationSourceParticipantAttempt? sourceFailure =
                        sourceAttempt
                            is IntegrationSourceParticipantAttempt.Available
                                ? null
                                : sourceAttempt;
                    IntegrationMatrixCellState state =
                        sourceFailure is null
                        && policyFailures.IsEmpty
                        && failed.IsEmpty
                            ? new IntegrationMatrixCellState.Complete()
                            : new IntegrationMatrixCellState.Incomplete(
                                sourceFailure,
                                policyFailures,
                                failed);
                    cells.Add(
                        new IntegrationMatrixCell(
                            incidence.Participant,
                            bindingContext,
                            concept,
                            classified,
                            state));
                }

                rows.Add(
                    new IntegrationMatrixRow(
                        incidence.Participant,
                        bindingContext,
                        cells.MoveToImmutable()));
            }
        }

        return new IntegrationMatrixProjectionResult(
            plan,
            snapshot,
            rows.ToImmutable());
    }

    static Dictionary<
        ParticipantConceptKey,
        List<IntegrationProducerPolicyAttempt>> IndexProducerFailures(
            IntegrationCensusSnapshot snapshot)
    {
        Dictionary<
            ParticipantConceptKey,
            List<IntegrationProducerPolicyAttempt>> failures = [];
        foreach (IntegrationProducerPolicyAttempt attempt
            in snapshot.ProducerPolicyAttempts)
        {
            if (attempt is IntegrationProducerPolicyAttempt.Completed)
                continue;

            foreach (IntegrationConceptDescriptor concept
                in attempt.Address.Policy.Policy.Concepts)
            {
                Add(
                    failures,
                    new ParticipantConceptKey(
                        attempt.Address.Participant,
                        concept),
                    attempt);
            }
        }
        return failures;
    }

    static void Add<TKey, TValue>(
        Dictionary<TKey, List<TValue>> index,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out List<TValue>? values))
        {
            values = [];
            index.Add(key, values);
        }
        values.Add(value);
    }

    static ImmutableArray<TValue> Get<TKey, TValue>(
        Dictionary<TKey, List<TValue>> index,
        TKey key)
        where TKey : notnull =>
        index.TryGetValue(key, out List<TValue>? values)
            ? [.. values]
            : [];

    readonly struct ParticipantConceptKey :
        IEquatable<ParticipantConceptKey>
    {
        public ParticipantConceptKey(
            IntegrationSourceParticipantIdentity participant,
            IntegrationConceptDescriptor concept)
        {
            Participant = participant;
            Concept = concept;
        }

        public IntegrationSourceParticipantIdentity Participant { get; }
        public IntegrationConceptDescriptor Concept { get; }

        public bool Equals(ParticipantConceptKey other) =>
            Participant.Equals(other.Participant)
            && ReferenceEquals(Concept, other.Concept);

        public override bool Equals(object? obj) =>
            obj is ParticipantConceptKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                Participant,
                RuntimeHelpers.GetHashCode(Concept));
    }

    readonly struct CellKey : IEquatable<CellKey>
    {
        public CellKey(
            IntegrationSourceParticipantIdentity participant,
            IIntegrationBindingContextIdentity bindingContext,
            IntegrationConceptDescriptor concept)
        {
            Participant = participant;
            BindingContext = bindingContext;
            Concept = concept;
        }

        public IntegrationSourceParticipantIdentity Participant { get; }
        public IIntegrationBindingContextIdentity BindingContext { get; }
        public IntegrationConceptDescriptor Concept { get; }

        public static CellKey For(
            IntegrationCandidateAttemptAddress address) =>
            new(
                address.Candidate.Source.Participant,
                address.BindingContext,
                address.Candidate.Concept);

        public bool Equals(CellKey other) =>
            Participant.Equals(other.Participant)
            && EqualityComparer<IIntegrationBindingContextIdentity>
                .Default.Equals(
                    BindingContext,
                    other.BindingContext)
            && ReferenceEquals(Concept, other.Concept);

        public override bool Equals(object? obj) =>
            obj is CellKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                Participant,
                EqualityComparer<IIntegrationBindingContextIdentity>
                    .Default.GetHashCode(BindingContext),
                RuntimeHelpers.GetHashCode(Concept));
    }
}
