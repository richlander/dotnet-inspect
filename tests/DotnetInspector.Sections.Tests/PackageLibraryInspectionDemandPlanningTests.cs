using DotnetInspector.Packages;
using QuerySpace.Composition;

namespace DotnetInspector.Sections.Tests;

// PR-fast: resource-free acquisition planning, no images or network work.
public sealed class PackageLibraryInspectionDemandPlanningTests
{
    [Fact]
    public void ApiOnlySelectsSurfaceWithoutImplementationProvision()
    {
        var selector = new PackageLibrarySelector("compile:ref/net10.0/Avalonia.Base.dll", PackageLibrarySelectionKind.AssetId);
        PackageLibraryInspectionDemandPlan plan = PackageLibraryInspectionDemandPlanner.Plan(
            selector, PackageLibraryInspectionRequirement.PublicApi);
        Assert.Same(selector, plan.Selector);
        Assert.Equal(PackageAssetDemand.Surface, plan.AssetDemand);
        Assert.Single(plan.StructuralPlan.Requirements);
        Assert.Single(plan.StructuralPlan.Provisions);
        Assert.Equal(ProducerCapabilitySatisfactionKind.Direct,
            Assert.Single(plan.StructuralPlan.Satisfactions).Kind);
    }

    [Fact]
    public void OverviewSharesOneProvisionAndPreservesIndependentAssociations()
    {
        PackageLibraryInspectionRequirement[] requirements =
            [PackageLibraryInspectionRequirement.PublicApi, PackageLibraryInspectionRequirement.ImplementationFacts];
        PackageLibraryInspectionDemandPlan plan = PackageLibraryInspectionDemandPlanner.Plan(
            new("compile:lib/net6.0/Newtonsoft.Json.dll", PackageLibrarySelectionKind.AssetId), requirements);
        requirements[0] = PackageLibraryInspectionRequirement.ImplementationFacts;
        Assert.Equal(PackageLibraryInspectionRequirement.PublicApi, plan.Requirements[0]);
        Assert.Equal(PackageAssetDemand.SurfaceAndImplementation, plan.AssetDemand);
        Assert.Single(plan.StructuralPlan.Provisions);
        Assert.Equal(2, plan.StructuralPlan.Requirements.Length);
        Assert.Equal(2, plan.StructuralPlan.Satisfactions.Length);
        Assert.NotSame(plan.StructuralPlan.Requirements[0].Association, plan.StructuralPlan.Requirements[1].Association);
        Assert.Equal(ProducerCapabilitySatisfactionKind.Covering, plan.StructuralPlan.Satisfactions[0].Kind);
        Assert.Equal(ProducerCapabilitySatisfactionKind.Direct, plan.StructuralPlan.Satisfactions[1].Kind);
        Assert.All(plan.StructuralPlan.Satisfactions,
            satisfaction => Assert.Same(plan.StructuralPlan.Provisions[0], satisfaction.Provision));
        Assert.Same(plan.StructuralPlan.Requirements[0].Resource, plan.StructuralPlan.Requirements[1].Resource);
    }

    [Fact]
    public void ImplementationFactsKeepTheirIndependentDemand()
    {
        PackageLibraryInspectionDemandPlan plan = PackageLibraryInspectionDemandPlanner.Plan(
            new("compile:lib/net8.0/Dapper.dll", PackageLibrarySelectionKind.AssetId),
            PackageLibraryInspectionRequirement.ImplementationFacts);
        Assert.Equal(PackageAssetDemand.SurfaceAndImplementation, plan.AssetDemand);
        Assert.Equal(ProducerCapabilitySatisfactionKind.Direct, Assert.Single(plan.StructuralPlan.Satisfactions).Kind);
    }

    [Fact]
    public void InvalidRequirementsFailBeforeAcquisition()
    {
        var selector = new PackageLibrarySelector("compile:lib/net6.0/Newtonsoft.Json.dll", PackageLibrarySelectionKind.AssetId);
        Assert.Throws<ArgumentException>(() => PackageLibraryInspectionDemandPlanner.Plan(selector));
        Assert.Throws<ArgumentOutOfRangeException>(() => PackageLibraryInspectionDemandPlanner.Plan(selector,
            (PackageLibraryInspectionRequirement)99));
    }
}
