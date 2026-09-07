using DotnetInspector.Packages;

namespace DotnetInspector.SourceSelection.Tests;

public sealed class PackageSourceIntentTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("LATEST")]
    [InlineData("*")]
    [InlineData("11.0.0-preview*")]
    [InlineData("1.0")]
    [InlineData("1.0.0+Build")]
    [InlineData(" 1.0.0 ")]
    public void ReferenceRetainsOwnerAcceptedVersionSpelling(string? version)
    {
        Assert.True(PackageReferenceParser.IsValidVersion(version));
        var reference = new SourceSelector.PackageReference("Contoso.Core", version);
        var intent = SourceIntent.Empty.Append(reference);
        SearchSourceSelection selection = SearchSourceNormalizer.Normalize(intent);

        Assert.Equal("Contoso.Core", reference.PackageId);
        Assert.Equal(version, reference.Version);
        Assert.Same(reference, Assert.Single(selection.Packages));
        Assert.Same(intent, selection.Intent);
        Assert.False(selection.UsesImplicitPlatform);
        Assert.Empty(selection.Frameworks);
        Assert.Empty(selection.OtherSources);
    }

    [Theory]
    [InlineData("not a version")]
    [InlineData("[1.0,2.0)")]
    [InlineData(" \t")]
    [InlineData("1.*\0")]
    public void ReferenceRejectsIntrinsicInvalidVersion(string version) =>
        Assert.Throws<ArgumentException>(() =>
            new SourceSelector.PackageReference("Contoso", version));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a package")]
    [InlineData("Contoso.")]
    [InlineData("Contoso@latest")]
    public void ReferenceUsesPackageOwnerIdValidation(string? packageId) =>
        Assert.Throws<ArgumentException>(() =>
            new SourceSelector.PackageReference(packageId!));

    [Theory]
    [InlineData("Contoso.Core", "Contoso.Core", null)]
    [InlineData("Contoso.Core@latest", "Contoso.Core", "latest")]
    [InlineData("Contoso.Core@11.0.0-preview*", "Contoso.Core", "11.0.0-preview*")]
    [InlineData("Contoso.Core@", "Contoso.Core", "")]
    [InlineData("Contoso.Core@1.0.0+Build", "Contoso.Core", "1.0.0+Build")]
    [InlineData("Contoso.Core.1.0.0.nupkg", "Contoso.Core", "1.0.0")]
    [InlineData("Contoso.Core.nupkg", "Contoso.Core", null)]
    public void PureParserPreservesExistingReferenceParsing(
        string source, string expectedName, string? expectedVersion)
    {
        var (name, version) = PackageReferenceParser.Parse(source);
        Assert.Equal(expectedName, name);
        Assert.Equal(expectedVersion, version);
    }

    [Fact]
    public void ArchiveRetainsExplicitPathWithoutInferringPackageIdentity()
    {
        const string path = "../not-created/Contoso.Core.1.0.0.nupkg";
        var archive = new SourceSelector.PackageArchive(path);
        SearchSourceSelection selection = SearchSourceNormalizer.Normalize(
            SourceIntent.Empty.Append(archive));

        Assert.Equal(path, archive.Path);
        Assert.Same(archive, Assert.Single(selection.Packages));
        Assert.False(selection.UsesImplicitPlatform);
        Assert.Empty(selection.Frameworks);
        Assert.Empty(selection.OtherSources);
    }

    [Fact]
    public void MixedDirectSourcesPrecedeGroupsWithoutLosingRequestForms()
    {
        var latest = new SourceSelector.PackageReference("Contoso.Core", "latest");
        var archive = new SourceSelector.PackageArchive("./local/Contoso.Core.1.0.0.nupkg");
        var pattern = new SourceSelector.PackageReference("Contoso.Core", "2.*");
        var intent = SourceIntent.Create(
        [
            new SourceSelector.PackageGroup([new("Contoso.Core")]),
            latest,
            archive,
            pattern,
        ]);

        SearchSourceSelection selection = SearchSourceNormalizer.Normalize(intent);
        Assert.Collection(selection.Packages,
            source => Assert.Same(latest, source),
            source => Assert.Same(archive, source),
            source => Assert.Same(pattern, source),
            source => Assert.Equal(new("Contoso.Core"),
                Assert.IsType<SourceSelector.Package>(source).Coordinate));
        Assert.Same(intent, selection.Intent);
        Assert.False(selection.UsesImplicitPlatform);
        Assert.Equal(4, intent.Selectors.Count);
        Assert.Equal(selection.Packages.Take(3),
            SearchSourceNormalizer.Normalize(intent).Packages.Take(3));
    }

    [Fact]
    public void ReferenceAndCoordinateEqualityPreservesFirstTypedForm()
    {
        var reference = new SourceSelector.PackageReference("CONTOSO.CORE", "1.0.0-RC.1");
        var coordinate = new SourceSelector.Package(new("Contoso.Other"));
        var intent = SourceIntent.Create(
        [
            new SourceSelector.PackageGroup([new("contoso.core", "1.0.0-rc.1"), new("contoso.other")]),
            reference,
            new SourceSelector.Package(new("contoso.core", "1.0.0-rc.1")),
            coordinate,
            new SourceSelector.PackageReference("CONTOSO.OTHER"),
            new SourceSelector.PackageReference("contoso.core", "1.0.0-rc.1"),
        ]);

        SearchSourceSelection selection = SearchSourceNormalizer.Normalize(intent);
        Assert.Equal([reference, coordinate], selection.Packages);
    }

    [Fact]
    public void ReferenceExpressionsAndCoordinateDimensionsRemainDistinct()
    {
        SourceSelector.PackageSource[] sources =
        [
            new SourceSelector.PackageReference("Contoso"),
            new SourceSelector.PackageReference("Contoso", ""),
            new SourceSelector.PackageReference("Contoso", "latest"),
            new SourceSelector.PackageReference("Contoso", "*"),
            new SourceSelector.PackageReference("Contoso", "1.*"),
            new SourceSelector.PackageReference("Contoso", "1.0"),
            new SourceSelector.PackageReference("Contoso", "1.0.0"),
            new SourceSelector.PackageReference("Contoso", "1.0.0+Build"),
            new SourceSelector.Package(new("Contoso", "1.0.0", "net10.0")),
            new SourceSelector.Package(new("Contoso", "1.0.0", "net10.0", "linux-x64")),
        ];
        var intent = SourceIntent.Create(sources)
            .Append(new SourceSelector.PackageReference("CONTOSO", "LATEST"))
            .Append(new SourceSelector.PackageReference("CONTOSO", "1.0.0+build"));

        Assert.Equal(sources, SearchSourceNormalizer.Normalize(intent).Packages);
    }

    [Fact]
    public void ArchiveEqualityIsLexicalAndSeparateFromRemoteIdentity()
    {
        var first = new SourceSelector.PackageArchive("one/Contoso.nupkg");
        var second = new SourceSelector.PackageArchive("two/Contoso.nupkg");
        var relative = new SourceSelector.PackageArchive("./one/Contoso.nupkg");
        var bare = new SourceSelector.PackageArchive("Contoso.nupkg");
        var remote = new SourceSelector.Package(new("Contoso.nupkg"));
        var intent = SourceIntent.Create(
        [
            first, second, relative, bare, remote,
            new SourceSelector.PackageArchive("ONE/CONTOSO.NUPKG"),
        ]);

        Assert.Equal([first, second, relative, bare, remote],
            SearchSourceNormalizer.Normalize(intent).Packages);
    }
}
