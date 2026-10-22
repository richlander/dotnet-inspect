using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed class PackageRuntimePopulationChoiceTests
{
    private static readonly PackageSourceCoordinate Coordinate =
        PackageSourceCoordinate.Create("contoso.runtime", "1.0.0");

    [Fact]
    public void NoneReturnsNoMatchWithoutEligiblePopulations()
    {
        PackageAssetSelectionReceipt receipt =
            Evaluate("lib/net10.0/Neutral.dll");
        PackageRuntimePopulationChoiceOperationAssociation operation =
            PackageRuntimePopulationChoiceOperationAssociation.Create();

        PackageRuntimePopulationChoiceResult result =
            PackageRuntimePopulationChoice.Evaluate(
                Request(
                    operation,
                    Coordinate,
                    receipt,
                    new PackageRuntimePopulationAuthority.None()));

        var noMatch =
            Assert.IsType<
                PackageRuntimePopulationChoiceResult.NoMatch>(
                result);
        Assert.Same(operation, noMatch.OperationAssociation);
        Assert.Same(Coordinate, noMatch.Coordinate);
        Assert.Same(receipt, noMatch.LowerSelectionReceipt);
        Assert.Empty(noMatch.AvailableRuntimePopulations);
    }

    [Fact]
    public void NoneInfersTheSoleEligiblePopulation()
    {
        PackageAssetSelectionReceipt receipt =
            Evaluate(
                "runtimes/unix/lib/netstandard1.0/Sample.dll");

        var selected =
            Assert.IsType<
                PackageRuntimePopulationChoiceResult.Selected>(
                PackageRuntimePopulationChoice.Evaluate(
                    Request(
                        receipt,
                        new PackageRuntimePopulationAuthority.None())));

        Assert.Equal("unix", selected.RuntimeIdentifier);
        Assert.Equal(
            "netstandard1.0",
            selected.SelectedTargetFramework);
        Assert.Equal(
            PackageRuntimePopulationChoiceSource.SingleEligible,
            selected.ChoiceSource);
        Assert.Same(
            receipt.Generation,
            selected.CoordinateAssociation.Generation);
        Assert.Same(
            selected.Population,
            selected.CoordinateAssociation.Population);
    }

    [Fact]
    public void NoneSelectsTheProductDefaultAndOrdersItFirst()
    {
        PackageAssetSelectionReceipt receipt =
            Evaluate(
                "runtimes/win-x64/lib/net10.0/Windows.dll",
                "runtimes/android-arm64/lib/net10.0/Android.dll",
                "runtimes/linux-x64/lib/net10.0/Linux.dll");

        var selected =
            Assert.IsType<
                PackageRuntimePopulationChoiceResult.Selected>(
                PackageRuntimePopulationChoice.Evaluate(
                    Request(
                        receipt,
                        new PackageRuntimePopulationAuthority.None())));

        Assert.Equal(
            PackageRuntimePopulationChoice.ProductDefaultRuntimeIdentifier,
            selected.RuntimeIdentifier);
        Assert.Equal(
            PackageRuntimePopulationChoiceSource.ProductDefault,
            selected.ChoiceSource);
        Assert.Equal(
            ["linux-x64", "android-arm64", "win-x64"],
            selected.AvailableRuntimeIdentifiers);
    }

    [Fact]
    public void NoneRetainsTypedAmbiguityWithoutTheProductDefault()
    {
        PackageAssetSelectionReceipt receipt =
            Evaluate(
                "runtimes/win-x64/lib/net10.0/Windows.dll",
                "runtimes/osx-arm64/lib/net10.0/Mac.dll");

        var ambiguous =
            Assert.IsType<
                PackageRuntimePopulationChoiceResult.Ambiguous>(
                PackageRuntimePopulationChoice.Evaluate(
                    Request(
                        receipt,
                        new PackageRuntimePopulationAuthority.None())));

        Assert.Equal(
            ["osx-arm64", "win-x64"],
            ambiguous.AvailableRuntimeIdentifiers);
    }

    [Fact]
    public void ExplicitSelectsOnlyTheExactRidWithoutShrinkingInventory()
    {
        PackageAssetSelectionReceipt receipt =
            Evaluate(
                "runtimes/win-x64/lib/net10.0/Windows.dll",
                "runtimes/linux-x64/lib/net10.0/Linux.dll");

        var selected =
            Assert.IsType<
                PackageRuntimePopulationChoiceResult.Selected>(
                PackageRuntimePopulationChoice.Evaluate(
                    Request(
                        receipt,
                        new PackageRuntimePopulationAuthority.Explicit(
                            "win-x64"))));

        Assert.Equal("win-x64", selected.RuntimeIdentifier);
        Assert.Equal(
            PackageRuntimePopulationChoiceSource.Explicit,
            selected.ChoiceSource);
        Assert.Equal(
            ["linux-x64", "win-x64"],
            selected.AvailableRuntimeIdentifiers);

        Assert.IsType<
            PackageRuntimePopulationChoiceResult.NoMatch>(
            PackageRuntimePopulationChoice.Evaluate(
                Request(
                    receipt,
                    new PackageRuntimePopulationAuthority.Explicit(
                        "osx-x64"))));
    }

    [Fact]
    public void RetainedCoordinateRequiresItsExactOwnerEvidence()
    {
        var content = new InMemoryPackageContent(
            TestPackageArchive.Create(
                "runtimes/win-x64/lib/net10.0/Windows.dll",
                "runtimes/linux-x64/lib/net10.0/Linux.dll"),
            fromCache: true,
            "test-source");
        PackageAssetSelectionReceipt receipt =
            PackageAssetSelector.EvaluateSingleRuntime(content);
        var initial =
            Assert.IsType<
                PackageRuntimePopulationChoiceResult.Selected>(
                PackageRuntimePopulationChoice.Evaluate(
                    Request(
                        receipt,
                        new PackageRuntimePopulationAuthority.None())));

        var retained =
            Assert.IsType<
                PackageRuntimePopulationChoiceResult.Selected>(
                PackageRuntimePopulationChoice.Evaluate(
                    Request(
                        PackageRuntimePopulationChoiceOperationAssociation
                            .Create(),
                        Coordinate,
                        PackageAssetSelector.EvaluateSingleRuntime(content),
                        new PackageRuntimePopulationAuthority
                            .RetainedCoordinate(
                                initial.CoordinateAssociation))));

        Assert.Equal("linux-x64", retained.RuntimeIdentifier);
        Assert.Equal(
            PackageRuntimePopulationChoiceSource.RetainedCoordinate,
            retained.ChoiceSource);

        Assert.IsType<
            PackageRuntimePopulationChoiceResult.Refused>(
            PackageRuntimePopulationChoice.Evaluate(
                Request(
                    PackageRuntimePopulationChoiceOperationAssociation
                        .Create(),
                    PackageSourceCoordinate.Create(
                        "other.runtime",
                        "1.0.0"),
                    receipt,
                    new PackageRuntimePopulationAuthority
                        .RetainedCoordinate(
                            initial.CoordinateAssociation))));

        PackageAssetSelectionReceipt foreignGeneration =
            Evaluate(
                "runtimes/win-x64/lib/net10.0/Windows.dll",
                "runtimes/linux-x64/lib/net10.0/Linux.dll");
        Assert.IsType<
            PackageRuntimePopulationChoiceResult.Refused>(
            PackageRuntimePopulationChoice.Evaluate(
                Request(
                    foreignGeneration,
                    new PackageRuntimePopulationAuthority
                        .RetainedCoordinate(
                            initial.CoordinateAssociation))));

        var targetContent = new InMemoryPackageContent(
            TestPackageArchive.Create(
                "runtimes/linux-x64/lib/net8.0/Older.dll",
                "runtimes/linux-x64/lib/net10.0/Current.dll"),
            fromCache: true,
            "test-source");
        var targetInitial =
            Assert.IsType<
                PackageRuntimePopulationChoiceResult.Selected>(
                PackageRuntimePopulationChoice.Evaluate(
                    Request(
                        PackageAssetSelector.EvaluateSingleRuntime(
                            targetContent),
                        new PackageRuntimePopulationAuthority.None())));
        Assert.IsType<
            PackageRuntimePopulationChoiceResult.Refused>(
            PackageRuntimePopulationChoice.Evaluate(
                Request(
                    PackageAssetSelector.EvaluateSingleRuntime(
                        targetContent,
                        "net9.0"),
                    new PackageRuntimePopulationAuthority
                        .RetainedCoordinate(
                            targetInitial.CoordinateAssociation))));
    }

    [Fact]
    public void LowerOwnerFailureRemainsTypedFailure()
    {
        var content = new InMemoryPackageContent(
            TestPackageArchive.Create(
                "runtimes/linux-x64/lib/net10.0/Linux.dll"),
            fromCache: true,
            "test-source");
        PackageAssetSelectionReceipt receipt =
            PackageAssetSelector.EvaluateSingleRuntime(
                content,
                targetFramework: " ");

        var failed =
            Assert.IsType<
                PackageRuntimePopulationChoiceResult.Failed>(
                PackageRuntimePopulationChoice.Evaluate(
                    Request(
                        receipt,
                        new PackageRuntimePopulationAuthority.None())));

        Assert.IsType<PackageAssetSelection.Invalid>(
            failed.LowerSelection);
    }

    [Fact]
    public void RequestRejectsAPreselectedLowerRid()
    {
        var content = new InMemoryPackageContent(
            TestPackageArchive.Create(
                "runtimes/linux-x64/lib/net10.0/Linux.dll"),
            fromCache: true,
            "test-source");
        PackageAssetSelectionReceipt receipt =
            PackageAssetSelector.EvaluateSingleRuntime(
                content,
                runtimeIdentifier: "linux-x64");

        Assert.Throws<ArgumentException>(() =>
            Request(
                receipt,
                new PackageRuntimePopulationAuthority.None()));
        Assert.Throws<ArgumentException>(() =>
            new PackageRuntimePopulationAuthority.Explicit(
                "LINUX-X64"));
    }

    private static PackageRuntimePopulationChoiceRequest Request(
        PackageAssetSelectionReceipt receipt,
        PackageRuntimePopulationAuthority authority) =>
        Request(
            PackageRuntimePopulationChoiceOperationAssociation.Create(),
            Coordinate,
            receipt,
            authority);

    private static PackageRuntimePopulationChoiceRequest Request(
        PackageRuntimePopulationChoiceOperationAssociation operation,
        PackageSourceCoordinate coordinate,
        PackageAssetSelectionReceipt receipt,
        PackageRuntimePopulationAuthority authority) =>
        new(
            operation,
            coordinate,
            receipt,
            authority);

    private static PackageAssetSelectionReceipt Evaluate(
        params string[] entries) =>
        PackageAssetSelector.EvaluateSingleRuntime(
            new InMemoryPackageContent(
                TestPackageArchive.Create(entries),
                fromCache: true,
                "test-source"));
}
