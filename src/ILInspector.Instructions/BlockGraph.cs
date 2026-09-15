using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.ControlFlow;

namespace ILInspector.Instructions;

/// <summary>The CLI exception-region kinds, mirrored from SRM so callers do not need a body block.</summary>
public enum HandlerKind
{
    Catch,
    Filter,
    Finally,
    Fault,
}

/// <summary>A normalized, instruction-boundary-aligned exception region.</summary>
public sealed record ExceptionRegionModel(
    HandlerKind Kind,
    int TryStart,
    int TryEnd,
    int HandlerStart,
    int HandlerEnd,
    int FilterStart,
    int FilterEnd)
{
    public bool ContainsTry(int offset) => offset >= TryStart && offset < TryEnd;
    public bool ContainsHandler(int offset) => offset >= HandlerStart && offset < HandlerEnd;
    public bool ContainsFilter(int offset) => Kind == HandlerKind.Filter && offset >= FilterStart && offset < FilterEnd;
    public bool ContainsAny(int offset) => ContainsTry(offset) || ContainsHandler(offset) || ContainsFilter(offset);
}

/// <summary>One basic block: its IL byte span and outgoing control-flow edges.</summary>
public sealed record InstructionBlock(int Index, int Start, int End, BlockEdges Edges);

/// <summary>
/// Builds EH-aware basic blocks from a decoded instruction stream, emitting
/// <see cref="ControlFlow.BlockEdges"/> so the dominance/dataflow kernels can run unchanged.
/// Block leaders, try/handler boundaries, and EH survivor edges follow ECMA-335 Partition I §12.4.
/// </summary>
public sealed record BlockGraph(
    ImmutableArray<InstructionBlock> Blocks,
    ImmutableArray<ExceptionRegionModel> Regions,
    bool IsComplete,
    string? IncompleteReason)
{
    public static BlockGraph Build(
        int ilLength,
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        ArgumentNullException.ThrowIfNull(exceptionRegions);
        ExceptionFlowTopology topology =
            ExceptionFlowTopology.Create(ilLength, instructions, exceptionRegions);
        return Build(ilLength, instructions, topology);
    }

    internal static BlockGraph Build(
        int ilLength,
        ImmutableArray<DecodedInstruction> instructions,
        ExceptionFlowTopology topology)
    {
        ImmutableArray<InstructionBlock> blocks =
            BuildBlocks(ilLength, instructions, topology);
        string? reason = ComputeIncompleteReason(
            blocks,
            topology.BlockGraphIncompleteReason);
        return new BlockGraph(blocks, topology.Models, reason is null, reason);
    }

    /// <summary>
    /// The index of the block whose <c>[Start, End)</c> span contains <paramref name="offset"/>,
    /// or -1 if out of range. Binary search over block starts (the blocks tile the IL), mirroring
    /// runtime's <c>FlowGraph.LookupIndex</c> — the offset→block half of the substrate's join key.
    /// </summary>
    public int BlockIndexAt(int offset)
    {
        int lo = 0;
        int hi = Blocks.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            var block = Blocks[mid];
            if (offset < block.Start)
                hi = mid - 1;
            else if (offset >= block.End)
                lo = mid + 1;
            else
                return mid;
        }
        return -1;
    }

    static ImmutableArray<InstructionBlock> BuildBlocks(
        int ilLength,
        ImmutableArray<DecodedInstruction> instructions,
        ExceptionFlowTopology topology)
    {
        ImmutableArray<ExceptionRegionModel> regions = topology.Models;
        var admittedTargets = new HashSet<int> { ilLength };
        for (int index = 0; index < instructions.Length; index++)
        {
            if (index == 0 || !instructions[index - 1].OpCode.IsPrefix())
                admittedTargets.Add(instructions[index].Offset);
        }
        var leaders = new SortedSet<int> { 0 };
        foreach (var instruction in instructions)
        {
            foreach (int target in instruction.BranchTargets)
            {
                if (target >= 0 && target < ilLength)
                {
                    if (!admittedTargets.Contains(target))
                        throw new BadImageFormatException($"Branch target IL_{target:X4} does not align with an instruction.");
                    leaders.Add(target);
                }
            }
            if ((instruction.Branches || instruction.Exits) && instruction.NextOffset < ilLength)
                leaders.Add(instruction.NextOffset);
        }
        foreach (var region in regions)
        {
            AddLeader(region.TryStart);
            AddLeader(region.TryEnd);
            AddLeader(region.HandlerStart);
            AddLeader(region.HandlerEnd);
            if (region.Kind == HandlerKind.Filter)
            {
                AddLeader(region.FilterStart);
                AddLeader(region.FilterEnd);
            }
        }
        foreach (var instruction in instructions)
        {
            if (regions.Any(region => region.ContainsAny(instruction.Offset)))
                AddLeader(instruction.Offset);
        }

        var starts = leaders.ToImmutableArray();
        var offsetToBlock = new Dictionary<int, int>(starts.Length);
        for (int i = 0; i < starts.Length; i++)
            offsetToBlock[starts[i]] = i;

        var successorsByBlock = new List<int>[starts.Length];
        var externalByBlock = new List<int>[starts.Length];
        var exitsByBlock = new bool[starts.Length];
        var leavesByBlock = new bool[starts.Length];
        var lastByBlock = new DecodedInstruction?[starts.Length];

        // Single O(n) pass: each instruction belongs to the block whose start it falls in; the last
        // instruction seen in a block is its terminator. Starts are sorted and instructions are in
        // offset order, so a forward block cursor suffices (vs an O(blocks*n) per-block rescan).
        int cursor = 0;
        foreach (var instruction in instructions)
        {
            while (cursor + 1 < starts.Length && instruction.Offset >= starts[cursor + 1])
                cursor++;
            lastByBlock[cursor] = instruction;
        }

        for (int i = 0; i < starts.Length; i++)
        {
            var successors = new List<int>();
            var external = new List<int>();
            if (lastByBlock[i] is { } terminator)
            {
                foreach (int target in terminator.BranchTargets)
                {
                    if (offsetToBlock.TryGetValue(target, out int targetBlock))
                        successors.Add(targetBlock);
                    else
                        external.Add(target);
                }
                if (terminator.FallsThrough && i + 1 < starts.Length)
                    successors.Add(i + 1);
            }

            successorsByBlock[i] = successors;
            externalByBlock[i] = external;
            exitsByBlock[i] = lastByBlock[i]?.Exits ?? false;
            leavesByBlock[i] = lastByBlock[i]?.LeavesRegion ?? false;
        }

        AddExceptionEdges(
            topology,
            instructions,
            starts,
            offsetToBlock,
            successorsByBlock,
            externalByBlock,
            lastByBlock);

        var blocks = ImmutableArray.CreateBuilder<InstructionBlock>(starts.Length);
        for (int i = 0; i < starts.Length; i++)
        {
            blocks.Add(new InstructionBlock(
                i,
                starts[i],
                i + 1 < starts.Length ? starts[i + 1] : ilLength,
                new BlockEdges(
                    successorsByBlock[i].Distinct().Order().ToArray(),
                    externalByBlock[i].Distinct().Order().ToArray(),
                    exitsByBlock[i],
                    leavesByBlock[i])));
        }
        return blocks.ToImmutable();

        void AddLeader(int offset)
        {
            if (offset < ilLength)
                leaders.Add(offset);
        }
    }

    static void AddExceptionEdges(
        ExceptionFlowTopology topology,
        ImmutableArray<DecodedInstruction> instructions,
        ImmutableArray<int> blockStarts,
        IReadOnlyDictionary<int, int> offsetToBlock,
        IReadOnlyList<List<int>> successorsByBlock,
        IReadOnlyList<List<int>> externalByBlock,
        IReadOnlyList<DecodedInstruction?> lastByBlock)
    {
        ImmutableArray<ExceptionRegionModel> regions = topology.Models;
        foreach (var region in regions)
        {
            int handlerBlock = offsetToBlock[region.HandlerStart];
            int exceptionEntryBlock = region.Kind == HandlerKind.Filter
                ? offsetToBlock[region.FilterStart]
                : handlerBlock;

            foreach (int block in BlocksInRange(region.TryStart, region.TryEnd))
            {
                if (lastByBlock[block]?.LeavesRegion == true)
                    continue;
                successorsByBlock[block].Add(exceptionEntryBlock);
            }

            if (region.Kind == HandlerKind.Filter)
            {
                foreach (int block in BlocksInRange(region.FilterStart, region.FilterEnd))
                {
                    successorsByBlock[block].Add(handlerBlock);
                    foreach (var sibling in regions)
                    {
                        if (sibling.Equals(region)
                            || sibling.TryStart != region.TryStart
                            || sibling.TryEnd != region.TryEnd)
                            continue;
                        successorsByBlock[block].Add(sibling.Kind == HandlerKind.Filter
                            ? offsetToBlock[sibling.FilterStart]
                            : offsetToBlock[sibling.HandlerStart]);
                    }
                }
            }
        }

        foreach (DecodedInstruction instruction in instructions)
        {
            if (!instruction.LeavesRegion)
                continue;
            foreach (int target in instruction.BranchTargets)
            {
                ImmutableArray<ExceptionRegionModel> cleanup =
                    topology.CleanupModels(instruction.Offset, target);
                if (cleanup.IsEmpty)
                    continue;

                RedirectLeaveToFirstFinally(instruction, target, cleanup[0]);
                for (int index = 0; index < cleanup.Length; index++)
                {
                    ExceptionRegionModel current = cleanup[index];
                    int nextTarget = index + 1 < cleanup.Length
                        ? cleanup[index + 1].HandlerStart
                        : target;
                    AddEndfinallyEdges(current, nextTarget);
                }
            }
        }

        void RedirectLeaveToFirstFinally(DecodedInstruction leave, int finalTarget, ExceptionRegionModel firstFinally)
        {
            if (!offsetToBlock.TryGetValue(leave.Offset, out int leaveBlock))
                return;

            if (offsetToBlock.TryGetValue(finalTarget, out int finalTargetBlock))
                successorsByBlock[leaveBlock].RemoveAll(block => block == finalTargetBlock);
            else
                externalByBlock[leaveBlock].RemoveAll(target => target == finalTarget);

            successorsByBlock[leaveBlock].Add(offsetToBlock[firstFinally.HandlerStart]);
        }

        void AddEndfinallyEdges(ExceptionRegionModel finallyRegion, int target)
        {
            foreach (int block in BlocksInRange(finallyRegion.HandlerStart, finallyRegion.HandlerEnd))
            {
                if (lastByBlock[block] is not { OpCode: ILOpCode.Endfinally })
                    continue;
                if (offsetToBlock.TryGetValue(target, out int targetBlock))
                    successorsByBlock[block].Add(targetBlock);
                else
                    externalByBlock[block].Add(target);
            }
        }

        IEnumerable<int> BlocksInRange(int start, int end)
        {
            for (int i = 0; i < blockStarts.Length; i++)
                if (blockStarts[i] >= start && blockStarts[i] < end)
                    yield return i;
        }
    }

    static string? ComputeIncompleteReason(
        ImmutableArray<InstructionBlock> blocks,
        string? topologyIncompleteReason)
    {
        if (blocks.Any(block => block.Edges.ExternalTargets.Count > 0))
            return "External control-flow targets are not modeled.";
        if (topologyIncompleteReason is not null)
            return topologyIncompleteReason;
        return null;
    }
}
