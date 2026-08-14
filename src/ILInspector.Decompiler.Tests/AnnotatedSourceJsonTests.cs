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

        Assert.Equal(
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
            """,
            indented);
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

    [Fact]
    public void StrictDocumentReader_RejectsDuplicateProperties()
    {
        string json = CompactJson().Replace(
            "\"text\":\"return;\"",
            "\"text\":\"return;\",\"text\":\"return;\"",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AnnotatedSourceJson.DeserializeDocument(json));
    }

    [Fact]
    public void StrictDocumentReader_RejectsUnknownProperties()
    {
        string json = CompactJson().Replace(
            "\"text\":\"return;\"",
            "\"text\":\"return;\",\"unknown\":0",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AnnotatedSourceJson.DeserializeDocument(json));
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
    public void StrictDocumentReader_RejectsMalformedUtf16Transport()
    {
        string json = CompactJson().Replace(
            "\"text\":\"return;\"",
            "\"text\":\"\\uD800\"",
            StringComparison.Ordinal)
            .Replace("\"length\":7", "\"length\":1", StringComparison.Ordinal);

        Assert.ThrowsAny<Exception>(() => AnnotatedSourceJson.DeserializeDocument(json));
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
