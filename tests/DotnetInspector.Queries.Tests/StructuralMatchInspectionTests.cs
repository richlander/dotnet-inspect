using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Presentation;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

using ILInspector.Analysis;
using ILInspector.Analysis.StructuralCloneFixtures;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

public sealed class StructuralMatchInspectionTests
{
    [Fact]
    public void Execute_ReturnsResearchResultInCompletedEnvelope()
    {
        string assemblyPath =
            typeof(StructuralCloneFixture).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var image = new PEReader(stream);
        MetadataReader reader = image.GetMetadataReader();

        InspectionEnvelope<ResearchMatchResult> inspection =
            StructuralMatchInspection.Execute(
                assemblyPath,
                Method(
                    reader,
                    nameof(StructuralCloneFixture.ExactPositiveA)),
                Method(
                    reader,
                    nameof(StructuralCloneFixture.ExactPositiveB)));

        Assert.Equal(
            StructuralCloneDisposition.Completed,
            inspection.Content.Document.Disposition);
        Assert.Equal(
            StructuralCloneRelation.Exact,
            inspection.Content.Document.Relation);
        Assert.Equal(
            ResearchMatchOutcome.RenamedOrMoved,
            inspection.Content.Outcome);
        Assert.Empty(inspection.Diagnostics);
        InspectionShare.NonProjectable share =
            Assert.IsType<InspectionShare.NonProjectable>(
                inspection.Share);
        Assert.Equal("structural-match/endpoints", share.Path);

        StructuralMatchView view =
            StructuralMatchPresentation.Create(
                nameof(StructuralCloneFixture.ExactPositiveA),
                nameof(StructuralCloneFixture.ExactPositiveB),
                inspection.Content);
        Assert.Equal("Completed", view.Disposition);
        Assert.Equal("Exact", view.Relation);
        Assert.Equal("RenamedOrMoved", view.Outcome);
        Assert.Null(view.Blockers);
    }

    [Fact]
    public void Presentation_ContainsUntrustedDisplayAndBlockerText()
    {
        const string unsafeText = "unsafe\u202Etext";
        var blocker = new StructuralMatchBlockerRow(
            "Kind",
            "Side",
            unsafeText);

        Assert.DoesNotContain('\u202E', blocker.Detail);

        string assemblyPath =
            typeof(StructuralCloneFixture).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var image = new PEReader(stream);
        MetadataReader reader = image.GetMetadataReader();
        ResearchMatchResult result = ResearchMatch.Compare(
            "fixture.dll",
            image,
            Method(
                reader,
                nameof(StructuralCloneFixture.ExactPositiveA)),
            Method(
                reader,
                nameof(StructuralCloneFixture.ExactPositiveB)));

        StructuralMatchView view =
            StructuralMatchPresentation.Create(
                unsafeText,
                unsafeText,
                result);

        Assert.DoesNotContain('\u202E', view.Title);
    }

    static MethodDefinitionHandle Method(
        MetadataReader reader,
        string name)
    {
        MethodDefinitionHandle[] matches =
        [
            .. reader.MethodDefinitions.Where(handle =>
                reader.StringComparer.Equals(
                    reader.GetMethodDefinition(handle).Name,
                    name)),
        ];
        return Assert.Single(matches);
    }
}
