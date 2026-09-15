using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace DotnetInspector.Presentation.Tests;

public sealed class LibraryApiDiffJsonTests
{
    static ApiSurfaceProjectionLimits GenerousLimits { get; } =
        new(64, 1_000_000, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue);

    [Fact]
    public async Task Serialize_AvailablePopulated_PreservesCompleteNamedContract()
    {
        LibraryApiDiffOutcome outcome = await Execute(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
            GenerousLimits);
        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(outcome);

        using JsonDocument json = SerializeAndRoundTrip(outcome);
        JsonElement root = json.RootElement;
        Assert.Equal("available", root.GetProperty("outcome").GetString());
        Assert.False(root.TryGetProperty("$type", out _));

        JsonElement document = root.GetProperty("document");
        Assert.Equal(
            available.Document.Before.Identity.Name,
            document.GetProperty("before").GetProperty("identity").GetProperty("name").GetString());
        Assert.Equal(
            available.Document.Before.Identity.Version!.ToString(),
            document.GetProperty("before").GetProperty("identity").GetProperty("version").GetString());
        Assert.Equal(
            available.Document.Summary.ChangedTypeCount,
            document.GetProperty("summary").GetProperty("changedTypeCount").GetInt32());

        JsonElement comparison = document.GetProperty("comparison");
        Assert.Equal(
            ComparisonDocument<LibraryApiTypeDiff>.CurrentSchemaVersion,
            comparison.GetProperty("schema_version").GetInt32());
        Assert.Equal(
            "root-relative",
            comparison.GetProperty("subject_coordinate_basis").GetString());

        JsonElement removed = Subject(
            comparison,
            "LibraryApiDiffFixture.RemovedType");
        JsonElement removedPayload = removed.GetProperty("comparison");
        Assert.Equal(
            (int)LibraryApiTypePairKind.Removed,
            removedPayload.GetProperty("pairKind").GetInt32());
        Assert.Equal(JsonValueKind.Null, removedPayload.GetProperty("after").ValueKind);
        JsonElement removedIdentity = removedPayload.GetProperty("before");
        Assert.Equal(
            "LibraryApiDiffFixture",
            removedIdentity
                .GetProperty("definitionName")
                .GetProperty("namespace")
                .GetString());
        Assert.Equal(
            "RemovedType",
            Assert.Single(
                removedIdentity
                    .GetProperty("definitionName")
                    .GetProperty("segments")
                    .EnumerateArray())
                .GetString());

        JsonElement receiver = Subject(
            comparison,
            "LibraryApiDiffFixture.ProjectionReceiver");
        JsonElement receiverPayload = receiver.GetProperty("comparison");
        JsonElement matchedMember = Assert.Single(
            receiverPayload.GetProperty("members").EnumerateArray(),
            member => member
                .GetProperty("relation")
                .GetProperty("match")
                .ValueKind == JsonValueKind.Object);
        JsonElement relation = matchedMember.GetProperty("relation");
        Assert.False(string.IsNullOrWhiteSpace(
            relation.GetProperty("identifier").GetString()));
        Assert.Equal(
            "Transform",
            relation
                .GetProperty("after")
                .GetProperty("anchor")
                .GetProperty("memberName")
                .GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            relation
                .GetProperty("match")
                .GetProperty("tier")
                .GetProperty("id")
                .GetString()));
        Assert.InRange(
            relation.GetProperty("match").GetProperty("confidence").GetInt32(),
            1,
            99);

        JsonElement compatibility = Assert.Single(
            receiverPayload.GetProperty("compatibilityChanges").EnumerateArray(),
            change => change.GetProperty("kind").GetInt32()
                == (int)ChangeKind.MemberAdded);
        Assert.Equal(JsonValueKind.String, compatibility.GetProperty("message").ValueKind);
        Assert.Equal(
            (int)ApiChangeSubjectKind.Member,
            compatibility.GetProperty("subject").GetProperty("kind").GetInt32());
        Assert.Equal(
            "Transform",
            compatibility
                .GetProperty("subject")
                .GetProperty("afterMember")
                .GetProperty("display")
                .GetString());
    }

    [Fact]
    public async Task Serialize_SameLibrary_PreservesEmptyAvailableDocument()
    {
        string path = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        LibraryApiDiffOutcome outcome = await Execute(path, path, GenerousLimits);

        using JsonDocument json = SerializeAndRoundTrip(outcome);
        JsonElement root = json.RootElement;
        Assert.Equal("available", root.GetProperty("outcome").GetString());
        JsonElement document = root.GetProperty("document");
        Assert.Equal(
            0,
            document.GetProperty("summary").GetProperty("changedTypeCount").GetInt32());
        JsonElement comparison = document.GetProperty("comparison");
        Assert.Empty(comparison.GetProperty("subjects").EnumerateArray());
        Assert.Empty(comparison.GetProperty("change_descriptions").EnumerateArray());
        Assert.False(comparison.TryGetProperty("change_kinds", out _));
        Assert.False(comparison.TryGetProperty("change_id", out _));
        Assert.False(comparison.TryGetProperty("comparison", out _));
    }

    [Fact]
    public async Task Serialize_RejectedLogicalMismatch_UsesDistinctOutcomeDiscriminator()
    {
        LibraryApiDiffOutcome outcome = await Execute(
            FixtureCatalog.DiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
            GenerousLimits);

        using JsonDocument json = SerializeAndRoundTrip(outcome);
        JsonElement root = json.RootElement;
        Assert.Equal("rejected", root.GetProperty("outcome").GetString());
        Assert.Equal(
            (int)LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
            root.GetProperty("kind").GetInt32());
        Assert.True(root.GetProperty("before").GetProperty("isComplete").GetBoolean());
        Assert.True(root.GetProperty("after").GetProperty("isComplete").GetBoolean());
        Assert.False(root.TryGetProperty("document", out _));
    }

    [Fact]
    public async Task Serialize_UnavailableTruncation_PreservesEndpointEvidence()
    {
        var limits = new ApiSurfaceProjectionLimits(
            maxParticipants: 1,
            maxTypes: 1,
            maxMembers: 100,
            maxInspectionFailures: 10,
            maxTypeForwarders: 10,
            maxMetadataRows: 10_000);
        LibraryApiDiffOutcome outcome = await Execute(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
            limits);

        using JsonDocument json = SerializeAndRoundTrip(outcome);
        JsonElement root = json.RootElement;
        Assert.Equal("unavailable", root.GetProperty("outcome").GetString());
        Assert.Equal(
            (int)LibraryApiDiffUnavailableKind.BothIncomplete,
            root.GetProperty("kind").GetInt32());
        Assert.All(
            new[] { root.GetProperty("before"), root.GetProperty("after") },
            endpoint =>
            {
                Assert.False(endpoint.GetProperty("isComplete").GetBoolean());
                JsonElement issue = Assert.Single(
                    endpoint.GetProperty("issues").EnumerateArray(),
                    candidate => candidate.GetProperty("issue").GetString()
                        == "truncated");
                Assert.Equal("truncated", issue.GetProperty("issue").GetString());
                Assert.Equal(
                    (int)ApiSurfaceProjectionLimit.Types,
                    issue.GetProperty("truncation").GetProperty("limit").GetInt32());
                Assert.True(
                    issue.GetProperty("truncation").GetProperty("bound").GetInt32() > 0);
            });
    }

    [Fact]
    public void Serialize_UnavailableEndpointIssues_PreservesEveryClosedCase()
    {
        AssemblyReferenceIdentity identity = ReadIdentity(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath());
        var subjectAssembly =
            new AssemblyReferenceIdentity("Subject", new Version(1, 2, 3, 4), null, null);
        var dependencyAssembly =
            new AssemblyReferenceIdentity("Dependency", new Version(5, 6, 7, 8), "en-US", "0011223344556677");
        LibraryApiDiffEndpointIssue[] issues =
        [
            new LibraryApiDiffEndpointIssue.Truncated(
                new ApiSurfaceProjectionTruncation(
                    ApiSurfaceProjectionLimit.Members,
                    10,
                    1,
                    2,
                    3,
                    4,
                    5,
                    6,
                    7,
                    8)),
            new LibraryApiDiffEndpointIssue.Rejected(
                CandidateOpenFailureKind.InvalidImage,
                new InertString(TextPolicy.Field, "invalid image"),
                MetadataRootMalformedReason.InvalidSignature),
            new LibraryApiDiffEndpointIssue.Failed(
                new InertString(TextPolicy.Field, "read failed")),
            new LibraryApiDiffEndpointIssue.InspectionFailures(1)
            {
                Details =
                [
                    new LibraryApiDiffInspectionFailure(
                        new InertString(TextPolicy.Field, "resolve constraint"),
                        0x02000001,
                        MetadataTypeNameFailureMechanism.Signature,
                        new InertString(TextPolicy.Field, "signature"),
                        new InertString(TextPolicy.Field, "depth exceeded"),
                        subjectAssembly,
                        dependencyAssembly),
                ],
            },
            new LibraryApiDiffEndpointIssue.DegradedSignatures(2),
            new LibraryApiDiffEndpointIssue.UnexpectedAssemblyPopulation(3),
        ];
        var outcome = new LibraryApiDiffOutcome.Unavailable(
            LibraryApiDiffUnavailableKind.BeforeIncomplete,
            new LibraryApiDiffEndpointSummary(
                identity,
                ApiSurfaceScope.Public,
                IsComplete: false,
                [.. issues]),
            new LibraryApiDiffEndpointSummary(
                identity,
                ApiSurfaceScope.Public,
                IsComplete: true,
                []));

        using JsonDocument json = SerializeAndRoundTrip(outcome);
        JsonElement[] serializedIssues =
        [
            .. json.RootElement
                .GetProperty("before")
                .GetProperty("issues")
                .EnumerateArray(),
        ];
        string[] expectedIssueKinds =
        [
            "truncated",
            "rejected",
            "failed",
            "inspectionFailures",
            "degradedSignatures",
            "unexpectedAssemblyPopulation",
        ];
        Assert.Equal(
            expectedIssueKinds,
            serializedIssues
                .Select(issue => issue.GetProperty("issue").GetString()!)
                .ToArray());

        JsonElement rejected = serializedIssues[1];
        Assert.Equal(
            (int)CandidateOpenFailureKind.InvalidImage,
            rejected.GetProperty("kind").GetInt32());
        Assert.Equal("invalid image", rejected.GetProperty("detail").GetString());
        Assert.Equal(
            (int)MetadataRootMalformedReason.InvalidSignature,
            rejected.GetProperty("metadataRootReason").GetInt32());

        JsonElement failure = Assert.Single(
            serializedIssues[3].GetProperty("details").EnumerateArray());
        Assert.Equal("resolve constraint", failure.GetProperty("operation").GetString());
        Assert.Equal("signature", failure.GetProperty("kind").GetString());
        Assert.Equal(
            "Subject",
            failure.GetProperty("subjectAssembly").GetProperty("name").GetString());
        Assert.Equal(
            "Dependency",
            failure.GetProperty("dependencyAssembly").GetProperty("name").GetString());
    }

    static JsonDocument SerializeAndRoundTrip(LibraryApiDiffOutcome outcome)
    {
        string json = JsonSerializer.Serialize(
            outcome,
            LibraryApiDiffJsonContext.Default.LibraryApiDiffOutcome);
        LibraryApiDiffOutcome? roundTrip = JsonSerializer.Deserialize(
            json,
            LibraryApiDiffJsonContext.Default.LibraryApiDiffOutcome);
        Assert.Equal(outcome, roundTrip);
        return JsonDocument.Parse(json);
    }

    static JsonElement Subject(JsonElement comparison, string display)
        => Assert.Single(
            comparison.GetProperty("subjects").EnumerateArray(),
            subject => subject.GetProperty("display").GetString() == display);

    static async Task<LibraryApiDiffOutcome> Execute(
        string beforePath,
        string afterPath,
        ApiSurfaceProjectionLimits limits)
    {
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            SinglePathGroup(workspace, beforePath, "Before", policy);
        using AssemblyContextGroup afterGroup =
            SinglePathGroup(workspace, afterPath, "After", policy);

        return LibraryApiDiffInspection.Execute(
            beforeGroup,
            Assert.Single(beforeGroup.Participants),
            afterGroup,
            Assert.Single(afterGroup.Participants),
            ApiSurfaceScope.Public,
            limits).Content;
    }

    static AssemblyContextGroup SinglePathGroup(
        InspectionWorkspace workspace,
        string path,
        string provenanceLabel,
        IAssemblyBindingPolicy policy)
        => workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(provenanceLabel)),
                    policy),
            ]);

    static AssemblyReferenceIdentity ReadIdentity(string path)
    {
        using var reader = new PEReader(File.OpenRead(path));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
    }

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
            => new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
