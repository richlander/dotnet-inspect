using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>
/// Objective structural measurements for one physical method body, paired
/// with its logical declared-source owner.
/// The default order is a body-size baseline, not a universal complexity score.
/// </summary>
public sealed record MethodImplementationProfile(
    MethodIdentity Method,
    MethodIdentity EvidenceMethod,
    int ILBytes,
    int InstructionCount,
    int DistinctOpcodeCount,
    int BasicBlockCount,
    int BranchCount,
    int ConditionalBranchCount,
    int SwitchCount,
    int SwitchTargetCount,
    int LoopCount,
    int CatchCount,
    int FilterCount,
    int FinallyCount,
    int FaultCount,
    int LocalCount,
    int DirectCallCount,
    int DistinctCalleeCount,
    int AllocationCount,
    int ThrowCount,
    bool Async,
    bool Unsafe,
    int ReflectionCallCount,
    int IncomingOverloadCallerCount,
    int OutgoingOverloadTargetCount,
    bool IsComplete,
    ImmutableArray<string> IncompleteReasons)
{
    /// <summary>
    /// Cyclomatic complexity of the ordinary-flow IL graph.
    /// </summary>
    /// <remarks>
    /// This is a compiled-implementation measure, not a source-level C# metric. The graph has
    /// one entry and one virtual exit; conditional branch opcodes contribute one additional
    /// edge, except <c>switch</c>, whose target count supplies the additional edges. Exception
    /// dispatch and cleanup edges are deliberately excluded and remain available through the
    /// exception counts. Interpret the value as incomplete when <see cref="IsComplete"/> is
    /// <see langword="false"/>.
    /// </remarks>
    public int NormalFlowCyclomaticComplexity =>
        1 + ConditionalBranchCount - SwitchCount + SwitchTargetCount;
}

/// <summary>An exact direct call between distinct methods in one overload family.</summary>
public sealed record OverloadCallRelationship(
    MethodIdentity Caller,
    MethodIdentity Callee,
    MethodIdentity EvidenceMethod,
    int ILOffset,
    CallKind Kind);

internal sealed record MethodBodyImplementationMetrics(
    MethodIdentity Method,
    MethodIdentity EvidenceMethod,
    int ILBytes,
    int InstructionCount,
    int DistinctOpcodeCount,
    int BasicBlockCount,
    int BranchCount,
    int ConditionalBranchCount,
    int SwitchCount,
    int SwitchTargetCount,
    int LoopCount,
    int CatchCount,
    int FilterCount,
    int FinallyCount,
    int FaultCount,
    int LocalCount,
    bool IsAsync,
    ImmutableArray<string> IncompleteReasons);

internal readonly record struct MethodImplementationContextMeasurements(
    ImplementationMetricInstructionShape? InstructionShape,
    ImplementationMetricControlFlow? ControlFlow);

internal static class MethodImplementationProfileAnalysis
{
    internal static MethodImplementationContextMeasurements MeasureContext(
        MethodBodyAnalysisContext context,
        bool includeInstructionShape,
        bool includeControlFlow)
    {
        HashSet<ILOpCode>? distinctOpcodes =
            includeInstructionShape ? [] : null;
        int branches = 0;
        int conditionalBranches = 0;
        int switches = 0;
        int switchTargets = 0;
        foreach (DecodedInstruction instruction
            in context.Instructions.Instructions)
        {
            distinctOpcodes?.Add(instruction.OpCode);
            if (!includeControlFlow)
                continue;
            if (instruction.Branches)
            {
                branches++;
                if (!instruction.IsUnconditionalBranch)
                    conditionalBranches++;
            }
            if (instruction.OpCode == ILOpCode.Switch)
            {
                switches++;
                switchTargets += instruction.BranchTargets.Length;
            }
        }

        return new(
            includeInstructionShape
                ? new(
                    context.Instructions.Instructions.Length,
                    distinctOpcodes!.Count)
                : null,
            includeControlFlow
                ? new(
                    context.Blocks.Blocks.Length,
                    branches,
                    conditionalBranches,
                    switches,
                    switchTargets,
                    context.LoopRegions.Distinct().Count())
                : null);
    }

    internal static MethodBodyImplementationMetrics Measure(
        MethodBodyAnalysisContext context,
        MethodIdentity method,
        int ilBytes,
        bool isAsync,
        MethodImplementationContextMeasurements contextMeasurements)
    {
        ImplementationMetricInstructionShape instructionShape =
            contextMeasurements.InstructionShape
            ?? throw new ArgumentException(
                "Complete profiles require instruction-shape measurements.",
                nameof(contextMeasurements));
        ImplementationMetricControlFlow controlFlow =
            contextMeasurements.ControlFlow
            ?? throw new ArgumentException(
                "Complete profiles require control-flow measurements.",
                nameof(contextMeasurements));

        int catches = 0;
        int filters = 0;
        int finallys = 0;
        int faults = 0;
        foreach (var region in
            context.RequireExceptionCatalog().Clauses)
        {
            switch (region.Kind)
            {
                case ExceptionRegionKind.Catch:
                    catches++;
                    break;
                case ExceptionRegionKind.Filter:
                    filters++;
                    break;
                case ExceptionRegionKind.Finally:
                    finallys++;
                    break;
                case ExceptionRegionKind.Fault:
                    faults++;
                    break;
            }
        }

        return new MethodBodyImplementationMetrics(
            method,
            context.Method,
            ilBytes,
            instructionShape.InstructionCount,
            instructionShape.DistinctOpcodeCount,
            controlFlow.BasicBlockCount,
            controlFlow.BranchCount,
            controlFlow.ConditionalBranchCount,
            controlFlow.SwitchCount,
            controlFlow.SwitchTargetCount,
            controlFlow.LoopCount,
            catches,
            filters,
            finallys,
            faults,
            context.LocalCount,
            isAsync,
            IncompleteReasons(context));
    }

    static ImmutableArray<string> IncompleteReasons(
        MethodBodyAnalysisContext context)
    {
        var reasons = ImmutableArray.CreateBuilder<string>();
        if (!context.Instructions.IsComplete)
        {
            reasons.Add(
                context.Instructions.Blocks.IncompleteReason
                ?? "Method body analysis was incomplete.");
        }
        if (context.LocalTypesIncompleteReason is { } localReason)
            reasons.Add(localReason);
        return reasons.ToImmutable();
    }

    internal static ImmutableArray<MethodImplementationProfile> Collect(
        ImmutableArray<MethodBodyImplementationMetrics> bodies,
        ImmutableArray<DirectCall> directCalls,
        IReadOnlyDictionary<int, MethodSignals> signals,
        ImmutableArray<OverloadCallRelationship> relationships,
        MethodDefinitionMap methodMap)
    {
        var incomingCallers = relationships
            .GroupBy(static relationship => relationship.Callee.MetadataToken)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static relationship => relationship.Caller.MetadataToken)
                    .Distinct()
                    .Count());
        var outgoingTargets = relationships
            .GroupBy(static relationship => (
                relationship.Caller.MetadataToken,
                relationship.EvidenceMethod.MetadataToken))
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static relationship => relationship.Callee.MetadataToken)
                    .Distinct()
                    .Count());
        var callsByEvidenceMethod = directCalls
            .Where(static call => IsInvocation(call.Kind))
            .GroupBy(static call =>
                call.EvidenceMethod.MetadataToken)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray());

        return
        [
            .. bodies.Select(body =>
                {
                    MethodIdentity method = body.Method;
                    MethodSignals signal = signals.GetValueOrDefault(
                        body.EvidenceMethod.MetadataToken,
                        MethodSignals.None);
                    callsByEvidenceMethod.TryGetValue(
                        body.EvidenceMethod.MetadataToken,
                        out DirectCall[]? calls);
                    calls ??= [];

                    return new MethodImplementationProfile(
                        method,
                        body.EvidenceMethod,
                        body.ILBytes,
                        body.InstructionCount,
                        body.DistinctOpcodeCount,
                        body.BasicBlockCount,
                        body.BranchCount,
                        body.ConditionalBranchCount,
                        body.SwitchCount,
                        body.SwitchTargetCount,
                        body.LoopCount,
                        body.CatchCount,
                        body.FilterCount,
                        body.FinallyCount,
                        body.FaultCount,
                        body.LocalCount,
                        calls.Length,
                        CountDistinctCallees(
                            calls,
                            methodMap),
                        signal.Allocations,
                        signal.Throws,
                        body.IsAsync,
                        signal.Unsafe,
                        signal.Reflection,
                        body.EvidenceMethod.MetadataToken
                            == method.MetadataToken
                                ? incomingCallers.GetValueOrDefault(
                                    method.MetadataToken)
                                : 0,
                        outgoingTargets.GetValueOrDefault((
                            method.MetadataToken,
                            body.EvidenceMethod.MetadataToken)),
                        body.IncompleteReasons.IsEmpty,
                        body.IncompleteReasons);
                })
                .OrderByDescending(static profile => profile.InstructionCount)
                .ThenByDescending(static profile => profile.DistinctOpcodeCount)
                .ThenByDescending(static profile => profile.BasicBlockCount)
                .ThenBy(static profile => profile.Method.MetadataToken),
        ];
    }

    static int CountDistinctCallees(
        IEnumerable<DirectCall> calls,
        MethodDefinitionMap methodMap)
    {
        var definitions = new HashSet<int>();
        var unresolved = new HashSet<MemberRef>();
        foreach (DirectCall call in calls)
        {
            int targetToken = methodMap.Resolve(call);
            if (targetToken != 0)
            {
                definitions.Add(targetToken);
            }
            else
            {
                unresolved.Add(call.Callee);
            }
        }
        return definitions.Count + unresolved.Count;
    }

    internal static ImmutableArray<OverloadCallRelationship>
        CollectOverloadRelationships(
            ImmutableArray<MethodIdentity> declaredMethods,
            ImmutableArray<DirectCall> directCalls)
        => CollectOverloadRelationships(
            declaredMethods,
            directCalls,
            MethodDefinitionMap.Create(declaredMethods));

    internal static ImmutableArray<OverloadCallRelationship>
        CollectOverloadRelationships(
            ImmutableArray<MethodIdentity> declaredMethods,
            ImmutableArray<DirectCall> directCalls,
            MethodDefinitionMap methodMap)
    {
        var methodsByToken = declaredMethods.ToDictionary(
            static method => method.MetadataToken);
        var relationships =
            ImmutableArray.CreateBuilder<OverloadCallRelationship>();
        foreach (var call in directCalls)
        {
            if (!IsInvocation(call.Kind))
                continue;

            int calleeToken = methodMap.Resolve(call);
            if (calleeToken == 0
                || !methodsByToken.TryGetValue(
                    calleeToken,
                    out MethodIdentity? callee)
                || call.Caller.MetadataToken == callee.MetadataToken
                || call.Caller.Name != callee.Name
                || !SameDeclaringType(
                    call.Caller.DeclaringType,
                    callee.DeclaringType))
            {
                continue;
            }

            relationships.Add(new(
                call.Caller,
                callee,
                call.EvidenceMethod,
                call.ILOffset,
                call.Kind));
        }

        return
        [
            .. relationships
                .OrderBy(static relationship =>
                    relationship.Caller.MetadataToken)
                .ThenBy(static relationship => relationship.ILOffset)
                .ThenBy(static relationship =>
                    relationship.Callee.MetadataToken),
        ];
    }

    static bool IsInvocation(CallKind kind)
        => kind is CallKind.Call
            or CallKind.CallVirtual
            or CallKind.NewObject;

    static bool SameDeclaringType(TypeRef left, TypeRef right)
    {
        left = GenericMemberIdentity.OpenDeclaringType(left);
        right = GenericMemberIdentity.OpenDeclaringType(right);
        return left.Kind == right.Kind
            && left.Assembly == right.Assembly
            && left.Namespace == right.Namespace
            && left.Name == right.Name;
    }
}
