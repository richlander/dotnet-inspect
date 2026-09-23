using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageSupplyChainBaselineTests
{
    [Fact]
    public void NothingRetainsCanonicalRootPackageIdsOnly()
    {
        PackageSupplyChainBaselinePolicy policy =
            PackageSupplyChainBaselinePolicy.CreateNothing(
                [
                    "Root.Package",
                    "root.package",
                    "Other.Root",
                ]);

        Assert.Equal(
            ["root.package", "other.root"],
            policy.RootPackageIds);
        Assert.Equal(
            PackageSupplyChainClassification.Baseline,
            policy.Classify("ROOT.PACKAGE"));
        Assert.Equal(
            PackageSupplyChainClassification.Baseline,
            policy.Classify("other.root"));
        Assert.Equal(
            PackageSupplyChainClassification.IncrementalExposure,
            policy.Classify("dependency.package"));
        Assert.Equal(
            PackageSupplyChainBaseline.Nothing,
            policy.Evidence.Kind);
        Assert.Empty(policy.Evidence.FirstPartyPackagePrefixes);
        Assert.Empty(policy.Evidence.RegisteredEcosystems);
    }

    [Fact]
    public async Task SelfUsesFirstPartyPrefixesButNotEcosystemPopulations()
    {
        WorkspaceEcosystemRegistrationDeclaration ecosystem =
            Ecosystem();
        await using var workspace = new InspectionWorkspace(
            new WorkspacePlan(
                [
                    new WorkspaceRegistration.PackagePrefix(
                        new PackagePrefixDeclaration("Contoso.")),
                    new WorkspaceRegistration.Ecosystem(ecosystem),
                ]));
        WorkspaceRegistrationRevision registrations =
            Current(workspace);

        PackageSupplyChainBaselinePolicy policy =
            PackageSupplyChainBaselinePolicy.Create(
                ["Root.Package"],
                PackageSupplyChainBaseline.Self,
                registrations);

        Assert.Equal(
            PackageSupplyChainClassification.Baseline,
            policy.Classify("Contoso.Dependency"));
        Assert.Equal(
            PackageSupplyChainClassification.IncrementalExposure,
            policy.Classify("Ecosystem.Core"));
        Assert.Equal(
            PackageSupplyChainClassification.IncrementalExposure,
            policy.Classify("Ecosystem.Plugin.Sample"));
        Assert.Equal(
            PackageSupplyChainClassification.IncrementalExposure,
            policy.Classify("System.Text.Json"));
        Assert.Equal(
            ["Contoso."],
            policy.Evidence.FirstPartyPackagePrefixes);
        Assert.Equal(
            ["ecosystem.test"],
            policy.Evidence.RegisteredEcosystems);
    }

    [Fact]
    public async Task RegisteredEcosystemsContributeAllPackagePopulations()
    {
        await using var workspace = new InspectionWorkspace(
            new WorkspacePlan(
                [
                    new WorkspaceRegistration.Ecosystem(Ecosystem()),
                ]));

        PackageSupplyChainBaselinePolicy policy =
            PackageSupplyChainBaselinePolicy.Create(
                ["Root.Package"],
                PackageSupplyChainBaseline
                    .SelfAndRegisteredEcosystems,
                Current(workspace));

        Assert.Equal(
            PackageSupplyChainClassification.Baseline,
            policy.Classify("Ecosystem.Core"));
        Assert.Equal(
            PackageSupplyChainClassification.Baseline,
            policy.Classify("Ecosystem.Plugin.Sample"));
        Assert.Equal(
            PackageSupplyChainClassification.Baseline,
            policy.Classify("System.Text.Json"));
        Assert.Equal(
            PackageSupplyChainClassification.IncrementalExposure,
            policy.Classify("Outside.Package"));
    }

    [Fact]
    public async Task IssuedPolicyDoesNotObserveLaterRegistrationReplacement()
    {
        await using var workspace = new InspectionWorkspace(
            new WorkspacePlan(
                [
                    new WorkspaceRegistration.PackagePrefix(
                        new PackagePrefixDeclaration("Before.")),
                ]));
        WorkspaceRegistrationRevision captured = Current(workspace);
        PackageSupplyChainBaselinePolicy policy =
            PackageSupplyChainBaselinePolicy.Create(
                ["Root.Package"],
                PackageSupplyChainBaseline.Self,
                captured);

        Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(
                captured,
                [
                    new WorkspaceRegistration.PackagePrefix(
                        new PackagePrefixDeclaration("After.")),
                ]));

        Assert.Equal(
            PackageSupplyChainClassification.Baseline,
            policy.Classify("Before.Dependency"));
        Assert.Equal(
            PackageSupplyChainClassification.IncrementalExposure,
            policy.Classify("After.Dependency"));
        Assert.Equal(
            ["Before."],
            policy.Evidence.FirstPartyPackagePrefixes);
    }

    private static WorkspaceEcosystemRegistrationDeclaration Ecosystem()
    {
        ExactLibrarySourceCoordinate packageLibrary =
            WorkspaceRegistrationTestData.RealPackageSystemTextJson();
        return new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.test"),
            [],
            [
                new PackageCoordinate("Ecosystem.Core"),
            ],
            [
                new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                    new PackagePrefixDeclaration("Ecosystem.Plugin.")),
                new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                    new ExactLibrarySourceCoordinate.Package(
                        PackageSourceCoordinate.Create(
                            "System.Text.Json",
                            "1.0.0"),
                        packageLibrary.LibraryIdentity)),
            ]);
    }

    private static WorkspaceRegistrationRevision Current(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;
}
