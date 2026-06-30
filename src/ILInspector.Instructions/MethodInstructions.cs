using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// Layer 0 of the substrate: one decode + one EH-aware block builder over a method body, plus
/// offset-keyed lookup. Metadata-free and cheap — the recall-net surface that broad scans and
/// the offset join (Research, <c>--il-offset</c>) share, and the de-dup target for the IL-level
/// block finders. The typed evaluation stack (Layer 1) is <b>opt-in</b> via
/// <see cref="InterpretStack(bool, IStackTypeResolver?)"/>, so it is never paid for unless a
/// consumer escalates to it. SRM-only; no IrNode, structuring, C#, Roslyn, or assembly loading.
/// </summary>
public sealed record MethodInstructions(
    ImmutableArray<DecodedInstruction> Instructions,
    BlockGraph Blocks)
{
    /// <summary>True when decode + blocks completed (Layer 0). Does not imply a typed stack was computed.</summary>
    public bool IsComplete => Blocks.IsComplete;

    /// <summary>Layer 0: decode + EH-aware blocks from raw IL and exception regions. Fail-closed on malformed IL.</summary>
    public static MethodInstructions Decode(byte[] il, int ilLength, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        ArgumentNullException.ThrowIfNull(il);
        try
        {
            var instructions = InstructionDecoder.Decode(il);
            var blocks = BlockGraph.Build(ilLength, instructions, exceptionRegions);
            return new MethodInstructions(instructions, blocks);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidProgramException)
        {
            // Fail closed: malformed IL produces an incomplete Layer 0 with a reason, never a throw.
            return new MethodInstructions([], new BlockGraph([], [], IsComplete: false, ex.Message));
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
        var resolver = MetadataStackTypeResolver.Create(reader, method, body);
        return InterpretStack(resolver.MethodReturnsValue, resolver);
    }
}
