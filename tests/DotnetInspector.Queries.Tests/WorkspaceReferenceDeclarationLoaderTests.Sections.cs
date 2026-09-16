using System.Text.Json;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceReferenceDeclarationLoaderTests
{
    [Fact]
    public async Task ReferenceSection_PreservesOriginTokensVectorsAndSafeDisplay()
    {
        byte[] image = WorkspaceReferenceTestData.Assembly("Widget", "N", "Widget");
        KeyValuePair<string, byte[]>[] entries =
            [WorkspaceReferenceTestData.Entry("ref/net11.0/Widget.dll", image)];
        await using var a = WorkspaceReferenceSourceFixture.Create("sections-a", entries);
        await using var b = WorkspaceReferenceSourceFixture.Create("sections-b", entries);
        await using var workspace = new InspectionWorkspace();
        var sourceResult = await a.Source.RealizeAsync(Coordinate(), ExactAssembly(image), Work(),
            a.IssueOperation(TestContext.Current.CancellationToken));
        var realization = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded>(sourceResult).Value;
        var first = WorkspaceReferenceDeclarationLoader.Admit(
            workspace, realization, cancellationToken: TestContext.Current.CancellationToken);
        var second = WorkspaceReferenceDeclarationLoader.Admit(
            workspace, realization, cancellationToken: TestContext.Current.CancellationToken);
        var third = await WorkspaceReferenceDeclarationLoader.LoadAsync(
            workspace, b.Source, Coordinate(), ExactAssembly(image), Work(),
            b.IssueOperation(TestContext.Current.CancellationToken));
        var query = TypeDeclarationLocatorQuery.Execute(Capture(workspace, first, second, third),
            [new TypeDeclarationLocatorRequest.Pattern("Missing"),
                new TypeDeclarationLocatorRequest.Pattern("N.Widget")],
            cancellationToken: TestContext.Current.CancellationToken);
        var section = Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
            TypeDeclarationLocatorSection.Project(query, TypeDeclarationLocatorSectionPlan.All));
        Assert.All(section.Answers, answer => Assert.True(answer.IsComplete));
        Assert.Empty(section.Answers[0].Candidates);
        var candidates = section.Answers[1].Candidates;
        Assert.Equal(3, candidates.Length);
        Assert.All(candidates, candidate => Assert.Equal(candidates[0].Coordinate, candidate.Coordinate));
        Assert.Equal([0, 1, 2], candidates.Select(candidate => candidate.Observation.ContextOrder));
        var references = candidates.Select(candidate => Assert.IsType<
            TypeDeclarationLocatorRealization.PlatformReferenceRealization>(
                candidate.Observation.Realization)).ToArray();
        Assert.Equal([1, 1, 2], references.Select(reference => reference.Source.ContentGeneration));
        Assert.Equal([1, 1, 2], references.Select(reference => reference.Source.Source.Association));
        Assert.Equal(["sections-a", "sections-a", "sections-b"],
            references.Select(reference => reference.Source.Authority));
        Assert.All(references, reference =>
        {
            Assert.Equal("ref/net11.0/Widget.dll", reference.Path);
            Assert.Equal(Coordinate().Target.Version.Value, reference.Source.Version);
            Assert.Equal("net11.0", reference.Source.Framework);
            Assert.Equal("Widget", reference.Source.RequestedAssembly?.Name);
        });
        Assert.Equal(realization.Generation.Name, references[0].Source.Generation);
        Assert.Equal(realization.Source.Producer.Key, references[0].Source.Source.Producer);
        Assert.NotEqual(references[0].Source.Source.Producer, references[2].Source.Source.Producer);

        using JsonDocument json = JsonDocument.Parse(TypeDeclarationLocatorSectionJson.Serialize(section));
        JsonElement answers = json.RootElement.GetProperty("answers");
        Assert.Equal(0, answers[0].GetProperty("candidates").GetArrayLength());
        JsonElement rows = answers[1].GetProperty("candidates");
        Assert.Equal(3, rows.GetArrayLength());
        JsonElement referenceJson = rows[0].GetProperty("observation").GetProperty("realization");
        Assert.Equal("platform-reference", referenceJson.GetProperty("kind").GetString());
        Assert.Equal("ref/net11.0/Widget.dll", referenceJson.GetProperty("path").GetString());
        Assert.Equal(1, referenceJson.GetProperty("source").GetProperty("content_generation").GetInt32());
        Assert.Equal("Widget", referenceJson.GetProperty("source")
            .GetProperty("requested_assembly").GetProperty("name").GetString());
        Assert.Equal("platform", rows[0].GetProperty("coordinate").GetProperty("kind").GetString());

        var view = TypeDeclarationLocatorView.Create(section);
        Assert.Equal(3, view.Results.Count);
        Assert.All(view.Results, row => Assert.Contains("Reference", row.Context, StringComparison.Ordinal));
        string markdown = MarkoutSerializer.Serialize(view, TypeDeclarationLocatorViewContext.Default);
        Assert.Contains("sections-a", markdown, StringComparison.Ordinal);
        Assert.Contains("sections-b", markdown, StringComparison.Ordinal);
        Assert.Contains("ref/net11.0/Widget.dll", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReferenceSection_RetainsSourceAndImageFailuresWithoutRows()
    {
        byte[] image = WorkspaceReferenceTestData.Assembly("Widget", "N", "Widget");
        KeyValuePair<string, byte[]>[] entries =
            [WorkspaceReferenceTestData.Entry("ref/net11.0/Widget.dll", image)];
        await using var healthy = WorkspaceReferenceSourceFixture.Create("sections-healthy", entries);
        await using var unavailable = WorkspaceReferenceSourceFixture.Create("sections-unavailable", entries,
            payloadFailure: PackageSourceFailureKind.Transport);
        await using var workspace = new InspectionWorkspace();
        var sourceResult = await healthy.Source.RealizeAsync(Coordinate(), ExactAssembly(image), Work(),
            healthy.IssueOperation(TestContext.Current.CancellationToken));
        var realization = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded>(sourceResult).Value;
        var admitted = WorkspaceReferenceDeclarationLoader.Admit(
            workspace, realization, cancellationToken: TestContext.Current.CancellationToken);
        var bounded = WorkspaceReferenceDeclarationLoader.Admit(
            workspace, realization, new() { MaxRetainedImageBytes = 0 }, TestContext.Current.CancellationToken);
        var failed = await WorkspaceReferenceDeclarationLoader.LoadAsync(
            workspace, unavailable.Source, Coordinate(), ExactAssembly(image), Work(),
            unavailable.IssueOperation(TestContext.Current.CancellationToken));
        using var http = new HttpClient();
        var empty = await WorkspaceContextLoader.LoadDeclarationContextAsync(workspace, new(),
            new() { HttpClient = http, SourceAuthorization = healthy.Authorization, PackageStore = healthy.Store },
            TestContext.Current.CancellationToken);
        var original = Assert.IsType<WorkspaceDeclarationFailure.ReferenceSource>(Assert.Single(failed.Receipt.Failures));
        var section = Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
            TypeDeclarationLocatorSection.Project(
                Locate(Capture(workspace, admitted, bounded, failed, empty), "Missing"),
                TypeDeclarationLocatorSectionPlan.All));
        Assert.Empty(Assert.Single(section.Answers).Candidates);
        Assert.False(section.Answers[0].IsComplete);
        var imageFailure = Assert.IsType<TypeDeclarationLocatorContextFailure.ReferenceImage>(
            Assert.Single(section.Contexts[1].Failures));
        Assert.Equal(CandidateOpenFailureKind.ResourceBudget, imageFailure.Failure.Kind);
        Assert.Equal("ref/net11.0/Widget.dll", imageFailure.Path);
        var sourceFailure = Assert.IsType<TypeDeclarationLocatorContextFailure.ReferenceSource>(
            Assert.Single(section.Contexts[2].Failures));
        Assert.Equal(original.Kind, sourceFailure.Outcome);
        Assert.Equal(original.Diagnostic.Kind, sourceFailure.Code);
        Assert.Equal(original.Diagnostic.Summary, sourceFailure.Message);
        Assert.Equal(original.Generation.Name, sourceFailure.Generation);
        Assert.NotEmpty(sourceFailure.PackageFailures);
        Assert.Equal(original.Diagnostic.PackageFailures.Select(failure => (failure.Kind, failure.Message)),
            sourceFailure.PackageFailures.Select(failure => (failure.Kind, failure.Message)));
        Assert.Equal(PackageSourceFailureKind.Transport,
            Assert.Single(sourceFailure.PackageFailures).SourceFailure?.Kind);
        Assert.Equal(WorkspaceContextLoadFailureKind.EmptyContext,
            Assert.IsType<TypeDeclarationLocatorContextFailure.ContextLoad>(
                Assert.Single(section.Contexts[3].Failures)).Code);

        using JsonDocument json = JsonDocument.Parse(TypeDeclarationLocatorSectionJson.Serialize(section));
        JsonElement contexts = json.RootElement.GetProperty("contexts");
        Assert.Equal("reference-image", contexts[1].GetProperty("failures")[0].GetProperty("kind").GetString());
        Assert.Equal("reference-source", contexts[2].GetProperty("failures")[0].GetProperty("kind").GetString());
        Assert.Equal("Transport", contexts[2].GetProperty("failures")[0]
            .GetProperty("package_failures")[0].GetProperty("source_failure").GetProperty("kind").GetString());
        Assert.Equal("context-load", contexts[3].GetProperty("failures")[0].GetProperty("kind").GetString());
        var view = TypeDeclarationLocatorView.Create(section);
        Assert.Equal("Incomplete", view.Completion);
        Assert.Empty(view.Results);
        Assert.Equal(3, view.Gaps.Count);
        Assert.Contains(view.Gaps, gap => gap.Kind == "ResourceBudget");
        Assert.Contains(view.Gaps, gap => gap.Kind == "EmptyContext");
    }
}
