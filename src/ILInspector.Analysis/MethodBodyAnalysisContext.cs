using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>
/// Shared per-method inputs produced once by the assembly analysis pipeline.
/// Topic-specific producers build their own interpretation over the common
/// Layer-0 instructions and blocks.
/// </summary>
internal sealed record MethodBodyAnalysisContext(
    MethodIdentity Method,
    MethodInstructions Instructions,
    ImmutableArray<ExceptionRegion> ExceptionRegions,
    IReadOnlyList<(int Start, int End)> LoopRegions,
    ImmutableArray<TypeRef> LocalTypes)
{
    ImmutableArray<TypeRef> _localTypes =
        LocalTypes.IsDefault ? [] : LocalTypes;

    public ImmutableArray<TypeRef> LocalTypes
    {
        get => _localTypes;
        init => _localTypes = value.IsDefault ? [] : value;
    }

    /// <summary>The shared Layer-0 block graph for this body.</summary>
    public BlockGraph Blocks => Instructions.Blocks;

    /// <summary>
    /// The instruction beginning exactly at <paramref name="offset"/>, or null
    /// when the offset is not an instruction boundary.
    /// </summary>
    public DecodedInstruction? InstructionAt(int offset)
        => Instructions.InstructionAt(offset);

    /// <summary>
    /// The index of the first instruction beginning at or after
    /// <paramref name="offset"/>.
    /// </summary>
    public int IndexAtOrAfter(int offset)
        => Instructions.InstructionIndexAtOrAfter(offset);

    /// <summary>
    /// The index of the first non-<c>nop</c> instruction beginning at or after
    /// <paramref name="offset"/>. Debug IL interleaves <c>nop</c>s that carry no
    /// stack effect, so shape recognizers step over them.
    /// </summary>
    public int NextNonNopIndexAtOrAfter(int offset)
    {
        var instructions = Instructions.Instructions;
        int index = IndexAtOrAfter(offset);
        while (index < instructions.Length
            && instructions[index].OpCode == ILOpCode.Nop)
        {
            index++;
        }
        return index;
    }

    /// <summary>
    /// True when the offset lies inside one of this body's loop regions. A
    /// neutral region-membership query; whether that makes an occurrence hot is
    /// a topic producer's interpretation.
    /// </summary>
    public bool IsInLoopRegion(int offset)
    {
        foreach (var region in LoopRegions)
        {
            if (offset >= region.Start && offset <= region.End)
                return true;
        }
        return false;
    }
}
