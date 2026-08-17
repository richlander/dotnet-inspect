using System.Text.Json;
using System.Text.Json.Nodes;
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

    [Fact]
    public void StructuralDiffWriter_RoundTripsStrictReaderAndDerivedRows()
    {
        var expected = StructuralDiff();

        string json = AnnotatedSourceJson.SerializeStructuralDiff(expected);
        var actual = AnnotatedSourceJson.DeserializeStructuralDiff(json);

        Assert.Equal(CSharpStructuralDiffDocument.CurrentSchemaVersion, actual.SchemaVersion);
        Assert.Equal(
            CSharpStructuralDiffDocument.CurrentMethodologyVersion,
            actual.MethodologyVersion);
        Assert.Equal(expected.Correspondence.BeforeRevision, actual.Correspondence.BeforeRevision);
        Assert.Equal(expected.Correspondence.AfterRevision, actual.Correspondence.AfterRevision);
        Assert.Equal(expected.Correspondence.Matches, actual.Correspondence.Matches);
        Assert.Equal(expected.Before, actual.Before);
        Assert.Equal(expected.After, actual.After);
        Assert.Equal(expected.Rows.Length, actual.Rows.Length);
        Assert.Equal(expected.Rows[0].Change, actual.Rows[0].Change);
        Assert.Equal(expected.Rows[0].BeforeSpans, actual.Rows[0].BeforeSpans);
        Assert.Equal(expected.Rows[0].AfterSpans, actual.Rows[0].AfterSpans);
        Assert.Equal(expected.Fidelity, actual.Fidelity);
        Assert.Single(actual.ToComparison().Rows);
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
        bool structuralDiff,
        string oldValue,
        string newValue,
        string expected)
    {
        string json = (structuralDiff ? StructuralDiffJson() : TrustedCompactJson())
            .Replace(oldValue, newValue, StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(() => Deserialize(structuralDiff, json));

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
    [InlineData(true, "C# structural diff JSON violates the JSON contract.")]
    public void StrictReaders_DoNotRelayUnknownPropertyNames(
        bool structuralDiff,
        string expectedMessage)
    {
        const string HostilePropertyName = "ATTACKER_TOKEN";
        string json = (structuralDiff ? StructuralDiffJson() : CompactJson()).Replace(
            structuralDiff ? "\"schema_version\":1" : "\"text\":\"return;\"",
            structuralDiff
                ? $"\"schema_version\":1,\"{HostilePropertyName}\":0"
                : $"\"text\":\"return;\",\"{HostilePropertyName}\":0",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => Deserialize(structuralDiff, json));

        Assert.Equal(expectedMessage, error.Message);
        Assert.DoesNotContain(HostilePropertyName, error.Message, StringComparison.Ordinal);
        AssertContained(error);
    }

    [Theory]
    [InlineData(false, "Annotated-source JSON is malformed.")]
    [InlineData(true, "C# structural diff JSON is malformed.")]
    public void StrictReaders_DoNotRelayMalformedJsonContent(
        bool structuralDiff,
        string expectedMessage)
    {
        const string HostileContent = "ATTACKER_TOKEN";
        string json = $"{(structuralDiff ? StructuralDiffJson() : CompactJson())} {HostileContent}";

        var error = Assert.Throws<JsonException>(
            () => Deserialize(structuralDiff, json));

        Assert.Equal(expectedMessage, error.Message);
        Assert.DoesNotContain(HostileContent, error.Message, StringComparison.Ordinal);
        AssertContained(error);
    }

    [Theory]
    [InlineData(false, "Annotated-source JSON is malformed.")]
    [InlineData(true, "C# structural diff JSON is malformed.")]
    public void StrictReaders_ContainRawMalformedUtf16(
        bool structuralDiff,
        string expectedMessage)
    {
        const string RawUnpairedSurrogate = "\uD800";
        string json = (structuralDiff ? StructuralDiffJson() : CompactJson()).Replace(
            structuralDiff ? "\"subject\":\"M\"" : "\"text\":\"return;\"",
            structuralDiff
                ? $"\"subject\":\"{RawUnpairedSurrogate}\""
                : $"\"text\":\"{RawUnpairedSurrogate}\"",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => Deserialize(structuralDiff, json));

        Assert.Equal(expectedMessage, error.Message);
        AssertContained(error);
    }

    [Theory]
    [InlineData(false, "Annotated-source JSON is malformed.")]
    [InlineData(true, "C# structural diff JSON violates the JSON contract.")]
    public void StrictReaders_ContainEscapedMalformedUtf16PropertyNames(
        bool structuralDiff,
        string expectedMessage)
    {
        string json = (structuralDiff ? StructuralDiffJson() : CompactJson()).Replace(
            structuralDiff ? "\"schema_version\":1" : "\"text\":\"return;\"",
            structuralDiff
                ? "\"schema_version\":1,\"\\uD800\":0"
                : "\"text\":\"return;\",\"\\uD800\":0",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => Deserialize(structuralDiff, json));

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
    [InlineData("schema_version")]
    [InlineData("methodology_version")]
    [InlineData("correspondence")]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("rows")]
    public void StrictStructuralDiffReader_RejectsNullRequiredFields(string propertyName)
    {
        var root = JsonNode.Parse(StructuralDiffJson())!.AsObject();
        root[propertyName] = null;

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeStructuralDiff(root.ToJsonString()));

        Assert.Contains("null required properties", error.Message, StringComparison.Ordinal);
        Assert.Contains(propertyName, error.Message, StringComparison.Ordinal);
        AssertContained(error);
    }

    [Fact]
    public void StrictStructuralDiffReader_RejectsNullMatch()
    {
        string json = StructuralDiffJson().Replace(
            "\"matches\":[{",
            "\"matches\":[null,{",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeStructuralDiff(json));

        Assert.Equal("correspondence.matches must contain JSON objects.", error.Message);
        AssertContained(error);
    }

    [Theory]
    [InlineData("\"provenance\":\"IlOriginSet\"", "\"provenance\":\"iloriginset\"", "node-match provenance")]
    [InlineData("\"reason\":\"NoCounterpart\"", "\"reason\":0", "unmatched-node reason")]
    [InlineData("\"before\":\"OpcodeDiff\"", "\"before\":\"Exact, OpcodeDiff\"", "IL body-diff outcome")]
    [InlineData("\"change\":\"Changed\"", "\"change\":\"Added, Removed\"", "structural change kind")]
    public void StrictStructuralDiffReader_RequiresExactDeclaredEnumNames(
        string oldValue,
        string newValue,
        string expected)
    {
        string json = StructuralDiffJson(includeUnmatched: true)
            .Replace(oldValue, newValue, StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeStructuralDiff(json));

        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
        AssertContained(error);
    }

    [Theory]
    [InlineData("schema_version")]
    [InlineData("methodology_version")]
    public void StrictStructuralDiffReader_RejectsUnsupportedVersions(string propertyName)
    {
        string json = StructuralDiffJson().Replace(
            $"\"{propertyName}\":1",
            $"\"{propertyName}\":999",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeStructuralDiff(json));

        Assert.Equal(
            "C# structural diff JSON violates the product-issued model contract.",
            error.Message);
        AssertContained(error);
    }

    [Fact]
    public void StrictStructuralDiffReader_RejectsTamperedRevision()
    {
        var document = StructuralDiff();
        string json = AnnotatedSourceJson.SerializeStructuralDiff(document, indented: false).Replace(
            document.Correspondence.BeforeRevision.Sha256,
            new string('F', 64),
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeStructuralDiff(json));

        Assert.Equal(
            "C# structural diff JSON violates the product-issued model contract.",
            error.Message);
        AssertContained(error);
    }

    [Fact]
    public void StrictStructuralDiffReader_RejectsTamperedRows()
    {
        string json = StructuralDiffJson().Replace(
            "\"change\":\"Changed\"",
            "\"change\":\"Moved\"",
            StringComparison.Ordinal);

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeStructuralDiff(json));

        Assert.Equal(
            "C# structural diff JSON violates the product-issued model contract.",
            error.Message);
        AssertContained(error);
    }

    [Fact]
    public void StrictStructuralDiffReader_RejectsTamperedProjection()
    {
        var root = JsonNode.Parse(StructuralDiffJson())!.AsObject();
        root["before"] = root["after"]!.DeepClone();

        var error = Assert.Throws<JsonException>(
            () => AnnotatedSourceJson.DeserializeStructuralDiff(root.ToJsonString()));

        Assert.Equal(
            "C# structural diff JSON violates the product-issued model contract.",
            error.Message);
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

    static string StructuralDiffJson(bool includeUnmatched = false)
        => AnnotatedSourceJson.SerializeStructuralDiff(
            StructuralDiff(includeUnmatched),
            indented: false);

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

    static void Deserialize(bool structuralDiff, string json)
    {
        if (structuralDiff)
            _ = AnnotatedSourceJson.DeserializeStructuralDiff(json);
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

    static CSharpStructuralDiffDocument StructuralDiff(bool includeUnmatched = false)
    {
        var source = new AnnotatedSourceDocumentSource(
            "Tests",
            new Guid("00112233-4455-6677-8899-AABBCCDDEEFF"),
            0x06000001,
            new string('A', 64),
            "M");
        var before = TrustedDocument(
            "return;",
            "ReturnStatement",
            [0],
            source);
        var after = TrustedDocument(
            includeUnmatched ? "break; Call();" : "break;",
            "BreakStatement",
            [0],
            source,
            includeUnmatched
                ? new AnnotatedSourceNode(
                    1,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(7, 6)],
                    Provenance: new AnnotatedSourceNodeProvenance([1]))
                : null);
        return CSharpStructuralDiffDocument.Create(
            before,
            after,
            new CSharpStructuralFidelityEvidence(
                ILInspector.Instructions.IlBodyDiffOutcome.OpcodeDiff,
                ILInspector.Instructions.IlBodyDiffOutcome.Exact,
                "terminal IL_0000: ret"));
    }

    static AnnotatedSourceDocument TrustedDocument(
        string text,
        string kind,
        IReadOnlyList<int> provenance,
        AnnotatedSourceDocumentSource source,
        AnnotatedSourceNode? additionalNode = null)
    {
        var nodes = new List<AnnotatedSourceNode>
        {
            new(
                0,
                kind,
                SourceLineKind.CSharp,
                [new AnnotatedSourceSpan(0, kind == "ReturnStatement" ? 7 : 6)],
                Provenance: new AnnotatedSourceNodeProvenance(provenance))
        };
        if (additionalNode is not null)
            nodes.Add(additionalNode);
        return new AnnotatedSourceDocument(text, nodes, [], [], [], source);
    }
}
