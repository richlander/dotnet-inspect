using System.Collections.Immutable;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal static class ResourceLifecycleAnalysisService
{
    internal static ResourceLifecycleMethodResult Analyze(
        MethodBodyAnalysisContext context,
        ResourceOccurrenceAnalysisResult occurrences,
        ImmutableArray<DirectCall> calls,
        Func<int, TypeRef?> resolveCatchType)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(resolveCatchType);

        var methodLimitations =
            ImmutableArray.CreateBuilder<ResourceLifecycleLimitation>();
        foreach (ResourceOccurrenceLimitation limitation
            in occurrences.Limitations.Where(
                static limitation => limitation.Root is null))
        {
            methodLimitations.Add(FromOccurrenceLimitation(limitation));
        }

        ImmutableArray<DirectCall> methodCalls =
        [
            .. calls.Where(call =>
                call.EvidenceMethod == context.Method),
        ];
        IReadOnlyDictionary<int, DirectCall> directCallsByOffset =
            methodCalls.ToDictionary(
                static call => call.ILOffset);
        IReadOnlyDictionary<int, MemberRef> callsByOffset =
            directCallsByOffset.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Callee);
        IReadOnlySet<int> throwsNeverOffsets = occurrences.Occurrences
            .Where(occurrence =>
                occurrence.Call is not null
                && !CanThrow(occurrence))
            .Select(static occurrence => occurrence.ILOffset)
            .ToHashSet();
        IReadOnlySet<int> cleanupHazards = methodCalls
            .Where(call =>
                call.Kind is CallKind.CallIndirect
                || (call.Kind is CallKind.Call
                        or CallKind.CallVirtual
                        or CallKind.NewObject
                    && !throwsNeverOffsets.Contains(call.ILOffset)
                    && !IsIntrinsicallyNonThrowing(
                        call.Callee,
                        call.Kind)
                    && !IsArrayPoolSharedGetter(call.Callee)))
            .Select(static call => call.ILOffset)
            .Concat(
                context.Instructions.Instructions
                    .Where(instruction =>
                        instruction.OpCode
                            is System.Reflection.Metadata.ILOpCode.Throw
                                or System.Reflection.Metadata.ILOpCode.Rethrow)
                    .Select(static instruction => instruction.Offset))
            .ToHashSet();

        ReachingDefinitionsResult? reaching = null;
        ResourceLifecycleLimitation? reachingLimitation = null;
        try
        {
            reaching = ReachingDefinitions.Analyze(
                context.Instructions,
                ArgumentSlotCount(context.Method));
            if (!reaching.IsComplete)
            {
                reachingLimitation = new(
                    ResourceLifecycleLimitationKind.ReachingDefinitions,
                    reaching.IncompleteReason
                        ?? "Reaching-definition analysis was incomplete.",
                    context.Method);
            }
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            reachingLimitation = new(
                ResourceLifecycleLimitationKind.ReachingDefinitions,
                $"{ex.GetType().Name}: {ex.Message}",
                context.Method);
        }

        InstructionExceptionFlowFacts? exceptionFlow = null;
        ResourceLifecycleLimitation? exceptionLimitation = null;
        if (context.Instructions.ExceptionFlow is
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Available available)
        {
            exceptionFlow = available.Value;
        }
        else
        {
            exceptionLimitation = new(
                ResourceLifecycleLimitationKind.ExceptionFlow,
                ExceptionFlowDetail(context.Instructions.ExceptionFlow),
                context.Method);
        }

        IReadOnlySet<MethodExceptionClauseId> catchAllCleanup =
            ImmutableHashSet<MethodExceptionClauseId>.Empty;
        if (exceptionFlow is not null)
        {
            try
            {
                catchAllCleanup =
                    ResourceExceptionPathAnalyzer
                        .ComputeCreditableCatchCleanup(
                            context.RequireExceptionCatalog(),
                            resolveCatchType);
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                exceptionFlow = null;
                exceptionLimitation = new(
                    ResourceLifecycleLimitationKind.ExceptionFlow,
                    $"{ex.GetType().Name}: {ex.Message}",
                    context.Method);
            }
        }

        var roots =
            ImmutableArray.CreateBuilder<ResourceLifecycleRootResult>(
                occurrences.Roots.Length);
        foreach (ResourceOccurrenceRoot root in occurrences.Roots)
        {
            var rootLimitations =
                ImmutableArray.CreateBuilder<ResourceLifecycleLimitation>();
            foreach (ResourceOccurrenceLimitation limitation
                in occurrences.Limitations.Where(candidate =>
                    candidate.Root == root))
            {
                rootLimitations.Add(FromOccurrenceLimitation(limitation));
            }

            var rootOccurrences = occurrences.Occurrences
                .Where(occurrence => occurrence.Root == root)
                .OrderBy(static occurrence => occurrence.ILOffset)
                .ToImmutableArray();
            if (root is not ResourceOccurrenceRoot.Acquisition acquisition)
            {
                rootLimitations.Add(new(
                    ResourceLifecycleLimitationKind.UnsupportedFlow,
                    "Incoming resource roots are not supported by lifecycle "
                    + "analysis version 1.",
                    context.Method,
                    root));
                roots.Add(new(root, [], rootLimitations.ToImmutable()));
                continue;
            }

            if (reachingLimitation is not null)
            {
                rootLimitations.Add(ForRoot(reachingLimitation, root));
            }
            if (exceptionLimitation is not null)
            {
                rootLimitations.Add(ForRoot(exceptionLimitation, root));
            }
            roots.Add(AnalyzeAcquisition(
                context,
                acquisition,
                rootOccurrences,
                directCallsByOffset,
                callsByOffset,
                reaching,
                exceptionFlow,
                catchAllCleanup,
                cleanupHazards,
                rootLimitations));
        }

        return new(
            context.Method,
            roots.ToImmutable(),
            methodLimitations.ToImmutable());
    }

    static ResourceLifecycleRootResult AnalyzeAcquisition(
        MethodBodyAnalysisContext context,
        ResourceOccurrenceRoot.Acquisition root,
        ImmutableArray<ResourceOccurrence> occurrences,
        IReadOnlyDictionary<int, DirectCall> directCallsByOffset,
        IReadOnlyDictionary<int, MemberRef> callsByOffset,
        ReachingDefinitionsResult? reaching,
        InstructionExceptionFlowFacts? exceptionFlow,
        IReadOnlySet<MethodExceptionClauseId> catchAllCleanup,
        IReadOnlySet<int> cleanupHazards,
        ImmutableArray<ResourceLifecycleLimitation>.Builder limitations)
    {
        var outcomes =
            ImmutableArray.CreateBuilder<ResourceLifecycleOutcome>();
        var releaseOffsets = ImmutableArray.CreateBuilder<int>();
        foreach (ResourceOccurrence release in occurrences.Where(
            occurrence => occurrence.Operations.Contains(
                ResourceOccurrenceOperationKind.Release)))
        {
            if (release.Effects.Any(effect =>
                effect.Effect is ResourceEffect.Release
                && effect.Guard is null
                && effect.Completion is
                    ResourceEffectCompletion.Entry
                        or ResourceEffectCompletion.NormalReturn))
            {
                releaseOffsets.Add(release.ILOffset);
            }
            else
            {
                limitations.Add(new(
                    ResourceLifecycleLimitationKind.UnsupportedFlow,
                    $"Conditional release at IL_{release.ILOffset:X4} "
                    + "is not supported by lifecycle analysis version 1.",
                    context.Method,
                    root));
            }
        }
        ImmutableArray<int> releases =
        [
            .. releaseOffsets
                .Distinct()
                .Order(),
        ];

        foreach (ResourceOccurrence transfer in occurrences.Where(
            occurrence =>
                occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.Storage)
                || occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.ReturnToCaller)))
        {
            outcomes.Add(new(
                ResourceLifecycleOutcomeKind.InvalidTransfer,
                root.Call.ILOffset,
                transfer.ILOffset,
                SecondaryOffset: null,
                Boundaries: []));
        }

        LocalDefinition? acquisitionDefinition = null;
        int pathStartOffset = root.Call.ILOffset;
        if (TryFindAcquisitionDefinition(
                context,
                root.Call.ILOffset,
                reaching,
                out LocalDefinition? definition,
                out int storeOffset))
        {
            acquisitionDefinition = definition;
            pathStartOffset = storeOffset;
        }
        else if (!occurrences.Any(occurrence =>
            occurrence.Operations.Contains(
                ResourceOccurrenceOperationKind.Storage)
            || occurrence.Operations.Contains(
                ResourceOccurrenceOperationKind.ReturnToCaller)))
        {
            limitations.Add(new(
                ResourceLifecycleLimitationKind.AcquisitionFlow,
                "The acquisition result was not proven to initialize one "
                + "tracked local.",
                context.Method,
                root));
        }

        ImmutableArray<int> localUses = [];
        if (acquisitionDefinition is not null && reaching?.IsComplete == true)
        {
            var uses = ImmutableArray.CreateBuilder<int>();
            foreach (LocalUse use in reaching.UsesOf(acquisitionDefinition))
            {
                if (use.Address)
                {
                    limitations.Add(new(
                        ResourceLifecycleLimitationKind.UnsupportedFlow,
                        $"The tracked local address is observed at "
                        + $"IL_{use.Offset:X4}.",
                        context.Method,
                        root));
                    continue;
                }
                if (!FeedsImmediateRelease(context, use.Offset, releases))
                    uses.Add(use.Offset);
            }
            localUses = uses.ToImmutable();
        }

        if (releases.Length > 0
            && localUses
                .Where(use => releases.Any(release =>
                    ResourceExceptionPathAnalyzer.ReachesInSameBlock(
                        context.Blocks,
                        release,
                        use)))
                .Order()
                .FirstOrDefault(-1) is int useAfterRelease
            && useAfterRelease >= 0)
        {
            outcomes.Add(new(
                ResourceLifecycleOutcomeKind.UseAfterRelease,
                root.Call.ILOffset,
                useAfterRelease,
                releases.Where(release =>
                        ResourceExceptionPathAnalyzer.ReachesInSameBlock(
                            context.Blocks,
                            release,
                            useAfterRelease))
                    .Min(),
                []));
        }

        if (releases.Length > 1)
        {
            var orderedPair =
                (from first in releases
                 from second in releases
                 where first != second
                     && ResourceExceptionPathAnalyzer.ReachesInSameBlock(
                         context.Blocks,
                         first,
                         second)
                 orderby second
                 select (First: first, Second: second))
                .FirstOrDefault();
            if (orderedPair != default)
            {
                outcomes.Add(new(
                    ResourceLifecycleOutcomeKind.DoubleRelease,
                    root.Call.ILOffset,
                    orderedPair.Second,
                    orderedPair.First,
                    []));
            }
        }

        ArrayPoolBoundarySet? arrayPoolBoundaries =
            IsArrayPoolRoot(root)
            && acquisitionDefinition is not null
            && reaching?.IsComplete == true
                ? ArrayPoolThrowingBoundaries(
                    context,
                    root,
                    acquisitionDefinition,
                    reaching,
                    directCallsByOffset,
                    callsByOffset,
                    occurrences)
                : null;
        if (arrayPoolBoundaries?.HasIndirectDispatch == true)
        {
            limitations.Add(new(
                ResourceLifecycleLimitationKind.UnsupportedFlow,
                "The tracked resource participates in indirect dispatch "
                + "or a method-group value.",
                context.Method,
                root));
        }
        if (arrayPoolBoundaries?.HasUnprovenSetup == true)
        {
            limitations.Add(new(
                ResourceLifecycleLimitationKind.ExceptionFlow,
                "A legacy setup operation is not proven nonthrowing.",
                context.Method,
                root));
        }
        ImmutableArray<ResourceLifecycleBoundaryEvidence> boundaries =
            arrayPoolBoundaries is { HasIndirectDispatch: false }
                ? arrayPoolBoundaries.All
                : arrayPoolBoundaries is null
                    ? ThrowingBoundaries(occurrences)
                    : [];
        if (!boundaries.IsEmpty && exceptionFlow is not null)
        {
            ResourceExceptionPathAnalyzer.ThrowingBoundaryClassification<
                ResourceLifecycleBoundaryEvidence> classification =
                ResourceExceptionPathAnalyzer.ClassifyThrowingBoundaries(
                    context.Blocks,
                    exceptionFlow,
                    catchAllCleanup,
                    releases,
                    boundaries,
                    static boundary => boundary.ILOffset,
                    cleanupHazards);
            foreach (ResourceLifecycleBoundaryEvidence boundary
                in classification.Indeterminate)
            {
                limitations.Add(new(
                    ResourceLifecycleLimitationKind.ExceptionFlow,
                    $"Cleanup release coverage is not proven for "
                    + $"IL_{boundary.ILOffset:X4}.",
                    context.Method,
                    root));
            }
            if (!classification.Unprotected.IsEmpty)
            {
                outcomes.Add(new(
                    ResourceLifecycleOutcomeKind.ExceptionalCleanupMissing,
                    root.Call.ILOffset,
                    classification.Unprotected[0].ILOffset,
                    SecondaryOffset: null,
                    classification.Unprotected));
            }
        }

        bool releasesSpanBlocks = releases
            .Select(context.Blocks.BlockIndexAt)
            .Distinct()
            .Skip(1)
            .Any();
        if (acquisitionDefinition is not null && releasesSpanBlocks)
        {
            limitations.Add(new(
                ResourceLifecycleLimitationKind.UnsupportedFlow,
                "Terminal-exit analysis does not support multiple "
                + "release sites for one resource root.",
                context.Method,
                root));
        }
        else if (acquisitionDefinition is not null)
        {
            ResourceExceptionPathAnalyzer.LeakExitFacts exits =
                ResourceExceptionPathAnalyzer.LeakExitsWithoutRelease(
                    context.Instructions.Instructions,
                    context.Blocks,
                    callsByOffset,
                    pathStartOffset,
                    releases);
            if (exits.Normal)
            {
                outcomes.Add(new(
                    ResourceLifecycleOutcomeKind.MissingReleaseOnNormalPath,
                    root.Call.ILOffset,
                    root.Call.ILOffset,
                    SecondaryOffset: null,
                    Boundaries: []));
            }
            if (exits.Exception)
            {
                outcomes.Add(new(
                    ResourceLifecycleOutcomeKind
                        .MissingReleaseOnExceptionalPath,
                    root.Call.ILOffset,
                    root.Call.ILOffset,
                    SecondaryOffset: null,
                    Boundaries: []));
                if (!outcomes.Any(outcome =>
                        outcome.Kind
                            == ResourceLifecycleOutcomeKind
                                .ExceptionalCleanupMissing))
                {
                    outcomes.Add(new(
                        ResourceLifecycleOutcomeKind
                            .ExceptionalCleanupMissing,
                        root.Call.ILOffset,
                        root.Call.ILOffset,
                        SecondaryOffset: null,
                        Boundaries: []));
                }
            }
        }

        return new(
            root,
            [
                .. outcomes
                    .Distinct()
                    .OrderBy(static outcome => outcome.PrimaryOffset)
                    .ThenBy(static outcome => outcome.Kind),
            ],
            limitations.ToImmutable());
    }

    static ImmutableArray<ResourceLifecycleBoundaryEvidence>
        ThrowingBoundaries(
            ImmutableArray<ResourceOccurrence> occurrences) =>
    [
        .. occurrences
            .Where(occurrence =>
                occurrence.Call is not null
                && occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.DirectCallBoundary)
                && !occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.Acquisition)
                && !occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.Release)
                && CanThrow(occurrence))
            .Select(occurrence =>
                new ResourceLifecycleBoundaryEvidence(
                    occurrence.ILOffset,
                    occurrence.Call!))
            .Distinct()
            .OrderBy(static boundary => boundary.ILOffset),
    ];

    sealed record ArrayPoolBoundarySet(
        ImmutableArray<ResourceLifecycleBoundaryEvidence> All,
        bool HasIndirectDispatch,
        bool HasUnprovenSetup);

    static ArrayPoolBoundarySet
        ArrayPoolThrowingBoundaries(
            MethodBodyAnalysisContext context,
            ResourceOccurrenceRoot.Acquisition root,
            LocalDefinition acquisitionDefinition,
            ReachingDefinitionsResult reaching,
            IReadOnlyDictionary<int, DirectCall> directCalls,
            IReadOnlyDictionary<int, MemberRef> calls,
            ImmutableArray<ResourceOccurrence> occurrences)
    {
        var boundaries =
            ImmutableArray.CreateBuilder<
                ResourceLifecycleBoundaryEvidence>();
        string? firstAmbiguousShape = null;
        bool hasIndirectDispatch = false;
        bool hasUnprovenSetup = false;
        Dictionary<int, ResourceOccurrence> occurrencesByOffset =
            occurrences
                .Where(occurrence => occurrence.Call is not null)
                .ToDictionary(occurrence => occurrence.ILOffset);
        foreach (LocalUse use in reaching.UsesOf(acquisitionDefinition)
            .OrderBy(static use => use.Offset))
        {
            if (use.Address)
            {
                firstAmbiguousShape ??= "alias-or-field-suppressed";
                continue;
            }
            ArrayPoolUseClassifier.UseClassification classification =
                ArrayPoolUseClassifier.ClassifyUse(
                    context.Instructions.Instructions,
                    calls,
                    use.Offset,
                    acquisitionDefinition.Slot,
                    root.Call.Callee.ReturnType);
            if (classification.Kind
                is ArrayPoolUseClassifier.UseKind.Release
                    or ArrayPoolUseClassifier.UseKind.LocalUse)
            {
                continue;
            }
            firstAmbiguousShape ??= classification.CandidateShape;
            if (classification.CandidateShape
                    != "cross-method-suppressed"
                || classification.Boundary is not { } boundary)
            {
                continue;
            }

            hasIndirectDispatch |= directCalls.Values.Any(call =>
                call.ILOffset >= use.Offset
                && call.ILOffset <= boundary.ILOffset
                && call.Kind is CallKind.LoadFunction
                    or CallKind.LoadVirtualFunction
                    or CallKind.CallIndirect);
            bool provenNonThrowing =
                (directCalls.TryGetValue(
                        boundary.ILOffset,
                        out DirectCall? boundaryCall)
                    && IsIntrinsicallyNonThrowing(
                        boundary.Operation,
                        boundaryCall.Kind))
                || (occurrencesByOffset.TryGetValue(
                        boundary.ILOffset,
                        out ResourceOccurrence? occurrence)
                    && !CanThrow(occurrence));
            if (!provenNonThrowing
                && classification.NonThrowingSetupBoundary)
            {
                hasUnprovenSetup = true;
            }
            else if (!provenNonThrowing)
            {
                AddBoundary(boundary);
            }
            if ((classification.NonThrowingSetupBoundary
                    || ArrayPoolUseClassifier
                        .IsTransparentWrapperBoundary(
                            boundary.Operation))
                && ResourceExceptionPathAnalyzer.FindBoundaryAfterSetup(
                    context.Instructions.Instructions,
                    reaching,
                    calls,
                    boundary) is { } downstream)
            {
                AddBoundary(downstream);
            }
        }

        if (!string.Equals(
                firstAmbiguousShape,
                "cross-method-suppressed",
                StringComparison.Ordinal))
        {
            return new(
                [],
                HasIndirectDispatch: false,
                HasUnprovenSetup: hasUnprovenSetup);
        }

        return new(
            [
                .. boundaries
                    .Distinct()
                    .OrderBy(static boundary => boundary.ILOffset),
            ],
            hasIndirectDispatch,
            hasUnprovenSetup);

        void AddBoundary(ResourceExceptionBoundary boundary)
        {
            if (directCalls.TryGetValue(
                    boundary.ILOffset,
                    out DirectCall? directCall))
            {
                var evidence = new ResourceLifecycleBoundaryEvidence(
                    boundary.ILOffset,
                    ResourceOccurrenceCallSite.From(directCall));
                boundaries.Add(evidence);
            }
        }
    }

    static bool IsArrayPoolSharedGetter(MemberRef member) =>
        member.Name == "get_Shared"
        && (FrameworkIdentity.IsKnownFrameworkType(
                member.DeclaringType,
                "System.Buffers",
                "System.Buffers",
                "ArrayPool`1")
            || FrameworkIdentity.IsCoreLibraryType(
                member.DeclaringType,
                "System.Buffers",
                "ArrayPool`1"));

    internal static bool IsIntrinsicallyNonThrowing(
        MemberRef member,
        CallKind kind) =>
        kind == CallKind.Call
        && member.Kind == MemberKind.Method
        && !member.HasThis
        && member.GenericArity == 0
        && (member.SignatureHeader & 0x0F) == 0
        && member.Name == "KeepAlive"
        && FrameworkIdentity.IsCoreLibraryType(
            member.DeclaringType,
            "System",
            "GC")
        && member.ParameterTypes is [var parameter]
        && FrameworkIdentity.IsCoreLibraryType(
            parameter,
            "System",
            "Object")
        && FrameworkIdentity.IsCoreLibraryType(
            member.ReturnType,
            "System",
            "Void");

    static bool IsArrayPoolRoot(
        ResourceOccurrenceRoot.Acquisition root) =>
        root.ResourceKinds.Any(kind =>
            kind.Identity == ArrayPoolResourceEffectModel.BufferKind);

    static bool CanThrow(ResourceOccurrence occurrence)
    {
        ImmutableArray<ResourceOccurrenceEffect> operations =
        [
            .. occurrence.Effects
                .Where(static effect =>
                    effect.Effect is ResourceEffect.Operation),
        ];
        return operations.IsEmpty
            || operations.Any(effect =>
                effect.Guard is not null
                || ((ResourceEffect.Operation)effect.Effect).Throws
                    == ResourceOperationThrows.Possible);
    }

    static bool TryFindAcquisitionDefinition(
        MethodBodyAnalysisContext context,
        int acquisitionOffset,
        ReachingDefinitionsResult? reaching,
        out LocalDefinition? definition,
        out int storeOffset)
    {
        definition = null;
        storeOffset = acquisitionOffset;
        if (reaching?.IsComplete != true
            || context.InstructionAt(acquisitionOffset) is not
                { } acquisitionInstruction)
        {
            return false;
        }

        int storeIndex = context.NextNonNopIndexAtOrAfter(
            acquisitionInstruction.NextOffset);
        ImmutableArray<DecodedInstruction> instructions =
            context.Instructions.Instructions;
        if (storeIndex >= instructions.Length
            || !MethodInstructionFacts.TryReadLocalSlot(
                instructions[storeIndex],
                out LocalSlotAccess access)
            || !access.IsStore
            || access.IsArgument)
        {
            return false;
        }

        int resolvedStoreOffset = instructions[storeIndex].Offset;
        storeOffset = resolvedStoreOffset;
        definition = reaching.Definitions.FirstOrDefault(candidate =>
            candidate.Offset == resolvedStoreOffset
            && candidate.Slot == access.Slot
            && !candidate.IsArgument);
        return definition is not null;
    }

    static bool FeedsImmediateRelease(
        MethodBodyAnalysisContext context,
        int useOffset,
        ImmutableArray<int> releases)
    {
        if (context.InstructionAt(useOffset) is not { } use)
            return false;
        int nextIndex =
            context.NextNonNopIndexAtOrAfter(use.NextOffset);
        ImmutableArray<DecodedInstruction> instructions =
            context.Instructions.Instructions;
        return nextIndex < instructions.Length
            && releases.Contains(instructions[nextIndex].Offset);
    }

    static ResourceLifecycleLimitation FromOccurrenceLimitation(
        ResourceOccurrenceLimitation limitation) =>
        new(
            ResourceLifecycleLimitationKind.ResourceOccurrence,
            limitation.Message,
            limitation.Method,
            limitation.Root,
            limitation);

    static ResourceLifecycleLimitation ForRoot(
        ResourceLifecycleLimitation limitation,
        ResourceOccurrenceRoot root) =>
        new(
            limitation.Kind,
            limitation.Detail,
            limitation.Method,
            root,
            limitation.OccurrenceLimitation);

    static string ExceptionFlowDetail(
        InstructionExceptionFlowResult<InstructionExceptionFlowFacts> result)
        => result switch
        {
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable unavailable =>
                $"Exception-flow analysis is unavailable "
                + $"({unavailable.Reason}): {unavailable.Detail}",
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Ambiguous ambiguous =>
                $"Exception-flow analysis is ambiguous: {ambiguous.Detail}",
            _ => "Exception-flow analysis was not available.",
        };

    static int ArgumentSlotCount(MethodIdentity method)
        => method.ParameterTypes.Length + (method.IsStatic ? 0 : 1);

    static bool IsRecoverable(Exception ex)
        => ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException
            or IndexOutOfRangeException;
}
