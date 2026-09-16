using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Shared per-method inputs produced once by the assembly analysis pipeline.
/// Topic-specific producers build their own interpretation over the common
/// Layer-0 instructions and blocks.
/// </summary>
internal sealed class MethodBodyAnalysisContext
{
    readonly MethodExceptionRegionCatalog? _exceptionCatalog;

    internal MethodBodyAnalysisContext(
        MethodIdentity method,
        MethodInstructions instructions,
        IReadOnlyList<(int Start, int End)> loopRegions,
        ImmutableArray<TypeRef> localTypes,
        MethodExceptionRegionCatalog? exceptionCatalog = null,
        int? localCount = null,
        string? localTypesIncompleteReason = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(loopRegions);

        Method = method;
        Instructions = instructions;
        LoopRegions = loopRegions;
        LocalTypes = localTypes.IsDefault ? [] : localTypes;
        LocalCount = localCount ?? LocalTypes.Length;
        LocalTypesIncompleteReason = localTypesIncompleteReason;
        _exceptionCatalog = exceptionCatalog;
    }

    public MethodIdentity Method { get; }
    public MethodInstructions Instructions { get; }
    public IReadOnlyList<(int Start, int End)> LoopRegions { get; }
    public ImmutableArray<TypeRef> LocalTypes { get; }
    public int LocalCount { get; }
    public string? LocalTypesIncompleteReason { get; }

    internal static MethodBodyAnalysisContext Create(
        MethodIdentity method,
        MethodBodyData body,
        ImmutableArray<TypeRef> localTypes,
        int? localCount = null,
        string? localTypesIncompleteReason = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(body);
        if (body.EvidenceId.Method.ModuleVersionId
                != method.ModuleVersionId
            || body.EvidenceId.Method.Token
                != method.MetadataToken)
        {
            throw new ArgumentException(
                "The Metadata body evidence does not identify the analyzed method.",
                nameof(body));
        }

        MethodInstructions instructions =
            MethodInstructions.Decode(body);
        if (!instructions.IsComplete)
        {
            throw new BadImageFormatException(
                instructions.Blocks.IncompleteReason
                ?? "Method instruction decoding was incomplete.");
        }

        return new MethodBodyAnalysisContext(
            method,
            instructions,
            CollectLoopRegions(instructions),
            localTypes,
            body.ExceptionRegionCatalog,
            localCount,
            localTypesIncompleteReason);
    }

    /// <summary>The shared Layer-0 block graph for this body.</summary>
    public BlockGraph Blocks => Instructions.Blocks;

    /// <summary>
    /// The Metadata-issued physical exception catalog for production analysis.
    /// Synthetic Layer-0 contexts do not manufacture correlated evidence.
    /// </summary>
    public MethodExceptionRegionCatalog RequireExceptionCatalog() =>
        _exceptionCatalog
        ?? throw new InvalidOperationException(
            "Physical exception-region evidence is unavailable for this analysis context.");

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

    static IReadOnlyList<(int Start, int End)> CollectLoopRegions(
        MethodInstructions body)
    {
        var regions = new List<(int Start, int End)>();
        BlockGraph blockGraph = body.Blocks;
        foreach (DecodedInstruction instruction in body.Instructions)
        {
            if (instruction.OpCode == ILOpCode.Switch)
                continue;
            int sourceBlock =
                blockGraph.BlockIndexAt(instruction.Offset);
            foreach (int target in instruction.BranchTargets)
            {
                if (target >= instruction.Offset)
                    continue;
                int targetBlock =
                    blockGraph.BlockIndexAt(target);
                if (sourceBlock >= 0
                    && targetBlock >= 0
                    && blockGraph.Blocks[sourceBlock]
                        .Edges.Successors.Contains(targetBlock))
                {
                    regions.Add(
                        (target, instruction.Offset));
                }
            }
        }
        return regions;
    }
}
