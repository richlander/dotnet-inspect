using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>
/// Reader- and raw-body-dependent facts needed by
/// <see cref="OptimizationOpportunityAnalysis"/>.
/// </summary>
/// <remarks>
/// The assembly reader retains ownership of its metadata reader, the caller's
/// generic scope, and raw IL. This contract exposes only the factual answers
/// needed by the producer, so none of those reader-owned inputs reach it.
/// </remarks>
internal interface IOptimizationOpportunityResolver
{
    MemberRef ResolveMember(int token);

    TypeRef ResolveType(int token);

    bool IsAllocatingValueTypeBox(int operandToken, TypeRef boxed);

    bool GenericParameterCanBeValueType(TypeRef genericParameter);

    bool IsStableReceiverGetter(DecodedInstruction instruction);

    bool IsAsyncStateMachineType(TypeRef? type);

    ReachingDefinitionsResult AnalyzeReachingDefinitions();
}

internal static partial class OptimizationOpportunityAnalysis
{
    internal static ImmutableArray<OptimizationOpportunity> Collect(
        MethodBodyAnalysisContext context,
        ImmutableArray<AllocationOccurrence> discoveredAllocations,
        MethodAllocationAnalysis allocationAnalysis,
        IOptimizationOpportunityResolver resolver)
    {
        var caller = context.Method;
        var opportunities = ImmutableArray.CreateBuilder<OptimizationOpportunity>();
        // Discovered allocation occurrences for this method, scanned once by the caller
        // and shared here to avoid a redundant second allocation scan. Escape state is not read.
        var allocationByOffset = discoveredAllocations.ToDictionary(occurrence => occurrence.ILOffset);
        ReachingDefinitionsResult? reachingDefinitions = null;
        ReachingDefinitionsResult GetReachingDefinitions()
            => reachingDefinitions ??= resolver.AnalyzeReachingDefinitions();

        var branchTargetOffsets = context.Instructions.Instructions
            .SelectMany(static instruction => instruction.BranchTargets)
            .ToArray();
        int? pendingConstant = null;
        int pendingConstantOffset = -1;
        int pendingConstantBlock = -1;
        // Delegate creation is `<push target>; ldftn/ldvirtftn M; newobj DelegateCtor`.
        // Track the pending function-pointer load so a single row is emitted at the
        // newobj (one row per delegate allocation), classified by the target.
        int? pendingDelegateOffset = null;
        bool pendingDelegateCapturing = false;
        bool pendingDelegateInstanceGroup = false;
        bool pendingDelegateStableReceiver = false;
        // The opcode that loaded the delegate receiver (the instruction before ldftn).
        // A static method group loads `ldnull`; a real instance receiver is anything else.
        ILOpCode previousOpcode = default;
        DecodedInstruction? previousInstruction = null;
        DecodedInstruction? previousPreviousInstruction = null;
        // A `box` of a concrete value type is deferred until the next instruction so we can
        // see whether the boxed value escapes (into a ref array, a call, a field, or a
        // return) rather than being consumed locally (unbox round-trip / type test).
        int? pendingBoxOffset = null;
        TypeRef? pendingBoxType = null;
        bool pendingBoxInLoop = false;
        int? pendingGenericObjectBoxOffset = null;
        TypeRef? pendingGenericObjectBoxType = null;
        bool pendingGenericObjectBoxConstrained = false;
        // Index (into opportunities) of a just-emitted delegate row awaiting its consumer:
        // if the delegate flows straight into a lazy LINQ operator, the obvious iterator
        // rewrite only moves the allocation, so we annotate that on the row.
        int? pendingDelegateOpportunityIndex = null;
        foreach (var instruction in context.Instructions.Instructions)
        {
            int offset = instruction.Offset;
            var opcode = instruction.OpCode;
            switch (opcode)
            {
                case ILOpCode.Ldc_i4_m1:
                case ILOpCode.Ldc_i4_0:
                case ILOpCode.Ldc_i4_1:
                case ILOpCode.Ldc_i4_2:
                case ILOpCode.Ldc_i4_3:
                case ILOpCode.Ldc_i4_4:
                case ILOpCode.Ldc_i4_5:
                case ILOpCode.Ldc_i4_6:
                case ILOpCode.Ldc_i4_7:
                case ILOpCode.Ldc_i4_8:
                    SetPendingConstant(
                        opcode switch
                        {
                            ILOpCode.Ldc_i4_m1 => -1,
                            ILOpCode.Ldc_i4_0 => 0,
                            ILOpCode.Ldc_i4_1 => 1,
                            ILOpCode.Ldc_i4_2 => 2,
                            ILOpCode.Ldc_i4_3 => 3,
                            ILOpCode.Ldc_i4_4 => 4,
                            ILOpCode.Ldc_i4_5 => 5,
                            ILOpCode.Ldc_i4_6 => 6,
                            ILOpCode.Ldc_i4_7 => 7,
                            _ => 8,
                        },
                        offset);
                    break;
                case ILOpCode.Ldc_i4_s:
                    SetPendingConstant((int)instruction.OperandValue, offset);
                    break;
                case ILOpCode.Ldc_i4:
                    SetPendingConstant(MethodInstructionFacts.OperandInt32(instruction), offset);
                    break;
                case ILOpCode.Newarr:
                {
                    int elementToken = MethodInstructionFacts.OperandInt32(instruction);
                    if (allocationByOffset.TryGetValue(offset, out var arrayAllocation)
                        && arrayAllocation.Kind == AllocationKind.Array
                        && ValidPendingConstant(offset) is int length && length >= 0 && length <= 8)
                    {
                        // Promote to a confident stackalloc recommendation only when the
                        // array provably stays local AND its element type is stackalloc-
                        // eligible (an unmanaged primitive); otherwise keep the
                        // non-committal shape.
                        bool local = ArrayEscapeAnalysis.ArrayProvablyStaysLocal(
                                context,
                                GetReachingDefinitions(),
                                instruction.NextOffset)
                            && IsStackallocEligibleElement(resolver.ResolveType(elementToken));
                        opportunities.Add(local
                            ? new OptimizationOpportunity(
                                caller,
                                "stackalloc-candidate",
                                $"newarr with small constant length ({length}) that does not escape",
                                "The array stays local, so a stackalloc span avoids the heap allocation.",
                                "high",
                                context.IsInLoopRegion(offset),
                                offset,
                                null)
                            : new OptimizationOpportunity(
                                caller,
                                "small-array",
                                $"newarr with small constant length ({length})",
                                "If the array does not escape, a span or stackalloc may avoid the allocation.",
                                "medium",
                                context.IsInLoopRegion(offset),
                                offset,
                                "Escape not analyzed; confirm the array stays local before replacing."));
                    }
                    ClearPendingConstant();
                    break;
                }
                case ILOpCode.Newobj:
                {
                    ClearPendingConstant();
                    if (pendingDelegateOffset is not null)
                    {
                        // A function pointer was just loaded, so this newobj is the delegate
                        // allocation. Two cases allocate a delegate per call and are worth
                        // reporting: a closure (captures locals/receiver) and an instance
                        // method group (binds the receiver). Non-capturing lambdas and static
                        // method groups are compiler-cached, so they are not reported. Also
                        // suppress the IL cache pattern directly (`ldsfld; dup; brtrue; ...;
                        // newobj; dup; stsfld`) so cached delegates are not misreported when
                        // the target method's compiler-generated identity is unavailable.
                        bool cachedOnce = allocationByOffset.TryGetValue(offset, out var delegateAllocation)
                            && delegateAllocation.Kind == AllocationKind.Delegate
                            && delegateAllocation.Frequency == AllocationFrequency.CachedOnce;
                        if (!cachedOnce && pendingDelegateCapturing)
                        {
                            // Confidence tracks semantic loop iteration: a delegate that
                            // genuinely repeats each iteration is high; a one-shot delegate —
                            // including a loop early-exit that runs once — is low, especially
                            // since .NET 10+ partially stack-allocates non-escaping ones.
                            var inLoop = context.IsInLoopRegion(offset);
                            bool iteratesInLoop = allocationAnalysis.MultiplicityAt(offset) == AllocationMultiplicity.Loop;
                            pendingDelegateOpportunityIndex = opportunities.Count;
                            opportunities.Add(new OptimizationOpportunity(
                                caller,
                                "capturing-delegate",
                                "delegate over a captured receiver or closure",
                                "Each call allocates a closure delegate; a static local function with explicit state parameters avoids it.",
                                iteratesInLoop ? "high" : "low",
                                inLoop,
                                offset,
                                "On .NET 10+ the JIT can partially stack-allocate a non-escaping closure (~88 to ~36 bytes/call measured), reducing but not eliminating it; it stays a full heap allocation when the closure escapes the method — stored, returned, or passed to a callee that lets it escape."));
                        }
                        else if (!cachedOnce && pendingDelegateInstanceGroup)
                        {
                            var inLoop = context.IsInLoopRegion(offset);
                            bool iteratesInLoop = allocationAnalysis.MultiplicityAt(offset) == AllocationMultiplicity.Loop;
                            bool stackGuardFallback = StackGuardFallbackAnalysis.IsFallbackAllocation(
                                context,
                                offset,
                                resolver);
                            pendingDelegateOpportunityIndex = opportunities.Count;
                            opportunities.Add(new OptimizationOpportunity(
                                caller,
                                "instance-method-group-delegate",
                                "delegate over an instance method group (binds the receiver)",
                                stackGuardFallback
                                    ? "This delegate allocation is on a StackGuard fallback path, not the common path; if profiles show it matters, cache it in a field when the receiver is stable or use a static method with explicit state."
                                    : "Each call allocates a delegate that binds the receiver; cache it in a field when the receiver is stable, or use a static method with explicit state.",
                                stackGuardFallback ? "low" : iteratesInLoop ? "high" : "low",
                                inLoop,
                                offset,
                                stackGuardFallback
                                    ? "Cold StackGuard fallback; not a steady-state per-call allocation."
                                    : "On .NET 10+ the JIT can partially stack-allocate a non-escaping delegate (~88 to ~36 bytes/call measured), reducing but not eliminating it; it stays a full heap allocation when it escapes the method — stored, returned, or passed to a callee that lets it escape.",
                                ColdPath: stackGuardFallback));
                        }
                        pendingDelegateOffset = null;
                    }
                    if (allocationByOffset.TryGetValue(offset, out var stateMachineAllocation)
                        && stateMachineAllocation.Kind == AllocationKind.StateMachine
                        && resolver.IsAsyncStateMachineType(stateMachineAllocation.AllocatedType))
                    {
                        var inLoop = context.IsInLoopRegion(offset);
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "async-state-machine",
                            $"async state-machine allocation ({stateMachineAllocation.Detail ?? "state machine"})",
                            "Async state machines are intrinsic to async/async-iterator lowering: this usually moves work into a state object rather than eliminating it, and is often once per call/enumeration/subscription rather than per item. Optimize only if profiles show this method creates state machines repeatedly on a hot path.",
                            inLoop ? "medium" : "low",
                            inLoop,
                            offset,
                            inLoop
                                ? "Repeated async state-machine allocation at a loop call site; still verify whether the async operation itself is required."
                                : "Amortized async state-machine allocation: often once per call/enumeration/subscription, not per item.",
                            ColdPath: false)
                        {
                            Amortized = !inLoop,
                        });
                    }
                    break;
                }
                case ILOpCode.Call:
                case ILOpCode.Callvirt:
                {
                    ClearPendingConstant();
                    int token = MethodInstructionFacts.OperandInt32(instruction);
                    var callee = resolver.ResolveMember(token);
                    if (opcode == ILOpCode.Callvirt
                        && pendingGenericObjectBoxOffset is { } genericBoxOffset
                        && pendingGenericObjectBoxConstrained
                        && IsObjectEquals(callee))
                    {
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "generic-parameter-object-box",
                            $"generic parameter {pendingGenericObjectBoxType?.ToDisplayString() ?? "T"} boxed for System.Object.Equals(object)",
                            "Use EqualityComparer<T>.Default.Equals, or constrain T to IEquatable<T> and call typed equality, so value-type instantiations do not box.",
                            "medium",
                            context.IsInLoopRegion(genericBoxOffset),
                            genericBoxOffset,
                            "The box allocates only for value-type instantiations; static analysis does not establish which constructed generic types execute at runtime."));
                    }
                    // When the delegate just allocated flows straight into a lazy LINQ
                    // operator (Where/Select/…), a static-local-function rewrite removes the
                    // closure but the LINQ call still allocates a deferred-query iterator per
                    // call — the allocation is reduced, not eliminated. Annotate the surfaced
                    // fix so a cleared closure shape is not read as a free win. (Eager
                    // membership terminals — Any/Count/… — allocate no iterator and are
                    // handled by the linq-scan-in-loop shape, so they are not annotated here.)
                    if (pendingDelegateOpportunityIndex is { } moveIndex
                        && opportunities[moveIndex].Shape == "instance-method-group-delegate"
                        && pendingDelegateStableReceiver
                        && IsConcurrentDictionaryGetOrAdd(callee))
                    {
                        var row = opportunities[moveIndex];
                        opportunities[moveIndex] = row with
                        {
                            Shape = "cache-lookup-factory-delegate",
                            Evidence = "instance method-group delegate constructed for ConcurrentDictionary<TKey, TValue>.GetOrAdd valueFactory",
                            SafeFixDirection = "Cache the stable-receiver value-factory delegate in a field, or use a static factory with explicit state, so cache hits do not allocate a fresh delegate.",
                            Confidence = "high",
                            Caveat = "The delegate escapes to ConcurrentDictionary.GetOrAdd on every invocation; static analysis does not establish the cache-hit rate or call frequency.",
                        };
                    }
                    else if (pendingDelegateOpportunityIndex is { } lazyIndex
                        && RepeatedScanAnalysis.IsLinqLazyProducer(callee, out _))
                    {
                        var row = opportunities[lazyIndex];
                        opportunities[lazyIndex] = row with
                        {
                            SafeFixDirection = "Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both.",
                            Caveat = "A delegate-only rewrite does not remove the allocation; the lazy LINQ call still allocates an iterator.",
                        };
                    }
                    if (IsBitConverterGetBytes(callee))
                    {
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "temporary-byte-array-copy",
                            $"{callee.DeclaringType.ToQualifiedDisplayString()}::{callee.Name}",
                            "Prefer BinaryPrimitives.Write* or a stackalloc span when byte order is known.",
                            "high",
                            context.IsInLoopRegion(offset),
                            offset,
                            null));
                    }
                    else if (IsSpanToArrayCopy(callee, out var copyReceiver))
                    {
                        if (!ArrayEscapeAnalysis.SpanToArrayResultEscapes(
                                context,
                                GetReachingDefinitions(),
                                instruction.NextOffset))
                        {
                            opportunities.Add(new OptimizationOpportunity(
                                caller,
                                "span-to-array-copy",
                                copyReceiver,
                                "Let the span flow through to the consumer instead of materializing a copy when the array is not retained.",
                                "medium",
                                context.IsInLoopRegion(offset),
                                offset,
                                "The copy is required if the array escapes (returned, stored, or passed to an array-typed API)."));
                        }
                    }
                    else if (RepeatedScanAnalysis.IsLinqMaterializer(callee, out var materializeOp)
                        && context.IsInLoopRegion(offset)
                        && LoopInvariantMaterializerAnalysis.TryGetLoopInvariantSource(
                            context,
                            GetReachingDefinitions(),
                            offset,
                            out var sourceEvidence))
                    {
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "materialize-in-loop",
                            $"Enumerable.{materializeOp}(...) inside a loop over loop-invariant source ({sourceEvidence})",
                            "Hoist the ToArray/ToList materialization outside the loop, or cache it before the loop, so each iteration reuses the same snapshot.",
                            "high",
                            true,
                            offset,
                            "Only valid when the source sequence is unchanged during the loop; this row requires complete reaching-defs and an outside-loop source definition."));
                    }
                    else if (RepeatedScanAnalysis.IsLinqMembershipScan(callee, out var scanOp) && context.IsInLoopRegion(offset))
                    {
                        // A membership/search LINQ terminal (Any, First, Count, Contains, …)
                        // that runs inside a loop re-scans its sequence on every iteration.
                        // If the scanned sequence scales with the loop this is O(n*m) — the
                        // canonical fix is to build a set/dictionary index once outside the loop.
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "linq-scan-in-loop",
                            $"Enumerable.{scanOp}(...) inside a loop",
                            "Linear LINQ scan per iteration; precompute a set/dictionary index (or hoist the result) once outside the loop.",
                            "medium",
                            true,
                            offset,
                            "Quadratic only if the scanned sequence grows with the loop; a small or constant sequence is fine."));
                    }
                    else if (RepeatedScanAnalysis.IsStringConcat(callee) && context.IsInLoopRegion(offset)
                        && StringConcatAccumulationAnalysis.AccumulatesIntoSource(
                            context,
                            offset,
                            instruction.NextOffset,
                            callee.ParameterTypes.Length,
                            resolver))
                    {
                        // `s += …` inside a loop lowers to String.Concat(s, …) stored back to
                        // the same local/parameter. Each iteration copies the whole growing
                        // accumulator, so the loop is O(n^2) in the final length — the
                        // canonical StringBuilder fix. Only this self-accumulation shape is
                        // reported: a non-accumulating String.Concat/Format/Join in a loop
                        // (e.g. `list.Add($"{k}={v}")`, `return $"{a}-{b}"`) allocates one
                        // transient per iteration with no StringBuilder rewrite, so it is not
                        // flagged — that tier was measured to be essentially all false
                        // positives on real assemblies.
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "string-build-in-loop",
                            "string += in a loop (String.Concat onto a growing accumulator)",
                            "Repeated concatenation copies the whole accumulator each iteration (O(n^2)); build with a StringBuilder hoisted outside the loop and ToString() once after.",
                            "high",
                            true,
                            offset,
                            null));
                    }
                    else if (RepeatedScanAnalysis.IsInterfaceEnumeratorAllocation(callee) && context.IsInLoopRegion(offset))
                    {
                        // foreach over an interface (IEnumerable/IEnumerable<T>) binds to a
                        // GetEnumerator returning the reference-type IEnumerator/IEnumerator<T>,
                        // whose implementation is a heap object — one allocation per foreach.
                        // foreach over a concrete type uses a struct enumerator and allocates
                        // nothing. Only the in-loop case is reported: a foreach inside a loop
                        // re-allocates the enumerator each outer iteration. A one-shot foreach
                        // allocates once and was measured to be essentially all noise.
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "enumerator-allocation",
                            $"foreach over an interface allocates a reference-type enumerator ({callee.ReturnType.ToQualifiedDisplayString()})",
                            "Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List<T>) uses a struct enumerator, or index/iterate it once outside the loop.",
                            "medium",
                            true,
                            offset,
                            "No allocation when the static type has a struct enumerator; worthwhile only if the concrete type is reachable at this call site."));
                    }
                    break;
                }
                case ILOpCode.Ldftn:
                case ILOpCode.Ldvirtftn:
                {
                    ClearPendingConstant();
                    int token = MethodInstructionFacts.OperandInt32(instruction);
                    // Defer emission to the following newobj (de-dup). Capture is decided
                    // by the target method's declaring type: a lambda that closes over state
                    // is emitted on a compiler-generated display class. An instance method
                    // group binds a runtime receiver (never cached), so it allocates per call
                    // too; we recognize it as a target on an ordinary type (nested
                    // compiler-generated names contain "<>") whose receiver is a real
                    // instance (the preceding load is not `ldnull`). Non-capturing lambdas
                    // (`<>c` cache) and static method groups (`ldnull` receiver) are
                    // compiler-cached and not reported.
                    var ftnTarget = resolver.ResolveMember(token);
                    pendingDelegateOffset = offset;
                    pendingDelegateCapturing = IsClosureTarget(ftnTarget);
                    pendingDelegateInstanceGroup = !pendingDelegateCapturing
                        && ftnTarget.Kind != MemberKind.Unsupported
                        && !CompilerGeneratedNames.LeafName(ftnTarget.DeclaringType).Contains("<>", StringComparison.Ordinal)
                        && previousOpcode != ILOpCode.Ldnull;
                    pendingDelegateStableReceiver = (previousOpcode == ILOpCode.Ldarg_0
                            && !caller.IsStatic
                        || (previousInstruction is { OpCode: ILOpCode.Call or ILOpCode.Callvirt } receiverCall
                            && previousPreviousInstruction?.OpCode == ILOpCode.Ldarg_0
                            && !caller.IsStatic
                            && resolver.IsStableReceiverGetter(receiverCall)));
                    break;
                }
                case ILOpCode.Ldarg_0:
                    ClearPendingConstant();
                    break;
                case ILOpCode.Ldarg:
                    ClearPendingConstant();
                    break;
                case ILOpCode.Ldarg_s:
                    ClearPendingConstant();
                    break;
                case ILOpCode.Ldfld:
                case ILOpCode.Ldflda:
                case ILOpCode.Stfld:
                    ClearPendingConstant();
                    break;
                case ILOpCode.Box:
                {
                    ClearPendingConstant();
                    int token = MethodInstructionFacts.OperandInt32(instruction);
                    var boxed = resolver.ResolveType(token);
                    // ECMA-335 permits `box` on reference types (a no-op) and generic
                    // parameters (compiler-mandated, JIT-specialized), and `box Nullable<T>`
                    // allocates only when non-null. Flag only a positively-identified,
                    // unconditionally-allocating value type. Escape is decided at the
                    // consumer below.
                    allocationByOffset.TryGetValue(offset, out var boxAllocation);
                    var allocating = boxAllocation is { Kind: AllocationKind.Box }
                        && resolver.IsAllocatingValueTypeBox(token, boxed);
                    // A box that flows into a throw within a few instructions is an
                    // error-path allocation (an exception message: `throw new
                    // ArgumentException($"bad {x}")` lowers to box; Format; newobj; throw).
                    // It executes at most once before unwinding, not in steady state, so it
                    // is not pay-dirt — suppress it entirely (mirrors excluding exception
                    // construction from allocation density), not merely demote it off the
                    // hot-loop bit.
                    var feedsThrow = allocating && boxAllocation!.Escape == AllocationEscape.ThrowPath;
                    pendingBoxOffset = allocating && !feedsThrow ? offset : null;
                    pendingBoxType = allocating && !feedsThrow ? boxAllocation!.AllocatedType ?? boxed : null;
                    // Semantic loop iteration (a loop early-exit box runs once, so it is
                    // not a hot loop) drives the box confidence and the Loop signal.
                    pendingBoxInLoop = pendingBoxOffset is not null
                        && boxAllocation!.Multiplicity == AllocationMultiplicity.Loop;
                    bool genericObjectBoxCandidate = boxed.Kind is
                            TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter
                        && resolver.GenericParameterCanBeValueType(boxed);
                    pendingGenericObjectBoxOffset = genericObjectBoxCandidate
                        ? offset
                        : null;
                    pendingGenericObjectBoxType = genericObjectBoxCandidate
                        ? boxed
                        : null;
                    pendingGenericObjectBoxConstrained = false;
                    break;
                }
                case ILOpCode.Constrained:
                {
                    ClearPendingConstant();
                    int token = MethodInstructionFacts.OperandInt32(instruction);
                    pendingGenericObjectBoxConstrained =
                        pendingGenericObjectBoxType is not null
                        && resolver.ResolveType(token)
                            .Equals(pendingGenericObjectBoxType);
                    break;
                }
                default:
                    ClearPendingConstant();
                    break;
            }

            // A bare ldftn not consumed by the next newobj does not allocate a delegate.
            // Stack-neutral nops between the ldftn and newobj (e.g. Debug IL) are skipped.
            if (opcode is not (ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Newobj or ILOpCode.Nop))
            {
                pendingDelegateOffset = null;
                pendingDelegateStableReceiver = false;
            }

            // The "moved allocation" annotation only applies when the delegate flows
            // directly into its consuming call. Keep the pending index alive across the
            // delegate newobj and intervening nops; clear it once any other instruction
            // (including the consuming call, already handled above) is processed.
            if (opcode is not (ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Newobj or ILOpCode.Nop))
            {
                pendingDelegateOpportunityIndex = null;
                pendingDelegateStableReceiver = false;
            }

            // A boxed concrete value type that flows straight into an escaping consumer
            // (stored into a reference array, passed to a call/ctor, written to a field, or
            // returned) is a real heap allocation. A box consumed locally (unbox round-trip,
            // type test) does not escape and is not reported. Nops are skipped (Debug IL).
            if (opcode is not (ILOpCode.Box or ILOpCode.Nop))
            {
                if (pendingBoxOffset is { } boxOffset && IsEscapingBoxConsumer(opcode))
                {
                    opportunities.Add(new OptimizationOpportunity(
                        caller,
                        "box-value-type",
                        $"box {pendingBoxType?.ToQualifiedDisplayString() ?? "value type"}",
                        "Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it.",
                        pendingBoxInLoop ? "high" : "medium",
                        pendingBoxInLoop,
                        boxOffset,
                        pendingBoxInLoop ? null : "The JIT can elide some non-escaping boxing after inlining; confirm the box escapes (e.g. into a collection or object[])."));
                }
                pendingBoxOffset = null;
                pendingBoxType = null;
            }

            if (opcode is not (ILOpCode.Box or ILOpCode.Constrained or ILOpCode.Nop))
            {
                pendingGenericObjectBoxOffset = null;
                pendingGenericObjectBoxType = null;
                pendingGenericObjectBoxConstrained = false;
            }

            // Remember the receiver-bearing instruction. Nops never carry the receiver, so
            // they do not overwrite it (Debug IL can interleave them before the ldftn).
            if (opcode != ILOpCode.Nop)
            {
                previousPreviousInstruction = previousInstruction;
                previousInstruction = instruction;
                previousOpcode = opcode;
            }
        }

        return [.. opportunities.Select(AnnotateOpportunityMetadata)];

        void SetPendingConstant(int value, int instructionOffset)
        {
            pendingConstant = value;
            pendingConstantOffset = instructionOffset;
            pendingConstantBlock = context.Blocks.BlockIndexAt(instructionOffset);
        }

        void ClearPendingConstant()
        {
            pendingConstant = null;
            pendingConstantOffset = -1;
            pendingConstantBlock = -1;
        }

        int? ValidPendingConstant(int newarrOffset)
            => pendingConstant is { } value
                && context.Blocks.IsComplete
                && pendingConstantBlock >= 0
                // EH-aware blocks can split protected regions at every instruction; only a
                // real branch target between the constant and newarr makes the length joined.
                && (pendingConstantBlock == context.Blocks.BlockIndexAt(newarrOffset)
                    || !HasBranchTargetBetween(pendingConstantOffset, newarrOffset))
                ? value
                : null;

        bool HasBranchTargetBetween(int startExclusive, int endInclusive)
            => branchTargetOffsets.Any(target => target > startExclusive && target <= endInclusive);

        OptimizationOpportunity AnnotateOpportunityMetadata(OptimizationOpportunity opportunity)
        {
            var annotated = opportunity;
            if (opportunity.ILOffset is { } opportunityOffset)
            {
                string? runtimeAllocation = opportunity.RuntimeAllocationType;
                allocationByOffset.TryGetValue(opportunityOffset, out var allocation);
                if (opportunity.Shape != "generic-parameter-object-box"
                    && allocation?.RuntimeAllocationType is { Length: > 0 } occurrenceRuntime)
                {
                    runtimeAllocation = occurrenceRuntime;
                }
                annotated = annotated with
                {
                    RuntimeAllocationType = runtimeAllocation,
                    PathContext = opportunity.PathContext ?? OptimizationOpportunityAnalysis.FormatPathContext(allocationAnalysis.PathContextAt(opportunityOffset)),
                    PathConfidence = opportunity.PathConfidence ?? OptimizationOpportunityAnalysis.FormatPathConfidence(allocationAnalysis.PathConfidenceAt(opportunityOffset)),
                    PostDominance = opportunity.PostDominance ?? OptimizationOpportunityAnalysis.FormatPostDominance(allocationAnalysis.PostDominanceAt(opportunityOffset)),
                    Multiplicity = opportunity.Multiplicity ?? OptimizationOpportunityAnalysis.FormatMultiplicity(
                        allocation?.Multiplicity is { } allocationMultiplicity
                            && allocationMultiplicity != AllocationMultiplicity.Unknown
                                ? allocationMultiplicity
                                : allocationAnalysis.MultiplicityAt(opportunityOffset)),
                    EstimatedSizeBytes = opportunity.EstimatedSizeBytes ?? allocation?.EstimatedSizeBytes,
                };
            }
            return OptimizationOpportunityAnalysis.AddFallbackMetadata(annotated);
        }
    }

    // True when a delegate's target method is a closure body emitted on a compiler-
    // generated display class (it closes over captured locals/parameters). The
    // non-capturing lambda cache type is named exactly <>c, and static/instance
    // method groups live on ordinary types, so none of those match.
    static bool IsClosureTarget(MemberRef target)
        => target.Kind != MemberKind.Unsupported
           && CompilerGeneratedNames.IsDisplayClass(target.DeclaringType);

    // Opcodes that consume a boxed value in a way that makes it escape (so the box is a
    // real heap allocation): stored into a reference array, passed to a call/ctor, written
    // to a field, or returned. Local round-trips (unbox/unbox.any/isinst/castclass/pop) are
    // deliberately absent.
    static bool IsEscapingBoxConsumer(ILOpCode op)
        => op is ILOpCode.Stelem_ref or ILOpCode.Call or ILOpCode.Callvirt
            or ILOpCode.Newobj or ILOpCode.Stfld or ILOpCode.Stsfld or ILOpCode.Ret;

    static bool IsObjectEquals(MemberRef member)
        => member.Kind != MemberKind.Unsupported
            && member.Name == "Equals"
            && member.HasThis
            && FrameworkIdentity.IsCoreLibraryType(
                member.DeclaringType,
                "System",
                "Object")
            && member.ParameterTypes is [var parameter]
            && FrameworkIdentity.IsCoreLibraryType(
                parameter,
                "System",
                "Object")
            && FrameworkIdentity.IsCoreLibraryType(
                member.ReturnType,
                "System",
                "Boolean");

    // True only for the unmanaged primitive element types that C# stackalloc accepts.
    // Enums and unmanaged structs are also stackalloc-eligible but require resolving the
    // type's layout/base, so they are conservatively excluded (kept as small-array).
    static bool IsStackallocEligibleElement(TypeRef element)
        => element.Kind == TypeRefKind.Definition
           && element.Namespace == "System"
           && element.Name is "Boolean" or "Byte" or "SByte" or "Char"
               or "Int16" or "UInt16" or "Int32" or "UInt32"
               or "Int64" or "UInt64" or "Single" or "Double"
               or "IntPtr" or "UIntPtr";

    static bool IsBitConverterGetBytes(MemberRef member)
        => member.Kind != MemberKind.Unsupported
            && FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "BitConverter")
            && member.Name == "GetBytes";

    static bool IsConcurrentDictionaryGetOrAdd(MemberRef member)
        => member.Kind != MemberKind.Unsupported
            && member.Name == "GetOrAdd"
            && FrameworkIdentity.IsKnownFrameworkType(
                member.DeclaringType,
                "System.Collections.Concurrent",
                "System.Collections.Concurrent",
                "ConcurrentDictionary`2");

    // A `ToArray()` call that copies a span into a freshly allocated array. ReadOnlySpan<T>
    // and Span<T> are single-argument corelib generic value types, so the receiver is a
    // GenericInstance over the corelib definition; requiring that exact identity (assembly,
    // namespace, arity) avoids matching a user type that happens to be named System.Span
    // with its own ToArray. The definition name carries arity (e.g. "ReadOnlySpan`1"), so
    // compare on the name before the backtick.
    //
    // Scoped to spans deliberately: ReadOnlySpan<T>/Span<T> exist to avoid allocation, so
    // materializing one back into an array is a high-signal, low-volume copy. List<T>.
    // ToArray() is far more common and usually a legitimate snapshot, so promoting it
    // without escape/usage analysis would flood the section — left to a follow-up.
    static bool IsSpanToArrayCopy(MemberRef member, out string receiver)
    {
        receiver = "";
        if (member.Kind == MemberKind.Unsupported || member.Name != "ToArray")
            return false;
        var declaring = member.DeclaringType;
        if (declaring.Kind != TypeRefKind.GenericInstance || declaring.TypeArguments.Length != 1)
            return false;
        var definition = declaring.ElementType;
        if (definition is null
            || !definition.TrustedFrameworkAssembly
            || definition.Assembly != TypeRef.CoreLibrary
            || definition.Namespace != "System")
            return false;
        var name = StripGenericArity(definition.Name);
        if (name is not ("ReadOnlySpan" or "Span"))
            return false;
        receiver = $"System.{name}<T>::ToArray";
        return true;
    }

    static string StripGenericArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

}
