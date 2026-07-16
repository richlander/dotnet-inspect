namespace ILInspector.Research;

public static partial class ResearchViews
{
    public static ILOffsetProjectionOutcome ProjectILOffset(ILOffsetProjectionRequest request)
        => ILOffsetProjectionProducer.Produce(request);
}
