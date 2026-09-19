using DotnetInspect.Web.Interop.Package;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;

namespace DotnetInspect.Web.Tests;

public sealed class BrowserWorkspacePackageSetOperationsTests
{
    [Fact]
    public void PackageSets_DeclareExactOrderedCatalogMembership()
    {
        BrowserWorkspacePackageSetCatalog catalog =
            BrowserWorkspacePackageSetOperations.PackageSets();
        Assert.Equal(1, catalog.Version);
        Assert.Equal(
            PackageSetCatalog.Discover().Length,
            catalog.PackageSets.Length);

        foreach (PackageSetDescriptor expected in PackageSetCatalog.Discover())
        {
            BrowserWorkspacePackageSetDescriptor actual =
                Assert.Single(
                    catalog.PackageSets,
                    packageSet => packageSet.Id == expected.Id.Value);
            Assert.Equal(expected.Title, actual.Title);
            Assert.Equal(expected.Summary, actual.Summary);
            Assert.Equal(expected.Order, actual.Order);
            Assert.Equal("PackageGroup", actual.SourceKind);
            Assert.Equal(expected.Members.Length, actual.Coordinates.Length);
            Assert.Equal(
                expected.Members.Select(member => member.PackageId),
                actual.Coordinates.Select(coordinate => coordinate.PackageId));
            Assert.All(
                actual.Coordinates,
                coordinate =>
                {
                    Assert.Null(coordinate.Version);
                    Assert.Null(coordinate.Framework);
                    Assert.Null(coordinate.RuntimeIdentifier);
                });
        }
    }

    [Fact]
    public void PackageSets_ExposeFittingAndOverCapacityPathologicalCases()
    {
        BrowserWorkspacePackageSetCatalog catalog =
            BrowserWorkspacePackageSetOperations.PackageSets();

        BrowserWorkspacePackageSetDescriptor extensions =
            Assert.Single(
                catalog.PackageSets,
                packageSet =>
                    packageSet.Id
                    == PackageSetIds.MicrosoftExtensions.Value);
        BrowserWorkspacePackageSetDescriptor aspire =
            Assert.Single(
                catalog.PackageSets,
                packageSet => packageSet.Id == PackageSetIds.Aspire.Value);

        Assert.Equal(44, extensions.Coordinates.Length);
        Assert.Equal(82, aspire.Coordinates.Length);
    }

    [Fact]
    public void Plan_ProducesOneCompleteAggregateRequest()
    {
        BrowserWorkspacePackageSetPlan.Ready plan =
            Assert.IsType<BrowserWorkspacePackageSetPlan.Ready>(
                BrowserWorkspacePackageSetOperations.Plan(
                    [
                        new BrowserWorkspacePackage(
                            "System.Text.Json",
                            "10.0.0",
                            "net10.0"),
                    ],
                    occupiedCoordinateCount: 1,
                    PackageSetIds.MicrosoftExtensions.Value));

        Assert.Equal(PackageSetIds.MicrosoftExtensions, plan.Descriptor.Id);
        Assert.Equal(44, plan.RequestedPackageCount);
        Assert.Equal(
            WorkspaceScopeLimits.DefaultMaxPackages - 1,
            plan.AvailableSlots);
        Assert.Equal(45, plan.Requests.Length);
        Assert.Equal("System.Text.Json", plan.Requests[0].PackageId);
        Assert.Equal("10.0.0", plan.Requests[0].Version);
        Assert.Equal("net10.0", plan.Requests[0].TargetFramework);
        Assert.Equal(
            plan.Descriptor.Members.Select(member => member.PackageId),
            plan.Requests.Skip(1).Select(request => request.PackageId));
    }

    [Fact]
    public void Plan_RejectsOverCapacityBeforeAggregateRealization()
    {
        BrowserWorkspacePackageSetPlan.CapacityRejected rejected =
            Assert.IsType<BrowserWorkspacePackageSetPlan.CapacityRejected>(
                BrowserWorkspacePackageSetOperations.Plan(
                    [],
                    occupiedCoordinateCount: 0,
                    PackageSetIds.Aspire.Value));

        Assert.Equal(82, rejected.RequestedPackageCount);
        Assert.Equal(
            WorkspaceScopeLimits.DefaultMaxPackages,
            rejected.AvailableSlots);
    }
}
