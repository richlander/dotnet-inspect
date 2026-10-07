using System.Collections.Immutable;

namespace QuerySpace.Composition;

/// <summary>
/// One producer-selected satisfaction path for a requirement.
/// </summary>
public sealed record ProducerCapabilitySatisfactionCandidate(
    QuerySpaceRequestAssociationIdentity? Association,
    ProducerCapabilityProvisionIdentity? Provision,
    ImmutableArray<ProducerCapabilityCoverageIdentity> Coverages)
{
    /// <summary>Creates one immutable satisfaction candidate.</summary>
    public static ProducerCapabilitySatisfactionCandidate Create(
        QuerySpaceRequestAssociationIdentity association,
        ProducerCapabilityProvisionIdentity provision,
        IReadOnlyList<ProducerCapabilityCoverageIdentity>? coverages = null)
    {
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(provision);
        return new(
            association,
            provision,
            coverages is null
                ? []
                : [.. coverages]);
    }
}

/// <summary>
/// One complete immutable producer-selected plan candidate.
/// </summary>
public sealed record ProducerCapabilityPlanCandidate(
    ProducerCapabilityStrategyIdentity? Strategy,
    ImmutableArray<ProducerCapabilityProvisionIdentity> Provisions,
    ImmutableArray<ProducerCapabilitySatisfactionCandidate> Satisfactions)
{
    /// <summary>Creates one immutable plan candidate.</summary>
    public static ProducerCapabilityPlanCandidate Create(
        ProducerCapabilityStrategyIdentity strategy,
        IReadOnlyList<ProducerCapabilityProvisionIdentity> provisions,
        IReadOnlyList<ProducerCapabilitySatisfactionCandidate> satisfactions)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(provisions);
        ArgumentNullException.ThrowIfNull(satisfactions);
        return new(
            strategy,
            [.. provisions],
            [.. satisfactions]);
    }
}

/// <summary>How one producer requirement is satisfied.</summary>
public enum ProducerCapabilitySatisfactionKind
{
    /// <summary>The provision directly satisfies the requirement.</summary>
    Direct,

    /// <summary>An owner-declared covering path satisfies the requirement.</summary>
    Covering,
}

/// <summary>One validated requirement satisfaction.</summary>
public sealed record ProducerCapabilitySatisfaction(
    ProducerCapabilityRequirement Requirement,
    ProducerCapabilityProvisionDeclaration Provision,
    ImmutableArray<ProducerCapabilityCoverageDeclaration> Coverages,
    ProducerCapabilitySatisfactionKind Kind);

/// <summary>One accepted immutable producer capability plan.</summary>
public sealed record ProducerCapabilityPlan(
    ImmutableArray<ProducerCapabilityRequirement> Requirements,
    ProducerCapabilityStrategyIdentity Strategy,
    ImmutableArray<ProducerCapabilityProvisionDeclaration> Provisions,
    ImmutableArray<ProducerCapabilitySatisfaction> Satisfactions);

/// <summary>Why a producer capability plan was structurally rejected.</summary>
public enum ProducerCapabilityPlanRejectionReason
{
    /// <summary>The complete requirement set was empty.</summary>
    EmptyRequirementSet,

    /// <summary>A required planning value was absent.</summary>
    MissingValue,

    /// <summary>The producer supplied no admitted strategy.</summary>
    NoAdmittedStrategy,

    /// <summary>An association identity occurred more than once.</summary>
    DuplicateAssociation,

    /// <summary>An identity belongs to another producer domain.</summary>
    ProducerDomainMismatch,

    /// <summary>A requirement or declaration belongs to another resource.</summary>
    ResourceMismatch,

    /// <summary>A declaration identity occurred more than once.</summary>
    DuplicateDeclaration,

    /// <summary>The selected strategy belongs to another producer.</summary>
    UnknownStrategy,

    /// <summary>A selected provision was not declared.</summary>
    UnknownProvision,

    /// <summary>A selected provision occurred more than once.</summary>
    DuplicateProvision,

    /// <summary>A selected provision is missing a declared dependency.</summary>
    MissingDependency,

    /// <summary>A dependency appears after the provision that needs it.</summary>
    DependencyOrder,

    /// <summary>A satisfaction names no known input association.</summary>
    UnknownAssociation,

    /// <summary>A requirement has no satisfaction.</summary>
    UnsatisfiedRequirement,

    /// <summary>A covering edge was absent or did not continue the path.</summary>
    InvalidCoveringPath,

    /// <summary>The selected path applies to another structural scope.</summary>
    IncompatibleScope,

    /// <summary>The selected path publishes another capability.</summary>
    CapabilityMismatch,

    /// <summary>The selected path does not publish the accepted completion.</summary>
    InsufficientCompletion,

    /// <summary>The selected path publishes another outcome contract.</summary>
    OutcomeContractMismatch,

    /// <summary>The selected path lacks a required structural property.</summary>
    RequiredPropertiesMissing,
}

/// <summary>One typed structural plan rejection.</summary>
public sealed record ProducerCapabilityPlanRejection(
    int CandidateIndex,
    QuerySpaceRequestAssociationIdentity? Association,
    ProducerCapabilityPlanRejectionReason Reason);

/// <summary>The accepted or rejected producer capability plan.</summary>
public abstract record ProducerCapabilityPlanResult
{
    private ProducerCapabilityPlanResult()
    {
    }

    /// <summary>One structurally valid producer-selected plan.</summary>
    public sealed record Accepted(ProducerCapabilityPlan Plan)
        : ProducerCapabilityPlanResult;

    /// <summary>Typed reasons the plan was rejected before execution.</summary>
    public sealed record Rejected(
        ImmutableArray<ProducerCapabilityPlanRejection> Reasons)
        : ProducerCapabilityPlanResult;
}

/// <summary>
/// Structural validator for producer-selected capability plans.
/// </summary>
public static class ProducerCapabilityPlanValidator
{
    /// <summary>
    /// Validates one complete requirement set and producer-selected plan.
    /// </summary>
    public static ProducerCapabilityPlanResult Validate(
        ProducerCapabilityDomainIdentity domain,
        IReadOnlyList<ProducerCapabilityRequirementCandidate?>
            requirements,
        IReadOnlyList<ProducerCapabilityProvisionDeclaration?>
            declarations,
        IReadOnlyList<ProducerCapabilityCoverageDeclaration?>
            coverages,
        ProducerCapabilityPlanCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(coverages);

        var rejections =
            ImmutableArray.CreateBuilder<
                ProducerCapabilityPlanRejection>();
        ImmutableArray<ProducerCapabilityRequirement> validatedRequirements =
            ValidateRequirements(
                domain,
                requirements,
                rejections);
        Dictionary<
            ProducerCapabilityProvisionIdentity,
            ProducerCapabilityProvisionDeclaration> declarationsByIdentity =
                ValidateDeclarations(
                    domain,
                    declarations,
                    rejections);
        Dictionary<
            ProducerCapabilityCoverageIdentity,
            ProducerCapabilityCoverageDeclaration> coveragesByIdentity =
                ValidateCoverages(
                    domain,
                    validatedRequirements,
                    coverages,
                    rejections);

        if (rejections.Count > 0)
            return Rejected(rejections);

        if (candidate is null)
        {
            rejections.Add(new(
                -1,
                null,
                ProducerCapabilityPlanRejectionReason
                    .NoAdmittedStrategy));
            return Rejected(rejections);
        }
        if (candidate.Strategy is null)
        {
            rejections.Add(new(
                -1,
                null,
                ProducerCapabilityPlanRejectionReason.MissingValue));
        }
        else if (!ReferenceEquals(
                     candidate.Strategy.Domain,
                     domain))
        {
            rejections.Add(new(
                -1,
                null,
                ProducerCapabilityPlanRejectionReason.UnknownStrategy));
        }

        ImmutableArray<ProducerCapabilityProvisionDeclaration>
            selectedProvisions =
                ValidateSelectedProvisions(
                    candidate,
                    declarationsByIdentity,
                    validatedRequirements[0].Resource,
                    rejections);
        ValidateDependencies(
            selectedProvisions,
            declarationsByIdentity,
            rejections);
        ImmutableArray<ProducerCapabilitySatisfaction> satisfactions =
            ValidateSatisfactions(
                validatedRequirements,
                selectedProvisions,
                coveragesByIdentity,
                candidate.Satisfactions,
                rejections);

        if (rejections.Count > 0)
            return Rejected(rejections);

        return new ProducerCapabilityPlanResult.Accepted(
            new(
                validatedRequirements,
                candidate.Strategy!,
                selectedProvisions,
                satisfactions));
    }

    static ImmutableArray<ProducerCapabilityRequirement>
        ValidateRequirements(
            ProducerCapabilityDomainIdentity domain,
            IReadOnlyList<ProducerCapabilityRequirementCandidate?>
                candidates,
            ImmutableArray<ProducerCapabilityPlanRejection>.Builder
                rejections)
    {
        if (candidates.Count == 0)
        {
            rejections.Add(new(
                -1,
                null,
                ProducerCapabilityPlanRejectionReason
                    .EmptyRequirementSet));
            return [];
        }

        var requirements =
            ImmutableArray.CreateBuilder<
                ProducerCapabilityRequirement>(candidates.Count);
        var associations =
            new HashSet<QuerySpaceRequestAssociationIdentity>(
                ReferenceEqualityComparer.Instance);
        QuerySpaceResourceIdentity? resource = null;
        for (int index = 0; index < candidates.Count; index++)
        {
            ProducerCapabilityRequirementCandidate? candidate =
                candidates[index];
            if (candidate is null
                || candidate.Association is null
                || candidate.Resource is null
                || candidate.Scope is null
                || candidate.Capability is null
                || candidate.Completion is null
                || candidate.Outcome is null)
            {
                rejections.Add(new(
                    index,
                    candidate?.Association,
                    ProducerCapabilityPlanRejectionReason.MissingValue));
                continue;
            }

            if (!associations.Add(candidate.Association))
            {
                rejections.Add(new(
                    index,
                    candidate.Association,
                    ProducerCapabilityPlanRejectionReason
                        .DuplicateAssociation));
                continue;
            }
            // Scope, capability, completion, and outcome are compared by
            // reference with domain-checked declarations, so only parameters,
            // which no offer carries, need their own domain check.
            if (candidate.Parameters is not null
                && !ReferenceEquals(candidate.Parameters.Domain, domain))
            {
                rejections.Add(new(
                    index,
                    candidate.Association,
                    ProducerCapabilityPlanRejectionReason
                        .ProducerDomainMismatch));
            }
            if (!ReferenceEquals(
                    candidate.Scope.Resource,
                    candidate.Resource)
                || resource is not null
                    && !ReferenceEquals(
                        resource,
                        candidate.Resource))
            {
                rejections.Add(new(
                    index,
                    candidate.Association,
                    ProducerCapabilityPlanRejectionReason
                        .ResourceMismatch));
            }
            resource ??= candidate.Resource;
            requirements.Add(new(
                candidate.Association,
                candidate.Resource,
                candidate.Scope,
                candidate.Capability,
                candidate.Completion,
                candidate.Outcome,
                candidate.Properties,
                candidate.Parameters));
        }
        return requirements.ToImmutable();
    }

    static Dictionary<
        ProducerCapabilityProvisionIdentity,
        ProducerCapabilityProvisionDeclaration> ValidateDeclarations(
            ProducerCapabilityDomainIdentity domain,
            IReadOnlyList<ProducerCapabilityProvisionDeclaration?>
                declarations,
            ImmutableArray<ProducerCapabilityPlanRejection>.Builder
                rejections)
    {
        var byIdentity =
            new Dictionary<
                ProducerCapabilityProvisionIdentity,
                ProducerCapabilityProvisionDeclaration>(
                    ReferenceEqualityComparer.Instance);
        for (int index = 0; index < declarations.Count; index++)
        {
            ProducerCapabilityProvisionDeclaration? declaration =
                declarations[index];
            if (declaration is null
                || declaration.Identity is null
                || declaration.Scope is null
                || declaration.Capability is null
                || declaration.Completion is null
                || declaration.Outcome is null
                || declaration.ResourceLifetime is null
                || declaration.ResultLifetime is null
                || declaration.Dependencies.IsDefault)
            {
                rejections.Add(new(
                    index,
                    null,
                    ProducerCapabilityPlanRejectionReason.MissingValue));
                continue;
            }
            if (!ReferenceEquals(declaration.Identity.Domain, domain)
                || !HasDomain(
                    domain,
                    declaration.Scope,
                    declaration.Capability,
                    declaration.Completion,
                    declaration.Outcome)
                || !ReferenceEquals(
                    declaration.ResourceLifetime.Domain,
                    domain)
                || !ReferenceEquals(
                    declaration.ResultLifetime.Domain,
                    domain)
                || declaration.Dependencies.Any(
                    dependency =>
                        dependency is null
                        || !ReferenceEquals(
                            dependency.Domain,
                            domain)))
            {
                rejections.Add(new(
                    index,
                    null,
                    ProducerCapabilityPlanRejectionReason
                        .ProducerDomainMismatch));
            }
            if (!byIdentity.TryAdd(
                    declaration.Identity,
                    declaration))
            {
                rejections.Add(new(
                    index,
                    null,
                    ProducerCapabilityPlanRejectionReason
                        .DuplicateDeclaration));
            }
        }
        return byIdentity;
    }

    static Dictionary<
        ProducerCapabilityCoverageIdentity,
        ProducerCapabilityCoverageDeclaration> ValidateCoverages(
            ProducerCapabilityDomainIdentity domain,
            ImmutableArray<ProducerCapabilityRequirement> requirements,
            IReadOnlyList<ProducerCapabilityCoverageDeclaration?>
                coverages,
            ImmutableArray<ProducerCapabilityPlanRejection>.Builder
                rejections)
    {
        var byIdentity =
            new Dictionary<
                ProducerCapabilityCoverageIdentity,
                ProducerCapabilityCoverageDeclaration>(
                    ReferenceEqualityComparer.Instance);
        QuerySpaceResourceIdentity? resource =
            requirements.IsDefaultOrEmpty
                ? null
                : requirements[0].Resource;
        for (int index = 0; index < coverages.Count; index++)
        {
            ProducerCapabilityCoverageDeclaration? coverage =
                coverages[index];
            if (coverage is null
                || coverage.Identity is null
                || coverage.SourceScope is null
                || coverage.SourceCapability is null
                || coverage.SourceCompletion is null
                || coverage.SourceOutcome is null
                || coverage.TargetScope is null
                || coverage.TargetCapability is null
                || coverage.TargetCompletion is null
                || coverage.TargetOutcome is null
                || coverage.Projection is null)
            {
                rejections.Add(new(
                    index,
                    null,
                    ProducerCapabilityPlanRejectionReason.MissingValue));
                continue;
            }
            if (!ReferenceEquals(coverage.Identity.Domain, domain)
                || !ReferenceEquals(coverage.Projection.Domain, domain)
                || !HasDomain(
                    domain,
                    coverage.SourceScope,
                    coverage.SourceCapability,
                    coverage.SourceCompletion,
                    coverage.SourceOutcome)
                || !HasDomain(
                    domain,
                    coverage.TargetScope,
                    coverage.TargetCapability,
                    coverage.TargetCompletion,
                    coverage.TargetOutcome))
            {
                rejections.Add(new(
                    index,
                    null,
                    ProducerCapabilityPlanRejectionReason
                        .ProducerDomainMismatch));
            }
            if (resource is not null
                && (!ReferenceEquals(
                        coverage.SourceScope.Resource,
                        resource)
                    || !ReferenceEquals(
                        coverage.TargetScope.Resource,
                        resource)))
            {
                rejections.Add(new(
                    index,
                    null,
                    ProducerCapabilityPlanRejectionReason
                        .ResourceMismatch));
            }
            if (!byIdentity.TryAdd(
                    coverage.Identity,
                    coverage))
            {
                rejections.Add(new(
                    index,
                    null,
                    ProducerCapabilityPlanRejectionReason
                        .DuplicateDeclaration));
            }
        }
        return byIdentity;
    }

    static ImmutableArray<ProducerCapabilityProvisionDeclaration>
        ValidateSelectedProvisions(
            ProducerCapabilityPlanCandidate candidate,
            IReadOnlyDictionary<
                ProducerCapabilityProvisionIdentity,
                ProducerCapabilityProvisionDeclaration>
                    declarations,
            QuerySpaceResourceIdentity resource,
            ImmutableArray<ProducerCapabilityPlanRejection>.Builder
                rejections)
    {
        if (candidate.Provisions.IsDefault)
        {
            rejections.Add(new(
                -1,
                null,
                ProducerCapabilityPlanRejectionReason.MissingValue));
            return [];
        }

        var selected =
            ImmutableArray.CreateBuilder<
                ProducerCapabilityProvisionDeclaration>(
                    candidate.Provisions.Length);
        var seen =
            new HashSet<ProducerCapabilityProvisionIdentity>(
                ReferenceEqualityComparer.Instance);
        for (int index = 0; index < candidate.Provisions.Length; index++)
        {
            ProducerCapabilityProvisionIdentity? identity =
                candidate.Provisions[index];
            if (identity is null)
            {
                rejections.Add(new(
                    index,
                    null,
                    ProducerCapabilityPlanRejectionReason.MissingValue));
                continue;
            }
            if (!seen.Add(identity))
            {
                rejections.Add(new(
                    index,
                    null,
                    ProducerCapabilityPlanRejectionReason
                        .DuplicateProvision));
                continue;
            }
            if (!declarations.TryGetValue(
                    identity,
                    out ProducerCapabilityProvisionDeclaration?
                        declaration))
            {
                rejections.Add(new(
                    index,
                    null,
                    ProducerCapabilityPlanRejectionReason
                        .UnknownProvision));
                continue;
            }
            if (!ReferenceEquals(declaration.Scope.Resource, resource))
            {
                rejections.Add(new(
                    index,
                    null,
                    ProducerCapabilityPlanRejectionReason
                        .ResourceMismatch));
            }
            selected.Add(declaration);
        }
        return selected.ToImmutable();
    }

    static void ValidateDependencies(
        ImmutableArray<ProducerCapabilityProvisionDeclaration>
            selected,
        IReadOnlyDictionary<
            ProducerCapabilityProvisionIdentity,
            ProducerCapabilityProvisionDeclaration> declarations,
        ImmutableArray<ProducerCapabilityPlanRejection>.Builder
            rejections)
    {
        var selectedIndices =
            new Dictionary<ProducerCapabilityProvisionIdentity, int>(
                ReferenceEqualityComparer.Instance);
        for (int index = 0; index < selected.Length; index++)
            selectedIndices[selected[index].Identity] = index;

        for (int index = 0; index < selected.Length; index++)
        {
            foreach (ProducerCapabilityProvisionIdentity dependency
                in selected[index].Dependencies)
            {
                if (!declarations.ContainsKey(dependency)
                    || !selectedIndices.TryGetValue(
                        dependency,
                        out int dependencyIndex))
                {
                    rejections.Add(new(
                        index,
                        null,
                        ProducerCapabilityPlanRejectionReason
                            .MissingDependency));
                }
                else if (dependencyIndex >= index)
                {
                    rejections.Add(new(
                        index,
                        null,
                        ProducerCapabilityPlanRejectionReason
                            .DependencyOrder));
                }
            }
        }
    }

    static ImmutableArray<ProducerCapabilitySatisfaction>
        ValidateSatisfactions(
            ImmutableArray<ProducerCapabilityRequirement> requirements,
            ImmutableArray<ProducerCapabilityProvisionDeclaration>
                selectedProvisions,
            IReadOnlyDictionary<
                ProducerCapabilityCoverageIdentity,
                ProducerCapabilityCoverageDeclaration> coverages,
            ImmutableArray<ProducerCapabilitySatisfactionCandidate>
                candidates,
            ImmutableArray<ProducerCapabilityPlanRejection>.Builder
                rejections)
    {
        if (candidates.IsDefault)
        {
            rejections.Add(new(
                -1,
                null,
                ProducerCapabilityPlanRejectionReason.MissingValue));
            return [];
        }

        var requirementsByAssociation =
            new Dictionary<
                QuerySpaceRequestAssociationIdentity,
                ProducerCapabilityRequirement>(
                    ReferenceEqualityComparer.Instance);
        foreach (ProducerCapabilityRequirement requirement
            in requirements)
        {
            requirementsByAssociation.Add(
                requirement.Association,
                requirement);
        }
        var provisionsByIdentity =
            new Dictionary<
                ProducerCapabilityProvisionIdentity,
                ProducerCapabilityProvisionDeclaration>(
                    ReferenceEqualityComparer.Instance);
        foreach (ProducerCapabilityProvisionDeclaration declaration
            in selectedProvisions)
        {
            provisionsByIdentity.Add(
                declaration.Identity,
                declaration);
        }
        var satisfactionsByAssociation =
            new Dictionary<
                QuerySpaceRequestAssociationIdentity,
                ProducerCapabilitySatisfaction>(
                    ReferenceEqualityComparer.Instance);
        for (int index = 0; index < candidates.Length; index++)
        {
            ProducerCapabilitySatisfactionCandidate? candidate =
                candidates[index];
            if (candidate is null
                || candidate.Association is null
                || candidate.Provision is null
                || candidate.Coverages.IsDefault)
            {
                rejections.Add(new(
                    index,
                    candidate?.Association,
                    ProducerCapabilityPlanRejectionReason.MissingValue));
                continue;
            }
            if (!requirementsByAssociation.TryGetValue(
                    candidate.Association,
                    out ProducerCapabilityRequirement? requirement))
            {
                rejections.Add(new(
                    index,
                    candidate.Association,
                    ProducerCapabilityPlanRejectionReason
                        .UnknownAssociation));
                continue;
            }
            if (satisfactionsByAssociation.ContainsKey(
                    candidate.Association))
            {
                rejections.Add(new(
                    index,
                    candidate.Association,
                    ProducerCapabilityPlanRejectionReason
                        .DuplicateAssociation));
                continue;
            }
            if (!provisionsByIdentity.TryGetValue(
                    candidate.Provision,
                    out ProducerCapabilityProvisionDeclaration?
                        provision))
            {
                rejections.Add(new(
                    index,
                    candidate.Association,
                    ProducerCapabilityPlanRejectionReason
                        .UnknownProvision));
                continue;
            }

            var offer = new Offer(
                provision.Scope,
                provision.Capability,
                provision.Completion,
                provision.Outcome,
                provision.Properties);
            var path =
                ImmutableArray.CreateBuilder<
                    ProducerCapabilityCoverageDeclaration>(
                        candidate.Coverages.Length);
            bool validPath = true;
            foreach (ProducerCapabilityCoverageIdentity coverageIdentity
                in candidate.Coverages)
            {
                if (coverageIdentity is null
                    || !coverages.TryGetValue(
                        coverageIdentity,
                        out ProducerCapabilityCoverageDeclaration?
                            coverage)
                    || !offer.MatchesSource(coverage))
                {
                    rejections.Add(new(
                        index,
                        candidate.Association,
                        ProducerCapabilityPlanRejectionReason
                            .InvalidCoveringPath));
                    validPath = false;
                    break;
                }
                path.Add(coverage);
                offer = offer.Through(coverage);
            }
            if (!validPath)
                continue;

            ValidateOffer(
                index,
                requirement,
                offer,
                rejections);
            satisfactionsByAssociation.Add(
                candidate.Association,
                new(
                    requirement,
                    provision,
                    path.ToImmutable(),
                    path.Count == 0
                        ? ProducerCapabilitySatisfactionKind.Direct
                        : ProducerCapabilitySatisfactionKind.Covering));
        }

        var ordered =
            ImmutableArray.CreateBuilder<
                ProducerCapabilitySatisfaction>(requirements.Length);
        for (int index = 0; index < requirements.Length; index++)
        {
            ProducerCapabilityRequirement requirement =
                requirements[index];
            if (!satisfactionsByAssociation.TryGetValue(
                    requirement.Association,
                    out ProducerCapabilitySatisfaction? satisfaction))
            {
                rejections.Add(new(
                    index,
                    requirement.Association,
                    ProducerCapabilityPlanRejectionReason
                        .UnsatisfiedRequirement));
                continue;
            }
            ordered.Add(satisfaction);
        }
        return ordered.ToImmutable();
    }

    static void ValidateOffer(
        int index,
        ProducerCapabilityRequirement requirement,
        Offer offer,
        ImmutableArray<ProducerCapabilityPlanRejection>.Builder
            rejections)
    {
        if (!ReferenceEquals(offer.Scope, requirement.Scope))
        {
            rejections.Add(new(
                index,
                requirement.Association,
                ProducerCapabilityPlanRejectionReason
                    .IncompatibleScope));
        }
        if (!ReferenceEquals(
                offer.Capability,
                requirement.Capability))
        {
            rejections.Add(new(
                index,
                requirement.Association,
                ProducerCapabilityPlanRejectionReason
                    .CapabilityMismatch));
        }
        if (!ReferenceEquals(
                offer.Completion,
                requirement.Completion))
        {
            rejections.Add(new(
                index,
                requirement.Association,
                ProducerCapabilityPlanRejectionReason
                    .InsufficientCompletion));
        }
        if (!ReferenceEquals(offer.Outcome, requirement.Outcome))
        {
            rejections.Add(new(
                index,
                requirement.Association,
                ProducerCapabilityPlanRejectionReason
                    .OutcomeContractMismatch));
        }
        if ((offer.Properties & requirement.Properties)
            != requirement.Properties)
        {
            rejections.Add(new(
                index,
                requirement.Association,
                ProducerCapabilityPlanRejectionReason
                    .RequiredPropertiesMissing));
        }
    }

    static bool HasDomain(
        ProducerCapabilityDomainIdentity domain,
        ProducerCapabilityScopeIdentity scope,
        ProducerCapabilityIdentity capability,
        ProducerCapabilityCompletionIdentity completion,
        ProducerCapabilityOutcomeIdentity outcome) =>
        ReferenceEquals(scope.Domain, domain)
        && ReferenceEquals(capability.Domain, domain)
        && ReferenceEquals(completion.Domain, domain)
        && ReferenceEquals(outcome.Domain, domain);

    static ProducerCapabilityPlanResult Rejected(
        ImmutableArray<ProducerCapabilityPlanRejection>.Builder
            rejections) =>
        new ProducerCapabilityPlanResult.Rejected(
            rejections.ToImmutable());

    readonly record struct Offer(
        ProducerCapabilityScopeIdentity Scope,
        ProducerCapabilityIdentity Capability,
        ProducerCapabilityCompletionIdentity Completion,
        ProducerCapabilityOutcomeIdentity Outcome,
        ProducerCapabilityProperties Properties)
    {
        public bool MatchesSource(
            ProducerCapabilityCoverageDeclaration coverage) =>
            ReferenceEquals(Scope, coverage.SourceScope)
            && ReferenceEquals(
                Capability,
                coverage.SourceCapability)
            && ReferenceEquals(
                Completion,
                coverage.SourceCompletion)
            && ReferenceEquals(Outcome, coverage.SourceOutcome);

        // A path keeps a structural property only when the provision and
        // every covering edge establish it: an edge that preserves exact
        // cardinality cannot create it from a source that lacks it.
        public Offer Through(
            ProducerCapabilityCoverageDeclaration coverage) =>
            new(
                coverage.TargetScope,
                coverage.TargetCapability,
                coverage.TargetCompletion,
                coverage.TargetOutcome,
                Properties & coverage.Properties);
    }
}

/// <summary>
/// One producer execution outcome. Failures remain typed and distinct from
/// completed values.
/// </summary>
public abstract record ProducerCapabilityOutcome<TValue, TFailure>
{
    private ProducerCapabilityOutcome()
    {
    }

    /// <summary>One completed detached value.</summary>
    public sealed record Completed(
        TValue Value,
        ProducerCapabilityCompletionIdentity Completion)
        : ProducerCapabilityOutcome<TValue, TFailure>;

    /// <summary>One visible typed selected-provision failure.</summary>
    public sealed record ProvisionFailed(
        TFailure Failure,
        ProducerCapabilityProvisionIdentity Provision)
        : ProducerCapabilityOutcome<TValue, TFailure>;

    /// <summary>One visible failure local to this requirement.</summary>
    public sealed record RequirementFailed(TFailure Failure)
        : ProducerCapabilityOutcome<TValue, TFailure>;
}

/// <summary>One not-yet-validated result association.</summary>
public sealed record ProducerCapabilityResultCandidate<TValue, TFailure>(
    QuerySpaceRequestAssociationIdentity? Association,
    ProducerCapabilityOutcome<TValue, TFailure>? Outcome);

/// <summary>One validated result association.</summary>
public sealed record ProducerCapabilityResultAssociation<TValue, TFailure>(
    ProducerCapabilitySatisfaction Satisfaction,
    ProducerCapabilityOutcome<TValue, TFailure> Outcome);

/// <summary>One complete detached producer result set.</summary>
public sealed record ProducerCapabilityResultSet<TValue, TFailure>(
    ImmutableArray<
        ProducerCapabilityResultAssociation<TValue, TFailure>> Results);

/// <summary>Why a producer result set was structurally rejected.</summary>
public enum ProducerCapabilityResultRejectionReason
{
    /// <summary>A result value or association was absent.</summary>
    MissingValue,

    /// <summary>The same association occurred more than once.</summary>
    DuplicateAssociation,

    /// <summary>The result names no requirement in the plan.</summary>
    UnknownAssociation,

    /// <summary>A planned requirement has no result.</summary>
    MissingResult,

    /// <summary>A completed result publishes another completion.</summary>
    CompletionMismatch,

    /// <summary>A failure names no selected provision.</summary>
    UnknownFailedProvision,

    /// <summary>The requirement does not depend on the failed provision.</summary>
    UnrelatedProvisionFailure,

    /// <summary>
    /// A requirement depends on a failed provision but reports no provision
    /// failure.
    /// </summary>
    SharedProvisionFailureMismatch,
}

/// <summary>One typed producer-result rejection.</summary>
public sealed record ProducerCapabilityResultRejection(
    int CandidateIndex,
    QuerySpaceRequestAssociationIdentity? Association,
    ProducerCapabilityResultRejectionReason Reason);

/// <summary>The accepted or rejected producer result set.</summary>
public abstract record ProducerCapabilityResultSetResult<TValue, TFailure>
{
    private ProducerCapabilityResultSetResult()
    {
    }

    /// <summary>One complete detached result set.</summary>
    public sealed record Accepted(
        ProducerCapabilityResultSet<TValue, TFailure> ResultSet)
        : ProducerCapabilityResultSetResult<TValue, TFailure>;

    /// <summary>Typed reasons the result set was rejected.</summary>
    public sealed record Rejected(
        ImmutableArray<ProducerCapabilityResultRejection> Reasons)
        : ProducerCapabilityResultSetResult<TValue, TFailure>;
}

/// <summary>Validates producer result associations against one plan.</summary>
public static class ProducerCapabilityResultSetValidator
{
    /// <summary>
    /// Preserves one completed or failed result for every planned association.
    /// </summary>
    public static ProducerCapabilityResultSetResult<TValue, TFailure>
        Validate<TValue, TFailure>(
            ProducerCapabilityPlan plan,
            IReadOnlyList<
                ProducerCapabilityResultCandidate<TValue, TFailure>?>
                    candidates)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(candidates);

        var rejections =
            ImmutableArray.CreateBuilder<
                ProducerCapabilityResultRejection>();
        var satisfactions =
            new Dictionary<
                QuerySpaceRequestAssociationIdentity,
                ProducerCapabilitySatisfaction>(
                    ReferenceEqualityComparer.Instance);
        foreach (ProducerCapabilitySatisfaction satisfaction
            in plan.Satisfactions)
        {
            satisfactions.Add(
                satisfaction.Requirement.Association,
                satisfaction);
        }
        var results =
            new Dictionary<
                QuerySpaceRequestAssociationIdentity,
                ProducerCapabilityResultAssociation<TValue, TFailure>>(
                    ReferenceEqualityComparer.Instance);
        var provisions =
            plan.Provisions.ToDictionary(
                static provision => provision.Identity);
        for (int index = 0; index < candidates.Count; index++)
        {
            ProducerCapabilityResultCandidate<TValue, TFailure>?
                candidate = candidates[index];
            if (candidate is null
                || candidate.Association is null
                || candidate.Outcome is null)
            {
                rejections.Add(new(
                    index,
                    candidate?.Association,
                    ProducerCapabilityResultRejectionReason.MissingValue));
                continue;
            }
            if (!satisfactions.TryGetValue(
                    candidate.Association,
                    out ProducerCapabilitySatisfaction? satisfaction))
            {
                rejections.Add(new(
                    index,
                    candidate.Association,
                    ProducerCapabilityResultRejectionReason
                        .UnknownAssociation));
                continue;
            }
            if (results.ContainsKey(candidate.Association))
            {
                rejections.Add(new(
                    index,
                    candidate.Association,
                    ProducerCapabilityResultRejectionReason
                        .DuplicateAssociation));
                continue;
            }
            if (candidate.Outcome
                    is ProducerCapabilityOutcome<
                        TValue,
                        TFailure>.Completed completed
                && !ReferenceEquals(
                    completed.Completion,
                    satisfaction.Requirement.Completion))
            {
                rejections.Add(new(
                    index,
                    candidate.Association,
                    ProducerCapabilityResultRejectionReason
                        .CompletionMismatch));
            }
            if (candidate.Outcome
                    is ProducerCapabilityOutcome<
                        TValue,
                        TFailure>.ProvisionFailed failed)
            {
                if (!provisions.ContainsKey(failed.Provision))
                {
                    rejections.Add(new(
                        index,
                        candidate.Association,
                        ProducerCapabilityResultRejectionReason
                            .UnknownFailedProvision));
                }
                else if (!DependsOn(
                             satisfaction.Provision.Identity,
                             failed.Provision,
                             provisions,
                             []))
                {
                    rejections.Add(new(
                        index,
                        candidate.Association,
                        ProducerCapabilityResultRejectionReason
                            .UnrelatedProvisionFailure));
                }
            }
            results.Add(
                candidate.Association,
                new(satisfaction, candidate.Outcome));
        }

        var ordered =
            ImmutableArray.CreateBuilder<
                ProducerCapabilityResultAssociation<TValue, TFailure>>(
                    plan.Satisfactions.Length);
        foreach (ProducerCapabilitySatisfaction satisfaction
            in plan.Satisfactions)
        {
            if (!results.TryGetValue(
                    satisfaction.Requirement.Association,
                    out ProducerCapabilityResultAssociation<
                        TValue,
                        TFailure>? result))
            {
                rejections.Add(new(
                    -1,
                    satisfaction.Requirement.Association,
                    ProducerCapabilityResultRejectionReason
                        .MissingResult));
                continue;
            }
            ordered.Add(result);
        }
        ValidateSharedProvisionFailures(
            ordered,
            provisions,
            rejections);

        if (rejections.Count > 0)
        {
            return new ProducerCapabilityResultSetResult<
                TValue,
                TFailure>.Rejected(rejections.ToImmutable());
        }
        return new ProducerCapabilityResultSetResult<
            TValue,
            TFailure>.Accepted(new(ordered.ToImmutable()));
    }

    static void ValidateSharedProvisionFailures<TValue, TFailure>(
        ImmutableArray<
            ProducerCapabilityResultAssociation<TValue, TFailure>>.Builder
                results,
        IReadOnlyDictionary<
            ProducerCapabilityProvisionIdentity,
            ProducerCapabilityProvisionDeclaration> provisions,
        ImmutableArray<ProducerCapabilityResultRejection>.Builder
            rejections)
    {
        var failedProvisions =
            new HashSet<ProducerCapabilityProvisionIdentity>(
                ReferenceEqualityComparer.Instance);
        foreach (ProducerCapabilityResultAssociation<TValue, TFailure>
            result in results)
        {
            if (result.Outcome
                is ProducerCapabilityOutcome<
                    TValue,
                    TFailure>.ProvisionFailed failed
                && provisions.ContainsKey(failed.Provision))
            {
                failedProvisions.Add(failed.Provision);
            }
        }

        if (failedProvisions.Count == 0)
            return;

        // A requirement whose chosen path depends on any failed provision
        // reports a provision failure, settled or not. When several provisions
        // fail, it may name any one of them; UnrelatedProvisionFailure already
        // requires that it names a provision it depends on.
        for (int index = 0; index < results.Count; index++)
        {
            ProducerCapabilityResultAssociation<TValue, TFailure> result =
                results[index];
            if (result.Outcome
                    is ProducerCapabilityOutcome<
                        TValue,
                        TFailure>.ProvisionFailed)
            {
                continue;
            }
            foreach (ProducerCapabilityProvisionIdentity failedProvision
                in failedProvisions)
            {
                if (DependsOn(
                        result.Satisfaction.Provision.Identity,
                        failedProvision,
                        provisions,
                        []))
                {
                    rejections.Add(new(
                        index,
                        result.Satisfaction.Requirement.Association,
                        ProducerCapabilityResultRejectionReason
                            .SharedProvisionFailureMismatch));
                    break;
                }
            }
        }
    }

    static bool DependsOn(
        ProducerCapabilityProvisionIdentity provision,
        ProducerCapabilityProvisionIdentity dependency,
        IReadOnlyDictionary<
            ProducerCapabilityProvisionIdentity,
            ProducerCapabilityProvisionDeclaration> provisions,
        HashSet<ProducerCapabilityProvisionIdentity> visited)
    {
        if (ReferenceEquals(provision, dependency))
            return true;
        if (!visited.Add(provision)
            || !provisions.TryGetValue(
                provision,
                out ProducerCapabilityProvisionDeclaration? declaration))
        {
            return false;
        }
        foreach (ProducerCapabilityProvisionIdentity candidate
            in declaration.Dependencies)
        {
            if (DependsOn(
                    candidate,
                    dependency,
                    provisions,
                    visited))
            {
                return true;
            }
        }
        return false;
    }
}
