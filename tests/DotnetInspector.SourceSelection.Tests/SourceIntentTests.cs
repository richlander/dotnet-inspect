using DotnetInspector.Packages;

namespace DotnetInspector.SourceSelection.Tests;

public sealed class SourceIntentTests
{
    [Fact]
    public void EmptyRemainsEmptyWithoutSearchInterpretation()
    {
        Assert.Empty(SourceIntent.Empty.Selectors);
        Assert.Same(SourceIntent.Empty, SourceIntent.Create([]));
        Assert.Empty(SourceIntent.Empty.Selectors);
    }

    [Fact]
    public void PublicConsumerConstructsAndInspectsEveryVariant()
    {
        var coordinate = new PackageCoordinate("Contoso.Core", "1.0.0", "net10.0", "linux-x64");
        var request = new PackagePrefixRequest("Contoso.", 500, includePrerelease: true);
        SourceIntent intent = SourceIntent.Create(
        [
            new SourceSelector.PlatformGroup(),
            new SourceSelector.Package(coordinate),
            new SourceSelector.PackageReference("Contoso.Core", "latest"),
            new SourceSelector.PackageArchive("local/Contoso.nupkg"),
            new SourceSelector.PackageGroup([coordinate]),
            new SourceSelector.PackagePrefix(request),
            new SourceSelector.Library("relative path/library.dll"),
            new SourceSelector.PlatformLibrary("System.Text.Json"),
            new SourceSelector.Project("../not-created/project.csproj"),
            new SourceSelector.BinaryDirectory("not-created/bin"),
        ]);

        Assert.Collection(intent.Selectors,
            source => Assert.IsType<SourceSelector.PlatformGroup>(source),
            source => Assert.Same(coordinate, Assert.IsType<SourceSelector.Package>(source).Coordinate),
            source => Assert.Equal("latest", Assert.IsType<SourceSelector.PackageReference>(source).Version),
            source => Assert.Equal("local/Contoso.nupkg", Assert.IsType<SourceSelector.PackageArchive>(source).Path),
            source => Assert.Same(coordinate,
                Assert.Single(Assert.IsType<SourceSelector.PackageGroup>(source).Coordinates)),
            source => Assert.Same(request, Assert.IsType<SourceSelector.PackagePrefix>(source).Request),
            source => Assert.Equal("relative path/library.dll", Assert.IsType<SourceSelector.Library>(source).Path),
            source => Assert.Equal("System.Text.Json", Assert.IsType<SourceSelector.PlatformLibrary>(source).Name),
            source => Assert.Equal("../not-created/project.csproj", Assert.IsType<SourceSelector.Project>(source).Path),
            source => Assert.Equal("not-created/bin", Assert.IsType<SourceSelector.BinaryDirectory>(source).Path));
    }

    [Fact]
    public void SnapshotsAndAppendDoNotChangeEarlierDeclarations()
    {
        var package = new PackageCoordinate("Contoso.Core");
        var members = new List<PackageCoordinate> { package };
        var group = new SourceSelector.PackageGroup(members);
        var selectors = new List<SourceSelector> { group, group };
        SourceIntent original = SourceIntent.Create(selectors);
        var library = new SourceSelector.Library("missing.dll");
        SourceIntent appended = original.Append(library);

        members.Clear();
        selectors.Clear();

        Assert.Same(package, Assert.Single(group.Coordinates));
        Assert.Equal([group, group], original.Selectors);
        Assert.Equal([group, group, library], appended.Selectors);
        Assert.Empty(SourceIntent.Empty.Selectors);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PackageCoordinate>)group.Coordinates)[0] = new("Other"));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SourceSelector>)original.Selectors)[0] = library);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SourceSelector>)appended.Selectors).Clear());
    }

    [Fact]
    public void NullConstructionInputsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => SourceIntent.Create(null!));
        Assert.Throws<ArgumentNullException>(() => SourceIntent.Create([null!]));
        Assert.Throws<ArgumentNullException>(() => SourceIntent.Empty.Append(null!));
        Assert.Throws<ArgumentNullException>(() => new SourceSelector.Package(null!));
        Assert.Throws<ArgumentNullException>(() => new SourceSelector.PackageGroup(null!));
        Assert.Throws<ArgumentNullException>(() => new SourceSelector.PackageGroup([null!]));
        Assert.Throws<ArgumentNullException>(() => new SourceSelector.PackagePrefix(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("some\0text")]
    public void DirectSourceTextRejectsIntrinsicInvalidity(string? text)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SourceSelector.Library(text!));
        Assert.ThrowsAny<ArgumentException>(() => new SourceSelector.PackageArchive(text!));
        Assert.ThrowsAny<ArgumentException>(() => new SourceSelector.PlatformLibrary(text!));
        Assert.ThrowsAny<ArgumentException>(() => new SourceSelector.Project(text!));
        Assert.ThrowsAny<ArgumentException>(() => new SourceSelector.BinaryDirectory(text!));
    }

    [Theory]
    [InlineData("not a package", null, null, null)]
    [InlineData("Contoso", "latest", null, null)]
    [InlineData("Contoso", "[1.0,2.0)", null, null)]
    [InlineData("Contoso", "1.0.0+build", null, null)]
    [InlineData("Contoso", null, "not a framework", null)]
    [InlineData("Contoso", null, null, "LINUX-X64")]
    public void BothPackageVariantsUsePackageOwnerValidation(
        string id, string? version, string? framework, string? runtime)
    {
        var coordinate = new PackageCoordinate(id, version, framework, runtime);
        Assert.NotNull(PackageCoordinateResolver.Validate(coordinate));
        Assert.Throws<ArgumentException>(() => new SourceSelector.Package(coordinate));
        Assert.Throws<ArgumentException>(() =>
            new SourceSelector.PackageGroup([new("Valid"), coordinate]));
    }
}
