using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;
using ILInspector.Findings;

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

    public IReadOnlyList<Finding<IAnnotation>> Produce(ResearchFactContext context)
    {
        IReadOnlyList<DirectCall> calls = context.CallSites
            ?? throw new InvalidOperationException(
                "Direct call relationship facts require supplied call-site evidence.");
        if (calls.Count == 0)
            return [];

        var subject = ResearchMemberIdentity.SubjectFromMethod(calls[0].Caller);
        return
        [
            .. AnalysisFindings.InspectCallSites(
                    calls,
                    new FindingSubject(subject.Id, subject.Display))
                .Select(finding => ResearchFactFinding.Project(
                    finding,
                    new Annotation<DirectCall>(
                        CallEdge,
                        finding.Payload.ILOffset,
                        finding.Payload,
                        Formatter: static occurrence =>
                            occurrence.Callee.ToQualifiedDisplayString()))),
        ];
    }
}
