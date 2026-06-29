using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Annotations;

/// <summary>
/// Surfaces heap allocations the C# source hides — boxing, object and array
/// construction, closures, state machines, delegates, and reference-type
/// enumerators. A pure, read-only walk over the freshly imported IR: it runs at
/// the <see cref="AnnotationStage.Imported"/> stage on purpose, before
/// DelegateConstructionPass and the raise fold <c>newobj</c>s into higher-level
/// shapes, so every allocation is still a literal node.
///
/// Positive-only: it reports the allocations it can see and stays silent
/// otherwise. It never claims a method is allocation-free.
/// </summary>
public sealed class AllocationClassifier : IHiddenFactClassifier
{
    const int MaxCachedIndexes = 8;
    static readonly object s_indexLock = new();
    static readonly Dictionary<string, ILInspector.Analysis.LibraryBodyIndex> s_indexes = new(PathComparer());

    public static readonly AnnotationDescriptor Box = new("alloc.box", AnnotationCategory.Allocation, "boxes a value type");
    public static readonly AnnotationDescriptor Array = new("alloc.array", AnnotationCategory.Allocation, "allocates an array");
    public static readonly AnnotationDescriptor NewObj = new("alloc.new", AnnotationCategory.Allocation, "allocates an object");
    public static readonly AnnotationDescriptor Closure = new("alloc.closure", AnnotationCategory.Allocation, "allocates a closure");
    public static readonly AnnotationDescriptor StateMachine = new("alloc.statemachine", AnnotationCategory.Allocation, "allocates a state machine");
    public static readonly AnnotationDescriptor Delegate = new("alloc.delegate", AnnotationCategory.Allocation, "allocates a delegate");
    public static readonly AnnotationDescriptor Enumerator = new("alloc.enumerator", AnnotationCategory.Allocation, "allocates an enumerator");

    public AnnotationCategory Category => AnnotationCategory.Allocation;

    public AnnotationStage Stage => AnnotationStage.Imported;

    public IReadOnlyList<Annotation> Classify(IrFunction function)
    {
        if (function.AssemblyPath is not { Length: > 0 } path || function.MetadataToken == 0)
            return [];

        var index = IndexFor(path);
        if (!index.GetAllocationOccurrences().TryGetValue(function.MetadataToken, out var occurrences))
            return [];

        return [.. occurrences.OrderBy(occurrence => occurrence.ILOffset).Select(ToAnnotation)];
    }

    static ILInspector.Analysis.LibraryBodyIndex IndexFor(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (s_indexLock)
        {
            if (s_indexes.TryGetValue(fullPath, out var index))
                return index;
            if (s_indexes.Count >= MaxCachedIndexes)
                s_indexes.Clear();
            index = ILInspector.Analysis.LibraryBodyIndex.Open(fullPath);
            s_indexes[fullPath] = index;
            return index;
        }
    }

    static StringComparer PathComparer()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    static Annotation ToAnnotation(ILInspector.Analysis.AllocationOccurrence occurrence)
    {
        var descriptor = occurrence.Kind switch
        {
            ILInspector.Analysis.AllocationKind.Box => Box,
            ILInspector.Analysis.AllocationKind.Array => Array,
            ILInspector.Analysis.AllocationKind.Closure => Closure,
            ILInspector.Analysis.AllocationKind.StateMachine => StateMachine,
            ILInspector.Analysis.AllocationKind.Delegate => Delegate,
            ILInspector.Analysis.AllocationKind.Enumerator => Enumerator,
            _ => NewObj,
        };
        var conditionality = occurrence.Frequency switch
        {
            ILInspector.Analysis.AllocationFrequency.CachedOnce => AnnotationConditionality.CachedOnce,
            ILInspector.Analysis.AllocationFrequency.PerIteration => AnnotationConditionality.PerIteration,
            _ => AnnotationConditionality.Always,
        };
        return new Annotation(descriptor, occurrence.ILOffset, occurrence.Detail, conditionality, Node: null);
    }
}
