using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Research;

public static partial class ResearchViews
{
    public static MemberProjectionResult ProjectMember(
        MemberProjectionRequest request) =>
        MemberProjectionProducer.Produce(request);

    public static IReadOnlyList<IAnnotation> CollectFacts(
        MetadataSource source,
        string type,
        string method,
        int overloadIndex = 0,
        bool publicOnly = false,
        ResearchFactRegistry? registry = null) =>
        MemberProjectionProducer.CollectFacts(
            source,
            type,
            method,
            overloadIndex,
            publicOnly,
            registry);

    public static IReadOnlyList<IAnnotation> CollectFacts(
        MetadataSource source,
        IrFunction imported,
        ResearchFactRegistry? registry = null) =>
        MemberProjectionProducer.CollectFacts(source, imported, registry);

    public static IReadOnlyList<IAnnotation> CollectFacts(
        MetadataSource source,
        IrFunction imported,
        MemberProjectionAnalysisInput? analysis,
        ResearchFactRegistry? registry = null) =>
        MemberProjectionProducer.CollectFacts(
            source,
            imported,
            analysis,
            registry);

    public static IReadOnlyList<FactRow> CollectFactRows(
        MetadataSource source,
        string type,
        string method,
        int overloadIndex = 0,
        bool publicOnly = false,
        ResearchFactRegistry? registry = null) =>
        MemberProjectionProducer.CollectFactRows(
            source,
            type,
            method,
            overloadIndex,
            publicOnly,
            registry);
}
