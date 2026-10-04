using System.Collections.Immutable;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

sealed class SectionRowCapabilityPlan<TIdentity>
    where TIdentity : notnull
{
    readonly IReadOnlyDictionary<TIdentity, bool> _countOnly;

    internal SectionRowCapabilityPlan(
        ProducerCapabilityPlan structuralPlan,
        IReadOnlyDictionary<TIdentity, bool> countOnly)
    {
        StructuralPlan = structuralPlan;
        _countOnly = countOnly;
    }

    internal ProducerCapabilityPlan StructuralPlan { get; }

    internal bool IsCountOnly(TIdentity identity) =>
        _countOnly[identity];
}

static class SectionRowCapabilityPlanner
{
    static readonly ProducerCapabilityDomainIdentity CapabilityDomain =
        ProducerCapabilityDomainIdentity.Create();
    static readonly QuerySpaceResourceDomainIdentity ResourceDomain =
        QuerySpaceResourceDomainIdentity.Create();
    static readonly ProducerCapabilityIdentity Rows =
        ProducerCapabilityIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityIdentity Count =
        ProducerCapabilityIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityCompletionIdentity Complete =
        ProducerCapabilityCompletionIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityOutcomeIdentity RowsOutcome =
        ProducerCapabilityOutcomeIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityOutcomeIdentity CountOutcome =
        ProducerCapabilityOutcomeIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityResourceLifetimeIdentity
        BorrowedSource =
            ProducerCapabilityResourceLifetimeIdentity.Create(
                CapabilityDomain);
    static readonly ProducerCapabilityResultLifetimeIdentity DetachedResult =
        ProducerCapabilityResultLifetimeIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityStrategyIdentity CardinalityStrategy =
        ProducerCapabilityStrategyIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityStrategyIdentity RowsStrategy =
        ProducerCapabilityStrategyIdentity.Create(CapabilityDomain);

    internal static SectionRowCapabilityPlan<TIdentity> Plan<
        TIdentity,
        TProjection>(
            IReadOnlyList<
                SectionRowSetDeclaration<TIdentity, TProjection>> rowSets,
            QuerySpaceTerminalRequirement terminal,
            bool canExecuteCountWithoutRows)
        where TIdentity : notnull
    {
        ArgumentNullException.ThrowIfNull(rowSets);
        if (rowSets.Count == 0)
            throw new ArgumentException("No row sets were supplied.", nameof(rowSets));

        bool countTerminal =
            terminal is QuerySpaceTerminalRequirement.Count;
        bool directCount =
            countTerminal && canExecuteCountWithoutRows;
        QuerySpaceResourceIdentity resource =
            QuerySpaceResourceIdentity.Create(ResourceDomain);
        var requirements =
            new List<ProducerCapabilityRequirementCandidate>(rowSets.Count);
        var declarations =
            new List<ProducerCapabilityProvisionDeclaration>(
                rowSets.Count * 2);
        var coverages =
            new List<ProducerCapabilityCoverageDeclaration>(rowSets.Count);
        var selected =
            new List<ProducerCapabilityProvisionIdentity>(rowSets.Count);
        var satisfactions =
            new List<ProducerCapabilitySatisfactionCandidate>(rowSets.Count);
        foreach (SectionRowSetDeclaration<TIdentity, TProjection> rowSet
            in rowSets)
        {
            ProducerCapabilityScopeIdentity scope =
                ProducerCapabilityScopeIdentity.Create(
                    CapabilityDomain,
                    resource);
            QuerySpaceRequestAssociationIdentity association =
                QuerySpaceRequestAssociationIdentity.Create();
            ProducerCapabilityProvisionIdentity rowsProvision =
                ProducerCapabilityProvisionIdentity.Create(
                    CapabilityDomain);
            ProducerCapabilityProvisionIdentity countProvision =
                ProducerCapabilityProvisionIdentity.Create(
                    CapabilityDomain);
            ProducerCapabilityCoverageIdentity rowsCoverCount =
                ProducerCapabilityCoverageIdentity.Create(
                    CapabilityDomain);

            requirements.Add(new(
                association,
                resource,
                scope,
                countTerminal ? Count : Rows,
                Complete,
                countTerminal ? CountOutcome : RowsOutcome,
                countTerminal
                    ? ProducerCapabilityProperties.ExactCardinality
                    : ProducerCapabilityProperties.None));
            declarations.Add(
                ProducerCapabilityProvisionDeclaration.Create(
                    rowsProvision,
                    scope,
                    Rows,
                    Complete,
                    RowsOutcome,
                    BorrowedSource,
                    DetachedResult,
                    ProducerCapabilityProperties.ExactCardinality));
            if (canExecuteCountWithoutRows)
            {
                declarations.Add(
                    ProducerCapabilityProvisionDeclaration.Create(
                        countProvision,
                        scope,
                        Count,
                        Complete,
                        CountOutcome,
                        BorrowedSource,
                        DetachedResult,
                        ProducerCapabilityProperties.ExactCardinality));
            }
            coverages.Add(
                ProducerCapabilityCoverageDeclaration.Create(
                    rowsCoverCount,
                    scope,
                    Rows,
                    Complete,
                    RowsOutcome,
                    scope,
                    Count,
                    Complete,
                    CountOutcome,
                    ProducerCapabilityProjectionIdentity.Create(
                        CapabilityDomain),
                    ProducerCapabilityProperties.ExactCardinality));

            ProducerCapabilityProvisionIdentity chosen =
                directCount
                    ? countProvision
                    : rowsProvision;
            selected.Add(chosen);
            satisfactions.Add(
                ProducerCapabilitySatisfactionCandidate.Create(
                    association,
                    chosen,
                    countTerminal && !directCount
                        ? [rowsCoverCount]
                        : null));
        }

        ProducerCapabilityPlanResult result =
            ProducerCapabilityPlanValidator.Validate(
                CapabilityDomain,
                requirements,
                declarations,
                coverages,
                ProducerCapabilityPlanCandidate.Create(
                    directCount
                        ? CardinalityStrategy
                        : RowsStrategy,
                    selected,
                    satisfactions));
        if (result is not ProducerCapabilityPlanResult.Accepted accepted)
        {
            var rejected =
                (ProducerCapabilityPlanResult.Rejected)result;
            throw new InvalidOperationException(
                "The section-row producer created an invalid capability "
                + $"plan: {string.Join(", ", rejected.Reasons.Select(
                    static reason => reason.Reason))}.");
        }

        var countOnly =
            new Dictionary<TIdentity, bool>(rowSets.Count);
        for (int index = 0; index < rowSets.Count; index++)
        {
            countOnly.Add(
                rowSets[index].Identity,
                ReferenceEquals(
                    accepted.Plan.Satisfactions[index]
                        .Provision.Capability,
                    Count));
        }
        return new(
            accepted.Plan,
            countOnly.ToImmutableDictionary());
    }
}
