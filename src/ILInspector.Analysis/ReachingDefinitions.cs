using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.ControlFlow;
using ILInspector.Instructions;

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
    static ReachingDefinitionsResult AnalyzeCore(byte[] il, int argumentSlotCount, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        ArgumentNullException.ThrowIfNull(il);
        if (argumentSlotCount < 0)
            throw new ArgumentOutOfRangeException(nameof(argumentSlotCount));
        if (il.Length == 0)
            return new ReachingDefinitionsResult([], [], exceptionRegions.Count == 0,
                exceptionRegions.Count == 0 ? null : "Exception-handler regions reference empty IL.");

        var instructions = InstructionDecoder.Decode(il);
        var blockGraph = BlockGraph.Build(il.Length, instructions, exceptionRegions);
        var blocks = blockGraph.Blocks;
        var incompleteReason = blockGraph.IncompleteReason;
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

    static GenKillSet BuildTransfer(
        InstructionBlock block,
        ImmutableArray<DecodedInstruction> instructions,
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

    readonly record struct SlotKey(int Slot, bool IsArgument);

    readonly record struct LocalAccess(int Slot, bool Argument, bool Store, bool Address);

}
