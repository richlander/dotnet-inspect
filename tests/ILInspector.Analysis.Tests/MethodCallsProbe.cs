using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Analysis.Planning;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

internal sealed class MethodCallOperandsProbe
    : MethodDefinitionProducer<
        ImmutableArray<KeyValuePair<CallSiteKey, MethodCallOperandSite>>,
        ImmutableDictionary<CallSiteKey, MethodCallOperandSite>.Builder,
        ImmutableDictionary<CallSiteKey, MethodCallOperandSite>>
{
    MethodCallOperandsProbe(string identity)
        : base(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.CallOperands)
    {
    }

    public static MethodCallOperandsProbe Instance { get; } =
        new("MethodCallOperandsProbe");

    public static MethodCallOperandsProbe Second { get; } =
        new("SecondMethodCallOperandsProbe");

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [MethodBodyAnalyzerDeclarations.ResolvedCalls];

    internal override ImmutableArray<
        KeyValuePair<CallSiteKey, MethodCallOperandSite>> Visit(
            scoped MethodDefinitionView view)
    {
        if (!view.HasManagedBody)
            return [];

        MethodInstructions instructions =
            view.MaterializeInstructions(out _);
        MethodCallOperandSites calls =
            view.GetCallOperands(instructions);
        int token = view.Token;
        return
        [
            .. calls.Sites.Select(
                call => new KeyValuePair<
                    CallSiteKey,
                    MethodCallOperandSite>(
                        new(
                            token,
                            call.ILOffset,
                            call.OperandToken),
                        call)),
        ];
    }

    internal override ImmutableDictionary<
        CallSiteKey,
        MethodCallOperandSite>.Builder Seed() =>
        ImmutableDictionary.CreateBuilder<
            CallSiteKey,
            MethodCallOperandSite>();

    internal override ImmutableDictionary<
        CallSiteKey,
        MethodCallOperandSite>.Builder Accumulate(
            ImmutableDictionary<
                CallSiteKey,
                MethodCallOperandSite>.Builder accumulator,
            ImmutableArray<
                KeyValuePair<
                    CallSiteKey,
                    MethodCallOperandSite>> fact)
    {
        accumulator.AddRange(fact);
        return accumulator;
    }

    internal override ImmutableDictionary<
        CallSiteKey,
        MethodCallOperandSite> Complete(
            ImmutableDictionary<
                CallSiteKey,
                MethodCallOperandSite>.Builder accumulator,
            MethodDefinitionCompletionView completion) =>
        accumulator.ToImmutable();
}

internal sealed class MethodCallsProbe
    : MethodDefinitionProducer<
        ImmutableArray<KeyValuePair<CallSiteKey, MethodCallSite>>,
        ImmutableDictionary<CallSiteKey, MethodCallSite>.Builder,
        ImmutableDictionary<CallSiteKey, MethodCallSite>>
{
    readonly bool _sharesCalleeIdentities;

    MethodCallsProbe(string identity, bool sharesCalleeIdentities = true)
        : base(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Calls)
    {
        _sharesCalleeIdentities = sharesCalleeIdentities;
    }

    public static MethodCallsProbe Instance { get; } =
        new("MethodCallsProbe");

    public static MethodCallsProbe Second { get; } =
        new("SecondMethodCallsProbe");

    public static MethodCallsProbe Unshared { get; } =
        new("UnsharedMethodCallsProbe", sharesCalleeIdentities: false);

    internal override bool SharesCalleeIdentities => _sharesCalleeIdentities;

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [MethodBodyAnalyzerDeclarations.ResolvedCalls];

    internal override ImmutableArray<KeyValuePair<CallSiteKey, MethodCallSite>>
        Visit(scoped MethodDefinitionView view)
    {
        if (!view.HasManagedBody)
            return [];

        MethodInstructions instructions =
            view.MaterializeInstructions(out MethodBodyData _);
        MethodCallSites calls = view.GetCalls(instructions);
        int token = view.Token;
        return
        [
            .. calls.Sites.Select(
                call => new KeyValuePair<CallSiteKey, MethodCallSite>(
                    new(
                        token,
                        call.ILOffset,
                        call.OperandToken),
                    call)),
        ];
    }

    internal override ImmutableDictionary<CallSiteKey, MethodCallSite>.Builder
        Seed() =>
        ImmutableDictionary.CreateBuilder<CallSiteKey, MethodCallSite>();

    internal override ImmutableDictionary<CallSiteKey, MethodCallSite>.Builder
        Accumulate(
            ImmutableDictionary<CallSiteKey, MethodCallSite>.Builder accumulator,
            ImmutableArray<KeyValuePair<CallSiteKey, MethodCallSite>> fact)
    {
        accumulator.AddRange(fact);
        return accumulator;
    }

    internal override ImmutableDictionary<CallSiteKey, MethodCallSite>
        Complete(
            ImmutableDictionary<CallSiteKey, MethodCallSite>.Builder accumulator,
            MethodDefinitionCompletionView completion) =>
        accumulator.ToImmutable();
}

internal sealed record MethodCallMetadataProbeFact(
    int MethodToken,
    MethodCallSites Calls,
    MethodLocalSignature Locals,
    ImmutableArray<MethodFieldOperandProbe> Fields);

internal sealed record MethodFieldOperandProbe(
    int OperandToken,
    MethodFieldOperand Operand);

internal sealed class MethodCallMetadataProbe
    : MethodDefinitionProducer<
        MethodCallMetadataProbeFact?,
        ImmutableArray<MethodCallMetadataProbeFact>.Builder,
        ImmutableArray<MethodCallMetadataProbeFact>>
{
    MethodCallMetadataProbe(string identity)
        : base(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Calls)
    {
    }

    public static MethodCallMetadataProbe Instance { get; } =
        new("MethodCallMetadataProbe");

    public static MethodCallMetadataProbe Second { get; } =
        new("SecondMethodCallMetadataProbe");

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [MethodBodyAnalyzerDeclarations.ResolvedCalls];

    internal override MethodCallMetadataProbeFact? Visit(
        scoped MethodDefinitionView view)
    {
        if (!view.HasManagedBody)
            return null;

        MethodInstructions instructions =
            view.MaterializeInstructions(out MethodBodyData _);
        var fields = ImmutableArray.CreateBuilder<MethodFieldOperandProbe>();
        foreach (DecodedInstruction instruction
            in instructions.Instructions)
        {
            if (instruction.OpCode is ILOpCode.Ldfld
                or ILOpCode.Ldflda
                or ILOpCode.Ldsfld
                or ILOpCode.Ldsflda
                or ILOpCode.Stfld
                or ILOpCode.Stsfld)
            {
                int operandToken =
                    MethodInstructionFacts.OperandInt32(instruction);
                fields.Add(
                    new(
                        operandToken,
                        view.ResolveFieldOperand(operandToken)));
            }
        }

        return new(
            view.Token,
            view.GetCalls(instructions),
            view.GetLocalSignature(),
            fields.ToImmutable());
    }

    internal override ImmutableArray<MethodCallMetadataProbeFact>.Builder
        Seed() =>
        ImmutableArray.CreateBuilder<MethodCallMetadataProbeFact>();

    internal override ImmutableArray<MethodCallMetadataProbeFact>.Builder
        Accumulate(
            ImmutableArray<MethodCallMetadataProbeFact>.Builder accumulator,
            MethodCallMetadataProbeFact? fact)
    {
        if (fact is not null)
            accumulator.Add(fact);
        return accumulator;
    }

    internal override ImmutableArray<MethodCallMetadataProbeFact> Complete(
        ImmutableArray<MethodCallMetadataProbeFact>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator.ToImmutable();
}

internal sealed class UndeclaredMethodCallsProbe
    : MethodDefinitionProducer<int, int, int>
{
    UndeclaredMethodCallsProbe()
        : base(
            "UndeclaredMethodCallsProbe",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body)
    {
    }

    public static UndeclaredMethodCallsProbe Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [MethodBodyAnalyzerDeclarations.ResolvedCalls];

    internal override int Visit(scoped MethodDefinitionView view)
    {
        if (!view.HasManagedBody)
            return 0;

        MethodInstructions instructions =
            view.MaterializeInstructions(out MethodBodyData _);
        return view.GetCalls(instructions).Sites.Length;
    }

    internal override int Seed() => 0;

    internal override int Accumulate(int accumulator, int fact) =>
        accumulator + fact;

    internal override int Complete(
        int accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator;
}

internal sealed class RepeatedMethodCallMetadataProbe
    : MethodDefinitionProducer<int, int, int>
{
    RepeatedMethodCallMetadataProbe()
        : base(
            "RepeatedMethodCallMetadataProbe",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Calls)
    {
    }

    public static RepeatedMethodCallMetadataProbe Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [MethodBodyAnalyzerDeclarations.ResolvedCalls];

    internal override int Visit(scoped MethodDefinitionView view)
    {
        MethodInstructions instructions =
            view.MaterializeInstructions(out MethodBodyData _);
        _ = view.GetCalls(instructions);
        _ = view.GetCalls(instructions);
        _ = view.GetLocalSignature();
        _ = view.GetLocalSignature();
        return 1;
    }

    internal override int Seed() => 0;

    internal override int Accumulate(int accumulator, int fact) =>
        accumulator + fact;

    internal override int Complete(
        int accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator;
}
