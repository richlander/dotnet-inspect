using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis.Planning;
using ILInspector.Instructions;

namespace ILInspector.Analysis.Tests;

/// <summary>One physical call site: the body, IL offset, and operand token.</summary>
internal readonly record struct CallSiteKey(
    int EvidenceMethodToken,
    int ILOffset,
    int OperandToken);

/// <summary>
/// Resolves every call site of every visited body through declared
/// call-target support, so the source's targets can be compared with Library
/// Body Analysis's <see cref="LibraryCallGraphAnalysisResult.ResolveTarget"/>.
/// </summary>
internal sealed class CallTargetProbe
    : MethodDefinitionProducer<
        ImmutableArray<KeyValuePair<CallSiteKey, MethodCallTarget>>,
        ImmutableDictionary<CallSiteKey, MethodCallTarget>.Builder,
        ImmutableDictionary<CallSiteKey, MethodCallTarget>>
{
    CallTargetProbe(string identity)
        : base(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body | MethodDefinitionLayers.CallTargets)
    {
    }

    public static CallTargetProbe Instance { get; } = new("CallTargetProbe");

    public static CallTargetProbe Second { get; } = new("SecondCallTargetProbe");

    internal override ImmutableArray<KeyValuePair<CallSiteKey, MethodCallTarget>> Visit(
        scoped MethodDefinitionView view)
    {
        if (!view.HasManagedBody)
            return [];

        int caller = MetadataTokens.GetToken(view.MethodHandle);
        var sites =
            ImmutableArray.CreateBuilder<KeyValuePair<CallSiteKey, MethodCallTarget>>();
        foreach (DecodedInstruction instruction
            in InstructionDecoder.Decode(view.GetBody().GetILBytes()!))
        {
            if (Kind(instruction.OpCode) is not { } kind)
                continue;
            int operand = checked((int)instruction.OperandValue);
            sites.Add(new(
                new CallSiteKey(caller, instruction.Offset, operand),
                view.ResolveCallTarget(kind, operand)));
        }
        return sites.ToImmutable();
    }

    static CallKind? Kind(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Call => CallKind.Call,
        ILOpCode.Callvirt => CallKind.CallVirtual,
        ILOpCode.Newobj => CallKind.NewObject,
        ILOpCode.Ldftn => CallKind.LoadFunction,
        ILOpCode.Ldvirtftn => CallKind.LoadVirtualFunction,
        ILOpCode.Calli => CallKind.CallIndirect,
        _ => null,
    };

    internal override ImmutableDictionary<CallSiteKey, MethodCallTarget>.Builder Seed() =>
        ImmutableDictionary.CreateBuilder<CallSiteKey, MethodCallTarget>();

    internal override ImmutableDictionary<CallSiteKey, MethodCallTarget>.Builder Accumulate(
        ImmutableDictionary<CallSiteKey, MethodCallTarget>.Builder accumulator,
        ImmutableArray<KeyValuePair<CallSiteKey, MethodCallTarget>> fact)
    {
        accumulator.AddRange(fact);
        return accumulator;
    }

    internal override ImmutableDictionary<CallSiteKey, MethodCallTarget> Complete(
        ImmutableDictionary<CallSiteKey, MethodCallTarget>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator.ToImmutable();

    /// <summary>The Library Body Analysis target of a call, projected to the source's shape.</summary>
    public static MethodCallTarget Expected(
        LibraryCallGraphAnalysisResult graph,
        DirectCall call) =>
        graph.ResolveTarget(call) switch
        {
            DirectCallTarget.CurrentModule current =>
                new MethodCallTarget.CurrentModule(
                    MetadataTokens.MethodDefinitionHandle(
                        current.Method.MetadataToken & 0x00FFFFFF)),
            DirectCallTarget.External external =>
                new MethodCallTarget.External(external.Origin),
            DirectCallTarget.RuntimeProvided =>
                new MethodCallTarget.RuntimeProvided(),
            DirectCallTarget.Unresolved unresolved =>
                new MethodCallTarget.Unresolved(unresolved.Reason),
            var other => throw new InvalidOperationException(
                $"Unexpected target {other}."),
        };

    public static CallSiteKey Key(DirectCall call) =>
        new(call.EvidenceMethod.MetadataToken, call.ILOffset, call.OperandToken);
}
