using System.Collections.Immutable;
using System.Text.Json;

using ILInspector.Findings;

namespace ILInspector.Instructions.Tests;

public class ComparisonDocumentJsonTests
{
    [Fact]
    public void Json_RoundTripsClosedPayloadAndAllCompositionCases()
    {
        ComparisonChangeDescription rootDescription = Description(
            "root",
            ComparisonExceptionalChangeKind.RenameAndMove,
            "Old.Root",
            "Old.Root<T>",
            "New.Root",
            "New.Root<T>");
        ComparisonChangeDescription renameDescription = Description(
            "rename",
            ComparisonExceptionalChangeKind.Rename,
            "Old",
            "Old()",
            "New",
            "New()");
        ComparisonChangeDescription moveDescription = new(
            "move",
            ComparisonExceptionalChangeKind.Move,
            new ComparisonSubjectEndpoint("Old.Type.M", "Old.Type.M()"),
            new ComparisonSubjectEndpoint("New.Type.M", "New.Type.M()"),
            [
                new ComparisonTransformationDescriptor(
                    "dotnet.member.to-extension",
                    "Moved to an extension member."),
            ]);
        var document = new ComparisonDocument<ComparisonDocumentTestPayload>(
            ComparisonDocument<ComparisonDocumentTestPayload>.CurrentSchemaVersion,
            SubjectCoordinateBasis.OuterContext,
            "New.Root",
            "New.Root<T>",
            new ComparisonSubjectChange.RenameAndMove("root"),
            new ComparisonRootComparison<ComparisonDocumentTestPayload>.Present(
                new("<root>&", "root")),
            [
                Subject("Diff", "Diff()", new ComparisonSubjectChange.Diff(), "left"),
                Subject(
                    "Added",
                    "Added()",
                    new ComparisonSubjectChange.Addition(),
                    "after"),
                Subject(
                    "Deleted",
                    "Deleted()",
                    new ComparisonSubjectChange.Deletion(),
                    "before"),
                Subject(
                    "New",
                    "New()",
                    new ComparisonSubjectChange.Rename("rename"),
                    "renamed"),
                Subject(
                    "New.Type.M",
                    "New.Type.M()",
                    new ComparisonSubjectChange.Move("move"),
                    "moved"),
            ],
            [moveDescription, rootDescription, renameDescription]);

        string json = ComparisonDocumentJson.Serialize(
            document,
            ComparisonDocumentTestJsonContext.Default.ComparisonDocumentTestPayload,
            indented: true);
        ComparisonDocument<ComparisonDocumentTestPayload> roundTrip =
            ComparisonDocumentJson.Deserialize(
                json,
                ComparisonDocumentTestJsonContext.Default.ComparisonDocumentTestPayload);

        Assert.Equal(document, roundTrip);
        Assert.Contains("\\u003Croot\\u003E\\u0026", json);
        Assert.True(
            json.IndexOf("\"id\": \"move\"", StringComparison.Ordinal)
            < json.IndexOf("\"id\": \"rename\"", StringComparison.Ordinal));
        Assert.True(
            json.IndexOf("\"id\": \"rename\"", StringComparison.Ordinal)
            < json.IndexOf("\"id\": \"root\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Json_UsesCanonicalDiffAndNotApplicableOmissions()
    {
        var document = new ComparisonDocument<ComparisonDocumentTestPayload>(
            ComparisonDocument<ComparisonDocumentTestPayload>.CurrentSchemaVersion,
            SubjectCoordinateBasis.RootRelative,
            "Root",
            "Root",
            new ComparisonSubjectChange.Diff(),
            new ComparisonRootComparison<ComparisonDocumentTestPayload>.NotApplicable(),
            [
                Subject(
                    "Member",
                    "Member()",
                    new ComparisonSubjectChange.Diff(),
                    "member"),
            ],
            []);

        string json = ComparisonDocumentJson.Serialize(
            document,
            ComparisonDocumentTestJsonContext.Default.ComparisonDocumentTestPayload);

        Assert.DoesNotContain("\"change_kinds\"", json);
        Assert.DoesNotContain("\"change_id\"", json);
        Assert.Equal(1, Count(json, "\"comparison\""));
        Assert.Contains("\"subject_coordinate_basis\":\"root-relative\"", json);
    }

    [Fact]
    public void Json_CanonicalizesDescriptionOrderAfterDeserialization()
    {
        const string json =
            """
            {
              "schema_version": 1,
              "subject_coordinate_basis": "outer-context",
              "identifier": "AfterRoot",
              "display": "AfterRoot",
              "change_kinds": ["rename"],
              "change_id": "z",
              "subjects": [
                {
                  "identifier": "AfterMember",
                  "display": "AfterMember",
                  "change_kinds": ["move"],
                  "change_id": "a",
                  "comparison": { "text": "payload", "orientation": "after" }
                }
              ],
              "change_descriptions": [
                {
                  "id": "z",
                  "change_kinds": ["rename"],
                  "before": { "identifier": "BeforeRoot", "display": "BeforeRoot" },
                  "after": { "identifier": "AfterRoot", "display": "AfterRoot" },
                  "transformations": []
                },
                {
                  "id": "a",
                  "change_kinds": ["move"],
                  "before": { "identifier": "BeforeMember", "display": "BeforeMember" },
                  "after": { "identifier": "AfterMember", "display": "AfterMember" },
                  "transformations": []
                }
              ]
            }
            """;

        ComparisonDocument<ComparisonDocumentTestPayload> document =
            ComparisonDocumentJson.Deserialize(
                json,
                ComparisonDocumentTestJsonContext.Default.ComparisonDocumentTestPayload);
        string canonical = ComparisonDocumentJson.Serialize(
            document,
            ComparisonDocumentTestJsonContext.Default.ComparisonDocumentTestPayload);

        Assert.Equal(
            ["a", "z"],
            document.ChangeDescriptions.Select(description => description.Id));
        Assert.True(
            canonical.IndexOf("\"id\":\"a\"", StringComparison.Ordinal)
            < canonical.IndexOf("\"id\":\"z\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Json_RoundTripsBothBasesIncludingEmptySubjects()
    {
        foreach (SubjectCoordinateBasis basis in Enum.GetValues<SubjectCoordinateBasis>())
        {
            var document = new ComparisonDocument<ComparisonDocumentTestPayload>(
                ComparisonDocument<ComparisonDocumentTestPayload>.CurrentSchemaVersion,
                basis,
                "Root",
                "Root",
                new ComparisonSubjectChange.Addition(),
                new ComparisonRootComparison<ComparisonDocumentTestPayload>.NotApplicable(),
                [],
                []);
            string json = ComparisonDocumentJson.Serialize(
                document,
                ComparisonDocumentTestJsonContext.Default.ComparisonDocumentTestPayload);

            ComparisonDocument<ComparisonDocumentTestPayload> roundTrip =
                Deserialize(json);

            Assert.Equal(basis, roundTrip.SubjectCoordinateBasis);
            Assert.Empty(roundTrip.Subjects);
        }
    }

    [Fact]
    public void Json_EncodesEnvelopeOwnedUntrustedStringsAsData()
    {
        const string identifier = "Root\"\\\n";
        const string display = "<root>&\t";
        var document = new ComparisonDocument<ComparisonDocumentTestPayload>(
            ComparisonDocument<ComparisonDocumentTestPayload>.CurrentSchemaVersion,
            SubjectCoordinateBasis.OuterContext,
            identifier,
            display,
            new ComparisonSubjectChange.Diff(),
            new ComparisonRootComparison<ComparisonDocumentTestPayload>.NotApplicable(),
            [],
            []);

        string json = ComparisonDocumentJson.Serialize(
            document,
            ComparisonDocumentTestJsonContext.Default.ComparisonDocumentTestPayload);
        ComparisonDocument<ComparisonDocumentTestPayload> roundTrip =
            Deserialize(json);

        Assert.Contains("\\u0022", json);
        Assert.Contains("\\\\", json);
        Assert.Contains("\\n", json);
        Assert.Contains("\\u003Croot\\u003E\\u0026\\t", json);
        Assert.Equal(identifier, roundTrip.Identifier);
        Assert.Equal(display, roundTrip.Display);
    }

    [Fact]
    public void Json_RejectsEveryRootRelativeChildRequiringAnAbsentRootSide()
    {
        ComparisonSubjectChange[] additionRootInvalidChildren =
        [
            new ComparisonSubjectChange.Diff(),
            new ComparisonSubjectChange.Deletion(),
            new ComparisonSubjectChange.Rename("rename"),
            new ComparisonSubjectChange.Move("move"),
            new ComparisonSubjectChange.RenameAndMove("rename-move"),
        ];
        ComparisonSubjectChange[] deletionRootInvalidChildren =
        [
            new ComparisonSubjectChange.Diff(),
            new ComparisonSubjectChange.Addition(),
            new ComparisonSubjectChange.Rename("rename"),
            new ComparisonSubjectChange.Move("move"),
            new ComparisonSubjectChange.RenameAndMove("rename-move"),
        ];

        foreach (ComparisonSubjectChange childChange in additionRootInvalidChildren)
            AssertMatrixRejection(new ComparisonSubjectChange.Addition(), childChange);
        foreach (ComparisonSubjectChange childChange in deletionRootInvalidChildren)
            AssertMatrixRejection(new ComparisonSubjectChange.Deletion(), childChange);
    }

    public static TheoryData<string> MalformedDocuments =>
        new()
        {
            """
            {"schema_version":1,"identifier":"R","display":"R","subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"unknown","identifier":"R","display":"R","subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","unknown":true,"subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","display":"Again","subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","change_kinds":["diff"],"subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","change_id":"x","subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","change_kinds":["addition"],"change_id":"x","subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","comparison":null,"subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","subjects":[{"identifier":"S","display":"S","comparison":null}],"change_descriptions":[]}
            """,
            """
            {"schema_version":2,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","change_kinds":["rename"],"change_id":"missing","subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","change_kinds":["move","rename"],"change_id":"x","subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","change_kinds":[],"subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","change_kinds":["rename","rename"],"change_id":"x","subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","change_kinds":["move","move"],"change_id":"x","subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","change_kinds":["addition","deletion"],"subjects":[],"change_descriptions":[]}
            """,
            """
            {"schema_version":1,"subject_coordinate_basis":"outer-context","identifier":"R","display":"R","change_kinds":["addition","rename"],"change_id":"x","subjects":[],"change_descriptions":[]}
            """,
        };

    [Theory]
    [MemberData(nameof(MalformedDocuments))]
    public void Json_RejectsMalformedAndNoncanonicalDocuments(string json)
    {
        Assert.Throws<JsonException>(
            () => ComparisonDocumentJson.Deserialize(
                json,
                ComparisonDocumentTestJsonContext.Default.ComparisonDocumentTestPayload));
    }

    [Fact]
    public void Json_RejectsUnknownNestedAndMissingRequiredProperties()
    {
        const string unknownNested =
            """
            {
              "schema_version": 1,
              "subject_coordinate_basis": "outer-context",
              "identifier": "R",
              "display": "R",
              "subjects": [
                {
                  "identifier": "S",
                  "display": "S",
                  "extra": 1,
                  "comparison": { "text": "p", "orientation": "left" }
                }
              ],
              "change_descriptions": []
            }
            """;
        const string missingPayload =
            """
            {
              "schema_version": 1,
              "subject_coordinate_basis": "outer-context",
              "identifier": "R",
              "display": "R",
              "subjects": [{ "identifier": "S", "display": "S" }],
              "change_descriptions": []
            }
            """;

        Assert.Throws<JsonException>(() => Deserialize(unknownNested));
        Assert.Throws<JsonException>(() => Deserialize(missingPayload));
    }

    static ComparisonDocument<ComparisonDocumentTestPayload> Deserialize(string json)
        => ComparisonDocumentJson.Deserialize(
            json,
            ComparisonDocumentTestJsonContext.Default.ComparisonDocumentTestPayload);

    static void AssertMatrixRejection(
        ComparisonSubjectChange rootChange,
        ComparisonSubjectChange childChange)
    {
        ImmutableArray<ComparisonChangeDescription> descriptions =
            ExceptionalDescription(childChange);
        var outerContext = new ComparisonDocument<ComparisonDocumentTestPayload>(
            ComparisonDocument<ComparisonDocumentTestPayload>.CurrentSchemaVersion,
            SubjectCoordinateBasis.OuterContext,
            "Root",
            "Root",
            rootChange,
            new ComparisonRootComparison<ComparisonDocumentTestPayload>.NotApplicable(),
            [
                new(
                    "Child",
                    "Child",
                    childChange,
                    new("payload", "subject")),
            ],
            descriptions);
        string json = ComparisonDocumentJson.Serialize(
            outerContext,
            ComparisonDocumentTestJsonContext.Default.ComparisonDocumentTestPayload)
            .Replace(
                "\"subject_coordinate_basis\":\"outer-context\"",
                "\"subject_coordinate_basis\":\"root-relative\"",
                StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => Deserialize(json));
    }

    static ComparisonSubject<ComparisonDocumentTestPayload> Subject(
        string identifier,
        string display,
        ComparisonSubjectChange change,
        string orientation)
        => new(
            identifier,
            display,
            change,
            new ComparisonDocumentTestPayload("payload", orientation));

    static ComparisonChangeDescription Description(
        string id,
        ComparisonExceptionalChangeKind kind,
        string beforeIdentifier,
        string beforeDisplay,
        string afterIdentifier,
        string afterDisplay)
        => new(
            id,
            kind,
            new ComparisonSubjectEndpoint(beforeIdentifier, beforeDisplay),
            new ComparisonSubjectEndpoint(afterIdentifier, afterDisplay),
            []);

    static ImmutableArray<ComparisonChangeDescription> ExceptionalDescription(
        ComparisonSubjectChange change)
        => change switch
        {
            ComparisonSubjectChange.Rename rename =>
            [
                Description(
                    rename.ChangeId,
                    ComparisonExceptionalChangeKind.Rename,
                    "BeforeChild",
                    "BeforeChild",
                    "Child",
                    "Child"),
            ],
            ComparisonSubjectChange.Move move =>
            [
                Description(
                    move.ChangeId,
                    ComparisonExceptionalChangeKind.Move,
                    "BeforeChild",
                    "BeforeChild",
                    "Child",
                    "Child"),
            ],
            ComparisonSubjectChange.RenameAndMove renameAndMove =>
            [
                Description(
                    renameAndMove.ChangeId,
                    ComparisonExceptionalChangeKind.RenameAndMove,
                    "BeforeChild",
                    "BeforeChild",
                    "Child",
                    "Child"),
            ],
            _ => [],
        };

    static int Count(string value, string search)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }
        return count;
    }
}
