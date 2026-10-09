using System.Text.Json;
using DotnetInspector.Sections;
using QuerySpace.Explanation;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Sections.Tests;

public sealed class ResourceExplanationDatasetTests
{
    [Fact]
    public void QualifiedRelations_DistinguishResourceTypesWithTheSameLocalName()
    {
        var schema = new ExplanationSchemaIdentity(new("test"), "schema");
        string first = ResourceExplanationDataset.RelationUri(new(new(schema, "first"), "members"));
        string second = ResourceExplanationDataset.RelationUri(new(new(schema, "second"), "members"));
        Assert.NotEqual(first, second);
        Assert.True(Uri.TryCreate(first, UriKind.Absolute, out _));
    }

    [Fact]
    public void PartialCoverage_CannotBecomeANegativeMembershipAssertion()
    {
        var identity = new VocabularyIdentity(ProductVocabularyComposition.Catalog, "test.flags");
        var map = new VocabularyMapDefinition(new(identity, "flag"), "Flag", "Partially observed flag.",
            new VocabularyMapTarget.Scalar(ExplanationScalarKind.Boolean),
            VocabularyMapCardinality.OptionalOne, VocabularyMapCoverage.Partial);
        ResourceExplanationCatalog catalog = Catalog(new(identity, "Flags", "Test flags.", [map],
            [new(new(identity, "unknown"), "Unknown", null, [])]));
        var selected = ResourceExplanationDataset.Create(catalog, Resolve(catalog, identity.Value));
        Assert.Contains("complete property coverage", Assert.Throws<InvalidOperationException>(
            () => selected.ToJson(path => path.Value)).Message);
    }

    [Fact]
    public void OptionalBooleanWithoutObservation_CannotBecomeFalse()
    {
        var identity = new VocabularyIdentity(ProductVocabularyComposition.Catalog, "test.flags");
        var map = VocabularyMapDefinition.Scalar(identity, "flag", "Flag", "Optional flag.",
            ExplanationScalarKind.Boolean, VocabularyMapCardinality.OptionalOne);
        ResourceExplanationCatalog catalog = Catalog(new(identity, "Flags", "Test flags.", [map],
            [new(new(identity, "absent"), "Absent", null, [new(map)])]));
        var selected = ResourceExplanationDataset.Create(catalog, Resolve(catalog, identity.Value));
        Assert.Contains("one available observation", Assert.Throws<InvalidOperationException>(
            () => selected.ToJson(path => path.Value)).Message);
    }

    [Fact]
    public void OversizedSelection_FailsInsteadOfEmittingPartialTables()
    {
        var identity = new VocabularyIdentity(ProductVocabularyComposition.Catalog, "test.large");
        ResourceExplanationCatalog catalog = Catalog(new(identity, "Large", "Bounded test.", [],
            Enumerable.Range(0, ResourceExplanationRequest.HostResourceLimit)
                .Select(i => new VocabularyTerm(new(identity, "v" + i), "Value " + i, null, []))));
        Assert.Contains("no partial dataset", Assert.Throws<InvalidOperationException>(
            () => ResourceExplanationDataset.Create(catalog, Resolve(catalog, identity.Value))).Message);
    }

    [Fact]
    public void EmptyPositiveSet_IsCompleteAndDoesNotEraseUnavailableSummary()
    {
        var identity = new VocabularyIdentity(ProductVocabularyComposition.Catalog, "test.flags");
        var map = VocabularyMapDefinition.Scalar(identity, "flag", "Flag", "Observed flag.",
            ExplanationScalarKind.Boolean);
        ResourceExplanationCatalog catalog = Catalog(new(identity, "Flags", "Test flags.", [map],
            [new(new(identity, "off"), "Off", null, [new(map, ExplanationValue.Boolean(false))])]));
        JsonElement data = ResourceExplanationDataset.Create(catalog, Resolve(catalog, identity.Value))
            .ToJson(path => path.Value);
        JsonElement vocabulary = data.GetProperty("vocabularies").GetProperty(identity.Value);
        Assert.Empty(vocabulary.GetProperty("property_sets").GetProperty("flag").EnumerateArray());
        JsonElement value = vocabulary.GetProperty("values")[0];
        Assert.Equal("off", value.GetProperty("id").GetString());
        Assert.False(value.TryGetProperty("summary", out _));
        Assert.Equal("Absent", value.GetProperty("fact_states").GetProperty("summary")
            .GetProperty("state").GetString());
    }

    private static ResourceExplanationCatalog Catalog(VocabularyDefinition vocabulary) =>
        ResourceExplanationCatalog.CreateVocabularies(ProductVocabularyComposition.Compose(
            [new ProductVocabularyContribution(vocabulary, "test.input")]));

    private static ResourcePathResolution.Resolved Resolve(ResourceExplanationCatalog catalog, string id) =>
        Assert.IsType<ResourcePathResolution.Resolved>(catalog.Resolve("vocabularies/" + id));
}
