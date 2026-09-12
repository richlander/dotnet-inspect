using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// The fixed section catalog for the timeline correlation document.
/// </summary>
public static class TimelineSections
{
    public const string Evaluations = "Evaluations";
    public const string Transitions = "Transitions";

    public static SectionCatalog<TimelineDocumentView> Catalog { get; } =
        CreatePipeline().Compile();

    public static DocumentSchema CreateSchema() =>
        TimelineViewContext.Default
            .GetSchemaInfo<TimelineDocumentView>()!
            .ToDocumentSchema();

    private static SectionPipeline<TimelineDocumentView> CreatePipeline()
        => new SectionPipeline<TimelineDocumentView>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<EvaluationRows>()
            .Add<TransitionRows>();

    public sealed class EvaluationRows :
        ISectionDescriptor<TimelineDocumentView>
    {
        public static string Name => Evaluations;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(TimelineDocumentView model) =>
            model.Evaluations is { Count: > 0 };
    }

    public sealed class TransitionRows :
        ISectionDescriptor<TimelineDocumentView>
    {
        public static string Name => Transitions;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(TimelineDocumentView model) =>
            model.Transitions is { Count: > 0 };
    }
}
