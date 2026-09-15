using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// Layer 0 of the substrate: one decode + one EH-aware block builder over a method body, plus
/// offset-keyed lookup. Reader-free and cheap — the recall-net surface that broad scans and the
/// offset join (Research, <c>--il-offset</c>) share, and the de-dup target for the IL-level block
/// finders. A Metadata-issued <see cref="MethodBodyData"/> additionally produces correlated
/// exception-flow facts. The typed evaluation stack (Layer 1) is <b>opt-in</b> via
/// <see cref="InterpretStack(bool, IStackTypeResolver?)"/>, so it is never paid for unless a
/// consumer escalates to it. SRM-only; no IrNode, structuring, C#, Roslyn, or assembly loading.
/// </summary>
public sealed class MethodInstructions
{
    public MethodInstructions(
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph blocks)
        : this(
            instructions,
            blocks,
            new InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable(
                    InstructionExceptionFlowUnavailableReason.MissingMetadataEvidence,
                    "This decode did not consume a Metadata-issued method-body observation."))
    {
    }

    internal MethodInstructions(
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph blocks,
        InstructionExceptionFlowResult<InstructionExceptionFlowFacts> exceptionFlow)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(exceptionFlow);
        Instructions = instructions.IsDefault ? [] : instructions;
        Blocks = blocks;
        ExceptionFlow = exceptionFlow;
    }

    public ImmutableArray<DecodedInstruction> Instructions { get; }
    public BlockGraph Blocks { get; }

    public InstructionExceptionFlowResult<InstructionExceptionFlowFacts>
        ExceptionFlow
    { get; }

    /// <summary>True when decode + blocks completed (Layer 0). Does not imply a typed stack was computed.</summary>
    public bool IsComplete => Blocks.IsComplete;

    /// <summary>Layer 0: decode + EH-aware blocks from raw IL and exception regions. Fail-closed on malformed IL.</summary>
    public static MethodInstructions Decode(byte[] il, int ilLength, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        ArgumentNullException.ThrowIfNull(il);
        try
        {
            if ((uint)ilLength > (uint)il.Length)
                throw new BadImageFormatException(
                    $"Declared IL length {ilLength} exceeds the {il.Length}-byte buffer.");
            var instructions = InstructionDecoder.Decode(ilLength == il.Length ? il : il[..ilLength]);
            var blocks = BlockGraph.Build(ilLength, instructions, exceptionRegions);
            return new MethodInstructions(instructions, blocks);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidProgramException)
        {
            // Fail closed: malformed IL produces an incomplete Layer 0 with a reason, never a throw.
            return new MethodInstructions(
                [],
                new BlockGraph([], [], IsComplete: false, ex.Message),
                new InstructionExceptionFlowResult<
                    InstructionExceptionFlowFacts>.Unavailable(
                        InstructionExceptionFlowUnavailableReason.DecodeFailure,
                        ex.Message));
        }
    }

    /// <summary>Layer 0 from a method body.</summary>
    public static MethodInstructions Decode(MethodBodyBlock body)
    {
        ArgumentNullException.ThrowIfNull(body);
        byte[] il = body.GetILBytes() ?? [];
        return Decode(il, il.Length, body.ExceptionRegions);
    }

    /// <summary>
    /// Layer 0 plus correlated exception-flow facts from a reader-independent
    /// Metadata body observation.
    /// </summary>
    public static MethodInstructions Decode(MethodBodyData body)
    {
        ArgumentNullException.ThrowIfNull(body);
        byte[] il = body.IL.ToArray();
        try
        {
            ImmutableArray<DecodedInstruction> instructions =
                InstructionDecoder.Decode(il);
            ExceptionFlowTopology topology =
                ExceptionFlowTopology.Create(il.Length, instructions, body);
            BlockGraph blocks = BlockGraph.Build(
                il.Length,
                instructions,
                topology);
            InstructionExceptionFlowResult<InstructionExceptionFlowFacts> flow =
                topology.IsComplete && blocks.IsComplete
                    ? new InstructionExceptionFlowResult<
                        InstructionExceptionFlowFacts>.Available(
                            new InstructionExceptionFlowFacts(
                                body.EvidenceId,
                                topology.IssuedClauses,
                                topology.IssuedRegions,
                                instructions,
                                blocks,
                                topology))
                    : new InstructionExceptionFlowResult<
                        InstructionExceptionFlowFacts>.Unavailable(
                            topology.UnavailableReason
                                ?? InstructionExceptionFlowUnavailableReason.DecodeFailure,
                            topology.IncompleteReason
                                ?? blocks.IncompleteReason
                                ?? "Exception-flow construction was incomplete.");
            return new MethodInstructions(instructions, blocks, flow);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidProgramException)
        {
            return new MethodInstructions(
                [],
                new BlockGraph([], [], IsComplete: false, ex.Message),
                new InstructionExceptionFlowResult<
                    InstructionExceptionFlowFacts>.Unavailable(
                        InstructionExceptionFlowUnavailableReason.DecodeFailure,
                        ex.Message));
        }
    }

    /// <summary>
    /// The instruction beginning exactly at <paramref name="offset"/>, or null if the offset is not an
    /// instruction boundary (the validation <c>--il-offset</c> wants before resolving a token/offset).
    /// </summary>
    public DecodedInstruction? InstructionAt(int offset)
    {
        int lo = 0;
        int hi = Instructions.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int start = Instructions[mid].Offset;
            if (start == offset)
                return Instructions[mid];
            if (start < offset)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return null;
    }

    /// <summary>
    /// The instruction whose <see cref="DecodedInstruction.NextOffset"/> equals
    /// <paramref name="offset"/>, or null when <paramref name="offset"/> is not the
    /// return address/fallthrough boundary after an instruction.
    /// </summary>
    public DecodedInstruction? InstructionBefore(int offset)
    {
        int lo = 0;
        int hi = Instructions.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int next = Instructions[mid].NextOffset;
            if (next == offset)
                return Instructions[mid];
            if (next < offset)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return null;
    }

    /// <summary>
    /// The index of the first instruction beginning at or after
    /// <paramref name="offset"/>, or <see cref="ImmutableArray{T}.Length"/> when
    /// no instruction follows it.
    /// </summary>
    public int InstructionIndexAtOrAfter(int offset)
    {
        int lo = 0;
        int hi = Instructions.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (Instructions[mid].Offset < offset)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>The index of the block containing <paramref name="offset"/>, or -1 if out of range.</summary>
    public int BlockIndexAt(int offset) => Blocks.BlockIndexAt(offset);

    /// <summary>Layer 1 (opt-in): the typed evaluation stack with provenance over this body.</summary>
    public TypedStackResult InterpretStack(bool methodReturnsValue, IStackTypeResolver? resolver = null)
        => StackTypeInterpreter.Interpret(Instructions, Blocks, methodReturnsValue, resolver);

    /// <summary>Layer 1 convenience: typed stack wired to an SRM-backed resolver for <paramref name="method"/>.</summary>
    public TypedStackResult InterpretStack(MetadataReader reader, MethodDefinitionHandle method, MethodBodyBlock body)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            var resolver = MetadataStackTypeResolver.Create(reader, method, body);
            return InterpretStack(resolver.MethodReturnsValue, resolver);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            // Fail closed: a malformed method/local signature yields an incomplete typed stack, never a throw.
            return new TypedStackResult(
                Instructions, Blocks,
                ImmutableDictionary<int, ImmutableArray<StackValue>>.Empty,
                false,
                $"metadata signature decode failed: {ex.Message}");
        }
    }
}
