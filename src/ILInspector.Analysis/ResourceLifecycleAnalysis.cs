using System.Collections.Immutable;
using System.Reflection.Metadata;

using Inspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

public readonly record struct ResourceBoundaryEvidence(
    int ILOffset,
    MemberRef Operation);

/// <summary>The supported lifecycle conclusion for one occurrence-issued root.</summary>
public enum ResourceLifecycleObservationKind
{
    ExceptionalExitBeforeRelease,
}

/// <summary>
/// One root-bound lifecycle conclusion derived from exact body-flow evidence.
/// </summary>
public sealed record ResourceLifecycleObservation(
    ResourceOccurrenceRoot Root,
    MethodIdentity Method,
    ResourceLifecycleObservationKind Kind,
    int AcquireOffset,
    ImmutableArray<ResourceBoundaryEvidence> Boundaries);

public enum ResourceLifecycleLimitationKind
{
    OccurrenceAnalysis,
    ControlFlow,
    ExceptionFlow,
    CatchTypeResolution,
}

/// <summary>A typed gap in the supported lifecycle census.</summary>
public sealed record ResourceLifecycleLimitation(
    ResourceLifecycleLimitationKind Kind,
    string Message)
{
    public MethodIdentity? Method { get; init; }
    public ResourceOccurrenceRoot? Root { get; init; }
    public ResourceOccurrenceLimitation? OccurrenceLimitation { get; init; }
}

/// <summary>Exceptional-cleanup lifecycle evidence for one physical method.</summary>
public sealed record ResourceLifecycleMethodAnalysisResult(
    MethodIdentity Method,
    ImmutableArray<ResourceLifecycleObservation> Observations,
    ImmutableArray<ResourceLifecycleLimitation> Limitations)
{
    public bool IsComplete => Limitations.IsEmpty;
}

/// <summary>
/// Detached lifecycle evidence produced by one library-body Analysis execution.
/// </summary>
public sealed record ResourceLifecycleAnalysisResult(
    LibraryBodyAnalysisReceipt Receipt,
    bool WasRequested,
    ResourceEffectAdmissionReceipt? AdmissionReceipt,
    ImmutableArray<ResourceLifecycleMethodAnalysisResult> Methods,
    ImmutableArray<ResourceLifecycleLimitation> Limitations)
{
    public bool IsComplete =>
        WasRequested
        && Limitations.IsEmpty
        && Methods.All(method => method.IsComplete);
}

public sealed record ResourceLifecycleOccurrence
{
    ImmutableArray<ResourceBoundaryEvidence> _boundaries;

    public ResourceLifecycleOccurrence(
        MethodIdentity Method,
        string Resource,
        string Shape,
        int AcquireOffset,
        ImmutableArray<ResourceBoundaryEvidence> Boundaries)
    {
        this.Method = Method ?? throw new ArgumentNullException(nameof(Method));
        ArgumentException.ThrowIfNullOrWhiteSpace(Resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(Shape);
        if (AcquireOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(AcquireOffset));

        this.Resource = Resource;
        this.Shape = Shape;
        this.AcquireOffset = AcquireOffset;
        _boundaries = ImmutableArrayValueEquality.RequireInitialized(
            Boundaries,
            nameof(Boundaries));
    }

    public MethodIdentity Method { get; }
    public string Resource { get; }
    public string Shape { get; }
    public int AcquireOffset { get; }
    public ImmutableArray<ResourceBoundaryEvidence> Boundaries
    {
        get => _boundaries;
        init => _boundaries = ImmutableArrayValueEquality.RequireInitialized(
            value,
            nameof(Boundaries));
    }
    public bool Equals(ResourceLifecycleOccurrence? other)
        => other is not null
            && Method == other.Method
            && string.Equals(Resource, other.Resource, StringComparison.Ordinal)
            && string.Equals(Shape, other.Shape, StringComparison.Ordinal)
            && AcquireOffset == other.AcquireOffset
            && ImmutableArrayValueEquality.SequenceEqual(Boundaries, other.Boundaries);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Method);
        hash.Add(Resource, StringComparer.Ordinal);
        hash.Add(Shape, StringComparer.Ordinal);
        hash.Add(AcquireOffset);
        ImmutableArrayValueEquality.AddToHash(ref hash, Boundaries);
        return hash.ToHashCode();
    }
}

public static class ResourceLifecycleAnalysis
{
    public static FindingInspection<ResourceLifecycleOccurrence> Inspect(
        ResourceLifecycleAnalysisResult result,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(subject);
        if (!result.WasRequested)
        {
            throw new InvalidOperationException(
                "Resource Lifecycle Analysis was not requested.");
        }

        ResourceLifecycleLimitation? first =
            result.Limitations.FirstOrDefault()
            ?? result.Methods
                .SelectMany(method => method.Limitations)
                .FirstOrDefault();
        if (first is not null)
        {
            string location = first.Method is null
                ? ""
                : $" at method token 0x{first.Method.MetadataToken:X8}";
            return new FindingInspection<ResourceLifecycleOccurrence>.Failed(
                new InspectionError(
                    subject,
                    AnalysisFindings.ResourceLifecycleDescriptor,
                    $"Resource lifecycle analysis was incomplete{location} "
                    + $"during {first.Kind} ({first.Message})."));
        }

        IEnumerable<ResourceLifecycleOccurrence> occurrences =
            result.Methods
                .SelectMany(method => method.Observations)
                .Select(CreateOccurrence);
        return new FindingInspection<ResourceLifecycleOccurrence>.Complete(
            AnalysisFindings.InspectResourceLifecycles(
                occurrences,
                subject));
    }

    public static FindingInspection<ResourceLifecycleOccurrence> InspectAssembly(
        string path,
        FindingSubject subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return InspectAssembly(
            () => LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.LeakTriage),
            subject);
    }

    public static FindingInspection<ResourceLifecycleOccurrence> InspectAssembly(
        Func<LibraryBodyIndex> openIndex,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(openIndex);
        ArgumentNullException.ThrowIfNull(subject);

        try
        {
            LeakTriageResult result =
                openIndex().LeakTriage;
            if (!result.Failures.IsEmpty)
            {
                LeakTriageFailure first = result.Failures[0];
                string count = result.Failures.Length == 1
                    ? "one method"
                    : $"{result.Failures.Length} methods";
                return new FindingInspection<ResourceLifecycleOccurrence>.Failed(
                    new InspectionError(
                        subject,
                        AnalysisFindings.ResourceLifecycleDescriptor,
                        $"Resource lifecycle analysis was incomplete for {count}; "
                        + $"first failure at method token 0x{first.MethodToken:X8} "
                        + $"during {FailurePhase(first.Kind)} "
                        + $"({first.Reason})."));
            }

            var occurrences = result
                .ExceptionPathCandidates
                .Select(CreateOccurrence);
            return new FindingInspection<ResourceLifecycleOccurrence>.Complete(
                AnalysisFindings.InspectResourceLifecycles(occurrences, subject));
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException
                or IndexOutOfRangeException)
        {
            return new FindingInspection<ResourceLifecycleOccurrence>.Failed(
                new InspectionError(
                    subject,
                    AnalysisFindings.ResourceLifecycleDescriptor,
                    $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    static ResourceLifecycleOccurrence CreateOccurrence(
        ArrayPoolExceptionPathCandidate candidate)
        => new(
            candidate.Method,
            "ArrayPool<T>",
            "pool-churn-on-exception",
            candidate.RentOffset,
            candidate.Boundaries
                .Select(boundary => new ResourceBoundaryEvidence(
                    boundary.ILOffset,
                    boundary.Operation))
                .ToImmutableArray());

    static ResourceLifecycleOccurrence CreateOccurrence(
        ResourceLifecycleObservation observation)
        => new(
            observation.Method,
            ResourceName(observation.Root),
            "pool-churn-on-exception",
            observation.AcquireOffset,
            observation.Boundaries);

    static string ResourceName(ResourceOccurrenceRoot root) =>
        root.ResourceKinds.Any(kind =>
            kind.Identity == ArrayPoolResourceEffectModel.BufferKind)
            ? "ArrayPool<T>"
            : string.Join(
                ", ",
                root.ResourceKinds.Select(kind => kind.Identity.Value));

    static string FailurePhase(LeakTriageFailureKind kind) =>
        kind switch
        {
            LeakTriageFailureKind.InstructionDecoding =>
                "instruction decoding",
            LeakTriageFailureKind.MethodResolution =>
                "method resolution",
            LeakTriageFailureKind.MethodMetadata =>
                "method metadata validation",
            LeakTriageFailureKind.BodyAcquisition =>
                "method body acquisition",
            LeakTriageFailureKind.ControlFlowAnalysis =>
                "control-flow analysis",
            _ => "analysis",
        };
}

internal static class ResourceLifecycleAnalysisService
{
    internal static ResourceLifecycleMethodAnalysisResult Analyze(
        MethodBodyAnalysisContext context,
        ResourceOccurrenceAnalysisResult occurrences,
        ImmutableArray<DirectCall> directCalls,
        IReadOnlySet<MethodExceptionClauseId> catchAllCleanup)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(catchAllCleanup);
        if (context.Method != occurrences.Method)
        {
            throw new ArgumentException(
                "Lifecycle body and occurrence evidence identify different methods.",
                nameof(occurrences));
        }

        var limitations =
            ImmutableArray.CreateBuilder<ResourceLifecycleLimitation>();
        foreach (ResourceOccurrenceLimitation limitation in
            occurrences.Limitations.Where(IsLifecycleBlocking))
        {
            limitations.Add(
                new(
                    ResourceLifecycleLimitationKind.OccurrenceAnalysis,
                    limitation.Message)
                {
                    Method = occurrences.Method,
                    Root = limitation.Root,
                    OccurrenceLimitation = limitation,
                });
        }

        if (!context.Blocks.IsComplete)
        {
            limitations.Add(
                new(
                    ResourceLifecycleLimitationKind.ControlFlow,
                    context.Blocks.IncompleteReason
                        ?? "Control flow is incomplete.")
                {
                    Method = occurrences.Method,
                });
            return new(occurrences.Method, [], limitations.ToImmutable());
        }

        if (context.Instructions.ExceptionFlow
            is not InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Available availableFlow)
        {
            string reason = context.Instructions.ExceptionFlow switch
            {
                InstructionExceptionFlowResult<
                    InstructionExceptionFlowFacts>.Unavailable unavailable =>
                    unavailable.Reason.ToString(),
                InstructionExceptionFlowResult<
                    InstructionExceptionFlowFacts>.Ambiguous =>
                    "Exception flow is ambiguous.",
                _ => "Exception flow is unavailable.",
            };
            limitations.Add(
                new(
                    ResourceLifecycleLimitationKind.ExceptionFlow,
                    reason)
                {
                    Method = occurrences.Method,
                });
            return new(occurrences.Method, [], limitations.ToImmutable());
        }

        IReadOnlyDictionary<int, MemberRef> calls = directCalls
            .Where(call =>
                call.EvidenceMethod == occurrences.Method
                && call.Kind is CallKind.Call
                    or CallKind.CallVirtual
                    or CallKind.NewObject)
            .ToDictionary(call => call.ILOffset, call => call.Callee);
        var observations =
            ImmutableArray.CreateBuilder<ResourceLifecycleObservation>();
        foreach (ResourceOccurrenceRoot.Acquisition root in
            occurrences.Roots.OfType<ResourceOccurrenceRoot.Acquisition>())
        {
            ImmutableArray<ResourceOccurrence> rootOccurrences =
            [
                .. occurrences.Occurrences.Where(occurrence =>
                    ReferenceEquals(occurrence.Root, root)
                    || occurrence.Root == root),
            ];
            ImmutableArray<int> releases =
            [
                .. rootOccurrences
                    .Where(occurrence =>
                        occurrence.Operations.Contains(
                            ResourceOccurrenceOperationKind.Release))
                    .Select(occurrence => occurrence.ILOffset)
                    .Distinct()
                    .Order(),
            ];
            ImmutableArray<ArrayPoolExceptionBoundary> boundaries =
            [
                .. rootOccurrences
                    .Where(IsThrowingBoundary)
                    .Select(occurrence =>
                        new ArrayPoolExceptionBoundary(
                            occurrence.ILOffset,
                            occurrence.Call!.Callee)),
            ];
            ImmutableArray<ArrayPoolExceptionBoundary> unprotected =
                UnprotectedThrowingBoundaries(
                    context,
                    availableFlow.Value,
                    catchAllCleanup,
                    root.Call.ILOffset,
                    releases,
                    boundaries);
            if (!unprotected.IsEmpty)
            {
                observations.Add(
                    new(
                        root,
                        occurrences.Method,
                        ResourceLifecycleObservationKind
                            .ExceptionalExitBeforeRelease,
                        root.Call.ILOffset,
                        [
                            .. unprotected.Select(boundary =>
                                new ResourceBoundaryEvidence(
                                    boundary.ILOffset,
                                    boundary.Operation)),
                        ]));
                continue;
            }

            if (ExitsExceptionallyWithoutRelease(
                    context,
                    calls,
                    root.Call.ILOffset,
                    releases))
            {
                observations.Add(
                    new(
                        root,
                        occurrences.Method,
                        ResourceLifecycleObservationKind
                            .ExceptionalExitBeforeRelease,
                        root.Call.ILOffset,
                        []));
            }
        }

        return new(
            occurrences.Method,
            [
                .. observations
                    .OrderBy(observation =>
                        observation.AcquireOffset),
            ],
            limitations.ToImmutable());
    }

    static ImmutableArray<ArrayPoolExceptionBoundary>
        UnprotectedThrowingBoundaries(
            MethodBodyAnalysisContext context,
            InstructionExceptionFlowFacts exceptionFlow,
            IReadOnlySet<MethodExceptionClauseId> catchAllCleanup,
            int acquireOffset,
            ImmutableArray<int> releases,
            ImmutableArray<ArrayPoolExceptionBoundary> boundaries)
    {
        var result =
            ImmutableArray.CreateBuilder<ArrayPoolExceptionBoundary>();
        var seenOffsets = new HashSet<int>();
        foreach (ArrayPoolExceptionBoundary boundary in
            boundaries.OrderBy(boundary => boundary.ILOffset))
        {
            if (!seenOffsets.Add(boundary.ILOffset)
                || !CanReachUnreleased(
                    context,
                    acquireOffset,
                    boundary.ILOffset,
                    releases)
                || HasGuaranteedCleanupRelease(
                    context,
                    exceptionFlow,
                    catchAllCleanup,
                    releases,
                    boundary.ILOffset))
            {
                continue;
            }
            result.Add(boundary);
        }
        return result.ToImmutable();
    }

    static bool CanReachUnreleased(
        MethodBodyAnalysisContext context,
        int startOffset,
        int targetOffset,
        ImmutableArray<int> releases)
    {
        BlockGraph graph = context.Blocks;
        int startBlock = graph.BlockIndexAt(startOffset);
        if (startBlock < 0 || graph.BlockIndexAt(targetOffset) < 0)
            return false;

        var releaseSet = releases.ToHashSet();
        var visited =
            new HashSet<(int Block, bool Released, int EntryOffset)>();
        var pending =
            new Stack<(int Block, bool Released, int StartOffset)>();
        pending.Push((startBlock, false, startOffset));
        while (pending.TryPop(out var state))
        {
            if (!visited.Add(
                (state.Block, state.Released, state.StartOffset)))
                continue;

            InstructionBlock block = graph.Blocks[state.Block];
            bool released = state.Released;
            foreach (DecodedInstruction instruction in
                InstructionsIn(context, block, state.StartOffset))
            {
                if (instruction.Offset == targetOffset)
                    return !released;
                if (instruction.Offset == startOffset)
                    released = false;
                if (releaseSet.Contains(instruction.Offset))
                    released = true;
            }

            foreach (int successor in block.Edges.Successors)
            {
                pending.Push(
                    (successor, released, graph.Blocks[successor].Start));
            }
        }
        return false;
    }

    static bool HasGuaranteedCleanupRelease(
        MethodBodyAnalysisContext context,
        InstructionExceptionFlowFacts exceptionFlow,
        IReadOnlySet<MethodExceptionClauseId> catchAllCleanup,
        ImmutableArray<int> releases,
        int boundaryOffset)
    {
        ImmutableArray<InstructionExceptionRegion> regions =
            RequireLocation(exceptionFlow, boundaryOffset);
        bool interceptingCatchSeen = false;
        foreach (InstructionExceptionRegion protectedRegion in regions
            .Where(region =>
                region.Id.Role
                    == InstructionExceptionRegionRole.Protected)
            .Reverse())
        {
            foreach (InstructionExceptionClause clause in
                exceptionFlow.Clauses
                    .Where(clause =>
                        clause.ProtectedRegion == protectedRegion.Id)
                    .OrderBy(clause => clause.Id.Ordinal))
            {
                if (clause.Kind is ExceptionRegionKind.Finally
                    or ExceptionRegionKind.Fault)
                {
                    if (HandlerAlwaysReleases(
                        context,
                        exceptionFlow,
                        clause.HandlerRegion,
                        releases))
                    {
                        return true;
                    }
                    continue;
                }

                if (clause.Kind is not (
                    ExceptionRegionKind.Catch
                    or ExceptionRegionKind.Filter))
                {
                    continue;
                }

                if (!interceptingCatchSeen
                    && catchAllCleanup.Contains(clause.Id)
                    && HandlerAlwaysReleases(
                        context,
                        exceptionFlow,
                        clause.HandlerRegion,
                        releases))
                {
                    return true;
                }
                interceptingCatchSeen = true;
            }
        }
        return false;
    }

    static bool HandlerAlwaysReleases(
        MethodBodyAnalysisContext context,
        InstructionExceptionFlowFacts exceptionFlow,
        InstructionExceptionRegionId handlerId,
        ImmutableArray<int> releases)
    {
        InstructionExceptionRegion handler =
            exceptionFlow.GetRegion(handlerId) switch
            {
                InstructionExceptionFlowResult<
                    InstructionExceptionRegion>.Available available =>
                    available.Value,
                InstructionExceptionFlowResult<
                    InstructionExceptionRegion>.Unavailable unavailable =>
                    throw new InvalidOperationException(
                        $"Exception handler is unavailable "
                        + $"({unavailable.Reason}): {unavailable.Detail}"),
                InstructionExceptionFlowResult<
                    InstructionExceptionRegion>.Ambiguous ambiguous =>
                    throw new InvalidOperationException(
                        "Exception handler is ambiguous: "
                        + ambiguous.Detail),
                _ => throw new InvalidOperationException(
                    "Unknown exception-handler result."),
            };
        BlockGraph graph = context.Blocks;
        int startBlock = graph.BlockIndexAt(handler.Extent.Start);
        if (startBlock < 0)
            return false;

        var releaseSet = releases.ToHashSet();
        var visited = new HashSet<(int Block, bool Released)>();
        var pending = new Stack<(int Block, bool Released)>();
        pending.Push((startBlock, false));
        bool sawExit = false;
        while (pending.TryPop(out var state))
        {
            if (!visited.Add(state))
                continue;

            InstructionBlock block = graph.Blocks[state.Block];
            bool released = state.Released;
            foreach (DecodedInstruction instruction in
                InstructionsIn(
                    context,
                    block,
                    handler.Extent.Start))
            {
                if (!handler.Extent.Contains(instruction.Offset))
                    continue;
                if (releaseSet.Contains(instruction.Offset))
                {
                    released = true;
                }
            }

            bool exitsHandler = block.Edges.ExitsMethod;
            foreach (int successor in block.Edges.Successors)
            {
                if (handler.Extent.Contains(graph.Blocks[successor].Start))
                    pending.Push((successor, released));
                else
                    exitsHandler = true;
            }
            if (!exitsHandler
                && block.Edges.Successors.Count == 0)
            {
                exitsHandler = true;
            }
            if (exitsHandler)
            {
                sawExit = true;
                if (!released)
                    return false;
            }
        }
        return sawExit;
    }

    static bool ExitsExceptionallyWithoutRelease(
        MethodBodyAnalysisContext context,
        IReadOnlyDictionary<int, MemberRef> calls,
        int startOffset,
        ImmutableArray<int> releases)
    {
        BlockGraph graph = context.Blocks;
        int startBlock = graph.BlockIndexAt(startOffset);
        if (startBlock < 0)
            return false;

        var releaseSet = releases.ToHashSet();
        var visited =
            new HashSet<(int Block, bool Released, int EntryOffset)>();
        var pending =
            new Stack<(int Block, bool Released, int StartOffset)>();
        pending.Push((startBlock, false, startOffset));
        while (pending.TryPop(out var state))
        {
            if (!visited.Add(
                (state.Block, state.Released, state.StartOffset)))
                continue;

            InstructionBlock block = graph.Blocks[state.Block];
            bool released = state.Released;
            bool exitsByException = false;
            foreach (DecodedInstruction instruction in
                InstructionsIn(context, block, state.StartOffset))
            {
                if (instruction.Offset == startOffset)
                    released = false;
                if (releaseSet.Contains(instruction.Offset))
                    released = true;
                if (IsDefinitelyThrowing(instruction, calls))
                    exitsByException = true;
            }
            if (!released
                && exitsByException
                && block.Edges.ExitsMethod
                && block.Edges.Successors.Count == 0)
            {
                return true;
            }
            foreach (int successor in block.Edges.Successors)
            {
                pending.Push(
                    (successor, released, graph.Blocks[successor].Start));
            }
        }
        return false;
    }

    static IEnumerable<DecodedInstruction> InstructionsIn(
        MethodBodyAnalysisContext context,
        InstructionBlock block,
        int startOffset) =>
        context.Instructions.Instructions.Where(instruction =>
            instruction.Offset >= block.Start
            && instruction.Offset < block.End
            && instruction.Offset >= startOffset);

    static ImmutableArray<InstructionExceptionRegion> RequireLocation(
        InstructionExceptionFlowFacts exceptionFlow,
        int offset) =>
        exceptionFlow.LocationAt(offset) switch
        {
            InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Available available =>
                available.Value,
            InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Unavailable unavailable =>
                throw new InvalidOperationException(
                    $"Exception-flow location is unavailable "
                    + $"({unavailable.Reason}): {unavailable.Detail}"),
            InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Ambiguous ambiguous =>
                throw new InvalidOperationException(
                    "Exception-flow location is ambiguous: "
                    + ambiguous.Detail),
            _ => throw new InvalidOperationException(
                "Unknown exception-flow location result."),
        };

    static bool IsDefinitelyThrowing(
        DecodedInstruction instruction,
        IReadOnlyDictionary<int, MemberRef> calls) =>
        instruction.OpCode is ILOpCode.Throw or ILOpCode.Rethrow
        || (calls.TryGetValue(
                instruction.Offset,
                out MemberRef? callee)
            && FrameworkIdentity.IsCoreLibraryType(
                callee.DeclaringType,
                "System",
                "ThrowHelper"));

    static bool IsThrowingBoundary(
        ResourceOccurrence occurrence)
    {
        if (occurrence.Call is null
            || !occurrence.Operations.Contains(
                ResourceOccurrenceOperationKind.DirectCallBoundary)
            || occurrence.Operations.Contains(
                ResourceOccurrenceOperationKind.Acquisition)
            || occurrence.Operations.Contains(
                ResourceOccurrenceOperationKind.Release))
        {
            return false;
        }

        ResourceEffect.Operation[] operations =
        [
            .. occurrence.Effects
                .Select(effect => effect.Effect)
                .OfType<ResourceEffect.Operation>(),
        ];
        return operations.Length == 0
            || operations.Any(operation =>
                operation.Throws == ResourceOperationThrows.Possible);
    }

    internal static bool IsLifecycleBlocking(
        ResourceOccurrenceLimitation limitation)
    {
        if (limitation.Kind
            == ResourceOccurrenceLimitationKind.EffectResolution)
        {
            return true;
        }
        if (limitation.Kind
            == ResourceOccurrenceLimitationKind.BodyAnalysis)
        {
            return limitation.EffectResolutionGap
                == ResourceEffectResolutionGapKind.PopulationIncomplete;
        }
        return limitation.Kind
            == ResourceOccurrenceLimitationKind.ValueFlow;
    }
}
