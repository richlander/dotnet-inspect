using DotnetInspect.Cli.Views;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

public sealed record LibraryCallUseDiscoveryModel;

public static class LibraryCallUseSections
{
    private static readonly SemanticRowDeclaration[] SemanticRows =
    [
        new(
            ConsumerUseSites,
            GraphLibrariesSectionRows.ConsumerUseSites,
            "Library consumer-use-site",
            "use site",
            "consumer use sites"),
        new(
            ProviderApiTypes,
            GraphLibrariesSectionRows.ProviderApiTypes,
            "Library provider-API-type",
            "type",
            "provider API types"),
        new(
            DirectUseClusters,
            GraphLibrariesSectionRows.DirectUseClusters,
            "Library direct-use cluster",
            "cluster",
            "direct-use clusters"),
        new(
            CallSites,
            GraphLibrariesSectionRows.CallSites,
            "Library call-site",
            "call site",
            "call sites"),
    ];

    private static readonly IReadOnlyDictionary<
        string,
        SemanticRowDeclaration> SemanticRowsBySection =
            SemanticRows.ToDictionary(
                static declaration => declaration.Section,
                StringComparer.OrdinalIgnoreCase);

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

    internal static IReadOnlyList<SemanticRowDeclaration>
        SemanticRowDeclarations => SemanticRows;

    internal static bool TryGetSemanticRows(
        IReadOnlyCollection<string>? selectedSections,
        out SemanticRowDeclaration? declaration)
    {
        if (selectedSections is null)
        {
            declaration =
                SemanticRowsBySection[DirectUseClusters];
            return true;
        }

        string[] distinct =
        [
            .. selectedSections.Distinct(
                StringComparer.OrdinalIgnoreCase),
        ];
        if (distinct is [var section])
        {
            return SemanticRowsBySection.TryGetValue(
                section,
                out declaration);
        }

        declaration = null;
        return false;
    }

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

    internal sealed record SemanticRowDeclaration(
        string Section,
        GraphLibrariesSectionRowBinding Rows,
        string FailureSubject,
        string RequiredItem,
        string AvailableItems)
    {
        internal string FormatFailure(
            int stageNumber,
            int requiredPosition,
            int availableCount) =>
            $"{FailureSubject} row selection stage {stageNumber} requires "
                + $"{RequiredItem} {requiredPosition}, but only "
                + $"{availableCount} {AvailableItems} are available.";
    }
}
