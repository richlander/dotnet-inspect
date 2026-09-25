using System.Text.Json;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Fact]
    public async Task TypeLocatorInspection_ExecutesResidentQueryInEnvelope()
    {
        byte[] image =
            LocatorImage(
                "Same",
                metadata =>
                    LocatorDefinition(
                        metadata,
                        "N",
                        "Widget"));
        await using var workspace = new InspectionWorkspace();
        _ = await LocatorContext(workspace, image);

        InspectionEnvelope<TypeDeclarationLocatorSectionResult> inspection =
            await TypeDeclarationLocatorInspection.ExecuteAsync(
                workspace,
                [
                    new TypeDeclarationLocatorRequest.Pattern(
                        "Widget"),
                ],
                TypeDeclarationLocatorSectionPlan.All,
                includeAll: true,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var result =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                inspection.Content);
        TypeDeclarationLocatorSectionCandidate candidate =
            Assert.Single(Assert.Single(result.Answers).Candidates);
        Assert.Equal("N.Widget", candidate.Name.ToMetadataFullName());
        Assert.IsType<InspectionShare.NonProjectable>(inspection.Share);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public async Task TypeLocatorInspection_RetainsRejectedResultAndDiagnostic()
    {
        await using var workspace = new InspectionWorkspace();

        InspectionEnvelope<TypeDeclarationLocatorSectionResult> inspection =
            await TypeDeclarationLocatorInspection.ExecuteAsync(
                workspace,
                [],
                TypeDeclarationLocatorSectionPlan.All,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var rejected =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Rejected>(
                inspection.Content);
        Assert.Equal(
            TypeDeclarationLocatorRejectionKind.EmptyRequests,
            rejected.RejectionKind);
        InspectionDiagnostic diagnostic =
            Assert.Single(inspection.Diagnostics);
        Assert.Equal(
            InspectionDiagnosticSeverity.Error,
            diagnostic.Severity);
        Assert.Contains(
            nameof(TypeDeclarationLocatorRejectionKind.EmptyRequests),
            diagnostic.Correspondence!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeLocatorSection_VectorsRetainCoverageAndTypedIdentity()
    {
        byte[] image =
            LocatorImage(
                "Same",
                metadata =>
                    LocatorDefinition(
                        metadata,
                        "N",
                        "Widget"));
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext first =
            await LocatorContext(workspace, image);
        WorkspaceDeclarationContext second =
            await LocatorContext(workspace, image);
        TypeDeclarationLocatorResult.Evaluated query =
            Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
                TypeDeclarationLocatorQuery.Execute(
                    CaptureDeclarations(
                        workspace,
                        first,
                        second),
                    [
                        new TypeDeclarationLocatorRequest.Pattern(
                            "Missing"),
                        new TypeDeclarationLocatorRequest.Pattern(
                            "Widget"),
                    ],
                    maxInventoryReads: 1,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        var result =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                TypeDeclarationLocatorSection.Project(
                    query,
                    TypeDeclarationLocatorSectionPlan.All));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Answers.Length);
        Assert.Empty(result.Answers[0].Candidates);
        Assert.Single(result.Answers[1].Candidates);
        Assert.Equal(1, result.Answers[1].AvailableCandidateCount);
        Assert.All(
            result.Answers,
            static answer =>
            {
                Assert.False(answer.IsComplete);
                Assert.True(answer.IsRowSelectionComplete);
            });
        Assert.Equal(
            [
                TypeDeclarationLocatorMemberCoverageKind.Searched,
                TypeDeclarationLocatorMemberCoverageKind.NotEvaluated,
            ],
            result.Members.Select(
                static member => member.Outcome));

        TypeDeclarationLocatorSectionCandidate candidate =
            Assert.Single(result.Answers[1].Candidates);
        Assert.Equal(
            AssemblyTypeDefinitionKind.Class,
            candidate.DefinitionKind);
        Assert.True(candidate.IsDefinitionPublic);
        Assert.Equal(0, candidate.DeclarationOrder);
        var coordinate =
            Assert.IsType<
                TypeDeclarationLocatorSectionCoordinate
                    .PackageCoordinate>(
                    candidate.Coordinate);
        Assert.Equal("Same", coordinate.LibraryIdentity.Name);
        var realization =
            Assert.IsType<
                TypeDeclarationLocatorRealization.PackageRealization>(
                candidate.Observation.Realization);
        Assert.False(string.IsNullOrWhiteSpace(realization.Producer));
        Assert.Equal(
            query.Answers[1].Candidates[0]
                .Observation.Occurrence.ContextOrder,
            candidate.Observation.ContextOrder);

        string json =
            TypeDeclarationLocatorSectionJson.Serialize(result);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("evaluated", root.GetProperty("kind").GetString());
        JsonElement answers = root.GetProperty("answers");
        Assert.Equal(2, answers.GetArrayLength());
        JsonElement serializedCandidate =
            answers[1].GetProperty("candidates")[0];
        Assert.Equal(
            "package",
            serializedCandidate
                .GetProperty("coordinate")
                .GetProperty("kind")
                .GetString());
        Assert.Equal(
            "N",
            serializedCandidate
                .GetProperty("name")
                .GetProperty("namespace")
                .GetString());
        Assert.Equal(
            "package",
            serializedCandidate
                .GetProperty("observation")
                .GetProperty("realization")
                .GetProperty("kind")
                .GetString());
        Assert.False(
            TypeDeclarationLocatorSectionJson.Serialize(
                    result,
                    compact: true)
                .Contains('\n'));

        TypeDeclarationLocatorView view =
            TypeDeclarationLocatorView.Create(result);
        Assert.Equal("Incomplete", view.Completion);
        Assert.Single(view.Results);
        Assert.Equal(2, view.RequestCoverage.Count);
        Assert.Single(view.Gaps);
        string markdown =
            MarkoutSerializer.Serialize(
                view,
                TypeDeclarationLocatorViewContext.Default);
        Assert.Contains(
            "Known declaration choices",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "NotEvaluated",
            markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "://",
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeLocatorSection_StrictWindowFailureIsAtomicButKeepsCoverage()
    {
        byte[] image =
            LocatorImage(
                "Same",
                metadata =>
                    LocatorDefinition(
                        metadata,
                        "N",
                        "Widget"));
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext first =
            await LocatorContext(workspace, image);
        WorkspaceDeclarationContext second =
            await LocatorContext(workspace, image);
        TypeDeclarationLocatorResult.Evaluated query =
            Locate(
                CaptureDeclarations(
                    workspace,
                    first,
                    second),
                new TypeDeclarationLocatorRequest.Pattern("Missing"),
                new TypeDeclarationLocatorRequest.Pattern("Widget"));
        var complete =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                TypeDeclarationLocatorSection.Project(
                    query,
                    TypeDeclarationLocatorSectionPlan.All));
        Assert.Equal(
            2,
            complete.Answers[1].Candidates.Length);
        Assert.Equal(
            complete.Answers[1].Candidates[0].Coordinate,
            complete.Answers[1].Candidates[1].Coordinate);
        Assert.NotEqual(
            complete.Answers[1].Candidates[0]
                .Observation.ContextOrder,
            complete.Answers[1].Candidates[1]
                .Observation.ContextOrder);

        var plan =
            new TypeDeclarationLocatorSectionPlan(
                RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Window(
                            1,
                            1),
                    ]));

        var result =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                TypeDeclarationLocatorSection.Project(query, plan));

        Assert.False(result.IsSuccess);
        Assert.All(
            result.Answers,
            static answer =>
                Assert.Empty(answer.Candidates));
        Assert.Equal(
            2,
            result.Answers[1].AvailableCandidateCount);
        Assert.All(
            result.Answers,
            static answer => Assert.True(answer.IsComplete));
        Assert.Equal(2, result.Members.Length);
        TypeDeclarationLocatorRowSelectionFailure failure =
            Assert.IsType<TypeDeclarationLocatorRowSelectionFailure>(
                result.RowSelectionFailure);
        Assert.Same(result.Answers[0].Identity, failure.Answer);
        Assert.Equal(1, failure.StageNumber);
        Assert.Equal(1, failure.RequiredPosition);
        Assert.Equal(0, failure.AvailableCount);

        TypeDeclarationLocatorView view =
            TypeDeclarationLocatorView.Create(result);
        Assert.Equal("Row selection failed", view.Status);
        Assert.Equal("Complete", view.Completion);
        Assert.Empty(view.Results);
        Assert.Equal(2, view.RequestCoverage.Count);
        Assert.Single(
            view.Gaps,
            static gap => gap.Kind == "Row selection");

        using JsonDocument document =
            JsonDocument.Parse(
                TypeDeclarationLocatorSectionJson.Serialize(result));
        JsonElement root = document.RootElement;
        Assert.Equal(
            2,
            root.GetProperty("answers")[1]
                .GetProperty("available_candidate_count")
                .GetInt32());
        Assert.Equal(
            0,
            root.GetProperty("answers")[1]
                .GetProperty("candidates")
                .GetArrayLength());
        Assert.Equal(
            2,
            root.GetProperty("members").GetArrayLength());
        Assert.Equal(
            1,
            root.GetProperty("row_selection_failure")
                .GetProperty("answer")
                .GetProperty("ordinal")
                .GetInt32());
    }

    [Fact]
    public void TypeLocatorSection_TopRequiresASeparateRankingContract()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeDeclarationLocatorSectionPlan(
                    RowSelectionIntent<string>.Create(
                        [
                            RowSelectionIntentOperation<string>.Top(
                                1),
                        ])));

        Assert.Equal("rowSelection", exception.ParamName);
    }

    [Fact]
    public async Task TypeLocatorSection_RejectedAdmissionRemainsTyped()
    {
        await using var workspace = new InspectionWorkspace();
        TypeDeclarationLocatorResult query =
            TypeDeclarationLocatorQuery.Execute(
                CaptureDeclarations(workspace),
                [],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var result =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Rejected>(
                TypeDeclarationLocatorSection.Project(
                    query,
                    TypeDeclarationLocatorSectionPlan.All));

        Assert.Equal(
            TypeDeclarationLocatorRejectionKind.EmptyRequests,
            result.RejectionKind);
        TypeDeclarationLocatorView view =
            TypeDeclarationLocatorView.Create(result);
        Assert.Equal("Rejected", view.Status);
        Assert.Empty(view.Results);
        Assert.Single(view.Gaps);
        using JsonDocument document =
            JsonDocument.Parse(
                TypeDeclarationLocatorSectionJson.Serialize(result));
        Assert.Equal(
            "rejected",
            document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(
            "EmptyRequests",
            document.RootElement
                .GetProperty("rejection_kind")
                .GetString());
    }
}
