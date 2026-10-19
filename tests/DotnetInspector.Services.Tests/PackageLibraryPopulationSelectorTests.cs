using DotnetInspector.Packages;

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
    }

    [Fact]
    public void MissingCompilePopulationSelectsOneRuntimePopulation()
    {
        var content = Content(
            "runtimes/linux-x64/lib/net8.0/Older.dll",
            "runtimes/linux-x64/lib/net10.0/Current.dll");

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
            "runtimes/linux-x64/lib/net10.0/Current.dll",
            Assert.Single(runtime.Universe.Assets).EntryPath);
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
}
