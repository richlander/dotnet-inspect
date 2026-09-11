// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using DotnetInspector.Packages;
using NuGet.Versioning;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Pins the transform from a package identity, in the context of one platform target, to what
/// that target supplies for it.
/// </summary>
/// <remarks>
/// The decision a consumer acts on is <see cref="PlatformSupply.DelegatesToPlatform"/>, and it is
/// true for exactly one of the three subsumption results. An unanswerable comparison must not
/// send a caller to the platform's older copy while a newer package exists, so both
/// <c>NotSubsumed</c> and <c>NotComparable</c> answer false.
/// </remarks>
public class PlatformPrunePolicyTests
{
    static readonly NuGetVersion Net11Pack = NuGetVersion.Parse("11.0.0-preview.7.26381.103");

    static PackageCoordinate At(string id, string? version = null, string? framework = null) =>
        new(id, version, framework);

    static PlatformPruneInventory Net11() =>
        PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget("Microsoft.NETCore.App", "net11.0", Net11Pack),
            [
                "System.Text.Json|11.0.0-preview.7.26381.103",
                "System.Runtime|4.3.1",
                "NETStandard.Library|2.0.3",
            ]);

    [Fact]
    public void SubsumedIdentityReportsFamilyAndSuppliedVersion()
    {
        PlatformSupply supply = PlatformPrunePolicy.Decide(Net11(), At("System.Text.Json", "9.0.0"));

        Assert.Equal(PlatformSubsumption.Subsumed, supply.Subsumption);
        Assert.True(supply.DelegatesToPlatform);
        Assert.Equal("Microsoft.NETCore.App", supply.Family);
        Assert.Equal(Net11Pack, supply.SuppliedVersion);
    }

    [Fact]
    public void LeapfroggingVersionKeepsTheEntryButDoesNotDelegate()
    {
        // The identity is known and the entry is still worth reporting -- a consumer explaining
        // the outcome needs it -- but the platform cannot answer for this version.
        PlatformSupply supply = PlatformPrunePolicy.Decide(Net11(), At("System.Text.Json", "12.0.0"));

        Assert.Equal(PlatformSubsumption.NotSubsumed, supply.Subsumption);
        Assert.False(supply.DelegatesToPlatform);
        Assert.Equal("Microsoft.NETCore.App", supply.Family);
        Assert.Equal(Net11Pack, supply.SuppliedVersion);
    }

    [Fact]
    public void UnansweredComparisonKeepsTheEntryButDoesNotDelegate()
    {
        PlatformPruneInventory inventory = Net11();

        // An omitted coordinate version floats to latest rather than meaning "none", so it is
        // not comparable until something resolves it.
        PlatformSupply unresolved =
            PlatformPrunePolicy.Decide(inventory, At("System.Text.Json"));
        Assert.Equal(PlatformSubsumption.NotComparable, unresolved.Subsumption);
        Assert.False(unresolved.DelegatesToPlatform);
        Assert.Equal("Microsoft.NETCore.App", unresolved.Family);
        Assert.Equal(Net11Pack, unresolved.SuppliedVersion);
    }

    [Fact]
    public void UnknownIdentitySuppliesNothing()
    {
        PlatformSupply supply = PlatformPrunePolicy.Decide(Net11(), At("Newtonsoft.Json", "13.0.3"));

        Assert.Equal(PlatformSupply.None, supply);
        Assert.False(supply.DelegatesToPlatform);
        Assert.Null(supply.Family);
        Assert.Null(supply.SuppliedVersion);
    }

    [Fact]
    public void WorkspaceWithNoPlatformSuppliesNothing()
    {
        PlatformSupply supply = PlatformPrunePolicy.Decide(
            PlatformPruneInventory.None("net10.0"), At("System.Text.Json", "9.0.0"));

        Assert.Equal(PlatformSupply.None, supply);
        Assert.False(supply.DelegatesToPlatform);
    }

    [Fact]
    public void InvalidCoordinateFailsBeforePolicy()
    {
        Assert.Throws<ArgumentException>(() =>
            PlatformPrunePolicy.Decide(Net11(), At("System.Text.Json", "*")));
        Assert.Throws<ArgumentException>(() =>
            PlatformPrunePolicy.Decide(Net11(), At("../System.Text.Json", "9.0.0")));
    }

    [Fact]
    public void CoordinateFrameworkMustNameTheInventoryTarget()
    {
        PlatformPruneInventory inventory = Net11();

        // Matching, and omitted, are both fine.
        Assert.True(PlatformPrunePolicy
            .Decide(inventory, At("System.Text.Json", "9.0.0", "net11.0")).DelegatesToPlatform);
        Assert.True(PlatformPrunePolicy
            .Decide(inventory, At("System.Text.Json", "9.0.0")).DelegatesToPlatform);

        // Answering a net8.0 question from a net11.0 inventory would be a different question
        // quietly answered, and subsumption is per framework.
        Assert.Throws<ArgumentException>(() => PlatformPrunePolicy
            .Decide(inventory, At("System.Text.Json", "9.0.0", "net8.0")));
    }

    [Fact]
    public void RuntimeIdentifierDoesNotChangeTheAnswer()
    {
        // Pruning is a per-framework fact; no RID narrows or widens it.
        PlatformSupply plain = PlatformPrunePolicy.Decide(Net11(), At("System.Text.Json", "9.0.0"));
        PlatformSupply rid = PlatformPrunePolicy.Decide(
            Net11(), new PackageCoordinate("System.Text.Json", "9.0.0", null, "linux-x64"));
        Assert.Equal(plain, rid);
    }

    [Fact]
    public void ReceiptRetainsTheExactComparisonInputs()
    {
        PlatformPruneInventory inventory = Net11();
        PackageCoordinate coordinate =
            At("System.Text.Json", "9.0.0", "net11.0");
        var supply = new PlatformSupply(
            PlatformSubsumption.Subsumed,
            "Microsoft.NETCore.App",
            Net11Pack);

        var receipt = new PlatformSupplyReceipt(
            inventory,
            coordinate,
            supply);

        Assert.Same(inventory, receipt.Inventory);
        Assert.Same(coordinate, receipt.Coordinate);
        Assert.Same(supply, receipt.Supply);
        Assert.True(receipt.Supply.DelegatesToPlatform);
    }

    [Fact]
    public void EqualSupplyDoesNotCollapseComparisonCorrespondence()
    {
        PlatformPruneInventory firstInventory = Net11();
        PlatformPruneInventory secondInventory = Net11();
        PackageCoordinate firstCoordinate =
            At("System.Text.Json", "9.0.0", "net11.0");
        PackageCoordinate secondCoordinate =
            At("System.Text.Json", "9.0.0", "net11.0");

        PlatformSupplyReceipt first =
            PlatformPrunePolicy.Evaluate(firstInventory, firstCoordinate);
        PlatformSupplyReceipt second =
            PlatformPrunePolicy.Evaluate(secondInventory, secondCoordinate);

        Assert.Equal(first.Supply, second.Supply);
        Assert.NotSame(first.Inventory, second.Inventory);
        Assert.NotSame(first.Coordinate, second.Coordinate);
    }

    [Fact]
    public void AbsentEntryReceiptRetainsInputsAndCanonicalNoSupply()
    {
        PlatformPruneInventory inventory = Net11();
        PackageCoordinate coordinate =
            At("Newtonsoft.Json", "13.0.3", "net11.0");

        PlatformSupplyReceipt receipt =
            PlatformPrunePolicy.Evaluate(inventory, coordinate);

        Assert.Same(inventory, receipt.Inventory);
        Assert.Same(coordinate, receipt.Coordinate);
        Assert.Same(PlatformSupply.None, receipt.Supply);
    }
}
