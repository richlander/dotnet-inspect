using System.Text.Json;

using DotnetInspector.Fixtures;

using BrowserDirectUseClusterInspection =
    DotnetInspect.Web.Interop.CallGraph.BrowserDirectUseClusterInspection;
using BrowserCallGraphJsonContext =
    DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphJsonContext;
using CallGraphExports =
    DotnetInspect.Web.Interop.CallGraph.CallGraphExports;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    [Fact]
    public async Task DirectUseClusters_PreserveComponentsAndFocusedCallSites()
    {
        const string sourcePackage = "Browser.DirectUse.Source";
        const string targetPackage = "Browser.DirectUse.Target";
        const string framework = "net11.0";
        string sourceAssembly = Path.GetFileName(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        string targetAssembly = Path.GetFileName(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        _ = await Coordinate(
            sourcePackage,
            Package(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath()),
                $"lib/{framework}/{sourceAssembly}"));
        _ = await Coordinate(
            targetPackage,
            Package(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()),
                $"lib/{framework}/{targetAssembly}"));

        BrowserDirectUseClusterInspection clusters =
            await Query(
                sourcePackage,
                framework,
                sourceAssembly,
                targetPackage,
                targetAssembly,
                selectedCluster: 0);

        Assert.Equal("available", clusters.Outcome);
        Assert.True(clusters.IsComplete);
        Assert.True(clusters.Clusters.Length > 1);
        Assert.Empty(clusters.CallSites);
        DotnetInspect.Web.Interop.CallGraph.BrowserDirectUseCluster echo =
            Assert.Single(
                clusters.Clusters,
                cluster =>
                    cluster.SourceMembers == 2
                    && cluster.ProviderTypes == 1
                    && cluster.TargetMembers == 1
                    && cluster.ExtensionMethods == 1
                    && cluster.CallSites == 3);
        Assert.Contains(
            clusters.Clusters,
            cluster =>
                cluster.SourceMembers == 1
                && cluster.CallSites == 1);

        BrowserDirectUseClusterInspection focused =
            await Query(
                sourcePackage,
                framework,
                sourceAssembly,
                targetPackage,
                targetAssembly,
                echo.Ordinal);

        Assert.Equal(echo.Ordinal, focused.SelectedCluster);
        Assert.Equal(3, focused.CallSites.Length);
        Assert.All(
            focused.CallSites,
            call =>
            {
                Assert.True(call.IlOffset >= 0);
                Assert.True(call.SourceToken > 0);
                Assert.True(call.TargetToken > 0);
                Assert.True(call.EvidenceToken > 0);
                Assert.NotEmpty(call.SourceMember);
                Assert.NotEmpty(call.TargetMember);
                Assert.NotEmpty(call.EvidenceMethod);
            });

        BrowserDirectUseClusterInspection missing =
            await Query(
                sourcePackage,
                framework,
                sourceAssembly,
                targetPackage,
                targetAssembly,
                selectedCluster: int.MaxValue);
        Assert.Equal("cluster-not-found", missing.Outcome);
        Assert.NotNull(missing.Failure);
        Assert.Empty(missing.CallSites);
    }

    [Fact]
    public async Task DirectUseClusters_PreserveRejectedAndMissingSelections()
    {
        const string packageId = "Browser.DirectUse.Rejected";
        const string framework = "net11.0";
        string assembly = Path.GetFileName(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        _ = await Coordinate(
            packageId,
            Package(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath()),
                $"lib/{framework}/{assembly}"));

        BrowserDirectUseClusterInspection rejected =
            await Query(
                packageId,
                framework,
                assembly,
                packageId,
                assembly,
                selectedCluster: 0);

        Assert.Equal("rejected", rejected.Outcome);
        Assert.NotNull(rejected.Failure);
        Assert.NotEmpty(rejected.Diagnostics);
    }

    static async Task<BrowserDirectUseClusterInspection> Query(
        string sourcePackage,
        string framework,
        string sourceAssembly,
        string targetPackage,
        string targetAssembly,
        int selectedCluster)
    {
        string json = await CallGraphExports.QueryDirectUseClusters(
            sourcePackage,
            "1.0.0",
            framework,
            sourceAssembly,
            targetPackage,
            "1.0.0",
            framework,
            targetAssembly,
            selectedCluster);
        return JsonSerializer.Deserialize(
                json,
                BrowserCallGraphJsonContext.Default
                    .BrowserDirectUseClusterInspection)
            ?? throw new InvalidOperationException(
                "The Direct-Use Cluster transport was empty.");
    }
}
