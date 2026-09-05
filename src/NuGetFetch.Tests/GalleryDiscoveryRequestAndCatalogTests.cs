namespace NuGetFetch.Tests;

public sealed class GalleryDiscoveryRequestAndCatalogTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u2003")]
    public void TermlessRequestsBrowseMostDownloaded(string? text)
    {
        var request = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery,
            capacity: 200,
            text: text);

        Assert.True(request.IsBrowse);
        Assert.Null(request.Text);
        Assert.Null(request.PackageType);
        Assert.False(request.IncludePrerelease);
        Assert.Equal(NuGetGalleryDiscoveryOrder.MostDownloaded, request.Order);
        Assert.Equal(200, request.Capacity);
    }

    [Theory]
    [InlineData("json")]
    [InlineData(" id:Newtonsoft.Json ")]
    [InlineData("owner:Contoso tags:tool")]
    [InlineData("System.*")]
    [InlineData("\u0394 tools")]
    public void SearchPreservesTextAndDefaultsToRelevance(string text)
    {
        var request = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery,
            capacity: 200,
            text: text);

        Assert.False(request.IsBrowse);
        Assert.Equal(text, request.Text);
        Assert.Equal(NuGetGalleryDiscoveryOrder.Relevance, request.Order);
    }

    [Theory]
    [InlineData(null, NuGetGalleryDiscoveryOrder.Relevance)]
    [InlineData(null, NuGetGalleryDiscoveryOrder.MostDownloaded)]
    [InlineData("json", NuGetGalleryDiscoveryOrder.Relevance)]
    [InlineData("json", NuGetGalleryDiscoveryOrder.MostDownloaded)]
    public void ExplicitOrderWinsForBrowseAndSearch(
        string? text,
        NuGetGalleryDiscoveryOrder order)
    {
        var request = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery,
            capacity: 20,
            text: text,
            order: order);

        Assert.Equal(order, request.Order);
    }

    [Fact]
    public void DiscoveredFacetCreatesTypedToolBrowseIntent()
    {
        NuGetGalleryPackageTypeFacetDescriptor facet =
            NuGetGalleryDiscoveryCatalog.GetFacet("nuget.gallery.package-type");
        var request = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery,
            capacity: 200,
            packageType: facet.Select("DotnetTool"),
            includePrerelease: true);

        Assert.Equal(NuGetGalleryPackageType.DotnetTool, request.PackageType);
        Assert.True(request.IsBrowse);
        Assert.True(request.IncludePrerelease);
        Assert.Equal(NuGetGalleryDiscoveryOrder.MostDownloaded, request.Order);
        Assert.Same(PackageSourceDescriptor.NuGetGallery, request.Source);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void CapacityIncludesBothProviderBoundaries(int capacity)
    {
        var request = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery,
            capacity);

        Assert.Equal(capacity, request.Capacity);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1001)]
    [InlineData(int.MaxValue)]
    public void CapacityOutsideProviderBoundsIsRejectedInsteadOfClamped(int capacity)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new NuGetGalleryDiscoveryRequest(
                    PackageSourceDescriptor.NuGetGallery,
                    capacity));

        Assert.Equal("capacity", exception.ParamName);
    }

    [Fact]
    public void CapacityRemainsPartOfTheSourceInputIdentity()
    {
        var ten = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery,
            capacity: 10);
        var twoHundred = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery,
            capacity: 200);
        var equivalent = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery,
            capacity: 200,
            text: " ",
            order: NuGetGalleryDiscoveryOrder.MostDownloaded);

        Assert.NotEqual(ten, twoHundred);
        Assert.Equal(twoHundred, equivalent);
        Assert.Equal(200, twoHundred.Capacity);
    }

    [Fact]
    public void NuGetOrgProducerIdentityDoesNotTurnAV3FeedIntoGallery()
    {
        PackageSourceDescriptor feed = PackageSourceDescriptor.NuGetV3(
            "nuget",
            "NuGet.org",
            new Uri("https://api.nuget.org/v3/index.json"));
        Assert.Equal(PackageSourceDescriptor.NuGetGallery.Identity, feed.Identity);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new NuGetGalleryDiscoveryRequest(feed, capacity: 20));

        Assert.Equal("source", exception.ParamName);
    }

    [Fact]
    public void SourceConfigurationIsPreservedWithoutGrantingEligibility()
    {
        PackageSourceDescriptor disabled =
            PackageSourceDescriptor.NuGetGallery with { Enabled = false };
        var request = new NuGetGalleryDiscoveryRequest(disabled, capacity: 20);

        Assert.Same(disabled, request.Source);
        Assert.False(request.Source.Enabled);
    }

    [Fact]
    public void SourceIsRequired()
    {
        Assert.Throws<ArgumentNullException>(
            () => new NuGetGalleryDiscoveryRequest(null!, capacity: 20));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void UndefinedOrdersAreNotSilentlyDefaulted(int value)
    {
        var order = (NuGetGalleryDiscoveryOrder)value;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NuGetGalleryDiscoveryRequest(
                PackageSourceDescriptor.NuGetGallery,
                capacity: 20,
                order: order));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NuGetGalleryDiscoveryCatalog.GetOrder(order));
    }

    [Fact]
    public void CatalogDescribesTheTypedPackageTypeDomain()
    {
        NuGetGalleryPackageTypeFacetDescriptor facet =
            Assert.Single(NuGetGalleryDiscoveryCatalog.Facets);

        Assert.Same(NuGetGalleryDiscoveryCatalog.PackageType, facet);
        Assert.Equal("nuget.gallery.package-type", facet.Id);
        Assert.Equal("Package type", facet.Label);
        Assert.NotEmpty(facet.Summary);
        Assert.Equal(PackageSourceKind.NuGetGallery, facet.SourceKind);
        Assert.Equal(0, facet.MinimumSelections);
        Assert.Equal(1, facet.MaximumSelections);
        Assert.Equal(
            [NuGetGalleryPackageType.DotnetTool, NuGetGalleryPackageType.Template,
                NuGetGalleryPackageType.Dependency],
            facet.Suggestions.Select(suggestion => suggestion.Value));
        Assert.Equal(
            [".NET tools", "Templates", "Dependency packages"],
            facet.Suggestions.Select(suggestion => suggestion.Label));
    }

    [Theory]
    [InlineData("DotnetTool", "dotnettool")]
    [InlineData("Template", "template")]
    [InlineData("Dependency", "dependency")]
    [InlineData("Contoso.Custom_Type", "contoso.custom_type")]
    [InlineData("\u0394.Tools", "\u03b4.tools")]
    public void PackageTypesUseTheProviderDomainAndCanonicalIdentity(
        string value,
        string expected)
    {
        NuGetGalleryPackageType selected =
            NuGetGalleryDiscoveryCatalog.PackageType.Select(value);

        Assert.Equal(expected, selected.Name);
        Assert.Equal(expected, selected.ToString());
        Assert.Equal(NuGetGalleryPackageType.Create(expected), selected);
        Assert.Equal(
            NuGetGalleryPackageType.Create(expected).GetHashCode(),
            selected.GetHashCode());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("DotnetTool ")]
    [InlineData("DotnetTool,Template")]
    [InlineData("Contoso..Type")]
    [InlineData("Contoso/Type")]
    [InlineData("Contoso.Type\n")]
    public void InvalidPackageTypesAreNotDroppedOrCombined(string? value)
    {
        Assert.Throws<ArgumentException>(
            () => NuGetGalleryDiscoveryCatalog.PackageType.Select(value!));
    }

    [Fact]
    public void PackageTypeLengthMatchesTheProviderAdmissionBoundary()
    {
        Assert.Equal(
            new string('a', 100),
            NuGetGalleryPackageType.Create(new string('A', 100)).Name);
        Assert.Throws<ArgumentException>(
            () => NuGetGalleryPackageType.Create(new string('a', 101)));
    }

    [Fact]
    public void AllTypesIsAbsenceNotASpecialPackageTypeValue()
    {
        var allTypes = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery,
            capacity: 20);
        var customTypeNamedAll = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery,
            capacity: 20,
            packageType: NuGetGalleryPackageType.Create("all"));

        Assert.Null(allTypes.PackageType);
        Assert.Equal("all", customTypeNamedAll.PackageType!.Name);
        Assert.NotEqual(allTypes, customTypeNamedAll);
    }

    [Fact]
    public void CatalogOrdersRoundTripByOpaqueIdentityAndTypedValue()
    {
        Assert.Equal(
            ["nuget.gallery.most-downloaded", "nuget.gallery.relevance"],
            NuGetGalleryDiscoveryCatalog.Orders.Select(order => order.Id));

        foreach (NuGetGalleryOrderDescriptor descriptor in NuGetGalleryDiscoveryCatalog.Orders)
        {
            Assert.Same(descriptor, NuGetGalleryDiscoveryCatalog.GetOrder(descriptor.Id));
            Assert.Same(descriptor, NuGetGalleryDiscoveryCatalog.GetOrder(descriptor.Order));
            Assert.Equal(PackageSourceKind.NuGetGallery, descriptor.SourceKind);
            Assert.NotEmpty(descriptor.Label);
            Assert.NotEmpty(descriptor.Summary);

            var request = new NuGetGalleryDiscoveryRequest(
                PackageSourceDescriptor.NuGetGallery,
                capacity: 20,
                order: descriptor.Order);
            Assert.Equal(descriptor.Order, request.Order);
        }
    }

    [Theory]
    [InlineData("Package type")]
    [InlineData("nuget.gallery.unknown")]
    [InlineData("NUGET.GALLERY.PACKAGE-TYPE")]
    [InlineData("nuget.gallery.package-type ")]
    public void FacetLookupDoesNotInterpretLabelsOrUnknownIdentities(string id)
    {
        Assert.Throws<ArgumentException>(
            () => NuGetGalleryDiscoveryCatalog.GetFacet(id));
    }

    [Theory]
    [InlineData("Most downloaded")]
    [InlineData("totalDownloads-desc")]
    [InlineData("NUGET.GALLERY.RELEVANCE")]
    [InlineData("nuget.gallery.unknown")]
    public void OrderLookupDoesNotInterpretLabelsOrProviderParameters(string id)
    {
        Assert.Throws<ArgumentException>(
            () => NuGetGalleryDiscoveryCatalog.GetOrder(id));
    }

    [Fact]
    public void CatalogDiscoveryRetainsImmutableEntries()
    {
        var facets = NuGetGalleryDiscoveryCatalog.Facets;
        var orders = NuGetGalleryDiscoveryCatalog.Orders;
        var suggestions = NuGetGalleryDiscoveryCatalog.PackageType.Suggestions;

        Assert.Empty(facets.Clear());
        Assert.Empty(orders.Clear());
        Assert.Empty(suggestions.Clear());
        Assert.Single(NuGetGalleryDiscoveryCatalog.Facets);
        Assert.Equal(2, NuGetGalleryDiscoveryCatalog.Orders.Length);
        Assert.Equal(3, NuGetGalleryDiscoveryCatalog.PackageType.Suggestions.Length);
        Assert.Same(facets[0], NuGetGalleryDiscoveryCatalog.Facets[0]);
        Assert.Same(orders[0], NuGetGalleryDiscoveryCatalog.Orders[0]);
    }
}
