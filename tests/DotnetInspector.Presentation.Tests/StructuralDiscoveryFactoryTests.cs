using System.Collections.Immutable;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Presentation.Tests;

public class StructuralDiscoveryFactoryTests
{
    [Fact]
    public void SharedFactoryPreservesCanonicalResourcesAcrossCategories()
    {
        var schema = new DocumentSchema()
            .Add("Rows", "column", "Name")
            .AddSection("Empty");
        var description = new SectionDescription(
            "Declared rows.", "Metadata.", "No rows.", "Structural only.");
        StructuralDiscoveryProjection projection = StructuralDiscoveryFactory.Create(
            "library", schema,
            new Dictionary<string, string[]>
            {
                [SectionCategoryNames.Library] = ["Rows", "Empty"],
                [SectionCategoryNames.Metadata] = ["Rows", "Unavailable"],
            },
            catalogHiddenSections: null,
            listedCategoryDoors: null,
            sectionCostAnnotations: null,
            OutputCapabilityCatalog.FromShapes(new Dictionary<string, SectionShape>
            {
                ["Rows"] = SectionShape.Table,
                ["Empty"] = SectionShape.Text,
            }),
            sectionCardinalities: new Dictionary<string, SectionCardinalityDeclaration>
            {
                ["Rows"] = SectionCardinalityDeclaration.Inventory,
            },
            sectionDescriptions: new Dictionary<string, SectionDescription>
            {
                ["Rows"] = description,
            });

        DiscoveryResourceIdentity rows = new(DiscoveryResourceKind.Section, "Rows");
        Assert.Single(projection.Document.Resources, resource => resource.Identity == rows);
        Assert.Equal(2, projection.Document.Resources.Count(resource =>
            resource.Identity.Kind == DiscoveryResourceKind.Category
            && resource.Members.Contains(rows)));
        Assert.Same(description, projection.Document.GetResource(rows).Description);
        Assert.Equal(SectionCardinalityKind.Inventory,
            projection.Document.GetResource(rows).Cardinality!.Kind);
        Assert.Equal(projection.Document.Resources.Length, projection.ResourcePaths.Length);
        Assert.Contains(projection.ResourcePaths, registration =>
            registration.Identity == rows && registration.Path.Value == "library/sections/rows");
        Assert.Equal("library/categories/metadata", Assert.Single(projection.ResourcePaths,
            registration => registration.Identity.Name == SectionCategoryNames.Metadata).Path.Value);
        Assert.DoesNotContain(projection.Document.Resources,
            resource => resource.Identity.Name == "Unavailable");
    }

    [Fact]
    public void AugmentedItemsKeepTheirKindAndOwnerBesideGeneratedColumns()
    {
        StructuralDiscoveryProjection projection = StructuralDiscoveryFactory.Create(
            "library", new DocumentSchema().Add("Rows", "column", "Name"),
            sectionCategories: null, catalogHiddenSections: null,
            listedCategoryDoors: null, sectionCostAnnotations: null,
            new OutputCapabilityCatalog(new Dictionary<string, SectionOutputCapabilities>()),
            additionalItems: new Dictionary<string, ImmutableArray<StructuralDiscoveryItem>>
            {
                ["Rows"] = [new("Name", "filterable", "name")],
            });

        DiscoveryResource section = projection.Document.GetResource(
            new(DiscoveryResourceKind.Section, "Rows"));
        Assert.Equal(2, section.Members.Length);
        Assert.All(section.Members, member => Assert.Equal("Rows", member.Section));
        Assert.Equal(["column", "filterable"], section.Members.Select(member => member.ItemKind));
        Assert.Equal(2, projection.ResourcePaths.Count(registration =>
            registration.Path.Value.EndsWith("/name", StringComparison.Ordinal)));
    }

    [Fact]
    public void CardinalityOutsideTheSchemaFailsRatherThanDisappearing()
    {
        Assert.Throws<ArgumentException>(() => StructuralDiscoveryFactory.Create(
            "library", new DocumentSchema().AddSection("Known"),
            sectionCategories: null, catalogHiddenSections: null,
            listedCategoryDoors: null, sectionCostAnnotations: null,
            new OutputCapabilityCatalog(new Dictionary<string, SectionOutputCapabilities>()),
            sectionCardinalities: new Dictionary<string, SectionCardinalityDeclaration>
            {
                ["Unknown"] = SectionCardinalityDeclaration.Inventory,
            }));
    }
}
