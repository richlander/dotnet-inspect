using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;
using ILInspector.Findings;

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
        var index = context.Assembly?.Index ?? AnalysisIndexCache.ForPath(path);
        if (!index.GetAllocationOccurrences().TryGetValue(function.MetadataToken, out var occurrences))
            return [];

        FindingSubject subject = occurrences.IsEmpty
            ? new($"{path}|{function.MetadataToken:X8}", function.Name)
            : ToFindingSubject(occurrences[0].Method);
        return
        [
            .. AnalysisFindings.InspectAllocations(occurrences, subject)
                .Select(finding => ToAnnotation(finding.Payload)),
        ];
    }

    static FindingSubject ToFindingSubject(MethodIdentity method)
    {
        var subject = ResearchMemberIdentity.SubjectFromMethod(method);
        return new FindingSubject(subject.Id, subject.Display);
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
        if (FormatPostDominance(occurrence.PostDominance) is { } postDominance)
            parts.Add($"post-dominance={postDominance}");
        if (occurrence.Escape != AllocationEscape.Unknown)
            parts.Add($"escape={FormatEscape(occurrence.Escape)}");
        if (FormatEscapeKind(occurrence.EscapeKind) is { } escapeKind)
            parts.Add($"escape-kind={escapeKind}");
        if (FormatMultiplicity(occurrence.Multiplicity) is { } multiplicity)
            parts.Add($"multiplicity={multiplicity}");
        if (occurrence.ChurnedType is { Length: > 0 } churned)
            parts.Add($"churned={churned}");
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

    static string? FormatPostDominance(AllocationPostDominance postDominance)
        => postDominance switch
        {
            AllocationPostDominance.ReturnPostDominates => "return-post-dominates",
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

    static string? FormatEscapeKind(AllocationEscapeKind kind)
        => kind switch
        {
            AllocationEscapeKind.Return => "escapes-return",
            AllocationEscapeKind.Field => "escapes-field",
            AllocationEscapeKind.Static => "escapes-static",
            AllocationEscapeKind.Collection => "escapes-collection",
            AllocationEscapeKind.Capture => "escapes-capture",
            _ => null,
        };

    static string? FormatMultiplicity(AllocationMultiplicity multiplicity)
        => multiplicity switch
        {
            AllocationMultiplicity.Once => "once",
            AllocationMultiplicity.Conditional => "conditional",
            AllocationMultiplicity.Loop => "loop",
            _ => null,
        };

}
