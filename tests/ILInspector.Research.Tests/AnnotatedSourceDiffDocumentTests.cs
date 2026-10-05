using System.Text.Json;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using Inspector.Findings;

namespace ILInspector.Research.Tests;

public class AnnotatedSourceDiffDocumentTests
{
    [Fact]
    public void Create_ProjectsExactMediaAndProductComparisons()
    {
        Guid beforeMvid =
            new("00112233-4455-6677-8899-AABBCCDDEEFF");
        Guid afterMvid =
            new("10213243-5465-7687-98A9-BACBDCEDFE0F");
        AnnotatedSourceDocument before =
            Document("return 1;", "ldc.i4.1", beforeMvid);
        AnnotatedSourceDocument after =
            Document("return 2;", "ldc.i4.2", afterMvid);

        AnnotatedSourceDiffDocument document =
            AnnotatedSourceDiffDocument.Create(
                new("Tests.C", "M"),
                AnnotatedSourceDiffSide.Present(
                    Endpoint(beforeMvid),
                    before),
                AnnotatedSourceDiffSide.Present(
                    Endpoint(afterMvid),
                    after),
                [],
                includeIl: true);

        Assert.Same(before, document.Before.Document);
        Assert.Same(after, document.After.Document);
        Assert.Collection(
            document.Media,
            csharp =>
            {
                Assert.Equal(
                    AnnotatedSourceDiffMediumKind.CSharp,
                    csharp.Medium);
                Assert.Equal(
                    "return 1;",
                    Assert.Single(csharp.Comparison!.Analysis.Before));
                Assert.Equal(
                    "return 2;",
                    Assert.Single(csharp.Comparison.Analysis.After));
                Assert.Null(csharp.TooComplex);
                Assert.Equal(
                    [
                        new AnnotatedSourceDiffLineMapEntry(0, 0, 9),
                    ],
                    csharp.BeforeLines);
                Assert.Equal(
                    csharp.Comparison.Analysis.Before.Length,
                    csharp.BeforeLines.Count);
            },
            il =>
            {
                Assert.Equal(AnnotatedSourceDiffMediumKind.Il, il.Medium);
                Assert.Equal(
                    ["ldc.i4.1", "ret"],
                    il.Comparison!.Analysis.Before);
                Assert.Equal(
                    ["ldc.i4.2", "ret"],
                    il.Comparison.Analysis.After);
                Assert.Equal(
                    [
                        new AnnotatedSourceDiffLineMapEntry(0, 10, 8),
                        new AnnotatedSourceDiffLineMapEntry(1, 19, 3),
                    ],
                    il.BeforeLines);
            });
    }

    [Fact]
    public void Create_CSharpOnlyRetainsOnlyCSharpDocument()
    {
        Guid mvid =
            new("00112233-4455-6677-8899-AABBCCDDEEFF");

        AnnotatedSourceDiffDocument document =
            AnnotatedSourceDiffDocument.Create(
                new("Tests.C", "M"),
                AnnotatedSourceDiffSide.Present(
                    Endpoint(mvid),
                    Document("return 1;", "ldc.i4.1", mvid)),
                AnnotatedSourceDiffSide.Absent(),
                [],
                includeIl: false);

        Assert.Equal("return 1;\n", document.Before.Document!.Text);
        Assert.DoesNotContain(
            document.Before.Document.Nodes,
            static node => node.Medium == SourceLineKind.Il);
        AnnotatedSourceDiffMedium medium = Assert.Single(document.Media);
        Assert.Null(medium.Comparison);
        Assert.Null(medium.TooComplex);
        Assert.Empty(medium.AfterLines);
    }

    [Fact]
    public void Create_RecordsExactEndpointLimit()
    {
        const int lineCount =
            AnnotatedSourceDiffDocument.MaximumEndpointLines + 1;
        string text = string.Join(
            '\n',
            Enumerable.Repeat("x", lineCount));
        Guid beforeMvid =
            new("00112233-4455-6677-8899-AABBCCDDEEFF");
        Guid afterMvid =
            new("10213243-5465-7687-98A9-BACBDCEDFE0F");
        AnnotatedSourceDocument before =
            CSharpDocument(text, beforeMvid);
        AnnotatedSourceDocument after =
            CSharpDocument("x", afterMvid);

        AnnotatedSourceDiffDocument document =
            AnnotatedSourceDiffDocument.Create(
                new("Tests.C", "M"),
                AnnotatedSourceDiffSide.Present(
                    Endpoint(beforeMvid),
                    before),
                AnnotatedSourceDiffSide.Present(
                    Endpoint(afterMvid),
                    after),
                [],
                includeIl: false);

        AnnotatedSourceDiffMedium medium = Assert.Single(document.Media);
        Assert.Null(medium.Comparison);
        Assert.Equal(
            new AnnotatedSourceDiffLimit(
                AnnotatedSourceDiffSideKind.Before,
                AnnotatedSourceDiffLimitDimension.Lines,
                lineCount,
                AnnotatedSourceDiffDocument.MaximumEndpointLines),
            medium.TooComplex);
    }

    [Fact]
    public void Create_RecordsExactOneSidedEndpointLimit()
    {
        const int lineCount =
            AnnotatedSourceDiffDocument.MaximumEndpointLines + 1;
        string text = string.Join(
            '\n',
            Enumerable.Repeat("x", lineCount));
        Guid afterMvid =
            new("10213243-5465-7687-98A9-BACBDCEDFE0F");

        AnnotatedSourceDiffDocument document =
            AnnotatedSourceDiffDocument.Create(
                new("Tests.C", "M"),
                AnnotatedSourceDiffSide.Absent(),
                AnnotatedSourceDiffSide.Present(
                    Endpoint(afterMvid),
                    CSharpDocument(text, afterMvid)),
                [],
                includeIl: false);

        AnnotatedSourceDiffMedium medium = Assert.Single(document.Media);
        Assert.Null(medium.Comparison);
        Assert.Equal(
            new AnnotatedSourceDiffLimit(
                AnnotatedSourceDiffSideKind.After,
                AnnotatedSourceDiffLimitDimension.Lines,
                lineCount,
                AnnotatedSourceDiffDocument.MaximumEndpointLines),
            medium.TooComplex);
    }

    [Fact]
    public void Create_FinalTerminatorDoesNotCreateAnExtraSequenceLine()
    {
        const int lineCount =
            AnnotatedSourceDiffDocument.MaximumEndpointLines;
        string text =
            string.Join('\n', Enumerable.Repeat("x", lineCount)) + '\n';
        Guid beforeMvid =
            new("00112233-4455-6677-8899-AABBCCDDEEFF");
        Guid afterMvid =
            new("10213243-5465-7687-98A9-BACBDCEDFE0F");

        AnnotatedSourceDiffDocument document =
            AnnotatedSourceDiffDocument.Create(
                new("Tests.C", "M"),
                AnnotatedSourceDiffSide.Present(
                    Endpoint(beforeMvid),
                    CSharpDocument(text, beforeMvid)),
                AnnotatedSourceDiffSide.Present(
                    Endpoint(afterMvid),
                    CSharpDocument(text, afterMvid)),
                [],
                includeIl: false);

        AnnotatedSourceDiffMedium medium = Assert.Single(document.Media);
        Assert.Null(medium.TooComplex);
        Assert.Equal(lineCount, medium.BeforeLines.Count);
        Assert.Equal(
            medium.Comparison!.Analysis.Before.Length,
            medium.BeforeLines.Count);
    }

    [Fact]
    public void Constructors_SnapshotPortableCollections()
    {
        var lines = new List<AnnotatedSourceDiffLineMapEntry>
        {
            new(0, 0, 1),
        };
        var medium = new AnnotatedSourceDiffMedium(
            AnnotatedSourceDiffMediumKind.CSharp,
            lines,
            [],
            Comparison: null,
            TooComplex: null);
        lines.Clear();
        Assert.Single(medium.BeforeLines);

        Guid mvid =
            new("00112233-4455-6677-8899-AABBCCDDEEFF");
        var forwarders = new List<AnnotatedSourceDiffForwarder>
        {
            new(
                AnnotatedSourceDiffSideKind.Before,
                0,
                "Tests.C",
                "Tests"),
        };
        AnnotatedSourceDiffDocument document =
            AnnotatedSourceDiffDocument.Create(
                new("Tests.C", "M"),
                AnnotatedSourceDiffSide.Present(
                    Endpoint(mvid),
                    CSharpDocument("x", mvid)),
                AnnotatedSourceDiffSide.Absent(),
                forwarders,
                includeIl: false);
        forwarders.Clear();

        Assert.Single(document.Forwarders);
    }

    [Fact]
    public void Json_RoundTripsCanonicalDocumentStrictly()
    {
        Guid beforeMvid =
            new("00112233-4455-6677-8899-AABBCCDDEEFF");
        Guid afterMvid =
            new("10213243-5465-7687-98A9-BACBDCEDFE0F");
        AnnotatedSourceDiffDocument document =
            AnnotatedSourceDiffDocument.Create(
                new("Tests.C", "M"),
                AnnotatedSourceDiffSide.Present(
                    Endpoint(beforeMvid),
                    Document("return 1;", "ldc.i4.1", beforeMvid)),
                AnnotatedSourceDiffSide.Present(
                    Endpoint(afterMvid),
                    Document("return 2;", "ldc.i4.2", afterMvid)),
                [
                    new(
                        AnnotatedSourceDiffSideKind.Before,
                        0,
                        "Tests.C",
                        "Tests.Implementation"),
                ],
                includeIl: true);

        string json = AnnotatedSourceDiffJson.Serialize(
            document,
            indented: false);
        AnnotatedSourceDiffDocument roundTripped =
            AnnotatedSourceDiffJson.Deserialize(json);

        Assert.Equal(
            json,
            AnnotatedSourceDiffJson.Serialize(
                roundTripped,
                indented: false));
    }

    [Theory]
    [InlineData(
        """{"schema_version":1,"methodology_version":1,"subject":{"declaring_type":"T","selector":"M"},"style":"ByteFaithful","before":{"outcome":"Absent","reason":"CorrespondenceKeyAbsent"},"after":{"outcome":"Absent","reason":"CorrespondenceKeyAbsent"},"forwarders":[],"media":[{"medium":"CSharp","before_lines":[],"after_lines":[]}],"unknown":true}""")]
    [InlineData(
        """{"schema_version":1,"schema_version":1,"methodology_version":1,"subject":{"declaring_type":"T","selector":"M"},"style":"ByteFaithful","before":{"outcome":"Absent","reason":"CorrespondenceKeyAbsent"},"after":{"outcome":"Absent","reason":"CorrespondenceKeyAbsent"},"forwarders":[],"media":[{"medium":"CSharp","before_lines":[],"after_lines":[]}]}""")]
    [InlineData(
        """{"schema_version":1,"methodology_version":1,"subject":{"declaring_type":"T","selector":"M"},"style":"bytefaithful","before":{"outcome":"Absent","reason":"CorrespondenceKeyAbsent"},"after":{"outcome":"Absent","reason":"CorrespondenceKeyAbsent"},"forwarders":[],"media":[{"medium":"CSharp","before_lines":[],"after_lines":[]}]}""")]
    public void Json_RejectsUnknownDuplicateAndNonExactValues(
        string json)
    {
        Assert.Throws<JsonException>(
            () => AnnotatedSourceDiffJson.Deserialize(json));
    }

    static AnnotatedSourceDiffEndpoint Endpoint(Guid mvid)
        => new(
            "Tests",
            mvid,
            0x06000001,
            ResearchTargetRelationshipRole.Method);

    static AnnotatedSourceDocument Document(
        string csharp,
        string firstInstruction,
        Guid mvid)
    {
        string text = $"{csharp}\n{firstInstruction}\nret";
        int first = csharp.Length + 1;
        int second = first + firstInstruction.Length + 1;
        return new(
            text,
            [
                new AnnotatedSourceNode(
                    0,
                    "ReturnStatement",
                    SourceLineKind.CSharp,
                    [new(0, csharp.Length)]),
                new AnnotatedSourceNode(
                    1,
                    AnnotatedSourceNode.InstructionKind,
                    SourceLineKind.Il,
                    [new(first, firstInstruction.Length)],
                    0),
                new AnnotatedSourceNode(
                    2,
                    AnnotatedSourceNode.InstructionKind,
                    SourceLineKind.Il,
                    [new(second, 3)],
                    1),
            ],
            [],
            [],
            [],
            new AnnotatedSourceDocumentSource(
                "Tests",
                mvid,
                0x06000001,
                new string('A', 64),
                "Tests.C.M()"));
    }

    static AnnotatedSourceDocument CSharpDocument(
        string text,
        Guid mvid)
        => new(
            text,
            [
                new AnnotatedSourceNode(
                    0,
                    "Block",
                    SourceLineKind.CSharp,
                    [new(0, text.Length)]),
            ],
            [],
            [],
            [],
            new AnnotatedSourceDocumentSource(
                "Tests",
                mvid,
                0x06000001,
                new string('A', 64),
                "Tests.C.M()"));
}
