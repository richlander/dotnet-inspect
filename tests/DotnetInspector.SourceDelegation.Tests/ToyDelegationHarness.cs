namespace DotnetInspector.SourceDelegation.Tests;

// The in-memory contract harness. It exercises the product protocol through its
// public surface with toy owner types: a caller that owns its plan, partition,
// residual, and completion requirement, and a cooperating source that owns its
// acquisition, dispositions, and proofs.
//
// The toy operations here are harness-owned. Nothing in this file declares or
// implies that any product operation is source-closed; the RowSelection owner
// makes that declaration in its own contract, under its own gate.

// One owner-issued member identity. Equality is token identity; the name is
// diagnostic only.
internal sealed class ToyMember(string name)
{
    public string Name { get; } = name;

    public override string ToString() =>
        Name;
}

// One opaque caller-owned row object. The protocol never clones or inspects it.
internal sealed class ToyRow(string id)
{
    public string Id { get; } = id;

    public override string ToString() =>
        Id;
}

// Already-authorized source input.
internal sealed class ToyInput(string feed)
{
    public string Feed { get; } = feed;
}

// A harness-owned operation the toy source knows how to execute exactly. A
// nonempty prefix means reverse semantic work was delegated; an empty prefix is
// acquisition only.
internal sealed class ToyOperation
{
    private readonly int _count;
    private readonly string? _excludedId;

    private ToyOperation(string name, int count, string? excludedId)
    {
        Name = name;
        _count = count;
        _excludedId = excludedId;
    }

    public string Name { get; }

    public static ToyOperation TakeFirst(int count) =>
        new($"take-first({count})", count, null);

    public static ToyOperation Exclude(string id) =>
        new($"exclude({id})", 0, id);

    public IReadOnlyList<ToyRow> Apply(IReadOnlyList<ToyRow> rows) =>
        _excludedId is null
            ? [.. rows.Take(_count)]
            : [.. rows.Where(row => row.Id != _excludedId)];

    public override string ToString() =>
        Name;
}

// One owner-issued disposition. Reference identity, like the other tokens.
internal sealed class ToyDisposition(string cause)
{
    public static ToyDisposition Acquired { get; } = new("acquired");

    // The source's whole candidate domain is empty: a candidate-wide fact that
    // is simultaneously each member's own fact.
    public static ToyDisposition DomainEmpty { get; } = new("domain-empty");

    public static ToyDisposition ProviderCapped { get; } = new("provider-capped");

    public static ToyDisposition Absent { get; } = new("absent");

    public static ToyDisposition Failed { get; } = new("feed-failed");

    public static ToyDisposition Cancelled { get; } = new("cancelled");

    public string Cause { get; } = cause;

    public override string ToString() =>
        Cause;
}

// One source-owned typed proof.
internal sealed class ToyWitness(string claim)
{
    public string Claim { get; } = claim;

    public override string ToString() =>
        Claim;
}

// The caller-owned completion requirement. It decides usability and exactness
// within the rules the protocol already enforces; it can refuse more, never
// less.
internal sealed class ToyRequirement(string name, bool acceptsCappedRows = true)
    : CompletionRequirement<ToyMember, ToyDisposition, ToyWitness>(
        CompletionRequirementIdentity.Create(name))
{
    // Every context this requirement judged, for assertions about the typed
    // member-and-candidate information it was given.
    public List<ToyContext> Contexts { get; } = [];

    public override bool AcceptsRows(ToyContext context)
    {
        Contexts.Add(context);
        if (context.Disposition == ToyDisposition.Failed || context.Disposition == ToyDisposition.Absent)
            return false;

        if (!ProofReachesMember(context))
            return false;

        // The caller owner's existing source-result contract: whether capped
        // acquisition rows are usable for its exact residual is its decision.
        return context.Evidence.Basis != CompletionEvidenceBasis.IncompleteStop || acceptsCappedRows;
    }

    public override bool AcceptsCount(ToyContext context)
    {
        Contexts.Add(context);
        return (context.Disposition == ToyDisposition.Acquired
                || context.Disposition == ToyDisposition.DomainEmpty)
            && ProofReachesMember(context);
    }

    // A candidate-scoped completion proof establishes this member's own claim
    // only when the source's disposition states a candidate-wide fact that is
    // also each member's fact. A candidate-scoped `Acquired` proof is a group
    // aggregate: it cannot establish an individual member's value. An
    // incomplete stop proves no completion at all, so its scope adds nothing;
    // the capped-rows rule above decides it.
    private static bool ProofReachesMember(ToyContext context) =>
        context.Evidence.Scope == CompletionEvidenceScope.Member
            || context.Evidence.Basis == CompletionEvidenceBasis.IncompleteStop
            || context.Disposition == ToyDisposition.DomainEmpty;
}

internal sealed class ToySource(
    Func<ToyCandidate, bool> supports,
    Func<ToyCandidate, CancellationToken, ValueTask<ToyReply>> execute)
    : IDelegationSource<ToyMember, ToyRow, ToyInput, ToyOperation, ToyDisposition, ToyWitness>
{
    // Every candidate planning inspected, in the order it inspected them.
    public List<ToyCandidate> Planned { get; } = [];

    public int ExecuteCount { get; private set; }

    // Source work of any kind. Planning must never move this counter.
    public int AcquisitionCount { get; private set; }

    public IReadOnlyList<ToyRow> Acquire(IEnumerable<ToyRow> rows)
    {
        AcquisitionCount++;
        return [.. rows];
    }

    public bool Supports(ToyCandidate candidate)
    {
        Planned.Add(candidate);
        return supports(candidate);
    }

    public ValueTask<ToyReply> ExecuteAsync(ToyCandidate candidate, CancellationToken cancellationToken)
    {
        ExecuteCount++;
        return execute(candidate, cancellationToken);
    }
}

// The caller side: it owns the complete plan, the partition, the residual, and
// residual admission. The protocol never sees any of it.
internal sealed class ToyCaller
{
    private readonly Dictionary<ToyCandidate, Func<ToyMember, IReadOnlyList<ToyOperation>>> _retained = [];

    public List<ToyMember> ResidualInvocations { get; } = [];

    public bool SuppressResiduals { get; set; }

    // The caller keeps its own residual beside the candidate it formed; the
    // source never receives either.
    public ToyCandidate Retain(
        ToyCandidate candidate,
        Func<ToyMember, IReadOnlyList<ToyOperation>> residual)
    {
        _retained[candidate] = residual;
        return candidate;
    }

    // Only Rows-usable entries are eligible for the retained residual, and the
    // owning composition may suppress every invocation. With no explicit
    // residual, the published candidate association selects the one this caller
    // retained for exactly that candidate.
    public Dictionary<ToyMember, IReadOnlyList<ToyRow>> AdmitToResidual(
        ToyHandoff handoff,
        Func<ToyMember, IReadOnlyList<ToyOperation>>? residual = null)
    {
        Dictionary<ToyMember, IReadOnlyList<ToyRow>> admitted = [];
        if (SuppressResiduals)
            return admitted;

        Func<ToyMember, IReadOnlyList<ToyOperation>> retained = residual ?? _retained[handoff.Candidate];
        foreach (ToyRowValues usable in handoff.UsableOutcomes)
        {
            ResidualInvocations.Add(usable.Member);
            admitted[usable.Member] = ToyDelegation.Execute(usable.Values, retained(usable.Member));
        }

        return admitted;
    }
}

internal static class ToyDelegation
{
    public static readonly IReadOnlyList<ToyOperation> NoOperations = [];

    public static readonly SourceCapability RowPrefix = SourceCapability.Create("toy.row-prefix");

    public static readonly SourceCapability ExactCount = SourceCapability.Create("toy.exact-count");

    public static readonly SourceCapability Unsupported = SourceCapability.Create("toy.unsupported");

    public static ToyMember Member(string name) =>
        new(name);

    public static IReadOnlyList<ToyRow> Rows(params string[] ids) =>
        [.. ids.Select(id => new ToyRow(id))];

    public static ToyGroup Group(params ToyMember[] members) =>
        ToyGroup.Create(members);

    public static IReadOnlyList<ToyRow> Execute(
        IReadOnlyList<ToyRow> rows,
        IReadOnlyList<ToyOperation> operations)
    {
        IReadOnlyList<ToyRow> current = rows;
        foreach (ToyOperation operation in operations)
            current = operation.Apply(current);

        return current;
    }

    public static ToyCandidate RowHandoff(
        ToyGroup group,
        CompletionRequirement<ToyMember, ToyDisposition, ToyWitness> requirement,
        Func<ToyMember, IReadOnlyList<ToyOperation>>? prefix = null,
        SourceCapability? capability = null,
        string feed = "toy-feed") =>
        DelegationCandidate.RowHandoff(
            new ToyInput(feed),
            group,
            capability ?? RowPrefix,
            requirement,
            prefix ?? (_ => NoOperations));

    public static ToyCandidate CountCandidate(
        ToyGroup group,
        CompletionRequirement<ToyMember, ToyDisposition, ToyWitness> requirement,
        Func<ToyMember, IReadOnlyList<ToyOperation>>? prefix = null,
        SourceCapability? capability = null,
        string feed = "toy-feed") =>
        DelegationCandidate.ExactCount(
            new ToyInput(feed),
            group,
            capability ?? ExactCount,
            requirement,
            prefix ?? (_ => NoOperations));

    public static ValueTask<ToyOutcome> RunAsync(ToySource source, params ToyCandidate[] candidates) =>
        SourceDelegationRunner.RunAsync(source, candidates);
}

// Shorthand constructors for the toy owner's evidence and offers.
internal static class Evidence
{
    public static ToyEvidence Exhaustion(
        CompletionEvidenceScope scope = CompletionEvidenceScope.Member) =>
        ToyEvidence.LogicalExhaustion(scope);

    public static ToyEvidence Witness(
        CompletionRequirement<ToyMember, ToyDisposition, ToyWitness> requirement,
        string claim,
        CompletionEvidenceScope scope = CompletionEvidenceScope.Member) =>
        ToyEvidence.RequirementWitness(requirement.Identity, new ToyWitness(claim), scope);

    public static ToyEvidence Witness(
        CompletionRequirementIdentity identity,
        string claim,
        CompletionEvidenceScope scope = CompletionEvidenceScope.Member) =>
        ToyEvidence.RequirementWitness(identity, new ToyWitness(claim), scope);

    public static ToyEvidence Stop(
        CompletionEvidenceScope scope = CompletionEvidenceScope.Member) =>
        ToyEvidence.IncompleteStop(scope);

    public static ToyEvidence Unavailable(
        CompletionEvidenceScope scope = CompletionEvidenceScope.Member) =>
        ToyEvidence.UnavailableSourceOutcome(scope);
}

internal static class Cause
{
    public static ToyCause Of(ToyDisposition disposition, ToyEvidence evidence) =>
        DelegationCause.Create(disposition, evidence);
}

// Source-side reply construction. A reply carries no candidate association of
// its own; the runner binds the published result to the candidate it selected.
internal static class ToyExecution
{
    public static ValueTask<ToyReply> Handoff(
        ToyCandidate candidate,
        Func<ToyMember, ToyRowOffer> outcome) =>
        ValueTask.FromResult<ToyReply>(candidate.ReplyWithRows(outcome));

    public static ValueTask<ToyReply> Counts(
        ToyCandidate candidate,
        Func<ToyMember, ToyCountOffer> count) =>
        ValueTask.FromResult(candidate.ReplyWithCounts<ToyRow>(count));

    public static ValueTask<ToyReply> NotSatisfied(ToyCandidate candidate, ToyCause candidateCause) =>
        ValueTask.FromResult<ToyReply>(candidate.ReplyNotSatisfied<ToyRow>(candidateCause));
}

internal static class Offer
{
    public static ToyRowOffer Rows(
        IEnumerable<ToyRow> values,
        ToyDisposition disposition,
        ToyEvidence evidence) =>
        ToyRowOffer.Rows(values, Cause.Of(disposition, evidence));

    public static ToyRowOffer Rows(IEnumerable<ToyRow> values, ToyCause cause) =>
        ToyRowOffer.Rows(values, cause);

    public static ToyRowOffer Unavailable(ToyDisposition disposition, ToyEvidence evidence) =>
        ToyRowOffer.Unavailable(Cause.Of(disposition, evidence));

    public static ToyRowOffer Unavailable(ToyCause cause) =>
        ToyRowOffer.Unavailable(cause);

    public static ToyCountOffer Count(int count, ToyDisposition disposition, ToyEvidence evidence) =>
        ToyCountOffer.Exact(count, Cause.Of(disposition, evidence));

    public static ToyCountOffer Count(int count, ToyCause cause) =>
        ToyCountOffer.Exact(count, cause);

    public static ToyCountOffer NoCount(ToyDisposition disposition, ToyEvidence evidence) =>
        ToyCountOffer.NotExact(Cause.Of(disposition, evidence));
}
