using DotnetInspector.Queries;

namespace DotnetInspector.Queries.Tests;

public sealed class ProjectQueriesTests
{
    [Fact]
    public void SkillsQuery_OrdersRowsAndPreservesFailures()
    {
        ProjectSkillsResult result = ProjectSkillsQuery.Execute(
            [
                new ProjectSkillData("Zulu", "2.0.0", "skills/z/SKILL.md", 1, "z", "", "z"),
                new ProjectSkillData("Alpha", "1.0.0", "skills/b/SKILL.md", 1, "b", "", "b"),
                new ProjectSkillData("Alpha", "1.0.0", "skills/a/SKILL.md", 1, "a", "", "a"),
            ],
            [
                new ProjectContentFailure("Broken", "skills/missing/SKILL.md", "not found"),
            ]);

        Assert.Equal(
            [
                ("Alpha", "skills/a/SKILL.md"),
                ("Alpha", "skills/b/SKILL.md"),
                ("Zulu", "skills/z/SKILL.md"),
            ],
            result.Skills.Select(skill => (skill.Package, skill.Path)));
        ProjectContentFailure failure = Assert.Single(result.Failures);
        Assert.Equal("Broken", failure.Package);
        Assert.Equal("not found", failure.Reason);
    }

    [Fact]
    public void AgentGuidanceQuery_OrdersDependenciesWithoutDocuments()
    {
        ProjectAgentGuidanceResult result = ProjectAgentGuidanceQuery.Execute(
            [
                new ProjectAgentGuidanceData("Zulu", "2.0.0", "", "", "", null),
                new ProjectAgentGuidanceData("Alpha", "1.0.0", "AGENTS.md", "alpha", "", "body"),
            ]);

        Assert.Equal(["Alpha", "Zulu"], result.Guidance.Select(entry => entry.Package));
        Assert.Equal("body", result.Guidance[0].Content);
        Assert.Null(result.Guidance[1].Content);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void PackageDocsQuery_IsUnboundedAndOrdersDocuments()
    {
        Assert.Equal(InspectionCost.Unbounded, ProjectPackageDocumentsQuery.Definition.Cost);

        ProjectPackageDocumentsResult result = ProjectPackageDocumentsQuery.Execute(
            [
                new ProjectPackageDocumentData("Zulu", "2.0.0", "README.md", 1, "z"),
                new ProjectPackageDocumentData("Alpha", "1.0.0", "PROJECT.md", 1, "a"),
            ]);

        Assert.Equal(["Alpha", "Zulu"], result.Documents.Select(document => document.Package));
        Assert.Empty(result.Failures);
    }
}
