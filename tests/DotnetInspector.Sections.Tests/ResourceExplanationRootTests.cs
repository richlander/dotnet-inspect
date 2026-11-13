using DotnetInspector.Sections;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections.Tests;

public sealed class ResourceExplanationRootTests
{
    private static readonly ResourcePath RootPath = new("root");

    [Fact]
    public void Root_ProjectsDeclarationsAndReachesTheirDestinations()
    {
        ResourceExplanationRootComposition composition =
            ResourceExplanationCatalog.ComposeRoot(
                Composed("library", "package"),
                RootPath,
                [
                    Subject("library", "library"),
                    Subject("package", "package"),
                    Operation("diff", ["library", "package"]),
                ]);

        Assert.True(composition.IsPublishable);
        Assert.Empty(composition.Unreachable);
        ResourceExplanationCatalog catalog = composition.Catalog;
        Assert.Equal(
            ["subject-kinds", "operations", "references"],
            Targets(catalog, "root", "root-collection")
                .Select(path => path.Split('/')[^1]));
        Assert.Equal(
            ["subject-kinds/library", "subject-kinds/package"],
            Targets(
                catalog,
                "subject-kinds",
                "collection-routing-entry"));
        Assert.Equal(
            ["library"],
            Targets(
                catalog,
                "subject-kinds/library",
                "routing-destination-catalog"));
        Assert.Equal(
            ["subject-kinds/library", "subject-kinds/package"],
            Targets(
                catalog,
                "operations/diff",
                "accepts-subject-kind"));
        Assert.Equal(
            ["operations/diff"],
            Targets(
                catalog,
                "subject-kinds/package",
                "accepted-by-operation"));
    }

    [Fact]
    public void Root_ReportsResourcesNoDeclarationReaches()
    {
        // A catalog no declaration routes to stays valid and resolvable; only
        // the root is withheld, and the report names every resource it misses.
        ResourceExplanationRootComposition composition =
            ResourceExplanationCatalog.ComposeRoot(
                Composed("library", "package"),
                RootPath,
                [Subject("library", "library")]);

        Assert.False(composition.IsPublishable);
        Assert.Contains(
            composition.Unreachable,
            static path => path.Value == "package");
        Assert.Contains(
            composition.Unreachable,
            static path => path.Value == "package/sections/summary");
        Assert.DoesNotContain(
            composition.Unreachable,
            static path => path.Value.StartsWith(
                "library",
                StringComparison.Ordinal));
        Assert.True(composition.Catalog.TryResolveExact(
            new ResourcePath("package/sections/summary"),
            out _));
    }

    [Fact]
    public void NotYetExplainableEntry_IsListedWithItsState()
    {
        ResourceExplanationRootComposition composition =
            ResourceExplanationCatalog.ComposeRoot(
                Composed("library"),
                RootPath,
                [
                    Subject("library", "library"),
                    new(
                        ResourceExplanationRoutingRole.SubjectKind,
                        "workspace",
                        "Workspace",
                        "Test owner",
                        "An open inspection workspace.",
                        ["Which libraries does this workspace hold?"],
                        [],
                        notYetExplainable:
                            "No installed catalog explains workspaces yet."),
                ]);

        Assert.True(composition.IsPublishable);
        Assert.Contains(
            "subject-kinds/workspace",
            Targets(
                composition.Catalog,
                "subject-kinds",
                "collection-routing-entry"));
        Assert.Empty(Targets(
            composition.Catalog,
            "subject-kinds/workspace",
            "routing-destination-catalog"));
    }

    [Fact]
    public void Destination_ProjectsThroughTheRelationshipOfItsType()
    {
        // A collection destination routes through the collection
        // relationship, never through the catalog one.
        ResourceExplanationCatalog composed = Composed("library");
        Assert.True(composed.TryResolveExact(
            new ResourcePath("library/sections"),
            out ResourcePathResolution.Resolved? sections));

        ResourceExplanationRootComposition composition =
            ResourceExplanationCatalog.ComposeRoot(
                composed,
                RootPath,
                [
                    new(
                        ResourceExplanationRoutingRole.SubjectKind,
                        "library",
                        "Library",
                        "Test owner",
                        "A library.",
                        ["What is in it?"],
                        [
                            ResourceExplanationCatalog.StructuralCatalogKey(
                                "library"),
                            sections.Key,
                        ]),
                ]);

        Assert.Equal(
            ["library"],
            Targets(
                composition.Catalog,
                "subject-kinds/library",
                "routing-destination-catalog"));
        Assert.Equal(
            ["library/sections"],
            Targets(
                composition.Catalog,
                "subject-kinds/library",
                "routing-destination-collection"));
    }

    [Fact]
    public void DuplicateIdentityWithinARole_IsAnInvalidRegistry()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.ComposeRoot(
                Composed("library"),
                RootPath,
                [Subject("library", "library"), Subject("library", "library")]));
        Assert.Contains("declared more than once", exception.Message);
    }

    [Fact]
    public void SameIdentityInDifferentRoles_IsAdmitted()
    {
        ResourceExplanationRootComposition composition =
            ResourceExplanationCatalog.ComposeRoot(
                Composed("library"),
                RootPath,
                [
                    Subject("library", "library"),
                    Operation("library", ["library"]),
                ]);

        Assert.True(composition.Catalog.TryResolveExact(
            new ResourcePath("operations/library"),
            out _));
    }

    [Fact]
    public void UnresolvedDestination_IsAnInvalidRegistry()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.ComposeRoot(
                Composed("library"),
                RootPath,
                [Subject("package", "package")]));
        Assert.Contains("is not an installed resource", exception.Message);
    }

    [Fact]
    public void DestinationOfAnUnroutableType_IsAnInvalidRegistry()
    {
        ResourceExplanationCatalog composed = Composed("library");
        Assert.True(composed.TryResolveExact(
            new ResourcePath("library/sections/summary"),
            out ResourcePathResolution.Resolved? section));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.ComposeRoot(
                composed,
                RootPath,
                [
                    new(
                        ResourceExplanationRoutingRole.SubjectKind,
                        "library",
                        "Library",
                        "Test owner",
                        "A library.",
                        ["What is in it?"],
                        [section.Key]),
                ]));
        Assert.Contains("no routing destination relationship", exception.Message);
    }

    [Fact]
    public void OperationNamingAnUndeclaredSubjectKind_IsAnInvalidRegistry()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.ComposeRoot(
                Composed("library"),
                RootPath,
                [Subject("library", "library"), Operation("diff", ["package"])]));
        Assert.Contains("undeclared subject kind", exception.Message);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Declaration_HasExactlyOneOfDestinationsAndState(
        bool destination,
        bool state)
    {
        Assert.Throws<ArgumentException>(() =>
            new ResourceExplanationRoutingDeclaration(
                ResourceExplanationRoutingRole.SubjectKind,
                "library",
                "Library",
                "Test owner",
                "A library.",
                ["What is in it?"],
                destination
                    ? [ResourceExplanationCatalog.StructuralCatalogKey("library")]
                    : [],
                notYetExplainable: state ? "Not yet." : null));
    }

    [Fact]
    public void Declaration_RequiresQuestionsAndOperationSubjects()
    {
        Assert.Throws<ArgumentException>(() =>
            new ResourceExplanationRoutingDeclaration(
                ResourceExplanationRoutingRole.SubjectKind,
                "library",
                "Library",
                "Test owner",
                "A library.",
                [],
                [ResourceExplanationCatalog.StructuralCatalogKey("library")]));
        Assert.Throws<ArgumentException>(() =>
            new ResourceExplanationRoutingDeclaration(
                ResourceExplanationRoutingRole.Operation,
                "diff",
                "Diff",
                "Test owner",
                "Compares two versions.",
                ["What changed?"],
                [ResourceExplanationCatalog.StructuralCatalogKey("library")]));
        Assert.Throws<ArgumentException>(() =>
            new ResourceExplanationRoutingDeclaration(
                ResourceExplanationRoutingRole.SubjectKind,
                "library",
                "Library",
                "Test owner",
                "A library.",
                ["What is in it?"],
                [ResourceExplanationCatalog.StructuralCatalogKey("library")],
                acceptedSubjectKinds: ["package"]));
    }

    private static ResourceExplanationRoutingDeclaration Subject(
        string identity,
        string catalog) =>
        new(
            ResourceExplanationRoutingRole.SubjectKind,
            identity,
            identity,
            "Test owner",
            $"The {identity} subject.",
            [$"What is in this {identity}?"],
            [ResourceExplanationCatalog.StructuralCatalogKey(catalog)]);

    private static ResourceExplanationRoutingDeclaration Operation(
        string identity,
        string[] subjectKinds) =>
        new(
            ResourceExplanationRoutingRole.Operation,
            identity,
            identity,
            "Test owner",
            $"The {identity} operation.",
            [$"What does {identity} report?"],
            [ResourceExplanationCatalog.StructuralCatalogKey("library")],
            acceptedSubjectKinds: subjectKinds);

    private static IEnumerable<string> Targets(
        ResourceExplanationCatalog catalog,
        string path,
        string relationship)
    {
        Assert.True(catalog.TryResolveExact(
            new ResourcePath(path),
            out ResourcePathResolution.Resolved? source));
        var paths = catalog.Resources.ToDictionary(
            static resource => resource.Key,
            static resource => resource.Path!.Value);
        return catalog.Relationships
            .Where(candidate =>
                candidate.Source == source.Key
                && candidate.Relationship.Value == relationship)
            .SelectMany(static candidate => candidate.Targets)
            .Select(target => paths[target.Resource])
            .ToArray();
    }

    // One structural catalog per root, each with one category and one
    // section, combined as a host composes its complete catalog.
    private static ResourceExplanationCatalog Composed(params string[] roots) =>
        ResourceExplanationCatalog.Combine(
        [
            .. roots.Select(static root =>
            {
                var category =
                    new DiscoveryResourceIdentity(
                        DiscoveryResourceKind.Category,
                        "@General");
                var section =
                    new DiscoveryResourceIdentity(
                        DiscoveryResourceKind.Section,
                        "Summary");
                var document = new DiscoveryDocument(
                    root,
                    [
                        new DiscoveryResource(category, members: [section]),
                        new DiscoveryResource(
                            section,
                            outputModes: [DiscoveryOutputMode.Markdown]),
                    ],
                    [category, section],
                    new DiscoverySelection(
                        isCatalog: true,
                        addressedResources: [],
                        rows: [category, section]));
                return ResourceExplanationCatalog.CreateStructural(
                    document,
                    [
                        new(
                            category,
                            new ResourcePath($"{root}/categories/general")),
                        new(
                            section,
                            new ResourcePath($"{root}/sections/summary")),
                    ]);
            }),
        ]);
}
