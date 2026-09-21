using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.Sections;

public static class DiffHistorySections
{
    public const string Outcome = "Outcome";
    public const string ProbeTrace = "Probe Trace";
    public const string Evaluations = "Evaluations";
    public const string Transitions = "Transitions";
    public const string ChangedVersions = "Changed Versions";
    public const string HistoryCategory = "@History";

    public static SectionCatalog<DiffHistoryDocumentView> Catalog { get; } =
        CreatePipeline().Compile();

    public static DocumentSchema CreateSchema() =>
        DiffHistoryViewContext.Default
            .GetSchemaInfo<DiffHistoryDocumentView>()!
            .ToDocumentSchema();

    private static SectionPipeline<DiffHistoryDocumentView> CreatePipeline() =>
        new SectionPipeline<DiffHistoryDocumentView>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<OutcomeRows>()
            .Add<ProbeTraceRows>()
            .Add<EvaluationRows>()
            .Add<TransitionRows>()
            .Add<ChangedVersionRows>()
            .AddCategory(
                HistoryCategory,
                Outcome,
                ProbeTrace);

    public sealed class OutcomeRows :
        ISectionDescriptor<DiffHistoryDocumentView>
    {
        public static string Name => Outcome;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DiffHistoryDocumentView model) => true;
    }

    public sealed class ProbeTraceRows :
        ISectionDescriptor<DiffHistoryDocumentView>
    {
        public static string Name => ProbeTrace;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DiffHistoryDocumentView model) => true;
    }

    public sealed class EvaluationRows :
        ISectionDescriptor<DiffHistoryDocumentView>
    {
        public static string Name => Evaluations;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DiffHistoryDocumentView model) => true;
    }

    public sealed class TransitionRows :
        ISectionDescriptor<DiffHistoryDocumentView>
    {
        public static string Name => Transitions;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DiffHistoryDocumentView model) => true;
    }

    public sealed class ChangedVersionRows :
        ISectionDescriptor<DiffHistoryDocumentView>
    {
        public static string Name => ChangedVersions;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DiffHistoryDocumentView model) => true;
    }
}
