using Markout;

namespace DotnetInspector.Sections;

public sealed record DiffDiscoveryModel;

public static class DiffSections
{
    public static SectionPipeline<DiffDiscoveryModel> CreatePipeline()
    {
        return new SectionPipeline<DiffDiscoveryModel>()
            .Add<Changes>()
            .Add<AnalysisDiff>()
            .Add<ImplementationDiff>()
            .Add<FindingTransitions>();
    }

    public static DocumentSchema CreateSchema()
    {
        return new DocumentSchema()
            .Add(Changes.Name, "column", "Change", "Classification", "Type", "Member", "Kind", "Detail", "Old", "New")
            .Add(AnalysisDiff.Name, "section", "Member", "Signal", "Old", "New", "Delta", "Shape", "Evidence")
            .Add(ImplementationDiff.Name, "section", "Member", "Mechanism", "Difference", "Change", "Evidence")
            .Add(FindingTransitions.Name, "section", "Transition", "Finding", "Target", "From", "To", "Old", "New", "Detail");
    }

    public sealed class Changes : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Changes";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => null;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }

    public sealed class FindingTransitions : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Finding Transitions";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }

    public sealed class AnalysisDiff : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Analysis Diff";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }

    public sealed class ImplementationDiff : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Implementation Diff";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }
}
