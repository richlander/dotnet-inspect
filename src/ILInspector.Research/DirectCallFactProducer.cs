using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research;

sealed class DirectCallFactProducer : IResearchFactProducer
{
    static readonly AnnotationDescriptor CallEdge =
        new(
            ResearchFactRegistry.CallRelationshipDescriptorId,
            AnnotationCategory.Relationship,
            "calls another member");

    public string Name => "direct-call-relationships";
    public IReadOnlyList<string> Produces { get; } =
        [ResearchFactRegistry.CallRelationshipDescriptorId];
    public IReadOnlyList<string> DependsOn => [];

    public IReadOnlyList<IAnnotation> Produce(ResearchFactContext context)
    {
        IReadOnlyList<DirectCall> calls = context.CallSites
            ?? throw new InvalidOperationException(
                "Direct call relationship facts require supplied call-site evidence.");
        return calls.Count == 0
            ? []
            :
            [
                .. calls
                    .OrderBy(call => call.ILOffset)
                    .ThenBy(call => call.OperandToken)
                    .Select(call => new Annotation<DirectCall>(
                        CallEdge,
                        call.ILOffset,
                        call,
                        Formatter: static occurrence =>
                            occurrence.Callee.ToQualifiedDisplayString())),
            ];
    }
}
