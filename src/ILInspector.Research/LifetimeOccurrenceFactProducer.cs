using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research;

sealed class LifetimeOccurrenceFactProducer : IResearchFactProducer
{
    static readonly AnnotationDescriptor RefReturn = new("lifetime.ref-return", AnnotationCategory.Lifetime, "returns a reference borrowing from an input or field");
    static readonly AnnotationDescriptor StackBound = new("lifetime.stack-bound", AnnotationCategory.Lifetime, "span backed by stack memory — cannot escape the frame");
    static readonly AnnotationDescriptor RefStructReturn = new("lifetime.ref-struct-return", AnnotationCategory.Lifetime, "returns a ref struct — lifetime-constrained, cannot be boxed or stored on the heap");
    static readonly AnnotationDescriptor PointerReturn = new("lifetime.pointer-return", AnnotationCategory.Lifetime, "returns an unmanaged pointer — the caller inherits an unverifiable lifetime obligation");
    static readonly AnnotationDescriptor StackEscape = new("lifetime.stack-escape", AnnotationCategory.Lifetime, "returns a pointer into stack memory reclaimed on return — a dangling pointer");

    public string Name => "lifetime-occurrences";
    public IReadOnlyList<string> Produces { get; } = ["lifetime.*"];
    public IReadOnlyList<string> DependsOn => [];

    public IReadOnlyList<Annotation> Produce(ResearchFactContext context)
    {
        var function = context.Imported;
        if (function.AssemblyPath is not { Length: > 0 } path || function.MetadataToken == 0)
            return [];
        var index = AnalysisIndexCache.ForPath(path);
        if (!index.GetLifetimeOccurrences().TryGetValue(function.MetadataToken, out var occurrences))
            return [];
        return [.. occurrences.Select(ToAnnotation)];
    }

    static Annotation ToAnnotation(LifetimeOccurrence occurrence)
    {
        var descriptor = occurrence.Kind switch
        {
            LifetimeKind.RefReturn => RefReturn,
            LifetimeKind.StackBound => StackBound,
            LifetimeKind.RefStructReturn => RefStructReturn,
            LifetimeKind.PointerReturn => PointerReturn,
            LifetimeKind.StackEscape => StackEscape,
            _ => RefReturn,
        };
        return new Annotation(descriptor, occurrence.ILOffset, occurrence.Detail);
    }
}
