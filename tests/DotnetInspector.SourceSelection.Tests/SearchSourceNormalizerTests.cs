using DotnetInspector.Packages;

namespace DotnetInspector.SourceSelection.Tests;

public sealed class SearchSourceNormalizerTests
{
    private static readonly SearchPlatformFramework[] PlatformFrameworks =
    [
        SearchPlatformFramework.Runtime,
        SearchPlatformFramework.AspNetCore,
        SearchPlatformFramework.NetStandard,
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void EveryPlatformAndGroupCombinationHasExactInterpretation(int flags)
    {
        List<SourceSelector> selectors = [];
        if ((flags & 1) != 0)
            selectors.Add(new SourceSelector.PlatformGroup());
        if ((flags & 2) != 0)
            selectors.Add(new SourceSelector.PackageGroup([new("Extensions.Core"), new("Shared")]));
        if ((flags & 4) != 0)
            selectors.Add(new SourceSelector.PackageGroup([new("AspNetCore.Core"), new("Shared")]));
        SourceIntent intent = SourceIntent.Create(selectors);

        SearchSourceSelection result = SearchSourceNormalizer.Normalize(intent);
        string[] expectedPackages = flags switch
        {
            0 or 1 => [],
            2 or 3 => ["Extensions.Core", "Shared"],
            4 or 5 => ["AspNetCore.Core", "Shared"],
            6 or 7 => ["Extensions.Core", "Shared", "AspNetCore.Core"],
            _ => throw new InvalidOperationException(),
        };

        Assert.Same(intent, result.Intent);
        Assert.Equal(flags == 0, result.UsesImplicitPlatform);
        Assert.Equal(flags == 0 || (flags & 1) != 0 ? PlatformFrameworks : [], result.Frameworks);
        Assert.Equal(expectedPackages, result.Packages.Select(package => package.PackageId));
        Assert.Empty(result.OtherSources);
    }

    [Fact]
    public void EachExplicitVariantSuppressesImplicitPlatform()
    {
        SourceSelector[] selectors =
        [
            new SourceSelector.Package(new("Contoso.Core")),
            new SourceSelector.PackageGroup([]),
            new SourceSelector.PackagePrefix(new("Contoso.", 500)),
            new SourceSelector.Library("not-created.dll"),
            new SourceSelector.PlatformLibrary("NotInstalled"),
            new SourceSelector.Project("not-created.csproj"),
            new SourceSelector.BinaryDirectory("not-created"),
        ];

        foreach (SourceSelector selector in selectors)
        {
            SearchSourceSelection result = SearchSourceNormalizer.Normalize(SourceIntent.Empty.Append(selector));
            Assert.False(result.UsesImplicitPlatform);
            Assert.Empty(result.Frameworks);
            Assert.Same(selector, Assert.Single(result.Intent.Selectors));
        }
    }

    [Fact]
    public void ExplicitPlatformRepeatsOnceAndRemainsExplicit()
    {
        SourceIntent intent = SourceIntent.Create(
        [
            new SourceSelector.Package(new("Contoso")),
            new SourceSelector.PlatformGroup(),
            new SourceSelector.PlatformGroup(),
        ]);
        SearchSourceSelection result = SearchSourceNormalizer.Normalize(intent);
        Assert.False(result.UsesImplicitPlatform);
        Assert.Equal(PlatformFrameworks, result.Frameworks);
        Assert.Equal("Contoso", Assert.Single(result.Packages).PackageId);
        Assert.Equal(3, result.Intent.Selectors.Count);
    }

    [Fact]
    public void DirectPackagesPrecedeGroupsAndFirstRequestWins()
    {
        var first = new PackageCoordinate("CONTOSO.CORE", "1.0.0-RC.1");
        var unversioned = new PackageCoordinate("Contoso.Core");
        SourceIntent intent = SourceIntent.Create(
        [
            new SourceSelector.PackageGroup([new("Group.First"), new("contoso.core", "1.0.0-rc.1")]),
            new SourceSelector.Package(first),
            new SourceSelector.Package(new("contoso.core", "1.0.0-rc.1")),
            new SourceSelector.PackageGroup([new("group.first"), new("Group.Second"), unversioned]),
            new SourceSelector.Package(unversioned),
        ]);

        SearchSourceSelection result = SearchSourceNormalizer.Normalize(intent);
        Assert.Equal([first, unversioned, new("Group.First"), new("Group.Second")], result.Packages);
        Assert.Same(first, result.Packages[0]);
        Assert.Same(unversioned, result.Packages[1]);
        Assert.Equal(5, intent.Selectors.Count);
    }

    [Fact]
    public void CoordinateFieldsAreComparedWithoutRealization()
    {
        PackageCoordinate[] unique =
        [
            new("Contoso"),
            new("Contoso", "1.0"),
            new("Contoso", "1.0.0"),
            new("Contoso", "2.0.0"),
            new("Contoso", "1.0.0", "net10.0"),
            new("Contoso", "1.0.0", "net11.0"),
            new("Contoso", "1.0.0", "net10.0", "linux-x64"),
            new("Contoso", "1.0.0", "net10.0", "win-x64"),
        ];
        SourceIntent intent = SourceIntent.Create(
            unique.Select(coordinate => new SourceSelector.Package(coordinate)));
        intent = intent.Append(new SourceSelector.Package(new("CONTOSO", "1.0.0", "NET10.0")));

        SearchSourceSelection result = SearchSourceNormalizer.Normalize(intent);
        Assert.Equal(unique, result.Packages);
    }

    [Fact]
    public void UnrealizedPrefixAndEmptyGroupNeverBecomeEmptyIntent()
    {
        var request = new PackagePrefixRequest("Aspire.", 500);
        var prefix = new SourceSelector.PackagePrefix(request);
        SourceIntent intent = SourceIntent.Create([prefix, new SourceSelector.PackageGroup([])]);
        SearchSourceSelection result = SearchSourceNormalizer.Normalize(intent);
        SearchSourceSelection repeated = SearchSourceNormalizer.Normalize(result.Intent);

        Assert.False(result.UsesImplicitPlatform);
        Assert.False(repeated.UsesImplicitPlatform);
        Assert.Empty(result.Frameworks);
        Assert.Empty(result.Packages);
        Assert.Empty(repeated.Frameworks);
        Assert.Empty(repeated.Packages);
        Assert.Same(prefix, Assert.Single(result.OtherSources));
        Assert.Same(request, Assert.IsType<SourceSelector.PackagePrefix>(result.OtherSources[0]).Request);
        Assert.Same(prefix, Assert.Single(repeated.OtherSources));
        Assert.Equal(2, intent.Selectors.Count);
    }

    [Fact]
    public void OtherSelectorsRetainRelativeOrderAndOccurrences()
    {
        var library = new SourceSelector.Library("first.dll");
        var prefix = new SourceSelector.PackagePrefix(new("Contoso.", 17, includePrerelease: true));
        var platformLibrary = new SourceSelector.PlatformLibrary("System.Text.Json");
        var project = new SourceSelector.Project("sample.csproj");
        var directory = new SourceSelector.BinaryDirectory("bin");
        SourceIntent intent = SourceIntent.Create(
        [
            library, new SourceSelector.PackageGroup([]), prefix, platformLibrary,
            new SourceSelector.Package(new("Contoso")), project, directory, library,
        ]);

        SearchSourceSelection result = SearchSourceNormalizer.Normalize(intent);
        Assert.Equal([library, prefix, platformLibrary, project, directory, library], result.OtherSources);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SourceSelector>)result.OtherSources).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PackageCoordinate>)result.Packages).Clear());
        SearchSourceSelection implicitResult = SearchSourceNormalizer.Normalize(SourceIntent.Empty);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SearchPlatformFramework>)implicitResult.Frameworks).Clear());
        Assert.Equal(PlatformFrameworks, SearchSourceNormalizer.Normalize(SourceIntent.Empty).Frameworks);
        Assert.Empty(SourceIntent.Empty.Selectors);
    }

    [Fact]
    public void NullDeclarationIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => SearchSourceNormalizer.Normalize(null!));
}
