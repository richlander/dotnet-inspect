// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins the ecosystem query: the registered catalog, and the platform-specific prune rows a
/// selected target contributes.
/// </summary>
public class EcosystemQueryTests
{
    [Fact]
    public void ProjectsTheRegisteredEcosystemsWithoutAPlatformTarget()
    {
        EcosystemProjection projection = EcosystemQuery.Execute(new EcosystemQueryContext(null));

        Assert.NotEmpty(projection.Ecosystems);
        Assert.Contains(projection.Ecosystems, row => row.Title == "Platform");

        // Rows are declared order, which is the order the registry authored.
        Assert.Equal(
            projection.Ecosystems.Select(row => row.Order).Order(),
            projection.Ecosystems.Select(row => row.Order));

        // No target was selected, so nothing platform-specific is claimed either way.
        Assert.Empty(projection.Pruning);
        Assert.Null(projection.PlatformTarget);
        Assert.Null(projection.PruningUnavailable);
    }

    [Fact]
    public void ProjectsPruningForASelectedPlatformTarget()
    {
        EcosystemProjection projection =
            EcosystemQuery.Execute(new EcosystemQueryContext("runtime"));

        Assert.False(string.IsNullOrWhiteSpace(projection.PlatformTarget));
        Assert.Null(projection.PruningUnavailable);
        Assert.NotEmpty(projection.Pruning);

        Assert.Contains(projection.Pruning, row =>
            row.PackageId.Equals("System.Text.Json", StringComparison.OrdinalIgnoreCase));

        // Both populations are present, and the distinction is what explains a result: a frozen
        // entry is subsumed for any plausible request, a live one turns on the comparison.
        Assert.Contains(projection.Pruning, row => row.Live);
        Assert.Contains(projection.Pruning, row => !row.Live);
        Assert.All(projection.Pruning, row =>
            Assert.False(string.IsNullOrWhiteSpace(row.Family)));
    }

    [Fact]
    public void APlatformFailureDoesNotSuppressTheEcosystemCatalog()
    {
        // The registry is compiled into the product and does not depend on an installed pack, so
        // a platform-side failure must narrow the answer rather than empty it.
        EcosystemProjection projection =
            EcosystemQuery.Execute(new EcosystemQueryContext("not-a-framework"));

        Assert.NotEmpty(projection.Ecosystems);
        Assert.Empty(projection.Pruning);
        Assert.False(string.IsNullOrWhiteSpace(projection.PruningUnavailable));
    }
}
