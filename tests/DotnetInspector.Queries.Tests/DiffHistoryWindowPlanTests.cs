using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using NuGet.Versioning;

namespace DotnetInspector.Queries.Tests;

// A version window evaluates through the existing Diff History plans
// (version-resolution.md#version-window-projection).
public sealed class DiffHistoryWindowPlanTests
{
    static readonly PackageVersionInfo[] Listing =
    [
        .. new[] { "8.0.0", "8.0.1", "9.0.0", "9.0.1", "10.0.0", "10.0.1", "10.0.2" }
            .Select(version => new PackageVersionInfo(version, Listed: true)),
    ];

    [Fact]
    public void SupplyChainEvaluatesItsWholePopulation()
    {
        PackageVersionWindow window = Window(PackageVersionWindowPolicy.SupplyChain, 2);

        DiffHistoryEvaluationPlan plan = DiffHistoryEvaluationPlan.For(window);

        Assert.IsType<DiffHistoryEvaluationPlan.FullPopulation>(plan);
        Assert.Equal(window.Points, plan.ResolveInitialSelection(window.Population));
    }

    [Fact]
    public void ApiEvolutionEvaluatesItsPointsOnly()
    {
        PackageVersionWindow window = Window(PackageVersionWindowPolicy.ApiEvolution, 2);

        DiffHistoryEvaluationPlan plan = DiffHistoryEvaluationPlan.For(window);

        Assert.IsType<DiffHistoryEvaluationPlan.ExplicitCheckpoints>(plan);
        Assert.Equal(
            ["8.0.1", "9.0.1", "10.0.2"],
            plan.ResolveInitialSelection(window.Population).Select(address => address.NormalizedVersion));
        Assert.Equal(6, window.Population.Addresses.Length);
    }

    static PackageVersionWindow Window(PackageVersionWindowPolicy policy, int depth)
    {
        Assert.True(PackageVersionWindow.TryProject(
            "Example.Package",
            policy,
            depth,
            NuGetVersion.Parse("10.0.2"),
            Listing,
            listingIsAuthoritative: true,
            out PackageVersionWindow? window,
            out _));
        return window!;
    }
}
