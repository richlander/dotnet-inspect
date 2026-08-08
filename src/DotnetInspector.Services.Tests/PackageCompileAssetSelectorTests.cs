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
    public void Selection_CaseCollidingPathsAreIndependentOfEnumerationOrder()
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

        Assert.Equal(first.Assets, reversed.Assets);
        Assert.Equal(first.DefaultAsset, reversed.DefaultAsset);
        Assert.Equal("LIB/NET8.0/example.dll", first.DefaultAsset!.Path);
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
            "runtimes/linux-x64/lib/net8.0/Runtime.dll",
            "lib/../Escape.dll",
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
