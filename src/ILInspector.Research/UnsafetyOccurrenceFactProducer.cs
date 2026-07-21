using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;
using ILInspector.Findings;

namespace ILInspector.Research;

sealed class UnsafetyOccurrenceFactProducer : IResearchFactProducer
{
    static readonly AnnotationDescriptor Deref = new("unsafe.deref", AnnotationCategory.Unsafety, "dereferences a pointer");
    static readonly AnnotationDescriptor StackAlloc = new("unsafe.stackalloc", AnnotationCategory.Unsafety, "allocates on the stack");
    static readonly AnnotationDescriptor Calli = new("unsafe.calli", AnnotationCategory.Unsafety, "calls through a function pointer");

    public string Name => "unsafety-occurrences";
    public IReadOnlyList<string> Produces { get; } = ["unsafe.*"];
    public IReadOnlyList<string> DependsOn => [];

    public IReadOnlyList<IAnnotation> Produce(ResearchFactContext context)
    {
        var function = context.Imported;
        if (context.Assembly is not { } assembly || function.MetadataToken == 0)
            return [];
        if (!assembly.Index.GetUnsafetyOccurrences().TryGetValue(function.MetadataToken, out var occurrences)
            || occurrences.IsEmpty)
            return [];
        var subject = ResearchMemberIdentity.SubjectFromMethod(occurrences[0].Method);
        return
        [
            .. AnalysisFindings.InspectUnsafety(
                    occurrences,
                    new FindingSubject(subject.Id, subject.Display))
                .Select(finding => ToAnnotation(finding.Payload)),
        ];
    }

    static Annotation<UnsafetyOccurrence> ToAnnotation(UnsafetyOccurrence occurrence)
    {
        var descriptor = occurrence.Kind switch
        {
            UnsafetyKind.StackAlloc => StackAlloc,
            UnsafetyKind.CallIndirect => Calli,
            _ => Deref,
        };
        return new Annotation<UnsafetyOccurrence>(descriptor, occurrence.ILOffset, occurrence, Formatter: static o => o.Detail);
    }
}
