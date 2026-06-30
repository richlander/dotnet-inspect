using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;

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
        var index = AnalysisIndexCache.ForPath(path);
        if (!index.GetUnsafetyOccurrences().TryGetValue(function.MetadataToken, out var occurrences))
            return [];
        return [.. occurrences.Select(ToAnnotation)];
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
