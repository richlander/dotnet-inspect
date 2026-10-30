using System.Collections.Immutable;
using DotnetInspector.Packages;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

/// <summary>The acquisition required by an independent exact Library inspection.</summary>
public enum PackageLibraryInspectionRequirement
{
    PublicApi,
    ImplementationFacts,
}

/// <summary>One resource-free source plan for inspections of the same exact Library.</summary>
public sealed class PackageLibraryInspectionDemandPlan
{
    internal PackageLibraryInspectionDemandPlan(
        PackageLibrarySelector selector,
        ImmutableArray<PackageLibraryInspectionRequirement> requirements,
        PackageAssetDemand assetDemand,
        ProducerCapabilityPlan structuralPlan)
    {
        Selector = selector;
        Requirements = requirements;
        AssetDemand = assetDemand;
        StructuralPlan = structuralPlan;
    }

    public PackageLibrarySelector Selector { get; }
    public ImmutableArray<PackageLibraryInspectionRequirement> Requirements { get; }
    public PackageAssetDemand AssetDemand { get; }
    public ProducerCapabilityPlan StructuralPlan { get; }
}

/// <summary>
/// Lowers all known exact Library inspection requirements before acquisition.
/// Each requirement retains its association while sharing one selected source provision.
/// </summary>
public static class PackageLibraryInspectionDemandPlanner
{
    static readonly ProducerCapabilityDomainIdentity Domain = ProducerCapabilityDomainIdentity.Create();
    static readonly QuerySpaceResourceDomainIdentity ResourceDomain = QuerySpaceResourceDomainIdentity.Create();
    static readonly ProducerCapabilityIdentity Surface = ProducerCapabilityIdentity.Create(Domain);
    static readonly ProducerCapabilityIdentity SurfaceAndImplementation = ProducerCapabilityIdentity.Create(Domain);
    static readonly ProducerCapabilityCompletionIdentity Complete = ProducerCapabilityCompletionIdentity.Create(Domain);
    static readonly ProducerCapabilityOutcomeIdentity Outcome = ProducerCapabilityOutcomeIdentity.Create(Domain);
    static readonly ProducerCapabilityResourceLifetimeIdentity Operation = ProducerCapabilityResourceLifetimeIdentity.Create(Domain);
    static readonly ProducerCapabilityResultLifetimeIdentity Receipt = ProducerCapabilityResultLifetimeIdentity.Create(Domain);
    static readonly ProducerCapabilityStrategyIdentity SurfaceStrategy = ProducerCapabilityStrategyIdentity.Create(Domain);
    static readonly ProducerCapabilityStrategyIdentity SharedStrategy = ProducerCapabilityStrategyIdentity.Create(Domain);
    static readonly ProducerCapabilityProjectionIdentity SurfaceProjection = ProducerCapabilityProjectionIdentity.Create(Domain);

    public static PackageLibraryInspectionDemandPlan Plan(
        PackageLibrarySelector selector,
        params PackageLibraryInspectionRequirement[] requirements)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(requirements);
        if (requirements.Length == 0)
            throw new ArgumentException("At least one Library inspection is required.", nameof(requirements));
        ImmutableArray<PackageLibraryInspectionRequirement> frozen = [.. requirements];
        foreach (PackageLibraryInspectionRequirement requirement in frozen)
        {
            if (!Enum.IsDefined(requirement))
                throw new ArgumentOutOfRangeException(nameof(requirements));
        }

        bool implementation = frozen.Contains(PackageLibraryInspectionRequirement.ImplementationFacts);
        QuerySpaceResourceIdentity resource = QuerySpaceResourceIdentity.Create(ResourceDomain);
        ProducerCapabilityScopeIdentity scope = ProducerCapabilityScopeIdentity.Create(Domain, resource);
        ProducerCapabilityProvisionIdentity provision = ProducerCapabilityProvisionIdentity.Create(Domain);
        ProducerCapabilityCoverageIdentity coverage = ProducerCapabilityCoverageIdentity.Create(Domain);
        ProducerCapabilityIdentity capability = implementation ? SurfaceAndImplementation : Surface;
        ProducerCapabilityProvisionDeclaration declaration = ProducerCapabilityProvisionDeclaration.Create(
            provision, scope, capability, Complete, Outcome, Operation, Receipt);
        ProducerCapabilityCoverageDeclaration[] coverages = implementation
            ? [ProducerCapabilityCoverageDeclaration.Create(
                coverage, scope, SurfaceAndImplementation, Complete, Outcome,
                scope, Surface, Complete, Outcome, SurfaceProjection)]
            : [];
        var candidates = new ProducerCapabilityRequirementCandidate[frozen.Length];
        var satisfactions = new ProducerCapabilitySatisfactionCandidate[frozen.Length];
        for (int i = 0; i < frozen.Length; i++)
        {
            QuerySpaceRequestAssociationIdentity association = QuerySpaceRequestAssociationIdentity.Create();
            bool surface = frozen[i] == PackageLibraryInspectionRequirement.PublicApi;
            candidates[i] = new(association, resource, scope,
                surface ? Surface : SurfaceAndImplementation, Complete, Outcome);
            satisfactions[i] = ProducerCapabilitySatisfactionCandidate.Create(
                association, provision, surface && implementation ? [coverage] : []);
        }

        ProducerCapabilityPlanResult result = ProducerCapabilityPlanValidator.Validate(
            Domain, candidates, [declaration], coverages,
            ProducerCapabilityPlanCandidate.Create(
                implementation ? SharedStrategy : SurfaceStrategy, [provision], satisfactions));
        if (result is not ProducerCapabilityPlanResult.Accepted accepted)
            throw new InvalidOperationException("The exact Library producer created an invalid acquisition plan.");
        return new(selector, frozen,
            implementation ? PackageAssetDemand.SurfaceAndImplementation : PackageAssetDemand.Surface,
            accepted.Plan);
    }
}
