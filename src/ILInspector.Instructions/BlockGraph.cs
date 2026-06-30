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
        var regions = BuildRegionModels(ilLength, instructions, exceptionRegions, out var regionReasons);
        var blocks = BuildBlocks(ilLength, instructions, regions);
        var reason = ComputeIncompleteReason(blocks, instructions, regions, regionReasons);
        return new BlockGraph(blocks, regions, reason is null, reason);
    }

    static ImmutableArray<InstructionBlock> BuildBlocks(
        int ilLength,
        ImmutableArray<DecodedInstruction> instructions,
        ImmutableArray<ExceptionRegionModel> regions)
    {
        var instructionOffsets = instructions.Select(i => i.Offset).ToHashSet();
        var leaders = new SortedSet<int> { 0 };
        foreach (var instruction in instructions)
        {
            foreach (int target in instruction.BranchTargets)
            {
                if (target >= 0 && target < ilLength)
                {
                    if (!instructionOffsets.Contains(target))
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

        for (int i = 0; i < starts.Length; i++)
        {
            int start = starts[i];
            int end = i + 1 < starts.Length ? starts[i + 1] : ilLength;
            var last = instructions.LastOrDefault(instruction => instruction.Offset >= start && instruction.Offset < end);
            var successors = new List<int>();
            var external = new List<int>();
            if (last is not null)
            {
                foreach (int target in last.BranchTargets)
                {
                    if (offsetToBlock.TryGetValue(target, out int targetBlock))
                        successors.Add(targetBlock);
                    else
                        external.Add(target);
                }
                if (last.FallsThrough && i + 1 < starts.Length)
                    successors.Add(i + 1);
            }

            successorsByBlock[i] = successors;
            externalByBlock[i] = external;
            exitsByBlock[i] = last?.Exits ?? false;
            leavesByBlock[i] = last?.LeavesRegion ?? false;
            lastByBlock[i] = last;
        }

        AddExceptionEdges(regions, instructions, starts, offsetToBlock, successorsByBlock, externalByBlock, lastByBlock);

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

    static ImmutableArray<ExceptionRegionModel> BuildRegionModels(
        int ilLength,
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyCollection<ExceptionRegion> regions,
        out ImmutableArray<string> incompleteReasons)
    {
        var instructionOffsets = instructions.Select(i => i.Offset).ToHashSet();
        var models = ImmutableArray.CreateBuilder<ExceptionRegionModel>();
        var reasons = ImmutableArray.CreateBuilder<string>();
        foreach (var region in regions)
        {
            if (!TryRange("try", region.TryOffset, region.TryLength, out int tryEnd)
                || !TryRange("handler", region.HandlerOffset, region.HandlerLength, out int handlerEnd))
                continue;

            int filterStart = -1;
            int filterEnd = -1;
            HandlerKind kind;
            switch (region.Kind)
            {
                case ExceptionRegionKind.Catch: kind = HandlerKind.Catch; break;
                case ExceptionRegionKind.Finally: kind = HandlerKind.Finally; break;
                case ExceptionRegionKind.Fault: kind = HandlerKind.Fault; break;
                case ExceptionRegionKind.Filter:
                    kind = HandlerKind.Filter;
                    filterStart = region.FilterOffset;
                    filterEnd = region.HandlerOffset;
                    if (!IsBoundary(filterStart) || filterStart >= filterEnd)
                    {
                        reasons.Add("Filter exception-handler region has unsupported boundaries.");
                        continue;
                    }
                    break;
                default:
                    reasons.Add($"Unsupported exception-handler kind {region.Kind}.");
                    continue;
            }

            models.Add(new ExceptionRegionModel(
                kind, region.TryOffset, tryEnd, region.HandlerOffset, handlerEnd, filterStart, filterEnd));
        }

        incompleteReasons = reasons.ToImmutable();
        return models.ToImmutable();

        bool TryRange(string name, int start, int length, out int end)
        {
            end = 0;
            long computedEnd = (long)start + length;
            if (start < 0 || length <= 0 || computedEnd > ilLength)
            {
                reasons.Add($"Exception {name} region has unsupported boundaries.");
                return false;
            }
            end = (int)computedEnd;
            if (!IsBoundary(start) || !IsBoundary(end))
            {
                reasons.Add($"Exception {name} region does not align with instruction boundaries.");
                return false;
            }
            return true;
        }

        bool IsBoundary(int offset) => offset == ilLength || instructionOffsets.Contains(offset);
    }

    static void AddExceptionEdges(
        ImmutableArray<ExceptionRegionModel> regions,
        ImmutableArray<DecodedInstruction> instructions,
        ImmutableArray<int> blockStarts,
        IReadOnlyDictionary<int, int> offsetToBlock,
        IReadOnlyList<List<int>> successorsByBlock,
        IReadOnlyList<List<int>> externalByBlock,
        IReadOnlyList<DecodedInstruction?> lastByBlock)
    {
        foreach (var region in regions)
        {
            int handlerBlock = offsetToBlock[region.HandlerStart];
            int exceptionEntryBlock = region.Kind == HandlerKind.Filter
                ? offsetToBlock[region.FilterStart]
                : handlerBlock;

            foreach (int block in BlocksInRange(region.TryStart, region.TryEnd))
                successorsByBlock[block].Add(exceptionEntryBlock);

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

            if (region.Kind == HandlerKind.Finally)
            {
                var leaveTargets = LeaveTargetsLeaving(region, instructions).ToArray();
                if (leaveTargets.Length == 0)
                    continue;
                foreach (int block in BlocksInRange(region.HandlerStart, region.HandlerEnd))
                {
                    if (lastByBlock[block] is not { OpCode: ILOpCode.Endfinally })
                        continue;
                    foreach (int target in leaveTargets)
                    {
                        if (offsetToBlock.TryGetValue(target, out int targetBlock))
                            successorsByBlock[block].Add(targetBlock);
                        else
                            externalByBlock[block].Add(target);
                    }
                }
            }
        }

        IEnumerable<int> BlocksInRange(int start, int end)
        {
            for (int i = 0; i < blockStarts.Length; i++)
                if (blockStarts[i] >= start && blockStarts[i] < end)
                    yield return i;
        }
    }

    static IEnumerable<int> LeaveTargetsLeaving(ExceptionRegionModel region, ImmutableArray<DecodedInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (!instruction.LeavesRegion || !region.ContainsTry(instruction.Offset))
                continue;
            foreach (int target in instruction.BranchTargets)
                if (!region.ContainsTry(target))
                    yield return target;
        }
    }

    static string? ComputeIncompleteReason(
        ImmutableArray<InstructionBlock> blocks,
        ImmutableArray<DecodedInstruction> instructions,
        ImmutableArray<ExceptionRegionModel> regions,
        ImmutableArray<string> regionIncompleteReasons)
    {
        if (blocks.Any(block => block.Edges.LeavesRegion))
        {
            foreach (var instruction in instructions)
            {
                if (!instruction.LeavesRegion)
                    continue;
                if (regions.Any(region => region.ContainsAny(instruction.Offset)))
                    continue;
                return "Region-leaving control-flow edges are not modeled.";
            }
        }
        if (blocks.Any(block => block.Edges.ExternalTargets.Count > 0))
            return "External control-flow targets are not modeled.";
        if (regionIncompleteReasons.Length > 0)
            return string.Join("; ", regionIncompleteReasons);
        return null;
    }
}
