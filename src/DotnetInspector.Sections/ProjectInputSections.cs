namespace DotnetInspector.Sections;

public static class ProjectInputSections
{
    public const string Dependencies = "Dependencies";
    public const string RestoredPackages = "Restored Packages";
    public const string Failures = "Failures";

    public static SectionCatalog<ProjectInputDocument> Catalog { get; } =
        new SectionPipeline<ProjectInputDocument>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<DependencySection>()
            .Add<RestoredPackageSection>()
            .Add<FailureSection>()
            .Compile();

    public sealed class DependencySection : ISectionDescriptor<ProjectInputDocument>
    {
        public static string Name => Dependencies;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionShape? Shape => SectionShape.Table;
        public static bool CanRender(ProjectInputDocument model) => true;
    }

    public sealed class RestoredPackageSection : ISectionDescriptor<ProjectInputDocument>
    {
        public static string Name => RestoredPackages;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionShape? Shape => SectionShape.Table;
        public static bool CanRender(ProjectInputDocument model) =>
            model.Kind == ProjectInputKind.RestoredProject;
    }

    public sealed class FailureSection : ISectionDescriptor<ProjectInputDocument>
    {
        public static string Name => Failures;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionShape? Shape => SectionShape.Table;
        public static bool CanRender(ProjectInputDocument model) =>
            !model.Evidence.Failures.IsEmpty;
    }
}
