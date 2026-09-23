using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// One Metadata request captured together with its resource-free identity.
/// </summary>
public sealed class PlatformMetadataRequestEvidence<TRequest>
    where TRequest : notnull
{
    public PlatformMetadataRequestEvidence(TRequest value, string identityName)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
        Identity = PlatformMetadataRequestIdentity.Issue(identityName);
    }

    public TRequest Value { get; }
    public PlatformMetadataRequestIdentity Identity { get; }
}

/// <summary>
/// Platform-route prerequisites captured together with their resource-free
/// identity.
/// </summary>
public sealed class PlatformRoutePrerequisitesEvidence<TPrerequisites>
    where TPrerequisites : notnull
{
    public PlatformRoutePrerequisitesEvidence(
        TPrerequisites value,
        string identityName)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
        Identity = PlatformRoutePrerequisitesIdentity.Issue(identityName);
    }

    public TPrerequisites Value { get; }
    public PlatformRoutePrerequisitesIdentity Identity { get; }
}

/// <summary>
/// Resource-free applicable-platform route for one exact assembly-reference
/// operation.
/// </summary>
public sealed class PlatformAssemblyReferenceRoute
{
    public PlatformAssemblyReferenceRoute(
        PlatformMetadataRequestIdentity request,
        PlatformFamilyTarget target,
        PlatformHouseRequestOrigin origin,
        PlatformSourcePlanIdentity sourcePlan,
        PlatformSourcePolicyGeneration sourcePolicy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(sourcePlan);
        ArgumentNullException.ThrowIfNull(sourcePolicy);
        Request = request;
        Target = target;
        Origin = origin;
        SourcePlan = sourcePlan;
        SourcePolicy = sourcePolicy;
    }

    public PlatformMetadataRequestIdentity Request { get; }
    public PlatformFamilyTarget Target { get; }
    public PlatformHouseRequestOrigin Origin { get; }
    public PlatformSourcePlanIdentity SourcePlan { get; }
    public PlatformSourcePolicyGeneration SourcePolicy { get; }
}

/// <summary>
/// One starting type-resolution candidate captured together with its
/// resource-free identity.
/// </summary>
public sealed class PlatformTypeResolutionCandidateEvidence<TCandidate>
    where TCandidate : notnull
{
    public PlatformTypeResolutionCandidateEvidence(
        TCandidate value,
        string identityName)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
        Identity = PlatformTypeResolutionCandidateIdentity.Issue(
            identityName);
    }

    public TCandidate Value { get; }
    public PlatformTypeResolutionCandidateIdentity Identity { get; }
}

/// <summary>
/// Settled reference evidence captured together with its resource-free
/// identity.
/// </summary>
public sealed class PlatformReferenceEvidence<TReference>
    where TReference : notnull
{
    public PlatformReferenceEvidence(TReference value, string identityName)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
        Identity = PlatformReferenceEvidenceIdentity.Issue(identityName);
    }

    public TReference Value { get; }
    public PlatformReferenceEvidenceIdentity Identity { get; }
}

/// <summary>
/// Non-generic resource-free projection of one owner-issued view
/// correspondence.
/// </summary>
public abstract class PlatformViewCorrespondenceEvidence
{
    private protected PlatformViewCorrespondenceEvidence(
        PlatformViewCorrespondenceIdentity identity) =>
        Identity = identity;

    public PlatformViewCorrespondenceIdentity Identity { get; }
}

/// <summary>
/// One live view correspondence captured together with its resource-free
/// identity.
/// </summary>
public sealed class PlatformViewCorrespondenceEvidence<TCorrespondence> :
    PlatformViewCorrespondenceEvidence
    where TCorrespondence : notnull
{
    public PlatformViewCorrespondenceEvidence(
        TCorrespondence value,
        string identityName)
        : base(PlatformViewCorrespondenceIdentity.Issue(identityName))
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public TCorrespondence Value { get; }
}

/// <summary>
/// Non-generic resource-free projection of one Metadata terminal outcome.
/// </summary>
public abstract class PlatformMetadataOutcomeEvidence
{
    private protected PlatformMetadataOutcomeEvidence(
        PlatformMetadataOutcomeIdentity identity,
        PlatformSourceContribution? terminalSupplier,
        PlatformViewCorrespondenceIdentity? correspondence)
    {
        Identity = identity;
        TerminalSupplier = terminalSupplier;
        Correspondence = correspondence;
    }

    public PlatformMetadataOutcomeIdentity Identity { get; }
    public PlatformSourceContribution? TerminalSupplier { get; }
    public PlatformViewCorrespondenceIdentity? Correspondence { get; }
}

/// <summary>
/// One live Metadata terminal outcome captured together with its
/// resource-free identity.
/// </summary>
public sealed class PlatformMetadataOutcomeEvidence<TOutcome> :
    PlatformMetadataOutcomeEvidence
    where TOutcome : notnull
{
    public PlatformMetadataOutcomeEvidence(TOutcome value, string identityName)
        : base(
            PlatformMetadataOutcomeIdentity.Issue(identityName),
            terminalSupplier: null,
            correspondence: null)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public PlatformMetadataOutcomeEvidence(
        TOutcome value,
        string identityName,
        PlatformSourceContribution terminalSupplier)
        : base(
            PlatformMetadataOutcomeIdentity.Issue(identityName),
            terminalSupplier,
            correspondence: null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(terminalSupplier);
        Value = value;
    }

    public PlatformMetadataOutcomeEvidence(
        TOutcome value,
        string identityName,
        PlatformSourceContribution terminalSupplier,
        PlatformViewCorrespondenceEvidence correspondence)
        : base(
            PlatformMetadataOutcomeIdentity.Issue(identityName),
            terminalSupplier,
            (correspondence
                ?? throw new ArgumentNullException(
                    nameof(correspondence))).Identity)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(terminalSupplier);
        Value = value;
    }

    public TOutcome Value { get; }
}
