namespace DotnetInspect.Cli.Sections;

public sealed record EcosystemDiscoveryModel;

public static class EcosystemSections
{
    public const string EcosystemsSection = "Ecosystems";
    public const string InfoSection = "Ecosystem Info";
    public const string NamespaceHintsSection = "Namespace Hints";
    public const string CorePackagesSection = "Core Packages";
    public const string ToolPackagesSection = "Tool Packages";
    public const string IntegrationsSection = "Integrations";
    public const string DemosSection = "Demos";
    public const string PruningSection = "Pruning";

    public static SectionCatalog<EcosystemDiscoveryModel> CatalogWide { get; } =
        CreateCatalogWidePipeline().Compile();

    public static SectionCatalog<EcosystemDiscoveryModel> Focused { get; } =
        CreateFocusedPipeline().Compile();

    public static SectionCatalog<EcosystemDiscoveryModel> DotNet { get; } =
        CreateDotNetPipeline().Compile();

    public static SectionPipeline<EcosystemDiscoveryModel>
        CreateCatalogWidePipeline() =>
        CreatePipeline<CatalogIndex>(includePruning: false);

    public static SectionPipeline<EcosystemDiscoveryModel>
        CreateFocusedPipeline() =>
        CreatePipeline<PackInfo>(includePruning: false);

    public static SectionPipeline<EcosystemDiscoveryModel>
        CreateDotNetPipeline() =>
        CreatePipeline<PackInfo>(includePruning: true);

    private static SectionPipeline<EcosystemDiscoveryModel>
        CreatePipeline<TIdentity>(bool includePruning)
        where TIdentity : ISectionDescriptor<EcosystemDiscoveryModel>
    {
        var pipeline = new SectionPipeline<EcosystemDiscoveryModel>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<TIdentity>()
            .Add<NamespaceHints>()
            .Add<CorePackages>()
            .Add<ToolPackages>()
            .Add<Integrations>()
            .Add<Demos>();

        if (includePruning)
            pipeline.Add<Pruning>();

        return pipeline.AddBaseCategory(
            SectionCategoryNames.Ecosystem,
            pipeline.SelectableSectionNames);
    }

    public sealed class CatalogIndex :
        ISectionDescriptor<EcosystemDiscoveryModel>
    {
        public static string Name => EcosystemsSection;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static bool CanRender(EcosystemDiscoveryModel model) => true;
    }

    public sealed class PackInfo :
        ISectionDescriptor<EcosystemDiscoveryModel>
    {
        public static string Name => InfoSection;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static bool CanRender(EcosystemDiscoveryModel model) => true;
    }

    public sealed class NamespaceHints :
        ISectionDescriptor<EcosystemDiscoveryModel>
    {
        public static string Name => NamespaceHintsSection;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool Info => true;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Informative;
        public static bool CanRender(EcosystemDiscoveryModel model) => true;
    }

    public sealed class CorePackages :
        ISectionDescriptor<EcosystemDiscoveryModel>
    {
        public static string Name => CorePackagesSection;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool Info => true;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Informative;
        public static bool CanRender(EcosystemDiscoveryModel model) => true;
    }

    public sealed class ToolPackages :
        ISectionDescriptor<EcosystemDiscoveryModel>
    {
        public static string Name => ToolPackagesSection;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static bool CanRender(EcosystemDiscoveryModel model) => true;
    }

    public sealed class Integrations :
        ISectionDescriptor<EcosystemDiscoveryModel>
    {
        public static string Name => IntegrationsSection;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool Info => true;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Informative;
        public static bool CanRender(EcosystemDiscoveryModel model) => true;
    }

    public sealed class Demos :
        ISectionDescriptor<EcosystemDiscoveryModel>
    {
        public static string Name => DemosSection;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool Info => true;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Informative;
        public static bool CanRender(EcosystemDiscoveryModel model) => true;
    }

    public sealed class Pruning :
        ISectionDescriptor<EcosystemDiscoveryModel>
    {
        public static string Name => PruningSection;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static bool CanRender(EcosystemDiscoveryModel model) => true;
    }
}
