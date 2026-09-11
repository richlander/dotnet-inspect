// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Pins reading a platform prune inventory from the reference packs installed on this machine.
/// </summary>
/// <remarks>
/// The prune data ships inside each reference pack, so an SDK-bearing machine already has it and
/// no acquisition is required. These cases assert the shape of the answer rather than the exact
/// membership, which moves with the installed SDK.
/// </remarks>
public class InstalledPlatformPruneSourceTests
{
    [Fact]
    public void ReadsTheInstalledRuntimeFamily()
    {
        InstalledPlatformPruneSource.Result result = InstalledPlatformPruneSource.Read("runtime");

        Assert.Null(result.Error);
        PlatformPruneInventory inventory = Assert.IsType<PlatformPruneInventory>(result.Inventory);

        PlatformPruneFamily family = Assert.Single(inventory.Families);
        Assert.Equal("Microsoft.NETCore.App", family.Name);
        Assert.StartsWith("net", inventory.TargetFramework, StringComparison.Ordinal);

        // The installed pack describes itself exactly, so no value is a projection.
        Assert.True(family.DescribesTarget);
        Assert.Equal(PlatformPrunePrecision.Exact, family.Precision);

        // System.Text.Json has been a subsumed identity for every supported target.
        Assert.Contains(inventory.Entries, entry =>
            entry.PackageId.Equals("System.Text.Json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadsTheInstalledAspNetCoreFamily()
    {
        InstalledPlatformPruneSource.Result result = InstalledPlatformPruneSource.Read("aspnetcore");

        Assert.Null(result.Error);
        PlatformPruneInventory inventory = Assert.IsType<PlatformPruneInventory>(result.Inventory);
        Assert.Equal("Microsoft.AspNetCore.App", Assert.Single(inventory.Families).Name);
    }

    [Fact]
    public void ComposesTheFamiliesOneTargetReferences()
    {
        // A web app references both, and its inventory is their union. This is the composition
        // the console/web distinction turns on.
        PlatformPruneInventory core =
            Assert.IsType<PlatformPruneInventory>(InstalledPlatformPruneSource.Read("runtime").Inventory);
        PlatformPruneInventory web =
            Assert.IsType<PlatformPruneInventory>(InstalledPlatformPruneSource.Read("aspnetcore").Inventory);

        if (!string.Equals(core.TargetFramework, web.TargetFramework, StringComparison.OrdinalIgnoreCase))
        {
            return; // Different installed targets cannot compose; that is asserted elsewhere.
        }

        PlatformPruneInventory composed = PlatformPruneInventory.Compose([core, web]);
        Assert.Equal(2, composed.Families.Count());
        Assert.True(composed.Entries.Count() >= core.Entries.Count());
    }

    [Fact]
    public void UnknownFrameworkFailsWithoutAnInventory()
    {
        InstalledPlatformPruneSource.Result result =
            InstalledPlatformPruneSource.Read("not-a-framework");

        Assert.Null(result.Inventory);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
