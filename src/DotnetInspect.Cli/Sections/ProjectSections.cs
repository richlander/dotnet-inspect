using Markout;

namespace DotnetInspect.Cli.Sections;

public sealed record ProjectDiscoveryModel;

public static class ProjectSections
{
    public const string SkillsName = "Skills";
    public const string PackageReadmeName = "Package README file";

    public static SectionCatalog<ProjectDiscoveryModel> Catalog { get; } =
        CreatePipeline().Compile();

    public static SectionPipeline<ProjectDiscoveryModel> CreatePipeline()
        => new SectionPipeline<ProjectDiscoveryModel>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<Skills>()
            .Add<PackageReadme>()
            .AddBaseCategory(
                SectionCategoryNames.Project,
                Skills.Name,
                PackageReadme.Name);

    public static DocumentSchema CreateSchema()
        => new DocumentSchema()
            .Add(
                Skills.Name,
                "column",
                "Package",
                "Version",
                "Path",
                "Size",
                "Name",
                "Description")
            .Add(
                PackageReadme.Name,
                "column",
                "Package",
                "Version",
                "Path",
                "Size");

    public sealed class Skills : ISectionDescriptor<ProjectDiscoveryModel>
    {
        public static string Name => SkillsName;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static bool CanRender(ProjectDiscoveryModel model) => true;
    }

    public sealed class PackageReadme :
        ISectionDescriptor<ProjectDiscoveryModel>
    {
        public static string Name => PackageReadmeName;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(ProjectDiscoveryModel model) => true;
    }
}
