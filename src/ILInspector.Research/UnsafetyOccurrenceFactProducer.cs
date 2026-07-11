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

    public IReadOnlyList<Annotation> Produce(ResearchFactContext context)
    {
        var function = context.Imported;
        if (function.AssemblyPath is not { Length: > 0 } path || function.MetadataToken == 0)
            return [];
        var index = context.Assembly?.Index ?? AnalysisIndexCache.ForPath(path);
        if (!index.GetUnsafetyOccurrences().TryGetValue(function.MetadataToken, out var occurrences)
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

    static Annotation ToAnnotation(UnsafetyOccurrence occurrence)
    {
        var descriptor = occurrence.Kind switch
        {
            UnsafetyKind.StackAlloc => StackAlloc,
            UnsafetyKind.CallIndirect => Calli,
            _ => Deref,
        };
        return new Annotation(descriptor, occurrence.ILOffset, occurrence.Detail);
    }
}
