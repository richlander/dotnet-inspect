namespace DotnetInspector.SourceDelegation;

// The source-owned basis of one completion-evidence value. The basis is what a
// source proved, never what its numbers happen to look like: caps, page sizes,
// row counts, and terminal tokens are operational bounds and construct
// incomplete-stop evidence at most.
public enum CompletionEvidenceBasis
{
    // The adopted source contract proved that no further value exists in the
    // source domain named by the candidate.
    LogicalExhaustion,

    // The source produced the typed witness required by the caller's
    // completion requirement.
    RequirementWitness,

    // A provider, page, work, time, memory, or acquisition bound, or
    // cancellation, stopped execution without proving exact completion.
    IncompleteStop,

    // An expected source failure, or an absent member or candidate domain,
    // prevented a usable or exact result.
    UnavailableSourceOutcome,
}

// What one evidence value speaks for. Member-scoped evidence is the default;
// one candidate-scoped value may carry the same candidate-wide proof, stop, or
// failure for every member. Whether a candidate-scoped basis establishes an
// individual member's own claim, or is only a group aggregate that cannot, is
// the caller requirement's judgment, made with the member and candidate
// context.
public enum CompletionEvidenceScope
{
    Member,
    Candidate,
}

// Immutable completion evidence, carried by the member entry it proves.
// Evidence carries no counts: numbers reach the caller only inside an
// owner-issued typed witness.
public sealed class CompletionEvidence<TWitness>
{
    private CompletionEvidence(
        CompletionEvidenceBasis basis,
        CompletionEvidenceScope scope,
        CompletionRequirementIdentity? requirement,
        TWitness? witness)
    {
        Basis = basis;
        Scope = scope;
        Requirement = requirement;
        Witness = witness;
    }

    public CompletionEvidenceBasis Basis { get; }

    public CompletionEvidenceScope Scope { get; }

    // Non-null only for requirement-witness evidence: the caller's requirement
    // the witness answers. A source never selects a requirement of its own.
    public CompletionRequirementIdentity? Requirement { get; }

    public TWitness? Witness { get; }

    public static CompletionEvidence<TWitness> LogicalExhaustion(
        CompletionEvidenceScope scope) =>
        new(CompletionEvidenceBasis.LogicalExhaustion, scope, null, default);

    public static CompletionEvidence<TWitness> RequirementWitness(
        CompletionRequirementIdentity requirement,
        TWitness witness,
        CompletionEvidenceScope scope = CompletionEvidenceScope.Member)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(witness);
        return new(CompletionEvidenceBasis.RequirementWitness, scope, requirement, witness);
    }

    public static CompletionEvidence<TWitness> IncompleteStop(
        CompletionEvidenceScope scope) =>
        new(CompletionEvidenceBasis.IncompleteStop, scope, null, default);

    public static CompletionEvidence<TWitness> UnavailableSourceOutcome(
        CompletionEvidenceScope scope) =>
        new(CompletionEvidenceBasis.UnavailableSourceOutcome, scope, null, default);
}
