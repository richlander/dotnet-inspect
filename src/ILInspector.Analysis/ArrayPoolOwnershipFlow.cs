using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>The proven effect of one use of an ArrayPool-owned array.</summary>
public enum ArrayPoolOwnershipUseKind
{
    ReturnedToPool,
    Stored,
    ReturnedToCaller,
    Forwarded,
}

/// <summary>
/// One body-local ownership effect. A forwarded effect retains the physical
/// call occurrence and the callee parameter that receives the array.
/// </summary>
public sealed record ArrayPoolOwnershipUse(
    ArrayPoolOwnershipUseKind Kind,
    int ILOffset,
    DirectCall? Call = null,
    int CalleeParameterIndex = -1)
{
    public bool IsForwarded =>
        Kind == ArrayPoolOwnershipUseKind.Forwarded;
}

/// <summary>Ownership effects rooted at one ArrayPool&lt;T&gt;.Shared.Rent.</summary>
public sealed record ArrayPoolRentOwnership(
    int RentOffset,
    ImmutableArray<ArrayPoolOwnershipUse> Uses,
    bool IsComplete);

/// <summary>Ownership effects rooted at one array parameter.</summary>
public sealed record ArrayPoolParameterOwnership(
    int ParameterIndex,
    ImmutableArray<ArrayPoolOwnershipUse> Uses,
    bool IsComplete);

/// <summary>
/// Compact body evidence retained for interprocedural ownership composition.
/// It contains no IL or control-flow graph state.
/// </summary>
public sealed record ArrayPoolOwnershipMethodEvidence(
    MethodIdentity Method,
    MemberRef Member,
    ImmutableArray<ArrayPoolRentOwnership> Rents,
    ImmutableArray<ArrayPoolParameterOwnership> Parameters,
    bool IsComplete);

static class ArrayPoolOwnershipFlow
{
    internal static ArrayPoolOwnershipMethodEvidence Analyze(
        MethodBodyAnalysisContext context,
        ImmutableArray<DirectCall> directCalls)
    {
        MethodIdentity method = context.Method;
        MemberRef member = CallTreeMember.FromDefinition(method);
        bool hasArrayParameter =
            method.ParameterTypes.Any(static parameter =>
                parameter.Kind == TypeRefKind.SzArray);
        bool hasRent = directCalls.Any(static call =>
            IsDirectInvocation(call)
            && LeakTriageAnalyzer.IsArrayPoolRent(call.Callee));
        if (!hasArrayParameter && !hasRent)
            return new(method, member, [], [], IsComplete: true);

        if (!context.Blocks.IsComplete)
            return new(method, member, [], [], IsComplete: false);

        ReachingDefinitionsResult reaching =
            ReachingDefinitions.Analyze(
                context.Instructions,
                method.ParameterTypes.Length
                    + (method.IsStatic ? 0 : 1));
        if (!reaching.IsComplete)
            return new(method, member, [], [], IsComplete: false);

        IReadOnlyDictionary<int, DirectCall> calls =
            directCalls
                .Where(IsDirectInvocation)
                .ToDictionary(
                    static call => call.ILOffset);
        IReadOnlyDictionary<int, MemberRef> members =
            calls.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Callee);
        var ignoredCandidates =
            ImmutableArray.CreateBuilder<LeakTriageCandidate>();
        ImmutableArray<LeakTriageAnalyzer.RentedLocal> rents =
        [
            .. LeakTriageAnalyzer.FindRents(
                method,
                context.Instructions.Instructions,
                context.Blocks,
                reaching,
                members,
                ignoredCandidates),
        ];

        ImmutableArray<ArrayPoolRentOwnership> rentEvidence =
        [
            .. rents.Select(rent =>
                AnalyzeDefinition(
                    rent.RentOffset,
                    rent.Definition,
                    rent.Slot,
                    isArgument: false,
                    context,
                    reaching,
                    calls,
                    members)),
        ];

        var parameters =
            ImmutableArray.CreateBuilder<ArrayPoolParameterOwnership>();
        for (int parameterIndex = 0;
            parameterIndex < method.ParameterTypes.Length;
            parameterIndex++)
        {
            if (method.ParameterTypes[parameterIndex].Kind
                != TypeRefKind.SzArray)
            {
                continue;
            }

            int slot = parameterIndex + (method.IsStatic ? 0 : 1);
            LocalDefinition? definition =
                reaching.Definitions.SingleOrDefault(candidate =>
                    candidate.IsArgument
                    && candidate.Slot == slot
                    && candidate.Offset == -1);
            if (definition is null)
            {
                parameters.Add(
                    new ArrayPoolParameterOwnership(
                        parameterIndex,
                        [],
                        IsComplete: false));
                continue;
            }

            ArrayPoolRentOwnership flow = AnalyzeDefinition(
                rentOffset: -1,
                definition,
                slot,
                isArgument: true,
                context,
                reaching,
                calls,
                members);
            parameters.Add(
                new ArrayPoolParameterOwnership(
                    parameterIndex,
                    flow.Uses,
                    flow.IsComplete));
        }

        return new(
            method,
            member,
            rentEvidence,
            parameters.ToImmutable(),
            IsComplete: ignoredCandidates.Count == 0);
    }

    static bool IsDirectInvocation(DirectCall call) =>
        call.Kind is CallKind.Call
            or CallKind.CallVirtual
            or CallKind.NewObject;

    static ArrayPoolRentOwnership AnalyzeDefinition(
        int rentOffset,
        LocalDefinition definition,
        int slot,
        bool isArgument,
        MethodBodyAnalysisContext context,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, DirectCall> calls,
        IReadOnlyDictionary<int, MemberRef> members)
    {
        var uses = ImmutableArray.CreateBuilder<ArrayPoolOwnershipUse>();
        bool complete = true;
        foreach (LocalUse use in reaching.UsesOf(definition))
        {
            if (use.Address)
            {
                complete = false;
                continue;
            }

            LeakTriageAnalyzer.UseClassification classification =
                LeakTriageAnalyzer.ClassifyUse(
                    context.Instructions.Instructions,
                    members,
                    use.Offset,
                    slot,
                    isArgument: isArgument);
            switch (classification.Kind)
            {
                case LeakTriageAnalyzer.UseKind.Release:
                    uses.Add(
                        new(
                            ArrayPoolOwnershipUseKind.ReturnedToPool,
                            classification.OperationOffset));
                    break;
                case LeakTriageAnalyzer.UseKind.Store:
                    uses.Add(
                        new(
                            ArrayPoolOwnershipUseKind.Stored,
                            classification.OperationOffset));
                    break;
                case LeakTriageAnalyzer.UseKind.Return:
                    uses.Add(
                        new(
                            ArrayPoolOwnershipUseKind.ReturnedToCaller,
                            classification.OperationOffset));
                    break;
                case LeakTriageAnalyzer.UseKind.Forward:
                    if (calls.TryGetValue(
                            classification.OperationOffset,
                            out DirectCall? call)
                        && classification.ParameterIndex >= 0)
                    {
                        uses.Add(
                            new(
                                ArrayPoolOwnershipUseKind.Forwarded,
                                classification.OperationOffset,
                                call,
                                classification.ParameterIndex));
                    }
                    else
                    {
                        complete = false;
                    }
                    break;
                case LeakTriageAnalyzer.UseKind.LocalUse:
                    break;
                default:
                    complete = false;
                    break;
            }
        }

        return new(
            rentOffset,
            uses
                .OrderBy(static use => use.ILOffset)
                .ThenBy(static use => use.Kind)
                .ToImmutableArray(),
            complete);
    }
}
