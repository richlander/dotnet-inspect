namespace DotnetInspector.SourceDelegation;

// The owner-issued identity of one caller-owned completion requirement.
// Equality is token identity; the name is diagnostic only.
public sealed class CompletionRequirementIdentity
{
    private CompletionRequirementIdentity(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public static CompletionRequirementIdentity Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(name);
    }

    public override string ToString() =>
        Name;
}

// The typed member-and-candidate context one completion decision is made in. It
// carries enough structure for a caller requirement to tell a candidate-wide
// proof that establishes this member's own claim from a group aggregate that
// cannot establish individual member values, and to judge one member of a
// mixed-prefix candidate.
public sealed class CompletionContext<TMember, TDisposition, TWitness>
    where TMember : notnull
{
    private readonly int _memberIndex;
    private readonly bool[] _delegatedPrefixIsEmpty;

    internal CompletionContext(
        DelegationGroup<TMember> group,
        int memberIndex,
        bool[] delegatedPrefixIsEmpty,
        DelegationResultShape shape,
        DelegationCause<TDisposition, TWitness> cause)
    {
        Group = group;
        _memberIndex = memberIndex;
        _delegatedPrefixIsEmpty = delegatedPrefixIsEmpty;
        Shape = shape;
        Cause = cause;
    }

    public DelegationGroup<TMember> Group { get; }

    public TMember Member => Group.Members[_memberIndex];

    public DelegationResultShape Shape { get; }

    public DelegationCause<TDisposition, TWitness> Cause { get; }

    public TDisposition Disposition => Cause.Disposition;

    public CompletionEvidence<TWitness> Evidence => Cause.Evidence;

    public bool DelegatedPrefixIsEmpty => _delegatedPrefixIsEmpty[_memberIndex];

    public bool CandidateIsAcquisitionOnly =>
        Array.TrueForAll(_delegatedPrefixIsEmpty, empty => empty);

    public bool DelegatedPrefixIsEmptyFor(TMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        int index = Group.IndexOf(member);
        return index < 0
            ? throw new ArgumentException(
                "The member is not part of this candidate's execution group.",
                nameof(member))
            : _delegatedPrefixIsEmpty[index];
    }
}

// The caller-owned typed requirement that decides, for the exact candidate and
// the exact retained residual the caller formed, which disposition-and-evidence
// pairs make a member's rows usable or its count exact.
public abstract class CompletionRequirement<TMember, TDisposition, TWitness>
    where TMember : notnull
{
    protected CompletionRequirement(CompletionRequirementIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Identity = identity;
    }

    public CompletionRequirementIdentity Identity { get; }

    public abstract bool AcceptsRows(CompletionContext<TMember, TDisposition, TWitness> context);

    public abstract bool AcceptsCount(CompletionContext<TMember, TDisposition, TWitness> context);
}

// The contract-owned half of evidence acceptance. Exhaustion and witnesses are
// referred to the caller's requirement, which holds the member and candidate
// context needed to judge scope sufficiency; the rules below are the ones no
// requirement may loosen.
internal static class CompletionAcceptance
{
    public static bool RowsAreUsable<TMember, TDisposition, TWitness>(
        CompletionRequirement<TMember, TDisposition, TWitness> requirement,
        CompletionContext<TMember, TDisposition, TWitness> context)
        where TMember : notnull =>
        context.Evidence.Basis switch
        {
            CompletionEvidenceBasis.LogicalExhaustion =>
                requirement.AcceptsRows(context),

            // A witness answers the caller's requirement, never one the source
            // selected for itself.
            CompletionEvidenceBasis.RequirementWitness =>
                ReferenceEquals(context.Evidence.Requirement, requirement.Identity)
                    && requirement.AcceptsRows(context),

            // An incomplete stop proves no completion. It can leave rows usable
            // only where no reverse semantic work happened — this member's own
            // empty delegated prefix — and the caller owner's existing
            // source-result contract then decides.
            CompletionEvidenceBasis.IncompleteStop =>
                context.DelegatedPrefixIsEmpty && requirement.AcceptsRows(context),

            _ => false,
        };

    public static bool CountIsExact<TMember, TDisposition, TWitness>(
        CompletionRequirement<TMember, TDisposition, TWitness> requirement,
        CompletionContext<TMember, TDisposition, TWitness> context)
        where TMember : notnull =>
        context.Evidence.Basis switch
        {
            CompletionEvidenceBasis.LogicalExhaustion =>
                requirement.AcceptsCount(context),
            CompletionEvidenceBasis.RequirementWitness =>
                ReferenceEquals(context.Evidence.Requirement, requirement.Identity)
                    && requirement.AcceptsCount(context),

            // An incomplete stop never establishes exact Count, and an
            // unavailable outcome establishes nothing.
            _ => false,
        };
}
