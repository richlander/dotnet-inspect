// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using DotnetInspector.Packages;
using NuGet.Versioning;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Pins the platform/package pruning fact: which package identities a platform target subsumes,
/// and whether a requested version is among them.
/// </summary>
/// <remarks>
/// <para>
/// The asymmetry that shapes these cases is that over-claiming and under-claiming are not equally
/// wrong. Reporting a package as subsumed when the platform supplies an older version delegates
/// the caller to a stale implementation and hides that a newer package exists; reporting it as not
/// subsumed when the platform has caught up merely offers a package the caller did not need. Every
/// uncertainty therefore resolves away from <see cref="PlatformSubsumption.Subsumed"/>.
/// </para>
/// <para>
/// The literals below are drawn from real reference packs. `System.Text.Json` is a live entry
/// whose supplied version tracks the pack; `System.Runtime` and `System.Memory` are frozen at
/// netstandard-era versions and show that the supplied version is a per-package fact rather than
/// something derivable from the release band.
/// </para>
/// </remarks>
public class PlatformPruneInventoryTests
{
    static readonly NuGetVersion Net11Pack = NuGetVersion.Parse("11.0.0-preview.7.26381.103");

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
        PlatformPruneInventory.ForFamily(NetCoreApp, "net11.0", Net11Pack, Net11Overrides);

    [Fact]
    public void ClassifiesPlatformOnlyPackageOnlyAndOverlapping()
    {
        var inventory = Net11();

        // An entry plus a library of that name: the common overlap.
        Assert.Equal(
            PlatformPruneClassification.Overlapping,
            inventory.Classify("System.Text.Json", hasPlatformLibrary: true));

        // An entry with no library of that name is still overlapping. NETStandard.Library is
        // subsumed without the framework shipping an assembly under that name.
        Assert.Equal(
            PlatformPruneClassification.Overlapping,
            inventory.Classify("NETStandard.Library", hasPlatformLibrary: false));

        // A library with no entry: no package supplies it.
        Assert.Equal(
            PlatformPruneClassification.PlatformOnly,
            inventory.Classify("System.Private.CoreLib", hasPlatformLibrary: true));

        // Neither: an ordinary package, including one that is platform-adjacent but ships out of
        // band. An ecosystem may still call this a core package; that is a separate concern.
        Assert.Equal(
            PlatformPruneClassification.PackageOnly,
            inventory.Classify("Microsoft.Extensions.AI", hasPlatformLibrary: false));
        Assert.Equal(
            PlatformPruneClassification.PackageOnly,
            inventory.Classify("Newtonsoft.Json", hasPlatformLibrary: false));
    }

    [Fact]
    public void DerivesLiveSuppliedVersionAndStoresFrozenLiterals()
    {
        var inventory = Net11();

        Assert.True(inventory.TryGetSuppliedVersion("System.Text.Json", out var live, out var liveFamily));
        Assert.Equal(Net11Pack, live);
        Assert.Equal(NetCoreApp, liveFamily);

        Assert.True(inventory.TryGetSuppliedVersion("System.Runtime", out var frozen, out _));
        Assert.Equal(NuGetVersion.Parse("4.3.1"), frozen);

        // The supplied version is a per-package fact. System.Memory sits at 5.0.0 on a net11.0
        // target, so it cannot be inferred from the release band.
        Assert.True(inventory.TryGetSuppliedVersion("System.Memory", out var memory, out _));
        Assert.Equal(NuGetVersion.Parse("5.0.0"), memory);

        Assert.False(inventory.TryGetSuppliedVersion("Newtonsoft.Json", out _, out _));

        Assert.Equal(
            ["Microsoft.Extensions.DependencyInjection.Abstractions", "System.Reflection.Metadata", "System.Text.Json"],
            inventory.LiveEntries.Select(entry => entry.PackageId));
    }

    [Fact]
    public void SubsumptionUsesSemanticVersionOrder()
    {
        var inventory = Net11();

        // 9.0.0 is below the live supplied version, so the platform answers for it.
        Assert.Equal(
            PlatformSubsumption.Subsumed,
            inventory.Subsumes("System.Text.Json", "9.0.0"));

        // String ordering would rank "10.0.0" below "9.0.0"; semantic ordering does not.
        Assert.Equal(
            PlatformSubsumption.Subsumed,
            inventory.Subsumes("System.Text.Json", "10.0.0"));

        // The supplied version itself is at the boundary and is subsumed.
        Assert.Equal(
            PlatformSubsumption.Subsumed,
            inventory.Subsumes("System.Text.Json", Net11Pack.ToNormalizedString()));

        // A frozen entry subsumes any plausible request, which is why the comparison is a
        // formality for the 219 frozen overlaps and the whole question for the 55 live ones.
        Assert.Equal(
            PlatformSubsumption.Subsumed,
            inventory.Subsumes("System.Runtime", "4.0.0"));
    }

    [Fact]
    public void LeapfroggingPackageIsNotSubsumed()
    {
        var inventory = Net11();

        // A package that ships ahead of the runtime is a genuinely distinct subject. The platform
        // cannot answer for it, and claiming otherwise would hide the newer package.
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            inventory.Subsumes("System.Text.Json", "12.0.0"));

        // A frozen entry leapfrogged long ago for anything modern.
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            inventory.Subsumes("System.Memory", "6.0.0"));
    }

    [Fact]
    public void UncertaintyResolvesAwayFromSubsumed()
    {
        var inventory = Net11();

        // Not in the inventory at all.
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            inventory.Subsumes("Newtonsoft.Json", "13.0.3"));

        // An absent or floating request is not a comparison. It must not default to subsumed;
        // the caller decides what an unversioned request means.
        Assert.Equal(
            PlatformSubsumption.NotComparable,
            inventory.Subsumes("System.Text.Json", (NuGetVersion?)null));
        Assert.Equal(
            PlatformSubsumption.NotComparable,
            inventory.Subsumes("System.Text.Json", "*"));
        Assert.Equal(
            PlatformSubsumption.NotComparable,
            inventory.Subsumes("System.Text.Json", (string?)null));

        // An unparsable request for a package the platform does not subsume stays NotSubsumed:
        // the identity already settles it without a comparison.
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            inventory.Subsumes("Newtonsoft.Json", "not-a-version"));
    }

    [Fact]
    public void DerivationDoesNotAdoptDiscoveredVersion()
    {
        // An inventory carries the target it was built for. Discovering a newer pack cannot
        // relabel this one; building for the newer target is a separate inventory.
        var shipped = Net11();
        var discovered = PlatformPruneInventory.ForFamily(NetCoreApp, 
            "net11.0",
            NuGetVersion.Parse("11.0.0"),
            ["System.Text.Json|11.0.0", "System.Runtime|4.3.1"]);

        Assert.True(shipped.TryGetSuppliedVersion("System.Text.Json", out var fromShipped, out _));
        Assert.True(discovered.TryGetSuppliedVersion("System.Text.Json", out var fromDiscovered, out _));

        Assert.Equal(Net11Pack, fromShipped);
        Assert.Equal(NuGetVersion.Parse("11.0.0"), fromDiscovered);
        Assert.NotEqual(fromShipped, fromDiscovered);

        // The shipped inventory still reports its own target, so a consumer cannot present its
        // inventory under the discovered version.
        Assert.Equal(Net11Pack, shipped.Families.Single().PackVersion);
    }

    [Fact]
    public void MalformedOverrideLineFails()
    {
        // A dropped identity would silently turn an overlapping package into a package-only one,
        // so a malformed line is a failure rather than a skipped row.
        Assert.Throws<FormatException>(() =>
            PlatformPruneInventory.ForFamily(NetCoreApp, "net11.0", Net11Pack, ["System.Text.Json"]));
        Assert.Throws<FormatException>(() =>
            PlatformPruneInventory.ForFamily(NetCoreApp, "net11.0", Net11Pack, ["System.Text.Json|"]));
        Assert.Throws<FormatException>(() =>
            PlatformPruneInventory.ForFamily(NetCoreApp, "net11.0", Net11Pack, ["|4.3.1"]));
        Assert.Throws<FormatException>(() =>
            PlatformPruneInventory.ForFamily(NetCoreApp, "net11.0", Net11Pack, ["System.Text.Json|banana"]));

        // Blank lines are ordinary padding, not corruption.
        var inventory = PlatformPruneInventory.ForFamily(NetCoreApp, 
            "net11.0", Net11Pack, ["System.Runtime|4.3.1", "", "   "]);
        Assert.Single(inventory.Entries);
    }

    [Fact]
    public void FamilyCompositionDecidesThePlatformExtensionsBoundary()
    {
        // Real membership. On net10.0 the base framework prunes no Microsoft.Extensions package;
        // ASP.NET Core prunes 46. On net11.0 nine of them moved into the base framework.
        var net10Base = PlatformPruneInventory.ForFamily(
            NetCoreApp, "net10.0", NuGetVersion.Parse("10.0.11"), ["System.Text.Json|10.0.11"]);
        var net10Web = PlatformPruneInventory.ForFamily(
            AspNetCoreApp,
            "net10.0",
            NuGetVersion.Parse("10.0.11"),
            ["Microsoft.Extensions.DependencyInjection.Abstractions|10.0.0"]);
        var net11Base = PlatformPruneInventory.ForFamily(
            NetCoreApp,
            "net11.0",
            Net11Pack,
            ["Microsoft.Extensions.DependencyInjection.Abstractions|11.0.0-preview.7.26381.103"]);

        const string di = "Microsoft.Extensions.DependencyInjection.Abstractions";

        // A net10.0 console app references only the base framework, so DI.Abstractions is an
        // ordinary package and belongs to the Extensions ecosystem.
        Assert.Equal(
            PlatformPruneClassification.PackageOnly,
            net10Base.Classify(di, hasPlatformLibrary: false));

        // A net10.0 web app also references ASP.NET Core, so the same package is Platform.
        var net10WebApp = PlatformPruneInventory.Compose([net10Base, net10Web]);
        Assert.Equal(
            PlatformPruneClassification.Overlapping,
            net10WebApp.Classify(di, hasPlatformLibrary: true));
        Assert.True(net10WebApp.TryGetSuppliedVersion(di, out _, out var supplier));
        Assert.Equal(AspNetCoreApp, supplier);

        // On net11.0 the console app gets it from the base framework without referencing
        // ASP.NET Core at all. The boundary moved without anyone authoring a prefix rule.
        Assert.Equal(
            PlatformPruneClassification.Overlapping,
            net11Base.Classify(di, hasPlatformLibrary: true));
        Assert.True(net11Base.TryGetSuppliedVersion(di, out _, out var net11Supplier));
        Assert.Equal(NetCoreApp, net11Supplier);
    }

    [Fact]
    public void CompositionRefusesMismatchedTargetsAndPrefersTheLowerSuppliedVersion()
    {
        var net10 = PlatformPruneInventory.ForFamily(
            NetCoreApp, "net10.0", NuGetVersion.Parse("10.0.11"), ["System.Text.Json|10.0.11"]);
        var net11 = Net11();

        // Composing across target frameworks would describe no real app.
        Assert.Throws<ArgumentException>(() => PlatformPruneInventory.Compose([net10, net11]));
        Assert.Throws<ArgumentException>(() => PlatformPruneInventory.Compose([]));

        // The shipped families publish disjoint identities, so this rule is defensive. When two
        // families do claim one identity, the lower supplied version wins, because that is the
        // direction that cannot over-claim.
        var low = PlatformPruneInventory.ForFamily(
            AspNetCoreApp, "net11.0", Net11Pack, ["System.Text.Json|9.0.0"]);
        var composed = PlatformPruneInventory.Compose([Net11(), low]);
        Assert.True(composed.TryGetSuppliedVersion("System.Text.Json", out var supplied, out _));
        Assert.Equal(NuGetVersion.Parse("9.0.0"), supplied);
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            composed.Subsumes("System.Text.Json", "10.0.0"));
    }

    [Fact]
    public void NoPlatformInventorySubsumesNothing()
    {
        // Removing the platform is a coherent workspace, not a broken one: nothing is subsumed,
        // so every package reference is followed as a package reference.
        var none = PlatformPruneInventory.None("net10.0");

        Assert.Empty(none.Entries);
        Assert.Empty(none.Families);
        Assert.Equal("net10.0", none.TargetFramework);

        Assert.Equal(
            PlatformPruneClassification.PackageOnly,
            none.Classify("System.Text.Json", hasPlatformLibrary: false));
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            none.Subsumes("System.Text.Json", "9.0.0"));
        Assert.Equal(
            PlatformSubsumption.NotSubsumed,
            none.Subsumes("System.Text.Json", (string?)null));
        Assert.False(none.TryGetSuppliedVersion("System.Text.Json", out _, out _));

        // A library the platform would have supplied is still reported as platform-only when the
        // catalog says so; the inventory only answers the package question.
        Assert.Equal(
            PlatformPruneClassification.PlatformOnly,
            none.Classify("System.Private.CoreLib", hasPlatformLibrary: true));
    }
}
