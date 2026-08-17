using System.Text.Json;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Decompiler.Tests;

public class AnnotatedSourceJsonTests
{
    [Fact]
    public void WriterContexts_PreserveAnnotatedSourceDocumentWireBytes()
    {
        var document = Document();

        string indented = JsonSerializer.Serialize(
            document,
            AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument);
        string compact = JsonSerializer.Serialize(
            document,
            AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument);

        string expectedIndented =
            """
            {
              "text": "return;",
              "nodes": [
                {
                  "id": 0,
                  "kind": "ReturnStatement",
                  "medium": "CSharp",
                  "spans": [
                    {
                      "start": 0,
                      "length": 7
                    }
                  ]
                }
              ],
              "regions": [
                {
                  "role": "Case",
                  "spans": [
                    {
                      "start": 0,
                      "length": 7
                    }
                  ]
                }
              ],
              "facts": [
                {
                  "id": 0,
                  "descriptor": "test.fact",
                  "category": "Semantics",
                  "conditionality": "Always",
                  "source_offset": -1,
                  "origin": "Body"
                }
              ],
              "targets": [
                {
                  "fact_id": 0,
                  "node_id": 0
                }
              ]
            }
            """;
        Assert.Equal(expectedIndented.ReplaceLineEndings(Environment.NewLine), indented);
        Assert.Equal(
            """{"text":"return;","nodes":[{"id":0,"kind":"ReturnStatement","medium":"CSharp","spans":[{"start":0,"length":7}]}],"regions":[{"role":"Case","spans":[{"start":0,"length":7}]}],"facts":[{"id":0,"descriptor":"test.fact","category":"Semantics","conditionality":"Always","source_offset":-1,"origin":"Body"}],"targets":[{"fact_id":0,"node_id":0}]}""",
            compact);
    }

    [Fact]
    public void StrictDocumentReader_RoundTripsWriterOutputWithOmittedNullableDetail()
    {
        var expected = Document();
        string json = JsonSerializer.Serialize(
            expected,
            AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument);

        var actual = AnnotatedSourceJson.DeserializeDocument(json);

        Assert.Equal(expected, actual);
        Assert.Null(Assert.Single(actual.Facts).Detail);
    }

    [Theory]
    [InlineData("\"start\":0,\"length\":7", "\"start\":0", "length")]
    [InlineData("\"medium\":\"CSharp\",", "", "medium")]
    [InlineData(",\"origin\":\"Body\"", "", "origin")]
    [InlineData(",\"node_id\":0", "", "node_id")]
    public void StrictDocumentReader_RejectsMissingRequiredFields(
        string oldValue,
        string newValue,
        string expected)
    {
        string json = CompactJson().Replace(oldValue, newValue, StringComparison.Ordinal);

        var error = Assert.ThrowsAny<Exception>(
            () => AnnotatedSourceJson.DeserializeDocument(json));

        Assert.IsType<JsonException>(error);
        Assert.Contains("missing required properties", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "\"provenance\":{\"il_offsets\":[0]}", "\"provenance\":{}", "il_offsets")]
    [InlineData(true, "\"provenance\":{\"il_offsets\":[0]}", "\"provenance\":{}", "il_offsets")]
    [InlineData(false, ",\"subject\":\"M\"", "", "subject")]
    [InlineData(true, ",\"subject\":\"M\"", "", "subject")]
    public void StrictReaders_RejectMissingNestedRequiredFields(
        bool structuralComparison,
        string oldValue,
        string newValue,
        string expected)
    {
        string document = TrustedCompactJson().Replace(oldValue, newValue, StringComparison.Ordinal);
        string json = structuralComparison ? StructuralComparisonJson(document) : document;

        var error = Assert.Throws<JsonException>(() => Deserialize(structuralComparison, json));

        Assert.Contains("missing required properties", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        AssertContained(error);
    }

    [Fact]
    public void StrictDocumentReader_RejectsDuplicateProperties()
    {
        string json = CompactJson().Replace(
            "\"text\":\"return;\"",
            "\"text\":\"return;\",\"text\":\"return;\"",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AnnotatedSourceJson.DeserializeDocument(json));
    }

    [Theory]
    [InlineData(false, "Annotated-source JSON violates the JSON contract.")]
    [InlineData(true, "Structural comparison JSON violates the JSON contract.")]
    public void StrictReaders_DoNotRelayUnknownPropertyNames(
        bool structuralComparison,
        string expectedMessage)
    {
        const string HostilePropertyName = "ATTACKER_TOKEN";
        string json = (structuralComparison ? StructuralComparisonJson() : CompactJson()).Replace(
            structuralComparison ? "\"subject\":\"test\"" : "\"text\":\"return;\"",
            structuralComparison
                ? $"\"subject\":\"test\",\"{HostilePropertyName}\":0"
                : $"\"text\":\"return;\",\"{HostilePropertyName}\":0",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => Deserialize(structuralComparison, json));

        Assert.Equal(expectedMessage, error.Message);
        Assert.DoesNotContain(HostilePropertyName, error.Message, StringComparison.Ordinal);
        AssertContained(error);
    }

    [Theory]
    [InlineData(false, "Annotated-source JSON is malformed.")]
    [InlineData(true, "Structural comparison JSON is malformed.")]
    public void StrictReaders_DoNotRelayMalformedJsonContent(
        bool structuralComparison,
        string expectedMessage)
    {
        const string HostileContent = "ATTACKER_TOKEN";
        string json = $"{(structuralComparison ? StructuralComparisonJson() : CompactJson())} {HostileContent}";

        var error = Assert.Throws<JsonException>(
            () => Deserialize(structuralComparison, json));

        Assert.Equal(expectedMessage, error.Message);
        Assert.DoesNotContain(HostileContent, error.Message, StringComparison.Ordinal);
        AssertContained(error);
    }

    [Theory]
    [InlineData(false, "Annotated-source JSON is malformed.")]
    [InlineData(true, "Structural comparison JSON is malformed.")]
    public void StrictReaders_ContainRawMalformedUtf16(
        bool structuralComparison,
        string expectedMessage)
    {
        const string RawUnpairedSurrogate = "\uD800";
        string json = (structuralComparison ? StructuralComparisonJson() : CompactJson()).Replace(
            structuralComparison ? "\"subject\":\"test\"" : "\"text\":\"return;\"",
            structuralComparison
                ? $"\"subject\":\"{RawUnpairedSurrogate}\""
                : $"\"text\":\"{RawUnpairedSurrogate}\"",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => Deserialize(structuralComparison, json));

        Assert.Equal(expectedMessage, error.Message);
        AssertContained(error);
    }

    [Theory]
    [InlineData(false, "Annotated-source JSON is malformed.")]
    [InlineData(true, "Structural comparison JSON violates the JSON contract.")]
    public void StrictReaders_ContainEscapedMalformedUtf16PropertyNames(
        bool structuralComparison,
        string expectedMessage)
    {
        string json = (structuralComparison ? StructuralComparisonJson() : CompactJson()).Replace(
            structuralComparison ? "\"subject\":\"test\"" : "\"text\":\"return;\"",
            structuralComparison
                ? "\"subject\":\"test\",\"\\uD800\":0"
                : "\"text\":\"return;\",\"\\uD800\":0",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => Deserialize(structuralComparison, json));

        Assert.Equal(expectedMessage, error.Message);
        AssertContained(error);
    }

    [Theory]
    [InlineData("\"text\":\"return;\"", "\"text\":null", "text")]
    [InlineData("\"kind\":\"ReturnStatement\"", "\"kind\":null", "kind")]
    public void StrictDocumentReader_RejectsNullRequiredFields(
        string oldValue,
        string newValue,
        string expected)
    {
        string json = CompactJson().Replace(oldValue, newValue, StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeDocument(json));

        Assert.Contains("null required properties", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        AssertContained(error);
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("before_node_ids")]
    [InlineData("after_node_ids")]
    [InlineData("correspondences")]
    public void StrictStructuralReader_RejectsNullRequiredFields(string propertyName)
    {
        string json = StructuralComparisonJson();
        string oldValue = propertyName switch
        {
            "subject" => "\"subject\":\"test\"",
            "before" => $"\"before\":{CompactJson()}",
            "after" => $"\"after\":{CompactJson()}",
            "before_node_ids" => "\"before_node_ids\":[0]",
            "after_node_ids" => "\"after_node_ids\":[0]",
            "correspondences" =>
                "\"correspondences\":[{\"before_node_id\":0,\"after_node_id\":0}]",
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName)),
        };
        string jsonWithNull = json.Replace(
            oldValue,
            $"\"{propertyName}\":null",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeStructuralComparison(jsonWithNull));

        Assert.Contains("null required properties", error.Message, StringComparison.Ordinal);
        Assert.Contains(propertyName, error.Message, StringComparison.Ordinal);
        AssertContained(error);
    }

    [Fact]
    public void StrictStructuralReader_RejectsNullCorrespondence()
    {
        string json = StructuralComparisonJson().Replace(
            """{"before_node_id":0,"after_node_id":0}""",
            "null",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeStructuralComparison(json));

        Assert.Equal("correspondences must contain JSON objects.", error.Message);
        AssertContained(error);
    }

    [Theory]
    [InlineData("\"CSharp\"", "\"csharp\"", "SourceLineKind")]
    [InlineData("\"CSharp\"", "0", "SourceLineKind")]
    [InlineData("\"Case\"", "\"Header, Body\"", "PrintedRegionRole")]
    [InlineData("\"Always\"", "\"always\"", "AnnotationConditionality")]
    [InlineData("\"Body\"", "\"body\"", "AnnotatedSourceFactOrigin")]
    public void StrictDocumentReader_RequiresExactDeclaredEnumNames(
        string oldValue,
        string newValue,
        string expected)
    {
        string json = CompactJson().Replace(oldValue, newValue, StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeDocument(json));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictDocumentReader_RejectsInvalidDocumentTopology()
    {
        string json = CompactJson().Replace(
            "\"length\":7",
            "\"length\":8",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeDocument(json));

        Assert.Equal(
            "Annotated-source JSON violates the document model contract.",
            error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void StrictDocumentReader_DoesNotRelayRejectedArtifactValues()
    {
        const string HostileKind = "hostile\u001B[31m";
        const string HostileKindJson = "hostile\\u001B[31m";
        string json = CompactJson()
            .Replace("ReturnStatement", HostileKindJson, StringComparison.Ordinal)
            .Replace(
                "\"medium\":\"CSharp\",\"spans\":[",
                "\"medium\":\"CSharp\",\"il_offset\":0,\"spans\":[",
                StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeDocument(json));

        Assert.DoesNotContain(HostileKind, error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    static string CompactJson()
        => JsonSerializer.Serialize(
            Document(),
            AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument);

    static string StructuralComparisonJson()
        => StructuralComparisonJson(CompactJson());

    static string StructuralComparisonJson(string document)
    {
        return
            $$"""{"subject":"test","before":{{document}},"after":{{document}},"before_node_ids":[0],"after_node_ids":[0],"correspondences":[{"before_node_id":0,"after_node_id":0}]}""";
    }

    static string TrustedCompactJson()
        => JsonSerializer.Serialize(
            new AnnotatedSourceDocument(
                "return;",
                [
                    new AnnotatedSourceNode(
                        0,
                        "ReturnStatement",
                        SourceLineKind.CSharp,
                        [new AnnotatedSourceSpan(0, 7)],
                        Provenance: new AnnotatedSourceNodeProvenance([0])),
                ],
                [],
                [],
                [],
                new AnnotatedSourceDocumentSource(
                    "Tests",
                    new Guid("00112233-4455-6677-8899-AABBCCDDEEFF"),
                    0x06000001,
                    new string('A', 64),
                    "M")),
            AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument);

    static void Deserialize(bool structuralComparison, string json)
    {
        if (structuralComparison)
            _ = AnnotatedSourceJson.DeserializeStructuralComparison(json);
        else
            _ = AnnotatedSourceJson.DeserializeDocument(json);
    }

    static void AssertContained(JsonException error)
    {
        Assert.Null(error.InnerException);
        Assert.Null(error.Path);
        Assert.Null(error.LineNumber);
        Assert.Null(error.BytePositionInLine);
    }

    static AnnotatedSourceDocument Document()
        => new(
            "return;",
            [
                new AnnotatedSourceNode(
                    0,
                    "ReturnStatement",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 7)]),
            ],
            [
                new AnnotatedSourceRegion(
                    PrintedRegionRole.Case,
                    [new AnnotatedSourceSpan(0, 7)]),
            ],
            [
                new AnnotatedSourceFact(
                    0,
                    "test.fact",
                    "Semantics",
                    AnnotationConditionality.Always,
                    Detail: null,
                    SourceOffset: -1,
                    AnnotatedSourceFactOrigin.Body),
            ],
            [new AnnotatedSourceTarget(0, 0)]);
}
