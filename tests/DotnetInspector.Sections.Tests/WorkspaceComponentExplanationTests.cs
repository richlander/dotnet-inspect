using System.Text.Json;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections.Tests;

public sealed class WorkspaceComponentExplanationTests
{
    private const string Declaration = """
        {"contract":"workspace/components/package","schema_version":1,"package_id":"System.Text.Json","version":"10.0.0","framework":"net10.0"}
        """;

    [Fact]
    public void Contract_IsReachableFromRootWithoutEmbeddingItsFieldDefinitions()
    {
        var composition = ResourceExplanationCatalog.ComposeRoot(
            ResourceExplanationCatalog.CreateWorkspaceComponents(), new("root"),
            [ResourceExplanationCatalog.ComponentContractsRoutingDeclaration]);
        Assert.True(composition.IsPublishable);
        Assert.Empty(composition.Unreachable);
        Assert.True(composition.Catalog.TryResolveExact(new("root"), out var resolution));
        var dataset = ResourceExplanationDataset.Create(composition.Catalog, resolution!);
        JsonElement hal = dataset.ToJson(Bind, hal: true);
        AssertEquivalent(dataset.ToJson(Bind), hal);
        Assert.Equal("_links", hal.EnumerateObject().First().Name);
        Assert.DoesNotContain("maximum_values", hal.GetRawText());
        Assert.Contains("component-contracts", hal.GetRawText());
        Assert.Equal(dataset.Resources.Length,
            hal.GetProperty("_embedded").GetProperty("resources").GetArrayLength() + 1);
        AssertNoProjectedAddresses(hal);
    }

    [Fact]
    public void Contract_ProjectsOwnerFieldMeaningsAndSparsePopulationLimits()
    {
        var catalog = ResourceExplanationCatalog.CreateWorkspaceComponents();
        Assert.True(catalog.TryResolveExact(new("workspace/components/package"), out var resolution));
        var dataset = ResourceExplanationDataset.Create(catalog, resolution!);
        JsonElement direct = dataset.ToJson(Bind);
        JsonElement hal = dataset.ToJson(Bind, hal: true);
        AssertEquivalent(direct, hal);
        JsonElement fields = direct.GetProperty("facts").GetProperty("fields");
        var owner = WorkspacePackageComponentExplanation.Schema.ResourceTypes.Single();
        Assert.Equal(owner.Facts.Length, fields.GetArrayLength());
        foreach (var declared in owner.Facts)
        {
            JsonElement field = fields.EnumerateArray().Single(f => f.GetProperty("id").GetString() == declared.Identity.Value);
            Assert.Equal(declared.Meaning, field.GetProperty("meaning").GetString());
            Assert.Equal(declared.Cardinality.ToString(), field.GetProperty("cardinality").GetString());
            Assert.Equal(declared.ValueShape.ToString(), field.GetProperty("shape").GetString());
        }
        Assert.Equal(2, direct.GetProperty("facts").GetProperty("field_limits").GetArrayLength());
        Assert.DoesNotContain("?projection=", hal.GetRawText());
    }

    [Fact]
    public void Standalone_PreservesLocalValuesDefaultsAndUnavailableParentFacts()
    {
        var input = WorkspacePackageComponentExplanation.Parse(Declaration);
        var document = WorkspaceComponentExplanationOperation.Explain(input).Content;
        Assert.Null(document.RequestedPath);
        Assert.Single(document.Resources);
        Assert.Empty(document.Resources[0].Addresses);
        JsonElement direct = ResourceExplanationDataProjection.Create(document, Bind);
        JsonElement hal = ResourceExplanationDataProjection.CreateHal(document, Bind);
        AssertEquivalent(direct, hal);
        Assert.False(hal.GetProperty("_links").TryGetProperty("self", out _));
        Assert.Equal("inspect-resource:/workspace/components/package",
            hal.GetProperty("_links").GetProperty("describedby").GetProperty("href").GetString());
        var facts = direct.GetProperty("facts");
        Assert.Equal("System.Text.Json", facts.GetProperty("package_id").GetString());
        Assert.Equal("10.0.0", facts.GetProperty("version").GetString());
        Assert.Equal("net10.0", facts.GetProperty("framework").GetString());
        Assert.Equal("runtime_identifier", facts.GetProperty("defaulted_fields")[0].GetString());
        foreach (string parentFact in new[] { "component_path", "contexts", "focused" })
        {
            Assert.False(facts.TryGetProperty(parentFact, out _));
            Assert.Equal("Unavailable", direct.GetProperty("fact_states").GetProperty(parentFact)
                .GetProperty("state").GetString());
        }
    }

    [Fact]
    public void ParentQualified_FactsRemainNativeAndSamePathDoesNotJoinDifferentPackets()
    {
        var first = Parent(focused: true);
        var second = Parent(focused: false);
        var firstDocument = WorkspaceComponentExplanationOperation.Explain(
            WorkspacePackageComponentExplanation.Parse(Declaration, first)).Content;
        var secondDocument = WorkspaceComponentExplanationOperation.Explain(
            WorkspacePackageComponentExplanation.Parse(Declaration, second)).Content;
        Assert.NotEqual(firstDocument.Root, secondDocument.Root);
        JsonElement firstData = ResourceExplanationDataProjection.Create(firstDocument, Bind);
        JsonElement secondData = ResourceExplanationDataProjection.Create(secondDocument, Bind);
        Assert.True(firstData.GetProperty("facts").GetProperty("focused").GetBoolean());
        Assert.False(secondData.GetProperty("facts").GetProperty("focused").GetBoolean());
        Assert.Equal("contexts/g0", firstData.GetProperty("facts").GetProperty("contexts")[0].GetString());
        Assert.Equal("packages/system.text.json@10.0.0/net10.0/~",
            firstData.GetProperty("facts").GetProperty("component_path").GetString());
        Assert.False(firstData.TryGetProperty("path", out _));
        AssertEquivalent(firstData, ResourceExplanationDataProjection.CreateHal(firstDocument, Bind));
    }

    [Fact]
    public void ExplicitNull_IsAbsentWithNoDefaultedFieldAndNoRealizedValue()
    {
        var input = WorkspacePackageComponentExplanation.Parse(
            Declaration.Replace("\"version\":\"10.0.0\"", "\"version\":null"));
        var document = WorkspaceComponentExplanationOperation.Explain(input).Content;
        JsonElement data = ResourceExplanationDataProjection.Create(document, Bind);
        Assert.False(data.GetProperty("facts").TryGetProperty("version", out _));
        Assert.Equal("Absent", data.GetProperty("fact_states").GetProperty("version").GetProperty("state").GetString());
        Assert.DoesNotContain(data.GetProperty("facts").GetProperty("defaulted_fields").EnumerateArray(),
            value => value.GetString() == "version");
    }

    [Fact]
    public void RootReading_DoesNotHideBoundedTraversalOutcome()
    {
        var composition = ResourceExplanationCatalog.ComposeRoot(
            ResourceExplanationCatalog.CreateWorkspaceComponents(), new("root"),
            [ResourceExplanationCatalog.ComponentContractsRoutingDeclaration]);
        Assert.True(composition.Catalog.TryResolveExact(new("root"), out var resolution));
        var document = composition.Catalog.Explain(resolution!, new(0, 1, 10)).Content;
        Assert.NotEqual(ResourceExplanationCompleteness.Complete, document.Traversal.Completeness);
        JsonElement data = ResourceExplanationDataProjection.Create(document, Bind);
        AssertEquivalent(data, ResourceExplanationDataProjection.CreateHal(document, Bind));
    }

    [Fact]
    public void Hal_PreservesTruncatedRelationshipTargetsExplicitly()
    {
        var declaration = ResourceExplanationCatalog.ComponentContractsRoutingDeclaration;
        var composition = ResourceExplanationCatalog.ComposeRoot(
            ResourceExplanationCatalog.CreateWorkspaceComponents(), new("root"),
            [declaration, new(ResourceExplanationRoutingRole.Reference, "another-contract-route",
                declaration.Title, declaration.Issuer, declaration.Purpose,
                declaration.Questions, declaration.Destinations)]);
        Assert.True(composition.Catalog.TryResolveExact(new("references"), out var resolution));
        var document = composition.Catalog.Explain(resolution!, new(0, 1, 10, relationshipTargetLimit: 1)).Content;
        Assert.Contains(document.Relationships, edge =>
            edge.TargetCompleteness != ResourceExplanationTargetProjectionCompleteness.Complete);
        AssertEquivalent(ResourceExplanationDataProjection.Create(document, Bind),
            ResourceExplanationDataProjection.CreateHal(document, Bind));
    }

    private static void AssertEquivalent(JsonElement direct, JsonElement hal)
    {
        var embedded = hal.GetProperty("_embedded");
        var data = embedded.GetProperty("data");
        foreach (string field in new[] { "scope", "facts", "fact_states" })
            Assert.True(JsonElement.DeepEquals(direct.GetProperty(field), data.GetProperty(field)));
        Assert.True(JsonElement.DeepEquals(direct.GetProperty("_links"), hal.GetProperty("_links")));
        if (!direct.TryGetProperty("path", out _))
            Assert.True(JsonElement.DeepEquals(direct.GetProperty("identity"), data.GetProperty("identity")));
        if (direct.TryGetProperty("traversal", out var traversal))
            Assert.True(JsonElement.DeepEquals(traversal, data.GetProperty("traversal")));
        foreach (var relation in direct.GetProperty("relationships").EnumerateObject())
        {
            bool exceptional = data.TryGetProperty("relationships", out var outcomes)
                && outcomes.TryGetProperty(relation.Name, out _);
            if (exceptional)
                Assert.True(JsonElement.DeepEquals(relation.Value, outcomes.GetProperty(relation.Name)));
            else if (relation.Value.GetProperty("targets").GetArrayLength() == 0)
                Assert.Contains(data.GetProperty("empty_relations").EnumerateArray(),
                    value => value.GetString() == relation.Name);
            else
            {
                Assert.Equal("Available", relation.Value.GetProperty("state").GetString());
                Assert.Equal("Complete", relation.Value.GetProperty("completeness").GetString());
                Assert.Equal(relation.Value.GetProperty("targets").GetArrayLength(),
                    hal.GetProperty("_links").GetProperty(relation.Name).GetArrayLength());
            }
        }
        if (direct.TryGetProperty("_embedded", out var children))
        {
            var expected = children.GetProperty("resources");
            var actual = embedded.GetProperty("resources");
            Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());
            for (int i = 0; i < expected.GetArrayLength(); i++)
                AssertEquivalent(expected[i], actual[i]);
        }
    }

    private static void AssertNoProjectedAddresses(JsonElement value) =>
        Assert.DoesNotContain("?projection=", value.GetRawText());

    private static string Bind(ResourcePath path) => "inspect-resource:/" + path.Value;

    private static WorkspaceSharePacket Parent(bool focused) => WorkspaceSharePacketCodec.ParseJson("""
        {"f":4,"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":FOCUS,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}
        """.Replace("FOCUS", focused ? "0" : "null"), TestContext.Current.CancellationToken);
}
