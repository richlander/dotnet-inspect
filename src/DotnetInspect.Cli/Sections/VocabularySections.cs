using DotnetInspector.Vocabulary;

namespace DotnetInspect.Cli.Sections;

public static class VocabularySections
{
    public static SectionCatalog<VocabularyDocument> Catalog { get; } =
        CreatePipeline().Compile();

    public static SectionPipeline<VocabularyDocument> CreatePipeline()
        => new SectionPipeline<VocabularyDocument>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<Index>()
            .Add<Accessibility>()
            .Add<StyleTiers>()
            .Add<StyleChoices>()
            .Add<BodyKinds>()
            .AddBaseCategory(
                SectionCategoryNames.Vocabulary,
                Index.Name,
                Accessibility.Name,
                StyleTiers.Name,
                StyleChoices.Name,
                BodyKinds.Name)
            .AddCategory(
                SectionCategoryNames.Api,
                Accessibility.Name)
            .AddCategory(
                SectionCategoryNames.Decompiler,
                BodyKinds.Name,
                StyleChoices.Name,
                StyleTiers.Name);

    public sealed class Index : ISectionDescriptor<VocabularyDocument>
    {
        public static string Name => VocabularyCatalog.SectionsSection;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static bool CanRender(VocabularyDocument model) =>
            Contains(model, Name);
    }

    public sealed class Accessibility :
        ISectionDescriptor<VocabularyDocument>
    {
        public static string Name => VocabularyCatalog.AccessibilitySection;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static bool CanRender(VocabularyDocument model) =>
            Contains(model, Name);
    }

    public sealed class StyleTiers : ISectionDescriptor<VocabularyDocument>
    {
        public static string Name => VocabularyCatalog.StyleTiersSection;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static bool CanRender(VocabularyDocument model) =>
            Contains(model, Name);
    }

    public sealed class StyleChoices :
        ISectionDescriptor<VocabularyDocument>
    {
        public static string Name => VocabularyCatalog.StyleChoicesSection;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Informative;
        public static bool CanRender(VocabularyDocument model) =>
            Contains(model, Name);
    }

    public sealed class BodyKinds : ISectionDescriptor<VocabularyDocument>
    {
        public static string Name => VocabularyCatalog.BodyKindsSection;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static bool CanRender(VocabularyDocument model) =>
            Contains(model, Name);
    }

    private static bool Contains(VocabularyDocument model, string name)
        => model.Sections.Any(
            section => section.Name.Equals(
                name,
                StringComparison.OrdinalIgnoreCase));
}
