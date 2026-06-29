using Markout;

namespace DotnetInspector.Sections;

public sealed record DiffDiscoveryModel;

public static class DiffSections
{
    public static SectionPipeline<DiffDiscoveryModel> CreatePipeline()
    {
        return new SectionPipeline<DiffDiscoveryModel>()
            .Add<Changes>()
            .Add<AnalysisDiff>();
    }

    public static DocumentSchema CreateSchema()
    {
        return new DocumentSchema()
            .Add(Changes.Name, "column", "Change", "Type", "Detail")
            .Add(AnalysisDiff.Name, "section", "Member", "Signal", "Old", "New", "Delta", "Shape", "Evidence");
    }

    public sealed class Changes : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Changes";
        public static bool IsExpensive => false;
        public static bool Info => true;
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
}
