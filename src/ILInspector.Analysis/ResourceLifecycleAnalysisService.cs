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
        IReadOnlyDictionary<int, MemberRef> callsByOffset =
            methodCalls.ToDictionary(
                static call => call.ILOffset,
                static call => call.Callee);

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
                callsByOffset,
                reaching,
                exceptionFlow,
                catchAllCleanup,
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
        IReadOnlyDictionary<int, MemberRef> callsByOffset,
        ReachingDefinitionsResult? reaching,
        InstructionExceptionFlowFacts? exceptionFlow,
        IReadOnlySet<MethodExceptionClauseId> catchAllCleanup,
        ImmutableArray<ResourceLifecycleLimitation>.Builder limitations)
    {
        var outcomes =
            ImmutableArray.CreateBuilder<ResourceLifecycleOutcome>();
        ImmutableArray<int> releases =
        [
            .. occurrences
                .Where(occurrence => occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.Release))
                .Select(static occurrence => occurrence.ILOffset)
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

        ImmutableArray<ResourceLifecycleBoundaryEvidence> boundaries =
            ThrowingBoundaries(occurrences);
        if (!boundaries.IsEmpty && exceptionFlow is not null)
        {
            ImmutableArray<ResourceLifecycleBoundaryEvidence> unprotected =
                ResourceExceptionPathAnalyzer.UnprotectedThrowingBoundaries(
                    context.Blocks,
                    exceptionFlow,
                    catchAllCleanup,
                    releases,
                    boundaries,
                    static boundary => boundary.ILOffset);
            if (!unprotected.IsEmpty)
            {
                outcomes.Add(new(
                    ResourceLifecycleOutcomeKind.ExceptionalCleanupMissing,
                    root.Call.ILOffset,
                    unprotected[0].ILOffset,
                    SecondaryOffset: null,
                    unprotected));
            }
        }

        if (acquisitionDefinition is not null
            && releases.Length <= 1)
        {
            ResourceExceptionPathAnalyzer.LeakExitKind exit =
                ResourceExceptionPathAnalyzer.PathExitsWithoutRelease(
                    context.Instructions.Instructions,
                    context.Blocks,
                    callsByOffset,
                    pathStartOffset,
                    releases);
            if (exit != ResourceExceptionPathAnalyzer.LeakExitKind.None)
            {
                outcomes.Add(new(
                    exit
                        == ResourceExceptionPathAnalyzer.LeakExitKind.Exception
                            ? ResourceLifecycleOutcomeKind
                                .MissingReleaseOnExceptionalPath
                            : ResourceLifecycleOutcomeKind
                                .MissingReleaseOnNormalPath,
                    root.Call.ILOffset,
                    root.Call.ILOffset,
                    SecondaryOffset: null,
                    Boundaries: []));
                if (exit
                    == ResourceExceptionPathAnalyzer.LeakExitKind.Exception
                    && !outcomes.Any(outcome =>
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
        ThrowingBoundaries(ImmutableArray<ResourceOccurrence> occurrences) =>
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

    static bool CanThrow(ResourceOccurrence occurrence)
    {
        ImmutableArray<ResourceEffect.Operation> operations =
        [
            .. occurrence.Effects
                .Select(static effect => effect.Effect)
                .OfType<ResourceEffect.Operation>(),
        ];
        return operations.IsEmpty
            || operations.Any(operation =>
                operation.Throws == ResourceOperationThrows.Possible);
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
