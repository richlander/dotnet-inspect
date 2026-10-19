using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed class PackageLibraryPopulationSelectorTests
{
    [Fact]
    public void CompilePopulationRetainsPrecedenceOverRuntimeAssets()
    {
        var content = Content(
            "lib/net10.0/Neutral.dll",
            "runtimes/linux-x64/lib/net10.0/Runtime.dll",
            "runtimes/win-x64/lib/net10.0/Runtime.dll");

        var selected =
            Assert.IsType<PackageLibraryPopulationSelection.Compile>(
                PackageLibraryPopulationSelector.Evaluate(
                    content,
                    "Example",
                    PackageCompileAssetSelectionPolicy.HighestAvailable));

        Assert.Same(
            content.GenerationIdentity,
            selected.CompileReceipt.Generation);
        Assert.Equal(
            "lib/net10.0/Neutral.dll",
            Assert.Single(selected.CompileReceipt.Selection.Assets).Path);
        Assert.Equal("lib/net10.0/", selected.PopulationPath);
    }

    [Fact]
    public void CompilePopulationRetainsRootForSoleNestedAsset()
    {
        var selected =
            Assert.IsType<PackageLibraryPopulationSelection.Compile>(
                PackageLibraryPopulationSelector.Evaluate(
                    Content("lib/net10.0/nested/Neutral.dll"),
                    "Example",
                    PackageCompileAssetSelectionPolicy.HighestAvailable));

        Assert.Equal("lib/net10.0/", selected.PopulationPath);
    }

    [Fact]
    public void MissingCompilePopulationSelectsOneRuntimePopulation()
    {
        var content = Content(
            "runtimes/linux-x64/lib/net8.0/Older.dll",
            "runtimes/linux-x64/lib/NET10.0/Current.dll");

        var selected =
            Assert.IsType<PackageLibraryPopulationSelection.Runtime>(
                PackageLibraryPopulationSelector.Evaluate(
                    content,
                    "Example",
                    PackageCompileAssetSelectionPolicy.HighestAvailable));

        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            selected.CompileReceipt.Selection.Status);
        var runtime = Assert.IsType<PackageAssetSelection.Selected>(
            selected.RuntimeReceipt.Selection);
        Assert.Equal("net10.0", runtime.Universe.TargetFramework);
        Assert.Equal("linux-x64", runtime.Universe.RuntimeIdentifier);
        Assert.Equal(
            "runtimes/linux-x64/lib/NET10.0/Current.dll",
            Assert.Single(runtime.Universe.Assets).EntryPath);
        Assert.Equal(
            "runtimes/linux-x64/lib/NET10.0/",
            selected.PopulationPath);
    }

    [Fact]
    public void EmptyCompilePopulationSelectsCompatibleRuntimePopulation()
    {
        var content = Content(
            "ref/netstandard2.0/_._",
            "runtimes/unix/lib/netstandard1.0/Example.dll",
            "runtimes/unix/lib/netstandard1.3/Example.dll");

        var selected =
            Assert.IsType<PackageLibraryPopulationSelection.Runtime>(
                PackageLibraryPopulationSelector.Evaluate(
                    content,
                    "Example",
                    PackageCompileAssetSelectionPolicy.ExplicitTarget,
                    "netstandard2.0"));

        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            selected.CompileReceipt.Selection.Status);
        var runtime = Assert.IsType<PackageAssetSelection.Selected>(
            selected.RuntimeReceipt.Selection);
        Assert.Equal("netstandard1.3", runtime.Universe.TargetFramework);
        Assert.Equal("unix", runtime.Universe.RuntimeIdentifier);
    }

    [Fact]
    public void MultipleRuntimeIdentifiersRemainTypedNonSuccess()
    {
        var selected =
            Assert.IsType<PackageLibraryPopulationSelection.NonSuccess>(
                PackageLibraryPopulationSelector.Evaluate(
                    Content(
                        "runtimes/linux-x64/lib/net10.0/Example.dll",
                        "runtimes/win-x64/lib/net10.0/Example.dll"),
                    "Example",
                    PackageCompileAssetSelectionPolicy.HighestAvailable));

        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            selected.CompileReceipt.Selection.Status);
        Assert.IsType<PackageAssetSelection.Ambiguous>(
            selected.RuntimeReceipt!.Selection);
    }

    [Fact]
    public void UnrelatedDllRolesDoNotFormALibraryPopulation()
    {
        var selected =
            Assert.IsType<PackageLibraryPopulationSelection.NonSuccess>(
                PackageLibraryPopulationSelector.Evaluate(
                    Content(
                        "analyzers/dotnet/cs/Analyzer.dll",
                        "build/net10.0/BuildTask.dll",
                        "runtimes/linux-x64/native/Native.dll"),
                    "Example",
                    PackageCompileAssetSelectionPolicy.HighestAvailable));

        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            selected.CompileReceipt.Selection.Status);
        Assert.IsType<PackageAssetSelection.NoMatch>(
            selected.RuntimeReceipt!.Selection);
    }

    [Fact]
    public void DeclaredToolPackageSelectsOneToolLibraryPopulation()
    {
        var content = ContentWithBytes(
            ("example.nuspec", ToolNuspec("Example", "1.0.0")),
            ("tools/net8.0/any/Older.dll", [1]),
            ("tools/NET10.0/any/Tool.dll", [2]),
            ("tools/NET10.0/any/runtimes/win/lib/net10.0/Companion.dll", [3]),
            ("tools/NET10.0/any/runtimes/win/native/Native.dll", [4]),
            ("tools/NET10.0/any/fr/Tool.resources.dll", [5]));

        var selected =
            Assert.IsType<PackageLibraryPopulationSelection.Tool>(
                PackageLibraryPopulationSelector.Evaluate(
                    content,
                    "Example",
                    PackageCompileAssetSelectionPolicy.HighestAvailable,
                    coordinate:
                        PackageSourceCoordinate.Create(
                            "Example",
                            "1.0.0")));

        Assert.IsType<PackageAssetSelection.NoMatch>(
            selected.RuntimeReceipt.Selection);
        Assert.Same(
            content.GenerationIdentity,
            selected.ToolEvidence.Generation);
        Assert.Equal(
            "NET10.0",
            selected.ToolSelection.SelectedTargetFramework);
        Assert.Equal(
            [
                "tools/NET10.0/any/Tool.dll",
                "tools/NET10.0/any/runtimes/win/lib/net10.0/Companion.dll",
            ],
            selected.ToolSelection.SelectedEntries);
        Assert.Equal("tools/NET10.0/any/", selected.PopulationPath);
    }

    [Fact]
    public void ToolShapeWithoutDeclarationDoesNotFormALibraryPopulation()
    {
        var selected =
            Assert.IsType<PackageLibraryPopulationSelection.NonSuccess>(
                PackageLibraryPopulationSelector.Evaluate(
                    Content(
                        "tools/net10.0/any/Tool.dll"),
                    "Example",
                    PackageCompileAssetSelectionPolicy.HighestAvailable,
                    coordinate:
                        PackageSourceCoordinate.Create(
                            "Example",
                            "1.0.0")));

        Assert.IsType<PackageAssetSelection.NoMatch>(
            selected.RuntimeReceipt!.Selection);
        Assert.Null(selected.ToolEvidence);
        Assert.Null(selected.ToolSelection);
        Assert.False(selected.ToolDeclarationUnavailable);
    }

    [Fact]
    public void CompilePopulationPrecedesDeclaredToolPopulation()
    {
        var selected =
            Assert.IsType<PackageLibraryPopulationSelection.Compile>(
                PackageLibraryPopulationSelector.Evaluate(
                    ContentWithBytes(
                        ("example.nuspec", ToolNuspec("Example", "1.0.0")),
                        ("lib/net10.0/Compile.dll", [1]),
                        ("tools/net10.0/any/Tool.dll", [2])),
                    "Example",
                    PackageCompileAssetSelectionPolicy.HighestAvailable,
                    coordinate:
                        PackageSourceCoordinate.Create(
                            "Example",
                            "1.0.0")));

        Assert.Equal(
            "lib/net10.0/Compile.dll",
            Assert.Single(
                selected.CompileReceipt.Selection.Assets).Path);
    }

    [Fact]
    public void ExistingCompileReceiptRequiresTheSameContentGeneration()
    {
        var content = Content(
            "runtimes/linux-x64/lib/net10.0/Example.dll");
        PackageCompileAssetSelectionReceipt receipt =
            PackageCompileAssetSelector.Evaluate(content, "Example");

        Assert.IsType<PackageLibraryPopulationSelection.Runtime>(
            PackageLibraryPopulationSelector.Evaluate(content, receipt));
        Assert.Throws<ArgumentException>(() =>
            PackageLibraryPopulationSelector.Evaluate(
                Content("runtimes/linux-x64/lib/net10.0/Example.dll"),
                receipt));
    }

    private static InMemoryPackageContent Content(params string[] entries) =>
        new(
            TestPackageArchive.Create(entries),
            fromCache: true,
            "test-source");

    private static InMemoryPackageContent ContentWithBytes(
        params (string Path, byte[] Content)[] entries) =>
        new(
            TestPackageArchive.CreateWithContent(entries),
            fromCache: true,
            "test-source");

    private static byte[] ToolNuspec(
        string packageId,
        string version) =>
        System.Text.Encoding.UTF8.GetBytes(
            $"""
            <?xml version="1.0"?>
            <package>
              <metadata>
                <id>{packageId}</id>
                <version>{version}</version>
                <packageTypes>
                  <packageType name="DotnetToolRidPackage" />
                </packageTypes>
              </metadata>
            </package>
            """);
}
