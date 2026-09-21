namespace DotnetInspector.PlatformHouse;

/// <summary>
/// One ephemeral source result prepared for assembly-reference policy
/// settlement.
/// </summary>
public abstract class PlatformAssemblyReferenceSourceAttempt
    : IPlatformSourcePolicyAttempt
{
    private protected PlatformAssemblyReferenceSourceAttempt(
        PlatformSourceContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        Contribution = contribution;
    }

    public PlatformSourceContribution Contribution { get; }
    bool IPlatformSourcePolicyAttempt.Succeeded =>
        this is Succeeded;
    PlatformHouseCandidateIdentity?
        IPlatformSourcePolicyAttempt.Candidate =>
        this is Succeeded success ? success.Candidate : null;
    PlatformHouseRejectionKind?
        IPlatformSourcePolicyAttempt.RejectionKind =>
        this is NotSucceeded terminal
            ? terminal.RejectionKind
            : null;

    /// <summary>One authoritative source-prepared assembly snapshot.</summary>
    public sealed class Succeeded :
        PlatformAssemblyReferenceSourceAttempt
    {
        public Succeeded(
            PlatformHouseCandidateIdentity candidate,
            PlatformLibraryArtifactMaterializationItem materialization)
            : base(
                (materialization
                    ?? throw new ArgumentNullException(
                        nameof(materialization))).Contribution)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            Candidate = candidate;
            Materialization = materialization;
        }

        public PlatformHouseCandidateIdentity Candidate { get; }
        internal PlatformLibraryArtifactMaterializationItem Materialization
        {
            get;
        }
    }

    /// <summary>One resource-free source-terminal result.</summary>
    public sealed class NotSucceeded :
        PlatformAssemblyReferenceSourceAttempt
    {
        public NotSucceeded(
            PlatformSourceContribution contribution,
            PlatformHouseRejectionKind? rejectionKind = null)
            : base(contribution)
        {
            bool rejected =
                contribution is PlatformSourceContribution.Rejected;
            if (contribution
                    is not PlatformSourceContribution.Unavailable
                        and not PlatformSourceContribution.Rejected
                        and not PlatformSourceContribution.Incomplete
                        and not PlatformSourceContribution.Failed
                || rejected != rejectionKind.HasValue
                || rejectionKind is { } kind && !Enum.IsDefined(kind))
            {
                throw new ArgumentException(
                    "A terminal attempt requires a matching terminal contribution and rejection classification.",
                    nameof(contribution));
            }
            RejectionKind = rejectionKind;
        }

        public PlatformHouseRejectionKind? RejectionKind { get; }
    }
}
