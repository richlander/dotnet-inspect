using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research;

sealed class AllocationOccurrenceFactProducer : IResearchFactProducer
{
    static readonly AnnotationDescriptor Box = new("alloc.box", AnnotationCategory.Allocation, "boxes a value type");
    static readonly AnnotationDescriptor Array = new("alloc.array", AnnotationCategory.Allocation, "allocates an array");
    static readonly AnnotationDescriptor NewObj = new("alloc.new", AnnotationCategory.Allocation, "allocates an object");
    static readonly AnnotationDescriptor Closure = new("alloc.closure", AnnotationCategory.Allocation, "allocates a closure");
    static readonly AnnotationDescriptor StateMachine = new("alloc.statemachine", AnnotationCategory.Allocation, "allocates a state machine");
    static readonly AnnotationDescriptor Delegate = new("alloc.delegate", AnnotationCategory.Allocation, "allocates a delegate");
    static readonly AnnotationDescriptor Enumerator = new("alloc.enumerator", AnnotationCategory.Allocation, "allocates an enumerator");

    public string Name => "allocation-occurrences";
    public IReadOnlyList<string> Produces { get; } = ["alloc.*"];
    public IReadOnlyList<string> DependsOn => [];

    public IReadOnlyList<Annotation> Produce(ResearchFactContext context)
    {
        var function = context.Imported;
        if (function.AssemblyPath is not { Length: > 0 } path || function.MetadataToken == 0)
            return [];
        var index = AnalysisIndexCache.ForPath(path);
        if (!index.GetAllocationOccurrences().TryGetValue(function.MetadataToken, out var occurrences))
            return [];
        return [.. occurrences.Select(ToAnnotation)];
    }

    static Annotation ToAnnotation(AllocationOccurrence occurrence)
    {
        var descriptor = occurrence.Kind switch
        {
            AllocationKind.Box => Box,
            AllocationKind.Array => Array,
            AllocationKind.Closure => Closure,
            AllocationKind.StateMachine => StateMachine,
            AllocationKind.Delegate => Delegate,
            AllocationKind.Enumerator => Enumerator,
            _ => NewObj,
        };
        var conditionality = occurrence.Frequency switch
        {
            AllocationFrequency.CachedOnce => AnnotationConditionality.CachedOnce,
            AllocationFrequency.PerIteration => AnnotationConditionality.PerIteration,
            _ => AnnotationConditionality.Always,
        };
        return new Annotation(descriptor, occurrence.ILOffset, Detail(occurrence), conditionality, Node: null);
    }

    static string? Detail(AllocationOccurrence occurrence)
    {
        var parts = new List<string>();
        if (occurrence.Detail is { Length: > 0 } detail)
            parts.Add(detail);
        if (occurrence.RuntimeAllocationType is { Length: > 0 } runtime)
            parts.Add($"alloc={runtime}");
        parts.Add($"path={FormatPathContext(occurrence.PathContext)}");
        if (FormatPathConfidence(occurrence.PathConfidence) is { } confidence)
            parts.Add($"path-confidence={confidence}");
        if (occurrence.Escape != AllocationEscape.Unknown)
            parts.Add($"escape={FormatEscape(occurrence.Escape)}");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    static string FormatPathContext(AllocationPathContext context)
        => context switch
        {
            AllocationPathContext.Branch => "branch",
            AllocationPathContext.SwitchArm => "switch-arm",
            AllocationPathContext.LoopBody => "loop-body",
            AllocationPathContext.ErrorPath => "error-path",
            _ => "straight-line",
        };

    static string? FormatPathConfidence(AllocationPathConfidence confidence)
        => confidence switch
        {
            AllocationPathConfidence.DominatesReturn => "dominates-return",
            AllocationPathConfidence.BehindBranch => "behind-branch",
            _ => null,
        };

    static string FormatEscape(AllocationEscape escape)
        => escape switch
        {
            AllocationEscape.LocalOnly => "local-only",
            AllocationEscape.Escapes => "escapes",
            AllocationEscape.ThrowPath => "throw-path",
            _ => "unknown",
        };

}
