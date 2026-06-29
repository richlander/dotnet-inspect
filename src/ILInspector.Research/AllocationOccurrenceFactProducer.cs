using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research;

sealed class AllocationOccurrenceFactProducer : IResearchFactProducer
{
    const int MaxCachedIndexes = 8;
    static readonly object s_indexLock = new();
    static readonly Dictionary<string, LibraryBodyIndex> s_indexes = new(PathComparer());

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
        var index = IndexFor(path);
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
        return new Annotation(descriptor, occurrence.ILOffset, occurrence.Detail, conditionality, Node: null);
    }

    static LibraryBodyIndex IndexFor(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (s_indexLock)
        {
            if (s_indexes.TryGetValue(fullPath, out var index))
                return index;
            if (s_indexes.Count >= MaxCachedIndexes)
                s_indexes.Clear();
            index = LibraryBodyIndex.Open(fullPath);
            s_indexes[fullPath] = index;
            return index;
        }
    }

    static StringComparer PathComparer()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
