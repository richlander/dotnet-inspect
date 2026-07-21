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

    public SlotDefUseGraph ToDefUseGraph()
        => SlotDefUseGraph.Create(this);
}

public readonly record struct SlotIdentity(int Slot, bool IsArgument);

public sealed record SlotDefUseWeb(
    int Id,
    SlotIdentity Slot,
    ImmutableArray<LocalDefinition> Definitions,
    ImmutableArray<LocalUse> Uses,
    int StartOffset,
    int EndOffset,
    bool AddressTaken,
    bool HasMergedUse)
{
    public bool IsArgument => Slot.IsArgument;
    public bool HasMultipleDefinitions => Definitions.Length > 1;
    public bool HasSingleDefinition => Definitions.Length == 1;
    public bool HasSingleUse => Uses.Length == 1;
    public bool HasSingleDefinitionSingleUseWithoutAddress
        => HasSingleDefinition && HasSingleUse && !AddressTaken;
}

public sealed record SlotDefUseGraph(
    ImmutableArray<SlotDefUseWeb> Webs,
    bool IsComplete = true,
    string? IncompleteReason = null)
{
    public ImmutableArray<SlotDefUseWeb> WebsFor(SlotIdentity slot)
        => [.. Webs.Where(web => web.Slot == slot)];

    public ImmutableArray<SlotDefUseWeb> SingleDefinitionSingleUseWebs(bool includeArguments = false)
        => [.. Webs.Where(web =>
            web.HasSingleDefinitionSingleUseWithoutAddress
            && (includeArguments || !web.IsArgument))];

    public static SlotDefUseGraph Create(ReachingDefinitionsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        int definitionCount = result.Definitions.Length;
        int useCount = result.Uses.Length;
        int nodeCount = definitionCount + useCount;
        if (nodeCount == 0)
            return new SlotDefUseGraph([], result.IsComplete, result.IncompleteReason);

        var parents = Enumerable.Range(0, nodeCount).ToArray();
        var definitionIndexById = result.Definitions
            .Select((definition, index) => (definition.Id, Index: index))
            .ToDictionary(pair => pair.Id, pair => pair.Index);

        for (int useIndex = 0; useIndex < result.Uses.Length; useIndex++)
        {
            int useNode = definitionCount + useIndex;
            foreach (var definition in result.Uses[useIndex].ReachingDefinitions)
                if (definitionIndexById.TryGetValue(definition.Id, out int definitionIndex))
                    Union(useNode, definitionIndex);
        }

        var groups = new Dictionary<int, List<int>>();
        for (int node = 0; node < nodeCount; node++)
        {
            int root = Find(node);
            if (!groups.TryGetValue(root, out var nodes))
                groups[root] = nodes = [];
            nodes.Add(node);
        }

        var candidates = groups.Values
            .Select(nodes =>
            {
                var definitions = nodes
                    .Where(node => node < definitionCount)
                    .Select(node => result.Definitions[node])
                    .OrderBy(definition => definition.Offset)
                    .ThenBy(definition => definition.Id)
                    .ToImmutableArray();
                var uses = nodes
                    .Where(node => node >= definitionCount)
                    .Select(node => result.Uses[node - definitionCount])
                    .OrderBy(use => use.Offset)
                    .ThenBy(use => use.Slot)
                    .ToImmutableArray();
                var slot = definitions.Length > 0
                    ? new SlotIdentity(definitions[0].Slot, definitions[0].IsArgument)
                    : new SlotIdentity(uses[0].Slot, uses[0].IsArgument);
                int startOffset = Math.Min(
                    definitions.IsEmpty ? int.MaxValue : definitions[0].Offset,
                    uses.IsEmpty ? int.MaxValue : uses[0].Offset);
                int endOffset = Math.Max(
                    definitions.IsEmpty ? int.MinValue : definitions[^1].Offset,
                    uses.IsEmpty ? int.MinValue : uses[^1].Offset);
                return new
                {
                    Slot = slot,
                    Definitions = definitions,
                    Uses = uses,
                    StartOffset = startOffset,
                    EndOffset = endOffset,
                    AddressTaken = uses.Any(use => use.Address),
                    HasMergedUse = uses.Any(use => use.ReachingDefinitions.Length > 1),
                };
            })
            .OrderBy(candidate => candidate.Slot.IsArgument ? 0 : 1)
            .ThenBy(candidate => candidate.Slot.Slot)
            .ThenBy(candidate => candidate.StartOffset)
            .ThenBy(candidate => candidate.EndOffset)
            .ToArray();

        var webs = ImmutableArray.CreateBuilder<SlotDefUseWeb>(candidates.Length);
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            webs.Add(new SlotDefUseWeb(
                i,
                candidate.Slot,
                candidate.Definitions,
                candidate.Uses,
                candidate.StartOffset,
                candidate.EndOffset,
                candidate.AddressTaken,
                candidate.HasMergedUse));
        }

        return new SlotDefUseGraph(webs.MoveToImmutable(), result.IsComplete, result.IncompleteReason);

        int Find(int node)
        {
            while (parents[node] != node)
            {
                parents[node] = parents[parents[node]];
                node = parents[node];
            }

            return node;
        }

        void Union(int left, int right)
        {
            int leftRoot = Find(left);
            int rightRoot = Find(right);
            if (leftRoot != rightRoot)
                parents[rightRoot] = leftRoot;
        }
    }
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

        ImmutableArray<DecodedInstruction> instructions;
        BlockGraph blockGraph;
        try
        {
            instructions = InstructionDecoder.Decode(il);
            blockGraph = BlockGraph.Build(il.Length, instructions, exceptionRegions);
        }
        catch (InvalidProgramException ex)
        {
            // Preserve RD's malformed-IL contract. The substrate's runtime-ported ILReader throws
            // InvalidProgramException on a truncated read (opcode / branch operand / switch count),
            // but RD — and its callers' recoverable-failure filters — expect BadImageFormatException.
            throw new BadImageFormatException(ex.Message, ex);
        }

        var blocks = blockGraph.Blocks;
        var incompleteReason = blockGraph.IncompleteReason;
        var definitions = ImmutableArray.CreateBuilder<LocalDefinition>();
        var definitionsBySlot = new Dictionary<SlotKey, List<int>>();
        var definitionByOffset = new Dictionary<int, int>();

        for (int slot = 0; slot < argumentSlotCount; slot++)
            AddDefinition(new SlotKey(slot, IsArgument: true), Offset: -1);

        foreach (var instruction in instructions)
        {
            if (TryReadLocalSlot(instruction.OpCode, instruction.OperandValue, out var access)
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
                if (!TryReadLocalSlot(instruction.OpCode, instruction.OperandValue, out var access))
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

    // Slot/argument index operands are already decoded onto DecodedInstruction.OperandValue
    // by the shared InstructionDecoder substrate; re-reading raw IL bytes here would duplicate
    // that decode instead of reusing it.
    static bool TryReadLocalSlot(ILOpCode opcode, long operandValue, out LocalAccess access)
    {
        access = default;
        switch (opcode)
        {
            case ILOpCode.Ldloc_0: access = new(0, Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloc_1: access = new(1, Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloc_2: access = new(2, Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloc_3: access = new(3, Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloc_s: access = new((int)operandValue, Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloc: access = new((int)operandValue, Argument: false, Store: false, Address: false); return true;
            case ILOpCode.Ldloca_s: access = new((int)operandValue, Argument: false, Store: false, Address: true); return true;
            case ILOpCode.Ldloca: access = new((int)operandValue, Argument: false, Store: false, Address: true); return true;
            case ILOpCode.Stloc_0: access = new(0, Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Stloc_1: access = new(1, Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Stloc_2: access = new(2, Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Stloc_3: access = new(3, Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Stloc_s: access = new((int)operandValue, Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Stloc: access = new((int)operandValue, Argument: false, Store: true, Address: false); return true;
            case ILOpCode.Ldarg_0: access = new(0, Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarg_1: access = new(1, Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarg_2: access = new(2, Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarg_3: access = new(3, Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarg_s: access = new((int)operandValue, Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarg: access = new((int)operandValue, Argument: true, Store: false, Address: false); return true;
            case ILOpCode.Ldarga_s: access = new((int)operandValue, Argument: true, Store: false, Address: true); return true;
            case ILOpCode.Ldarga: access = new((int)operandValue, Argument: true, Store: false, Address: true); return true;
            case ILOpCode.Starg_s: access = new((int)operandValue, Argument: true, Store: true, Address: false); return true;
            case ILOpCode.Starg: access = new((int)operandValue, Argument: true, Store: true, Address: false); return true;
            default:
                return false;
        }
    }

    readonly record struct SlotKey(int Slot, bool IsArgument);

    readonly record struct LocalAccess(int Slot, bool Argument, bool Store, bool Address);

}
