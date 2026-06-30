using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.ControlFlow;

namespace ILInspector.Analysis;

public sealed record LocalDefinition(int Id, int Slot, bool IsArgument, int Offset);

public sealed record LocalUse(
    int Slot,
    bool IsArgument,
    int Offset,
    bool Address,
    ImmutableArray<LocalDefinition> ReachingDefinitions);

public sealed record ReachingDefinitionsResult(
    ImmutableArray<LocalDefinition> Definitions,
    ImmutableArray<LocalUse> Uses,
    bool IsComplete = true,
    string? IncompleteReason = null)
{
    public ImmutableArray<LocalUse> UsesOf(LocalDefinition definition)
        => [.. Uses.Where(use => use.ReachingDefinitions.Any(d => d.Id == definition.Id))];
}

public static class ReachingDefinitions
{
    public static ReachingDefinitionsResult Analyze(byte[] il, int argumentSlotCount)
        => Analyze(il, argumentSlotCount, []);

    public static ReachingDefinitionsResult Analyze(byte[] il, int argumentSlotCount, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        ArgumentNullException.ThrowIfNull(exceptionRegions);
        return AnalyzeCore(il, argumentSlotCount, exceptionRegions);
    }

    public static ReachingDefinitionsResult Analyze(MethodBodyBlock body, int argumentSlotCount)
    {
        ArgumentNullException.ThrowIfNull(body);
        return AnalyzeCore(body.GetILBytes() ?? [], argumentSlotCount, body.ExceptionRegions);
    }

    /// <summary>
    /// Test/parity hook: the basic blocks (start, end, and outgoing <see cref="BlockEdges"/>) this
    /// decoder+block-builder produces, used by the <c>ILInspector.Instructions</c> substrate's
    /// directional differential to prove its block builder is a regression-free replacement —
    /// edges included, since reaching-definitions dataflow depends on them. Not a product surface.
    /// </summary>
    internal static ImmutableArray<(int Start, int End, BlockEdges Edges)> BlocksForParity(byte[] il, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        ArgumentNullException.ThrowIfNull(il);
        ArgumentNullException.ThrowIfNull(exceptionRegions);
        if (il.Length == 0)
            return [];
        var instructions = DecodeInstructions(il);
        var regionModels = BuildRegionModels(il.Length, instructions, exceptionRegions, out _);
        var blocks = BuildBlocks(il.Length, instructions, regionModels);
        return [.. blocks.Select(block => (block.Start, block.End, block.Edges))];
    }

    static ReachingDefinitionsResult AnalyzeCore(byte[] il, int argumentSlotCount, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        ArgumentNullException.ThrowIfNull(il);
        if (argumentSlotCount < 0)
            throw new ArgumentOutOfRangeException(nameof(argumentSlotCount));
        if (il.Length == 0)
            return new ReachingDefinitionsResult([], [], exceptionRegions.Count == 0,
                exceptionRegions.Count == 0 ? null : "Exception-handler regions reference empty IL.");

        var instructions = DecodeInstructions(il);
        var regionModels = BuildRegionModels(il.Length, instructions, exceptionRegions, out var regionIncompleteReasons);
        var blocks = BuildBlocks(il.Length, instructions, regionModels);
        var incompleteReason = IncompleteReason(blocks, instructions, regionModels, regionIncompleteReasons);
        var definitions = ImmutableArray.CreateBuilder<LocalDefinition>();
        var definitionsBySlot = new Dictionary<SlotKey, List<int>>();
        var definitionByOffset = new Dictionary<int, int>();

        for (int slot = 0; slot < argumentSlotCount; slot++)
            AddDefinition(new SlotKey(slot, IsArgument: true), Offset: -1);

        foreach (var instruction in instructions)
        {
            if (TryReadLocalSlot(il, instruction.OpCode, instruction.OperandOffset, out var access)
                && access.Store)
            {
                AddDefinition(new SlotKey(access.Slot, access.Argument), instruction.Offset);
                definitionByOffset[instruction.Offset] = definitions.Count - 1;
            }
        }

        var universe = new HashSet<int>(Enumerable.Range(0, definitions.Count));
        var transfers = blocks.Select(block => BuildTransfer(block, instructions, definitions, definitionsBySlot, definitionByOffset)).ToImmutableArray();
        var dataflow = ForwardDataflow.Solve(
            blocks.Select(block => block.Edges).ToImmutableArray(),
            transfers,
            entry: new HashSet<int>(Enumerable.Range(0, argumentSlotCount)),
            universe,
            DataflowMerge.Union,
            DataflowEntry.MergePredecessors);

        var uses = ImmutableArray.CreateBuilder<LocalUse>();
        for (int i = 0; i < blocks.Length; i++)
        {
            if (!dataflow.Blocks[i].Reachable)
                continue;
            var current = new HashSet<int>(dataflow.Blocks[i].In);
            foreach (var instruction in instructions)
            {
                if (instruction.Offset < blocks[i].Start || instruction.Offset >= blocks[i].End)
                    continue;
                if (!TryReadLocalSlot(il, instruction.OpCode, instruction.OperandOffset, out var access))
                    continue;

                var key = new SlotKey(access.Slot, access.Argument);
                if (access.Store)
                {
                    KillSlot(current, key, definitionsBySlot);
                    if (definitionByOffset.TryGetValue(instruction.Offset, out int id))
                        current.Add(id);
                    continue;
                }

                var reaching = definitionsBySlot.TryGetValue(key, out var ids)
                    ? ids.Where(current.Contains).Select(id => definitions[id]).OrderBy(def => def.Offset).ToImmutableArray()
                    : [];
                uses.Add(new LocalUse(access.Slot, access.Argument, instruction.Offset, access.Address, reaching));
            }
        }

        return new ReachingDefinitionsResult(
            definitions.ToImmutable(),
            uses.ToImmutable(),
            incompleteReason is null,
            incompleteReason);

        void AddDefinition(SlotKey key, int Offset)
        {
            int id = definitions.Count;
            definitions.Add(new LocalDefinition(id, key.Slot, key.IsArgument, Offset));
            if (!definitionsBySlot.TryGetValue(key, out var ids))
                definitionsBySlot[key] = ids = [];
            ids.Add(id);
        }
    }

    static string? IncompleteReason(
        IReadOnlyList<Block> blocks,
        IReadOnlyList<Instruction> instructions,
        IReadOnlyList<ExceptionRegionModel> regions,
        IReadOnlyList<string> regionIncompleteReasons)
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
        if (regionIncompleteReasons.Count > 0)
            return string.Join("; ", regionIncompleteReasons);
        return null;
    }

    static GenKillSet BuildTransfer(
        Block block,
        IReadOnlyList<Instruction> instructions,
        IReadOnlyList<LocalDefinition> definitions,
        IReadOnlyDictionary<SlotKey, List<int>> definitionsBySlot,
        IReadOnlyDictionary<int, int> definitionByOffset)
    {
        var kill = new HashSet<int>();
        var lastDefinitionBySlot = new Dictionary<SlotKey, int>();
        foreach (var instruction in instructions)
        {
            if (instruction.Offset < block.Start || instruction.Offset >= block.End)
                continue;
            if (!definitionByOffset.TryGetValue(instruction.Offset, out int id))
                continue;
            var definition = definitions[id];
            var key = new SlotKey(definition.Slot, definition.IsArgument);
            lastDefinitionBySlot[key] = id;
        }

        foreach (var key in lastDefinitionBySlot.Keys)
            if (definitionsBySlot.TryGetValue(key, out var ids))
                kill.UnionWith(ids);

        return new GenKillSet(new HashSet<int>(lastDefinitionBySlot.Values), kill);
    }

    static void KillSlot(HashSet<int> current, SlotKey key, IReadOnlyDictionary<SlotKey, List<int>> definitionsBySlot)
    {
        if (definitionsBySlot.TryGetValue(key, out var ids))
            current.ExceptWith(ids);
    }

    static ImmutableArray<Block> BuildBlocks(
        int ilLength,
        IReadOnlyList<Instruction> instructions,
        IReadOnlyList<ExceptionRegionModel> regions)
    {
        var instructionOffsets = instructions.Select(instruction => instruction.Offset).ToHashSet();
        var leaders = new SortedSet<int> { 0 };
        foreach (var instruction in instructions)
        {
            foreach (int target in instruction.Targets)
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
            if (region.Kind == ExceptionRegionKind.Filter)
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
        var lastByBlock = new Instruction?[starts.Length];

        for (int i = 0; i < starts.Length; i++)
        {
            int start = starts[i];
            int end = i + 1 < starts.Length ? starts[i + 1] : ilLength;
            var last = instructions.LastOrDefault(instruction => instruction.Offset >= start && instruction.Offset < end);
            var successors = new List<int>();
            var external = new List<int>();
            bool exits = last.Exits;
            bool leaves = last.LeavesRegion;
            if (last.Offset >= 0)
            {
                foreach (int target in last.Targets)
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
            exitsByBlock[i] = exits;
            leavesByBlock[i] = leaves;
            lastByBlock[i] = last.Offset >= 0 ? last : null;
        }

        AddExceptionEdges(regions, instructions, starts, offsetToBlock, successorsByBlock, externalByBlock, lastByBlock);

        var blocks = ImmutableArray.CreateBuilder<Block>(starts.Length);
        for (int i = 0; i < starts.Length; i++)
        {
            blocks.Add(new Block(
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
        IReadOnlyList<Instruction> instructions,
        IReadOnlyCollection<ExceptionRegion> regions,
        out ImmutableArray<string> incompleteReasons)
    {
        var instructionOffsets = instructions.Select(instruction => instruction.Offset).ToHashSet();
        var models = ImmutableArray.CreateBuilder<ExceptionRegionModel>();
        var reasons = ImmutableArray.CreateBuilder<string>();
        foreach (var region in regions)
        {
            if (!TryRange("try", region.TryOffset, region.TryLength, out int tryEnd)
                || !TryRange("handler", region.HandlerOffset, region.HandlerLength, out int handlerEnd))
                continue;

            int filterStart = -1;
            int filterEnd = -1;
            switch (region.Kind)
            {
                case ExceptionRegionKind.Catch:
                case ExceptionRegionKind.Finally:
                case ExceptionRegionKind.Fault:
                    break;
                case ExceptionRegionKind.Filter:
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
                region.Kind,
                region.TryOffset,
                tryEnd,
                region.HandlerOffset,
                handlerEnd,
                filterStart,
                filterEnd));
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

        bool IsBoundary(int offset)
            => offset == ilLength || instructionOffsets.Contains(offset);
    }

    static void AddExceptionEdges(
        IReadOnlyList<ExceptionRegionModel> regions,
        IReadOnlyList<Instruction> instructions,
        IReadOnlyList<int> blockStarts,
        IReadOnlyDictionary<int, int> offsetToBlock,
        IReadOnlyList<List<int>> successorsByBlock,
        IReadOnlyList<List<int>> externalByBlock,
        IReadOnlyList<Instruction?> lastByBlock)
    {
        foreach (var region in regions)
        {
            int handlerBlock = offsetToBlock[region.HandlerStart];
            int exceptionEntryBlock = region.Kind == ExceptionRegionKind.Filter
                ? offsetToBlock[region.FilterStart]
                : handlerBlock;

            foreach (int block in BlocksInRange(region.TryStart, region.TryEnd))
                successorsByBlock[block].Add(exceptionEntryBlock);

            if (region.Kind == ExceptionRegionKind.Filter)
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
                        successorsByBlock[block].Add(sibling.Kind == ExceptionRegionKind.Filter
                            ? offsetToBlock[sibling.FilterStart]
                            : offsetToBlock[sibling.HandlerStart]);
                    }
                }
            }

            if (region.Kind == ExceptionRegionKind.Finally)
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
            for (int i = 0; i < blockStarts.Count; i++)
                if (blockStarts[i] >= start && blockStarts[i] < end)
                    yield return i;
        }
    }

    static IEnumerable<int> LeaveTargetsLeaving(ExceptionRegionModel region, IReadOnlyList<Instruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (!instruction.LeavesRegion || !region.ContainsTry(instruction.Offset))
                continue;
            foreach (int target in instruction.Targets)
                if (!region.ContainsTry(target))
                    yield return target;
        }
    }

    static ImmutableArray<Instruction> DecodeInstructions(byte[] il)
    {
        var instructions = ImmutableArray.CreateBuilder<Instruction>();
        int position = 0;
        while (position < il.Length)
        {
            int offset = position;
            var opcode = ReadOpcode(il, ref position);
            int operandOffset = position;
            var targets = ImmutableArray<int>.Empty;
            bool branches = false;
            bool exits = false;
            bool fallsThrough = true;
            bool leavesRegion = false;

            if (TryReadBranchTargets(opcode, il, ref position, offset, out targets, out bool unconditional))
            {
                branches = true;
                fallsThrough = !unconditional || opcode == ILOpCode.Switch;
                leavesRegion = opcode is ILOpCode.Leave or ILOpCode.Leave_s;
            }
            else
            {
                exits = opcode is ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Jmp
                    or ILOpCode.Endfinally or ILOpCode.Endfilter;
                fallsThrough = !exits;
                SkipOperand(il, opcode, ref position, offset);
            }

            instructions.Add(new Instruction(offset, opcode, operandOffset, position, targets, branches, exits, fallsThrough, leavesRegion));
        }

        return instructions.ToImmutable();
    }

    static bool TryReadBranchTargets(
        ILOpCode opcode,
        byte[] il,
        ref int position,
        int offset,
        out ImmutableArray<int> targets,
        out bool unconditional)
    {
        targets = [];
        unconditional = false;
        switch (opcode)
        {
            case ILOpCode.Br_s:
            case ILOpCode.Leave_s:
                targets = [offset + 2 + (sbyte)ReadByte(il, ref position, offset)];
                unconditional = true;
                return true;
            case ILOpCode.Br:
            case ILOpCode.Leave:
                targets = [offset + 5 + ReadInt32(il, ref position, offset)];
                unconditional = true;
                return true;
            case ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s
                or ILOpCode.Bge_s or ILOpCode.Bgt_s or ILOpCode.Ble_s or ILOpCode.Blt_s
                or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s
                or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s:
                targets = [offset + 2 + (sbyte)ReadByte(il, ref position, offset)];
                return true;
            case ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq
                or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt
                or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
                or ILOpCode.Ble_un or ILOpCode.Blt_un:
                targets = [offset + 5 + ReadInt32(il, ref position, offset)];
                return true;
            case ILOpCode.Switch:
            {
                int count = ReadInt32(il, ref position, offset);
                if (count < 0)
                    throw new BadImageFormatException($"Malformed switch at IL_{offset:X4}");
                long baseOffset = (long)position + (long)count * 4;
                if (baseOffset > il.Length)
                    throw new BadImageFormatException($"Malformed switch at IL_{offset:X4}");
                var builder = ImmutableArray.CreateBuilder<int>(count);
                for (int i = 0; i < count; i++)
                    builder.Add((int)baseOffset + ReadInt32(il, ref position, offset));
                targets = builder.ToImmutable();
                return true;
            }
            default:
                return false;
        }
    }

    static bool TryReadLocalSlot(byte[] il, ILOpCode opcode, int operandOffset, out LocalAccess access)
    {
        access = default;
        switch (opcode)
        {
            case ILOpCode.Ldloc_0: access = new(0, Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloc_1: access = new(1, Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloc_2: access = new(2, Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloc_3: access = new(3, Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloc_s: access = new(ReadByteAt(il, operandOffset), Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloc: access = new(ReadUInt16At(il, operandOffset), Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloca_s: access = new(ReadByteAt(il, operandOffset), Argument: false, Store: false, Address: true); return true;
            case ILOpCode.Ldloca: access = new(ReadUInt16At(il, operandOffset), Argument: false, Store: false, Address: true); return true;
            case ILOpCode.Stloc_0: access = new(0, Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Stloc_1: access = new(1, Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Stloc_2: access = new(2, Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Stloc_3: access = new(3, Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Stloc_s: access = new(ReadByteAt(il, operandOffset), Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Stloc: access = new(ReadUInt16At(il, operandOffset), Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Ldarg_0: access = new(0, Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarg_1: access = new(1, Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarg_2: access = new(2, Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarg_3: access = new(3, Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarg_s: access = new(ReadByteAt(il, operandOffset), Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarg: access = new(ReadUInt16At(il, operandOffset), Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarga_s: access = new(ReadByteAt(il, operandOffset), Argument: true, Store: false, Address: true); return true;
            case ILOpCode.Ldarga: access = new(ReadUInt16At(il, operandOffset), Argument: true, Store: false, Address: true); return true;
            case ILOpCode.Starg_s: access = new(ReadByteAt(il, operandOffset), Argument: true, Store: true, Address: false); return true;
            case ILOpCode.Starg: access = new(ReadUInt16At(il, operandOffset), Argument: true, Store: true, Address: false); return true;
            default:
                return false;
        }
    }

    static ILOpCode ReadOpcode(byte[] il, ref int position)
    {
        byte first = ReadByte(il, ref position, position);
        if (first != 0xFE)
            return (ILOpCode)first;
        byte second = ReadByte(il, ref position, position - 1);
        return (ILOpCode)(0xFE00 | second);
    }

    static void SkipOperand(byte[] il, ILOpCode opcode, ref int position, int offset)
    {
        switch (opcode)
        {
            case ILOpCode.Switch:
            {
                int count = ReadInt32(il, ref position, offset);
                if (count < 0)
                    throw new BadImageFormatException($"Malformed switch at IL_{offset:X4}");
                long tableBytes = (long)count * 4;
                if (tableBytes > int.MaxValue)
                    throw new BadImageFormatException($"Malformed switch at IL_{offset:X4}");
                Advance(il, ref position, (int)tableBytes, offset);
                break;
            }
            case ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s
                or ILOpCode.Bge_s or ILOpCode.Bgt_s or ILOpCode.Ble_s or ILOpCode.Blt_s
                or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s
                or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or ILOpCode.Leave_s
                or ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s
                or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
                or ILOpCode.Ldc_i4_s or ILOpCode.Unaligned:
                Advance(il, ref position, 1, offset);
                break;
            case ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg
                or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc:
                Advance(il, ref position, 2, offset);
                break;
            case ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq
                or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt
                or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
                or ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave
                or ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4
                or ILOpCode.Jmp or ILOpCode.Ldstr
                or ILOpCode.Call or ILOpCode.Calli or ILOpCode.Callvirt or ILOpCode.Newobj
                or ILOpCode.Ldftn or ILOpCode.Ldvirtftn
                or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld
                or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
                or ILOpCode.Cpobj or ILOpCode.Ldobj or ILOpCode.Castclass
                or ILOpCode.Isinst or ILOpCode.Unbox or ILOpCode.Stobj
                or ILOpCode.Box or ILOpCode.Newarr or ILOpCode.Ldelema
                or ILOpCode.Ldelem or ILOpCode.Stelem or ILOpCode.Unbox_any
                or ILOpCode.Refanyval or ILOpCode.Mkrefany or ILOpCode.Initobj
                or ILOpCode.Constrained or ILOpCode.Sizeof or ILOpCode.Ldtoken:
                Advance(il, ref position, 4, offset);
                break;
            case ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8:
                Advance(il, ref position, 8, offset);
                break;
        }
    }

    static byte ReadByte(byte[] il, ref int position, int offset)
    {
        if ((uint)position >= (uint)il.Length)
            throw new BadImageFormatException($"Malformed IL at IL_{offset:X4}");
        return il[position++];
    }

    static byte ReadByteAt(byte[] il, int offset)
    {
        if ((uint)offset >= (uint)il.Length)
            throw new BadImageFormatException($"Malformed IL at IL_{offset:X4}");
        return il[offset];
    }

    static int ReadUInt16At(byte[] il, int offset)
    {
        if (offset < 0 || offset + 2 > il.Length)
            throw new BadImageFormatException($"Malformed IL operand at IL_{offset:X4}");
        return BinaryPrimitives.ReadUInt16LittleEndian(il.AsSpan(offset));
    }

    static int ReadInt32(byte[] il, ref int position, int offset)
    {
        if (position < 0 || position + 4 > il.Length)
            throw new BadImageFormatException($"Malformed IL operand at IL_{offset:X4}");
        int value = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position));
        position += 4;
        return value;
    }

    static void Advance(byte[] il, ref int position, int bytes, int offset)
    {
        long targetPosition = (long)position + bytes;
        if (targetPosition < position || targetPosition > il.Length)
            throw new BadImageFormatException($"Malformed IL operand at IL_{offset:X4}");
        position = (int)targetPosition;
    }

    readonly record struct SlotKey(int Slot, bool IsArgument);

    readonly record struct LocalAccess(int Slot, bool Argument, bool Store, bool Address);

    readonly record struct Instruction(
        int Offset,
        ILOpCode OpCode,
        int OperandOffset,
        int NextOffset,
        ImmutableArray<int> Targets,
        bool Branches,
        bool Exits,
        bool FallsThrough,
        bool LeavesRegion);

    readonly record struct Block(int Start, int End, BlockEdges Edges);

    readonly record struct ExceptionRegionModel(
        ExceptionRegionKind Kind,
        int TryStart,
        int TryEnd,
        int HandlerStart,
        int HandlerEnd,
        int FilterStart,
        int FilterEnd)
    {
        public bool ContainsTry(int offset) => offset >= TryStart && offset < TryEnd;

        public bool ContainsHandler(int offset) => offset >= HandlerStart && offset < HandlerEnd;

        public bool ContainsFilter(int offset)
            => Kind == ExceptionRegionKind.Filter && offset >= FilterStart && offset < FilterEnd;

        public bool ContainsAny(int offset)
            => ContainsTry(offset) || ContainsHandler(offset) || ContainsFilter(offset);
    }
}
