using DotnetInspector.Packages;

namespace DotnetInspector.Packages.Tests;

public class PackageLibraryOrderTests
{
    [Fact]
    public void FirstLibraryRequest_PrefersFirstNamesakeInLibraryOrder()
    {
        // Three namesakes differ only in tie-break order: the case-sensitive
        // name step ranks "Contoso.Json" before "contoso.json", and the path
        // step ranks lib/a before lib/c. An earlier non-namesake is passed
        // over, and the Package ID's case does not matter.
        PackageLibraryIdentity aardvark = new("lib/z/z.dll", "Aardvark");
        PackageLibraryIdentity lowerNamesake =
            new("lib/b/Contoso.Json.dll", "contoso.json");
        PackageLibraryIdentity laterNamesake =
            new("lib/c/y.dll", "Contoso.Json");
        PackageLibraryIdentity firstNamesake =
            new("lib/a/x.dll", "Contoso.Json");

        var selection = PackageLibraryOrder.SelectFirst(
            [lowerNamesake, laterNamesake, aardvark, firstNamesake],
            [],
            "CONTOSO.JSON");

        var selected =
            Assert.IsType<FirstPackageLibrarySelection.Selected>(selection);
        Assert.Same(firstNamesake, selected.Library);
        Assert.Equal(FirstPackageLibraryReason.Namesake, selected.Reason);
        Assert.Equal(
            [aardvark, firstNamesake, laterNamesake, lowerNamesake],
            selected.LibraryOrder);
    }

    [Fact]
    public void FirstLibraryRequest_WithoutNamesakeSelectsFirstInLibraryOrder()
    {
        // Names compare ignoring case first, so "alpha" precedes "Beta" even
        // though ordinal order would not; paths do not decide.
        PackageLibraryIdentity zed = new("lib/a/Zed.dll", "Zed");
        PackageLibraryIdentity beta = new("lib/m/Beta.dll", "Beta");
        PackageLibraryIdentity alpha = new("lib/z/Alpha.dll", "alpha");

        var selection = PackageLibraryOrder.SelectFirst(
            [zed, beta, alpha],
            [],
            "Contoso");

        var selected =
            Assert.IsType<FirstPackageLibrarySelection.Selected>(selection);
        Assert.Same(alpha, selected.Library);
        Assert.Equal(
            FirstPackageLibraryReason.FirstInLibraryOrder,
            selected.Reason);
        Assert.Equal([alpha, beta, zed], selected.LibraryOrder);
    }

    [Fact]
    public void FirstLibraryRequest_UnresolvedIdentityFailsClosed()
    {
        var selection = PackageLibraryOrder.SelectFirst(
            [new("lib/a/Contoso.dll", "Contoso")],
            ["lib/b/Broken.dll"],
            "Contoso");

        var failed = Assert.IsType<
            FirstPackageLibrarySelection.IdentityUnresolved>(selection);
        Assert.Equal(["lib/b/Broken.dll"], failed.AssetPaths);
    }

    [Fact]
    public void FirstLibraryRequest_EmptyPopulationIsUnavailable()
    {
        var selection = PackageLibraryOrder.SelectFirst([], [], "Contoso");

        Assert.IsType<FirstPackageLibrarySelection.Unavailable>(selection);
    }
}
