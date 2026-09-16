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
        // the console/web distinction turns on. `Compose` itself is pinned on synthetic data in
        // PlatformPruneInventoryTests; what this adds is that the two installed families this
        // source reads are actually composable.
        PlatformPruneInventory core =
            Assert.IsType<PlatformPruneInventory>(InstalledPlatformPruneSource.Read("runtime").Inventory);
        PlatformPruneInventory web =
            Assert.IsType<PlatformPruneInventory>(InstalledPlatformPruneSource.Read("aspnetcore").Inventory);

        // Different installed targets describe no one framework and cannot compose. Skip rather
        // than pass, so a machine that proves nothing here does not read as evidence.
        Assert.SkipUnless(
            string.Equals(core.TargetFramework, web.TargetFramework, StringComparison.OrdinalIgnoreCase),
            $"the installed runtime ({core.TargetFramework}) and ASP.NET Core "
            + $"({web.TargetFramework}) packs target different frameworks");

        PlatformPruneInventory composed = PlatformPruneInventory.Compose([core, web]);
        Assert.Equal(2, composed.Families.Count());
        Assert.True(composed.Entries.Count() >= core.Entries.Count());
    }

    [Fact]
    public void EveryFrameworkTheResolverMapsNamesAKnownSharedFramework()
    {
        // The family name is derived from the resolver's own pack mapping, so a framework it
        // learns to resolve cannot become a pack this source then refuses to name.
        int resolved = 0;
        foreach (string frameworkName in PlatformResolver.FrameworkMappings.Keys)
        {
            (string? refPath, _, string? resolveError) =
                PlatformResolver.ResolveFramework(frameworkName);
            if (resolveError is not null || refPath is null)
            {
                continue; // Not installed here, so there is nothing for this source to read.
            }

            resolved++;
            InstalledPlatformPruneSource.Result result =
                InstalledPlatformPruneSource.Read(frameworkName);
            Assert.Null(result.Error);
            Assert.NotNull(result.Inventory);
        }

        Assert.True(
            resolved > 0,
            "no mapped framework resolved on this machine, so this pins nothing");
    }

    [Fact]
    public void UnknownFrameworkFailsWithoutAnInventory()
    {
        InstalledPlatformPruneSource.Result result =
            InstalledPlatformPruneSource.Read("not-a-framework");

        Assert.Null(result.Inventory);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void MissingOverrideFileProducesAnExactEmptyInventory()
    {
        string packsDirectory = CreateRuntimePack();
        try
        {
            InstalledPlatformPruneSource.Result result =
                InstalledPlatformPruneSource.Read("runtime@99.0.0", packsDirectory);

            Assert.Null(result.Error);
            PlatformPruneInventory inventory =
                Assert.IsType<PlatformPruneInventory>(result.Inventory);
            Assert.Empty(inventory.Entries);
            Assert.Equal(
                "Microsoft.NETCore.App",
                Assert.Single(inventory.Families).Name);
        }
        finally
        {
            Directory.Delete(packsDirectory, recursive: true);
        }
    }

    [Fact]
    public void UnreadableOverridePathFailsWithoutAnInventory()
    {
        string packsDirectory = CreateRuntimePack();
        try
        {
            Directory.CreateDirectory(
                Path.Combine(
                    packsDirectory,
                    "Microsoft.NETCore.App.Ref",
                    "99.0.0",
                    "data",
                    "PackageOverrides.txt"));

            InstalledPlatformPruneSource.Result result =
                InstalledPlatformPruneSource.Read("runtime@99.0.0", packsDirectory);

            Assert.Null(result.Inventory);
            Assert.Contains("Could not read", result.Error, StringComparison.Ordinal);
            Assert.Contains("PackageOverrides.txt", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(packsDirectory, recursive: true);
        }
    }

    private static string CreateRuntimePack()
    {
        string packsDirectory =
            Directory.CreateTempSubdirectory("installed-platform-prune-").FullName;
        string referenceDirectory = Directory.CreateDirectory(
            Path.Combine(
                packsDirectory,
                "Microsoft.NETCore.App.Ref",
                "99.0.0",
                "ref",
                "net99.0")).FullName;
        File.WriteAllBytes(Path.Combine(referenceDirectory, "System.Runtime.dll"), []);
        return packsDirectory;
    }
}
