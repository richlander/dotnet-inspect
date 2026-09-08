using System.IO.Compression;
using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

public class PackageCompileAssetSelectorTests : IDisposable
{
    readonly string _root =
        Path.Combine(Path.GetTempPath(), $"package-assets-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void InMemorySelection_PrefersReferenceAssetsAndPackageNamedDefault()
    {
        IPackageContent content = InMemory(
            "lib/net8.0/Example.dll",
            "ref/net8.0/Example.Companion.dll",
            "ref/net8.0/Example.dll",
            "ref/net8.0/fr/Example.resources.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example", "net8.0");

        Assert.True(selection.IsSelected);
        Assert.Equal(PackageCompileAssetSelectionStatus.Selected, selection.Status);
        Assert.Equal("net8.0", selection.TargetFramework);
        Assert.Equal(
            ["ref/net8.0/Example.Companion.dll", "ref/net8.0/Example.dll"],
            selection.Assets.Select(asset => asset.Path));
        Assert.Equal("ref/net8.0/Example.dll", selection.DefaultAsset!.Path);
        Assert.Equal(
            PackageCompileAssetKind.Reference,
            selection.DefaultAsset.Kind);
        Assert.Same(
            selection.DefaultAsset,
            selection.FindAsset(selection.DefaultAsset.Id));
        Assert.Null(selection.FindAsset(selection.DefaultAsset.Id.ToUpperInvariant()));
        Assert.Equal(
            ["lib/net8.0/Example.dll"],
            selection.ImplementationAssets.Select(asset => asset.Path));
        Assert.Equal(
            "lib/net8.0/Example.dll",
            selection.FindImplementationAsset(selection.DefaultAsset!)!.Path);
        Assert.Null(selection.FindImplementationAsset(selection.Assets[0]));
    }

    [Fact]
    public void InMemorySelection_FallsBackToLibraryAssetsAtHighestTfm()
    {
        IPackageContent content = InMemory(
            "lib/netstandard2.0/Example.dll",
            "lib/net10.0/Example.dll",
            "lib/net10.0/Example.Companion.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example");

        Assert.True(selection.IsSelected);
        Assert.Equal(["net10.0", "netstandard2.0"], selection.AvailableTargetFrameworks);
        Assert.Equal("net10.0", selection.TargetFramework);
        Assert.Equal(
            ["lib/net10.0/Example.Companion.dll", "lib/net10.0/Example.dll"],
            selection.Assets.Select(asset => asset.Path));
        Assert.Same(
            selection.DefaultAsset,
            selection.FindImplementationAsset(selection.DefaultAsset!));
    }

    [Fact]
    public void RidSpecificImplementation_DoesNotReplaceLibraryCompileFallback()
    {
        IPackageContent content = InMemory(
            "lib/net8.0/Example.dll",
            "lib/net8.0/Example.Companion.dll",
            "lib/net8.0/shadow/Example.dll",
            "runtimes/linux-x64/lib/net8.0/Example.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(
                content,
                "Example",
                "net8.0",
                "linux-x64");

        Assert.True(selection.IsSelected);
        Assert.Equal(
            ["lib/net8.0/Example.Companion.dll", "lib/net8.0/Example.dll"],
            selection.Assets.Select(asset => asset.Path));
        Assert.Equal(
            [
                "lib/net8.0/Example.Companion.dll",
                "lib/net8.0/shadow/Example.dll",
                "runtimes/linux-x64/lib/net8.0/Example.dll",
            ],
            selection.ImplementationAssets.Select(asset => asset.Path));
        Assert.Equal("lib/net8.0/Example.dll", selection.DefaultAsset!.Path);
        Assert.Equal(
            "runtimes/linux-x64/lib/net8.0/Example.dll",
            selection.FindImplementationAsset(selection.DefaultAsset)!.Path);
        PackageCompileAsset companion = selection.Assets[0];
        Assert.Same(
            companion,
            selection.FindImplementationAsset(companion));
    }

    [Fact]
    public void ReferenceSelection_UsesSharedImplementationFrameworkReduction()
    {
        IPackageContent content = InMemory(
            "ref/net8.0/Example.dll",
            "lib/net6.0/Example.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(
                content,
                "Example",
                "net8.0");

        Assert.Equal(
            ["ref/net8.0/Example.dll"],
            selection.Assets.Select(asset => asset.Path));
        PackageCompileAsset implementation =
            Assert.Single(selection.ImplementationAssets);
        Assert.Equal("lib/net6.0/Example.dll", implementation.Path);
        Assert.Equal("net6.0", implementation.TargetFramework);
        Assert.Same(
            implementation,
            selection.FindImplementationAsset(selection.DefaultAsset!));
    }

    [Fact]
    public void Selection_RejectsEmptyAssemblyStem()
    {
        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(
                InMemory("lib/net8.0/.dll"),
                "Example",
                "net8.0");

        Assert.False(selection.IsSelected);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            selection.Status);
    }

    [Fact]
    public void Selection_RanksTargetFrameworksCaseInsensitively()
    {
        IPackageContent content = InMemory(
            "lib/NET8.0/Example.dll",
            "lib/netstandard2.0/Example.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example");

        Assert.True(selection.IsSelected);
        Assert.Equal("NET8.0", selection.TargetFramework);
        Assert.Equal("lib/NET8.0/Example.dll", selection.DefaultAsset!.Path);
    }

    [Fact]
    public void Selection_KeepsRootAssemblyEndingInResources()
    {
        IPackageContent content = InMemory(
            "lib/net8.0/Example.dll",
            "lib/net8.0/Example.Resources.dll",
            "lib/net8.0/fr/Example.resources.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example", "net8.0");

        Assert.Equal(
            ["lib/net8.0/Example.dll", "lib/net8.0/Example.Resources.dll"],
            selection.Assets.Select(asset => asset.Path));
    }

    [Fact]
    public void Selection_CaseCollidingImplementationPathsAreRejected()
    {
        string[] entries =
        [
            "lib/net8.0/Example.dll",
            "LIB/NET8.0/example.dll",
        ];

        PackageCompileAssetSelection first =
            PackageCompileAssetSelector.Select(
                InMemory(entries),
                "Example");
        PackageCompileAssetSelection reversed =
            PackageCompileAssetSelector.Select(
                InMemory(entries.Reverse().ToArray()),
                "Example");

        Assert.Equal(
            PackageCompileAssetSelectionStatus.InvalidImplementationAssets,
            first.Status);
        Assert.Equal(first.Status, reversed.Status);
        Assert.Equal(first.Message, reversed.Message);
    }

    [Fact]
    public void Selection_MissingFrameworkPreservesTypedCandidates()
    {
        IPackageContent content = InMemory("lib/net8.0/Example.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example", "net6.0");

        Assert.False(selection.IsSelected);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework,
            selection.Status);
        Assert.Equal(["net8.0"], selection.AvailableTargetFrameworks);
        Assert.Equal(
            ["lib/net8.0/Example.dll"],
            selection.CandidateAssets.Select(asset => asset.Path));
    }

    [Fact]
    public void Selection_IgnoresNonCompileAndTraversalShapedEntries()
    {
        IPackageContent content = InMemory(
            "../escape.dll",
            "lib\\net8.0\\Backslash.dll",
            "/lib/net8.0/Leading.dll",
            "runtimes/linux-x64/lib/net8.0/Runtime.dll",
            "lib/../Escape.dll",
            "lib/net8.0//RepeatedSeparator.dll",
            "lib/net8.0/Trailing.dll/",
            "lib/uap10.0/Legacy.dll",
            "lib/portable-net45+win8/Portable.dll",
            "lib/net8.0/fr/Example.resources.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example");

        Assert.Equal(PackageCompileAssetSelectionStatus.NoCompileAssets, selection.Status);
        Assert.Empty(selection.CandidateAssets);
    }

    [Fact]
    public void FileSystemAndInMemoryContentProduceTheSameSelection()
    {
        string[] entries =
        [
            "lib/net8.0/Example.dll",
            "ref/net8.0/Example.dll",
            "ref/net8.0/Example.Companion.dll",
        ];
        foreach (string entry in entries)
        {
            string path = Path.Combine(_root, entry.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1, 2, 3]);
        }

        var fileSystem = new FileSystemPackageContent(
            _root,
            nupkgPath: null,
            fromCache: false,
            producerKey: "test");
        PackageCompileAssetSelection expected =
            PackageCompileAssetSelector.Select(InMemory(entries), "Example", "net8.0");
        PackageCompileAssetSelection actual =
            PackageCompileAssetSelector.Select(fileSystem, "Example", "net8.0");

        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.TargetFramework, actual.TargetFramework);
        Assert.Equal(
            expected.AvailableTargetFrameworks,
            actual.AvailableTargetFrameworks);
        Assert.Equal(expected.Assets, actual.Assets);
        Assert.Equal(expected.DefaultAsset, actual.DefaultAsset);
        Assert.Equal(expected.CandidateAssets, actual.CandidateAssets);
    }

    // NuGet reads `ref/<tfm>/_._` as an explicit statement that the package contributes no
    // compile-time assembly for that framework. Falling back to lib/ there would compile against
    // assets the package deliberately withheld.
    [Fact]
    public void EmptyReferenceGroup_AtTheSelectedFramework_SuppressesLibraryFallback()
    {
        IPackageContent content = InMemory(
            "ref/net8.0/_._",
            "lib/net8.0/Example.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example", "net8.0");

        Assert.False(selection.IsSelected);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            selection.Status);
        Assert.Equal("net8.0", selection.TargetFramework);
        Assert.Empty(selection.Assets);
        Assert.Null(selection.DefaultAsset);
        Assert.Equal(
            ["lib/net8.0/Example.dll"],
            selection.CandidateAssets.Select(asset => asset.Path));
    }

    [Fact]
    public void EmptyReferenceGroup_AtRequestedFramework_SuppressesCompatibleLibraryFallback()
    {
        IPackageContent content = InMemory(
            "ref/net10.0/_._",
            "lib/net8.0/Example.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example", "net10.0");

        Assert.False(selection.IsSelected);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            selection.Status);
        Assert.Equal("net10.0", selection.TargetFramework);
        Assert.Empty(selection.Assets);
        Assert.Null(selection.DefaultAsset);
        Assert.Equal(
            ["lib/net8.0/Example.dll"],
            selection.CandidateAssets.Select(asset => asset.Path));
    }

    [Fact]
    public void EmptyReferenceGroup_NearestCompatibleGroupSuppressesLibraryFallback()
    {
        IPackageContent content = InMemory(
            "ref/netstandard2.0/_._",
            "lib/net8.0/Example.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example", "net8.0");

        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            selection.Status);
        Assert.Equal("net8.0", selection.TargetFramework);
    }

    [Fact]
    public void EmptyReferenceGroup_NewerThanTheSelectedFramework_PreservesLibraryFallback()
    {
        IPackageContent content = InMemory(
            "ref/net10.0/_._",
            "lib/net8.0/Example.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example", "net8.0");

        Assert.True(selection.IsSelected);
        Assert.Equal("net8.0", selection.TargetFramework);
        Assert.Equal(
            ["lib/net8.0/Example.dll"],
            selection.Assets.Select(asset => asset.Path));
    }

    [Fact]
    public void EmptyReferenceGroup_IncompatibleFamily_PreservesLibraryFallback()
    {
        IPackageContent content = InMemory(
            "ref/net472/_._",
            "lib/net8.0/Example.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example", "net8.0");

        Assert.True(selection.IsSelected);
        Assert.Equal(
            ["lib/net8.0/Example.dll"],
            selection.Assets.Select(asset => asset.Path));
    }

    [Fact]
    public void EmptyReferenceGroup_LosesToRealReferenceAssetsAtTheSelectedFramework()
    {
        IPackageContent content = InMemory(
            "ref/netstandard2.0/_._",
            "ref/net8.0/Example.dll",
            "lib/net8.0/Example.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example", "net8.0");

        Assert.True(selection.IsSelected);
        Assert.Equal(
            ["ref/net8.0/Example.dll"],
            selection.Assets.Select(asset => asset.Path));
    }

    [Theory]
    // A library empty group says nothing about compile assets.
    [InlineData("lib/net8.0/_._")]
    // Only the exact marker name is the marker.
    [InlineData("ref/net8.0/__._")]
    [InlineData("ref/net8.0/_")]
    // Not a TFM-shaped group, and not a three-segment entry.
    [InlineData("ref/any/_._")]
    [InlineData("ref/net8.0/sub/_._")]
    [InlineData("ref\\net8.0\\_._")]
    public void NonMarkerEntries_PreserveLibraryFallback(string entry)
    {
        IPackageContent content = InMemory(entry, "lib/net8.0/Example.dll");

        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, "Example", "net8.0");

        Assert.True(selection.IsSelected);
        Assert.Equal(
            ["lib/net8.0/Example.dll"],
            selection.Assets.Select(asset => asset.Path));
    }

    // "_._.dll" is an ordinary reference assembly whose stem happens to be the marker text, not
    // an empty group: it is selected, and the group is not empty.
    [Fact]
    public void MarkerNamedAssembly_IsAnOrdinaryReferenceAsset()
    {
        PackageCompileAssetSelection selection = PackageCompileAssetSelector.Select(
            InMemory("ref/net8.0/_._.dll", "lib/net8.0/Example.dll"),
            "Example",
            "net8.0");

        Assert.True(selection.IsSelected);
        Assert.Equal(
            ["ref/net8.0/_._.dll"],
            selection.Assets.Select(asset => asset.Path));
    }

    [Fact]
    public void EmptyReferenceGroup_AloneStillReportsNoCompileAssets()
    {
        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(InMemory("ref/net8.0/_._"), "Example");

        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            selection.Status);
    }

    static IPackageContent InMemory(params string[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string path in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using Stream stream = entry.Open();
                stream.Write([1, 2, 3]);
            }
        }

        return new InMemoryPackageContent(
            buffer.ToArray(),
            fromCache: false,
            producerKey: "test");
    }
}
