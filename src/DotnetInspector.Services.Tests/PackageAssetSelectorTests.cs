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

    /// <summary>
    /// .NET Standard is the contract .NET Framework implements, so a
    /// netstandard2.0 asset is the right answer for a net472 or net481 target.
    /// Ordering the two by a single cross-family priority number rejects it,
    /// because netstandard2.0 scores "newer" than every .NET Framework version.
    /// </summary>
    [Theory]
    [InlineData("net472")]
    [InlineData("net481")]
    [InlineData("net461")]
    public void Select_NetFrameworkTargetAcceptsASupportedNetStandardAsset(
        string targetFramework)
    {
        PackageAssetSelection selection = Select(
            targetFramework,
            null,
            "lib/netstandard2.0/Sample.dll");

        Assert.Equal("netstandard2.0", Selected(selection).TargetFramework);
    }

    [Fact]
    public void Select_NetFrameworkTargetRejectsAnUnsupportedNetStandardAsset()
    {
        // net46 implements netstandard1.3, not 2.0.
        PackageAssetSelection selection = Select(
            "net46",
            null,
            "lib/netstandard2.0/Sample.dll");

        Assert.IsType<PackageAssetSelection.NoMatch>(selection);
    }

    [Fact]
    public void Select_NetCoreApp1TargetRejectsANetStandard21Asset()
    {
        // netstandard2.1 arrived with netcoreapp3.0. A lower priority number
        // than netcoreapp1.0's does not make it consumable there.
        PackageAssetSelection selection = Select(
            "netcoreapp1.0",
            null,
            "lib/netstandard2.1/Sample.dll");

        Assert.IsType<PackageAssetSelection.NoMatch>(selection);
    }

    [Fact]
    public void Select_PrefersTheTargetsOwnLineageOverNetStandard()
    {
        PackageAssetSelection selection = Select(
            "net472",
            null,
            "lib/netstandard2.0/Sample.dll",
            "lib/net472/Sample.dll");

        Assert.Equal("net472", Selected(selection).TargetFramework);
    }

    [Fact]
    public void Select_NetCoreAppTargetTakesItsHighestSupportedNetStandard()
    {
        PackageAssetSelection selection = Select(
            "netcoreapp2.0",
            null,
            "lib/netstandard1.6/Sample.dll",
            "lib/netstandard2.0/Sample.dll",
            "lib/netstandard2.1/Sample.dll");

        Assert.Equal("netstandard2.0", Selected(selection).TargetFramework);
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
    public void Select_NeutralTargetRejectsAPlatformSpecificFolder()
    {
        // A neutral target never asked for a platform, so a platform-specific
        // universe is not a fallback for it — it is a different target.
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/net10.0-windows/Sample.dll");

        Assert.IsType<PackageAssetSelection.NoMatch>(selection);
    }

    [Fact]
    public void Select_NeutralTargetPrefersTheNeutralFolder()
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/net10.0/Sample.dll",
            "lib/net10.0-windows/Sample.dll");

        PackageAssetUniverse universe = Selected(selection);
        Assert.Equal("net10.0", universe.TargetFramework);
        Assert.Equal(
            "lib/net10.0/Sample.dll",
            Assert.Single(universe.Assets).EntryPath);
    }

    [Fact]
    public void Select_PlatformTargetPrefersTheExactPlatformFolder()
    {
        PackageAssetSelection selection = Select(
            "net10.0-windows",
            null,
            "lib/net10.0/Sample.dll",
            "lib/net10.0-windows/Sample.dll");

        PackageAssetUniverse universe = Selected(selection);
        Assert.Equal("net10.0-windows", universe.TargetFramework);
        Assert.Equal(
            "lib/net10.0-windows/Sample.dll",
            Assert.Single(universe.Assets).EntryPath);
    }

    [Fact]
    public void Select_PlatformTargetFallsBackToTheNeutralFolder()
    {
        PackageAssetSelection selection = Select(
            "net10.0-windows",
            null,
            "lib/net10.0/Sample.dll");

        Assert.Equal("net10.0", Selected(selection).TargetFramework);
    }

    [Fact]
    public void Select_PlatformTargetRejectsAnotherPlatform()
    {
        PackageAssetSelection selection = Select(
            "net10.0-ios",
            null,
            "lib/net10.0-windows/Sample.dll");

        Assert.IsType<PackageAssetSelection.NoMatch>(selection);
    }

    [Fact]
    public void Select_PlatformTargetSelectsTheExactVersionedPlatformFolder()
    {
        PackageAssetSelection selection = Select(
            "net8.0-windows10.0.19041",
            null,
            "lib/netstandard2.0/Sample.dll",
            "lib/net8.0/Sample.dll",
            "lib/net8.0-windows10.0.19041/Sample.dll");

        PackageAssetUniverse universe = Selected(selection);
        Assert.Equal("net8.0-windows10.0.19041", universe.TargetFramework);
        Assert.Equal(
            "lib/net8.0-windows10.0.19041/Sample.dll",
            Assert.Single(universe.Assets).EntryPath);
    }

    [Fact]
    public void Select_PlatformTargetRejectsADifferentPlatformVersionSpelling()
    {
        // Conservative by design: the product owns no platform-version
        // reduction table, so a differently spelled platform version is a
        // different platform and the neutral fallback is what remains.
        PackageAssetSelection selection = Select(
            "net8.0-windows10.0.19041",
            null,
            "lib/net8.0/Sample.dll",
            "lib/net8.0-windows/Sample.dll");

        PackageAssetUniverse universe = Selected(selection);
        Assert.Equal("net8.0", universe.TargetFramework);
        Assert.Equal(
            "lib/net8.0/Sample.dll",
            Assert.Single(universe.Assets).EntryPath);
    }

    [Fact]
    public void Select_PlatformTargetWithoutAnyApplicableFolderIsNoMatch()
    {
        PackageAssetSelection selection = Select(
            "net8.0-windows10.0.19041",
            null,
            "lib/net8.0-windows/Sample.dll",
            "lib/net10.0/Sample.dll");

        Assert.IsType<PackageAssetSelection.NoMatch>(selection);
    }

    [Fact]
    public void Select_PrefersAHigherBaseFrameworkOverAPlatformMatch()
    {
        // Base framework decides first, so the fallback order stays
        // deterministic when platform specificity and framework level disagree.
        PackageAssetSelection selection = Select(
            "net10.0-windows",
            null,
            "lib/net8.0-windows/Sample.dll",
            "lib/net10.0/Sample.dll");

        Assert.Equal("net10.0", Selected(selection).TargetFramework);
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
            "runtimes/linux-x64/lib/NET10.0/sample.dll",
        ];
        if (reverse)
            Array.Reverse(entries);

        Assert.IsType<PackageAssetSelection.Ambiguous>(
            Select("net10.0", "linux-x64", entries));
    }

    [Fact]
    public void Select_IgnoresARuntimeFolderMatchingTheRidOnlyByCase()
    {
        // Runtime identifiers are canonically lowercase, so a differently
        // cased folder is another name rather than another spelling of the
        // requested one. Folding them would let a package direct a request at
        // a folder its own manifest never named.
        PackageAssetSelection selection = Select(
            "net10.0",
            "linux-x64",
            "runtimes/LINUX-X64/lib/net10.0/Sample.dll");

        Assert.IsType<PackageAssetSelection.NoMatch>(selection);
    }

    [Fact]
    public void Select_AmbiguityNamesOnlyTheRequestedFramework()
    {
        // The selected folder is archive-controlled text. The message names
        // the framework the caller asked for and nothing read out of the
        // package.
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/NETSTANDARD2.0/Sample.dll",
            "lib/netstandard2.0/sample.dll");

        var ambiguous =
            Assert.IsType<PackageAssetSelection.Ambiguous>(selection);
        Assert.Contains("net10.0", ambiguous.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "netstandard",
            ambiguous.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("lib/net10.0/Sam\u0007ple.dll")]
    [InlineData("lib/net\u000110.0/Sample.dll")]
    public void Select_RejectsAControlBearingCandidateEntryPath(
        string entryPath)
    {
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/net10.0/Sample.dll",
            entryPath);

        var invalid =
            Assert.IsType<PackageAssetSelection.Invalid>(selection);
        Assert.DoesNotContain(entryPath, invalid.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\u0007', invalid.Message);
        Assert.DoesNotContain('\u0001', invalid.Message);
    }

    [Fact]
    public void Select_KeepsABidiBearingEntryOutOfTheFailureMessage()
    {
        // A bidi override is not a control character, so it is not rejected on
        // that ground. What protects the message is that no scalar read out of
        // the archive is ever quoted into one: this layout does produce a
        // failure, and the failure names none of it.
        PackageAssetSelection selection = Select(
            "net10.0",
            null,
            "lib/net10.0/\u202eSample.dll",
            "lib/NET10.0/\u202esample.dll");

        var ambiguous =
            Assert.IsType<PackageAssetSelection.Ambiguous>(selection);
        Assert.DoesNotContain('\u202e', ambiguous.Message);
        Assert.DoesNotContain(
            "Sample",
            ambiguous.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("net10.0", ambiguous.Message, StringComparison.Ordinal);
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
