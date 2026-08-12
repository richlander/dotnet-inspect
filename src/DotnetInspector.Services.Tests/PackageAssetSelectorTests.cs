using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Asset selection over package content, exercised through the in-memory
/// content a browser host uses. The selector never opens an entry, so these
/// fixtures carry entry layouts rather than real images.
/// </summary>
public sealed class PackageAssetSelectorTests
{
    [Fact]
    public void Select_TakesHighestApplicableFrameworkFolder()
    {
        PackageAssetSelection selection = Select(
            "net9.0",
            null,
            "lib/netstandard2.0/Sample.dll",
            "lib/net8.0/Sample.dll",
            "lib/net10.0/Sample.dll");

        PackageAssetUniverse universe = Selected(selection);
        Assert.Equal("net8.0", universe.TargetFramework);
        Assert.Equal(
            "lib/net8.0/Sample.dll",
            Assert.Single(universe.Assets).EntryPath);
    }

    [Fact]
    public void Select_FallsBackToACompatibleOlderFramework()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/netstandard2.0/Sample.dll");

        Assert.Equal("netstandard2.0", Selected(selection).TargetFramework);
    }

    [Fact]
    public void Select_RejectsAnIncompatibleFrameworkFamily()
    {
        PackageAssetSelection selection = Select(
            "net481",
            null,
            "lib/net8.0/Sample.dll");

        Assert.IsType<PackageAssetSelection.NoMatch>(selection);
    }

    [Fact]
    public void Select_IncludesEveryAssemblyInTheSelectedFolder()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/net10.0/Zebra.dll",
            "lib/net10.0/Alpha.dll",
            "lib/net10.0/Middle.dll",
            "lib/net8.0/Ignored.dll");

        Assert.Equal(
            ["lib/net10.0/Alpha.dll", "lib/net10.0/Middle.dll", "lib/net10.0/Zebra.dll"],
            Selected(selection).Assets.Select(asset => asset.EntryPath));
    }

    [Fact]
    public void Select_ExcludesSatelliteResourceAssemblies()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/net10.0/Sample.dll",
            "lib/net10.0/de/Sample.resources.dll",
            "lib/net10.0/zh-Hans/Sample.resources.dll");

        Assert.Equal(
            "lib/net10.0/Sample.dll",
            Assert.Single(Selected(selection).Assets).EntryPath);
    }

    [Fact]
    public void Select_KeepsAResourceNamedAssemblyWithNoPrimary()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/net10.0/Sample.dll",
            "lib/net10.0/de/Orphan.resources.dll");

        Assert.Equal(
            ["lib/net10.0/Sample.dll", "lib/net10.0/de/Orphan.resources.dll"],
            Selected(selection).Assets.Select(asset => asset.EntryPath));
    }

    [Fact]
    public void Select_ExcludesNonAssemblyAndNativeAssets()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            "linux-x64",
            "lib/net10.0/Sample.dll",
            "lib/net10.0/Sample.xml",
            "runtimes/linux-x64/native/libsample.so",
            "runtimes/linux-x64/native/sample.dll",
            "build/Sample.props",
            "Sample.nuspec");

        Assert.Equal(
            "lib/net10.0/Sample.dll",
            Assert.Single(Selected(selection).Assets).EntryPath);
    }

    [Fact]
    public void Select_PrefersTheRuntimeSpecificAssetForTheRequestedRid()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            "linux-x64",
            "lib/net10.0/Sample.dll",
            "lib/net10.0/Companion.dll",
            "runtimes/linux-x64/lib/net10.0/Sample.dll",
            "runtimes/win-x64/lib/net10.0/Sample.dll");

        PackageAssetUniverse universe = Selected(selection);
        Assert.Equal("linux-x64", universe.RuntimeIdentifier);
        Assert.Equal(
            ["lib/net10.0/Companion.dll", "runtimes/linux-x64/lib/net10.0/Sample.dll"],
            universe.Assets.Select(asset => asset.EntryPath));
        Assert.Equal(
            "linux-x64",
            universe.Assets
                .Single(asset => asset.FileName == "Sample.dll")
                .RuntimeIdentifier);
    }

    [Fact]
    public void Select_WithoutARid_UsesOnlyRuntimeNeutralAssets()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/net10.0/Sample.dll",
            "runtimes/linux-x64/lib/net10.0/Sample.dll");

        PackageAssetEntry asset = Assert.Single(Selected(selection).Assets);
        Assert.Equal("lib/net10.0/Sample.dll", asset.EntryPath);
        Assert.Null(asset.RuntimeIdentifier);
    }

    [Fact]
    public void Select_IgnoresRuntimeAssetsForAnotherRid()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            "linux-x64",
            "runtimes/win-x64/lib/net10.0/Sample.dll");

        Assert.IsType<PackageAssetSelection.NoMatch>(selection);
    }

    [Fact]
    public void Select_ReportsEquallyApplicableFoldersAsAmbiguous()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/net10.0/Sample.dll",
            "lib/net10.0-windows/Sample.dll");

        Assert.IsType<PackageAssetSelection.Ambiguous>(selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_ReportsCaseCollidingNeutralAssetsAsAmbiguous(
        bool reverse)
    {
        string[] entries =
        [
            "lib/net10.0/Sample.dll",
            "lib/NET10.0/sample.dll",
        ];
        if (reverse)
            Array.Reverse(entries);

        Assert.IsType<PackageAssetSelection.Ambiguous>(
            Select("net10.0", null, entries));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_ReportsCaseCollidingRuntimeAssetsAsAmbiguous(
        bool reverse)
    {
        string[] entries =
        [
            "runtimes/linux-x64/lib/net10.0/Sample.dll",
            "runtimes/LINUX-X64/lib/NET10.0/sample.dll",
        ];
        if (reverse)
            Array.Reverse(entries);

        Assert.IsType<PackageAssetSelection.Ambiguous>(
            Select("net10.0", "linux-x64", entries));
    }

    [Fact]
    public void Select_ReportsNoMatchForANonLibraryLayout()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "tools/net10.0/any/any/Sample.dll",
            "ref/net10.0/Sample.dll");

        Assert.IsType<PackageAssetSelection.NoMatch>(selection);
    }

    [Theory]
    [InlineData("lib/net10.0/../Escape.dll")]
    [InlineData("lib/net10.0/./Sample.dll")]
    [InlineData("lib/net10.0/sub\\Sample.dll")]
    public void Select_RejectsAnUnsafeCandidateEntryPath(string entryPath)
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/net10.0/Sample.dll",
            entryPath);

        var invalid =
            Assert.IsType<PackageAssetSelection.Invalid>(selection);
        Assert.DoesNotContain(entryPath, invalid.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" net10.0")]
    public void Select_RejectsAnUnusableTargetFramework(string framework)
    {
        PackageAssetSelection selection = Select(
            framework,
            null,
            "lib/net10.0/Sample.dll");

        Assert.IsType<PackageAssetSelection.Invalid>(selection);
    }

    [Fact]
    public void Select_RejectsAnUnusableRuntimeIdentifier()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            "linux-x64 ",
            "lib/net10.0/Sample.dll");

        Assert.IsType<PackageAssetSelection.Invalid>(selection);
    }

    [Fact]
    public void Select_OrdersAssetsIndependentlyOfArchiveOrder()
    {
        PackageAssetSelection forward = Select(
            "net10.0",
            null,
            "lib/net10.0/Alpha.dll",
            "lib/net10.0/Zebra.dll");
        PackageAssetSelection reversed = Select(
            "net10.0",
            null,
            "lib/net10.0/Zebra.dll",
            "lib/net10.0/Alpha.dll");

        Assert.Equal(
            Selected(forward).Assets.Select(asset => asset.EntryPath),
            Selected(reversed).Assets.Select(asset => asset.EntryPath));
    }

    static PackageAssetUniverse Selected(PackageAssetSelection selection)
        => Assert.IsType<PackageAssetSelection.Selected>(selection).Universe;

    static PackageAssetSelection Select(
        string targetFramework,
        string? runtimeIdentifier,
        params string[] entryPaths)
        => PackageAssetSelector.Select(
            new InMemoryPackageContent(
                TestPackageArchive.Create(entryPaths),
                fromCache: true,
                "test-source"),
            targetFramework,
            runtimeIdentifier);
}
