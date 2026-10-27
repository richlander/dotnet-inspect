using System.Text.Json;
using DotnetInspector.Sections;

namespace DotnetInspector.Sections.Tests;

public sealed class CapabilityCatalogStructuralSearchTests
{
    private static readonly DiscoveryResourceIdentity Audit =
        new(DiscoveryResourceKind.Category, "@Audit");
    private static readonly DiscoveryResourceIdentity UnsafeMembers =
        new(DiscoveryResourceKind.Section, "Unsafe Members");
    private static readonly DiscoveryResourceIdentity LibraryMetrics =
        new(DiscoveryResourceKind.Section, "Library Metrics");
    private static readonly DiscoveryResourceIdentity Undeclared =
        new(DiscoveryResourceKind.Section, "Undeclared Section");
    private static readonly DiscoveryResourceIdentity OrdinalColumn =
        new(
            DiscoveryResourceKind.Item,
            "Ordinal",
            "Unsafe Members",
            "column");

    [Fact]
    public void SectionName_ReachesTheStructuralSection()
    {
        CapabilityCatalogSearchDocument document = Search("unsafe");

        Assert.Equal(
            CapabilityCatalogSearch.SimilarityThreshold,
            document.SimilarityThreshold);
        CapabilityCatalogSearchResult result = document.Results[0];
        Assert.Equal(1.0, result.Similarity);
        Assert.Equal("Unsafe", result.MatchedTerm);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.OwnerIdentity,
            result.MatchSource);
        Assert.Null(result.ResourceIdentity);
        Assert.Equal(
            new CapabilityCatalogSearchStructuralIdentity(
                "library",
                DiscoveryResourceKind.Section,
                "Unsafe Members"),
            result.StructuralIdentity);
        Assert.Equal(
            CapabilityCatalogSearchResourceKind.Section,
            result.ResourceKind);
        Assert.Equal("Unsafe Members", result.ResourceName);
        Assert.Empty(result.CanonicalKeys);
        Assert.Equal("library/sections/unsafe-members", result.ResourcePath);
        Assert.Equal(
            "library",
            Assert.Single(result.OwningRoutes).ResourcePath);
        CapabilityCatalogSearchBinding binding =
            Assert.Single(result.ProductionBindings);
        Assert.Equal("library -S", binding.Gesture);
        Assert.Equal(InspectionConsumerKind.Cli, binding.ConsumerKind);
        Assert.Equal("library", binding.ResourcePath);
    }

    [Fact]
    public void CategoryName_ReachesTheStructuralCategory()
    {
        CapabilityCatalogSearchResult result = Search("audit").Results[0];

        Assert.Equal(
            CapabilityCatalogSearchResourceKind.Category,
            result.ResourceKind);
        Assert.Equal(
            new CapabilityCatalogSearchStructuralIdentity(
                "library",
                DiscoveryResourceKind.Category,
                "@Audit"),
            result.StructuralIdentity);
        Assert.Equal("library/categories/audit", result.ResourcePath);
    }

    [Fact]
    public void DescriptionSummaryParticipates()
    {
        CapabilityCatalogSearchResult result =
            Assert.Single(Search("pointers").Results);

        Assert.Equal("Unsafe Members", result.ResourceName);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.Summary,
            result.MatchSource);
    }

    [Theory]
    [InlineData("zebra")]
    [InlineData("quokka")]
    [InlineData("wombat")]
    [InlineData("ordinal")]
    public void DescriptionBasisEmptyBoundaryAndColumnsDoNotParticipate(
        string text)
    {
        CapabilityCatalogSearchDocument document = Search(text);

        Assert.Equal(0, document.MatchCount);
        Assert.Empty(document.Results);
    }

    [Fact]
    public void Phrase_AveragesTheBestScoreOfEachWord()
    {
        CapabilityCatalogSearchDocument document = Search("unsafe code");

        Assert.Equal(
            CapabilityCatalogSearch.PhraseThreshold,
            document.SimilarityThreshold);
        CapabilityCatalogSearchResult result = Assert.Single(document.Results);
        Assert.Equal("Unsafe Members", result.ResourceName);
        Assert.Equal(0.5, result.Similarity);
        Assert.Equal("Unsafe", result.MatchedTerm);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.OwnerIdentity,
            result.MatchSource);
        Assert.True(result.IsSegment);
    }

    [Fact]
    public void PhraseWord_InTheResourceHeadingOutweighsASummaryWord()
    {
        CapabilityCatalogSearchResult[] results =
            [.. Search("members volume").Results];

        Assert.Equal(
            ["Unsafe Members", "Library Metrics"],
            results.Select(static result => result.ResourceName));
        Assert.Equal(0.5, results[0].Similarity);
        // The summary word counts at the context weight 1/1.5, averaged
        // over two words.
        Assert.Equal(0.333, results[1].Similarity);
        Assert.Equal("volume", results[1].MatchedTerm);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.Summary,
            results[1].MatchSource);
    }

    [Fact]
    public void PhraseWord_InAScopingPathSegmentIsContext()
    {
        CapabilityCatalogSearchResult[] results =
            [.. Search("unsafe library").Results];

        // `library` scopes `library/sections/unsafe-members` but names
        // `library/sections/library-metrics`.
        Assert.Equal(
            [
                ("Unsafe Members", 0.833),
                ("Library Metrics", 0.5),
            ],
            results
                .Take(2)
                .Select(static result =>
                    (result.ResourceName, result.Similarity)));
        Assert.All(
            results.Skip(2),
            static result => Assert.Equal(0.333, result.Similarity));
    }

    [Fact]
    public void Phrase_IgnoresFunctionWordsAndShortWords()
    {
        CapabilityCatalogSearchDocument document = Search("is the unsafe");

        Assert.Equal(
            CapabilityCatalogSearch.PhraseThreshold,
            document.SimilarityThreshold);
        CapabilityCatalogSearchResult result = Assert.Single(document.Results);
        Assert.Equal("Unsafe Members", result.ResourceName);
        Assert.Equal(1.0, result.Similarity);
    }

    [Fact]
    public void TextWithNoSearchWord_UsesTheWholeTextRule()
    {
        CapabilityCatalogSearchDocument document = Search("is it");

        Assert.Equal(
            CapabilityCatalogSearch.SimilarityThreshold,
            document.SimilarityThreshold);
        Assert.Equal(0, document.MatchCount);
    }

    [Fact]
    public void PhraseEqualToATerm_MatchesTheCompleteTerm()
    {
        CapabilityCatalogSearchResult result =
            Search("unsafe members").Results[0];

        Assert.Equal(1.0, result.Similarity);
        Assert.Equal("Unsafe Members", result.MatchedTerm);
        Assert.False(result.IsSegment);
    }

    [Fact]
    public void PhraseBelowThePhraseThreshold_IsCompleteEmpty()
    {
        CapabilityCatalogSearchDocument document =
            Search("unsafe alpha bravo charlie delta");

        Assert.Equal(0, document.MatchCount);
        Assert.Empty(document.Results);
        Assert.Equal(
            CapabilityCatalogSearch.PhraseThreshold,
            document.SimilarityThreshold);
    }

    [Fact]
    public void PopulationIsEverySectionAndCategoryAndNoItem()
    {
        CapabilityCatalogSearchDocument document = Search("unsafe");

        // One category and three sections; the column item, the catalog
        // root, and the navigation collections are not candidates.
        Assert.Equal(4, document.CandidateResourceCount);
    }

    [Fact]
    public void StructuralResultSerializesItsOneOfIdentity()
    {
        CapabilityCatalogSearchResult result = Search("unsafe").Results[0];

        using JsonDocument json = JsonDocument.Parse(
            JsonSerializer.Serialize(
                result,
                CapabilityCatalogSearchJsonContext.Default
                    .CapabilityCatalogSearchResult));

        JsonElement root = json.RootElement;
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("resource_identity").ValueKind);
        JsonElement structural = root.GetProperty("structural_identity");
        Assert.Equal("library", structural.GetProperty("catalog").GetString());
        Assert.Equal("Section", structural.GetProperty("kind").GetString());
        Assert.Equal(
            "Unsafe Members",
            structural.GetProperty("name").GetString());
        Assert.Equal("Section", root.GetProperty("resource_kind").GetString());
    }

    [Fact]
    public void EveryCandidatePathResolvesInTheStructuralExplanation()
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();
        ResourceExplanationCatalog explanation =
            ResourceExplanationCatalog.CreateStructural(document, paths);

        CapabilityCatalogSearchDocument search =
            CapabilityCatalogSearch.Search(
                    EmptyCapabilities(),
                    EmptyExplanation(),
                    [Admit(document, paths)],
                    // The catalog route name is a term of every candidate.
                    new("library", CapabilityCatalogSearchRequest
                        .MaximumResultLimit))
                .Content;

        Assert.Equal(4, search.Results.Length);
        foreach (CapabilityCatalogSearchResult result in search.Results)
        {
            Assert.IsType<ResourcePathResolution.Resolved>(
                explanation.Resolve(result.ResourcePath));
        }
    }

    [Fact]
    public void StructuralResourceRequiresAPathRegistration()
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            Search(
                Admit(
                    document,
                    [
                        .. paths.Where(static path =>
                            path.Identity != UnsafeMembers),
                    ]),
                "unsafe"));
        Assert.Contains("has no path registration", exception.Message);
    }

    [Fact]
    public void StructuralResourceHasOnePathRegistration()
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            Search(
                Admit(
                    document,
                    [
                        .. paths,
                        new(
                            UnsafeMembers,
                            new ResourcePath("library/sections/unsafe")),
                    ]),
                "unsafe"));
        Assert.Contains("more than one path registration", exception.Message);
    }

    [Fact]
    public void StructuralPathRegistrationRequiresADiscoveryResource()
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            Search(
                Admit(
                    document,
                    [
                        .. paths,
                        new(
                            new DiscoveryResourceIdentity(
                                DiscoveryResourceKind.Section,
                                "Phantom"),
                            new ResourcePath("library/sections/phantom")),
                    ]),
                "unsafe"));
        Assert.Contains("no Discovery resource", exception.Message);
    }

    [Fact]
    public void StructuralPathMustBeUnderTheCatalogRoot()
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            Search(
                Admit(
                    document,
                    [
                        .. paths.Select(static path =>
                            path.Identity == UnsafeMembers
                                ? new StructuralResourcePathRegistration(
                                    path.Identity,
                                    new ResourcePath(
                                        "package/sections/unsafe-members"))
                                : path),
                    ]),
                "unsafe"));
        Assert.Contains("is not under catalog root", exception.Message);
    }

    [Fact]
    public void StructuralCatalogIsAdmittedOnce()
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CapabilityCatalogSearch.Search(
                EmptyCapabilities(),
                EmptyExplanation(),
                [Admit(document, paths), Admit(document, paths)],
                new("unsafe")));
        Assert.Contains("more than once", exception.Message);
    }

    [Fact]
    public void StructuralRoutePathMustBeTheCatalogRoot()
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            Admit(document, paths, root: "package"));
        Assert.Contains("is not the root", exception.Message);
    }

    [Fact]
    public void StructuralRouteAndBindingShareTheCatalogRoot()
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();

        Assert.Throws<ArgumentException>(() =>
            new CapabilityCatalogSearchStructuralCatalog(
                document,
                paths,
                new("test/library-catalog", "library", "library"),
                [
                    Binding(
                        "library -S",
                        [Audit, UnsafeMembers, LibraryMetrics, Undeclared],
                        root: "package"),
                ]));
    }

    [Fact]
    public void StructuralResource_CarriesOnlyTheBindingsThatAddressIt()
    {
        // A catalog two command views project admits one binding per view;
        // a section only the second view selects carries only its binding.
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();
        CapabilityCatalogSearchStructuralCatalog catalog = new(
            document,
            paths,
            new("test/library-catalog", "library", "library"),
            [
                Binding(
                    "library -S",
                    [Audit, UnsafeMembers, LibraryMetrics, Undeclared]),
                Binding(
                    "library address -S",
                    [UnsafeMembers],
                    identity: "test/library-address-sections"),
            ]);

        CapabilityCatalogSearchResult unsafeMembers =
            Search(catalog, "unsafe").Results[0];
        Assert.Equal(
            ["library -S", "library address -S"],
            unsafeMembers.ProductionBindings.Select(
                static binding => binding.Gesture));
        CapabilityCatalogSearchResult metrics =
            Search(catalog, "metrics").Results[0];
        Assert.Equal(
            "library -S",
            Assert.Single(metrics.ProductionBindings).Gesture);
    }

    [Fact]
    public void StructuralResourceWithNoBinding_IsRejected()
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();
        CapabilityCatalogSearchStructuralCatalog catalog = new(
            document,
            paths,
            new("test/library-catalog", "library", "library"),
            [Binding("library -S", [Audit, UnsafeMembers, LibraryMetrics])]);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            Search(catalog, "unsafe"));
        Assert.Contains("has no binding", exception.Message);
    }

    [Fact]
    public void BindingOverAnUnadmittedResource_IsRejected()
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();
        CapabilityCatalogSearchStructuralCatalog catalog = new(
            document,
            paths,
            new("test/library-catalog", "library", "library"),
            [
                Binding(
                    "library -S",
                    [
                        Audit,
                        UnsafeMembers,
                        LibraryMetrics,
                        Undeclared,
                        new DiscoveryResourceIdentity(
                            DiscoveryResourceKind.Section,
                            "Absent"),
                    ]),
            ]);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            Search(catalog, "unsafe"));
        Assert.Contains("does not admit", exception.Message);
    }

    private static CapabilityCatalogSearchDocument Search(string text)
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();
        return Search(Admit(document, paths), text);
    }

    private static CapabilityCatalogSearchDocument Search(
        CapabilityCatalogSearchStructuralCatalog catalog,
        string text) =>
        CapabilityCatalogSearch.Search(
                EmptyCapabilities(),
                EmptyExplanation(),
                [catalog],
                new(text))
            .Content;

    private static InspectionCapabilityCatalog EmptyCapabilities() =>
        InspectionCapabilityCatalog.Create([]);

    // Search resolves only capability paths in its explanation catalog;
    // with no capabilities, any catalog serves.
    private static ResourceExplanationCatalog EmptyExplanation()
    {
        (DiscoveryDocument document,
            StructuralResourcePathRegistration[] paths) = Structural();
        return ResourceExplanationCatalog.CreateStructural(document, paths);
    }

    private static CapabilityCatalogSearchStructuralCatalog Admit(
        DiscoveryDocument document,
        IEnumerable<StructuralResourcePathRegistration> paths,
        string root = "library") =>
        new(
            document,
            paths,
            new($"test/{root}-catalog", root, root),
            [
                Binding(
                    "library -S",
                    [Audit, UnsafeMembers, LibraryMetrics, Undeclared],
                    root),
            ]);

    private static CapabilityCatalogSearchStructuralBinding Binding(
        string gesture,
        IEnumerable<DiscoveryResourceIdentity> resources,
        string root = "library",
        string? identity = null) =>
        new(
            new(
                identity ?? $"test/{root}-sections",
                "test CLI",
                InspectionConsumerKind.Cli,
                gesture,
                root),
            resources);

    // The fixture's Discovery projection and its path registrations: the
    // inputs a host creates its structural explanation catalog from.
    private static (
        DiscoveryDocument Document,
        StructuralResourcePathRegistration[] Paths) Structural()
    {
        const string root = "library";
        var document = new DiscoveryDocument(
            root,
            [
                new DiscoveryResource(
                    Audit,
                    members: [UnsafeMembers, LibraryMetrics]),
                new DiscoveryResource(
                    UnsafeMembers,
                    members: [OrdinalColumn],
                    outputModes: [DiscoveryOutputMode.Markdown],
                    description: new SectionDescription(
                        "Members whose bodies use pointers.",
                        "Compiled zebra bodies.",
                        "No quokka was found.",
                        "Not a wombat verdict.")),
                new DiscoveryResource(OrdinalColumn),
                new DiscoveryResource(
                    LibraryMetrics,
                    outputModes: [DiscoveryOutputMode.Markdown],
                    description: new SectionDescription(
                        "Implementation volume.",
                        "Compiled method bodies.",
                        "The library has no method bodies.")),
                new DiscoveryResource(
                    Undeclared,
                    outputModes: [DiscoveryOutputMode.Markdown]),
            ],
            [Audit, UnsafeMembers, LibraryMetrics, Undeclared],
            new DiscoverySelection(
                isCatalog: true,
                addressedResources: [],
                rows: [Audit, UnsafeMembers, LibraryMetrics, Undeclared]));
        return (
            document,
            [
                new(Audit, new ResourcePath($"{root}/categories/audit")),
                new(
                    UnsafeMembers,
                    new ResourcePath($"{root}/sections/unsafe-members")),
                new(
                    OrdinalColumn,
                    new ResourcePath(
                        $"{root}/sections/unsafe-members/items/column/ordinal")),
                new(
                    LibraryMetrics,
                    new ResourcePath($"{root}/sections/library-metrics")),
                new(
                    Undeclared,
                    new ResourcePath($"{root}/sections/undeclared-section")),
            ]);
    }
}
