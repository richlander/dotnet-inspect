using DotnetInspect.Cli.Views;

namespace DotnetInspect.Cli.Sections;

public sealed record LibraryCallUseDiscoveryModel;

public static class LibraryCallUseSections
{
    public const string ConsumerUseSites =
        LibraryCallUseViewSections.ConsumerUseSites;
    public const string ProviderApiTypes =
        LibraryCallUseViewSections.ProviderApiTypes;
    public const string DirectUseClusters =
        LibraryCallUseViewSections.DirectUseClusters;
    public const string CallSites =
        LibraryCallUseViewSections.CallSites;
    public const string PublicRootPaths =
        LibraryCallUseViewSections.PublicRootPaths;

    public static string[] BareSelectSectionNames { get; } =
    [
        ConsumerUseSites,
        ProviderApiTypes,
    ];

    public static IReadOnlySet<string> ExactOnlySectionNames { get; } =
        new HashSet<string>(
            [PublicRootPaths],
            StringComparer.OrdinalIgnoreCase);

    public static SectionCatalog<LibraryCallUseDiscoveryModel> Catalog
    {
        get;
    } = CreatePipeline().Compile();

    public static SectionPipeline<LibraryCallUseDiscoveryModel>
        CreatePipeline() =>
        new SectionPipeline<LibraryCallUseDiscoveryModel>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<ConsumerUseSitesSection>()
            .Add<ProviderApiTypesSection>()
            .Add<DirectUseClustersSection>()
            .Add<CallSitesSection>()
            .Add<PublicRootPathsSection>()
            .AddBaseCategory(
                SectionCategoryNames.Libraries,
                CallSites,
                ConsumerUseSites,
                DirectUseClusters,
                ProviderApiTypes);

    public sealed class ConsumerUseSitesSection :
        ISectionDescriptor<LibraryCallUseDiscoveryModel>
    {
        public static string Name => ConsumerUseSites;
        public static bool IsExpensive => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(LibraryCallUseDiscoveryModel model) => true;
    }

    public sealed class ProviderApiTypesSection :
        ISectionDescriptor<LibraryCallUseDiscoveryModel>
    {
        public static string Name => ProviderApiTypes;
        public static bool IsExpensive => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(LibraryCallUseDiscoveryModel model) => true;
    }

    public sealed class DirectUseClustersSection :
        ISectionDescriptor<LibraryCallUseDiscoveryModel>
    {
        public static string Name => DirectUseClusters;
        public static bool IsExpensive => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(LibraryCallUseDiscoveryModel model) => true;
    }

    public sealed class CallSitesSection :
        ISectionDescriptor<LibraryCallUseDiscoveryModel>
    {
        public static string Name => CallSites;
        public static bool IsExpensive => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(LibraryCallUseDiscoveryModel model) => true;
    }

    public sealed class PublicRootPathsSection :
        ISectionDescriptor<LibraryCallUseDiscoveryModel>
    {
        public static string Name => PublicRootPaths;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(LibraryCallUseDiscoveryModel model) => true;
    }
}
