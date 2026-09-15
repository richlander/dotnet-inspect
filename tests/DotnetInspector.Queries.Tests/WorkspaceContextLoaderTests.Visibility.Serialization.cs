using System.Reflection.Metadata;
using System.Text.Json;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Fact]
    public async Task Visibility_JsonAndMarkoutKeepZeroOneManyRowsAndAttributedUnknowns()
    {
        byte[] firstImage =
            LocatorImage(
                "JsonA",
                metadata =>
                {
                    LocatorDefinition(metadata, "N", "Unique");
                    LocatorDefinition(metadata, "N", "Ordinary");
                    TypeDefinitionHandle hidden =
                        LocatorDefinition(metadata, "N", "Hidden");
                    LocatorDiscoveryAttribute(
                        metadata,
                        hidden,
                        obsolete: false);
                    VisibilityForwarder(metadata, "N", "Forwarded");
                });
        byte[] secondImage =
            LocatorImage(
                "JsonB",
                metadata =>
                {
                    LocatorDefinition(metadata, "N", "Ordinary");
                    VisibilityForwarder(metadata, "N", "Forwarded");
                });
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext first =
            await LocatorContext(workspace, firstImage);
        WorkspaceDeclarationContext second =
            await LocatorContext(workspace, secondImage);
        TypeDeclarationLocatorResult.Evaluated query =
            LocateAll(
                CaptureDeclarations(workspace, first, second),
                new TypeDeclarationLocatorRequest.Pattern("Missing"),
                new TypeDeclarationLocatorRequest.Pattern("Unique"),
                new TypeDeclarationLocatorRequest.Pattern("*"),
                new TypeDeclarationLocatorRequest.Pattern("Forwarded"));
        TypeDeclarationLocatorSectionResult.Evaluated @default =
            ProjectVisibility(
                query,
                TypeDeclarationVisibilityPlan.Default);
        Assert.True(@default.IsSuccess);
        Assert.False(@default.Answers[3].IsComplete);
        Assert.True(@default.Answers[3].IsEvaluationComplete);
        Assert.Empty(@default.Answers[3].Candidates);
        Assert.Equal(
            2,
            @default.Answers[3].Visibility!
                .UnknownCandidates.Length);

        foreach (bool compact in new[] { false, true })
        {
            string json =
                TypeDeclarationLocatorSectionJson.Serialize(
                    @default,
                    compact);
            Assert.Equal(compact, !json.Contains('\n'));
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.Equal("evaluated", root.GetProperty("kind").GetString());
            JsonElement visibility = root.GetProperty("visibility");
            Assert.Equal(
                "Default",
                visibility.GetProperty("preset").GetString());
            Assert.Equal(
                0,
                visibility.GetProperty("predicates").GetArrayLength());
            Assert.Equal(
                3,
                visibility.GetProperty("effective_predicates")
                    .GetArrayLength());
            Assert.True(
                visibility.GetProperty(
                        "exclude_compiler_generated_names")
                    .GetBoolean());

            JsonElement answers = root.GetProperty("answers");
            Assert.Equal(4, answers.GetArrayLength());
            Assert.Equal(
                0,
                answers[0].GetProperty("candidates")
                    .GetArrayLength());
            Assert.Equal(
                1,
                answers[1].GetProperty("candidates")
                    .GetArrayLength());
            Assert.Equal(
                3,
                answers[2].GetProperty("candidates")
                    .GetArrayLength());
            Assert.Equal(
                0,
                answers[3].GetProperty("candidates")
                    .GetArrayLength());
            Assert.False(
                answers[3].GetProperty("is_complete").GetBoolean());
            Assert.True(
                answers[3].GetProperty("is_evaluation_complete")
                    .GetBoolean());

            JsonElement unique =
                answers[1].GetProperty("candidates")[0];
            Assert.True(
                unique.GetProperty("is_public_surface")
                    .GetBoolean());
            JsonElement uniqueAttributes =
                unique.GetProperty("discovery_attributes");
            Assert.False(
                uniqueAttributes.GetProperty(
                        "is_editor_browsable_never")
                    .GetBoolean());
            Assert.False(
                uniqueAttributes.GetProperty("is_obsolete")
                    .GetBoolean());

            JsonElement starCoverage =
                answers[2].GetProperty("visibility");
            Assert.Equal(
                6,
                starCoverage.GetProperty("input_candidate_count")
                    .GetInt32());
            Assert.Equal(
                1,
                starCoverage.GetProperty(
                        "excluded_candidate_count")
                    .GetInt32());
            JsonElement starUnknown =
                starCoverage.GetProperty("unknown_candidates");
            Assert.Equal(2, starUnknown.GetArrayLength());
            Assert.Equal(
                2,
                starUnknown.EnumerateArray()
                    .Select(
                        static unknown =>
                            (
                                unknown.GetProperty("candidate")
                                    .GetProperty("observation")
                                    .GetProperty("context_order")
                                    .GetInt32(),
                                unknown.GetProperty("candidate")
                                    .GetProperty("observation")
                                    .GetProperty("member_order")
                                    .GetInt32()))
                    .Distinct()
                    .Count());
            foreach (JsonElement unknown
                in starUnknown.EnumerateArray())
            {
                JsonElement candidate =
                    unknown.GetProperty("candidate");
                Assert.Equal(
                    "Forwarded",
                    candidate.GetProperty("name")
                        .GetProperty("segments")[0]
                        .GetString());
                Assert.True(
                    candidate.GetProperty("is_public_surface")
                        .GetBoolean());
                Assert.False(
                    candidate.TryGetProperty(
                        "discovery_attributes",
                        out _));
                Assert.Equal(
                    [
                        "EditorBrowsableNever",
                        "Obsolete",
                    ],
                    unknown.GetProperty("facets")
                        .EnumerateArray()
                        .Select(
                            static facet => facet.GetString()));
            }

            JsonElement forwardedUnknown =
                answers[3].GetProperty("visibility")
                    .GetProperty("unknown_candidates");
            Assert.Equal(2, forwardedUnknown.GetArrayLength());
            Assert.Equal(
                starUnknown.EnumerateArray()
                    .Select(VisibilityJsonObservation),
                forwardedUnknown.EnumerateArray()
                    .Select(VisibilityJsonObservation));
        }

        TypeDeclarationLocatorSectionResult.Evaluated all =
            ProjectVisibility(
                query,
                TypeDeclarationVisibilityPlan.All);
        TypeDeclarationLocatorCandidate sourceHidden =
            Assert.Single(
                query.Answers[2].Candidates,
                static candidate =>
                    candidate.Name.Segments is ["Hidden"]);
        TypeDeclarationLocatorSectionCandidate projectedHidden =
            Assert.Single(
                all.Answers[2].Candidates,
                static candidate =>
                    candidate.Name.Segments is ["Hidden"]);
        Assert.Same(
            sourceHidden.DiscoveryAttributes,
            projectedHidden.DiscoveryAttributes);
        Assert.True(projectedHidden.IsPublicSurface);
        using (JsonDocument document =
            JsonDocument.Parse(
                TypeDeclarationLocatorSectionJson.Serialize(
                    all,
                    compact: true)))
        {
            JsonElement hidden =
                Assert.Single(
                    document.RootElement.GetProperty("answers")[2]
                        .GetProperty("candidates")
                        .EnumerateArray(),
                    static candidate =>
                        candidate.GetProperty("name")
                            .GetProperty("segments")[0]
                            .GetString() == "Hidden");
            Assert.True(
                hidden.GetProperty("is_public_surface")
                    .GetBoolean());
            JsonElement attributes =
                hidden.GetProperty("discovery_attributes");
            Assert.True(
                attributes.GetProperty(
                        "is_editor_browsable_never")
                    .GetBoolean());
            Assert.False(
                attributes.GetProperty("is_obsolete")
                    .GetBoolean());
        }

        TypeDeclarationLocatorView view =
            TypeDeclarationLocatorView.Create(@default);
        Assert.Equal("Evaluated", view.Status);
        Assert.Equal("Incomplete", view.Completion);
        Assert.Equal(4, view.RequestCoverage.Count);
        Assert.Equal("0", view.RequestCoverage[3].Selected);
        Assert.Equal("No", view.RequestCoverage[3].Complete);
        Assert.Equal(4, view.Gaps.Count);
        Assert.All(
            view.Gaps,
            static gap =>
                Assert.Equal("Visibility unknown", gap.Kind));
        Assert.Equal(
            4,
            view.Gaps.Select(static gap => gap.Scope)
                .Distinct()
                .Count());
        Assert.Equal(
            @default.Answers[2].Visibility!.UnknownCandidates
                .Select(
                    static unknown =>
                        VisibilityExpectedGapScope("*", unknown))
                .Concat(
                    @default.Answers[3].Visibility!.UnknownCandidates
                        .Select(
                            static unknown =>
                                VisibilityExpectedGapScope(
                                    "Forwarded",
                                    unknown)))
                .Order(StringComparer.Ordinal),
            view.Gaps.Select(static gap => gap.Scope)
                .Order(StringComparer.Ordinal));
        Assert.Contains(
            view.Gaps,
            static gap =>
                gap.Scope.Contains("*;", StringComparison.Ordinal));
        Assert.Contains(
            view.Gaps,
            static gap =>
                gap.Scope.Contains(
                    "Forwarded;",
                    StringComparison.Ordinal));
        string markdown =
            MarkoutSerializer.Serialize(
                view,
                TypeDeclarationLocatorViewContext.Default);
        Assert.Contains(
            "Incomplete",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "Visibility unknown",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "Forwarded",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "context 1",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "context 2",
            markdown,
            StringComparison.Ordinal);
    }

    static (int ContextOrder, int MemberOrder) VisibilityJsonObservation(
        JsonElement unknown)
    {
        JsonElement observation =
            unknown.GetProperty("candidate")
                .GetProperty("observation");
        return (
            observation.GetProperty("context_order").GetInt32(),
            observation.GetProperty("member_order").GetInt32());
    }

    static string VisibilityExpectedGapScope(
        string request,
        TypeDeclarationVisibilityUnknownCandidate unknown)
    {
        TypeDeclarationLocatorObservation observation =
            unknown.Candidate.Observation;
        var origin =
            Assert.IsType<
                TypeDeclarationLocatorRealization.PackageRealization>(
                    observation.Realization);
        return $"{request}; {observation.AssemblyIdentity.Name} "
            + $"(context {observation.ContextOrder + 1}, "
            + $"member {observation.MemberOrder + 1}); "
            + origin.Producer;
    }
}
