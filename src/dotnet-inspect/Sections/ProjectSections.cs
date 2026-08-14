using DotnetInspector.Queries;
using Markout;

namespace DotnetInspector.Sections;

public static class ProjectSectionNames
{
    public const string Skills = "Skills";
    public const string AgentGuidance = "Agent Guidance";
    public const string PackageDocs = "Package Docs";
}

public sealed record ProjectQueryContext(
    Func<ProjectSkillsResult> ReadSkills,
    Func<ProjectAgentGuidanceResult> ReadAgentGuidance,
    Func<CancellationToken, ValueTask<ProjectPackageDocumentsResult>> ReadPackageDocuments);

public sealed record ProjectSectionCatalog(
    SectionPipeline<ProjectInspection> Pipeline,
    InspectionQueryRegistry<ProjectQueryContext> QueryRegistry);

public sealed class ProjectInspection
{
    public ProjectSkillsResult? Skills { get; private set; }
    public ProjectAgentGuidanceResult? AgentGuidance { get; private set; }
    public ProjectPackageDocumentsResult? PackageDocuments { get; private set; }

    public void Apply(InspectionQueryResults results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.TryGet(ProjectSkillsQuery.Definition, out ProjectSkillsResult? skills))
            Skills = skills;
        if (results.TryGet(
                ProjectAgentGuidanceQuery.Definition,
                out ProjectAgentGuidanceResult? agentGuidance))
        {
            AgentGuidance = agentGuidance;
        }
        if (results.TryGet(
                ProjectPackageDocumentsQuery.Definition,
                out ProjectPackageDocumentsResult? packageDocuments))
        {
            PackageDocuments = packageDocuments;
        }
    }

    public IEnumerable<ProjectContentFailure> Failures()
    {
        if (Skills is not null)
        {
            foreach (ProjectContentFailure failure in Skills.Failures)
                yield return failure;
        }
        if (AgentGuidance is not null)
        {
            foreach (ProjectContentFailure failure in AgentGuidance.Failures)
                yield return failure;
        }
        if (PackageDocuments is not null)
        {
            foreach (ProjectContentFailure failure in PackageDocuments.Failures)
                yield return failure;
        }
    }
}

public static class ProjectSections
{
    public static ProjectSectionCatalog CreateCatalog()
    {
        InspectionQueryRegistry<ProjectQueryContext> queryRegistry = CreateQueryRegistry();
        return new ProjectSectionCatalog(
            CreatePipeline(queryRegistry.CostOf),
            queryRegistry);
    }

    public static SectionPipeline<ProjectInspection> CreatePipeline()
    {
        InspectionQueryRegistry<ProjectQueryContext> queryRegistry = CreateQueryRegistry();
        return CreatePipeline(queryRegistry.CostOf);
    }

    public static InspectionQueryRegistry<ProjectQueryContext> CreateQueryRegistry()
        => new InspectionQueryRegistry<ProjectQueryContext>()
            .Add(ProjectSkillsQuery.Definition, static context => context.ReadSkills())
            .Add(
                ProjectAgentGuidanceQuery.Definition,
                static context => context.ReadAgentGuidance())
            .AddAsync(
                ProjectPackageDocumentsQuery.Definition,
                static (context, cancellationToken) =>
                    context.ReadPackageDocuments(cancellationToken));

    static SectionPipeline<ProjectInspection> CreatePipeline(
        Func<InspectionQueryDefinition, InspectionCost> queryCost)
        => new SectionPipeline<ProjectInspection>()
            .UseCuratedCatalog()
            .UseQueryCosts(queryCost)
            .WithoutComputedPoles()
            .Add<Skills>(
                ProjectSkillsQuery.Definition,
                static model => model.Skills is { Skills.Length: > 0 })
            .Add<AgentGuidance>(
                ProjectAgentGuidanceQuery.Definition,
                static model =>
                    model.AgentGuidance?.Guidance.Any(
                        static item => !string.IsNullOrWhiteSpace(item.Path))
                    == true)
            .Add<PackageDocs>(
                ProjectPackageDocumentsQuery.Definition,
                static _ => true)
            .AddBaseCategory(
                SectionCategoryNames.Project,
                ProjectSectionNames.Skills,
                ProjectSectionNames.AgentGuidance,
                ProjectSectionNames.PackageDocs);

    public static DocumentSchema CreateSchema()
        => new DocumentSchema()
            .Add(
                ProjectSectionNames.Skills,
                "column",
                "Package",
                "Version",
                "Path",
                "Size",
                "Name",
                "Description")
            .Add(
                ProjectSectionNames.AgentGuidance,
                "column",
                "Package",
                "Version",
                "Path",
                "Name",
                "Description")
            .Add(
                ProjectSectionNames.PackageDocs,
                "column",
                "Package",
                "Version",
                "Path",
                "Size");

    public sealed class Skills : ISectionDescriptor<ProjectInspection>
    {
        public static string Name => ProjectSectionNames.Skills;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static string? ScannerKey => null;
        public static bool CanRender(ProjectInspection model) => model.Skills is not null;
    }

    public sealed class AgentGuidance : ISectionDescriptor<ProjectInspection>
    {
        public static string Name => ProjectSectionNames.AgentGuidance;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static string? ScannerKey => null;
        public static bool CanRender(ProjectInspection model) => model.AgentGuidance is not null;
    }

    public sealed class PackageDocs : ISectionDescriptor<ProjectInspection>
    {
        public static string Name => ProjectSectionNames.PackageDocs;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static string? ScannerKey => null;
        public static bool CanRender(ProjectInspection model) => model.PackageDocuments is not null;
    }
}
