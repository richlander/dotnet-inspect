using QuerySpace.Composition;

namespace DotnetInspector.Sections;

/// <summary>
/// The row work selected by one Package children capability plan.
/// </summary>
public enum PackageChildrenRowProvision
{
    /// <summary>No Package child rows are required.</summary>
    None,

    /// <summary>The selected child identities are required.</summary>
    SelectedRows,
}

/// <summary>
/// One validated, resource-free Package children producer plan.
/// </summary>
public sealed class PackageChildrenCapabilityPlan
{
    internal PackageChildrenCapabilityPlan(
        ProducerCapabilityPlan structuralPlan,
        QuerySpaceTerminalRequirement terminal,
        int sourceCount,
        int selectedStart,
        int selectedEnd,
        PackageChildrenRowProvision rowProvision,
        bool populationCountDerivedFromRows)
    {
        StructuralPlan = structuralPlan;
        Terminal = terminal;
        SourceCount = sourceCount;
        SelectedStart = selectedStart;
        SelectedEnd = selectedEnd;
        RowProvision = rowProvision;
        PopulationCountDerivedFromRows =
            populationCountDerivedFromRows;
    }

    /// <summary>The accepted structural producer plan.</summary>
    public ProducerCapabilityPlan StructuralPlan { get; }

    /// <summary>The terminal requirement supplied by the host.</summary>
    public QuerySpaceTerminalRequirement Terminal { get; }

    /// <summary>The complete Package child population count.</summary>
    public int SourceCount { get; }

    /// <summary>The inclusive start of the selected child window.</summary>
    public int SelectedStart { get; }

    /// <summary>The exclusive end of the selected child window.</summary>
    public int SelectedEnd { get; }

    /// <summary>The number of selected children.</summary>
    public int SelectedCount => SelectedEnd - SelectedStart;

    /// <summary>
    /// The Count returned by a Count terminal after applying its selection.
    /// </summary>
    public int RequestedCount => SelectedCount;

    /// <summary>The selected row execution provision.</summary>
    public PackageChildrenRowProvision RowProvision { get; }

    /// <summary>
    /// Whether complete selected rows cover the source population Count.
    /// </summary>
    public bool PopulationCountDerivedFromRows { get; }
}

/// <summary>
/// Selects owner-issued Package children provisions for one complete request
/// set.
/// </summary>
public static class PackageChildrenCapabilityPlanner
{
    static readonly ProducerCapabilityDomainIdentity CapabilityDomain =
        ProducerCapabilityDomainIdentity.Create();
    static readonly QuerySpaceResourceDomainIdentity ResourceDomain =
        QuerySpaceResourceDomainIdentity.Create();
    static readonly ProducerCapabilityIdentity Count =
        ProducerCapabilityIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityIdentity SelectedRows =
        ProducerCapabilityIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityCompletionIdentity Complete =
        ProducerCapabilityCompletionIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityOutcomeIdentity CountOutcome =
        ProducerCapabilityOutcomeIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityOutcomeIdentity RowsOutcome =
        ProducerCapabilityOutcomeIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityResourceLifetimeIdentity
        BorrowedSource =
            ProducerCapabilityResourceLifetimeIdentity.Create(
                CapabilityDomain);
    static readonly ProducerCapabilityResultLifetimeIdentity DetachedResult =
        ProducerCapabilityResultLifetimeIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityStrategyIdentity CountStrategy =
        ProducerCapabilityStrategyIdentity.Create(CapabilityDomain);
    static readonly ProducerCapabilityStrategyIdentity RowsStrategy =
        ProducerCapabilityStrategyIdentity.Create(CapabilityDomain);

    /// <summary>
    /// Plans Count or Rows for one Package child population and selected
    /// window.
    /// </summary>
    public static PackageChildrenCapabilityPlan Plan(
        int sourceCount,
        int selectedStart,
        int selectedEnd,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(selectedStart);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            selectedEnd,
            selectedStart);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            selectedEnd,
            sourceCount);
        if (terminal is not (
            QuerySpaceTerminalRequirement.Count
            or QuerySpaceTerminalRequirement.Rows))
        {
            throw new ArgumentOutOfRangeException(
                nameof(terminal),
                terminal,
                "Package children support Count and Rows terminals.");
        }
        QuerySpaceResourceIdentity resource =
            QuerySpaceResourceIdentity.Create(ResourceDomain);
        ProducerCapabilityScopeIdentity sourceScope =
            ProducerCapabilityScopeIdentity.Create(
                CapabilityDomain,
                resource);
        ProducerCapabilityScopeIdentity selectedScope =
            ProducerCapabilityScopeIdentity.Create(
                CapabilityDomain,
                resource);
        ProducerCapabilityProvisionIdentity countProvision =
            ProducerCapabilityProvisionIdentity.Create(
                CapabilityDomain);
        ProducerCapabilityProvisionIdentity selectedRowsProvision =
            ProducerCapabilityProvisionIdentity.Create(
                CapabilityDomain);
        ProducerCapabilityCoverageIdentity rowsCoverCount =
            ProducerCapabilityCoverageIdentity.Create(
                CapabilityDomain);
        ProducerCapabilityScopeIdentity countScope =
            terminal is QuerySpaceTerminalRequirement.Count
                ? selectedScope
                : sourceScope;
        var requirements =
            new List<ProducerCapabilityRequirementCandidate>();
        var declarations =
            new List<ProducerCapabilityProvisionDeclaration>();
        var coverages =
            new List<ProducerCapabilityCoverageDeclaration>();
        var selected =
            new List<ProducerCapabilityProvisionIdentity>();
        var satisfactions =
            new List<ProducerCapabilitySatisfactionCandidate>();

        QuerySpaceRequestAssociationIdentity countAssociation =
            QuerySpaceRequestAssociationIdentity.Create();
        requirements.Add(new(
            countAssociation,
            resource,
            countScope,
            Count,
            Complete,
            CountOutcome,
            ProducerCapabilityProperties.ExactCardinality));
        declarations.Add(
            ProducerCapabilityProvisionDeclaration.Create(
                countProvision,
                countScope,
                Count,
                Complete,
                CountOutcome,
                BorrowedSource,
                DetachedResult,
                ProducerCapabilityProperties.ExactCardinality));

        if (terminal is QuerySpaceTerminalRequirement.Count)
        {
            selected.Add(countProvision);
            satisfactions.Add(
                ProducerCapabilitySatisfactionCandidate.Create(
                    countAssociation,
                    countProvision));
            return CreatePlan(
                terminal,
                sourceCount,
                selectedStart,
                selectedEnd,
                PackageChildrenRowProvision.None,
                populationCountDerivedFromRows: false,
                requirements,
                declarations,
                coverages,
                selected,
                satisfactions);
        }

        bool completeSelection =
            selectedStart == 0
            && selectedEnd == sourceCount;
        QuerySpaceRequestAssociationIdentity rowsAssociation =
            QuerySpaceRequestAssociationIdentity.Create();
        requirements.Add(new(
            rowsAssociation,
            resource,
            selectedScope,
            SelectedRows,
            Complete,
            RowsOutcome,
            ProducerCapabilityProperties.ExactCardinality));
        declarations.Add(
            ProducerCapabilityProvisionDeclaration.Create(
                selectedRowsProvision,
                selectedScope,
                SelectedRows,
                Complete,
                RowsOutcome,
                BorrowedSource,
                DetachedResult,
                ProducerCapabilityProperties.ExactCardinality));
        if (completeSelection)
        {
            coverages.Add(
                ProducerCapabilityCoverageDeclaration.Create(
                    rowsCoverCount,
                    selectedScope,
                    SelectedRows,
                    Complete,
                    RowsOutcome,
                    sourceScope,
                    Count,
                    Complete,
                    CountOutcome,
                    ProducerCapabilityProjectionIdentity.Create(
                        CapabilityDomain),
                    ProducerCapabilityProperties.ExactCardinality));
            satisfactions.Add(
                ProducerCapabilitySatisfactionCandidate.Create(
                    countAssociation,
                    selectedRowsProvision,
                    [rowsCoverCount]));
        }
        else
        {
            selected.Add(countProvision);
            satisfactions.Add(
                ProducerCapabilitySatisfactionCandidate.Create(
                    countAssociation,
                    countProvision));
        }

        selected.Add(selectedRowsProvision);
        satisfactions.Add(
            ProducerCapabilitySatisfactionCandidate.Create(
                rowsAssociation,
                selectedRowsProvision));
        return CreatePlan(
            terminal,
            sourceCount,
            selectedStart,
            selectedEnd,
            PackageChildrenRowProvision.SelectedRows,
            completeSelection,
            requirements,
            declarations,
            coverages,
            selected,
            satisfactions);
    }

    static PackageChildrenCapabilityPlan CreatePlan(
        QuerySpaceTerminalRequirement terminal,
        int sourceCount,
        int selectedStart,
        int selectedEnd,
        PackageChildrenRowProvision rowProvision,
        bool populationCountDerivedFromRows,
        IReadOnlyList<ProducerCapabilityRequirementCandidate>
            requirements,
        IReadOnlyList<ProducerCapabilityProvisionDeclaration>
            declarations,
        IReadOnlyList<ProducerCapabilityCoverageDeclaration>
            coverages,
        IReadOnlyList<ProducerCapabilityProvisionIdentity> selected,
        IReadOnlyList<ProducerCapabilitySatisfactionCandidate>
            satisfactions)
    {
        ProducerCapabilityPlanResult result =
            ProducerCapabilityPlanValidator.Validate(
                CapabilityDomain,
                requirements,
                declarations,
                coverages,
                ProducerCapabilityPlanCandidate.Create(
                    terminal is QuerySpaceTerminalRequirement.Count
                        ? CountStrategy
                        : RowsStrategy,
                    selected,
                    satisfactions));
        if (result is not ProducerCapabilityPlanResult.Accepted accepted)
        {
            var rejected =
                (ProducerCapabilityPlanResult.Rejected)result;
            throw new InvalidOperationException(
                "The Package children producer created an invalid "
                + $"capability plan: {string.Join(
                    ", ",
                    rejected.Reasons.Select(
                        static reason => reason.Reason))}.");
        }

        return new(
            accepted.Plan,
            terminal,
            sourceCount,
            selectedStart,
            selectedEnd,
            rowProvision,
            populationCountDerivedFromRows);
    }
}
