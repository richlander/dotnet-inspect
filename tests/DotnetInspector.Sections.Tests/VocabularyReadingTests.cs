using System.Globalization;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using QuerySpace.Explanation;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Sections.Tests;

public sealed class VocabularyReadingTests
{
    [Fact]
    public void ProductReading_PreservesEveryDeclaredValueAndMapObservation()
    {
        var snapshot = ProductSnapshot();
        var owner = new VocabularyExplanation(snapshot);
        foreach (var declared in snapshot.Vocabularies.Where(v =>
            v.Identity.Value != ProductVocabularyComposition.SectionsId))
        {
            var dataset = owner.SelectData(ResourceExplanationCatalog.VocabularyPath(declared.Identity.Value));
            JsonElement data = dataset.ToJson(path => path.Value);
            JsonElement hal = dataset.ToJson(path => path.Value, hal: true);
            AssertEquivalent(data, hal);
            JsonElement vocabulary = data.GetProperty("vocabularies").GetProperty(declared.Identity.Value);
            Assert.Equal(declared.Terms.Select(term => term.Identity.Value),
                vocabulary.GetProperty("value_order").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(declared.Maps.Select(map => map.Identity.Value),
                vocabulary.GetProperty("map_order").EnumerateArray().Select(value => value.GetString()));
            foreach (var term in declared.Terms)
            {
                JsonElement row = vocabulary.GetProperty("values").GetProperty(term.Identity.Value);
                Assert.Equal(term.DisplayLabel, row.TryGetProperty("name", out var name)
                    ? name.GetString() : term.Identity.Value);
                Assert.Equal(term.Summary, row.TryGetProperty("summary", out var summary) ? summary.GetString() : null);
                foreach (var map in declared.Maps)
                {
                    JsonElement descriptor = vocabulary.GetProperty("maps").GetProperty(map.Identity.Value);
                    Assert.Equal(map.DisplayLabel, descriptor.GetProperty("name").GetString());
                    Assert.Equal(map.Summary, descriptor.GetProperty("summary").GetString());
                    term.TryGetValues(map.Identity, out var observations);
                    if (map.Target is VocabularyMapTarget.Scalar { Kind: ExplanationScalarKind.Boolean })
                    {
                        bool expected = Assert.IsType<ExplanationValue.Scalar>(Assert.Single(observations)).Value.Boolean!.Value;
                        Assert.Equal(expected, vocabulary.GetProperty("property_sets").GetProperty(map.Identity.Value)
                            .EnumerateArray().Any(value => value.GetString() == term.Identity.Value));
                    }
                    else if (map.Target is VocabularyMapTarget.Scalar { Kind: ExplanationScalarKind.Text }
                        && map.Cardinality == VocabularyMapCardinality.OptionalOne)
                    {
                        string? expected = observations.IsEmpty ? null
                            : Assert.IsType<ExplanationValue.Scalar>(Assert.Single(observations)).Value.Text;
                        string? actual = vocabulary.GetProperty("property_groups").GetProperty(map.Identity.Value)
                            .EnumerateObject().Where(group => group.Value.EnumerateArray()
                                .Any(value => value.GetString() == term.Identity.Value))
                            .Select(group => group.Name).SingleOrDefault();
                        Assert.Equal(expected, actual);
                    }
                    else
                    {
                        JsonElement actual = row.GetProperty(map.Identity.Value);
                        string[] expected = observations.Select(value => value switch
                        {
                            ExplanationValue.VocabularyTerm target => target.Identity.Value,
                            ExplanationValue.Scalar scalar => scalar.Value.Text
                                ?? scalar.Value.Integer!.Value.ToString(CultureInfo.InvariantCulture),
                            _ => throw new InvalidOperationException(),
                        }).ToArray();
                        Assert.Equal(expected, actual.ValueKind == JsonValueKind.Array
                            ? actual.EnumerateArray().Select(value => value.GetString()).ToArray()
                            : actual.ValueKind == JsonValueKind.Null ? [] : [actual.GetString()]);
                    }
                }
            }
        }
    }

    [Fact]
    public void OneChoice_EmbedsOnlyItsExactTierAndLeavesOtherChoicesUnknown()
    {
        var owner = new VocabularyExplanation(ProductSnapshot());
        var selection = owner.SelectData(new("vocabularies/csharp.style-choices"), ["slot-local-names"]);
        JsonElement direct = selection.ToJson(path => path.Value);
        JsonElement hal = selection.ToJson(path => path.Value, hal: true);
        AssertEquivalent(direct, hal);
        Assert.Equal(4, selection.Resources.Length); // two vocabularies, two values
        JsonElement tables = direct.GetProperty("vocabularies");
        JsonElement choices = tables.GetProperty("csharp.style-choices");
        JsonElement choice = Assert.Single(choices.GetProperty("values").EnumerateObject()).Value;
        Assert.Equal("slot-local-names", choice.GetProperty("option").GetString());
        Assert.Equal("true", choice.GetProperty("value").GetString());
        Assert.Equal("Synthesis", choice.GetProperty("tier").GetString());
        Assert.False(choices.GetProperty("values").TryGetProperty("qualify-field-access", out _));
        Assert.All(choices.GetProperty("property_sets").EnumerateObject(), property =>
            Assert.Empty(property.Value.EnumerateArray()));
        Assert.Empty(choices.GetProperty("property_groups").GetProperty("conflict_group").EnumerateObject());
        JsonElement tiers = tables.GetProperty("csharp.style-tiers");
        Assert.Equal(4, tiers.GetProperty("catalog_values").GetInt32());
        Assert.Equal("Synthesis", Assert.Single(tiers.GetProperty("values").EnumerateObject()).Name);
        Assert.Contains("exact source identity", tiers.GetProperty("values").GetProperty("Synthesis")
            .GetProperty("summary").GetString());
        Assert.Equal("Complete", hal.GetProperty("scope").GetProperty("completeness").GetString());
        Assert.Single(hal.GetProperty("_links").EnumerateObject());
        Assert.True(hal.GetProperty("_links").TryGetProperty("collection", out _));
    }

    [Fact]
    public void NativeIdentitySelection_IsExactDeduplicatedAndOwnerOrdered()
    {
        var owner = new VocabularyExplanation(ProductSnapshot());
        var path = new ResourcePath("vocabularies/csharp.body-kinds");
        JsonElement data = owner.SelectData(path, ["ObjectCreationExpression", "AddressExpression", "ObjectCreationExpression"])
            .ToJson(p => p.Value);
        Assert.Equal(["AddressExpression", "ObjectCreationExpression"], data.GetProperty("vocabularies")
            .GetProperty("csharp.body-kinds").GetProperty("value_order").EnumerateArray().Select(value => value.GetString()));
        foreach (string[] invalid in new[] { Array.Empty<string>(), new[] { "objectcreationexpression" }, new[] { "Object*" } })
            Assert.Throws<InvalidOperationException>(() => owner.SelectData(path, invalid));
    }

    [Fact]
    public void OptionalGroupsAndDefaults_DescribeSelectedMembership()
    {
        var owner = new VocabularyExplanation(ProductSnapshot());
        JsonElement choice = owner.SelectData(new("vocabularies/csharp.style-choices"),
            ["guarded-boolean-return-style:branchless"]).ToJson(p => p.Value)
            .GetProperty("vocabularies").GetProperty("csharp.style-choices");
        Assert.Equal("guarded-boolean-return-style:branchless", Assert.Single(choice.GetProperty("property_groups")
            .GetProperty("conflict_group").GetProperty("guarded-boolean-return-style").EnumerateArray()).GetString());
        JsonElement accessibility = owner.SelectData(new("vocabularies/api.accessibility"), ["private"])
            .ToJson(p => p.Value).GetProperty("vocabularies").GetProperty("api.accessibility");
        Assert.Empty(accessibility.GetProperty("defaults").EnumerateArray());
        Assert.Empty(accessibility.GetProperty("property_sets").GetProperty("default").EnumerateArray());
        Assert.Equal(4, accessibility.GetProperty("catalog_values").GetInt32());
    }

    [Fact]
    public void Narrowing_CanReadOneValueFromAnOtherwiseOversizedVocabulary()
    {
        var id = new VocabularyIdentity(ProductVocabularyComposition.Catalog, "test.large");
        var definition = new VocabularyDefinition(id, "Large", null, [],
            Enumerable.Range(0, ResourceExplanationRequest.HostResourceLimit + 1)
                .Select(i => new VocabularyTerm(new(id, "v" + i), "v" + i, null, [])));
        var owner = new VocabularyExplanation(ProductVocabularyComposition.Compose([new(definition, "test.input")]));
        var selected = owner.SelectData(ResourceExplanationCatalog.VocabularyPath(id.Value), ["v0"]);
        Assert.Equal(2, selected.Resources.Length);
        Assert.Equal(ResourceExplanationRequest.HostResourceLimit + 1, selected.ToJson(p => p.Value)
            .GetProperty("vocabularies").GetProperty(id.Value).GetProperty("catalog_values").GetInt32());
    }

    [Fact]
    public void RecursiveTermContext_ClosesExactValuesAndTerminatesOnCycles()
    {
        var first = new VocabularyIdentity(ProductVocabularyComposition.Catalog, "test.first");
        var second = new VocabularyIdentity(ProductVocabularyComposition.Catalog, "test.second");
        var firstMap = Map(first, second);
        var secondMap = Map(second, first);
        var definitions = new[]
        {
            new VocabularyDefinition(first, "First", null, [firstMap],
                [Term(first, second, firstMap, "a"), Term(first, second, firstMap, "b")]),
            new VocabularyDefinition(second, "Second", null, [secondMap],
                [Term(second, first, secondMap, "a"), Term(second, first, secondMap, "b")]),
        };
        var owner = new VocabularyExplanation(ProductVocabularyComposition.Compose(
            definitions.Select(definition => new ProductVocabularyContribution(definition, "test.input"))));
        var selected = owner.SelectData(ResourceExplanationCatalog.VocabularyPath(first.Value), ["a"]);
        Assert.Equal(4, selected.Resources.Length);
        JsonElement direct = selected.ToJson(path => path.Value);
        AssertEquivalent(direct, selected.ToJson(path => path.Value, hal: true));
        foreach (JsonProperty vocabulary in direct.GetProperty("vocabularies").EnumerateObject())
        {
            Assert.Equal("a", Assert.Single(vocabulary.Value.GetProperty("values").EnumerateObject()).Name);
            Assert.Equal(2, vocabulary.Value.GetProperty("catalog_values").GetInt32());
        }

        static VocabularyMapDefinition Map(VocabularyIdentity owner, VocabularyIdentity target) =>
            new(new(owner, "next"), "Next", "Exact term context.",
                new VocabularyMapTarget.Terms(new VocabularyTermSetReference.Local(target)),
                VocabularyMapCardinality.ExactlyOne, VocabularyMapCoverage.Complete);
        static VocabularyTerm Term(VocabularyIdentity owner, VocabularyIdentity target,
            VocabularyMapDefinition map, string id) =>
            new(new(owner, id), id, null, [new(map, new ExplanationValue.VocabularyTerm(new(target, id)))]);
    }

    private static void AssertEquivalent(JsonElement direct, JsonElement hal)
    {
        Assert.True(JsonElement.DeepEquals(direct.GetProperty("scope"), hal.GetProperty("scope")));
        var vocabularies = hal.GetProperty("_embedded").GetProperty("vocabularies").EnumerateArray().ToArray();
        Assert.Equal(direct.GetProperty("vocabularies").EnumerateObject().Count(), vocabularies.Length);
        foreach (JsonElement vocabulary in vocabularies)
        {
            var source = direct.GetProperty("vocabularies").GetProperty(vocabulary.GetProperty("id").GetString()!);
            foreach (JsonProperty field in source.EnumerateObject().Where(field => field.Name is not ("values" or "value_order")))
                Assert.True(JsonElement.DeepEquals(field.Value, vocabulary.GetProperty(field.Name)), field.Name);
            var values = vocabulary.GetProperty("_embedded").GetProperty("values").EnumerateArray().ToArray();
            Assert.Equal(source.GetProperty("value_order").EnumerateArray().Select(value => value.GetString()),
                values.Select(value => value.GetProperty("id").GetString()));
            foreach (JsonElement value in values)
            {
                var expected = source.GetProperty("values").GetProperty(value.GetProperty("id").GetString()!);
                Assert.Equal(expected.EnumerateObject().Count() + 1, value.EnumerateObject().Count());
                foreach (var field in expected.EnumerateObject())
                    Assert.True(JsonElement.DeepEquals(field.Value, value.GetProperty(field.Name)), field.Name);
            }
        }
    }

    private static VocabularySnapshot ProductSnapshot()
    {
        var catalog = ProductVocabularyComposition.Catalog;
        return ProductVocabularyComposition.Compose([
            new(ApiAccessibilityVocabulary.Declare(catalog), "api.type-inventory", "api.member-inventory"),
            new(StyleOptionVocabularies.DeclareStyleTiers(catalog), "decompiler.style-picker"),
            new(StyleOptionVocabularies.DeclareStyleChoices(catalog), "decompiler.style-picker", "decompiler.render"),
            new(BodyShapeVocabulary.Declare(catalog), "decompiler.body-kind"),
            new(PackageQueryDurableRowVocabulary.Declare(catalog), "package-query.durable-row"),
        ]);
    }
}
