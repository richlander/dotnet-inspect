using System.Text;

namespace DotnetInspector.Services.Tests;

public class TfmSelectorTests : IDisposable
{
    private readonly string _tempDir;

    public TfmSelectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tfm-selector-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_SelectsHighestTfmAssemblies()
    {
        var netstandard = WriteDll("lib/netstandard2.0/MyLib.dll");
        var net8 = WriteDll("lib/net8.0/MyLib.dll");
        var net8Companion = WriteDll("lib/net8.0/MyLib.Companion.dll");
        WriteDll("lib/net8.0/fr/MyLib.resources.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir);

        Assert.Equal("net8.0", tfm);
        Assert.Equal(2, paths.Count);
        Assert.Contains(net8, paths);
        Assert.Contains(net8Companion, paths);
        Assert.DoesNotContain(netstandard, paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_KeepsPrimaryAssemblyEndingInResources()
    {
        var resourcesAssembly = WriteDll("lib/net472/MyCompany.Resources.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir);

        Assert.Equal("net472", tfm);
        Assert.Equal([resourcesAssembly], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_KeepsToolPrimaryAssemblyEndingInResources()
    {
        var resourcesAssembly = WriteDll("tools/net8.0/any/MyTool.Resources.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "net8.0");

        Assert.Equal("net8.0", tfm);
        Assert.Equal([resourcesAssembly], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_ExplicitTfm_SelectsMatchingAssemblies()
    {
        var net6 = WriteDll("lib/net6.0/MyLib.dll");
        WriteDll("lib/net8.0/MyLib.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "net6.0");

        Assert.Equal("net6.0", tfm);
        Assert.Equal([net6], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_ToolsLayoutExplicitTfm_SelectsMatchingAssemblies()
    {
        var tool = WriteDll("tools/net8.0/any/MyTool.dll");
        WriteDll("tools/net6.0/any/MyTool.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "net8.0");

        Assert.Equal("net8.0", tfm);
        Assert.Equal([tool], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_RefLayoutExplicitTfm_SelectsMatchingAssemblies()
    {
        var referenceAssembly = WriteDll("ref/net8.0/MyLib.dll");
        WriteDll("ref/net6.0/MyLib.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "net8.0");

        Assert.Equal("net8.0", tfm);
        Assert.Equal([referenceAssembly], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_ExplicitTfmScansAllLayouts()
    {
        WriteDll("tools/net472/MyTool.dll");
        var libraryAssembly = WriteDll("lib/net8.0/MyLib.dll");
        var companionAssembly = WriteDll("runtimes/linux-x64/lib/net8.0/MyRuntime.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "net8.0");

        Assert.Equal("net8.0", tfm);
        Assert.Equal([libraryAssembly, companionAssembly], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_ExplicitTfmWithoutMatches_ReturnsEmptySelection()
    {
        WriteDll("lib/net8.0/MyLib.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "net6.0");

        Assert.Equal("net6.0", tfm);
        Assert.Empty(paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_AllTfms_ReturnsAllNonResourceAssemblies()
    {
        var net6 = WriteDll("lib/net6.0/MyLib.dll");
        var net8 = WriteDll("lib/net8.0/MyLib.dll");
        WriteDll("lib/net8.0/fr/MyLib.resources.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "all");

        Assert.Null(tfm);
        Assert.Equal([net6, net8], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_AllTfmsScansAllLayouts()
    {
        var tool = WriteDll("tools/net6.0/Tool.dll");
        var library = WriteDll("lib/net8.0/Lib.dll");
        var runtime = WriteDll("runtimes/linux-x64/lib/net8.0/Runtime.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "all");

        Assert.Null(tfm);
        Assert.Equal([library, runtime, tool], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_FiltersSatelliteWithUppercaseCultureDirectory()
    {
        var primary = WriteDll("lib/net8.0/MyLib.dll");
        WriteDll("lib/net8.0/ES-ES/MyLib.resources.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir);

        Assert.Equal("net8.0", tfm);
        Assert.Equal([primary], paths);
    }

    [Fact]
    public void GetPackageTfms_ReturnsDistinctTfmsInPriorityOrder()
    {
        WriteDll("lib/netstandard2.0/MyLib.dll");
        WriteDll("lib/net8.0/MyLib.dll");
        WriteDll("lib/net8.0/MyLib.Companion.dll");

        var tfms = TfmSelector.GetPackageTfms(_tempDir);

        Assert.Equal(["net8.0", "netstandard2.0"], tfms);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_NoTfmLayout_ReturnsPackageAssemblies()
    {
        var root = WriteDll("MyLib.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir);

        Assert.Null(tfm);
        Assert.Equal([root], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_RuntimeOnlyPackage_ReturnsRuntimeAssembly()
    {
        var runtimeAssembly = WriteDll("runtimes/linux-x64/lib/net8.0/MyRuntime.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir);

        Assert.Equal("net8.0", tfm);
        Assert.Equal([runtimeAssembly], paths);
    }

    [Fact]
    public void SelectPackageLibrary_CandidateNamesake_UsesManagedIdentity()
    {
        WriteAssembly(
            "lib/net8.0/Companion.dll",
            typeof(TfmSelector).Assembly.Location);
        var primary = WriteAssembly(
            "lib/net8.0/Renamed.dll",
            typeof(TfmSelectorTests).Assembly.Location);
        string packageId =
            typeof(TfmSelectorTests).Assembly.GetName().Name!;

        var result = TfmSelector.SelectPackageLibrary(
            [
                Path.Combine(_tempDir, "lib/net8.0/Companion.dll"),
                primary,
            ],
            _tempDir,
            packageId,
            requestedLibrary: "",
            tfm: "net8.0");

        Assert.True(result.IsSelected);
        Assert.Equal(TfmSelector.PackageLibraryResolutionStatus.Selected, result.Status);
        Assert.Equal([primary], result.Paths);
        Assert.Equal("net8.0", result.Tfm);
    }

    [Fact]
    public void SelectPackageLibrary_BareRequest_AmbiguousWhenNoPackageNameMatch()
    {
        var first = WriteAssembly(
            "lib/net8.0/First.dll",
            typeof(TfmSelector).Assembly.Location);
        var second = WriteAssembly(
            "lib/net8.0/Second.dll",
            typeof(TfmSelector).Assembly.Location);

        var result = TfmSelector.SelectPackageLibrary(_tempDir, "MyPackage", requestedLibrary: "");

        Assert.False(result.IsSelected);
        Assert.Equal(TfmSelector.PackageLibraryResolutionStatus.Ambiguous, result.Status);
        Assert.Equal("net8.0", result.Tfm);
        Assert.Equal([first, second], result.CandidatePaths);
    }

    [Fact]
    public void SelectPackageLibrary_CandidateNamesake_ReportsUnreadableIdentity()
    {
        var unreadable = WriteDll("lib/net8.0/Unreadable.dll");

        var result = TfmSelector.SelectPackageLibrary(
            [unreadable],
            _tempDir,
            "MyPackage",
            requestedLibrary: "",
            tfm: "net8.0");

        Assert.False(result.IsSelected);
        Assert.Equal(
            TfmSelector.PackageLibraryResolutionStatus
                .NamesakeIdentityUnavailable,
            result.Status);
        Assert.Equal([unreadable], result.IdentityFailurePaths);
    }

    [Fact]
    public void SelectPackageLibrary_CandidateNamesake_SkipsNonAssemblyCandidates()
    {
        var namesake = WriteAssembly(
            "lib/net8.0/Renamed.dll",
            typeof(TfmSelectorTests).Assembly.Location);
        string placeholder =
            WriteDll("lib/net8.0/Text.dll");
        File.WriteAllText(
            placeholder,
            "café",
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true));
        string packageId =
            typeof(TfmSelectorTests).Assembly.GetName().Name!;

        var result = TfmSelector.SelectPackageLibrary(
            [namesake, placeholder],
            _tempDir,
            packageId,
            requestedLibrary: "",
            tfm: "net8.0");

        Assert.True(result.IsSelected);
        Assert.Equal([namesake], result.Paths);
        Assert.Empty(result.IdentityFailurePaths ?? []);
    }

    [Fact]
    public void SelectPackageLibrary_RequestedLibraryNotFound_ReturnsTfmCandidates()
    {
        var candidate = WriteDll("lib/net8.0/Actual.dll");

        var result = TfmSelector.SelectPackageLibrary(_tempDir, "MyPackage", requestedLibrary: "Missing", tfm: "net8.0");

        Assert.False(result.IsSelected);
        Assert.Equal(TfmSelector.PackageLibraryResolutionStatus.RequestedLibraryNotFound, result.Status);
        Assert.Equal("net8.0", result.Tfm);
        Assert.Equal([candidate], result.CandidatePaths);
    }

    [Fact]
    public void SelectPackageLibrary_RequestedLibrarySearchesAllTfms()
    {
        WriteDll("lib/net10.0/Unrelated.dll");
        var requested = WriteDll("lib/net45/Requested.dll");

        var result = TfmSelector.SelectPackageLibrary(
            _tempDir,
            "MyPackage",
            requestedLibrary: "Requested.dll");

        Assert.True(result.IsSelected);
        Assert.Equal("net45", result.Tfm);
        Assert.Equal([requested], result.Paths);
    }

    [Fact]
    public void SelectPackageLibraries_NoTfm_SelectsHighestTfmInStableOrder()
    {
        WriteDll("lib/net8.0/Zeta.dll");
        var alpha = WriteDll("lib/net10.0/Alpha.dll");
        var beta = WriteDll("lib/net10.0/Beta.dll");

        var result = TfmSelector.SelectPackageLibraries(_tempDir);

        Assert.True(result.IsSelected);
        Assert.Equal("net10.0", result.Tfm);
        Assert.Equal([alpha, beta], result.Paths);
    }

    [Fact]
    public void SelectPackageLibraries_ExplicitTfmWithoutMatches_ReturnsNoMatchingTfm()
    {
        WriteDll("lib/net8.0/Actual.dll");

        var result = TfmSelector.SelectPackageLibraries(_tempDir, "net6.0");

        Assert.False(result.IsSelected);
        Assert.Equal(TfmSelector.PackageLibraryResolutionStatus.NoMatchingTargetFramework, result.Status);
        Assert.Equal("net6.0", result.Tfm);
        Assert.Empty(result.Paths);
    }

    [Fact]
    public void FindAssemblyByTfm_UsesPackageNameMatch()
    {
        WriteDll("lib/net8.0/Companion.dll");
        var primary = WriteDll("lib/net8.0/MyPackage.dll");

        var result = TfmSelector.FindAssemblyByTfm(_tempDir, "net8.0", "MyPackage");

        Assert.Equal(primary, result);
    }

    [Fact]
    public void FindAssemblyInPackage_ExplicitTfmScansAllLayouts()
    {
        WriteDll("tools/net472/MyTool.dll");
        var libraryAssembly = WriteDll("lib/net8.0/MyLib.dll");

        var (path, tfm) = TfmSelector.FindAssemblyInPackage(_tempDir, "MyLib", "net8.0");

        Assert.Equal(libraryAssembly, path);
        Assert.Equal("net8.0", tfm);
    }

    [Fact]
    public void FindAssemblyInPackage_ExactRelativePathWinsOverAHigherTfmWithTheSameFileName()
    {
        var requested = WriteDll("lib/net8.0/Target.dll");
        WriteDll("lib/net10.0/Target.dll");

        var (path, tfm) = TfmSelector.FindAssemblyInPackage(
            _tempDir,
            "lib/net8.0/Target.dll");

        Assert.Equal(requested, path);
        Assert.Equal("net8.0", tfm);
    }

    [Fact]
    public void FindExactPackageAsset_PrefersExactCaseAndRejectsAmbiguousCaseFolding()
    {
        string upper = Path.Combine(_tempDir, "lib", "net8.0", "Target.dll");
        string lower = Path.Combine(_tempDir, "lib", "net8.0", "target.dll");
        string[] paths = [upper, lower];

        Assert.Equal(
            upper,
            TfmSelector.FindExactPackageAsset(
                paths,
                _tempDir,
                "lib/net8.0/Target.dll"));
        Assert.Null(
            TfmSelector.FindExactPackageAsset(
                paths,
                _tempDir,
                "lib/net8.0/TARGET.dll"));
    }

    [Fact]
    public void FindAssemblyInPackage_PathQualifiedAmbiguousCaseDoesNotFallBackToFileName()
    {
        string upper = Path.Combine(_tempDir, "lib", "net8.0", "Target.dll");
        string lower = Path.Combine(_tempDir, "lib", "net8.0", "target.dll");

        var (path, tfm) = TfmSelector.FindAssemblyInPackage(
            [upper, lower],
            _tempDir,
            "lib/net8.0/TARGET.dll",
            tfm: null);

        Assert.Null(path);
        Assert.Null(tfm);
    }

    [Fact]
    public void FindAssemblyInPackage_PathQualifiedCaseMismatchIsNotAnExactAsset()
    {
        WriteDll("lib/net8.0/Target.dll");
        string differentlyCased = "lib/net8.0/target.dll";

        var (path, tfm) = TfmSelector.FindAssemblyInPackage(
            _tempDir,
            differentlyCased);

        Assert.Null(path);
        Assert.Null(tfm);
    }

    [Fact]
    public void FindAssemblyByTfm_FiltersResourceAssemblies()
    {
        var primary = WriteDll("tools/net8.0/any/MyTool.dll");
        WriteDll("tools/net8.0/any/fr/MyTool.resources.dll");

        var result = TfmSelector.FindAssemblyByTfm(_tempDir, "net8.0");

        Assert.Equal(primary, result);
    }

    [Fact]
    public void FindAssemblyByTfm_HandlesRuntimeSpecificLibLayout()
    {
        var runtimeAssembly = WriteDll("runtimes/linux-x64/lib/net8.0/MyRuntime.dll");

        var result = TfmSelector.FindAssemblyByTfm(_tempDir, "net8.0");

        Assert.Equal(runtimeAssembly, result);
    }

    [Fact]
    public void FindAssemblyByTfm_PrefersLibPackageNameMatchOverToolsAssembly()
    {
        WriteDll("tools/net8.0/MyTool.dll");
        var libraryAssembly = WriteDll("lib/net8.0/MyLib.dll");

        var result = TfmSelector.FindAssemblyByTfm(_tempDir, "net8.0", "MyLib");

        Assert.Equal(libraryAssembly, result);
    }

    [Fact]
    public void FindAssemblyByTfm_FindsLibTfmWhenToolsHasDifferentTfm()
    {
        WriteDll("tools/net472/MyTool.dll");
        var libraryAssembly = WriteDll("lib/net8.0/MyLib.dll");

        var result = TfmSelector.FindAssemblyByTfm(_tempDir, "net8.0");

        Assert.Equal(libraryAssembly, result);
    }

    [Fact]
    public void FindAssemblyByTfm_FindsRuntimeSpecificLibWhenLibHasDifferentTfm()
    {
        WriteDll("lib/net6.0/MyLib.dll");
        var runtimeAssembly = WriteDll("runtimes/linux-x64/lib/net8.0/MyRuntime.dll");

        var result = TfmSelector.FindAssemblyByTfm(_tempDir, "net8.0");

        Assert.Equal(runtimeAssembly, result);
    }

    /// <summary>
    /// The archive-directory form of the explicit-framework selection picks
    /// exactly the package-relative paths the extracted-tree form picks, so
    /// the package endpoint scope can apply the legacy selector before
    /// anything is extracted (docs/design/package-endpoint-scope.md).
    /// </summary>
    [Theory]
    [InlineData("RealAssets/PackageReadDemand/avalonia.12.1.2.nupkg", "net8.0")]
    [InlineData("RealAssets/PackageReadDemand/avalonia.12.1.2.nupkg", "net10.0")]
    [InlineData("RealAssets/PackageReadDemand/avalonia.12.1.2.nupkg", "netstandard2.0")]
    [InlineData("RealAssets/PackageAdmission/System.Text.Json.10.0.0.nupkg", "net8.0")]
    [InlineData("RealAssets/PackageAdmission/System.Text.Json.10.0.0.nupkg", "net462")]
    [InlineData("fixtures/nugetfetch/pclstorage.1.0.2.nupkg", "net45")]
    public void SelectAssembliesByTfmFromEntries_MatchesTheExtractedSelection(
        string archive,
        string tfm)
    {
        string path = Path.Combine(AppContext.BaseDirectory, archive);
        string[] entries;
        using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
        {
            entries = [.. zip.Entries.Select(static entry => entry.FullName)];
        }
        System.IO.Compression.ZipFile.ExtractToDirectory(path, _tempDir);

        string[] extracted =
        [
            .. TfmSelector.SelectAssembliesByTfmFromPackage(_tempDir, tfm).paths
                .Select(selected => Path.GetRelativePath(_tempDir, selected).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(extracted, TfmSelector.SelectAssembliesByTfmFromEntries(entries, tfm));
    }

    [Fact]
    public void SelectAssembliesByTfmFromEntries_MatchesTheExtractedSelectionForSatellitesAndTools()
    {
        string[] entries =
        [
            "lib/net8.0/MyLib.dll",
            "lib/net8.0/fr/MyLib.resources.dll",
            "lib/net8.0/de/Orphan.resources.dll",
            "ref/net8.0/MyLib.dll",
            "tools/net8.0/any/Tool.dll",
            "runtimes/win/lib/net8.0/MyLib.dll",
            "runtimes/win/native/native.dll",
            "lib/net6.0/MyLib.dll",
            "analyzers/dotnet/cs/MyLib.Analyzers.dll",
            "lib/net8.0/",
        ];
        foreach (string entry in entries.Where(static entry => !entry.EndsWith('/')))
            WriteDll(entry);

        string[] extracted =
        [
            .. TfmSelector.SelectAssembliesByTfmFromPackage(_tempDir, "net8.0").paths
                .Select(selected => Path.GetRelativePath(_tempDir, selected).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(extracted, TfmSelector.SelectAssembliesByTfmFromEntries(entries, "net8.0"));
        Assert.Contains("ref/net8.0/MyLib.dll", extracted);
        Assert.Contains("lib/net8.0/MyLib.dll", extracted);
        Assert.DoesNotContain("lib/net8.0/fr/MyLib.resources.dll", extracted);
    }

    private string WriteDll(string relativePath)
    {
        var path = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    private string WriteAssembly(
        string relativePath,
        string sourcePath)
    {
        string path = Path.Combine(
            _tempDir,
            relativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(sourcePath, path);
        return path;
    }
}
