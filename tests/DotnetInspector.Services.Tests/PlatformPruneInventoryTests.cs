// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using DotnetInspector.Packages;
using NuGet.Versioning;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Pins the platform/package pruning fact: which package identities one target subsumes, and
/// whether a requested version is among them.
/// </summary>
public class PlatformPruneInventoryTests
{
    static readonly NuGetVersion Net11Pack =
        NuGetVersion.Parse("11.0.0-preview.7.26381.103");

    static readonly string[] Net11Overrides =
    [
        "System.Text.Json|11.0.0-preview.7.26381.103",
        "System.Reflection.Metadata|11.0.0-preview.7.26381.103",
        "Microsoft.Extensions.DependencyInjection.Abstractions|11.0.0-preview.7.26381.103",
        "System.Runtime|4.3.1",
        "System.Memory|5.0.0",
        "Microsoft.CSharp|4.7.0",
        "NETStandard.Library|2.0.3",
    ];

    const string NetCoreApp = "Microsoft.NETCore.App";
    const string AspNetCoreApp = "Microsoft.AspNetCore.App";

    static PlatformPruneInventory Net11() =>
        PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(NetCoreApp, "net11.0", Net11Pack),
            Net11Overrides);

    [Fact]
    public void ReadsInventoryMembershipForExactTarget()
    {
        var inventory = Net11();

        Assert.True(inventory.Contains("System.Text.Json"));
        Assert.True(inventory.Contains("NETStandard.Library"));
        Assert.False(inventory.Contains("Newtonsoft.Json"));

        Assert.True(inventory.TryGetEntry("System.Text.Json", out var entry));
        Assert.Equal(NetCoreApp, entry.Family);
        Assert.Equal(Net11Pack, entry.SuppliedVersion);
        Assert.Equal(Net11Pack, entry.TargetPackVersion);
        Assert.Equal(Net11Pack, entry.SourcePackVersion);
        Assert.Equal(PlatformPrunePrecision.Exact, entry.Precision);

        var family = Assert.Single(inventory.Families);
        Assert.Equal(Net11Pack, family.TargetPackVersion);
        Assert.Equal(Net11Pack, family.SourcePackVersion);
        Assert.True(family.DescribesTarget);
    }

    [Fact]
    public void InventoryAbsenceDoesNotClassifyPackageAvailability()
    {
        var inventory = Net11();

        // The inventory has no package-discovery or catalog input. An absent entry means only
        // that this target makes no subsumption claim for the identity.
        Assert.False(inventory.Contains("Microsoft.Extensions.FileProviders.Embedded"));
        Assert.False(inventory.TryGetEntry("Microsoft.Extensions.FileProviders.Embedded", out _));
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            inventory.Subsumes("Microsoft.Extensions.FileProviders.Embedded", "10.0.0"));
    }

    [Fact]
    public void PreservesLiteralSuppliedVersions()
    {
        var inventory = Net11();

        Assert.True(inventory.TryGetEntry("System.Text.Json", out var textJson));
        Assert.Equal(Net11Pack, textJson.SuppliedVersion);

        Assert.True(inventory.TryGetEntry("System.Runtime", out var runtime));
        Assert.Equal(NuGetVersion.Parse("4.3.1"), runtime.SuppliedVersion);

        // System.Memory stays at its literal 5.0.0 ceiling on this net11.0 target. The value is a
        // package fact, not something derived from the release band.
        Assert.True(inventory.TryGetEntry("System.Memory", out var memory));
        Assert.Equal(NuGetVersion.Parse("5.0.0"), memory.SuppliedVersion);
    }

    [Fact]
    public void SubsumptionUsesSemanticVersionOrder()
    {
        var inventory = Net11();

        Assert.Equal(
            PlatformSubsumption.Subsumed,
            inventory.Subsumes("System.Text.Json", "9.0.0"));

        // String ordering would put "10.0.0" below "9.0.0"; semantic ordering does not.
        Assert.Equal(
            PlatformSubsumption.Subsumed,
            inventory.Subsumes("System.Text.Json", "10.0.0"));

        Assert.Equal(
            PlatformSubsumption.Subsumed,
            inventory.Subsumes("System.Text.Json", Net11Pack.ToNormalizedString()));
        Assert.Equal(
            PlatformSubsumption.Subsumed,
            inventory.Subsumes("System.Runtime", "4.0.0"));
    }

    [Fact]
    public void LeapfroggingPackageIsNotSubsumed()
    {
        var inventory = Net11();

        // A package ahead of the runtime remains a distinct subject.
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            inventory.Subsumes("System.Text.Json", "12.0.0"));
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            inventory.Subsumes("System.Memory", "6.0.0"));
    }

    [Fact]
    public void UncertaintyResolvesAwayFromSubsumed()
    {
        var inventory = Net11();

        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            inventory.Subsumes("Newtonsoft.Json", "13.0.3"));

        Assert.Equal(
            PlatformSubsumption.NotComparable,
            inventory.Subsumes("System.Text.Json", (NuGetVersion?)null));
        Assert.Equal(
            PlatformSubsumption.NotComparable,
            inventory.Subsumes("System.Text.Json", "*"));
        Assert.Equal(
            PlatformSubsumption.NotComparable,
            inventory.Subsumes("System.Text.Json", (string?)null));

        // The absent identity already settles this away from subsumed.
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            inventory.Subsumes("Newtonsoft.Json", "not-a-version"));
    }

    [Fact]
    public void SuppliedVersionDoesNotAdoptDiscoveredTarget()
    {
        var source = NuGetVersion.Parse("10.0.0");
        var selected = NuGetVersion.Parse("10.0.1");
        var projected = PlatformPruneInventory.FromProjectedFamily(
            new PlatformPruneTarget(AspNetCoreApp, "net10.0", selected),
            source,
            ["Microsoft.Extensions.Caching.Memory|10.0.0"]);

        Assert.True(projected.TryGetEntry(
            "Microsoft.Extensions.Caching.Memory",
            out var entry));
        Assert.Equal(source, entry.SourcePackVersion);
        Assert.Equal(source, entry.SuppliedVersion);
        Assert.Equal(selected, entry.TargetPackVersion);
        Assert.Equal(PlatformPrunePrecision.Projected, entry.Precision);

        var family = Assert.Single(projected.Families);
        Assert.Equal(selected, family.TargetPackVersion);
        Assert.Equal(source, family.SourcePackVersion);
        Assert.False(family.DescribesTarget);
    }

    [Fact]
    public void ProjectedInventoryIsNotComparableAcrossTargets()
    {
        var source = NuGetVersion.Parse("10.0.0");
        var selected = NuGetVersion.Parse("10.0.1");
        const string packageId = "Microsoft.Extensions.Caching.Memory";
        string[] lines = [$"{packageId}|10.0.0"];

        var crossTarget = PlatformPruneInventory.FromProjectedFamily(
            new PlatformPruneTarget(AspNetCoreApp, "net10.0", selected),
            source,
            lines);
        Assert.True(crossTarget.Contains(packageId));
        Assert.Equal(
            PlatformSubsumption.NotComparable,
            crossTarget.Subsumes(packageId, "10.0.0"));

        // The same projection remains comparable for the exact target it came from.
        var sameTarget = PlatformPruneInventory.FromProjectedFamily(
            new PlatformPruneTarget(AspNetCoreApp, "net10.0", source),
            source,
            lines);
        Assert.Equal(
            PlatformSubsumption.Subsumed,
            sameTarget.Subsumes(packageId, "10.0.0"));

        // Acquiring 10.0.1 makes even the unchanged literal exact for that selected target.
        var exact = PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(AspNetCoreApp, "net10.0", selected),
            lines);
        Assert.Equal(
            PlatformSubsumption.Subsumed,
            exact.Subsumes(packageId, "10.0.0"));
    }

    [Fact]
    public void MalformedOverrideLineFails()
    {
        Assert.Throws<FormatException>(() =>
            PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(NetCoreApp, "net11.0", Net11Pack),
            ["System.Text.Json"]));
        Assert.Throws<FormatException>(() =>
            PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(NetCoreApp, "net11.0", Net11Pack),
            ["System.Text.Json|"]));
        Assert.Throws<FormatException>(() =>
            PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(NetCoreApp, "net11.0", Net11Pack),
            ["|4.3.1"]));
        Assert.Throws<FormatException>(() =>
            PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(NetCoreApp, "net11.0", Net11Pack),
            ["System.Text.Json|banana"]));

        var inventory = PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(NetCoreApp, "net11.0", Net11Pack),
            ["System.Runtime|4.3.1", "", "   "]);
        Assert.Single(inventory.Entries);
    }

    [Fact]
    public void FamilyCompositionDecidesInventoryMembership()
    {
        var net10 = NuGetVersion.Parse("10.0.11");
        var net10Base = PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(NetCoreApp, "net10.0", net10),
            ["System.Text.Json|10.0.11"]);
        var net10Web = PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(AspNetCoreApp, "net10.0", net10),
            ["Microsoft.Extensions.DependencyInjection.Abstractions|10.0.0"]);
        var net11Base = PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(NetCoreApp, "net11.0", Net11Pack),
            ["Microsoft.Extensions.DependencyInjection.Abstractions|11.0.0-preview.7.26381.103"]);

        const string di = "Microsoft.Extensions.DependencyInjection.Abstractions";

        // A net10.0 console app has only the base-family inventory.
        Assert.False(net10Base.Contains(di));

        // A net10.0 web app also composes ASP.NET Core, which publishes the entry.
        var net10WebApp = PlatformPruneInventory.Compose([net10Base, net10Web]);
        Assert.True(net10WebApp.TryGetEntry(di, out var webEntry));
        Assert.Equal(AspNetCoreApp, webEntry.Family);

        // On net11.0 the base framework publishes the entry itself.
        Assert.True(net11Base.TryGetEntry(di, out var baseEntry));
        Assert.Equal(NetCoreApp, baseEntry.Family);
    }

    [Fact]
    public void CompositionRefusesMismatchedTargetsAndPrefersTheLowerSuppliedVersion()
    {
        var net10 = PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(NetCoreApp, "net10.0", NuGetVersion.Parse("10.0.11")),
            ["System.Text.Json|10.0.11"]);
        var net11 = Net11();

        Assert.Throws<ArgumentException>(() => PlatformPruneInventory.Compose([net10, net11]));
        Assert.Throws<ArgumentException>(() => PlatformPruneInventory.Compose([]));

        var low = PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(AspNetCoreApp, "net11.0", Net11Pack),
            ["System.Text.Json|9.0.0"]);
        var composed = PlatformPruneInventory.Compose([Net11(), low]);

        Assert.True(composed.TryGetEntry("System.Text.Json", out var entry));
        Assert.Equal(NuGetVersion.Parse("9.0.0"), entry.SuppliedVersion);
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            composed.Subsumes("System.Text.Json", "10.0.0"));
    }

    [Fact]
    public void EmptyInventorySubsumesNothing()
    {
        var none = PlatformPruneInventory.None("net10.0");

        Assert.Empty(none.Entries);
        Assert.Empty(none.Families);
        Assert.Equal("net10.0", none.TargetFramework);
        Assert.False(none.Contains("System.Text.Json"));
        Assert.False(none.TryGetEntry("System.Text.Json", out _));

        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            none.Subsumes("System.Text.Json", "9.0.0"));
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            none.Subsumes("System.Text.Json", (string?)null));
    }
}
